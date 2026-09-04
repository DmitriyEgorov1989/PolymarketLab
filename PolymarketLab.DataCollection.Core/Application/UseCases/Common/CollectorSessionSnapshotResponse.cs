namespace PolymarketLab.DataCollection.Core.Application.UseCases.Common;

/// <summary>Безопасный снимок рынка коллекторной сессии для HTTP-ответа.</summary>
/// <param name="ExternalEventId">Идентификатор события Gamma; <see langword="null" /> только у legacy session.</param>
/// <param name="EventSlug">Slug события Gamma; <see langword="null" /> только у legacy session.</param>
/// <param name="ExternalMarketId">Идентификатор дочернего рынка Gamma; <see langword="null" /> только у legacy session.</param>
/// <param name="MarketSlug">Slug дочернего рынка Gamma; <see langword="null" /> только у legacy session.</param>
/// <param name="ConditionId">Condition id рынка; <see langword="null" /> только у legacy session.</param>
/// <param name="EventStartsAt">Начало предметного окна; <see langword="null" /> только у legacy session.</param>
/// <param name="EventEndsAt">Конец предметного окна; <see langword="null" /> только у legacy session.</param>
/// <param name="ProjectionVersion">Версия нормализации; <see langword="null" /> только у legacy session.</param>
/// <param name="Tokens">Токены исхода snapshot в порядке индекса; всегда массив.</param>
public sealed record CollectorSessionSnapshotResponse(
    string? ExternalEventId,
    string? EventSlug,
    string? ExternalMarketId,
    string? MarketSlug,
    string? ConditionId,
    DateTimeOffset? EventStartsAt,
    DateTimeOffset? EventEndsAt,
    int? ProjectionVersion,
    IReadOnlyList<CollectorSessionTokenResponse> Tokens);

/// <summary>Токен исхода из неизменяемого snapshot сессии.</summary>
/// <param name="TokenId">Внешний идентификатор токена.</param>
/// <param name="Outcome">Название исхода в момент создания сессии.</param>
/// <param name="OutcomeIndex">Позиция исхода во внешнем рынке.</param>
public sealed record CollectorSessionTokenResponse(
    string TokenId,
    string Outcome,
    int OutcomeIndex);
