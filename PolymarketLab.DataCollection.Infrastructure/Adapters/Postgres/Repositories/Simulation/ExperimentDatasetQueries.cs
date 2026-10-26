namespace PolymarketLab.DataCollection.Infrastructure.Adapters.Postgres.Repositories.Simulation;

/// <summary>Только читающие запросы архивного снимка; имена таблиц заданы кодом, а не входом.</summary>
internal static class ExperimentDatasetQueries
{
    internal static readonly (string Table, string Order, string EventType)[] TypedTables =
    [
        ("book_snapshots", "event_id", "book"),
        ("book_levels", "side, level_index, id", "book"),
        ("price_change", "item_index, id", "price_change"),
        ("tick_size_changes", "event_id", "tick_size_change"),
        ("best_bid_asks", "event_id", "best_bid_ask"),
        ("last_trade_price", "event_id", "last_trade_price"),
        ("new_markets", "event_id", "new_market"),
        ("new_market_assets", "item_index, id", "new_market"),
        ("market_resolutions", "event_id", "market_resolved"),
        ("market_resolution_assets", "item_index, id", "market_resolved")
    ];

    internal const string Raw = """
        SELECT raw.id,
            raw.id::text || ':' || raw.connection_epoch::text || ':' ||
            to_char(raw.received_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS.US') || ':' ||
            encode(sha256(raw.payload), 'hex'),
            raw.connection_epoch, raw.received_at, encode(sha256(raw.payload), 'hex')
        FROM data_collection.raw_market_messages AS raw
        WHERE raw.session_id = @session_id
        ORDER BY raw.id
        """;

    internal const string Ledger = """
        SELECT to_jsonb(ledger)::text
        FROM data_collection.raw_message_normalizations AS ledger
        JOIN data_collection.raw_market_messages AS raw ON raw.id = ledger.raw_message_id
        WHERE raw.session_id = @session_id AND ledger.projection_version = @projection_version
        ORDER BY ledger.raw_message_id
        """;

    internal static readonly string Projection = """
        SELECT normalized.raw_message_id, normalized.raw_item_index, normalized.event_type,
            normalized.normalizer_version,
            normalized.session_id = raw.session_id
                AND normalized.received_at = raw.received_at AS provenance_matches,
            ledger.status = 3 AND ledger.completed_at IS NOT NULL AS parent_processed,
            jsonb_build_object('header', to_jsonb(normalized),
        """ + string.Join(",\n", TypedTables.Select(table => $"""
            '{table.Table}', COALESCE((
                SELECT jsonb_agg(to_jsonb(item) ORDER BY {table.Order})
                FROM data_collection.{table.Table} AS item
                WHERE item.event_id = normalized.id
            ), '[]'::jsonb)
            """)) + """
            )::text
        FROM data_collection.normalized_events AS normalized
        JOIN data_collection.raw_market_messages AS raw ON raw.id = normalized.raw_message_id
        LEFT JOIN data_collection.raw_message_normalizations AS ledger
            ON ledger.raw_message_id = raw.id AND ledger.projection_version = @projection_version
        WHERE normalized.projection_version = @projection_version
          AND (raw.session_id = @session_id OR normalized.session_id = @session_id)
        ORDER BY normalized.raw_message_id, normalized.raw_item_index
        """;

    // Включаем сохранённые метаданные сессии целиком: их изменение также меняет манифест.
    internal const string Evidence = """
        SELECT jsonb_build_object(
            'session', to_jsonb(session),
            'state', (SELECT to_jsonb(state) FROM data_collection.resolution_states AS state
                      WHERE state.session_id = session.id),
            'observations', COALESCE((
                SELECT jsonb_agg(jsonb_build_object(
                    'observation', to_jsonb(observation),
                    'outcomes', COALESCE((
                        SELECT jsonb_agg(to_jsonb(outcome_row) ORDER BY outcome_row.outcome_index)
                        FROM data_collection.resolution_observation_outcomes AS outcome_row
                        WHERE outcome_row.observation_id = observation.id
                    ), '[]'::jsonb)
                ) ORDER BY observation.id)
                FROM data_collection.resolution_observations AS observation
                WHERE observation.session_id = session.id
            ), '[]'::jsonb)
        )::text
        FROM data_collection.collector_sessions AS session
        WHERE session.id = @session_id
        """;
}
