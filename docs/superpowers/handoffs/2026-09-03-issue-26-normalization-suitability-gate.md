# Handoff: issue #26 — normalization suitability gate и terminal lifecycle

## Статус

- Ветка: `feature/first-full-five-minute-market`.
- Исходный commit: `e0406a0 Implement atomic dataset cleanup and startup recovery`.
- Задача `#34` (controlled drain и durable raw equality) **реализована, исправлена после ревью и не закоммичена**. Всё в рабочем дереве.
- Следующая задача: `#26` «Добавить session snapshot normalization suitability gate» — Task 11 в `docs/superpowers/plans/2026-08-27-first-full-five-minute-market-roadmap.md`.
- Реализация `#26` ещё не начиналась.

## Что сделано по #34 (для контекста diff)

Новый application coordinator `CollectorRawDatasetCompletionCoordinator` после durable consensus выполняет: CAS `Running/AwaitingResolution -> Stopping/DrainingRaw` -> `runtime.StopAsync` -> wait persisted до final enqueued boundary + final checkpoint -> один PostgreSQL read -> проверка `received=enqueued=persisted=raw>0` -> CAS `Stopping/DrainingRaw -> Stopping/AwaitingNormalization`. Ошибка любого шага -> `ICollectorSessionInvalidationCoordinator` с `PersistenceFailure`.

Файлы (созданы):

- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRawDatasetCompletion/ICollectorRawDatasetCompletionCoordinator.cs`
- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionCoordinator.cs`
- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionErrors.cs`
- `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionCoordinatorTests.cs`
- `PolymarketLab.DataCollection.Infrastructure.Tests/Adapters/RawMessageIngestion/CollectorSessionProgressCompletionTests.cs`

Изменены: `ResolutionConsensusCoordinator.cs` (handoff после consensus, без повторного Gamma/CLOB polling), `DataCollectionApplicationDependencyInjection.cs` (+scoped регистрация), `ICollectorSessionProgressCompletion.cs` и его adapter (явная final boundary), consensus/DI/PostgreSQL tests, `README.md` CollectorRuntime, `docs/agent-context.md`.

Проверки выполнены после ревью и исправления lifecycle handoff: Core tests 358, Infrastructure tests 507, PostgreSQL integration (Docker доступен), `dotnet test .\PolymarketLab.slnx`, `dotnet build .\PolymarketLab.slnx` (0 ошибок; 8 предупреждений — существующие NU1900/NU1903), `git diff --check`. После durable confirmation polling и WebSocket scanning не продолжаются; resolution conflict не запускает raw completion; ошибка PostgreSQL equality read проходит через durable invalidation и возвращает безопасный failure.

## Исправления после ревью #34

- `ResolutionConsensusCoordinator` теперь сразу передаёт session в raw completion, если `ResolutionConfirmationReference` уже сохранён. После durable confirmation нет повторных Gamma/CLOB polling и WebSocket scanning.
- Внутренний результат consensus больше не является двусмысленным `bool`: `Pending`, `Confirmed` и `Invalidated` разделены. Успешно обработанный resolution conflict не запускает `CollectorRawDatasetCompletionCoordinator`.
- Ошибка `ICollectorSessionProgressRepository.GetAsync` перехватывается на application boundary, исходное исключение журналируется без raw payload, а безопасная ошибка `collector.raw_completion.progress_read_failed` сохраняется через durable invalidation.
- После успешной invalidation исходная completion-ошибка возвращается вызывающей стороне как failure; успешный повторный `runtime.StopAsync` больше её не маскирует.
- Тесты фиксируют отсутствие повторного scanning после confirmation, отсутствие raw completion после conflict, durable invalidation при ошибке equality read и сохранение исходных error codes.

## Что осталось от #34

- Незавершённых изменений поведения по `#34` не осталось.
- Commit не выполнялся — только с явного разрешения пользователя. Чужие незавершённые изменения (`.harness/*`, `AGENTS.md`, migration `20260831121534_PersistConnectionEpochAndExactRawAccounting.*`) в коммит не включать.
- `HEAD` по-прежнему равен `e0406a0`; новые файлы #34 untracked и поэтому не попадают в обычный `git diff e0406a0`. Перед любым staging обязательно сверять одновременно `git diff` и `git status --short`, не использовать широкое `git add .`.

