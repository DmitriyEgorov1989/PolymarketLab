# Agent Context

Этот документ хранит project-specific знания, которые слишком подробны для operational contract в `AGENTS.md`. Код остаётся источником истины; при расхождении сначала проверь текущую реализацию.

## Структура И Wiring

- Единственный executable host - `PolymarketLab.Api/Program.cs`. Папки `/src/...` в `PolymarketLab.slnx` виртуальные; физической директории `src` нет.
- Все .NET проекты используют `net10.0`. `global.json`, NuGet lock и repo-local tool manifest отсутствуют. Frontend имеет `package-lock.json`, поэтому setup использует `npm ci`.
- `PolymarketLab.Markets.Core` содержит Domain, Application и Ports. Application DI сканирует MediatR handlers и FluentValidation validators; общий validation pipeline возвращает ожидаемые ошибки как `ErrorList`.
- `Program.cs` подключает Markets и DataCollection Application, Infrastructure и Presentation. Не меняй порядок hosted services и DI-регистраций collector runtime без проверки lifecycle.
- Framework возвращает `Envelope`, включая model binding, unknown routes и неожиданные ошибки. Exception handler скрывает exception details; Problem Details не является основным контрактом.

## Markets

- Инварианты находятся в Domain, orchestration - в MediatR handlers, persistence и внешние интеграции - в Infrastructure.
- Value objects и entities с инвариантами создаются через фабрики; приватные пустые конструкторы существуют для EF Core.
- Ports/domain возвращают `Result<T, Error>` или `UnitResult<Error>`; command/controller boundary использует `Result<T, ErrorList>`.
- Повторная регистрация того же рынка успешна: тот же ID и `Created = false`. Новая запись возвращает `Created = true`.
- Расширяй существующий parser/gateway/repository flow, не создавай параллельную реализацию.
- Identity рынка защищают отдельные constraints для event/market slugs и IDs, `condition_id` и глобального `external_token_id`. Только эти PostgreSQL `23505` преобразуются в `MarketInsertStatus.UniqueConflict`; scoped token conflicts и остальные database errors не маскируются.
- Repository queries используют `AsNoTracking()` и загружают `Tokens`. Aggregate нельзя возвращать частично материализованным.
- Регистрация автоматически гарантирует durable collector job; публичный `POST /api/Collector` остаётся compatibility/administrative endpoint. Live-проверки Gamma выполняются при регистрации и на lifecycle boundaries; внешние даты не заменяют status flags Gamma.
- Market раздельно хранит event identity (`ExternalEventId`, `EventSlug`) и child market identity (`ExternalMarketId`, `MarketSlug`, `ConditionId`, ordered tokens). Schedule не входит в identity.
- `DiscoveredAt` неизменяем, а повторная exact registration обновляет внешнее расписание и `ScheduleRefreshedAt`. `EventStartsAt` берётся только из Gamma `eventStartTime`, не из `startDate`.
- Future market регистрируется без требований `active` и `acceptingOrders`, если order book включён и рынок ещё не закрыт или разрешён. Exact existing market остаётся идемпотентным независимо от текущего terminal state.
- Migration `PersistEventIdentityAndSchedule` намеренно требует пустую таблицу `markets`; старые identity и точное расписание не backfill-ятся вымышленными значениями.

Фактический frontend HTTP-контракт описан в controllers/DTO и отражён в `docs/frontend-api-contract.md`.

## Data Collection

