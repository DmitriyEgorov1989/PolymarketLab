namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

public sealed record NormalizationReplaySnapshot(
    long HighWatermarkRawMessageId,
    DateTimeOffset SourceCompletedBefore);
