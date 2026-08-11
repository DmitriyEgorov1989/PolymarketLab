# Normalizer: порядок реализации

## Назначение

Документ фиксирует последовательность небольших задач для реализации Normalizer
поверх `data_collection.raw_market_messages`.

Основные правила:

- raw-архив является единственным источником истины;
- Normalizer читает только сохранённые raw-сообщения из PostgreSQL;
- Collector не разбирает бизнес-содержимое сообщений;
- normalized tables являются полностью перестраиваемой проекцией;
- предыдущие версии проекции не удаляются автоматически;
- ошибка одного payload не блокирует последующие сообщения;
- OrderBook Projector не входит в Normalizer.

Фактический входной контракт описан в
[`normalizer-input-contract.md`](normalizer-input-contract.md).

## Текущее состояние

### Выполнено: реальные contract fixtures

- проанализированы 189 599 raw-сообщений;
- подтверждены все семь `event_type`;
- подтверждены корневые JSON object и array;
- добавлены fixtures и проверки SHA-256;
- зафиксированы фактические поля и числовые форматы.

### Выполнено: базовые Core-модели

- `NormalizationStatus`;
- `NormalizationOutcome`;
- `NormalizationIssue`;
- `RawMessageEnvelope`;
- `NormalizedRecord`;
- `NormalizedEvent`;
- `NormalizationResult`.

## Оставшиеся задачи

Каждая задача должна завершаться узкими тестами, полным `dotnet test` и успешной
сборкой solution. Не объединять несколько следующих задач в один большой этап без
необходимости.

## 1. Decoder raw payload

Реализовать infrastructure-компонент, который преобразует bytes одного
`RawMessageEnvelope` в последовательность logical JSON events.

Поддержать:

- корневой object как item с индексом `0`;
- корневой array с устойчивыми индексами элементов;
- пустой array;
- malformed UTF-8;
- malformed JSON;
- JSON scalar вместо object/array;
- object без `event_type`.

Decoder не должен знать схемы `book`, `price_change` и других событий.

Definition of Done:

- реальные `book-array.json` и `empty-array.json` проходят decoder;
- дополнительное JSON-поле не влияет на результат;
- ошибки возвращаются как `NormalizationIssue`, а не как необработанные parser
  exceptions;
- Core не получает зависимость от EF Core или Npgsql.

Узкие тесты:

```powershell
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter "FullyQualifiedName~RawMessageDecoderTests"
```

## 2. Dispatcher normalizer

Добавить контракты:

```text
IRawMessageNormalizer
INormalizationDispatcher
```

Dispatcher получает logical event и выбирает normalizer по точному значению
`event_type`.

Правила:

- неизвестный тип возвращает `Unsupported`;
- отсутствующий или пустой `event_type` возвращает `Invalid`;
- сравнение типов регистрозависимое;
- два normalizer для одного типа приводят к fail-fast при построении dispatcher;
- один большой `switch` по всем типам не используется.

Definition of Done:

- routing не зависит от DI-контейнера;
- duplicate registration обнаруживается до обработки сообщений;
- поддерживаемые типы добавляются отдельными реализациями интерфейса.

## 3. Общие Polymarket parsing helpers

Добавить минимальные infrastructure helpers для внешнего JSON-контракта:

- чтение обязательной строки;
- чтение optional строки и `null`;
- parsing `decimal` через invariant culture;
- parsing epoch milliseconds;
- parsing `BUY` и `SELL`;
- формирование структурированных ошибок с именем поля.

Не создавать универсальный framework parsing и не использовать
`Dictionary<string, object>`.

Definition of Done:

- финансовые значения не проходят через `double` или `float`;
- пустая строка не превращается в `0` или дату автоматически;
- неизвестные JSON-поля игнорируются;
- ошибки не содержат полный raw payload.

## 4. `last_trade_price` normalizer

Реализовать первым как минимальный типизированный vertical slice без persistence.

Добавить:

- внешний DTO или targeted JSON parser;
- `TradeSide`;
- `LastTradeRecord : NormalizedRecord`;
- `LastTradePriceNormalizer` версии `1`;
- unit-тесты на реальном fixture.

