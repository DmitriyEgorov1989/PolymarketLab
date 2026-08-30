using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.Errors;
using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.SharedKernel.DomainModels;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession;

/// <summary>Управляет lifecycle одного неизменяемого snapshot рынка.</summary>
public sealed class CollectorSession : Aggregate<CollectorSessionId>
{
    private readonly List<CollectorSessionToken> _tokens = [];

    private CollectorSession()
    {
    }

    private CollectorSession(
        CollectorSessionId id,
        MarketId marketId,
        string externalEventId,
        string eventSlug,
        string externalMarketId,
        string marketSlug,
        string conditionId,
        DateTimeOffset eventStartsAt,
        DateTimeOffset eventEndsAt,
        int projectionVersion,
        IReadOnlyCollection<CollectorSessionTokenDefinition> tokens,
        DateTimeOffset createdAt) : base(id)
    {
        MarketId = marketId;
        ExternalEventId = externalEventId;
        EventSlug = eventSlug;
        ExternalMarketId = externalMarketId;
        MarketSlug = marketSlug;
        ConditionId = conditionId;
        EventStartsAt = eventStartsAt;
        EventEndsAt = eventEndsAt;
        ProjectionVersion = projectionVersion;
        CreatedAt = createdAt;
        Status = CollectorSessionStatus.Scheduled;
        Phase = CollectorSessionPhase.WaitingForPreparation;
        _tokens.AddRange(tokens
            .OrderBy(token => token.OutcomeIndex)
            .Select(token => new CollectorSessionToken(id, token)));
    }

    /// <summary>Идентификатор зарегистрированного рынка.</summary>
    public MarketId MarketId { get; private set; } = null!;

    /// <summary>Идентификатор события Gamma; <see langword="null" /> только у legacy session.</summary>
    public string? ExternalEventId { get; private set; }

    /// <summary>Slug события Gamma; <see langword="null" /> только у legacy session.</summary>
    public string? EventSlug { get; private set; }

    /// <summary>Идентификатор дочернего рынка Gamma; <see langword="null" /> только у legacy session.</summary>
    public string? ExternalMarketId { get; private set; }

    /// <summary>Slug дочернего рынка Gamma; <see langword="null" /> только у legacy session.</summary>
    public string? MarketSlug { get; private set; }

    /// <summary>Condition id рынка; <see langword="null" /> только у legacy session.</summary>
    public string? ConditionId { get; private set; }

    /// <summary>Начало предметного окна; <see langword="null" /> только у legacy session.</summary>
    public DateTimeOffset? EventStartsAt { get; private set; }

    /// <summary>Конец предметного окна; <see langword="null" /> только у legacy session.</summary>
    public DateTimeOffset? EventEndsAt { get; private set; }

    /// <summary>Версия нормализации; <see langword="null" /> только у legacy session.</summary>
    public int? ProjectionVersion { get; private set; }

    /// <summary>Токены snapshot в порядке <see cref="CollectorSessionToken.OutcomeIndex" />.</summary>
    public IReadOnlyList<CollectorSessionToken> Tokens =>
        _tokens.OrderBy(token => token.OutcomeIndex).ToArray();

    /// <summary>Текущее состояние сессии.</summary>
    public CollectorSessionStatus Status { get; private set; }

    /// <summary>Точная фаза нетерминальной сессии; <see langword="null" /> для terminal и legacy session.</summary>
    public CollectorSessionPhase? Phase { get; private set; }

    /// <summary>Дата и время создания сессии.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Начало preparation; <see langword="null" />, если preparation не начиналась.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>Момент доказанной готовности подписки; <see langword="null" />, пока readiness не доказана.</summary>
    public DateTimeOffset? SubscriptionReadyAt { get; private set; }

    /// <summary>Дата завершения; <see langword="null" />, пока session нетерминальна.</summary>
    public DateTimeOffset? StoppedAt { get; private set; }

    /// <summary>Причина terminal transition; <see langword="null" />, пока session нетерминальна.</summary>
    public CollectorStopReason? StopReason { get; private set; }

    /// <summary>Машиночитаемый код failure; <see langword="null" /> при отсутствии failure.</summary>
    public string? FailureCode { get; private set; }