## Следующая задача: #26

**Суть:** session сейчас остаётся в `Stopping/AwaitingNormalization` — терминальный переход (`Stopped/MarketResolved`) никто не выполняет. Нужен suitability gate: для каждого raw row должна существовать ровно одна ledger row с snapshot `ProjectionVersion` (зафиксированной при создании session) и status `Processed`; cardinality равна raw count; strict WS resolution observation указывает на `Processed` raw item. `Pending`/`Processing` — ждать до `AwaitingNormalizationAt + 5m`, то есть пять минут после durable raw drain. `Unsupported`/`Failed`/чужая version — durable invalidation. Полная спецификация: Task 11 roadmap (`docs/superpowers/plans/2026-08-27-first-full-five-minute-market-roadmap.md`, строки 378–405).

**Заготовки из плана #26:**

- Создать `PolymarketLab.DataCollection.Core/Ports/INormalizationSuitabilityReader.cs` и `Ports/Dtos/NormalizationSuitability.cs`.
- Gate в `Application/UseCases/CollectorCompletion/`.
- PostgreSQL reader поверх raw и normalization ledger; один session-scoped read.
- Изменить «terminal lifecycle coordinator after raw equality»: после `Stopping/AwaitingNormalization` опрашивать gate и переводить в `Stopped/MarketResolved` (доменный переход, вероятно, надо добавить рядом с `CollectorSession.MarkAwaitingNormalization()`).
- Tests: Core gate tests + PostgreSQL cardinality/version tests (all-Processed, Pending/Processing wait, Invalid, Unsupported, terminal Failed, timeout, empty root array, version mismatch).

**Существующие зацепки:**

- Ledger claims: `PolymarketLab.DataCollection.Core/Ports/IRawMessageNormalizationClaimRepository.cs`, adapter `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Repositories/Normalization/RawMessageNormalizationClaimRepository.cs`, integration tests `RawMessageNormalizationClaimRepositoryPostgreSqlTests.cs`.
- Backlog reader: `Adapters/Postgres/Repositories/Normalization/NormalizationBacklogReader.cs`.
- Нормализация и версии: `docs/normalizer-input-contract.md`, `IRawMessageNormalizer`, `NormalizationProcessor`.
- Терминальный статус: `CollectorSession.Stop(stoppedAt, reason)` (требует `IsExclusive(Status)` — Stopping входит), `CollectorStopReason.MarketClosed` уже существует.
- Читать перед работой: `docs/agent-context.md` (раздел Data Collection) и `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/README.md`.

**Фактическая persistence-модель, на которую должен опираться reader:**

- `data_collection.raw_market_messages`: ключ `id`, session scope по `session_id`; EF-модель `RawMarketMessageRecord`, mapping `RawMarketMessageConfiguration`.
- `data_collection.raw_message_normalizations`: составной ключ `(raw_message_id, projection_version)`; одна ledger row описывает весь raw message, а не отдельный элемент root JSON array. EF-модель `RawMessageNormalizationRecord`, mapping `RawMessageNormalizationConfiguration`.
- Возможные ledger statuses уже заданы `NormalizationStatus`: `Pending`, `Processing`, `Processed`, `Unsupported`, `Invalid`, `Failed`.
- Snapshot version брать только из `CollectorSession.ProjectionVersion`. Не подменять её текущим `IProjectionVersionProvider.ProjectionVersion`; у legacy session значение может быть `null`, и этот случай должен завершаться безопасным invalidation, а не выбором активной runtime version.
- Strict WebSocket observation хранит provenance в `ResolutionObservationEntity.RawMessageId` и `RawItemIndex`; оба значения должны быть проверены. Ledger подтверждает `Processed` для соответствующего parent raw message snapshot-версии.
- Существующая схема уже содержит нужные session/version/status/provenance поля. Миграция для #26 по текущей спецификации не ожидается; если реализация всё же потребует изменения схемы, сначала запросить разрешение.

