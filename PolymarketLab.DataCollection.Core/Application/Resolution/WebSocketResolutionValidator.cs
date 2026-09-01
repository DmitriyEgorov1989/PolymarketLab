using CSharpFunctionalExtensions;
using PolymarketLab.DataCollection.Core.Domain.Models.Resolution;
using PolymarketLab.SharedKernel.Errors;
using CollectorSessionAggregate = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Core.Application.Resolution;

/// <summary>Проверяет WebSocket market_resolved относительно immutable snapshot текущей session.</summary>
public sealed class WebSocketResolutionValidator
{
    /// <summary>Проверяет время, epoch, identity, token set и победителя observation.</summary>
    public Result<WebSocketResolutionValidation, Error> Validate(
        WebSocketResolutionCandidate candidate,
        CollectorSessionAggregate session,
        long currentConnectionEpoch,
        DateTimeOffset confirmationDeadline)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(session);

        if (session.EventEndsAt is null
            || candidate.ReceivedAt < session.EventEndsAt.Value)
        {
            return WebSocketResolutionValidation.Rejected("PreEndObservation");
        }

        if (candidate.ReceivedAt > confirmationDeadline)
            return WebSocketResolutionValidation.Rejected("PostDeadlineObservation");

        if (candidate.ConnectionEpoch != currentConnectionEpoch)
            return WebSocketResolutionValidation.Rejected("StaleConnectionEpoch");

        if (!string.Equals(
                candidate.ExternalMarketId,
                session.ExternalMarketId,
                StringComparison.Ordinal)
            || !string.Equals(
                candidate.ConditionId,
                session.ConditionId,
                StringComparison.Ordinal)
            || !HasExactTokenSet(candidate.AssetIds, session))
        {
            return ResolutionErrors.Conflict;
        }

        var winner = session.Tokens.SingleOrDefault(token =>
            string.Equals(
                token.TokenId.Value,
                candidate.WinningAssetId,
                StringComparison.Ordinal));
        if (winner is null
            || !string.Equals(
                winner.Outcome,
                candidate.WinningOutcome,
                StringComparison.Ordinal))
        {
            return ResolutionErrors.Conflict;
        }

        return WebSocketResolutionValidation.Terminal(
            new ResolutionWinner(winner.TokenId.Value, winner.Outcome));
    }

    private static bool HasExactTokenSet(
        IReadOnlyCollection<string>? assetIds,
        CollectorSessionAggregate session)
    {
        if (assetIds is null || assetIds.Count != session.Tokens.Count)
            return false;

        var observed = assetIds.ToHashSet(StringComparer.Ordinal);
        return observed.Count == session.Tokens.Count
            && session.Tokens.All(token => observed.Contains(token.TokenId.Value));
    }
}
