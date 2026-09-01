using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Domain.Models.Resolution;
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

    /// <summary>Момент получения подтверждающего WebSocket signal; <see langword="null" />, пока signal не принят.</summary>
    public DateTimeOffset? ResolutionSignaledAt { get; private set; }

    /// <summary>Момент согласования всех resolution sources; <see langword="null" />, пока consensus не достигнут.</summary>
    public DateTimeOffset? ResolutionConfirmedAt { get; private set; }

    /// <summary>Выигравший token id; <see langword="null" />, пока consensus не достигнут.</summary>
    public string? WinningTokenId { get; private set; }

    /// <summary>Выигравший outcome; <see langword="null" />, пока consensus не достигнут.</summary>
    public string? WinningOutcome { get; private set; }

    /// <summary>Connection epoch WebSocket signal; <see langword="null" />, пока consensus не достигнут.</summary>
    public long? ResolutionConnectionEpoch { get; private set; }

    /// <summary>Дата завершения; <see langword="null" />, пока session нетерминальна.</summary>
    public DateTimeOffset? StoppedAt { get; private set; }

    /// <summary>
    /// Момент установки durable write fence;
    /// <see langword="null" />, если invalidation ещё не начиналась.
    /// </summary>
    public DateTimeOffset? InvalidatingAt { get; private set; }

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

    /// <summary>Фиксирует согласованный всеми источниками terminal winner.</summary>
    public UnitResult<Error> ConfirmResolution(
        DateTimeOffset signaledAt,
        DateTimeOffset confirmedAt,
        ResolutionWinner winner,
        long connectionEpoch)
    {
        ArgumentNullException.ThrowIfNull(winner);

        if (Status != CollectorSessionStatus.Running
            || Phase != CollectorSessionPhase.AwaitingResolution)
        {
            return CollectorSessionErrors.InvalidPhaseTransition(
                Status,
                Phase,
                CollectorSessionPhase.AwaitingResolution);
        }
        if (EventEndsAt is null
            || signaledAt < EventEndsAt.Value
            || confirmedAt < signaledAt)
        {
            return UnitResult.Failure(CollectorSessionErrors.InvalidResolutionTimestamps);
        }
        if (connectionEpoch <= 0)
            return UnitResult.Failure(CollectorSessionErrors.InvalidResolutionConnectionEpoch);

        var snapshotWinner = Tokens.SingleOrDefault(token =>
            string.Equals(token.TokenId.Value, winner.TokenId, StringComparison.Ordinal)
            && string.Equals(token.Outcome, winner.Outcome, StringComparison.Ordinal));
        if (snapshotWinner is null)
            return UnitResult.Failure(CollectorSessionErrors.InvalidResolutionWinner);

        ResolutionSignaledAt = signaledAt;
        ResolutionConfirmedAt = confirmedAt;
        WinningTokenId = snapshotWinner.TokenId.Value;
        WinningOutcome = snapshotWinner.Outcome;
        ResolutionConnectionEpoch = connectionEpoch;
        return UnitResult.Success<Error>();
    }

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

    /// <summary>
    /// Необратимо начинает invalidation неполной session и сохраняет первую безопасную
    /// diagnostic; повторный вызов не меняет исходную причину.
    /// </summary>
    public UnitResult<Error> BeginInvalidation(
        DateTimeOffset invalidatingAt,
        CollectorStopReason reason,
        string failureCode,
        string failureMessage)
    {
        if (Status == CollectorSessionStatus.Invalidating)
            return UnitResult.Success<Error>();
        if (Status is not CollectorSessionStatus.Scheduled
            and not CollectorSessionStatus.Starting
            and not CollectorSessionStatus.Running
            and not CollectorSessionStatus.Stopping)
        {
            return InvalidTransition(CollectorSessionStatus.Invalidating);
        }

        var lowerBound = StartedAt ?? CreatedAt;
        if (invalidatingAt < lowerBound)
            return UnitResult.Failure(CollectorSessionErrors.InvalidInvalidatingAt);
        if (string.IsNullOrWhiteSpace(failureCode))
            return UnitResult.Failure(GeneralErrors.ValueIsRequired(nameof(failureCode)));
        if (string.IsNullOrWhiteSpace(failureMessage))
            return UnitResult.Failure(GeneralErrors.ValueIsRequired(nameof(failureMessage)));

        Status = CollectorSessionStatus.Invalidating;
        Phase = CollectorSessionPhase.Cleaning;
        InvalidatingAt = invalidatingAt;
        StopReason = reason;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
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