Проверить:

- `asset_id` и `market` обязательны;
- `price` находится в диапазоне от `0` до `1`;
- `size` неотрицательный;
- `side` равен `BUY` или `SELL`;
- timestamp является epoch milliseconds;
- `fee_rate_bps` и `transaction_hash` соответствуют фактическому контракту.

Definition of Done:

- fixture `last-trade-price.json` создаёт один `LastTradeRecord`;
- malformed supported payload возвращает `Invalid`;
- normalizer не вызывает PostgreSQL и не использует EF entity.

## 5. `price_change` normalizer

Добавить `PriceChangeRecord` и normalizer версии `1`.

Один logical event создаёт N records по `price_changes[]`. В каждом record нужно
сохранить `itemIndex` внутри массива изменений.

Definition of Done:

- реальный fixture создаёт ровно два records;
- порядок элементов сохраняется;
- пустой массив имеет явно определённый результат;
- ошибка одного элемента делает весь logical event `Invalid` без частичного
  результата;
- поддержаны `hash`, `best_bid` и `best_ask`.

## 6. `book` normalizer

Добавить:

- `BookSnapshotRecord`;
- `BookLevelRecord`;
- `OrderBookSide`;
- `BookNormalizer` версии `1`.

Normalizer создаёт snapshot и отдельные уровни, но не строит текущий стакан.

Definition of Done:

- поддержаны пустые `bids` и `asks`;
- сохраняются `side` и исходный `levelIndex`;
- уровни не сортируются silently;
- optional `tick_size` и `last_trade_price` поддержаны для initial array;
- fixture object и оба события из fixture array нормализуются.

## 7. `tick_size_change` normalizer

Добавить `TickSizeChangeRecord` и normalizer версии `1`.

Definition of Done:

- `old_tick_size` и `new_tick_size` читаются как `decimal`;
- новый tick size положительный;
- normalizer работает на реальном fixture;
- неверный контракт возвращает `Invalid`.

## 8. `best_bid_ask` normalizer

Добавить `BestBidAskRecord` и normalizer версии `1`.

Definition of Done:

- `best_bid`, `best_ask` и `spread` читаются без потери точности;
- допустимость отсутствующих сторон определяется контрактом, а не предположением;
- значения `0` и `1` не считаются отсутствующими;
- реальный fixture проходит normalizer.

## 9. `new_market` normalizer

Добавить внутренние typed records только для подтверждённых fixture полей.

Отдельно сохранить упорядоченное соответствие:

```text
asset id → outcome
```

Не выполнять lookup в `MarketsDbContext` и не добавлять межмодульный запрос на каждый
event.

Definition of Done:

- пустые строки external optional fields не приводят к parser exception;
- порядок `assets_ids` и `outcomes` сохраняется;
- несовпадение размеров массивов имеет явный результат validation;
- сложные `event_message` и `fee_schedule` разбираются только в подтверждённом
  объёме;
- неизвестные поля не ломают normalizer.

## 10. `market_resolved` normalizer

Добавить `MarketResolvedRecord` и normalizer версии `1`.

Definition of Done:

- сохраняются external market id, список assets, winner asset и winner outcome;
- `event_message: null` поддерживается;
- реальный единственный fixture проходит normalizer;
- отсутствие обязательного winner возвращает `Invalid`, если контракт не будет
  уточнён новым fixture.

## 11. Регистрация normalizer в Core DI

Зарегистрировать семь normalizer и dispatcher.

Definition of Done:

- DI validation проходит;
- lifetime не создаёт captive dependencies;
- duplicate event type приводит к fail-fast;
- registration tests подтверждают все семь типов.

Background worker и repositories на этом этапе не подключать.

## 12. EF-модель processing ledger

Добавить internal EF record для таблицы:

```text
data_collection.raw_message_normalizations
```

Минимальные поля:

```text
raw_message_id
projection_version
status
attempt_count
claimed_at
completed_at
error_code
error_message
```

Ключ:

```text
PRIMARY KEY (raw_message_id, projection_version)
```

Индекс pending/processing:

```text
(projection_version, status, raw_message_id)
```

Definition of Done:

- есть FK к `raw_market_messages`;
- статусы не добавляются непосредственно в immutable raw row;
- model metadata tests проверяют имена колонок, ключ и индекс;
- migration пока не создаётся.

## 13. EF-модель общего event header

Добавить таблицу:

```text
data_collection.normalized_events
```

Основные поля:

```text
id
raw_message_id
raw_item_index
projection_version
normalizer_version
event_type
session_id
received_at
source_timestamp
market_condition_id
asset_id
normalized_at
```

Идемпотентный ключ:

```text
UNIQUE (raw_message_id, raw_item_index, projection_version)
```

Definition of Done:

- header поддерживает object и array raw payload;
- несколько projection versions могут существовать одновременно;
- model metadata tests проходят.

## 14. Typed EF-таблица `last_trade_price`

Добавить typed table с PK/FK на `normalized_events.id`.

Definition of Done:

- финансовые значения имеют явный PostgreSQL numeric precision;
- side хранится как стабильное enum value;
- transaction hash не ограничен случайной короткой длиной;
- model tests проверяют mapping.

## 15. Typed EF-таблица `price_change`

Добавить дочерние строки с ключом:

```text
UNIQUE (event_id, item_index)
```

Definition of Done:

- один event header связан с N changes;
- есть индекс для будущих запросов по asset и source timestamp;
- model tests проверяют cardinality и constraints.

## 16. Typed EF-таблицы `book`

Добавить snapshot и levels.

Ключ уровней:

```text
UNIQUE (event_id, side, level_index)
```

Definition of Done:

- удаление projection version корректно удаляет дочерние rebuildable rows;
- FK и delete behavior заданы явно;
- model tests проверяют пустые и непустые стороны на уровне writer tests позже.

## 17. Остальные typed EF-таблицы

Добавлять отдельными маленькими подзадачами:

```text
tick_size_changes
best_bid_asks
new_markets и дочерние token/outcome rows
market_resolutions и дочерние asset rows
```

Definition of Done каждой подзадачи:

- PK/FK на общий event header;
- отсутствует дублирование header columns без причины;
- порядок внешних массивов сохраняется индексом;
- model metadata tests проходят.

## 18. Migration полной normalization schema

Создать migration только после стабилизации всей EF-модели.

Definition of Done:

- migration создаёт ledger, header и typed tables;
- `raw_market_messages.payload` не изменяется;
- `Down` удаляет только normalization projection;
- snapshot соответствует модели;
- migration проверена на реальном PostgreSQL.

## 19. PostgreSQL integration test infrastructure

Отдельной задачей добавить Testcontainers PostgreSQL.

Definition of Done:

- тесты применяют реальные migrations;
- тесты не требуют заранее запущенную локальную БД;
- PostgreSQL constraints и concurrency проверяются фактически;
- добавленная зависимость ограничена Infrastructure.Tests.

## 20. Claim repository

Реализовать безопасный выбор raw-сообщений для конкретной
`projection_version`.

Использовать короткий claim с состоянием `Processing` и lease/recovery semantics.
Не держать длительную транзакцию во время JSON parsing всего batch.

Definition of Done:

- выборка упорядочена по raw ID;
- уже terminal rows выбранной версии пропускаются;
- новая projection version снова видит тот же raw archive;
- два processor не получают одну запись одновременно;
- stale claim может быть восстановлен;
- batch limit проверяется PostgreSQL integration tests.

## 21. Versioned normalized writer

Реализовать запись общего header, typed rows и terminal processing status в одной
транзакции.

Definition of Done:

- `Processed` записывается только после normalized rows;
- повторная запись той же версии не создаёт дубликаты;
- v1 и v2 могут существовать одновременно;
- `Invalid` и `Unsupported` сохраняются без typed rows;
- database failure откатывает весь результат raw-сообщения;
- исходный payload не изменяется.

## 22. Ручной batch processor

Добавить scoped application service:

```text
INormalizationProcessor.ProcessBatchAsync
```

До появления background worker processor должен запускаться тестом или временным
administrative entry point.

Definition of Done:

- valid payload становится `Processed`;
- invalid payload между двумя valid не блокирует третий;
- неизвестный event становится `Unsupported`;
- пустой array получает согласованный terminal status без normalized events;
- mixed array сохраняет результаты каждого item атомарно;
- cancellation распространяется;
- batch summary содержит counts по outcome.

## 23. Полные PostgreSQL vertical-slice tests

Проверить реальные цепочки:

```text
raw last_trade_price → header → typed row → Processed
raw price_change → header → N typed rows → Processed
raw book → header → snapshot + levels → Processed
```

Затем добавить остальные четыре типа.

Обязательные сценарии:

- повторная обработка той же версии;
- одновременное хранение v1 и v2;
- invalid payload;
- unsupported event;
- transaction rollback;
- конкурентный claim;
- root array;
- пустой root array.

## 24. Infrastructure DI без background worker

Подключить repositories, writer и processor к существующему DataCollection DI.

Definition of Done:

- repositories и processor имеют scoped lifetime;
- parser/normalizer не зависят от `DbContext`;
- provider проходит scope validation;
- существующий raw ingestion lifecycle не изменён.

## 25. Настройки continuous Normalizer

Добавить validated options:

```text
Enabled
ProjectionVersion
BatchSize
IdleDelay
ClaimTimeout
```

Definition of Done:

- небезопасные значения отклоняются при startup;
- worker можно полностью отключить;
- defaults не создают busy loop.

## 26. Background worker

Добавить scheduler поверх `INormalizationProcessor`.

Worker не содержит parsing или persistence logic.

Definition of Done:

- worker читает только PostgreSQL, не ingestion channel;
- idle batch использует configurable delay;
- cancellation и graceful shutdown работают;
- exception не создаёт hot loop;
- ошибка Normalizer не останавливает Collector автоматически.

## 27. Structured logging

Логировать summary batch:

```text
ProjectionVersion
BatchSize
FirstRawMessageId
LastRawMessageId
Processed
Invalid
Unsupported
Failed
DurationMs
```

Для ошибки сообщения логировать ID, session, item index, event type, version и error
code. Полный payload по умолчанию не логировать.

## 28. Метрики

Добавить отдельный meter Normalizer:

```text
normalizer_messages_processed_total
normalizer_messages_invalid_total
normalizer_messages_unsupported_total
normalizer_messages_failed_total
normalizer_batches_total
normalizer_batch_duration_ms
normalizer_pending_messages
normalizer_lag_messages
```

Definition of Done:

- lag считается для конкретной projection version;
- metric tags имеют ограниченную cardinality;
- raw message ID, asset ID и текст ошибки не используются как metric tags.

## 29. Replay use case

Добавить административный application use case для повторной нормализации:

```text
by session
by event type
by projection version
```

Definition of Done:

- replay не изменяет raw payload;
- новая версия не удаляет предыдущую;
- повтор команды идемпотентен;
- фильтры покрыты integration tests;
- массовый replay не блокирует live collection.

## 30. Документация эксплуатации

Обновить README:

- параметры Normalizer;
- применение migration;
- включение worker;
- просмотр backlog и ошибок;
- запуск replay;
- интерпретация lag metrics;
- восстановление stale claims.

## Общие проверки после каждой задачи

Сначала запускать самый узкий тест, затем:

```powershell
dotnet test .\PolymarketLab.slnx
dotnet build .\PolymarketLab.slnx
git diff --check
git diff
```

Если задача изменяет EF model или migrations, дополнительно запускать PostgreSQL
integration tests после появления соответствующей test infrastructure.

## Что не входит в этот план

- построение текущего `OrderBookState`;
- REST resync стакана;
- candles и indicators;
- trading и order placement;
- стратегии и backtesting;
- изменение Collector WebSocket pipeline;
- удаление raw archive.

После завершения этого плана отдельным документом проектируется OrderBook Projector.