    /// <summary>Безопасное описание failure; <see langword="null" /> при отсутствии failure.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>Создаёт scheduled session с полным проверенным snapshot рынка.</summary>
    public static Result<CollectorSession, Error> Create(
        CollectorSessionId id,
        MarketId marketId,
        string externalEventId,
        string eventSlug,
        string externalMarketId,
        string marketSlug,
        string conditionId,
        DateTimeOffset eventStartsAt,
        DateTimeOffset eventEndsAt,
        int projectionVersion,
        IReadOnlyCollection<CollectorSessionTokenDefinition> tokens,
        DateTimeOffset createdAt)
    {
        if (createdAt == default)
            return CollectorSessionErrors.InvalidCreatedAt;
        if (string.IsNullOrWhiteSpace(externalEventId))
            return GeneralErrors.ValueIsRequired(nameof(externalEventId));
        if (string.IsNullOrWhiteSpace(eventSlug))
            return GeneralErrors.ValueIsRequired(nameof(eventSlug));
        if (string.IsNullOrWhiteSpace(externalMarketId))
            return GeneralErrors.ValueIsRequired(nameof(externalMarketId));
        if (string.IsNullOrWhiteSpace(marketSlug))
            return GeneralErrors.ValueIsRequired(nameof(marketSlug));
        if (string.IsNullOrWhiteSpace(conditionId))
            return GeneralErrors.ValueIsRequired(nameof(conditionId));
        if (eventStartsAt == default || eventEndsAt <= eventStartsAt)
            return CollectorSessionErrors.InvalidWindow;
        if (projectionVersion <= 0)
            return CollectorSessionErrors.InvalidProjectionVersion;
        if (tokens is null || tokens.Count < 2)
            return CollectorSessionErrors.TokensRequired;

        var missingOutcome = tokens.FirstOrDefault(token =>
            string.IsNullOrWhiteSpace(token.Outcome));
        if (missingOutcome is not null)
            return CollectorSessionErrors.TokenOutcomeRequired(missingOutcome.OutcomeIndex);

        var duplicateTokenId = tokens
            .GroupBy(token => token.TokenId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTokenId is not null)
            return CollectorSessionErrors.DuplicateTokenId(duplicateTokenId.Key.Value);

        var duplicateOutcomeIndex = tokens
            .GroupBy(token => token.OutcomeIndex)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateOutcomeIndex is not null)
        {
            return CollectorSessionErrors.DuplicateOutcomeIndex(
                duplicateOutcomeIndex.Key);
        }