- `CollectorController` публикует read/start/stop endpoints. Infrastructure регистрирует `DataCollectionDbContext`, repositories, singleton collector runtime и bounded raw-message ingestion worker.
- WebSocket collector принимает text messages, собирает fragments и передаёт полные исходные UTF-8 bytes в bounded ingestion pipeline. Silent drop недопустим.
- Active slot существует только per-market. Разные рынки обрабатываются параллельно без global capacity limit или admission queue, хотя WebSocket connections, ingestion channel, consumer, память и PostgreSQL остаются общими конечными ресурсами. `BoundedChannelFullMode.Wait` создаёт backpressure без silent drop.
- Каждый raw message сохраняет монотонную connection epoch. Durable progress атомарно хранит current epoch и received/enqueued/persisted counters, а raw count вычисляется авторитетно из PostgreSQL.
- Lifecycle scheduler раз в секунду обрабатывает все сохранённые active sessions независимо: до `T-60s` каждая остаётся `Scheduled`, затем exact Gamma boundary check и CAS запускают preparation; временные ошибки повторяются до `T = EventStartsAt`, включая период после `T-10s`. Readiness принимается только строго до `T` после durable enqueue initial `book` каждого snapshot token и matching `PONG` текущей connection epoch; connect/subscription недостаточны. Missed window завершается failure и cleanup.
- Future `Scheduled` переживает restart и продолжает обрабатываться scheduler без браузера. Ручной Stop, ошибка runtime, штатная остановка host и active partial sessions предыдущего процесса проходят через общий coordinator в `Invalidating/Cleaning`. `InvalidatingAt` является долговечным write fence и сохраняется при последующем переходе в `Failed`; начатые partial sessions не возобновляются.
- Переходы `CollectorSession` сохраняются compare-and-set по ожидаемому `Status`; `status` является EF concurrency token. При конфликте перечитай состояние и разреши переход, не выполняй unconditional update.
- Автономная ошибка collector переводит сохранённую активную session в `Invalidating/Cleaning`; ошибка сохранения этого перехода останавливает приложение.
- После `EventEndsAt` отдельный strict observer сканирует уже сохранённые raw `market_resolved`, проверяет current connection epoch и immutable session snapshot, а Gamma/CLOB polling выполняется без overlap каждые 2 секунды до общего срока `EventEndsAt + 5m`. Consensus требует одного winner от всех трёх sources; безопасные observations и raw provenance сохраняются отдельно от permissive archive normalizer.
- После durable consensus дальнейшие Gamma/CLOB polling и WebSocket scanning не выполняются: coordinator сразу передаёт session в `CollectorRawDatasetCompletionCoordinator`. Он выполняет controlled drain: CAS `Running/AwaitingResolution -> Stopping/DrainingRaw`, останавливает producer, ждёт сохранения хвоста до final enqueued boundary, записывает final checkpoint и одним PostgreSQL read проверяет точное равенство `received = enqueued = persisted = raw count > 0`; только после него session CAS-переходит в `Stopping/AwaitingNormalization`. Ошибка stop, drain, checkpoint, equality read или state transition ведёт в durable invalidation с `PersistenceFailure` и возвращается вызывающей стороне как failure. Успешный drain не закрывает singleton `RawMarketMessageChannel`; `CompleteProducers()` остаётся механизмом только host shutdown.
- После durable raw equality session сохраняет `AwaitingNormalizationAt` и остаётся `Stopping/AwaitingNormalization` максимум до `AwaitingNormalizationAt + 5m`. Один PostgreSQL statement доказывает exact Processed cardinality snapshot `ProjectionVersion` и strict WS `market_resolved` provenance. `Pending`, `Processing` и missing snapshot rows ожидаются до deadline; `Invalid`, `Unsupported`, terminal `Failed`, runtime version mismatch, legacy null version, provenance mismatch и timeout запускают durable invalidation через `ICollectorSessionInvalidationCoordinator`. Только успешный gate до deadline CAS-выполняет `Stopped/MarketClosed`; empty root array может быть `Processed` с нулём normalized events, а дополнительные ledger/projection rows других версий не влияют на доказательство snapshot-версии.
- Startup reconciliation до запуска ingestion, normalizer и HTTP API сохраняет future `Scheduled`, но переводит active partial session предыдущего процесса в invalidation, атомарно удаляет её normalized events всех версий, normalization ledger и raw rows, сохраняет cleanup audit и завершает как `Failed`; collection начатой session не возобновляется. Ошибка recovery останавливает startup. Механизм рассчитан на один экземпляр приложения; multi-instance ownership и reconnect не реализованы.
- В работающем процессе `CollectorScheduler` также обрабатывает `Invalidating`: повторяет запрет запуска, ожидает завершения сборщика и вызывает ту же атомарную очистку. Запись заблокированного сборщика остаётся в реестре до `Completion`, даже после тайм-аута остановки; только подтверждённое завершение разрешает очистку и освобождение per-market slot. Ошибка прохода планировщика журналируется и повторяется в новой области зависимостей; отмена при завершении приложения пробрасывается.
- Cancel `Scheduled`/`Starting`/`Running` долговечен и очищает неполный dataset; успешный `Stopped/MarketClosed` dataset не изменяется. Явное повторное добавление до `T` создаёт новую попытку после cancel или pre-window failure, а успешный существующий результат переиспользуется и после окна. Старые Markets без job автоматически не включаются.
- Session snapshot, включая расписание, неизменяем. Schedule mismatch ведёт в invalidation/cleanup; явное повторное добавление после cleanup принимает обновлённое расписание.
- До изменения runtime, ingestion, ownership буферов или hosted-service ordering прочитай `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/README.md`.
- До изменения normalizer прочитай `docs/normalizer-input-contract.md` и root `README.md`. Raw payload не логируй и не включай в agent context без необходимости.
- Fixtures в `PolymarketLab.DataCollection.Infrastructure.Tests/Fixtures/Polymarket` имеют зафиксированные SHA-256 и помечены `-text`; не форматируй их и не меняй line endings.

## Локальное Окружение

- Connection string задаётся через API User Secrets или `Database__ConnectionString`; значения нет в `appsettings`.
- PostgreSQL из Compose опубликован на host port `5433`. Перед сменой порта проверь занятость.
- Приложение не вызывает `Migrate()` или `EnsureCreated()` автоматически. Миграции применяются отдельно для `MarketsDbContext` и `DataCollectionDbContext` командами из root `README.md`.
- HTTP profile: `http://localhost:5285`. Swagger `/swagger` и OpenAPI `/openapi/v1.json` доступны только в Development.
- Полные integration tests используют `Testcontainers.PostgreSql` и требуют Docker. Gamma tests используют stub `HttpMessageHandler`.

## Известные Ограничения

- Mapping новых `ErrorType` нужно добавлять в `ResponseExtensions`, иначе неизвестный тип может стать HTTP 500.
- Миграции не применяются автоматически. Есть deterministic host acceptance с PostgreSQL и transport fakes; автоматического live Polymarket end-to-end suite нет.
- Первый registration flow принимает только single-market events; multi-market
  events отклоняются без неявного выбора дочернего market.
- Reconnect после readiness, multi-instance session ownership и resume начатой partial session отсутствуют; future `Scheduled` восстанавливается scheduler.
