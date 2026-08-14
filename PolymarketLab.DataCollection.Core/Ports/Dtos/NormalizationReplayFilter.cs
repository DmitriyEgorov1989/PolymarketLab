using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

public sealed record NormalizationReplayFilter(
    int SourceProjectionVersion,
    int TargetProjectionVersion,
    CollectorSessionId? SessionId,
    string? EventType);
