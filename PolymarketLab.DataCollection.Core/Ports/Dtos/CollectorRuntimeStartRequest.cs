using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

public sealed record CollectorRuntimeStartRequest(
    CollectorSessionId SessionId,
    CollectionMarket Market);
