# Session Snapshot Normalization Suitability Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `polymarketlab-feature` and `tdd` to implement this plan task-by-task. If `superpowers:subagent-driven-development` or `superpowers:executing-plans` is available, it may be used, but do not install missing skills automatically. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** После durable raw equality доказать, что каждый raw message одной `CollectorSession` обработан её immutable snapshot-версией normalizer, и только после этого завершить session как пригодную `Stopped/MarketClosed`.

**Architecture:** Существующий lifecycle tick маршрутизирует `Stopping/AwaitingNormalization` в новый application coordinator. Coordinator сравнивает snapshot `ProjectionVersion` с активной runtime version, получает одним PostgreSQL statement согласованный session-scoped снимок ledger/provenance, ожидает незавершённую обработку до абсолютного deadline и использует существующий durable invalidation flow для любого недоказанного или ошибочного dataset. Новый normalizer, hosted service, HTTP endpoint и схема БД не создаются.

**Tech Stack:** .NET 10, C# 14, CSharpFunctionalExtensions, EF Core 10, Npgsql/PostgreSQL, xUnit, FluentAssertions.

**Spec:** GitHub issue `#26`; итоговые спецификации `#14` и `#12`; roadmap `docs/superpowers/plans/2026-08-27-first-full-five-minute-market-roadmap.md`, Task 11; текущий handoff `docs/superpowers/handoffs/2026-09-03-issue-26-normalization-suitability-gate.md`.

## Global Constraints

- Перед изменением кода прочитать `AGENTS.md`, `docs/agent-context.md`, `docs/normalizer-input-contract.md` и `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/README.md`.
- Серверная часть и фактические controller/DTO являются источником истины; публичный HTTP-контракт в этой задаче не менять.
- Deadline gate равен `AwaitingNormalizationAt + 5 минут`; `AwaitingNormalizationAt` устойчиво фиксирует завершение successful raw drain и вход в `Stopping/AwaitingNormalization`.
- Snapshot version брать только из `CollectorSession.ProjectionVersion`; значение `null` у legacy session ведёт в invalidation, а не подменяется активной версией.
- Любой runtime mismatch между `CollectorSession.ProjectionVersion` и `IProjectionVersionProvider.ProjectionVersion` немедленно ведёт в invalidation.
- Ledger имеет один row на весь raw message, а не на каждый элемент root JSON array; `Processed` с нулём normalized events допустим.
- Strict WebSocket resolution provenance обязан указывать на существующие `RawMessageId` и `RawItemIndex` normalized event `market_resolved` snapshot-версии, а parent ledger row обязан быть `Processed`.
- Дополнительные ledger/projection rows других versions допустимы для replay и не учитываются при доказательстве snapshot-version.
- `Pending`, `Processing` и отсутствие snapshot ledger row означают ожидание до deadline; `Invalid`, `Unsupported`, terminal `Failed` означают немедленную invalidation.
- Только успешный gate выполняет `Stopped/MarketClosed`; manual Stop продолжает существующий destructive flow `Invalidating/Cleaning -> Failed`.
- Не добавлять dependencies, новый hosted service, новый lifecycle loop, authentication, trading, multi-instance ownership или frontend changes.
- Создать EF migration только штатным `dotnet ef` после разрешения владельца; не менять вручную migration snapshots. Не менять `Fixtures/Polymarket`, `bin`, `obj`, `node_modules`, `dist` и другие сгенерированные файлы.
- Не затрагивать существующие пользовательские изменения в `.harness/*`, `AGENTS.md` и migration `20260831121534_PersistConnectionEpochAndExactRawAccounting.*`.
- Не выполнять commit, push, branch, rebase или PR без отдельного разрешения пользователя.
- Не включать raw payload, credentials, connection strings и stack traces в errors, logs, HTTP или audit.

---

## Current Behavior and Target Examples

Сейчас `CollectorRawDatasetCompletionCoordinator` доказывает:

```text
MessagesReceived
= MessagesEnqueued
= MessagesPersisted
= count(raw_market_messages for SessionId)
> 0
```

После этого он выполняет CAS `Stopping/DrainingRaw -> Stopping/AwaitingNormalization`. `ResolutionConsensusCoordinator.TickCoreAsync` возвращает success для любой session не в `Running`, поэтому terminal transition отсутствует.

**Успех:** `1250` raw rows, `1250` ledger rows version `3`, все `Processed`; strict WS observation ссылается на normalized `market_resolved` с теми же `RawMessageId`, `RawItemIndex` и version `3`. Session CAS-переходит в `Stopped`, `phase = null`, `StopReason = MarketClosed`.

**Ожидание:** `1240 Processed`, `7 Pending`, `3 Processing` при `now < AwaitingNormalizationAt + 5 минут`. Session остаётся `Stopping/AwaitingNormalization`, coordinator возвращает success, следующий tick повторяет read.

**Ошибка:** один row `Unsupported`, `Invalid` или `Failed`; runtime version `4` при snapshot version `3`; неверный resolution provenance; либо незавершённость ровно на deadline. Coordinator сохраняет точный безопасный `FailureCode` через `ICollectorSessionInvalidationCoordinator`; cleanup переводит session в `Failed`.

