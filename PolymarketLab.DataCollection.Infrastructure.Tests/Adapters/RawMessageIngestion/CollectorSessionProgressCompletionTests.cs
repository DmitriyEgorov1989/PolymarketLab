using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.RawMessageIngestion;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.RawMessageIngestion;

public sealed class CollectorSessionProgressCompletionTests
{
    private static readonly DateTimeOffset ReceivedAt =
        DateTimeOffset.Parse("2026-08-31T11:00:00Z");

    [Fact]
    public async Task CompleteAsync_WhenBatchIsInFlight_ShouldWaitForFinalEnqueuedBoundary()
    {
        await using var fixture = CreateFixture(TimeSpan.FromSeconds(5));
        var sessionId = fixture.SessionId;
        fixture.Telemetry.RecordReceivedComplete(sessionId, ReceivedAt);
        fixture.Telemetry.RecordReceivedComplete(sessionId, ReceivedAt);
        fixture.Telemetry.RecordReceivedComplete(sessionId, ReceivedAt);
        fixture.Telemetry.RecordEnqueued(sessionId);
        fixture.Telemetry.RecordEnqueued(sessionId);
        fixture.Telemetry.RecordEnqueued(sessionId);
        fixture.Telemetry.RecordPersisted(sessionId, 2);

        var completion = fixture.Completion.CompleteAsync(sessionId, CancellationToken.None);
        completion.IsCompleted.Should().BeFalse();
        fixture.Repository.Checkpoints.Should().BeEmpty();

        fixture.Telemetry.RecordPersisted(sessionId, 1);
        var result = await completion;

        result.IsSuccess.Should().BeTrue();
        fixture.Repository.Checkpoints.Should().ContainSingle();
        var checkpoint = fixture.Repository.Checkpoints[0];
        checkpoint.SessionId.Should().Be(sessionId);
        checkpoint.MessagesReceived.Should().Be(3);
        checkpoint.MessagesEnqueued.Should().Be(3);
        checkpoint.MessagesPersisted.Should().Be(3);
    }

    [Fact]
    public async Task CompleteAsync_WhenDrainTimesOut_ShouldFailWithoutCheckpoint()
    {
        await using var fixture = CreateFixture(TimeSpan.FromMilliseconds(50));
        var sessionId = fixture.SessionId;
        fixture.Telemetry.RecordReceivedComplete(sessionId, ReceivedAt);
        fixture.Telemetry.RecordEnqueued(sessionId);

        var result = await fixture.Completion.CompleteAsync(
            sessionId,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.progress.persistence_timeout");
        fixture.Repository.Checkpoints.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_WhenCheckpointFails_ShouldReturnSafeErrorWithoutDetails()
    {
        await using var fixture = CreateFixture(TimeSpan.FromSeconds(5));
        var sessionId = fixture.SessionId;
        fixture.Telemetry.RecordReceivedComplete(sessionId, ReceivedAt);
        fixture.Telemetry.RecordEnqueued(sessionId);
        fixture.Telemetry.RecordPersisted(sessionId, 1);
        fixture.Repository.Handler = _ => throw new InvalidOperationException(
            "PostgreSQL rejected the checkpoint.");

        var result = await fixture.Completion.CompleteAsync(
            sessionId,
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("collector.progress.persistence_failed");
        result.Error.Message.Should().NotContain("PostgreSQL rejected");
        var entry = fixture.Logger.Entries.Should().ContainSingle(candidate =>
            candidate.Level == LogLevel.Error).Subject;
        entry.Properties.Should().ContainKey("SessionId");
        entry.Properties["SessionId"].Should().Be(sessionId.Value);
    }

    private static Fixture CreateFixture(TimeSpan shutdownTimeout)
    {
        var options = new RawMessageIngestionOptions
        {
            ShutdownTimeout = shutdownTimeout
        };
        var telemetry = new RawMarketMessageTelemetry();
        var repository = new RecordingProgressRepository();
        var logger = new CapturingLogger<CollectorSessionProgressCompletion>();
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddScoped<ICollectorSessionProgressRepository>(
            provider => provider.GetRequiredService<RecordingProgressRepository>());
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var completion = new CollectorSessionProgressCompletion(
            telemetry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            logger);

        return new Fixture(telemetry, repository, completion, logger, provider);
    }

    private sealed class RecordingProgressRepository : ICollectorSessionProgressRepository
    {
        public List<CollectorSessionProgressCheckpoint> Checkpoints { get; } = [];
        public Action<CollectorSessionProgressCheckpoint>? Handler { get; set; }

        public Task<CollectorSessionProgress> GetAsync(
            CollectorSessionId sessionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CheckpointAsync(
            CollectorSessionProgressCheckpoint checkpoint,
            CancellationToken cancellationToken)
        {
            Handler?.Invoke(checkpoint);
            Checkpoints.Add(checkpoint);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.Where(value => value.Key != "{OriginalFormat}").ToDictionary()
                : [];
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                properties,
                exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Properties,
        Exception? Exception);

    private sealed class Fixture(
        RawMarketMessageTelemetry telemetry,
        RecordingProgressRepository repository,
        CollectorSessionProgressCompletion completion,
        CapturingLogger<CollectorSessionProgressCompletion> logger,
        ServiceProvider provider)
        : IAsyncDisposable
    {
        public CollectorSessionId SessionId { get; } =
            CollectorSessionId.Create(Guid.NewGuid()).Value;
        public RawMarketMessageTelemetry Telemetry { get; } = telemetry;
        public RecordingProgressRepository Repository { get; } = repository;
        public CollectorSessionProgressCompletion Completion { get; } = completion;
        public CapturingLogger<CollectorSessionProgressCompletion> Logger { get; } = logger;

        public async ValueTask DisposeAsync()
        {
            Telemetry.Dispose();
            await provider.DisposeAsync();
        }
    }
}
