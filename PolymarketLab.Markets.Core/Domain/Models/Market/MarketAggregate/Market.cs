using CSharpFunctionalExtensions;
using PolymarketLab.Markets.Core.Domain.Models.Market.Entity;
using PolymarketLab.Markets.Core.Domain.Models.Market.Errors;
using PolymarketLab.Markets.Core.Domain.Models.Market.ValueObjects;
using PolymarketLab.SharedKernel.DomainModels;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.Markets.Core.Domain.Models.Market.MarketAggregate;

/// <summary>
///     Представляет зарегистрированный дочерний рынок Polymarket и identity его родительского события.
/// </summary>
public sealed class Market : Aggregate<MarketId>
{
    private readonly List<MarketToken> _tokens = [];

    private Market()
    {
    }

    private Market(
        MarketId id,
        ExternalEventId externalEventId,
        EventSlug eventSlug,
        ExternalMarketId externalMarketId,
        MarketSlug marketSlug,
        ConditionId conditionId,
        string question,
        DateTimeOffset discoveredAt,
        DateTimeOffset? externalCreatedAt,
        DateTimeOffset? ordersOpenedAt,
        DateTimeOffset? gammaStartDate,
        DateTimeOffset eventStartsAt,
        DateTimeOffset eventEndsAt,
        DateTimeOffset? externalClosedAt,
        DateTimeOffset scheduleRefreshedAt) : base(id)
    {
        ExternalEventId = externalEventId;
        EventSlug = eventSlug;
        ExternalMarketId = externalMarketId;
        MarketSlug = marketSlug;
        ConditionId = conditionId;
        Question = question;
        DiscoveredAt = discoveredAt.ToUniversalTime();
        SetSchedule(
            externalCreatedAt,
            ordersOpenedAt,
            gammaStartDate,
            eventStartsAt,
            eventEndsAt,
            externalClosedAt,
            scheduleRefreshedAt);
    }

    /// <summary>Возвращает идентификатор родительского события Gamma.</summary>
    public ExternalEventId ExternalEventId { get; private set; } = null!;

    /// <summary>Возвращает slug родительского события Gamma.</summary>
    public EventSlug EventSlug { get; private set; } = null!;

    /// <summary>Возвращает идентификатор дочернего рынка Gamma.</summary>
    public ExternalMarketId ExternalMarketId { get; private set; } = null!;

    /// <summary>Возвращает slug дочернего рынка Gamma.</summary>
    public MarketSlug MarketSlug { get; private set; } = null!;

    /// <summary>Возвращает идентификатор condition в CLOB.</summary>
    public ConditionId ConditionId { get; private set; } = null!;

    /// <summary>Возвращает вопрос рынка.</summary>
    public string Question { get; private set; } = string.Empty;

    /// <summary>Возвращает неизменяемое UTC-время первого успешного обнаружения события.</summary>
    public DateTimeOffset DiscoveredAt { get; private set; }

    /// <summary>Возвращает Gamma market <c>createdAt</c> в UTC либо <see langword="null"/>, если значение отсутствует.</summary>
    public DateTimeOffset? ExternalCreatedAt { get; private set; }

    /// <summary>Возвращает Gamma market <c>acceptingOrdersTimestamp</c> в UTC либо <see langword="null"/>, если значение отсутствует.</summary>
    public DateTimeOffset? OrdersOpenedAt { get; private set; }

    /// <summary>Возвращает Gamma market <c>startDate</c> в UTC либо <see langword="null"/>, если значение отсутствует.</summary>
    public DateTimeOffset? GammaStartDate { get; private set; }

    /// <summary>Возвращает обязательное Gamma market <c>eventStartTime</c> в UTC.</summary>
    public DateTimeOffset EventStartsAt { get; private set; }

    /// <summary>Возвращает обязательное Gamma market <c>endDate</c> в UTC.</summary>
    public DateTimeOffset EventEndsAt { get; private set; }

    /// <summary>Возвращает Gamma market <c>closedTime</c> в UTC либо <see langword="null"/>, если значение отсутствует.</summary>
    public DateTimeOffset? ExternalClosedAt { get; private set; }

