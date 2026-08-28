# Первый полный сбор пятиминутного рынка: план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Автоматически собрать один заранее выбранный пятиминутный Polymarket market и признать dataset пригодным только после доказанного `Stopped/MarketResolved`.

**Architecture:** `CollectorSession` хранит неизменяемый снимок рынка и управляет одной глобально эксклюзивной collection session. Runtime заранее готовит WebSocket, контролирует непрерывность, подтверждает resolution через WebSocket, Gamma и CLOB, затем останавливает producer, дожидается raw persistence и normalization. Любое нарушение переводит session в durable invalidation; cleanup атомарно удаляет только её dataset.

**Tech Stack:** .NET 10, ASP.NET Core, MediatR, FluentValidation, EF Core, PostgreSQL, React 19, TypeScript, TanStack Query, xUnit, Testcontainers и Vitest.

**Spec:** GitHub issues [#14](https://github.com/DmitriyEgorov1989/PolymarketLab/issues/14) и [#17](https://github.com/DmitriyEgorov1989/PolymarketLab/issues/17), `docs/agent-context.md`, `docs/frontend-context.md`, `docs/frontend-api-contract.md`.

## Global Constraints

- Backend controllers и DTO имеют приоритет над документацией при расхождении.
- Только `Stopped/MarketResolved` означает пригодный dataset; `Stopped/Requested`, `Failed`, `Interrupted` и legacy sessions непригодны.
- Одновременно допускается одна session в одном из пяти exclusive statuses: `Scheduled`, `Starting`, `Running`, `Stopping`, `Invalidating`.
- Snapshot identity, ordered tokens, `EventStartsAt`, `EventEndsAt` и `ProjectionVersion` неизменяемы после создания session.
- Время проверяется через `TimeProvider`; deterministic tests не используют реальные задержки или live network.
- Expected integration errors сохраняют исходные code/message и не превращаются в исключения.
- Raw payload, credentials, connection strings и stack traces не попадают в HTTP, документацию или evidence.
- Новые migrations создаются и применяются только после отдельного разрешения пользователя; snapshots вручную не редактируются.
- Новые dependencies, изменение публичного HTTP-контракта и commits требуют отдельного разрешения пользователя.
- После каждой issue сначала выполняются узкие tests, затем `dotnet test .\PolymarketLab.slnx`, `dotnet build .\PolymarketLab.slnx` и `git diff --check`; для frontend также выполняются `npm --prefix .\PolymarketLab.Web run test`, `typecheck` и `build`.

---

## Сквозной пример

Во всех примерах используется один условный рынок:

- Event: `btc-updown-5m-1200`, начало `12:00:00 UTC`, конец `12:05:00 UTC`.
- Market: `btc-updown-5m-1200`, `ConditionId=0xabc`.
- Ordered tokens: index `0` = `Yes`, token `1001`; index `1` = `No`, token `1002`.
- До resolution торговые цены могут быть `Yes=0.99`, то есть примерно `99%`, и `No=0.01`, то есть примерно `1%`.
- Terminal settlement обязан быть точным: winner `Yes=1.00`, loser `No=0.00`. Это settlement, а не приблизительная вероятность.
- Успешный dataset содержит, например, `1250` полных WebSocket text messages: `received=1250`, `enqueued=1250`, `persisted=1250`, `raw=1250`, и все `1250` raw rows имеют `Processed` для snapshot `ProjectionVersion`.

## Порядок и зависимости

```text
#30 -> #22 -> #25 -> #28 -> #31 -> #35 -> #29
          |      |      |      |             |
          |      |      |      +-> #33       |
          |      |      +-------------------+ |
          |      +-------------------------+| |
          +-------------------------------+|| |
                                           vvv v
                                            #24 -> #34 -> #26 -> #27 -> #36 -> #23 -> #17
```

Практический порядок реализации: `#30`, `#22`, `#25`, `#28`, `#31`, `#33`, `#35`, `#29`, `#24`, `#34`, `#26`, `#27`, `#36`, `#23`, затем закрытие `#17`. Issue `#32` уже реализована commit `34b9795` и используется в `#24`.

---

### Task 1: #30 Расширить verified Market source для session snapshot

**Зачем это делаем:** До создания session нужно повторно спросить Gamma и доказать, что зарегистрированный market всё ещё тот же самый market. Иначе collector может подписаться на устаревшие token IDs и собрать данные, которые невозможно честно связать с выбранным исходом.

**Успешный пример:** В PostgreSQL и свежем Gamma response совпадают event, market, `ConditionId=0xabc`, расписание и ordered tokens `1001/Yes`, `1002/No`. Source возвращает verified snapshot, даже если в `11:57:00 UTC` future market ещё имеет `acceptingOrders=false`.

**Ожидающий пример:** Эта задача не решает, когда подключать WebSocket. В `11:57:00 UTC` она только подтверждает market; перевод в `Scheduled/WaitingForPreparation` выполнит `#25`.

**Ошибочный пример:** Gamma поменяла второй token с `1002` на `9999` или вернула другой `ConditionId`. Start получает conflict и не создаёт session; collector не смешивает два разных datasets.

**Files:**

- Modify: `PolymarketLab.DataCollection.Core/Ports/IMarketCollectionSource.cs`
- Modify: `PolymarketLab.DataCollection.Core/Ports/Dtos/CollectionMarket.cs`
- Modify: `PolymarketLab.DataCollection.Core/Ports/Dtos/CollectionMarketToken.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/MarketIntegration/MarketCollectionSource.cs`
- Test: `PolymarketLab.DataCollection.Infrastructure.Tests/Adapters/MarketIntegration/MarketCollectionSourceTests.cs`
- Test: `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/Commands/StartCollector/StartCollectorHandlerTests.cs`

**Implementation:**

- [ ] Зафиксировать failing tests для exact ordered identity, schedule, operational flags, terminal Gamma state и сохранения исходной integration error.
- [ ] Расширить `CollectionMarket` полями event identity, market identity, `ConditionId`, `EventStartsAt`, `EventEndsAt`, ordered token/outcome/index и флагами `active`, `closed`, `acceptingOrders`, `enableOrderBook`.
- [ ] В `MarketCollectionSource` сравнить fresh Gamma identity с persisted market и вернуть conflict на любое несовпадение, не применяя time-dependent readiness policy.
- [ ] Запустить `dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter MarketCollectionSourceTests` и Start handler tests.

**Done:** Verified source возвращает только точный pre-Start snapshot и не отклоняет корректный future market из-за временного `acceptingOrders=false`.

---

### Task 2: #22 Добавить CollectorSession snapshot и global exclusivity

**Зачем это делаем:** Все последующие проверки должны использовать факты, зафиксированные в момент Start, а не меняющиеся данные Gamma. Глобальная exclusivity не позволяет двум collectors конкурировать за runtime и смешивать доказательства разных markets.

**Успешный пример:** Первая команда Start для `0xabc` создаёт session со snapshot `1001/Yes`, `1002/No`, window `12:00-12:05 UTC` и `ProjectionVersion=3`. Повторная Start того же market возвращает эту же session без нового Gamma request.

**Ожидающий пример:** Session, созданная в `11:57:00 UTC`, остаётся `Scheduled/WaitingForPreparation`. Exclusive slot уже занят, хотя WebSocket ещё не подключён.

**Ошибочный пример:** Пока `0xabc` находится в `Running`, Start другого market получает стабильный HTTP `409`. Два конкурентных PostgreSQL insert не обходят защиту: partial unique index разрешает только одного победителя.

**Files:**

- Modify: `PolymarketLab.DataCollection.Core/Domain/Models/CollectorSession/CollectorSession.cs`
- Modify: `PolymarketLab.DataCollection.Core/Domain/Models/Enums/CollectorSessionStatus.cs`
- Create: `PolymarketLab.DataCollection.Core/Domain/Models/Enums/CollectorSessionPhase.cs`
- Modify: `PolymarketLab.DataCollection.Core/Ports/ICollectorSessionRepository.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Configurations/CollectorSessionConfiguration.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Repositories/CollectorSession/CollectorSessionRepository.cs`
- Generate after approval: new EF migration under `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Migrations/`
- Test: `PolymarketLab.DataCollection.Core.Tests/Domain/Models/CollectorSession/CollectorSessionTests.cs`
- Test: `PolymarketLab.DataCollection.Infrastructure.Tests/Adapters/Postgres/CollectorSessionRepositoryTests.cs`

**Implementation:**

- [ ] Написать domain tests для полного status/phase vocabulary, terminal `phase=null`, immutable snapshot и отдельного `SubscriptionReadyAt`.
- [ ] Написать Testcontainers tests для same-market idempotency, different-market conflict и конкурентных inserts.
- [ ] Расширить aggregate и persistence mapping всеми snapshot fields и durable phase observations.
- [ ] После разрешения сгенерировать migration с partial unique index на пять exclusive statuses; migration snapshot вручную не менять.
- [ ] Обновить Start flow: сначала global slot, затем fresh source; same market возвращает existing session.

**Done:** Snapshot не меняется после Gamma refresh, а domain и PostgreSQL независимо защищают один global exclusive slot.

---

### Task 3: #25 Реализовать scheduler и Gamma boundary checks

**Зачем это делаем:** Collector должен быть готов ровно к открытию, но не должен менять официальное окно данных ради позднего запуска. Scheduler отделяет раннее планирование от подготовки и запрещает притворяться, что неполный сбор после открытия является полным.

**Успешный пример:** Start в `11:57:00 UTC` создаёт `Scheduled/WaitingForPreparation`; в `11:59:00 UTC`, то есть `T-60s`, CAS переводит session в `Starting`. Readiness должна наступить не позднее `11:59:50 UTC`, то есть `T-10s`.

**Ожидающий пример:** Start в `11:59:30 UTC` сразу начинает preparation и ждёт readiness до `11:59:50 UTC`. Start в `11:59:55 UTC` разрешён как late preparation, но deadline остаётся `12:00:00 UTC`.

**Ошибочный пример:** Start в `12:00:00 UTC` возвращает `409 collector.start.market_already_open` и не создаёт session. Если Gamma boundary check в `11:59:50 UTC` показывает `enableOrderBook=false`, session инвалидируется.

**Files:**

- Modify: `PolymarketLab.DataCollection.Core/Application/UseCases/Commands/StartCollector/StartCollectorHandler.cs`
- Modify: `PolymarketLab.DataCollection.Core/Application/Errors/StartCollectorErrors.cs`
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorScheduling/CollectorScheduler.cs`
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorScheduling/ICollectorScheduler.cs`
- Modify: `PolymarketLab.DataCollection.Core/Application/DependencyInjection/DataCollectionApplicationDependencyInjection.cs`
- Test: `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/Commands/StartCollector/StartCollectorHandlerTests.cs`
- Create test: `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/CollectorScheduling/CollectorSchedulerTests.cs`

**Implementation:**

- [ ] Написать table-driven tests для `<T-60s`, `T-60s..T-10s`, `T-10s..T` и `now>=T` через fake `TimeProvider`.
- [ ] Реализовать precedence: global slot, persisted open-time rejection, fresh Gamma verification, time-window branch.
- [ ] Реализовать идемпотентный durable tick и CAS preparation без `Task.Delay` в tests.
- [ ] На preparation/readiness boundaries требовать `active=true`, `closed=false`, `acceptingOrders=true`, `enableOrderBook=true`; snapshot mismatch направлять в invalidation.
- [ ] Зафиксировать restart behavior: incomplete collection не resume, а передаётся startup recovery из `#29`.

**Done:** Время Start и boundaries дают ровно заявленные состояния и deadlines, а immutable collection window никогда не сдвигается.

---

### Task 4: #28 Реализовать WebSocket readiness, reconnect и heartbeat

**Зачем это делаем:** Сам факт TCP/WebSocket connection не доказывает, что подписка работает. Нужны initial books обоих tokens и живой PONG; после readiness любой разрыв означает дыру в dataset и делает его непригодным.

**Успешный пример:** Epoch `1` получает и успешно enqueue-ит initial `book` для tokens `1001` и `1002`, затем matching text PONG. Только после этого session становится `Running/ReadyBeforeWindow`; PING каждые `10 секунд`, PONG приходит не позже чем через `10 секунд`.

**Ожидающий пример:** В epoch `1` пришёл book только для `1001`. Session остаётся `Starting/AwaitingInitialBooks`. До deadline socket может переподключиться; epoch `2` начинает readiness с нуля.

**Ошибочный пример:** После readiness в `12:02:00 UTC` socket закрывается или PONG не приходит за `10 секунд`. Reconnect уже не маскирует потерю данных: session переходит в invalidation.

**Files:**

- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/CollectorWebSocketWorker.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/CollectorRuntime.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/WebSockets/ICollectorWebSocketConnection.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/WebSockets/ClientWebSocketConnection.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/CollectorWebSocketWorkerFactory.cs`
- Test: `PolymarketLab.DataCollection.Infrastructure.Tests/Adapters/CollectorRuntime/CollectorWebSocketWorkerTests.cs`
- Test: `PolymarketLab.DataCollection.Infrastructure.Tests/Adapters/CollectorRuntime/CollectorRuntimeStartTests.cs`

**Implementation:**

- [ ] Написать tests для initial books, matching PONG, duplicate/unknown/malformed observations, reconnect до deadline и stale epoch.
- [ ] Ввести монотонный `ConnectionEpoch`; connect/subscription оставлять в `Starting`, а readiness подтверждать отдельным CAS.
- [ ] Реализовать heartbeat с одним outstanding PING, интервалом `10 секунд` и deadline `10 секунд`; PING/PONG не отправлять в raw sink.
- [ ] Разрешить bounded reconnect только до effective readiness; timeout, close, protocol, identity и backpressure failure после readiness направлять в invalidation.
- [ ] Повторно прогнать fragmentation, message-size и bounded-backpressure regression tests.

**Done:** Только текущая epoch может стать ready, а после readiness непрерывность WebSocket доказуема до resolution.

---

### Task 5: #31 Сохранить ConnectionEpoch и exact durable raw accounting

**Зачем это делаем:** In-memory counters исчезают при restart и не могут служить доказательством полноты. Durable counters и epoch каждой raw row позволяют после завершения процесса проверить, сколько сообщений действительно прошло каждую границу pipeline.

**Успешный пример:** После завершения session PostgreSQL показывает `received=1250`, `enqueued=1250`, `persisted=1250`, raw count `1250`; каждая row хранит epoch, в которой было полностью принято text message.

**Ожидающий пример:** Persistence batch ещё выполняется: `received=1250`, `enqueued=1250`, `persisted=1200`. Это нормальное промежуточное состояние, но session ещё нельзя признать complete.

**Ошибочный пример:** После restart durable counters показывают `received=1250`, `enqueued=1249`. Одно полное message потеряно до queue, поэтому equality gate позже инвалидирует dataset, даже если текущая telemetry пуста.

**Files:**

- Modify: `PolymarketLab.DataCollection.Core/Ports/Dtos/RawMarketMessage.cs`
- Modify: `PolymarketLab.DataCollection.Core/Ports/Dtos/CollectorSessionProgressCheckpoint.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/RawMessageIngestion/RawMarketMessageTelemetry.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/RawMessageIngestion/RawMarketMessagePersistenceWorker.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Models/RawMarketMessageRecord.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Models/CollectorSessionProgressRecord.cs`
- Modify: PostgreSQL configurations and repositories for raw/progress
- Generate after approval: new EF migration under DataCollection migrations
- Test: raw ingestion tests and PostgreSQL integration tests

**Implementation:**

- [ ] Зафиксировать increment boundaries: полное non-control text message, successful bounded enqueue, committed raw insert.
- [ ] Написать failing tests для PING/PONG exclusion, batching, retry, concurrent persistence, epoch assignment и restart read.
- [ ] Добавить `ConnectionEpoch` в raw DTO/entity и current epoch плюс exact counters в durable checkpoint.
- [ ] Реализовать authoritative PostgreSQL read, возвращающий counters вместе с raw row count.
- [ ] После разрешения сгенерировать и проверить EF migration.

**Done:** Exact counters и epoch читаются из PostgreSQL независимо от живого runtime и не учитывают protocol heartbeat.

---

### Task 6: #33 Добавить CLOB market resolution source по ConditionId

**Зачем это делаем:** Цена `0.99` похожа на победу, но остаётся торговой вероятностью. Для terminal evidence нужен независимый CLOB market endpoint, который после закрытия показывает exact settlement `1.00/0.00` для snapshot `ConditionId`.

**Успешный пример:** CLOB для `0xabc` возвращает `closed=true`, `accepting_orders=false`, tokens `1001` и `1002`, prices `1.00` и `0.00`; source определяет winner `1001/Yes`.

**Ожидающий пример:** В `12:05:02 UTC` market ещё `closed=false` или prices `0.99/0.01`. Source возвращает non-terminal observation, а consensus polling повторится через `2 секунды`.

**Ошибочный пример:** Terminal payload содержит неизвестный token `9999`, два winners с `1.00` или wrong condition. Это invalid terminal payload, а не transient retry.

**Files:**

- Create: `PolymarketLab.DataCollection.Core/Ports/IClobTerminalResolutionSource.cs`
- Create: `PolymarketLab.DataCollection.Core/Ports/Dtos/ClobTerminalResolutionRequest.cs`
- Create: `PolymarketLab.DataCollection.Core/Ports/Dtos/ClobTerminalResolutionObservation.cs`
- Create: `PolymarketLab.DataCollection.Infrastructure/Adapters/ClobResolution/ClobTerminalResolutionClient.cs`
- Create: `PolymarketLab.DataCollection.Infrastructure/Adapters/ClobResolution/ClobTerminalResolutionDto.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/DependencyInjection/DataCollectionInfrastructureDependencyInjection.cs`
- Create test: `PolymarketLab.DataCollection.Infrastructure.Tests/Adapters/ClobResolution/ClobTerminalResolutionClientTests.cs`

**Implementation:**

- [ ] Подтвердить актуальный read-only CLOB contract по primary Polymarket documentation перед написанием transport DTO.
- [ ] Написать deterministic HTTP tests для terminal, non-terminal, wrong identity, inconsistent winner, timeout, network, HTTP и malformed JSON.
- [ ] Реализовать отдельный adapter по `ConditionId`; существующий `/book` `IOrderBookSnapshotSource` не расширять.
- [ ] Валидировать exact token set и единственного winner только при `closed=true`, `accepting_orders=false`, prices `1.00/0.00`.
- [ ] Зарегистрировать typed client и source без credentials и trading permissions.

**Done:** CLOB source безопасно различает terminal, non-terminal, transient и invalid response и возвращает Core observation без transport DTO leakage.

---

### Task 7: #35 Добавить invalidation coordinator и write claim fences

**Зачем это делаем:** Удалить плохой dataset недостаточно, если параллельный raw writer или normalizer может записать его обратно после cleanup. Durable fence сначала останавливает новые producers/claims и только затем разрешает удаление.

**Успешный пример:** Heartbeat failure переводит session в `Invalidating/Cleaning`, сохраняет `InvalidatingAt` и diagnostics. Уже начатый raw batch завершается до fence, после чего cleanup видит стабильный набор rows.

**Ожидающий пример:** Normalizer держит claim в момент invalidation. Coordinator ждёт завершения согласованного claim или writer получает `ClaimLost`; до этого cleanup не стартует.

**Ошибочный пример:** Stale writer пытается commit raw или projection после установленного fence. PostgreSQL protocol отклоняет запись, поэтому удалённый dataset не появляется снова.

**Files:**

- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorInvalidation/ICollectorInvalidationCoordinator.cs`
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorInvalidation/CollectorInvalidationCoordinator.cs`
- Modify: `ICollectorSessionRepository`, raw writer and normalization claim ports
- Modify: `CollectorRuntimeFailureHandler.cs`, shutdown handler and startup reconciler
- Modify: `RawMarketMessageWriter.cs`, `RawMessageNormalizationClaimRepository.cs`, `RawMessageNormalizationReplayClaimRepository.cs`, `VersionedNormalizedWriter.cs`
- Modify: `CollectorSessionConfiguration.cs`
- Generate after approval: new EF migration if fence columns/indexes are required
- Test: domain, repository and deterministic PostgreSQL concurrency tests

**Implementation:**

- [ ] Написать concurrency tests для in-flight raw batch, continuous/replay claims, stale writer и повторной invalidation.
- [ ] Добавить необратимый transition incomplete session в `Invalidating/Cleaning` с durable failure diagnostics и timestamp.
- [ ] Реализовать единый fence/lock protocol для producer, raw persistence, claim acquisition и normalized writer.
- [ ] Изменить manual Stop, application shutdown и process termination до успеха: они инициируют invalidation, а не `Stopped/Requested`.
- [ ] Доказать tests, что после успешного fence не появляются raw, ledger или projection rows target session.

**Done:** Cleanup получает стабильный dataset target, а любой stale write или claim безопасно отклоняется.

---

### Task 8: #29 Реализовать atomic dataset cleanup и startup recovery

**Зачем это делаем:** Неполный dataset опаснее отсутствующего: аналитика может принять его за полный. Cleanup удаляет только данные failed session, но сохраняет саму session и audit, чтобы причина отказа оставалась видимой.

**Успешный пример:** Для failed session удалены `1250` raw rows, ledger всех versions и все typed projections; session, historical counters, diagnostics, market/tokens и dataset другой session сохранены. Затем CAS завершает `Invalidating -> Failed`.

**Ожидающий пример:** Process упал в `Invalidating/Cleaning` до commit. При startup recovery cleanup запускается до normalizer/replay/API и безопасно продолжает работу.

**Ошибочный пример:** Transaction падает после удаления части projections. Rollback возвращает весь dataset и оставляет `Invalidating`, чтобы retry удалил его целиком, а не зафиксировал половинчатый cleanup.

**Files:**

- Create: `PolymarketLab.DataCollection.Core/Ports/ICollectorDatasetCleanup.cs`
- Create: `PolymarketLab.DataCollection.Core/Ports/Dtos/CollectorDatasetCleanupAudit.cs`
- Create: `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Repositories/CollectorSession/CollectorDatasetCleanup.cs`
- Modify: `CollectorSessionStartupReconciler.cs`
- Modify: `CollectorSessionStartupReconciliationService.cs`
- Modify: `DataCollectionInfrastructureDependencyInjection.cs`
- Test: new PostgreSQL cleanup integration tests and hosted-service ordering tests

**Implementation:**

- [ ] Написать PostgreSQL tests с каждым typed event, несколькими projection versions, второй session, forced transaction failure и повторным вызовом.
- [ ] Определять ownership только через `raw_market_messages.session_id`; удалить normalized events, ledger и raw target в одной transaction.
- [ ] Сохранить audit/deleted counts и historical counters, затем выполнить CAS `Invalidating -> Failed`.
- [ ] Сделать retry после commit идемпотентным no-op, а retry после rollback повторяющим полную transaction.
- [ ] Переставить startup recovery раньше normalizer, replay и API; recovery failure должен остановить startup.

**Done:** Failed dataset удаляется атомарно и возобновляемо, не затрагивая session evidence и unrelated data.

---

### Task 9: #24 Реализовать strict resolution observations и consensus

**Зачем это делаем:** Одно сообщение `market_resolved` может быть stale, относиться к другому market или противоречить внешним источникам. Consensus требует, чтобы current WebSocket epoch, Gamma и CLOB независимо назвали одного winner из immutable snapshot.

**Успешный пример:** После `12:05:00 UTC` WS raw item, Gamma и CLOB указывают `1001/Yes`; Gamma/CLOB settlement равен `1.00/0.00`. Observations и timestamps сохраняются, после чего начинается controlled drain.

**Ожидающий пример:** WS уже указал `Yes`, но Gamma и CLOB в `12:05:02 UTC` ещё non-terminal. Polling идёт каждые `2 секунды` без overlap до `12:10:00 UTC`, то есть `EventEndsAt + 5 минут`.

**Ошибочный пример:** WS и Gamma указывают `Yes`, а valid terminal CLOB указывает `No`. Это immediate `ResolutionConflict`; retries не скрывают реальное противоречие, session инвалидируется.

**Files:**

- Create: strict WS resolution observer and observation DTOs under `PolymarketLab.DataCollection.Core/Application/Resolution/`
- Create: resolution consensus coordinator under the same feature directory
- Modify: `CollectorWebSocketWorker.cs` to submit current-epoch raw observations
- Use: `IGammaTerminalResolutionSource` and new `IClobTerminalResolutionSource`
- Add PostgreSQL entities/configurations for durable safe observations and WS raw item reference
- Generate after approval: new EF migration
- Test: Core consensus tests, runtime tests and PostgreSQL observation tests

**Implementation:**

- [ ] Написать fake-TimeProvider tests для stale epoch, pre-end WS message, wrong token/condition, conflict, deadline и no-overlap polling.
- [ ] Реализовать strict observer отдельно от generic archive `MarketResolvedNormalizer`; проверять snapshot identity, token membership, current epoch и `observedAt>=EventEndsAt`.
- [ ] Связать durable WS observation с `RawMessageId` и `RawItemIndex`.
- [ ] Начинать Gamma/CLOB polling ровно в `EventEndsAt` независимо от наличия WS observation; interval `2 секунды`, deadline `5 минут`, без overlap.
- [ ] На consensus передать flow в controlled drain; conflict, timeout или continuity failure передать invalidation coordinator.

**Done:** Только согласованные durable observations трёх sources текущей session могут подтвердить resolution.

---

### Task 10: #34 Завершить controlled drain и durable raw equality

**Зачем это делаем:** Даже правильный winner не гарантирует, что последнее `market_resolved` и сообщения перед ним дошли до PostgreSQL. Producer нужно остановить первым, полностью осушить queue и сравнить четыре authoritative количества.

**Успешный пример:** После consensus producer закрыт, final batch сохранён, включая raw `market_resolved`; PostgreSQL подтверждает `1250=1250=1250=1250>0` для received, enqueued, persisted и raw rows.

**Ожидающий пример:** Producer уже закрыт, но в channel остаётся batch из `50 сообщений`; session находится в `Stopping/DrainingRaw`, пока persisted не вырастет с `1200` до `1250`.

**Ошибочный пример:** Final checkpoint говорит `persisted=1250`, но PostgreSQL насчитывает `1249` raw rows. Telemetry не считается доказательством; equality failure инициирует invalidation.

**Files:**

- Modify: `PolymarketLab.DataCollection.Core/Application/UseCases/Commands/StopCollector/StopCollectorHandler.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/CollectorRuntimeShutdownService.cs`
- Modify: `RawMarketMessagePersistenceWorker.cs`
- Modify: `CollectorSessionProgressCompletion.cs`
- Extend authoritative PostgreSQL accounting repository from `#31`
- Test: shutdown, persistence worker, completion and PostgreSQL equality tests

**Implementation:**

- [ ] Написать tests для in-flight batch, producer-stop ordering, drain timeout, checkpoint retry и equality read после restart.
- [ ] После resolution consensus закрыть producer до final drain; manual Stop по-прежнему направлять в invalidation.
- [ ] Дождаться channel drain и final flush, затем durably сохранить final received/enqueued/persisted checkpoint.
- [ ] Одним PostgreSQL read проверить `received=enqueued=persisted=count(raw)>0`.
- [ ] Любой stop, drain, checkpoint или equality failure передать invalidation coordinator.

**Done:** Ни один in-flight raw message не потерян, и полнота raw dataset доказана из PostgreSQL.

---

### Task 11: #26 Добавить session snapshot normalization suitability gate

**Зачем это делаем:** Равенство raw counters доказывает только сохранность байтов. Dataset пригоден для аналитики лишь тогда, когда каждый raw обработан именно той `ProjectionVersion`, которую session зафиксировала при Start.

**Успешный пример:** Для `1250` raw rows существует ровно `1250` ledger rows version `3` со status `Processed`; WS resolution raw item тоже `Processed`. Session может перейти в `Stopped/MarketResolved`.

**Ожидающий пример:** `1240` rows имеют `Processed`, а `10` находятся в `Pending` или `Processing`. Session остаётся `Stopping/AwaitingNormalization` максимум до `12:10:00 UTC`, то есть до истечения `5 минут` ожидания.

**Ошибочный пример:** Одна row имеет `Unsupported`, terminal `Failed` или только ledger version `4`, хотя snapshot version `3`. Dataset инвалидируется; активную version нельзя незаметно подменить во время session.

**Files:**

- Create: `PolymarketLab.DataCollection.Core/Ports/INormalizationSuitabilityReader.cs`
- Create: `PolymarketLab.DataCollection.Core/Ports/Dtos/NormalizationSuitability.cs`
- Create: normalization suitability gate under `Application/UseCases/CollectorCompletion/`
- Create PostgreSQL reader over raw and normalization ledger
- Modify: terminal lifecycle coordinator after raw equality
- Test: Core gate tests and PostgreSQL cardinality/version tests

**Implementation:**

- [ ] Написать tests для all-Processed, Pending/Processing wait, Invalid, Unsupported, terminal Failed, timeout, empty root array и runtime version mismatch.
- [ ] Snapshot-ить текущую active `ProjectionVersion` при session creation; configuration rollover требует restart и не поддерживается внутри exclusive session.
- [ ] Одним session-scoped read проверить ровно одну ledger row snapshot version на каждый raw и cardinality равную raw count.
- [ ] Проверить, что strict WS resolution observation указывает на raw item со status `Processed`; empty root array считать valid `Processed` с нулём events.
- [ ] Разрешить `Stopped/MarketResolved` только после успешного gate; все terminal failures направить в invalidation.

**Done:** Пригодность normalized projections доказана для неизменяемой snapshot version, а не только для произвольной текущей версии normalizer.

---

### Task 12: #27 Расширить Collector read DTO и HTTP contract

**Зачем это делаем:** Backend уже знает readiness, continuity, counters, resolution и cleanup, но без единого read contract оператор и frontend не могут понять, почему session ждёт, завершилась или была удалена. Эта задача только агрегирует доказательства, не повторяя orchestration.

**Успешный пример:** GET session возвращает snapshot, `Stopped`, `phase=null`, winner `Yes`, timestamps трёх observations, counters `1250`, normalization `1250 Processed` и отсутствие cleanup.

**Ожидающий пример:** GET во время `Stopping/AwaitingNormalization` показывает effective deadline, raw equality и `10 Pending`, поэтому оператор видит конкретную причину ожидания.

**Ошибочный пример:** После cleanup GET показывает historical `messagesPersisted=1250`, remaining raw rows `0`, failure code и deleted counts. Historical counters не подменяются текущим количеством rows.

**Files:**

- Modify: `PolymarketLab.DataCollection.Core/Application/UseCases/Common/CollectorSessionResponse.cs`
- Modify: query handlers/responses under `GetCollectorSessionById` and `GetCollectorSessionByMarket`
- Modify: DataCollection API controller DTO mapping
- Test: `PolymarketLab.ApiContract.Tests/ReadControllerResponseTests.cs`
- Test: `PolymarketLab.ApiContract.Tests/FrontendApiContractTests.cs`
- Modify: `docs/frontend-api-contract.md`

**Implementation:**

- [ ] До изменения публичного HTTP contract запросить отдельное разрешение пользователя.
- [ ] Написать contract tests для exact JSON names, nullable semantics, всех status/phase values и legacy `Interrupted`.
- [ ] Добавить application read slices для snapshot/window/version, readiness per token, epoch, durable counters, normalization, resolution и cleanup audit.
- [ ] Агрегировать slices в существующих GET routes и сохранить Envelope, expected errors и string enum conventions.
- [ ] Проверить allowlist полей: raw payload, credentials и stack traces не сериализуются; синхронизировать `docs/frontend-api-contract.md`.

**Done:** Существующие routes возвращают полное безопасное lifecycle evidence с точно зафиксированным JSON contract.

---

### Task 13: #36 Расширить dashboard полным lifecycle сборщика

**Зачем это делаем:** Оператору нужен не просто индикатор `Running`, а понятный ответ: сколько осталось до старта, готовы ли оба tokens, непрерывен ли socket, согласован ли winner и почему cleanup удаляет dataset.

**Успешный пример:** Future market виден и selectable; после early Start UI показывает countdown и `Scheduled / Waiting for preparation`, затем readiness обоих tokens, collecting window, resolution sources и итоговый `Stopped / Market resolved`.

**Ожидающий пример:** В `Stopping/AwaitingNormalization` UI продолжает polling и показывает `10` pending raw items и effective deadline в локальном времени. Polling останавливается только для terminal или неизвестного status.

**Ошибочный пример:** Пользователь нажимает Stop и подтверждает destructive действие. UI не выставляет status вручную: он показывает фактический backend flow `Invalidating/Cleaning -> Failed`, diagnostics и cleanup counts.

**Files:**

- Modify: `PolymarketLab.Web/src/api/collectorsApi.ts`
- Modify: `PolymarketLab.Web/src/features/collectors/model/collectorSession.ts`
- Modify: `PolymarketLab.Web/src/features/collectors/model/collectorStatus.ts`
- Modify: collector query hooks and polling policy
- Modify: `CollectorPanel.tsx`, `CollectorControls.tsx`, `CollectorMetrics.tsx`, `CollectorFailure.tsx`
- Modify: market list/page to retain future registered markets
- Test: API, model, hook, component and dashboard tests

**Implementation:**

- [ ] Обновить typed API/model tests по фактическому backend DTO из `#27`; server state оставить только в TanStack Query.
- [ ] Убрать фильтрацию future markets, добавить local-time countdown и точные текстовые status/phase labels с fallback `Unknown`.
- [ ] Добавить per-token readiness, continuity, resolution observations, normalization и cleanup; `null` показывать как `-`, counters форматировать через `Intl.NumberFormat`.
- [ ] Poll `Scheduled`, `Starting`, `Running`, `Stopping`, `Invalidating`; terminal и unknown не poll-ить.
- [ ] Через by-market reads обнаруживать известную exclusive session другого registered market и блокировать Start; backend `409` остаётся race protection.
- [ ] Добавить keyboard-accessible confirmation destructive Stop и responsive tests для mobile token IDs/tables.

**Done:** Dashboard честно отображает весь backend lifecycle, не дублирует orchestration и работает на desktop/mobile и с клавиатуры.

---

### Task 14: #23 Добавить deterministic host acceptance и opt-in live run

**Зачем это делаем:** Unit и component tests не доказывают, что routing, Envelope, MediatR, EF, hosted services и два DbContexts работают вместе. Deterministic host acceptance ловит wiring defects повторяемо, а отдельный live run подтверждает совместимость с реальным Polymarket без превращения внешней сети в обязательный test dependency.

**Успешный пример:** Fake Gamma/CLOB/WebSocket проводят real ASP.NET host по всей цепочке до `Stopped/MarketResolved`; PostgreSQL доказывает `received=enqueued=persisted=raw=Processed(version)>0`. Отдельный opt-in live run фиксирует sanitized terminal evidence.

**Ожидающий пример:** Deterministic suite проходит, но live run не запускался или market ещё не завершился. Код проверен локально, однако epic остаётся открытым до одного успешного live evidence.

**Ошибочный пример:** Live Gamma временно возвращает HTTP `503`. Попытка считается неуспешной, но это не делает deterministic suite дефектной и не разрешает публиковать stack trace или raw payload.

**Files:**

- Extend: `PolymarketLab.ApiContract.Tests/` или создать отдельный acceptance test project только после согласования dependency/project change
- Add controllable fake Gamma, CLOB and WebSocket transport in test scope
- Modify: existing operational documentation with commands, evidence checklist and cleanup recovery
- Do not add live test to default solution test path

**Implementation:**

- [ ] Спроектировать host fixture с clean PostgreSQL, migrations обоих DbContexts, fake external transports и controllable `TimeProvider`; новую test dependency согласовать отдельно.
- [ ] Написать full lifecycle acceptance от `Scheduled/WaitingForPreparation` до `Stopped/MarketResolved` через реальные HTTP routes и Envelope.
- [ ] Написать failure acceptance на readiness, continuity, resolution conflict, drain equality и normalization boundaries с durable invalidation/cleanup assertions.
- [ ] Проверить отсутствие pending migrations и финальное равенство исключительно из PostgreSQL checkpoint/ledger, не из telemetry.
- [ ] Задокументировать opt-in live command, safe configuration, recovery и evidence: commit SHA, UTC time, sanitized identity, `sessionId`, terminal DTO и aggregate checks.
- [ ] Выполнить live run только после явного opt-in; не публиковать raw payload, credentials, connection string или stack traces.

**Done:** Повторяемый host acceptance проходит отдельно от сети, а один opt-in live market завершён с безопасным `Stopped/MarketResolved` evidence.

---

### Task 15: #17 Закрыть epic «Первый полный сбор пятиминутного рынка»

**Зачем это делаем:** Epic является финальным quality gate. Он не позволяет объявить успех по одному зелёному status, если не закрыты continuity, exact accounting, normalization suitability, UI и reproducible acceptance.

**Успешный пример:** Все implementation issues закрыты, deterministic host suite зелёная, а live session на commit SHA завершилась `Stopped/MarketResolved` с sanitized evidence и равенством всех durable counts больше нуля.

**Ожидающий пример:** Issues и deterministic acceptance закрыты, но live run завершился transient integration failure или ещё не состоялся. Epic остаётся открытым; новая попытка не требует ослаблять tests.

**Ошибочный пример:** Session завершилась legacy `Stopped/Requested`, была остановлена вручную или имеет хотя бы одну `Unsupported` normalization row. Такой запуск не считается первым полным dataset и не закрывает epic.

**Files:**

- Review: all child issue acceptance evidence
- Review: `docs/agent-context.md`, `docs/frontend-api-contract.md` and live-run documentation
- Update: GitHub issue `#17` only after all gates pass

**Implementation:**

- [ ] Проверить, что `#22`-`#36` в scope epic закрыты, включая уже выполненные `#20`, `#21`, `#32`.
- [ ] На clean PostgreSQL выполнить migrations, полный `.NET` test/build, frontend test/typecheck/build и `git diff --check`.
- [ ] Повторно выполнить deterministic host acceptance и сохранить её aggregate result без sensitive data.
- [ ] Проверить live evidence: commit SHA, UTC time, sanitized market identity, `sessionId`, terminal DTO, exact durable equality и snapshot-version Processed cardinality.
- [ ] Закрыть `#17` только если terminal reason равен `MarketResolved`; `Requested`, `Failed`, `Interrupted` и legacy sessions отклонить.

**Done:** Спецификация `#14` выполнена end-to-end, а первый полный пятиминутный dataset доказуемо пригоден для последующей аналитики.

---

## Общая стратегия тестирования

- Domain tests фиксируют допустимые transitions, immutable snapshot и status/phase vocabulary.
- Application tests используют fake ports и controllable `TimeProvider`, проверяют precedence и orchestration без реального ожидания.
- Adapter tests проверяют Gamma/CLOB/WebSocket payload mapping и сохраняют исходные integration errors.
- PostgreSQL Testcontainers tests доказывают race safety, fences, atomic cleanup, authoritative equality и normalization cardinality.
- API contract tests фиксируют exact JSON, nullability, enums и запрет sensitive fields.
- Vitest component tests покрывают loading, empty, error, success, lifecycle polling, global slot, destructive Stop и responsive accessibility.
- Host acceptance проверяет реальный composition root с fake external boundaries.
- Live run остаётся отдельной opt-in операцией и никогда не заменяет deterministic tests.

## Self-review checklist

- [ ] Каждое требование issues `#22`-`#36` сопоставлено с task и test level.
- [ ] Для каждой из 15 открытых задач есть объяснение «Зачем», успешный, ожидающий и ошибочный пример.
- [ ] Все числовые значения имеют смысл и единицы: секунды, минуты, message counts, probability или terminal settlement.
- [ ] Имена statuses, phases, errors и DTO сверены с фактическим backend перед реализацией каждой issue.
- [ ] План не требует frontend orchestration, trading, wallet, authentication, SignalR, browser WebSocket или multi-instance ownership.
- [ ] Перед migrations, HTTP-contract changes, dependencies, commits и live run запрашивается требуемое разрешение.
