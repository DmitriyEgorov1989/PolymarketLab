using PolymarketLab.DataCollection.Core.Domain.Models.Enums;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.Errors;

internal static class CollectorSessionErrors
{
    public static Error InvalidCreatedAt => new(
        "collector.session.created_at.invalid",
        "Collector session creation time must be specified.",
        ErrorType.ValueIsInvalid,
        "createdAt");

    public static Error InvalidStartedAt => new(
        "collector.session.started_at.invalid",
        "Collector session start time cannot precede its creation time.",
        ErrorType.ValueIsInvalid,
        "startedAt");

    public static Error InvalidSubscriptionReadyAt => new(
        "collector.session.subscription_ready_at.invalid",
        "Collector subscription readiness cannot precede preparation.",
        ErrorType.ValueIsInvalid,
        "subscriptionReadyAt");

    public static Error InvalidWindow => new(
        "collector.session.window.invalid",
        "Collector session end time must be later than its start time.",
        ErrorType.ValueIsInvalid,
        "eventEndsAt");

    public static Error InvalidProjectionVersion => new(
        "collector.session.projection_version.invalid",
        "Collector session projection version must be positive.",
        ErrorType.ValueIsInvalid,
        "projectionVersion");

    public static Error TokensRequired => new(
        "collector.session.tokens.insufficient",
        "Collector session requires at least two snapshot tokens.",
        ErrorType.CollectionIsTooSmall,
        "tokens");

    public static Error TokenOutcomeRequired(int outcomeIndex) => new(
        "collector.session.token_outcome.required",
        $"Collector session token outcome is required at index '{outcomeIndex}'.",
        ErrorType.ValueIsRequired,
        "tokens");

    public static Error DuplicateTokenId(string tokenId) => new(
        "collector.session.token_id.duplicate",
        $"Collector session token id '{tokenId}' is duplicated.",
        ErrorType.Conflict,
        "tokens");

    public static Error DuplicateOutcomeIndex(int outcomeIndex) => new(
        "collector.session.outcome_index.duplicate",
        $"Collector session outcome index '{outcomeIndex}' is duplicated.",
        ErrorType.Conflict,
        "tokens");

    public static Error InvalidStoppedAt => new(
        "collector.session.stopped_at.invalid",
        "Collector session stop time cannot precede its start time.",
        ErrorType.ValueIsInvalid,
        "stoppedAt");

    public static Error InvalidInvalidatingAt => new(
        "collector.session.invalidating_at.invalid",
        "Collector session invalidation time cannot precede its start time.",
        ErrorType.ValueIsInvalid,
        "invalidatingAt");

    public static Error InvalidResolutionTimestamps => new(
        "collector.session.resolution_timestamps.invalid",
        "Resolution signal must not precede the event end and confirmation must not precede the signal.",
        ErrorType.ValueIsInvalid,
        "resolutionConfirmedAt");

    public static Error InvalidResolutionConnectionEpoch => new(
        "collector.session.resolution_connection_epoch.invalid",
        "Resolution connection epoch must be positive.",
        ErrorType.ValueIsInvalid,
        "resolutionConnectionEpoch");

    public static Error InvalidResolutionWinner => new(
        "collector.session.resolution_winner.invalid",
        "Resolution winner must match one token/outcome pair in the collector session snapshot.",
        ErrorType.Conflict,
        "winningTokenId");

    public static Error NotActive => new(
        "collector.session.not_active",
        "Collector session is not active.",
        ErrorType.Conflict);

    public static Error InvalidTransition(
        CollectorSessionStatus current,
        CollectorSessionStatus target) => new(
        "collector.session.transition.invalid",
        $"Collector session cannot transition from '{current}' to '{target}'.",
        ErrorType.Conflict,
        "status");

    public static Error InvalidPhaseTransition(
        CollectorSessionStatus status,
        CollectorSessionPhase? current,
        CollectorSessionPhase target) => new(
        "collector.session.phase_transition.invalid",
        $"Collector session cannot transition from '{status}/{current?.ToString() ?? "null"}' to phase '{target}'.",
        ErrorType.Conflict,
        "phase");
}
