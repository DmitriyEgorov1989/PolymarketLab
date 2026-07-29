using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

public sealed record CollectorRuntimeFailure(
    CollectorSessionId SessionId,
    DateTimeOffset FailedAt,
    Error Error);
