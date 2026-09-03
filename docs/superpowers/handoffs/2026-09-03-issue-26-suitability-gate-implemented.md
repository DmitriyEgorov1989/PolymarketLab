# Handoff: issue #26 — normalization suitability gate (реализация завершена)

## Статус

- Ветка: `feature/first-full-five-minute-market`; базовый `HEAD` реализации = `55501c3`.
- Задача `#26` «Добавить session snapshot normalization suitability gate» **реализована полностью и не закоммичена**. Все изменения в рабочем дереве.
- План работ: `docs/superpowers/plans/2026-09-03-normalization-suitability-gate.md` (Tasks 1–5 выполнены).
- Предыдущий контекст `#34` и критерии `#26`: `docs/superpowers/handoffs/2026-09-03-issue-26-normalization-suitability-gate.md`.

## Что реализовано

После durable raw equality (`CollectorRawDatasetCompletionCoordinator`, задача `#34`) session попадала в `Stopping/AwaitingNormalization`, и никто не выполнял терминальный переход. Теперь тот же tick `ResolutionConsensusBackgroundService` (1 сек) маршрутизирует эту фазу в новый `CollectorNormalizationSuitabilityCoordinator`:

- **Ready**: один PostgreSQL statement (`NormalizationSuitabilityReader`) доказывает `RawCount = LedgerCount = ProcessedCount > 0` snapshot-версии `CollectorSession.ProjectionVersion` и strict WS `market_resolved` provenance (exact raw id, item index, version, event type, epoch, winner, signal time) → CAS `Stopping -> Stopped/MarketClosed` с expected status `Stopping`.
- **Waiting**: `Pending`/`Processing`/missing rows при `now < AwaitingNormalizationAt + 5m` → success без мутаций. `AwaitingNormalizationAt` устойчиво фиксируется после successful raw equality.
- **Invalid**: `Unsupported`/`Invalid`/terminal `Failed`, mismatch snapshot/runtime version, legacy `null` version, отсутствующий `AwaitingNormalizationAt`, неверный provenance, timeout, ошибка read, CAS-конфликты → `ICollectorSessionInvalidationCoordinator` с `PersistenceFailure` → cleanup `Failed`.
- Deadline `AwaitingNormalizationAt + 5m` проверяется до чтения ledger: после истечения срока даже полностью обработанный dataset инвалидируется как timeout без лишнего read.
- Идемпотентность: `Stopped/MarketClosed`, `Invalidating`, `Failed` при входе/после CAS-конфликта → success без invalidation. `ICollectorRuntime.StopAsync` в gate не вызывается.
- Empty root array = `Processed` без normalized events; лишние ledger rows других версий игнорируются.

## Файлы

Создано:

- `PolymarketLab.DataCollection.Core/Ports/Dtos/NormalizationSuitability.cs`
- `PolymarketLab.DataCollection.Core/Ports/INormalizationSuitabilityReader.cs`
- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorNormalizationSuitability/ICollectorNormalizationSuitabilityCoordinator.cs`
- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorNormalizationSuitability/CollectorNormalizationSuitabilityCoordinator.cs`
- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorNormalizationSuitability/CollectorNormalizationSuitabilityErrors.cs`
- `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/CollectorNormalizationSuitability/CollectorNormalizationSuitabilityCoordinatorTests.cs` (16 тестов)
- `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Repositories/Normalization/NormalizationSuitabilityReader.cs`
- `PolymarketLab.DataCollection.Infrastructure.Tests/Integration/Postgres/NormalizationSuitabilityReaderPostgreSqlTests.cs` (12 тестов)
- `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Migrations/20260903125150_PersistAwaitingNormalizationDeadline.cs` (+Designer) — nullable `awaiting_normalization_at`.

Изменено: `CollectorSession.cs` (`AwaitingNormalizationAt`, `MarkAwaitingNormalization(DateTimeOffset)`), `CollectorSessionErrors.cs`, `CollectorSessionConfiguration.cs`, `DataCollectionDbContextModelSnapshot.cs`, `CollectorRawDatasetCompletionCoordinator.cs` (штампует время входа), `ResolutionConsensusCoordinator.cs` (routing), DI-файлы Core/Infrastructure, DI-тесты, `CollectorSessionTests`, `CollectorSessionRepositoryPostgreSqlTests`, `CollectorRawDatasetCompletionCoordinatorTests`, `docs/agent-context.md`, `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/README.md`.

## Проверки (все зелёные)

- Узкие: suitability 16/16, routing+completion+invalidation 51/51; Core.Tests: 380; Infrastructure.Tests: 520 (Docker daemon доступен); solution: 1064; build: 0 ошибок, 0 предупреждений; `git diff --check` чист.

## Осторожности для следующей сессии

- Commit/push/PR только с явного разрешения пользователя. `git add` точечно, не широким `git add .`.
- Чужие незавершённые изменения сохранены и НЕ входят в задачу: `.harness/*` (включая `harness.lock`, `health.ps1`, `skills/REGISTRY.md`, новый `licenses/…`, `skills/handoff/`), `AGENTS.md`, migration `20260831121534_PersistConnectionEpochAndExactRawAccounting.*`.
- HTTP-контракт, frontend и hosted-service ordering не менялись. Задача `#27` (публичный HTTP-контракт status/normalization) — отдельная и требует разрешения.
- Legacy session в Core-тесте материализуется рефлексией (приватный конструктор + приватные сеттеры) — такова осознанная замена EF-техники на уровне Core.
- Перед работой читать `docs/agent-context.md`, `docs/normalizer-input-contract.md`, `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/README.md`.

## Suggested skills

- `code-review` — для ревью изменений `#26` перед commit.
- `polymarketlab-feature` — для продолжения работ по Data Collection.
- `tdd` — для следующих вертикальных срезов (`#27` и далее).
- `systematic-debugging` — при падении PostgreSQL/cardinality-тестов.
