using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

public static class ReplayNormalizationErrors
{
    public static readonly Error SourceProjectionVersionInvalid = new(
        "normalization.replay.source_projection_version.invalid",
        "Source projection version must be positive.",
        ErrorType.ValueIsInvalid,
        "sourceProjectionVersion");

    public static readonly Error TargetProjectionVersionInvalid = new(
        "normalization.replay.target_projection_version.invalid",
        "Target projection version must be greater than source projection version.",
        ErrorType.ValueIsInvalid,
        "targetProjectionVersion");

    public static readonly Error SessionIdInvalid = new(
        "normalization.replay.session_id.invalid",
        "Session id must not be empty.",
        ErrorType.ValueIsInvalid,
        "sessionId");

    public static readonly Error EventTypeInvalid = new(
        "normalization.replay.event_type.invalid",
        "Event type must contain between 1 and 128 characters.",
        ErrorType.ValueIsInvalid,
        "eventType");

    public static Error TargetProjectionVersionIsActive(int projectionVersion) => new(
        "normalization.replay.target_projection_version.active",
        $"Projection version {projectionVersion} is used by the active Normalizer.",
        ErrorType.Conflict,
        "targetProjectionVersion");
}
