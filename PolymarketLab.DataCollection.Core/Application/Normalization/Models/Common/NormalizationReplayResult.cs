namespace PolymarketLab.DataCollection.Core.Application.Normalization.Models;

public sealed record NormalizationReplayResult(
    int BatchCount,
    int Total,
    int Processed,
    int Invalid,
    int Unsupported,
    int Failed,
    long? FirstRawMessageId,
    long? LastRawMessageId);