**Lifecycle seam:**

- `ResolutionConsensusBackgroundService` создаёт scope и вызывает `IResolutionConsensusCoordinator.TickAsync` раз в `1 секунду`.
- Сейчас `ResolutionConsensusCoordinator` немедленно завершает tick для session не в `Running`, поэтому `Stopping/AwaitingNormalization` никто повторно не обрабатывает.
- Минимальный путь — расширить существующий application lifecycle tick маршрутизацией `Stopping/AwaitingNormalization` в новый gate, не меняя порядок hosted services. Не создавать параллельный lifecycle loop без доказанной необходимости.
- `Pending`/`Processing` — обычное ожидание, а не ошибка: следующий tick повторяет проверку до `AwaitingNormalizationAt + 5m`.
- После deadline незавершённость становится terminal failure и проходит через `ICollectorSessionInvalidationCoordinator`; успешный gate вызывает доменный `Stop(..., CollectorStopReason.MarketClosed)` и CAS с expected status `Stopping`.

**Рекомендуемый первый вертикальный slice:**

1. Зафиксировать Core-тестами три семантических результата gate: готовность, ожидание и непригодность; точные имена DTO выбрать в соответствии с существующей терминологией проекта.
2. Для готовности доказать точную cardinality: каждому raw message соответствует ровно одна ledger row snapshot-версии со статусом `Processed`, а strict WebSocket resolution observation указывает на item внутри обработанного parent raw message.
3. Для `Waiting` оставить session в `Stopping/AwaitingNormalization` до общего deadline `AwaitingNormalizationAt + 5m`.
4. Для `Invalid` выполнить durable invalidation без перехода в `Stopped/MarketResolved`.
5. Только после стабилизации application seam реализовать один PostgreSQL session-scoped read и его integration tests.

**Критерии завершения #26:**

- Успех: `1250` raw rows и ровно `1250` ledger rows snapshot version `3`, все `Processed`; strict WS resolution provenance ссылается на существующий обработанный raw message/item; session CAS-переходит в `Stopped/MarketResolved`.
- Ожидание: при `1240 Processed` и `10 Pending/Processing` session остаётся `Stopping/AwaitingNormalization`; это не exception и не invalidation до deadline.
- Ошибка: `Unsupported`, `Invalid`, terminal `Failed`, отсутствующая/чужая version, нарушение cardinality, неверный WS provenance либо timeout ведут в durable invalidation. Dataset не получает успешный terminal status.
- Empty root array считается успешно `Processed` с `0` normalized events и само по себе не делает dataset непригодным.
- PostgreSQL suitability вычисляется одним session-scoped read; отдельные последовательные запросы, допускающие несогласованный snapshot, не подходят.
- Публичный HTTP-контракт, frontend и EF schema не меняются.

## Осторожности

- Не менять вручную migration snapshots, `Fixtures/Polymarket` (SHA-256, `-text`), сгенерированные артефакты.
- Публичный HTTP-контракт в #26 не расширяется (это задача #27, требующая отдельного разрешения).
- Commit, push, branch, PR — только по отдельному разрешению пользователя.
- Не включать payload, connection strings, токены в код, логи и тесты.

## Suggested skills

- `polymarketlab-feature` — многомодульная фича, изучить архитектурные границы.
- `writing-plans` — задача затрагивает Core/Infrastructure/lifecycle и несколько этапов.
- `tdd` — test-first: gate tests -> reader -> lifecycle.
- `systematic-debugging` — при падении PostgreSQL/кардинальности.
- `polymarket-integration` — только если понадобится перепроверка внешних Polymarket-контрактов.
