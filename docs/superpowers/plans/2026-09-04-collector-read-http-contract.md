# Collector Read HTTP Contract Implementation Plan

> **For agentic workers:** перед реализацией использовать навыки `polymarketlab-feature`, `tdd` и `karpathy-guidelines`; выполнять задачи по порядку и отмечать checkbox. Недоступные навыки или инструменты автоматически не устанавливать.

**Goal:** расширить существующий `CollectorSessionResponse`, чтобы GET и Stop возвращали единый безопасный снимок lifecycle, snapshot рынка, готовности, durable accounting, нормализации, resolution и cleanup по issue [#27](https://github.com/DmitriyEgorov1989/PolymarketLab/issues/27).

**Architecture:** оставить controller и маршруты без изменений. Application-сервис `CollectorSessionResponseFactory` последовательно читает существующие backend slices через порты, вычисляет только presentation-safe производные значения и строит один DTO; PostgreSQL-детали остаются в Infrastructure. Для cleanup добавить отдельный read port, потому что текущий `ICollectorDatasetCleanup` является командным портом.

**Tech Stack:** .NET 10, C# records, MediatR, EF Core, PostgreSQL, xUnit, FluentAssertions, System.Text.Json.

**Spec:** issue [#27](https://github.com/DmitriyEgorov1989/PolymarketLab/issues/27), итоговая спецификация в комментарии к [#14](https://github.com/DmitriyEgorov1989/PolymarketLab/issues/14#issuecomment-5395220881), roadmap `docs/superpowers/plans/2026-08-27-first-full-five-minute-market-roadmap.md:409-436`.

## Global Constraints

- До реализации получить отдельное разрешение пользователя на изменение публичного HTTP-контракта.
- Существующие routes, `Envelope`, expected errors и строковые enum conventions не менять.
- Не возвращать raw payload, credentials, exception text или stack trace.
- `Interrupted` и legacy sessions с nullable snapshot-полями должны оставаться читаемыми.
- Historical `messagesReceived/messagesEnqueued/messagesPersisted` не смешивать с текущим `remainingRawMessageCount` после cleanup.
- Не добавлять новые dependencies.
- До реализации получить отдельное разрешение пользователя на EF migration: partial initial-book progress сейчас существует только внутри `CollectorWebSocketWorker`, поэтому честная `readiness per token` требует durable observation текущей connection epoch.
- Новая epoch не удаляет историю readiness, но HTTP выбирает только observations, совпадающие с `CollectorSessionProgress.CurrentConnectionEpoch`; stale epoch не подтверждает текущую готовность.
- Frontend UI не менять: это scope issue #36. Typed API interface обновить только вместе с #36, чтобы не создавать неиспользуемую модель в клиенте.
- Не изменять чужие незавершённые правки и migration-файлы.

---

## Target JSON Contract

Сохранить существующие верхнеуровневые поля и добавить сгруппированные evidence slices:

```json
{
  "sessionId": "22222222-2222-2222-2222-222222222222",
  "marketId": "11111111-1111-1111-1111-111111111111",
  "snapshot": {
    "externalEventId": "event-123",
    "eventSlug": "btc-updown-5m-1200",
    "externalMarketId": "market-123",
    "marketSlug": "btc-updown-5m-1200",
    "conditionId": "0xabc",
    "eventStartsAt": "2026-09-04T12:00:00Z",
    "eventEndsAt": "2026-09-04T12:05:00Z",
    "projectionVersion": 3,
    "tokens": [
      { "tokenId": "1001", "outcome": "Yes", "outcomeIndex": 0 },
      { "tokenId": "1002", "outcome": "No", "outcomeIndex": 1 }
    ]
  },
  "status": "Stopping",
  "phase": "AwaitingNormalization",
  "effectiveDeadline": "2026-09-04T12:10:04Z",
  "createdAt": "2026-09-04T11:57:00Z",
  "startedAt": "2026-09-04T11:59:00Z",
  "subscriptionReadyAt": "2026-09-04T11:59:48Z",
  "stoppedAt": null,
  "invalidatingAt": null,
  "stopReason": null,
  "failureCode": null,
  "failureMessage": null,
  "readiness": {
    "connectionEpoch": 2,
    "tokens": [
      { "tokenId": "1001", "initialBookEnqueuedAt": "2026-09-04T11:59:44Z" },
      { "tokenId": "1002", "initialBookEnqueuedAt": "2026-09-04T11:59:45Z" }
    ]
  },
  "messagesReceived": 1250,
  "messagesEnqueued": 1250,
  "messagesPersisted": 1250,
  "remainingRawMessageCount": 1250,
  "lastMessageAt": "2026-09-04T12:05:03Z",
  "reconnectCount": 1,
  "normalization": {
    "rawCount": 1250,
    "ledgerCount": 1250,
    "processedCount": 1240,
    "pendingCount": 10,
    "processingCount": 0,
    "unsupportedCount": 0,
    "invalidCount": 0,
    "failedCount": 0,
    "missingCount": 0,
    "resolutionRawItemProcessed": false
  },
  "resolution": {
    "signaledAt": "2026-09-04T12:05:01Z",
    "confirmedAt": "2026-09-04T12:05:03Z",
    "winningTokenId": "1001",
    "winningOutcome": "Yes",
    "connectionEpoch": 2,
    "lastPollingCycleAt": "2026-09-04T12:05:02Z",
    "sourceStates": [
      {
        "source": "WebSocket",
        "status": "Terminal",
        "observedAt": "2026-09-04T12:05:01Z",
        "winningTokenId": "1001",
        "winningOutcome": "Yes",
        "errorCode": null,
        "errorMessage": null
      },
      {
        "source": "Gamma",
        "status": "Terminal",
        "observedAt": "2026-09-04T12:05:02Z",
        "winningTokenId": "1001",
        "winningOutcome": "Yes",
        "errorCode": null,
        "errorMessage": null
      },
      {
        "source": "Clob",
        "status": "Terminal",
        "observedAt": "2026-09-04T12:05:03Z",
        "winningTokenId": "1001",
        "winningOutcome": "Yes",
        "errorCode": null,
        "errorMessage": null
      }
    ],
    "confirmationSources": [
      {
        "source": "WebSocket",
        "status": "Terminal",
        "observedAt": "2026-09-04T12:05:01Z",
        "winningTokenId": "1001",
        "winningOutcome": "Yes",
        "errorCode": null,
        "errorMessage": null
      },
      {
        "source": "Gamma",
        "status": "Terminal",
        "observedAt": "2026-09-04T12:05:02Z",
        "winningTokenId": "1001",
        "winningOutcome": "Yes",
        "errorCode": null,
        "errorMessage": null
      },
      {
        "source": "Clob",
        "status": "Terminal",
        "observedAt": "2026-09-04T12:05:03Z",
        "winningTokenId": "1001",
        "winningOutcome": "Yes",
        "errorCode": null,
        "errorMessage": null
      }
    ]
  },
  "cleanup": null
}
```

Nullable rules:

- `snapshot` всегда присутствует, но его identity/window/version члены nullable для legacy session; `tokens` всегда массив.
- `phase` равен `null` для `Stopped`, `Failed`, `Interrupted` и legacy rows без сохранённой фазы.
- `effectiveDeadline` вычисляется только для фаз с фиксированной границей: preparation, readiness, market window, resolution confirmation и normalization; для `DrainingRaw`, `Cleaning` и terminal status это `null`.
- `readiness.tokens[].initialBookEnqueuedAt` равен `null`, если exact token не имеет durable observation текущей `connectionEpoch`; timestamp не переносится между epoch.
- `normalization` равен `null`, если `projectionVersion` отсутствует у legacy session либо committed cleanup уже удалил dataset; иначе это текущие remaining counts для snapshot-version.
- `resolution` всегда присутствует; nullable winner/timestamps и пустые массивы означают, что durable observation ещё нет.
- `sourceStates` содержит последнее observation каждого источника по `(ObservedAt, Id)`, отсортированное `WebSocket`, `Gamma`, `Clob`.
- `confirmationSources` содержит exact terminal evidence состоявшегося consensus: WebSocket observation сопоставляется с session `ResolutionSignaledAt`/winner/epoch, Gamma и CLOB берутся по ID из `ResolutionConfirmationReference`. Поэтому более позднее non-terminal observation не скрывает evidence подтверждения.
- `cleanup` равен `null` до committed cleanup. После cleanup содержит `invalidatingAt`, `cleanedAt`, snapshot `projectionVersion`, сохранённые `failureCode/failureMessage` и deleted counts.

---

### Task 1: Freeze DTO Shape and Enum Vocabulary

**Files:**
- Modify: `PolymarketLab.ApiContract.Tests/FrontendApiContractTests.cs`
- Modify: `PolymarketLab.DataCollection.Core/Application/UseCases/Common/CollectorSessionResponse.cs`
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/Common/CollectorSessionSnapshotResponse.cs`
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/Common/CollectorReadinessResponse.cs`
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/Common/CollectorNormalizationResponse.cs`
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/Common/CollectorResolutionResponse.cs`
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/Common/CollectorCleanupResponse.cs`

**Interfaces:**
- Consumes: `CollectorSession`, `CollectorSessionProgress`, `NormalizationSuitability`, `DurableResolutionState`, `CollectorDatasetCleanupAudit`.
- Produces: immutable HTTP response records with the exact JSON names shown above.

- [ ] **Step 1: Add failing exact-shape contract tests**

Replace the narrow `StopResponse_ShouldContainFullSession` assertion with tests that serialize a complete response and assert exact property allowlists at every nesting level. Also freeze all phase/source/status values:

```csharp
Enum.GetNames<CollectorSessionPhase>().Should().Equal(
    "WaitingForPreparation",
    "Connecting",
    "AwaitingInitialBooks",
    "AwaitingHeartbeat",
    "ReadyBeforeWindow",
    "CollectingWindow",
    "AwaitingResolution",
    "DrainingRaw",
    "AwaitingNormalization",
    "Cleaning");

Enum.GetNames<ResolutionObservationSource>().Should().Equal(
    "WebSocket", "Gamma", "Clob");
Enum.GetNames<DurableResolutionObservationStatus>().Should().Equal(
    "Rejected", "NonTerminal", "Terminal", "Failed", "Conflict");

Enum.GetNames<CollectorStopReason>().Should().Equal(
    "Requested",
    "ApplicationShutdown",
    "MarketClosed",
    "FatalWebSocketError",
    "PersistenceFailure",
    "RecoveryTimeout",
    "StartupFailure",
    "ProcessTerminated",
    "ResolutionFailure");
```

Add a separate legacy fixture with `status="Interrupted"`, nullable snapshot members, `phase=null`, `effectiveDeadline=null`, `normalization=null`, empty `sourceStates`/`confirmationSources` and `cleanup=null`.

- [ ] **Step 2: Run the contract test and confirm RED**

Run:

```powershell
dotnet test .\PolymarketLab.ApiContract.Tests\PolymarketLab.ApiContract.Tests.csproj --filter "FullyQualifiedName~FrontendApiContractTests"
```

Expected: compile failure because nested response records and new constructor members do not exist.

- [ ] **Step 3: Add the immutable response records**

Use these signatures and add Russian XML comments to every public type/member, including `null` semantics:

```csharp
public sealed record CollectorSessionResponse(
    Guid SessionId,
    Guid MarketId,
    CollectorSessionSnapshotResponse Snapshot,
    string Status,
    string? Phase,
    DateTimeOffset? EffectiveDeadline,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? SubscriptionReadyAt,
    DateTimeOffset? StoppedAt,
    DateTimeOffset? InvalidatingAt,
    string? StopReason,
    string? FailureCode,
    string? FailureMessage,
    CollectorReadinessResponse Readiness,
    long MessagesReceived,
    long MessagesEnqueued,
    long MessagesPersisted,
    long RemainingRawMessageCount,
    DateTimeOffset? LastMessageAt,
    long ReconnectCount,
    CollectorNormalizationResponse? Normalization,
    CollectorResolutionResponse Resolution,
    CollectorCleanupResponse? Cleanup);

public sealed record CollectorSessionSnapshotResponse(
    string? ExternalEventId,
    string? EventSlug,
    string? ExternalMarketId,
    string? MarketSlug,
    string? ConditionId,
    DateTimeOffset? EventStartsAt,
    DateTimeOffset? EventEndsAt,
    int? ProjectionVersion,
    IReadOnlyList<CollectorSessionTokenResponse> Tokens);

public sealed record CollectorSessionTokenResponse(
    string TokenId,
    string Outcome,
    int OutcomeIndex);

public sealed record CollectorReadinessResponse(
    long ConnectionEpoch,
    IReadOnlyList<CollectorTokenReadinessResponse> Tokens);

public sealed record CollectorTokenReadinessResponse(
    string TokenId,
    DateTimeOffset? InitialBookEnqueuedAt);

public sealed record CollectorNormalizationResponse(
    long RawCount,
    long LedgerCount,
    long ProcessedCount,
    long PendingCount,
    long ProcessingCount,
    long UnsupportedCount,
    long InvalidCount,
    long FailedCount,
    long MissingCount,
    bool ResolutionRawItemProcessed);

public sealed record CollectorResolutionResponse(
    DateTimeOffset? SignaledAt,
    DateTimeOffset? ConfirmedAt,
    string? WinningTokenId,
    string? WinningOutcome,
    long? ConnectionEpoch,
    DateTimeOffset? LastPollingCycleAt,
    IReadOnlyList<CollectorResolutionSourceResponse> SourceStates,
    IReadOnlyList<CollectorResolutionSourceResponse> ConfirmationSources);

public sealed record CollectorResolutionSourceResponse(
    string Source,
    string Status,
    DateTimeOffset ObservedAt,
    string? WinningTokenId,
    string? WinningOutcome,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record CollectorCleanupResponse(
    DateTimeOffset? InvalidatingAt,
    DateTimeOffset CleanedAt,
    int? ProjectionVersion,
    string? FailureCode,
    string? FailureMessage,
    long DeletedRawMessageCount,
    long DeletedNormalizationCount,
    long DeletedNormalizedEventCount);
```

- [ ] **Step 4: Run the contract test and keep it RED only on missing mapping**

The project should compile after updating test fixtures to construct the new records. Tests may still fail until the response factory is implemented in Task 4.

---

### Task 2: Persist Per-Token Readiness for Each Connection Epoch

**Files:**
- Create: `PolymarketLab.DataCollection.Core/Ports/Dtos/CollectorTokenReadiness.cs`
- Create: `PolymarketLab.DataCollection.Core/Ports/ICollectorTokenReadinessRepository.cs`
- Modify: `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRuntimeReadiness/ICollectorRuntimeReadinessHandler.cs`
- Modify: `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRuntimeReadiness/CollectorRuntimeReadinessHandler.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/ICollectorRuntimeReadinessDispatcher.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/CollectorRuntimeReadinessDispatcher.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/CollectorWebSocketWorker.cs:660-691`
- Create: `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Models/CollectorTokenReadinessRecord.cs`
- Create: `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Configurations/CollectorTokenReadinessConfiguration.cs`
- Create: `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Repositories/CollectorSession/CollectorTokenReadinessRepository.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/DataCollectionDbContext.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/DependencyInjection/DataCollectionInfrastructureDependencyInjection.cs`
- Generate: EF migration named `PersistCollectorTokenReadiness`
- Modify: readiness handler, worker, PostgreSQL integration and DI tests.

**Interfaces:**
- Consumes: validated initial `book` token ID only after successful bounded-channel enqueue, current `ConnectionReadinessState.Epoch`, current UTC time.
- Produces: one immutable observation per `(SessionId, ConnectionEpoch, TokenId)` and current-epoch read model for HTTP.

- [ ] **Step 1: Obtain migration permission and write failing tests**

Do not continue without explicit permission because this task changes the EF model and database schema. Tests must prove:

- first successfully enqueued initial book creates one observation;
- duplicate book in the same epoch is idempotent and preserves the first timestamp;
- books in epoch `1` do not mark tokens ready when current progress epoch is `2`;
- a second token can become ready independently;
- persistence failure is returned to the worker and follows the existing readiness failure/invalidation path;
- cleanup does not delete compact readiness observations.

- [ ] **Step 2: Add the port DTO and repository contract**

```csharp
public sealed record CollectorTokenReadiness(
    CollectorSessionId SessionId,
    long ConnectionEpoch,
    TokenId TokenId,
    DateTimeOffset InitialBookEnqueuedAt);

public interface ICollectorTokenReadinessRepository
{
    Task RecordInitialBookEnqueuedAsync(
        CollectorTokenReadiness readiness,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CollectorTokenReadiness>> GetAsync(
        CollectorSessionId sessionId,
        long connectionEpoch,
        CancellationToken cancellationToken);
}
```

Add Russian XML comments to public types and members. Reject `connectionEpoch <= 0` and default timestamps before calling persistence.

- [ ] **Step 3: Add application/runtime dispatch without duplicating readiness decisions**

Extend both readiness handler interfaces with:

```csharp
Task<UnitResult<Error>> RecordInitialBookEnqueuedAsync(
    CollectorSessionId sessionId,
    TokenId tokenId,
    long connectionEpoch,
    DateTimeOffset enqueuedAt,
    CancellationToken cancellationToken);
```

The application handler verifies that the session exists, is `Starting/AwaitingInitialBooks`, and token belongs to immutable snapshot, then writes the observation. Expected invalid state returns existing safe readiness error conventions; persistence exceptions remain unexpected and are handled by the existing dispatcher boundary.

- [ ] **Step 4: Record the observation at the exact enqueue boundary**

In `CollectorWebSocketWorker`, after `messageSink.EnqueueAsync(...)` and successful `telemetry.RecordEnqueued(...)`, but before `state.ObserveInitialBook(...)`, call:

```csharp
var readinessResult = await readinessDispatcher.RecordInitialBookEnqueuedAsync(
    request.SessionId,
    TokenId.Create(observation.TokenId).Value,
    state.Epoch,
    timeProvider.GetUtcNow(),
    receiveToken);
if (readinessResult.IsFailure)
    return UnitResult.Failure(readinessResult.Error);

state.ObserveInitialBook(observation.TokenId);
```

Do not write observations for malformed or unknown payloads. A duplicate valid book may attempt the same insert; repository idempotency preserves the first timestamp and is the race-safe fallback.

- [ ] **Step 5: Implement PostgreSQL persistence**

Create table `data_collection.collector_token_readiness` with:

```text
session_id uuid not null
connection_epoch bigint not null check (connection_epoch > 0)
token_id text not null
initial_book_enqueued_at timestamptz not null
primary key (session_id, connection_epoch, token_id)
foreign key (session_id) references data_collection.collector_sessions(id) on delete cascade
```

Use PostgreSQL `INSERT ... ON CONFLICT DO NOTHING` so the first timestamp is immutable. `GetAsync` must use `AsNoTracking()`, filter exact session/epoch and order by snapshot `OutcomeIndex` at the response-factory mapping boundary.

- [ ] **Step 6: Generate, review and test the migration**

Generate with the repository's documented EF command and migration name `PersistCollectorTokenReadiness`; do not hand-edit designer or model snapshot files. Review SQL for the composite primary key, epoch check and session FK.

Run focused tests:

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter "FullyQualifiedName~CollectorRuntimeReadinessHandlerTests"
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter "FullyQualifiedName~CollectorWebSocketWorkerTests|FullyQualifiedName~CollectorTokenReadiness"
```

---

### Task 3: Add Cleanup Audit Read Slice

**Files:**
- Create: `PolymarketLab.DataCollection.Core/Ports/ICollectorDatasetCleanupAuditReader.cs`
- Create: `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Repositories/CollectorSession/CollectorDatasetCleanupAuditReader.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/DependencyInjection/DataCollectionInfrastructureDependencyInjection.cs:150-174`
- Modify: `PolymarketLab.DataCollection.Infrastructure.Tests/DependencyInjection/DataCollectionInfrastructureDependencyInjectionTests.cs`
- Create: `PolymarketLab.DataCollection.Infrastructure.Tests/Adapters/Postgres/CollectorDatasetCleanupAuditReaderTests.cs`

**Interfaces:**
- Consumes: existing `collector_dataset_cleanup_audits` EF entity and `CollectorDatasetCleanupAuditRecord.ToAudit()`.
- Produces: `Task<CollectorDatasetCleanupAudit?> GetBySessionIdAsync(CollectorSessionId, CancellationToken)`.

- [ ] **Step 1: Write failing reader tests**

Cover an existing audit and a missing audit using the repository test pattern already used under `Adapters/Postgres`:

```csharp
var result = await reader.GetBySessionIdAsync(session.Id, CancellationToken.None);

result.Should().BeEquivalentTo(expectedAudit);
```

The missing case must return `null`, not throw and not synthesize zero deleted counts.

- [ ] **Step 2: Run the narrow test and confirm RED**

```powershell
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter "FullyQualifiedName~CollectorDatasetCleanupAuditReaderTests"
```

- [ ] **Step 3: Add the read port and EF adapter**

```csharp
public interface ICollectorDatasetCleanupAuditReader
{
    Task<CollectorDatasetCleanupAudit?> GetBySessionIdAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);
}

internal sealed class CollectorDatasetCleanupAuditReader(DataCollectionDbContext dbContext)
    : ICollectorDatasetCleanupAuditReader
{
    public async Task<CollectorDatasetCleanupAudit?> GetBySessionIdAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.CollectorDatasetCleanupAudits
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);
        return record?.ToAudit();
    }
}
```

Register it as scoped and assert its lifetime in the existing DI test. No schema or migration change is required.

- [ ] **Step 4: Run reader and DI tests and confirm GREEN**

```powershell
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter "FullyQualifiedName~CollectorDatasetCleanupAuditReaderTests|FullyQualifiedName~DataCollectionInfrastructureDependencyInjectionTests"
```

---

### Task 4: Aggregate Existing Read Slices in Application

**Files:**
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/Common/ICollectorSessionResponseFactory.cs`
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/Common/CollectorSessionResponseFactory.cs`
- Modify: `PolymarketLab.DataCollection.Core/Application/DependencyInjection/DataCollectionApplicationDependencyInjection.cs`
- Modify: `PolymarketLab.DataCollection.Core/Application/UseCases/Queries/GetCollectorSessionById/GetCollectorSessionByIdHandler.cs`
- Modify: `PolymarketLab.DataCollection.Core/Application/UseCases/Queries/GetCollectorSessionByMarket/GetCollectorSessionByMarketHandler.cs`
- Modify: `PolymarketLab.DataCollection.Core/Application/UseCases/Commands/StopCollector/StopCollectorHandler.cs`
- Create: `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/Common/CollectorSessionResponseFactoryTests.cs`
- Modify: query and Stop handler tests to stub the response factory.

**Interfaces:**
- Consumes: aggregate from `ICollectorSessionRepository`; progress from `ICollectorSessionProgressRepository`; readiness from `ICollectorTokenReadinessRepository`; normalization from `INormalizationSuitabilityReader`; resolution from `IResolutionObservationRepository`; cleanup from `ICollectorDatasetCleanupAuditReader`.
- Produces: `Task<CollectorSessionResponse> CreateAsync(CollectorSession session, CancellationToken cancellationToken)` used by both GET handlers and `StopCollectorHandler`.

- [ ] **Step 1: Write failing factory tests for the three observable scenarios**

Cover:

1. `Stopped/MarketClosed`: full snapshot, `phase=null`, all durable counters, exact current-epoch token timestamps, normalization counts, winner, latest source states, exact confirmation evidence and no cleanup.
2. `Stopping/AwaitingNormalization`: dynamic `effectiveDeadline = AwaitingNormalizationAt + 5 minutes`, `pendingCount > 0`, no cleanup.
3. Cleaned `Failed`: historical counters remain non-zero, `remainingRawMessageCount=0`, `normalization=null`, cleanup deleted counts are non-zero.
4. Legacy `Interrupted`: cover JSON nullability in Core contract tests and add a PostgreSQL test with an explicitly inserted legacy row; do not add a production rehydration API only for a unit test.
5. Resolution reduction: multiple observations of one source produce latest `sourceStates` by `(ObservedAt, Id)` while `confirmationSources` still references the earlier terminal evidence used by consensus. Test a later `NonTerminal` after `Terminal`, a newer timestamp with lower ID and equal timestamps with different IDs.
6. Table-driven deadline cases: all ten phases, early readiness `T-10s`, late readiness `T`, exact `StartedAt == T-10s`, terminal statuses and nullable legacy window.

- [ ] **Step 2: Run the factory tests and confirm RED**

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter "FullyQualifiedName~CollectorSessionResponseFactoryTests"
```

- [ ] **Step 3: Implement the factory with sequential persistence reads**

Use sequential awaits because all scoped adapters can share one EF `DbContext`:

```csharp
public async Task<CollectorSessionResponse> CreateAsync(
    CollectorSessionAggregate session,
    CancellationToken cancellationToken)
{
    var progress = await progressRepository.GetAsync(session.Id, cancellationToken);
    var tokenReadiness = progress.CurrentConnectionEpoch > 0
        ? await tokenReadinessRepository.GetAsync(
            session.Id,
            progress.CurrentConnectionEpoch,
            cancellationToken)
        : [];
    var resolution = await resolutionRepository.GetStateAsync(session.Id, cancellationToken);
    var cleanup = await cleanupAuditReader.GetBySessionIdAsync(session.Id, cancellationToken);
    var normalization = cleanup is null && session.ProjectionVersion is > 0
        ? await normalizationReader.ReadAsync(
            session.Id,
            session.ProjectionVersion.Value,
            cancellationToken)
        : null;

    return Map(session, progress, normalization, resolution, cleanup);
}
```

Mapping rules:

```csharp
var tokens = session.Tokens
    .OrderBy(x => x.OutcomeIndex)
    .Select(x => new CollectorSessionTokenResponse(
        x.TokenId.Value,
        x.Outcome,
        x.OutcomeIndex))
    .ToArray();

var readinessByTokenId = tokenReadiness.ToDictionary(x => x.TokenId.Value);
var readiness = new CollectorReadinessResponse(
    progress.CurrentConnectionEpoch,
    tokens.Select(x => new CollectorTokenReadinessResponse(
        x.TokenId,
        readinessByTokenId.GetValueOrDefault(x.TokenId)?.InitialBookEnqueuedAt)).ToArray());
```

Dynamic deadline rules use the fixed values from #14:

```csharp
private static DateTimeOffset? EffectiveDeadline(CollectorSessionAggregate session) =>
    session.Phase switch
    {
        CollectorSessionPhase.WaitingForPreparation => session.EventStartsAt - TimeSpan.FromSeconds(60),
        CollectorSessionPhase.Connecting or
        CollectorSessionPhase.AwaitingInitialBooks or
        CollectorSessionPhase.AwaitingHeartbeat => ReadinessDeadline(session),
        CollectorSessionPhase.ReadyBeforeWindow => session.EventStartsAt,
        CollectorSessionPhase.CollectingWindow => session.EventEndsAt,
        CollectorSessionPhase.AwaitingResolution => session.EventEndsAt + TimeSpan.FromMinutes(5),
        CollectorSessionPhase.AwaitingNormalization => session.AwaitingNormalizationAt + TimeSpan.FromMinutes(5),
        _ => null
    };

private static DateTimeOffset? ReadinessDeadline(CollectorSessionAggregate session)
{
    if (session.EventStartsAt is not { } eventStartsAt)
        return null;

    var regularDeadline = eventStartsAt - TimeSpan.FromSeconds(10);
    return session.StartedAt is null || session.StartedAt < regularDeadline
        ? regularDeadline
        : eventStartsAt;
}
```

Do not map `RawMessageId`, `RawItemIndex`, external payload details or observation outcome arrays into HTTP. Build `sourceStates` from latest safe observations. Build `confirmationSources` from the WebSocket observation matching session `ResolutionSignaledAt`, winner and epoch plus Gamma/CLOB IDs in `state.Confirmation`; this array must not switch to later polling observations.

Cleanup mapping is explicit: `CleanedAt` and deleted counts come from `CollectorDatasetCleanupAudit`; `InvalidatingAt`, `ProjectionVersion`, `FailureCode` and `FailureMessage` come from the preserved `CollectorSession`. If cleanup exists, skip `INormalizationSuitabilityReader` and return `normalization=null`.

- [ ] **Step 4: Route all full-session responses through the factory**

Handlers become thin:

```csharp
var response = await responseFactory.CreateAsync(session, cancellationToken);
return new GetCollectorSessionByIdResponse(response);
```

Apply the same call in `GetCollectorSessionByMarketHandler` and `StopCollectorHandler`. This prevents GET and Stop from returning different shapes or semantics.

Keep dedicated Stop tests for active `Invalidating/Cleaning`, already `Failed` with audit, successful `Stopped` and legacy `Interrupted`; assert the returned full DTO, not only that the factory was called.

- [ ] **Step 5: Register and test the factory**

```csharp
services.AddScoped<ICollectorSessionResponseFactory, CollectorSessionResponseFactory>();
```

Update handler tests to verify the selected session is passed to the factory, while preserving current not-found, null-session and validation behavior.

Also update `PolymarketLab.DataCollection.Core.Tests/Application/DependencyInjection/DataCollectionApplicationDependencyInjectionTests.cs` to assert one scoped `ICollectorSessionResponseFactory` registration.

- [ ] **Step 6: Run Core tests and confirm GREEN**

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj
```

---

### Task 5: Freeze HTTP Safety and Synchronize Documentation

**Files:**
- Modify: `PolymarketLab.ApiContract.Tests/FrontendApiContractTests.cs`
- Modify: `PolymarketLab.ApiContract.Tests/ReadControllerResponseTests.cs`
- Modify: `docs/frontend-api-contract.md:56-71,211-365`

**Interfaces:**
- Consumes: final `CollectorSessionResponse` from Task 4.
- Produces: tested JSON contract and matching operator/frontend documentation.

- [ ] **Step 1: Complete HTTP contract tests**

Assert exact top-level and nested property names, JSON string values for every status/phase/source status, and nullable legacy semantics. Add an explicit recursive allowlist assertion that these names are absent:

```csharp
json.ToJsonString().Should().NotContain("rawPayload");
json.ToJsonString().Should().NotContain("credentials");
json.ToJsonString().Should().NotContain("stackTrace");
json.ToJsonString().Should().NotContain("rawMessageId");
json.ToJsonString().Should().NotContain("rawItemIndex");
```

Use synthetic safe messages in tests; do not place real credentials or raw Polymarket payloads in fixtures.

- [ ] **Step 2: Preserve controller behavior tests**

Keep the existing assertions for:

- `GET /api/Collector/{sessionId}` returning `404` for unknown session;
- `GET /api/Collector/by-market/{marketId}` returning `200` and `session:null` when history is absent;
- invalid GUID values returning the existing `400` Envelope;
- Stop returning the same full DTO type.

- [ ] **Step 3: Update `docs/frontend-api-contract.md`**

Replace the old Collector DTO examples with the target JSON, document every nullable rule, dynamic deadline semantics, current-epoch token readiness, source-state versus confirmation-evidence semantics, historical-vs-remaining counters, `normalization=null` after cleanup and legacy `Interrupted`. Keep route/request/error sections unchanged.

- [ ] **Step 4: Run focused contract tests**

```powershell
dotnet test .\PolymarketLab.ApiContract.Tests\PolymarketLab.ApiContract.Tests.csproj
```

- [ ] **Step 5: Run solution verification**

```powershell
dotnet test .\PolymarketLab.slnx
dotnet build .\PolymarketLab.slnx
git diff --check
```

Full PostgreSQL integration tests require Docker. If Docker is unavailable, report exactly which tests were skipped or failed because of the environment.

---

## Persisted Read Contract Integration Test

Extend `PolymarketLab.DataCollection.Infrastructure.Tests/Integration/Postgres/CollectorDatasetCleanupPostgreSqlTests.cs` or add a focused `CollectorSessionReadModelPostgreSqlTests.cs`. The test must create progress, token readiness, normalization rows, resolution observations and cleanup audit, execute real cleanup, then read through the same five ports used by `CollectorSessionResponseFactory` and prove:

- historical received/enqueued/persisted counters remain non-zero;
- current raw count is `0`;
- normalization is omitted because cleanup exists;
- readiness and resolution compact observations remain;
- cleanup counts and preserved failure/snapshot fields are readable;
- unrelated session data is unchanged.

## Self-Review

- Spec coverage: snapshot/window/version, exact status/phase, dynamic deadline, durable per-token readiness, epoch, three durable counters, remaining raw rows, normalization, resolution, winner/timestamps and cleanup are assigned to Tasks 1-5.
- Safety coverage: HTTP allowlist excludes raw provenance and sensitive/error internals; expected Envelope behavior is preserved.
- Legacy coverage: nullable snapshot/version and `Interrupted` are explicitly tested.
- Type consistency: all handlers return one `CollectorSessionResponse` built by one factory.
- Scope: routes, consensus, normalization gate, cleanup behavior, frontend UI and dependencies are unchanged. Единственное расширение runtime/persistence — запись требуемого durable per-token readiness observation после успешного enqueue; оно выполняется только после отдельного разрешения на migration.