**Пустой массив:** raw message с root `[]` имеет ledger `Processed`, но не имеет `normalized_events`. Он считается успешно обработанным и не вводит minimum event count.

## File Map

**Create:**

- `PolymarketLab.DataCollection.Core/Ports/Dtos/NormalizationSuitability.cs` — согласованный набор счётчиков и resolution provenance из одного PostgreSQL snapshot.
- `PolymarketLab.DataCollection.Core/Ports/INormalizationSuitabilityReader.cs` — application-facing read port.
- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorNormalizationSuitability/ICollectorNormalizationSuitabilityCoordinator.cs` — lifecycle contract gate.
- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorNormalizationSuitability/CollectorNormalizationSuitabilityCoordinator.cs` — orchestration, deadline, CAS completion и invalidation.
- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorNormalizationSuitability/CollectorNormalizationSuitabilityErrors.cs` — безопасные точные failure codes.
- `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/CollectorNormalizationSuitability/CollectorNormalizationSuitabilityCoordinatorTests.cs` — deterministic application tests с controllable `TimeProvider`.
- `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Repositories/Normalization/NormalizationSuitabilityReader.cs` — один session-scoped PostgreSQL read.
- `PolymarketLab.DataCollection.Infrastructure.Tests/Integration/Postgres/NormalizationSuitabilityReaderPostgreSqlTests.cs` — cardinality/version/provenance integration tests.

**Modify:**

- `PolymarketLab.DataCollection.Core/Application/UseCases/ResolutionConsensus/ResolutionConsensusCoordinator.cs` — route `Stopping/AwaitingNormalization` в gate до проверки `Running`.
- `PolymarketLab.DataCollection.Core/Application/DependencyInjection/DataCollectionApplicationDependencyInjection.cs` — scoped registration coordinator.
- `PolymarketLab.DataCollection.Core.Tests/Application/DependencyInjection/DataCollectionApplicationDependencyInjectionTests.cs` — registration assertion.
- `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/ResolutionConsensus/ResolutionConsensusCoordinatorTests.cs` — lifecycle routing assertion и constructor fixture.
- `PolymarketLab.DataCollection.Infrastructure/DependencyInjection/DataCollectionInfrastructureDependencyInjection.cs` — scoped reader registration.
- `PolymarketLab.DataCollection.Infrastructure.Tests/DependencyInjection/DataCollectionInfrastructureDependencyInjectionTests.cs` — scoped reader registration и сохранение единственного lifecycle hosted service.
- `docs/agent-context.md` — terminal normalization gate invariant.
- `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/README.md` — полный lifecycle после raw drain.

**Do not modify:** controller/DTO, `ResolutionConsensusBackgroundService`, normalizers, cleanup implementation и frontend. `CollectorSession`/configuration и новая migration меняются только для `AwaitingNormalizationAt`.

---

### Task 1: Define the Core Evidence Contract

**Files:**

- Create: `PolymarketLab.DataCollection.Core/Ports/Dtos/NormalizationSuitability.cs`
- Create: `PolymarketLab.DataCollection.Core/Ports/INormalizationSuitabilityReader.cs`
- Test: `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/CollectorNormalizationSuitability/CollectorNormalizationSuitabilityCoordinatorTests.cs`

**Interfaces:**

- Consumes: `CollectorSessionId` from SharedKernel and PostgreSQL `COUNT(*)` semantics.
- Produces: `Task<NormalizationSuitability> ReadAsync(CollectorSessionId sessionId, int projectionVersion, CancellationToken cancellationToken)`.

- [ ] **Step 1: Add a failing compile-time test fixture that constructs the evidence DTO**

Add the first test file and prove the intended count types/properties:

```csharp
using FluentAssertions;
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using Xunit;

namespace PolymarketLab.DataCollection.Core.Tests.Application.UseCases.CollectorNormalizationSuitability;

public sealed class CollectorNormalizationSuitabilityCoordinatorTests
{
    [Fact]
    public void Suitability_WithMissingSnapshotRows_ShouldExposeMissingCount()
    {
        var suitability = new NormalizationSuitability(
            RawCount: 1250,
            LedgerCount: 1240,
            ProcessedCount: 1230,
            PendingCount: 7,
            ProcessingCount: 3,
            UnsupportedCount: 0,
            InvalidCount: 0,
            FailedCount: 0,
            ResolutionRawItemProcessed: false);

        suitability.MissingCount.Should().Be(10);
    }
}
```

- [ ] **Step 2: Run the narrow test and verify the expected compile failure**

Run:

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter FullyQualifiedName~CollectorNormalizationSuitabilityCoordinatorTests --no-restore
```

Expected: compile failure because `NormalizationSuitability` does not exist.

- [ ] **Step 3: Create the immutable evidence DTO**