        return new CollectorSession(
            id,
            marketId,
            externalEventId,
            eventSlug,
            externalMarketId,
            marketSlug,
            conditionId,
            eventStartsAt,
            eventEndsAt,
            projectionVersion,
            tokens.ToArray(),
            createdAt);
    }

    /// <summary>Начинает preparation запланированной session.</summary>
    public UnitResult<Error> BeginPreparation(DateTimeOffset startedAt)
    {
        if (Status != CollectorSessionStatus.Scheduled
            || Phase != CollectorSessionPhase.WaitingForPreparation)
        {
            return InvalidTransition(CollectorSessionStatus.Starting);
        }
        if (startedAt < CreatedAt)
            return UnitResult.Failure(CollectorSessionErrors.InvalidStartedAt);

        Status = CollectorSessionStatus.Starting;
        Phase = CollectorSessionPhase.Connecting;
        StartedAt = startedAt;
        return UnitResult.Success<Error>();
    }

    /// <summary>Отмечает ожидание initial books текущей WebSocket epoch.</summary>
    public UnitResult<Error> MarkAwaitingInitialBooks() =>
        ChangePhase(
            CollectorSessionStatus.Starting,
            CollectorSessionPhase.Connecting,
            CollectorSessionPhase.AwaitingInitialBooks);

    /// <summary>Отмечает ожидание heartbeat после получения initial books.</summary>
    public UnitResult<Error> MarkAwaitingHeartbeat() =>
        ChangePhase(
            CollectorSessionStatus.Starting,
            CollectorSessionPhase.AwaitingInitialBooks,
            CollectorSessionPhase.AwaitingHeartbeat);

    /// <summary>Фиксирует доказанную готовность подписки до предметного окна.</summary>
    public UnitResult<Error> MarkRunning(DateTimeOffset subscriptionReadyAt)
    {
        if (Status != CollectorSessionStatus.Starting
            || Phase != CollectorSessionPhase.AwaitingHeartbeat)
        {
            return InvalidTransition(CollectorSessionStatus.Running);
        }
        if (StartedAt is null || subscriptionReadyAt < StartedAt)
        {
            return UnitResult.Failure(
                CollectorSessionErrors.InvalidSubscriptionReadyAt);
        }

        Status = CollectorSessionStatus.Running;
        Phase = CollectorSessionPhase.ReadyBeforeWindow;
        SubscriptionReadyAt = subscriptionReadyAt;
        return UnitResult.Success<Error>();
    }

    /// <summary>Отмечает начало предметного окна сбора.</summary>
    public UnitResult<Error> MarkCollectingWindow() =>
        ChangePhase(
            CollectorSessionStatus.Running,
            CollectorSessionPhase.ReadyBeforeWindow,
            CollectorSessionPhase.CollectingWindow);

    /// <summary>Отмечает ожидание terminal resolution после конца окна.</summary>
    public UnitResult<Error> MarkAwaitingResolution() =>
        ChangePhase(
            CollectorSessionStatus.Running,
            CollectorSessionPhase.CollectingWindow,
            CollectorSessionPhase.AwaitingResolution);

    /// <summary>Начинает controlled raw drain.</summary>
    public UnitResult<Error> MarkStopping()
    {
        if (Status is not CollectorSessionStatus.Starting
            and not CollectorSessionStatus.Running)
        {
            return InvalidTransition(CollectorSessionStatus.Stopping);
        }

        Status = CollectorSessionStatus.Stopping;
        Phase = CollectorSessionPhase.DrainingRaw;
        return UnitResult.Success<Error>();
    }

    /// <summary>Отмечает ожидание завершения нормализации snapshot-версии.</summary>
    public UnitResult<Error> MarkAwaitingNormalization() =>
        ChangePhase(
            CollectorSessionStatus.Stopping,
            CollectorSessionPhase.DrainingRaw,
            CollectorSessionPhase.AwaitingNormalization);

    /// <summary>Необратимо начинает invalidation неполной session.</summary>
    public UnitResult<Error> BeginInvalidation()
    {
        if (!IsExclusive(Status))
            return InvalidTransition(CollectorSessionStatus.Invalidating);

        Status = CollectorSessionStatus.Invalidating;
        Phase = CollectorSessionPhase.Cleaning;
        return UnitResult.Success<Error>();
    }

    /// <summary>Завершает session с успешной terminal reason.</summary>
    public UnitResult<Error> Stop(DateTimeOffset stoppedAt, CollectorStopReason reason) =>
        Complete(stoppedAt, CollectorSessionStatus.Stopped, reason, null, null);

    /// <summary>Прерывает незавершённую session.</summary>
    public UnitResult<Error> Interrupt(
        DateTimeOffset interruptedAt,
        CollectorStopReason reason) =>
        Complete(interruptedAt, CollectorSessionStatus.Interrupted, reason, null, null);

    /// <summary>Завершает session с безопасной failure diagnostic.</summary>
    public UnitResult<Error> Fail(
        DateTimeOffset failedAt,
        CollectorStopReason reason,
        string failureCode,
        string failureMessage)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
            return UnitResult.Failure(GeneralErrors.ValueIsRequired(nameof(failureCode)));
        if (string.IsNullOrWhiteSpace(failureMessage))
            return UnitResult.Failure(GeneralErrors.ValueIsRequired(nameof(failureMessage)));

        return Complete(
            failedAt,
            CollectorSessionStatus.Failed,
            reason,
            failureCode,
            failureMessage);
    }

    private UnitResult<Error> Complete(
        DateTimeOffset completedAt,
        CollectorSessionStatus terminalStatus,
        CollectorStopReason reason,
        string? failureCode,
        string? failureMessage)
    {
        if (!IsExclusive(Status))
            return UnitResult.Failure(CollectorSessionErrors.NotActive);

        var lowerBound = StartedAt ?? CreatedAt;
        if (completedAt < lowerBound)
            return UnitResult.Failure(CollectorSessionErrors.InvalidStoppedAt);

        Status = terminalStatus;
        Phase = null;
        StoppedAt = completedAt;
        StopReason = reason;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        return UnitResult.Success<Error>();
    }

    private UnitResult<Error> ChangePhase(
        CollectorSessionStatus requiredStatus,
        CollectorSessionPhase requiredPhase,
        CollectorSessionPhase targetPhase)
    {
        if (Status != requiredStatus || Phase != requiredPhase)
        {
            return UnitResult.Failure(
                CollectorSessionErrors.InvalidPhaseTransition(
                    Status,
                    Phase,
                    targetPhase));
        }

        Phase = targetPhase;
        return UnitResult.Success<Error>();
    }

    private UnitResult<Error> InvalidTransition(CollectorSessionStatus target) =>
        UnitResult.Failure(CollectorSessionErrors.InvalidTransition(Status, target));

    private static bool IsExclusive(CollectorSessionStatus status) =>
        status is CollectorSessionStatus.Scheduled
            or CollectorSessionStatus.Starting
            or CollectorSessionStatus.Running
            or CollectorSessionStatus.Stopping
            or CollectorSessionStatus.Invalidating;
}
