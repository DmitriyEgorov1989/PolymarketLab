# Normalizer input contract

This document records the observed Polymarket market-channel payload contract before
the Normalizer implementation. The archive was inspected read-only on 2026-08-10.

## Raw source

The source of truth is `data_collection.raw_market_messages`:

```text
id          bigint
session_id  uuid
received_at timestamptz
payload     bytea
```

The collector stores one complete text WebSocket message as UTF-8 bytes. It does not
validate JSON or extract event metadata.

## Archive inventory

The inspected archive contained 189,599 rows with IDs 1 through 189,599. All rows
were valid UTF-8 JSON.

| Root/event type | Logical events | Raw messages |
| --- | ---: | ---: |
| `price_change` | 170,234 | 170,234 |
| `best_bid_ask` | 8,450 | 8,450 |
| `book` | 7,214 | 7,209 |
| `last_trade_price` | 3,456 | 3,456 |
| `new_market` | 244 | 244 |
| `tick_size_change` | 4 | 4 |
| `market_resolved` | 1 | 1 |

There were 189,593 root objects and six root arrays. Five arrays contained two
`book` events; one array was empty. A raw row therefore cannot be modeled as exactly
one logical event. The Normalizer must assign a stable zero-based `raw_item_index`.

## Observed representation

- All event timestamps are decimal epoch-millisecond strings.
- Market identifiers, asset identifiers and external IDs are JSON strings.
- Prices, sizes, tick sizes, spreads, fee values and sports lines are JSON strings.
- `price_change` always contained two items in this archive.
- `book.bids` and `book.asks` are arrays and may be empty.
- `book.tick_size` and `book.last_trade_price` appeared only in initial array messages.
- `market_resolved.event_message` was explicitly `null` in the only observed event.
- Empty optional values in `new_market` can be empty strings rather than `null`.
- Unknown additional properties must not invalidate a supported event.

### Field presence profile

The archive was profiled again read-only on 2026-08-11 before implementing the
remaining normalizers.

- all 8,450 `best_bid_ask` events contained non-empty string values for
  `best_bid`, `best_ask` and `spread`; bids ranged from `0` to `0.999`, asks from
  `0.001` to `1`, and spreads from `0.001` to `1`;
- all 244 `new_market` events contained the confirmed scalar fields,
  `assets_ids`, `outcomes`, `event_message` and `fee_schedule` with consistent
  JSON types;
- every `new_market` event contained two asset IDs and two outcomes, with no
  length mismatches, null items or empty items;
- all confirmed `event_message` and `fee_schedule` fields were present and
  non-null; optional external string values were represented by empty strings
  for `sports_market_type`, `line`, `game_start_time` and `group_item_title`.

Observed financial ranges are evidence, not validation limits. Prices used up to
three fractional digits, trade size up to six, and book/change size up to six integer
digits and two fractional digits. Persistence precision must retain at least these
values without using `double` or `float`.

## Fixture provenance

Fixtures under
`PolymarketLab.DataCollection.Infrastructure.Tests/Fixtures/Polymarket` come from
the following archived rows:

| Fixture | Raw ID |
| --- | ---: |
| `book-array.json` | 1 |
| `new-market.json` | 35,404 |
| `last-trade-price.json` | 86,176 |
| `book.json` | 122,174 |
| `empty-array.json` | 155,006 |
| `tick-size-change.json` | 189,350 |
| `best-bid-ask.json` | 189,496 |
| `price-change.json` | 189,509 |
| `market-resolved.json` | 189,599 |

Contract tests verify source SHA-256 hashes. Repository text files have a final line
feed; the fixture loader removes that repository terminator for source object rows.
The two archived array messages already contained a final line feed.

## Decisions for implementation

- Keep `raw_market_messages` immutable and parse only committed rows.
- Use one global `projection_version` for a consistent rebuild.
- Keep previous projection versions instead of overwriting them.
- Store processing state outside the raw table by `(raw_message_id, projection_version)`.
- Store one common normalized event header per logical event.
- Use `(raw_message_id, raw_item_index, projection_version)` as its idempotency key.
- Store event-specific data in typed tables linked to the common header.
- Treat malformed supported events as `Invalid` and unknown event types as
  `Unsupported`; neither outcome stops later raw rows.
- Support all seven observed event types.
