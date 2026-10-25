using System.Text.Json;
using PolymarketLab.DataCollection.Core.Application.Simulation.Datasets;
using Session = PolymarketLab.DataCollection.Core.Domain.Models.CollectorSession.CollectorSession;

namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Simulation;

/// <summary>Проверяет сохранённые ссылки подтверждения, не вызывая внешние API или coordinators записи.</summary>
internal static class ExperimentDatasetEvidence
{
    internal static DatasetKnowledge ReadResolution(
        string evidence, Session session, bool rawItemProcessed, IEnumerable<ExperimentProjectionItem> projection)
    {
        using var json = JsonDocument.Parse(evidence);
        var root = json.RootElement;
        var state = root.GetProperty("state");
        var observations = root.GetProperty("observations").EnumerateArray().ToArray();
        if (observations.Any(item => item.GetProperty("observation").GetProperty("status").GetInt32() == 4))
            return DatasetKnowledge.Conflicting;
        if (observations.Select(item => item.GetProperty("observation"))
            .Where(item => item.GetProperty("status").GetInt32() == 2)
            .Select(item => (item.GetProperty("winner_token_id").GetString(), item.GetProperty("winner_outcome").GetString()))
            .Distinct().Count() > 1)
            return DatasetKnowledge.Conflicting;
        if (session.ResolutionConfirmedAt is null)
            return session.WinningTokenId is null && session.WinningOutcome is null
                && (state.ValueKind == JsonValueKind.Null || state.GetProperty("confirmed_at").ValueKind == JsonValueKind.Null)
                ? DatasetKnowledge.Missing : DatasetKnowledge.Conflicting;
        if (!rawItemProcessed || state.ValueKind == JsonValueKind.Null
            || state.GetProperty("confirmed_at").ValueKind == JsonValueKind.Null
            || state.GetProperty("confirmed_at").GetDateTimeOffset() != session.ResolutionConfirmedAt
            || !session.Tokens.Any(token => token.TokenId.Value == session.WinningTokenId && token.Outcome == session.WinningOutcome))
            return DatasetKnowledge.Conflicting;

        var primary = state.GetProperty("primary_observation_id");
        var confirming = state.GetProperty("confirming_observation_id");
        if (primary.ValueKind == JsonValueKind.Null || confirming.ValueKind == JsonValueKind.Null)
            return DatasetKnowledge.Conflicting;
        var gamma = observations.Where(item => item.GetProperty("observation").GetProperty("id").GetInt64() == primary.GetInt64()).ToArray();
        var clob = observations.Where(item => item.GetProperty("observation").GetProperty("id").GetInt64() == confirming.GetInt64()).ToArray();
        if (gamma.Length != 1 || clob.Length != 1 || !Matches(gamma[0], 1) || !Matches(clob[0], 2))
            return DatasetKnowledge.Conflicting;
        var webSocketItems = observations.Select(item => item.GetProperty("observation"))
            .Where(item => item.GetProperty("source").GetInt32() == 0
                && item.GetProperty("status").GetInt32() == 2
                && item.GetProperty("observed_at").GetDateTimeOffset() == session.ResolutionSignaledAt
                && item.GetProperty("connection_epoch").ValueKind == JsonValueKind.Number
                && item.GetProperty("connection_epoch").GetInt64() == session.ResolutionConnectionEpoch)
            .ToArray();
        var typedSignalMatches = projection.Where(item => item.EventType == "market_resolved").Any(item =>
        {
            using var typed = JsonDocument.Parse(item.CanonicalContent);
            var records = typed.RootElement.GetProperty("market_resolutions");
            return records.GetArrayLength() == 1
                && records[0].GetProperty("external_market_id").GetString() == session.ExternalMarketId
                && records[0].GetProperty("winning_asset_id").GetString() == session.WinningTokenId
                && records[0].GetProperty("winning_outcome").GetString() == session.WinningOutcome
                && webSocketItems.Any(observation => observation.GetProperty("raw_message_id").ValueKind == JsonValueKind.Number
                    && observation.GetProperty("raw_message_id").GetInt64() == item.RawMessageId
                    && observation.GetProperty("raw_item_index").GetInt32() == item.RawItemIndex);
        });
        if (!typedSignalMatches)
            return DatasetKnowledge.Conflicting;
        if (observations.Any(item =>
            item.GetProperty("observation").GetProperty("status").GetInt32() == 2
            && (item.GetProperty("observation").GetProperty("winner_token_id").GetString() != session.WinningTokenId
                || item.GetProperty("observation").GetProperty("winner_outcome").GetString() != session.WinningOutcome)))
            return DatasetKnowledge.Conflicting;
        return DatasetKnowledge.Available;

        bool Matches(JsonElement item, int source)
        {
            var observation = item.GetProperty("observation");
            if (observation.GetProperty("source").GetInt32() != source
                || observation.GetProperty("status").GetInt32() != 2
                || observation.GetProperty("condition_id").GetString() != session.ConditionId
                || observation.GetProperty("winner_token_id").GetString() != session.WinningTokenId
                || observation.GetProperty("winner_outcome").GetString() != session.WinningOutcome
                || observation.GetProperty("closed").ValueKind != JsonValueKind.True
                || observation.GetProperty("accepting_orders").ValueKind != JsonValueKind.False
                || observation.GetProperty("observed_at").GetDateTimeOffset() > session.ResolutionConfirmedAt)
                return false;
            if (source == 1 && (observation.GetProperty("external_event_id").GetString() != session.ExternalEventId
                || observation.GetProperty("event_slug").GetString() != session.EventSlug
                || observation.GetProperty("external_market_id").GetString() != session.ExternalMarketId
                || observation.GetProperty("market_slug").GetString() != session.MarketSlug))
                return false;
            var outcomes = item.GetProperty("outcomes").EnumerateArray().ToArray();
            return outcomes.Length == session.Tokens.Count
                && outcomes.Select(outcome => outcome.GetProperty("token_id").GetString())
                    .SequenceEqual(session.Tokens.Select(token => token.TokenId.Value))
                && outcomes.Select(outcome => outcome.GetProperty("outcome").GetString())
                    .SequenceEqual(session.Tokens.Select(token => token.Outcome))
                && outcomes.Count(outcome => outcome.GetProperty("is_winner").GetBoolean()) == 1
                && outcomes.Single(outcome => outcome.GetProperty("is_winner").GetBoolean())
                    .GetProperty("token_id").GetString() == session.WinningTokenId;
        }
    }
}