```csharp
namespace PolymarketLab.DataCollection.Core.Ports.Dtos;

/// <summary>
/// Согласованный снимок обработки raw-сообщений одной collector session
/// указанной snapshot-версией нормализации.
/// </summary>
/// <param name="RawCount">Количество raw-сообщений session.</param>
/// <param name="LedgerCount">Количество ledger rows указанной версии для raw-сообщений session.</param>
/// <param name="ProcessedCount">Количество ledger rows со статусом <c>Processed</c>.</param>
/// <param name="PendingCount">Количество ledger rows со статусом <c>Pending</c>.</param>
/// <param name="ProcessingCount">Количество ledger rows со статусом <c>Processing</c>.</param>
/// <param name="UnsupportedCount">Количество ledger rows со статусом <c>Unsupported</c>.</param>
/// <param name="InvalidCount">Количество ledger rows со статусом <c>Invalid</c>.</param>
/// <param name="FailedCount">Количество ledger rows со статусом <c>Failed</c>.</param>
/// <param name="ResolutionRawItemProcessed">
/// <see langword="true" />, если strict WebSocket resolution observation ссылается
/// на обработанный parent raw и normalized <c>market_resolved</c> item этой версии.
/// </param>
public sealed record NormalizationSuitability(
    long RawCount,
    long LedgerCount,
    long ProcessedCount,
    long PendingCount,
    long ProcessingCount,
    long UnsupportedCount,
    long InvalidCount,
    long FailedCount,
    bool ResolutionRawItemProcessed)
{
    /// <summary>Количество raw-сообщений без ledger row указанной версии.</summary>
    public long MissingCount => RawCount - LedgerCount;
}
```

- [ ] **Step 4: Create the reader port with full XML semantics**

```csharp
using PolymarketLab.DataCollection.Core.Ports.Dtos;
using PolymarketLab.SharedKernel.DomainModels.Ids;

namespace PolymarketLab.DataCollection.Core.Ports;

/// <summary>Читает доказательства завершения нормализации одной collector session.</summary>
public interface INormalizationSuitabilityReader
{
    /// <summary>
    /// Одним согласованным persistence read получает raw/ledger cardinality,
    /// status counts и strict resolution provenance указанной версии.
    /// </summary>
    /// <param name="sessionId">Идентификатор проверяемой session.</param>
    /// <param name="projectionVersion">Положительная snapshot-версия session.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Согласованный снимок без raw payload.</returns>
    Task<NormalizationSuitability> ReadAsync(
        CollectorSessionId sessionId,
        int projectionVersion,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Run the narrow test and verify success**

Run the command from Step 2. Expected: PASS for the DTO test.

- [ ] **Step 6: Review checkpoint**

Verify that all counts are `long`, the port contains no EF/Npgsql types, XML comments explain `ResolutionRawItemProcessed`, and no existing files outside task scope changed. Do not commit without user permission.

---

### Task 2: Implement the Application Gate Test-First

**Files:**

- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorNormalizationSuitability/ICollectorNormalizationSuitabilityCoordinator.cs`
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorNormalizationSuitability/CollectorNormalizationSuitabilityCoordinator.cs`
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorNormalizationSuitability/CollectorNormalizationSuitabilityErrors.cs`
- Modify: `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/CollectorNormalizationSuitability/CollectorNormalizationSuitabilityCoordinatorTests.cs`

**Interfaces:**

- Consumes: `INormalizationSuitabilityReader`, `ICollectorSessionRepository`, `IProjectionVersionProvider`, `ICollectorSessionInvalidationCoordinator`, `TimeProvider`, `ILogger<CollectorNormalizationSuitabilityCoordinator>`.
- Produces: `Task<UnitResult<Error>> EvaluateAsync(CollectorSessionId sessionId, CancellationToken cancellationToken)`.

- [ ] **Step 1: Add the coordinator interface**

```csharp
using CSharpFunctionalExtensions;
using PolymarketLab.SharedKernel.DomainModels.Ids;
using PolymarketLab.SharedKernel.Errors;

namespace PolymarketLab.DataCollection.Core.Application.UseCases.CollectorNormalizationSuitability;

/// <summary>Доказывает пригодность normalized dataset snapshot-версии session.</summary>
public interface ICollectorNormalizationSuitabilityCoordinator
{
    /// <summary>
    /// Ожидает незавершённую нормализацию до deadline, инвалидирует недоказанный
    /// dataset и завершает session только при полном доказательстве пригодности.
    /// </summary>
    /// <param name="sessionId">Идентификатор session в <c>Stopping/AwaitingNormalization</c>.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Успех ожидания/завершения либо исходная ожидаемая ошибка.</returns>
    Task<UnitResult<Error>> EvaluateAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Add failing tests for the complete decision matrix**

Build a fixture following the in-memory repository pattern in `CollectorRawDatasetCompletionCoordinatorTests`. The fixture must create a session in exact state `Stopping/AwaitingNormalization`, expose a mutable fake `INormalizationSuitabilityReader`, fake `IProjectionVersionProvider`, fake invalidation coordinator, `FakeTimeProvider`, update call history and current session.

Add these exact tests:

```csharp
[Fact]
public async Task EvaluateAsync_WithAllProcessedAndResolutionProvenance_ShouldStopAsMarketClosed()
```

Assert `Status == Stopped`, `Phase == null`, `StopReason == MarketClosed`, one reader call with version `3`, one CAS with expected status `Stopping`, zero invalidations.

```csharp
[Theory]
[InlineData(7, 3, 0)]
[InlineData(0, 0, 10)]
public async Task EvaluateAsync_BeforeDeadlineWithIncompleteLedger_ShouldWait(
    long pending,
    long processing,
    long missing)
