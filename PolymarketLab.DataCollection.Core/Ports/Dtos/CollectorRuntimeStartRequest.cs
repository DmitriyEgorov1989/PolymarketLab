using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>Данные для запуска runtime сборщика.</summary>
/// <param name="SessionId">Идентификатор сохранённой сессии.</param>
/// <param name="Market">Рынок и токены для подписки.</param>
/// <param name="ReadinessDeadline">Крайний UTC-момент доказательства готовности подписки.</param>
public sealed record CollectorRuntimeStartRequest(
    CollectorSessionId SessionId,
    CollectionMarket Market,
    DateTimeOffset ReadinessDeadline);