    /// <summary>Возвращает UTC-время последнего успешного обновления расписания.</summary>
    public DateTimeOffset ScheduleRefreshedAt { get; private set; }

    /// <summary>Возвращает упорядоченные соответствия outcomes и tokens.</summary>
    public IReadOnlyCollection<MarketToken> Tokens => _tokens;

    /// <summary>
    ///     Создаёт зарегистрированный рынок с неизменяемой identity обнаружения и исходным расписанием.
    /// </summary>
    /// <param name="id">Локальный идентификатор рынка.</param>
    /// <param name="externalEventId">Внешний идентификатор события.</param>
    /// <param name="eventSlug">Slug родительского события.</param>
    /// <param name="externalMarketId">Внешний идентификатор дочернего рынка.</param>
    /// <param name="marketSlug">Slug дочернего рынка.</param>
    /// <param name="conditionId">Идентификатор condition в CLOB.</param>
    /// <param name="question">Непустой вопрос рынка.</param>
    /// <param name="discoveredAt">UTC-время первого успешного обнаружения.</param>
    /// <param name="externalCreatedAt">Внешнее время создания либо <see langword="null"/>, если оно отсутствует.</param>
    /// <param name="ordersOpenedAt">Внешнее время открытия заявок либо <see langword="null"/>, если оно отсутствует.</param>
    /// <param name="gammaStartDate">Gamma <c>startDate</c> либо <see langword="null"/>, если значение отсутствует.</param>
    /// <param name="eventStartsAt">Обязательное начало предметного окна.</param>
    /// <param name="eventEndsAt">Обязательный конец предметного окна, следующий после начала.</param>
    /// <param name="externalClosedAt">Внешнее время закрытия либо <see langword="null"/>, если оно отсутствует.</param>
    /// <param name="scheduleRefreshedAt">UTC-время исходного чтения расписания.</param>
    /// <returns>Созданный рынок либо ошибка проверки входных значений.</returns>
    public static Result<Market, Error> Create(
        MarketId id,
        ExternalEventId externalEventId,
        EventSlug eventSlug,
        ExternalMarketId externalMarketId,
        MarketSlug marketSlug,
        ConditionId conditionId,
        string question,
        DateTimeOffset discoveredAt,
        DateTimeOffset? externalCreatedAt,
        DateTimeOffset? ordersOpenedAt,
        DateTimeOffset? gammaStartDate,
        DateTimeOffset eventStartsAt,
        DateTimeOffset eventEndsAt,
        DateTimeOffset? externalClosedAt,
        DateTimeOffset scheduleRefreshedAt)
    {
        if (string.IsNullOrWhiteSpace(question))
            return GeneralErrors.ValueIsRequired(nameof(question));

        if (eventEndsAt <= eventStartsAt)
            return GeneralErrors.ValueIsInvalid(nameof(eventEndsAt));

        return new Market(
            id,
            externalEventId,
            eventSlug,
            externalMarketId,
            marketSlug,
            conditionId,
            question,
            discoveredAt,
            externalCreatedAt,
            ordersOpenedAt,
            gammaStartDate,
            eventStartsAt,
            eventEndsAt,
            externalClosedAt,
            scheduleRefreshedAt);
    }

    /// <summary>
    ///     Определяет, представляют ли оба aggregate одну полную внешнюю identity.
    /// </summary>
    /// <param name="other">Другой полностью сформированный рынок.</param>
    /// <returns><see langword="true"/> при полном совпадении identity и ordered tokens.</returns>
    public bool HasSameIdentity(Market other)
    {
        if (!ExternalEventId.Equals(other.ExternalEventId)
            || !EventSlug.Equals(other.EventSlug)
            || !ExternalMarketId.Equals(other.ExternalMarketId)
            || !MarketSlug.Equals(other.MarketSlug)
            || !ConditionId.Equals(other.ConditionId)
            || _tokens.Count != other._tokens.Count)
        {
            return false;
        }

        var tokens = _tokens.OrderBy(token => token.OutcomeIndex);
        var otherTokens = other._tokens.OrderBy(token => token.OutcomeIndex);

        return tokens.Zip(otherTokens).All(pair =>
            pair.First.ExternalTokenId.Equals(pair.Second.ExternalTokenId)
            && pair.First.Outcome == pair.Second.Outcome
            && pair.First.OutcomeIndex == pair.Second.OutcomeIndex);
    }