```

Construct counts so `RawCount = 1250`, `LedgerCount = 1250 - missing`, `ProcessedCount = RawCount - pending - processing - missing`; assert session remains `Stopping/AwaitingNormalization`, no update and no invalidation.

```csharp
[Theory]
[InlineData(1, 0, 0, "collector.normalization_suitability.unsupported")]
[InlineData(0, 1, 0, "collector.normalization_suitability.invalid")]
[InlineData(0, 0, 1, "collector.normalization_suitability.failed")]
public async Task EvaluateAsync_WithTerminalLedgerStatus_ShouldInvalidateImmediately(
    long unsupported,
    long invalid,
    long failed,
    string expectedCode)
```

Assert exact failure code, `PersistenceFailure` invalidation category, no successful stop.

```csharp
[Fact]
public async Task EvaluateAsync_AtExactDeadlineWithIncompleteLedger_ShouldInvalidateAsTimeout()
```

Set `UtcNow = AwaitingNormalizationAt + TimeSpan.FromMinutes(5)` and assert code `collector.normalization_suitability.timeout`.

```csharp
[Fact]
public async Task EvaluateAsync_OneTickBeforeDeadlineWithIncompleteLedger_ShouldWait()
```

Set `UtcNow = deadline - TimeSpan.FromTicks(1)` and assert no invalidation.

```csharp
[Fact]
public async Task EvaluateAsync_WithProcessedCardinalityAndMissingResolutionProvenance_ShouldInvalidate()
```

Use all counts `Processed` and `ResolutionRawItemProcessed = false`; assert code `collector.normalization_suitability.resolution_provenance_invalid`.

```csharp
[Fact]
public async Task EvaluateAsync_WithRuntimeVersionMismatch_ShouldInvalidateWithoutReadingLedger()
```

Session version `3`, provider version `4`; assert reader call count `0` and code `collector.normalization_suitability.projection_version_mismatch`.

```csharp
[Fact]
public async Task EvaluateAsync_WithLegacyNullProjectionVersion_ShouldInvalidateWithoutReadingLedger()
```

Materialize a legacy aggregate using the same EF/in-memory technique already used by repository tests; assert code `collector.normalization_suitability.projection_version_missing`.

```csharp
[Fact]
public async Task EvaluateAsync_WithMissingAwaitingNormalizationAt_ShouldInvalidateWithoutReadingLedger()
```

Create `Stopping/AwaitingNormalization` without the durable wait start; assert reader call count `0` and code `collector.normalization_suitability.awaiting_normalization_at_missing`.

```csharp
[Fact]
public async Task EvaluateAsync_WhenReaderThrows_ShouldDurablyInvalidateAndReturnSafeFailure()
```

Throw `InvalidOperationException("database read failed")`; assert logger receives the exception, audit failure is `collector.normalization_suitability.read_failed`, and exception text is not copied to the returned safe message.

```csharp
[Fact]
public async Task EvaluateAsync_WhenCompletionCasConflicts_ShouldReloadAndRetryThreeTimes()
```

Return `ConcurrencyConflict` three times; assert three updates, re-reads between attempts and safe `state_transition_conflict` handling through invalidation.

```csharp
[Fact]
public async Task EvaluateAsync_WhenAnotherTickAlreadyStoppedSession_ShouldSucceedIdempotently()
```

After first CAS conflict return current `Stopped/MarketClosed`; assert success and no invalidation.

- [ ] **Step 3: Run the tests and verify failure for missing coordinator**

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter FullyQualifiedName~CollectorNormalizationSuitabilityCoordinatorTests --no-restore
```

Expected: compile/test failures because coordinator and errors are not implemented.

- [ ] **Step 4: Implement exact safe errors**

Create errors with these codes and `ErrorType` values:

```text
collector.normalization_suitability.session_not_found                 NotFound
collector.normalization_suitability.awaiting_normalization_at_missing Failure
collector.normalization_suitability.projection_version_missing        Failure
collector.normalization_suitability.projection_version_mismatch       Conflict
collector.normalization_suitability.unsupported                       Failure
collector.normalization_suitability.invalid                           Failure
collector.normalization_suitability.failed                            Failure
collector.normalization_suitability.resolution_provenance_invalid     Failure
collector.normalization_suitability.timeout                           Failure
collector.normalization_suitability.read_failed                       Failure
collector.normalization_suitability.state_transition_conflict         Conflict
```

Messages may contain `sessionId`, numeric counts and versions, but never raw payload, normalization `ErrorMessage` or external response body.

- [ ] **Step 5: Implement coordinator decision order**

Use this order exactly:

```csharp
private static readonly TimeSpan NormalizationTimeout = TimeSpan.FromMinutes(5);
private const int MaximumUpdateAttempts = 3;

// 1. Load session.
// 2. Treat existing Stopped/MarketClosed as idempotent success.
// 3. Require Stopping/AwaitingNormalization.
// 4. Require non-null positive snapshot ProjectionVersion.
// 5. Require snapshot version == current IProjectionVersionProvider.ProjectionVersion.
// 6. Require non-null AwaitingNormalizationAt.
// 7. If now >= AwaitingNormalizationAt + 5 minutes, invalidate as timeout before read.
// 8. Read one NormalizationSuitability snapshot; catch non-cancellation exceptions.
// 9. Invalidate immediately for Unsupported, Invalid or Failed counts > 0.
// 10. Determine allProcessed from exact raw/ledger/Processed cardinality.
// 11. If allProcessed but resolution provenance is false, invalidate immediately.
// 12. If allProcessed and provenance is true, CAS Stop(..., MarketClosed).
// 13. Otherwise return success without mutation so next tick waits.
```

Define exact completeness as:

```csharp
private static bool IsFullyProcessed(NormalizationSuitability value) =>
    value.RawCount > 0
    && value.LedgerCount == value.RawCount
    && value.ProcessedCount == value.RawCount
    && value.PendingCount == 0
    && value.ProcessingCount == 0
    && value.UnsupportedCount == 0
    && value.InvalidCount == 0
    && value.FailedCount == 0
    && value.MissingCount == 0;
```

Implement invalidation through the existing coordinator:

```csharp
var invalidation = await invalidationCoordinator.InvalidateAsync(
    session.Id,
    timeProvider.GetUtcNow(),
    CollectorStopReason.PersistenceFailure,
    failure,
    cancellationToken);
```

Do not call `ICollectorRuntime.StopAsync`: producer was already stopped by raw completion before entering `AwaitingNormalization`.

Implement successful terminal transition with the existing aggregate and CAS:

```csharp
var transition = session.Stop(
    timeProvider.GetUtcNow(),
    CollectorStopReason.MarketClosed);

var update = await sessionRepository.TryUpdateAsync(
    session,
    CollectorSessionStatus.Stopping,
    cancellationToken);
```

On CAS conflict reload and retry up to three times. If reload returns `Stopped/MarketClosed`, return success. If it returns `Invalidating` or `Failed`, return success because another lifecycle actor already established a terminal/invalidation outcome. Other states produce `state_transition_conflict` and must not be overwritten unconditionally.

- [ ] **Step 6: Run the narrow Core tests until green**

Run the command from Step 3. Expected: all coordinator tests PASS.

