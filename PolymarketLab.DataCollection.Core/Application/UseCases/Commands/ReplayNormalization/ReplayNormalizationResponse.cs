namespace PolymarketLab.DataCollection.Core.Application.UseCases.Commands.ReplayNormalization;

public sealed record ReplayNormalizationResponse(
    int SourceProjectionVersion,
    int TargetProjectionVersion,
    Guid? SessionId,
    string? EventType,
    int BatchCount,
    int Total,
    int Processed,
    int Invalid,
    int Unsupported,
    int Failed,
    long? FirstRawMessageId,
    long? LastRawMessageId);
