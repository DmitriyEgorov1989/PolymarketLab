using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Normalization;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Normalization;

public sealed class NormalizationMetricsBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenEnabled_ShouldRefreshVersionedBacklogAndDisposeScope()
    {
        var state = new ReaderState((projectionVersion, _, _) =>
            Task.FromResult(new NormalizationBacklogSnapshot(projectionVersion, 3, 5)));
        await using var fixture = CreateFixture(
            state,
            new NormalizerOptions
            {
                ProjectionVersion = 4,
                ClaimTimeout = TimeSpan.FromMinutes(7)
            });

        await fixture.Service.StartAsync(default);
        await state.FirstCall.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await state.FirstDisposed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        state.Request.Should().Be((4, TimeSpan.FromMinutes(7)));
        state.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ShouldNotResolveReader()
    {
        var state = new ReaderState((_, _, _) => throw new InvalidOperationException());
        await using var fixture = CreateFixture(
            state,
            new NormalizerOptions { Enabled = false });

        await fixture.Service.StartAsync(default);
        await fixture.Service.StopAsync(default);

        state.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRefreshFails_ShouldNotCreateHotLoop()
    {
        var state = new ReaderState((_, _, _) =>
            throw new InvalidOperationException("Database is unavailable."));
        await using var fixture = CreateFixture(state, new NormalizerOptions());

        await fixture.Service.StartAsync(default);
        await state.FirstCall.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(100);

        state.CallCount.Should().Be(1);
    }

    private static MetricsFixture CreateFixture(
        ReaderState state,
        NormalizerOptions options)
    {
        var services = new ServiceCollection();
        services.AddScoped<INormalizationBacklogReader>(_ => state.CreateReader());
        var provider = services.BuildServiceProvider();
        var telemetry = new NormalizerTelemetry();
        var service = new NormalizationMetricsBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            telemetry,
            Options.Create(options),
            TimeProvider.System,
            NullLogger<NormalizationMetricsBackgroundService>.Instance);
        return new MetricsFixture(service, provider, telemetry);
    }

    private sealed class ReaderState(
        Func<int, TimeSpan, CancellationToken, Task<NormalizationBacklogSnapshot>> read)
    {
        private int callCount;
        private int disposeCount;

        public int CallCount => Volatile.Read(ref callCount);
        public int DisposeCount => Volatile.Read(ref disposeCount);
        public (int ProjectionVersion, TimeSpan ClaimTimeout)? Request { get; private set; }
        public TaskCompletionSource FirstCall { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstDisposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public INormalizationBacklogReader CreateReader() => new StubReader(this);

        private Task<NormalizationBacklogSnapshot> ReadAsync(
            int projectionVersion,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            Request = (projectionVersion, claimTimeout);
            FirstCall.TrySetResult();
            return read(projectionVersion, claimTimeout, cancellationToken);
        }

        private sealed class StubReader(ReaderState state)
            : INormalizationBacklogReader, IDisposable
        {
            public Task<NormalizationBacklogSnapshot> ReadAsync(
                int projectionVersion,
                TimeSpan claimTimeout,
                CancellationToken cancellationToken) =>
                state.ReadAsync(projectionVersion, claimTimeout, cancellationToken);

            public void Dispose()
            {
                Interlocked.Increment(ref state.disposeCount);
                state.FirstDisposed.TrySetResult();
            }
        }
    }

    private sealed class MetricsFixture(
        NormalizationMetricsBackgroundService service,
        ServiceProvider provider,
        NormalizerTelemetry telemetry) : IAsyncDisposable
    {
        public NormalizationMetricsBackgroundService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Service.StopAsync(timeout.Token);
            telemetry.Dispose();
            await provider.DisposeAsync();
        }
    }
}