- [ ] **Step 7: Run the existing neighboring Core tests**

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter "FullyQualifiedName~CollectorRawDatasetCompletionCoordinatorTests|FullyQualifiedName~CollectorSessionInvalidationCoordinatorTests|FullyQualifiedName~CollectorSessionTests" --no-restore
```

Expected: PASS; raw completion still stops at `AwaitingNormalization`, invalidation remains durable, domain lifecycle remains valid.

- [ ] **Step 8: Review checkpoint**

Verify `OperationCanceledException` propagates, all expected failures use `Result` rather than exceptions, reader failure is logged without payload, and no unconditional session update exists. Do not commit without user permission.

---

### Task 3: Implement One PostgreSQL Suitability Read

**Files:**

- Create: `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Repositories/Normalization/NormalizationSuitabilityReader.cs`
- Create: `PolymarketLab.DataCollection.Infrastructure.Tests/Integration/Postgres/NormalizationSuitabilityReaderPostgreSqlTests.cs`

**Interfaces:**

- Consumes: `INormalizationSuitabilityReader`, `DataCollectionDbContext`, existing tables `raw_market_messages`, `raw_message_normalizations`, `normalized_events`, `resolution_observations`, `collector_sessions`.
- Produces: one PostgreSQL statement returning one `NormalizationSuitability`.

- [ ] **Step 1: Create PostgreSQL integration-test setup**

Follow `NormalizationBacklogReaderPostgreSqlTests` for `PostgreSqlFixture`, migrated per-test database, `DbContextOptionsBuilder<DataCollectionDbContext>.UseNpgsql`, parameterized `NpgsqlCommand`, and cleanup ownership.

Seed a real `CollectorSession` in `Stopping/AwaitingNormalization` with:

```text
ProjectionVersion = 3
EventEndsAt = 2026-09-03T12:05:00Z
ResolutionSignaledAt = 2026-09-03T12:05:01Z
ResolutionConnectionEpoch = 1
WinningTokenId = 1001
WinningOutcome = Yes
```

Seed raw, ledger, normalized header and resolution observation rows only with SQL parameters. Never inline payload text; use a minimal `byte[]` parameter.

- [ ] **Step 2: Add the exact integration-test matrix**

Add these tests:

```csharp
[Fact]
public async Task ReadAsync_WithAllSnapshotRowsProcessed_ShouldReturnExactCountsAndResolutionProvenance()
```

Use three raw messages, three version `3` Processed ledger rows, one exact version `3` normalized `market_resolved`, exact strict observation. Expect all counts and `ResolutionRawItemProcessed = true`.

```csharp
[Fact]
public async Task ReadAsync_WithMixedStatusesAndMissingRow_ShouldReturnSessionScopedCounts()
```

Use six raw rows: Processed, Pending, Processing, Unsupported, Invalid and one missing; add unrelated session rows. Assert `RawCount = 6`, `LedgerCount = 5`, each status count exact, `MissingCount = 1`, unrelated rows ignored.

```csharp
[Fact]
public async Task ReadAsync_WithAdditionalOtherVersionRows_ShouldUseOnlyRequestedVersion()
```

For the same raw rows seed complete version `3` plus version `2`/`4` rows with different statuses. Assert only version `3` counts determine snapshot.

```csharp
[Fact]
public async Task ReadAsync_WithProcessedEmptyRootArray_ShouldNotRequireNormalizedEvent()
```

Seed a non-resolution raw with Processed ledger and no normalized event, plus a separate valid resolution raw/event. Assert full Processed cardinality and provenance true.

```csharp
[Theory]
[InlineData("wrong_raw")]
[InlineData("wrong_item")]
[InlineData("wrong_version")]
[InlineData("wrong_event_type")]
[InlineData("wrong_epoch")]
[InlineData("wrong_winner")]
[InlineData("wrong_signal_time")]
public async Task ReadAsync_WithMismatchedStrictResolutionProvenance_ShouldReturnFalse(string mismatch)
```

Mutate exactly one join predicate per row; assert raw/ledger counts stay complete while `ResolutionRawItemProcessed = false`.

```csharp
[Fact]
public async Task ReadAsync_WhenCancelled_ShouldPropagateCancellation()
```

Cancel token before call and assert `OperationCanceledException`.

- [ ] **Step 3: Run the integration test and verify failure**

```powershell
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter FullyQualifiedName~NormalizationSuitabilityReaderPostgreSqlTests --no-restore
```

Expected: compile failure because reader does not exist. Docker daemon must be available.

- [ ] **Step 4: Implement one-statement reader**

Use `dbContext.Database.GetDbConnection()`, `OpenConnectionAsync`, `DbCommand`, parameters and `CloseConnectionAsync` in `finally`, matching `NormalizationBacklogReader`.

The statement must be one PostgreSQL command and therefore one READ COMMITTED statement snapshot. Use this shape:

```sql
WITH target_session AS
(
    SELECT
        id,
        resolution_signaled_at,
        resolution_connection_epoch,
        winning_token_id,
        winning_outcome
    FROM data_collection.collector_sessions
    WHERE id = @session_id
),
session_raw AS
(
    SELECT raw.id
    FROM data_collection.raw_market_messages AS raw
    WHERE raw.session_id = @session_id
),
snapshot_ledger AS
(
    SELECT normalization.raw_message_id, normalization.status
    FROM data_collection.raw_message_normalizations AS normalization
    INNER JOIN session_raw AS raw
        ON raw.id = normalization.raw_message_id
    WHERE normalization.projection_version = @projection_version
),
counts AS
(
    SELECT
        (SELECT COUNT(*)::bigint FROM session_raw) AS raw_count,
        COUNT(*)::bigint AS ledger_count,
        COUNT(*) FILTER (WHERE status = @processed_status)::bigint AS processed_count,
        COUNT(*) FILTER (WHERE status = @pending_status)::bigint AS pending_count,
        COUNT(*) FILTER (WHERE status = @processing_status)::bigint AS processing_count,
        COUNT(*) FILTER (WHERE status = @unsupported_status)::bigint AS unsupported_count,
        COUNT(*) FILTER (WHERE status = @invalid_status)::bigint AS invalid_count,
        COUNT(*) FILTER (WHERE status = @failed_status)::bigint AS failed_count
    FROM snapshot_ledger
)
SELECT
    counts.raw_count,
    counts.ledger_count,
    counts.processed_count,
    counts.pending_count,
    counts.processing_count,
    counts.unsupported_count,
    counts.invalid_count,
    counts.failed_count,
    EXISTS
    (
        SELECT 1
        FROM target_session AS session
        INNER JOIN data_collection.resolution_observations AS observation
            ON observation.session_id = session.id
           AND observation.source = @websocket_source
           AND observation.status = @terminal_observation_status
           AND observation.observed_at = session.resolution_signaled_at
           AND observation.connection_epoch = session.resolution_connection_epoch
           AND observation.winner_token_id = session.winning_token_id
           AND observation.winner_outcome = session.winning_outcome
        INNER JOIN session_raw AS raw
            ON raw.id = observation.raw_message_id
        INNER JOIN snapshot_ledger AS ledger
            ON ledger.raw_message_id = raw.id
           AND ledger.status = @processed_status
        INNER JOIN data_collection.normalized_events AS normalized
            ON normalized.raw_message_id = observation.raw_message_id
           AND normalized.raw_item_index = observation.raw_item_index
           AND normalized.projection_version = @projection_version
           AND normalized.event_type = 'market_resolved'
    ) AS resolution_raw_item_processed
