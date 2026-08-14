using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PolymarketLab.Core.Options;
using PolymarketLab.DataCollection.Core.Application.Normalization;
using PolymarketLab.DataCollection.Core.Application.Normalization.Models;
using PolymarketLab.DataCollection.Core.Ports;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.DataCollection.Infrastructure.Adapters.Normalization;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using Xunit;

namespace PolymarketLab.DataCollection.Infrastructure.Tests.Adapters.Normalization;

public sealed class NormalizationReplayServiceTests
{
    [Fact]
    public async Task Replay_ShouldUseBoundedBatchesAndAggregateResult()
    {
        var state = new ReplayState([100, 1, 0]);
        await using var provider = CreateProvider(state);
        var service = new NormalizationReplayService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new NormalizerOptions { BatchSize = 500 }));

        var result = await service.ReplayAsync(
            new NormalizationReplayFilter(1, 2, null, null),
            default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new NormalizationReplayResult(
            2, 101, 101, 0, 0, 0, 1, 101));
        state.RequestedBatchSizes.Should().OnlyContain(batchSize => batchSize == 100);
        state.ProcessedBatchSizes.Should().Equal(100, 1);
    }

    [Fact]
    public async Task Replay_TargetUsedByLiveNormalizer_ShouldReturnConflictWithoutClaiming()
    {
        var state = new ReplayState([]);
        await using var provider = CreateProvider(state);
        var service = new NormalizationReplayService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new NormalizerOptions
            {
                Enabled = true,
                ProjectionVersion = 2
            }));

        var result = await service.ReplayAsync(
            new NormalizationReplayFilter(1, 2, null, null),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("normalization.replay.target_projection_version.active");
        state.CaptureCalls.Should().Be(0);
    }

    [Fact]
    public async Task Replay_EmptyContendedBatch_ShouldRetryUntilSnapshotIsComplete()
    {
        var state = new ReplayState([0, 1, 0], [true, false]);
        await using var provider = CreateProvider(state);
        var service = new NormalizationReplayService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new NormalizerOptions()));

        var result = await service.ReplayAsync(
            new NormalizationReplayFilter(1, 2, null, null),
            default);

        result.Value.Total.Should().Be(1);
        state.RequestedBatchSizes.Should().HaveCount(3);
    }

    private static ServiceProvider CreateProvider(ReplayState state)
    {
        var services = new ServiceCollection();
        services.AddScoped<IRawMessageNormalizationReplayClaimRepository>(_ => state);
        services.AddScoped<IClaimedNormalizationBatchProcessor>(_ => state);
        return services.BuildServiceProvider();
    }

    private sealed class ReplayState(
        IReadOnlyCollection<int> batchSizes,
        IReadOnlyCollection<bool>? remaining = null)
        : IRawMessageNormalizationReplayClaimRepository, IClaimedNormalizationBatchProcessor
    {
        private readonly Queue<int> batchSizes = new(batchSizes);
        private readonly Queue<bool> remaining = new(remaining ?? []);
        private long nextRawMessageId = 1;

        public int CaptureCalls { get; private set; }
        public List<int> RequestedBatchSizes { get; } = [];
        public List<int> ProcessedBatchSizes { get; } = [];

        public Task<NormalizationReplaySnapshot> CaptureSnapshotAsync(
            CancellationToken cancellationToken)
        {
            CaptureCalls++;
            return Task.FromResult(new NormalizationReplaySnapshot(
                1_000,
                DateTimeOffset.Parse("2026-08-14T10:00:00Z")));
        }

        public Task<IReadOnlyList<ClaimedRawMessage>> ClaimBatchAsync(
            NormalizationReplayFilter filter,
            NormalizationReplaySnapshot snapshot,
            int batchSize,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            RequestedBatchSizes.Add(batchSize);
            var count = batchSizes.Dequeue();
            IReadOnlyList<ClaimedRawMessage> claims = Enumerable.Range(0, count)
                .Select(_ => CreateClaim(nextRawMessageId++, filter.TargetProjectionVersion))
                .ToArray();
            return Task.FromResult(claims);
        }

        public Task<bool> HasRemainingAsync(
            NormalizationReplayFilter filter,
            NormalizationReplaySnapshot snapshot,
            CancellationToken cancellationToken) =>
            Task.FromResult(remaining.Count > 0 && remaining.Dequeue());

        public Task<NormalizationBatchResult> ProcessClaimsAsync(
            IReadOnlyList<ClaimedRawMessage> claims,
            CancellationToken cancellationToken)
        {
            if (claims.Count == 0)
                return Task.FromResult(new NormalizationBatchResult(0, 0, 0, 0, 0, null, null));

            ProcessedBatchSizes.Add(claims.Count);
            return Task.FromResult(new NormalizationBatchResult(
                claims.Count,
                claims.Count,
                0,
                0,
                0,
                claims[0].Message.RawMessageId,
                claims[^1].Message.RawMessageId));
        }

        private static ClaimedRawMessage CreateClaim(long rawMessageId, int projectionVersion) =>
            new(
                new RawMessageEnvelope(
                    rawMessageId,
                    CollectorSessionId.Create(
                        Guid.Parse("11111111-1111-1111-1111-111111111111")).Value,
                    DateTimeOffset.Parse("2026-08-14T10:00:00Z"),
                    Array.Empty<byte>()),
                projectionVersion,
                1);
    }
}
