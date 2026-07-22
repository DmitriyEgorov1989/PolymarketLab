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

    public static Error InvalidStoppedAt => new(
        "collector.session.stopped_at.invalid",
        "Collector session stop time cannot precede its start time.",
        ErrorType.ValueIsInvalid,
        "stoppedAt");

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
}