FROM counts;
```

Parameterize all enum numeric values. Validate `projectionVersion > 0` with `ArgumentOutOfRangeException.ThrowIfNegativeOrZero`. Do not select payload or ledger error messages.

- [ ] **Step 5: Run the PostgreSQL tests until green**

Run the command from Step 3. Expected: all reader tests PASS.

- [ ] **Step 6: Review query invariants**

Confirm the reader:

```text
uses exactly one command;
scopes every raw/ledger/provenance row by session;
filters only requested projection version;
does not reject harmless extra versions;
requires exact raw item for market_resolved;
does not require normalized_events for ordinary Processed empty arrays;
does not read raw payload.
```

Do not commit without user permission.

---

### Task 4: Route Lifecycle Ticks and Wire Dependency Injection

**Files:**

- Modify: `PolymarketLab.DataCollection.Core/Application/UseCases/ResolutionConsensus/ResolutionConsensusCoordinator.cs`
- Modify: `PolymarketLab.DataCollection.Core/Application/DependencyInjection/DataCollectionApplicationDependencyInjection.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/DependencyInjection/DataCollectionInfrastructureDependencyInjection.cs`
- Modify: `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/ResolutionConsensus/ResolutionConsensusCoordinatorTests.cs`
- Modify: `PolymarketLab.DataCollection.Core.Tests/Application/DependencyInjection/DataCollectionApplicationDependencyInjectionTests.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure.Tests/DependencyInjection/DataCollectionInfrastructureDependencyInjectionTests.cs`

**Interfaces:**

- Consumes: `ICollectorNormalizationSuitabilityCoordinator` and `INormalizationSuitabilityReader` from Tasks 1–3.
- Produces: existing `IResolutionConsensusCoordinator.TickAsync` handles both resolution and normalization terminal lifecycle without changing its public signature.

- [ ] **Step 1: Add failing lifecycle routing tests**

Extend the existing `ResolutionConsensusCoordinatorTests` fixture constructor with a fake `ICollectorNormalizationSuitabilityCoordinator`.

Add:

```csharp
[Fact]
public async Task TickAsync_WithAwaitingNormalization_ShouldEvaluateSuitabilityWithoutResolutionPolling()
```

Create `Stopping/AwaitingNormalization`; assert one suitability call, zero Gamma/CLOB/WebSocket scan/raw-completion calls.

```csharp
[Fact]
public async Task TickAsync_WithDrainingRaw_ShouldNotEvaluateSuitabilityOrPollResolution()
```

Create `Stopping/DrainingRaw`; assert all downstream call counts zero because raw completion owns that phase.

```csharp
[Fact]
public async Task TickAsync_WhenSuitabilityFails_ShouldReturnItsFailure()
```

Return an expected error from the fake gate and assert exact error is propagated for background logging.

- [ ] **Step 2: Run the routing tests and verify failure**

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter FullyQualifiedName~ResolutionConsensusCoordinatorTests --no-restore
```

Expected: compile/test failure because the coordinator dependency and routing do not exist.

- [ ] **Step 3: Add minimal routing before the existing Running guard**

Inject `ICollectorNormalizationSuitabilityCoordinator normalizationSuitabilityCoordinator` and replace the current early return with:

```csharp
var session = await sessionRepository.GetExclusiveAsync(cancellationToken);
if (session is null)
    return UnitResult.Success<Error>();

if (session.Status == CollectorSessionStatus.Stopping
    && session.Phase == CollectorSessionPhase.AwaitingNormalization)
{
    return await normalizationSuitabilityCoordinator.EvaluateAsync(
        session.Id,
        cancellationToken);
}

if (session.Status != CollectorSessionStatus.Running)
    return UnitResult.Success<Error>();
```

Do not change polling, consensus, scanner, deadline or raw completion behavior below this block.

- [ ] **Step 4: Register Core services**

Add:

```csharp
services.AddScoped<
    ICollectorNormalizationSuitabilityCoordinator,
    CollectorNormalizationSuitabilityCoordinator>();
```

Update `DataCollectionApplicationDependencyInjectionTests` to assert service type, implementation type and `ServiceLifetime.Scoped`.

- [ ] **Step 5: Register Infrastructure reader**

Add:

```csharp
services.AddScoped<
    INormalizationSuitabilityReader,
    NormalizationSuitabilityReader>();
```

Add the corresponding scoped registration assertion to `DataCollectionInfrastructureDependencyInjectionTests`:

```csharp
descriptors.Should().ContainSingle(descriptor =>
    descriptor.ServiceType == typeof(INormalizationSuitabilityReader)
    && descriptor.ImplementationType == typeof(NormalizationSuitabilityReader)
    && descriptor.Lifetime == ServiceLifetime.Scoped);
```

- [ ] **Step 6: Run routing and DI tests until green**

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter "FullyQualifiedName~ResolutionConsensusCoordinatorTests|FullyQualifiedName~DataCollectionApplicationDependencyInjectionTests" --no-restore
```

Then run:

```powershell
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter FullyQualifiedName~DataCollectionInfrastructureDependencyInjectionTests --no-restore
```

Expected: PASS.

- [ ] **Step 7: Verify the existing hosted-service wiring unchanged**

```powershell
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter FullyQualifiedName~DataCollectionInfrastructureDependencyInjectionTests --no-restore
```

Expected: PASS; the test continues to find exactly one `ResolutionConsensusBackgroundService`, so one existing background loop still drives the lifecycle every `1 секунду`.

- [ ] **Step 8: Review checkpoint**

Confirm no second hosted service or timer was added, DI lifetimes are scoped for DbContext-backed services, and `Stopping/DrainingRaw` remains owned only by raw completion. Do not commit without user permission.

---

### Task 5: Documentation and Full Verification

**Files:**

- Modify: `docs/agent-context.md`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/README.md`
- Verify: all files created/modified in Tasks 1–4.

