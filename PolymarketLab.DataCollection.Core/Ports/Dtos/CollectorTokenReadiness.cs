using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Durable-наблюдение успешной постановки initial book одного токена в bounded ingestion.</summary>
/// <param name="SessionId">Идентификатор сессии.</param>
/// <param name="ConnectionEpoch">Connection epoch, в которой наблюдался initial book.</param>
/// <param name="TokenId">Внешний идентификатор токена.</param>
/// <param name="InitialBookEnqueuedAt">UTC-момент успешной постановки initial book.</param>
public sealed record CollectorTokenReadiness(
    CollectorSessionId SessionId,
    long ConnectionEpoch,
    TokenId TokenId,
    DateTimeOffset InitialBookEnqueuedAt);