    /// <summary>
    ///     Заменяет изменяемые внешние наблюдения расписания, сохраняя identity обнаружения.
    /// </summary>
    /// <param name="externalCreatedAt">Внешнее время создания либо <see langword="null"/>, если оно отсутствует.</param>
    /// <param name="ordersOpenedAt">Внешнее время открытия заявок либо <see langword="null"/>, если оно отсутствует.</param>
    /// <param name="gammaStartDate">Gamma <c>startDate</c> либо <see langword="null"/>, если значение отсутствует.</param>
    /// <param name="eventStartsAt">Обязательное новое начало предметного окна.</param>
    /// <param name="eventEndsAt">Обязательный новый конец предметного окна, следующий после начала.</param>
    /// <param name="externalClosedAt">Внешнее время закрытия либо <see langword="null"/>, если оно отсутствует.</param>
    /// <param name="scheduleRefreshedAt">UTC-время успешного чтения нового расписания.</param>
    /// <returns>Успех либо ошибка проверки нового расписания.</returns>
    public UnitResult<Error> RefreshSchedule(
        DateTimeOffset? externalCreatedAt,
        DateTimeOffset? ordersOpenedAt,
        DateTimeOffset? gammaStartDate,
        DateTimeOffset eventStartsAt,
        DateTimeOffset eventEndsAt,
        DateTimeOffset? externalClosedAt,
        DateTimeOffset scheduleRefreshedAt)
    {
        if (eventEndsAt <= eventStartsAt)
            return UnitResult.Failure(GeneralErrors.ValueIsInvalid(nameof(eventEndsAt)));

        SetSchedule(
            externalCreatedAt,
            ordersOpenedAt,
            gammaStartDate,
            eventStartsAt,
            eventEndsAt,
            externalClosedAt,
            scheduleRefreshedAt);

        return UnitResult.Success<Error>();
    }

    /// <summary>Добавляет одно упорядоченное соответствие outcome и token в identity рынка.</summary>
    /// <param name="externalTokenId">Внешний идентификатор token.</param>
    /// <param name="outcome">Непустое название outcome.</param>
    /// <param name="outcomeIndex">Неотрицательная позиция outcome во внешнем порядке.</param>
    /// <returns>Успех либо ошибка проверки или конфликт identity token.</returns>
    public UnitResult<Error> AddToken(TokenId externalTokenId, string outcome, int outcomeIndex)
    {
        if (_tokens.Any(token => token.ExternalTokenId.Equals(externalTokenId)))
            return UnitResult.Failure(MarketErrors.DuplicateTokenId(externalTokenId.Value));

        if (_tokens.Any(token => token.OutcomeIndex == outcomeIndex))
            return UnitResult.Failure(MarketErrors.DuplicateOutcomeIndex(outcomeIndex));

        var tokenResult = MarketToken.Create(Id, externalTokenId, outcome, outcomeIndex);
        if (tokenResult.IsFailure)
            return UnitResult.Failure(tokenResult.Error);

        _tokens.Add(tokenResult.Value);
        return UnitResult.Success<Error>();
    }

    private void SetSchedule(
        DateTimeOffset? externalCreatedAt,
        DateTimeOffset? ordersOpenedAt,
        DateTimeOffset? gammaStartDate,
        DateTimeOffset eventStartsAt,
        DateTimeOffset eventEndsAt,
        DateTimeOffset? externalClosedAt,
        DateTimeOffset scheduleRefreshedAt)
    {
        ExternalCreatedAt = externalCreatedAt?.ToUniversalTime();
        OrdersOpenedAt = ordersOpenedAt?.ToUniversalTime();
        GammaStartDate = gammaStartDate?.ToUniversalTime();
        EventStartsAt = eventStartsAt.ToUniversalTime();
        EventEndsAt = eventEndsAt.ToUniversalTime();
        ExternalClosedAt = externalClosedAt?.ToUniversalTime();
        ScheduleRefreshedAt = scheduleRefreshedAt.ToUniversalTime();
    }
}
