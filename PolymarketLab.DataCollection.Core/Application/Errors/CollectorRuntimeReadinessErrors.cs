using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.Errors;

public static class CollectorRuntimeReadinessErrors
{
    /// <summary>Initial book относится к токену вне immutable snapshot сессии.</summary>
    public static Error UnknownSnapshotToken(
        CollectorSessionId sessionId,
        TokenId tokenId) => new(
            "collector.runtime.readiness.token.unknown",
            $"Initial book token '{tokenId.Value}' does not belong to the immutable snapshot of collector session '{sessionId.Value}'.",
            ErrorType.Failure);

    /// <summary>Наблюдение готовности содержит недопустимые epoch или время.</summary>
    public static Error InvalidObservation(CollectorSessionId sessionId) => new(
        "collector.runtime.readiness.observation.invalid",
        $"Collector runtime readiness observation for session '{sessionId.Value}' is invalid.",
        ErrorType.Failure);
}
