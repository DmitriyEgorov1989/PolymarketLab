using PolymarketLab.DataCollection.Core.Ports.Dtos;

namespace PolymarketLab.DataCollection.Core.Ports;

public interface IRawMessageNormalizationReplayClaimRepository
{
    Task<NormalizationReplaySnapshot> CaptureSnapshotAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ClaimedRawMessage>> ClaimBatchAsync(
        NormalizationReplayFilter filter,
        NormalizationReplaySnapshot snapshot,
        int batchSize,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken);

    Task<bool> HasRemainingAsync(
        NormalizationReplayFilter filter,
        NormalizationReplaySnapshot snapshot,
        CancellationToken cancellationToken);
}
