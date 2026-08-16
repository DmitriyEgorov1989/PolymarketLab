# Polymarket contract fixtures

Файлы в этой папке взяты из сохранённого архива
`data_collection.raw_market_messages`, исследованного без изменений
2026-08-10. Они представляют market-channel сообщения для семи поддерживаемых
`event_type`, корневого массива `book` и наблюдавшегося пустого массива.

Точные исходные `raw_message_id`, профиль архива и правила сохранения завершающего
перевода строки описаны в [`docs/normalizer-input-contract.md`](../../../docs/normalizer-input-contract.md#fixture-provenance).

`PolymarketContractFixtureTests` фиксирует SHA-256 исходных payload. Не изменяй
fixtures без одновременной проверки происхождения, контракта и ожидаемого hash.