**Interfaces:**

- Consumes: completed behavior from Tasks 1–4.
- Produces: maintainable lifecycle documentation and evidence satisfying issue `#26`.

- [ ] **Step 1: Update agent context with the terminal invariant**

Add a concise Data Collection paragraph stating:

```text
После durable raw equality session остаётся Stopping/AwaitingNormalization до
AwaitingNormalizationAt + 5m. Один PostgreSQL statement доказывает exact Processed cardinality
snapshot ProjectionVersion и strict WS market_resolved provenance. Pending,
Processing и missing snapshot rows ожидаются до deadline; Invalid, Unsupported,
Failed, runtime version mismatch, provenance mismatch и timeout запускают durable
invalidation. Только успешный gate даёт Stopped/MarketClosed; empty root array может
быть Processed с нулём normalized events.
```

- [ ] **Step 2: Update Collector Runtime README lifecycle**

Extend the existing completion sequence from:

```text
CAS Stopping/AwaitingNormalization
```

to:

```text
CAS Stopping/AwaitingNormalization
-> repeated snapshot-version suitability read
-> Stopped/MarketClosed on exact Processed cardinality and resolution provenance
or
-> Invalidating/Cleaning -> Failed on terminal normalization failure or timeout
```

Document that the same `ResolutionConsensusBackgroundService` tick routes this phase and normalizer continues through its existing hosted service.

- [ ] **Step 3: Run the narrow test ladder**

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter FullyQualifiedName~CollectorNormalizationSuitabilityCoordinatorTests --no-restore

dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter "FullyQualifiedName~ResolutionConsensusCoordinatorTests|FullyQualifiedName~CollectorRawDatasetCompletionCoordinatorTests|FullyQualifiedName~CollectorSessionInvalidationCoordinatorTests" --no-restore

dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter FullyQualifiedName~NormalizationSuitabilityReaderPostgreSqlTests --no-restore
```

Expected: all PASS. The PostgreSQL test requires Docker daemon.

- [ ] **Step 4: Run project-level validation**

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --no-restore
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --no-restore
dotnet test .\PolymarketLab.slnx --no-restore
dotnet build .\PolymarketLab.slnx --no-restore
git diff --check
```

Expected: all tests and build pass; known existing NuGet warnings may remain but no new warning/error is introduced.

- [ ] **Step 5: Audit the final diff**

Run:

```powershell
git status --short
git diff -- PolymarketLab.DataCollection.Core PolymarketLab.DataCollection.Core.Tests PolymarketLab.DataCollection.Infrastructure PolymarketLab.DataCollection.Infrastructure.Tests docs
```

Verify:

```text
no migration changed;
no public HTTP DTO/controller changed;
no frontend changed;
no raw payload or secret appears;
unrelated .harness/AGENTS/migration edits remain untouched;
every issue #26 acceptance criterion maps to a passing test;
manual Stop still invalidates and never creates a suitable Stopped dataset.
```

- [ ] **Step 6: Prepare the completion report**

Report in Russian:

```text
причина изменений;
реализованный Ready/Waiting/Invalid lifecycle;
точный список изменённых файлов;
domain/application/infrastructure decisions;
тесты и команды с counts/results;
Docker/environment limitations;
deadline interpretation AwaitingNormalizationAt + 5 минут;
отсутствие migration и HTTP/frontend changes;
наличие сохранённых unrelated user changes.
```

Do not create a commit, push, branch or PR until the user explicitly authorizes it.

---

## Self-Review Checklist for the Implementing Agent

- [ ] `Stopping/AwaitingNormalization` обрабатывается существующим tick раз в `1 секунду`.
- [ ] Gate использует session snapshot version, а не только текущую global version.
- [ ] Runtime mismatch и legacy null version дают durable invalidation.
- [ ] PostgreSQL evidence читается одним statement и scoped по `SessionId`.
- [ ] Exact cardinality требует `RawCount = LedgerCount = ProcessedCount > 0`.
- [ ] `Pending`, `Processing` и missing row ждут только до deadline.
- [ ] `Invalid`, `Unsupported`, `Failed` инвалидируют немедленно.
- [ ] Strict WS resolution проверяет exact raw id, item index, version, event type, epoch, winner и signal time.
- [ ] Empty root array допускает `Processed` без normalized event.
- [ ] Extra replay versions не влияют на snapshot proof.
- [ ] Только успешный gate выполняет `Stopped/MarketClosed`.
- [ ] Manual Stop продолжает invalidation path.
- [ ] CAS conflicts перечитывают состояние и не выполняют unconditional update.
- [ ] Errors/logs не раскрывают raw payload или внешние ответы.
- [ ] Миграции, HTTP contract, frontend и hosted-service ordering не изменены.
- [ ] Узкие, Core, Infrastructure, solution tests, build и `git diff --check` выполнены.
