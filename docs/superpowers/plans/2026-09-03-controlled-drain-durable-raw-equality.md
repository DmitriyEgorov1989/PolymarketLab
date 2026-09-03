# План реализации controlled drain и durable raw equality

> **Для agentic workers:** выполнять задачи последовательно в текущем рабочем дереве. Недоступные `subagent-driven-development` и `executing-plans` не устанавливать. Шаги используют флажки (`- [ ]`) для отслеживания. Commit разрешён только после отдельного согласия пользователя.

**Цель:** после durable resolution consensus остановить WebSocket producer, дождаться сохранения всего хвоста raw-сообщений и разрешить переход к проверке нормализации только при точном равенстве `received = enqueued = persisted = raw count > 0`, прочитанном из PostgreSQL.

**Архитектура:** новый application coordinator управляет переходами `Running/AwaitingResolution -> Stopping/DrainingRaw -> Stopping/AwaitingNormalization`, вызывает существующие порты runtime и завершения ingestion и применяет invariant точного равенства. Infrastructure продолжает отвечать за ожидание in-memory persisted boundary, durable checkpoint и единый SQL-read прогресса вместе с `count(raw)`. Любая ошибка вызывает существующий durable invalidation coordinator; manual Stop и host shutdown остаются отдельными invalidation-сценариями.

**Стек:** .NET 10, C#, MediatR/application services, CSharpFunctionalExtensions, EF Core, PostgreSQL, xUnit, FluentAssertions.

**Спецификация:** GitHub issue `#34`; зависимости `#24`, `#31`, `#29`; решения `#14`, `#12`, `#16`; epic `#17`.

## Глобальные ограничения

- Серверный код и существующие DTO являются источником истины.
- Публичный HTTP-контракт не меняется.
- Новая EF migration не требуется: durable counters, `ConnectionEpoch` и связь raw row с session уже существуют.
- Глобальный `RawMarketMessageChannel` нельзя закрывать при успешном завершении одной session: это singleton, нужный следующим session. Осушение выполняется по session через final enqueued boundary.
- `market_resolved` не парсится повторно: strict observer из `#24` уже читает сохранённую raw row и подтверждает её provenance.
- Manual Stop и host shutdown продолжают вести session в `Invalidating/Cleaning`; успешный путь `#34` запускается только после durable resolution confirmation.
- Существующие незавершённые изменения, включая migration-файлы `PersistConnectionEpochAndExactRawAccounting`, не изменять.
- Commit, push, branch и pull request не создавать без отдельного разрешения пользователя.

---

## Карта файлов

### Создать

- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRawDatasetCompletion/ICollectorRawDatasetCompletionCoordinator.cs` — application-порт запуска успешного raw completion.
- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionCoordinator.cs` — порядок state transition, producer stop, drain/checkpoint, PostgreSQL equality и invalidation.
- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionErrors.cs` — ожидаемые ошибки отсутствующей session, недопустимого состояния, CAS-конфликта и неравенства счётчиков.
- `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionCoordinatorTests.cs` — unit-тесты orchestration и invariant.
- `PolymarketLab.DataCollection.Infrastructure.Tests/Adapters/RawMessageIngestion/CollectorSessionProgressCompletionTests.cs` — тесты ожидания in-flight tail, timeout и final checkpoint.

### Изменить

- `PolymarketLab.DataCollection.Core/Application/UseCases/ResolutionConsensus/ResolutionConsensusCoordinator.cs` — после durable confirmation передать session в raw completion coordinator.
- `PolymarketLab.DataCollection.Core/Application/DependencyInjection/DataCollectionApplicationDependencyInjection.cs` — scoped-регистрация нового coordinator.
- `PolymarketLab.DataCollection.Core/Ports/ICollectorSessionProgressCompletion.cs` — уточнить XML-контракт: порт ждёт final enqueued boundary и сохраняет checkpoint, но не принимает business-решение о пригодности.
- `PolymarketLab.DataCollection.Infrastructure/Adapters/RawMessageIngestion/CollectorSessionProgressCompletion.cs` — зафиксировать final checkpoint после ожидания хвоста и сохранить его ровно один раз за вызов.
- `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/ResolutionConsensus/ResolutionConsensusCoordinatorTests.cs` — проверить handoff после нового и уже сохранённого consensus.
- `PolymarketLab.DataCollection.Infrastructure.Tests/Integration/Postgres/RawMarketMessageWriterPostgreSqlTests.cs` — доказать restart-safe единый read и безопасный повтор final checkpoint.
- `PolymarketLab.DataCollection.Core.Tests/Application/DependencyInjection/DataCollectionApplicationDependencyInjectionTests.cs` или существующий ближайший DI-тест — проверить регистрацию нового интерфейса.
- `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/README.md` — описать session-scoped successful drain и отличие от host-wide channel shutdown.
- `docs/agent-context.md` — зафиксировать конечный raw equality invariant.

### Намеренно не менять

- `StopCollectorHandler.cs`: manual Stop уже вызывает durable invalidation.
- `CollectorRuntimeShutdownService.cs`: host shutdown уже является invalidation, а не успешным completion.
- `RawMarketMessagePersistenceWorker.cs`: batching, final flush и host-wide drain уже реализованы; session completion ждёт его telemetry boundary, не закрывая общий channel.
- Controllers, HTTP DTO и frontend: наблюдаемый API-контракт не расширяется в `#34`.
- EF model, migrations и snapshot: схема из `#31` уже содержит все нужные поля.

---

### Задача 1: Закрепить application-контракт успешного raw completion

**Файлы:**

- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRawDatasetCompletion/ICollectorRawDatasetCompletionCoordinator.cs`
- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionErrors.cs`
- Test: `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionCoordinatorTests.cs`

**Интерфейсы:**

- Consumes: `CollectorSessionId`, `CancellationToken`.
- Produces: `Task<UnitResult<Error>> CompleteAsync(CollectorSessionId sessionId, CancellationToken cancellationToken)`.

- [ ] **Шаг 1: создать интерфейс с полным XML-контрактом**

```csharp
public interface ICollectorRawDatasetCompletionCoordinator
{
    /// <summary>
    /// После durable resolution consensus останавливает producer, сохраняет весь raw tail
    /// и переводит session к ожиданию нормализации только при exact durable equality.
    /// </summary>
    /// <param name="sessionId">Идентификатор подтверждённой collector session.</param>
    /// <param name="cancellationToken">Токен отмены ожидания операции.</param>
    /// <returns>Успех либо ожидаемая ошибка orchestration или persistence.</returns>
    Task<UnitResult<Error>> CompleteAsync(
        CollectorSessionId sessionId,
        CancellationToken cancellationToken);
}
```

- [ ] **Шаг 2: определить безопасные ожидаемые ошибки**

```csharp
internal static class CollectorRawDatasetCompletionErrors
{
    public static Error SessionNotFound(CollectorSessionId sessionId) => new(
        "collector.raw_completion.session_not_found",
        $"Collector session '{sessionId.Value}' was not found during raw completion.",
        ErrorType.NotFound);

    public static Error ResolutionNotConfirmed(CollectorSessionId sessionId) => new(
        "collector.raw_completion.resolution_not_confirmed",
        $"Collector session '{sessionId.Value}' has no durable resolution confirmation.",
        ErrorType.Conflict);

    public static Error AccountingMismatch(CollectorSessionProgress progress) => new(
        "collector.raw_completion.accounting_mismatch",
        $"Collector session '{progress.SessionId.Value}' raw accounting differs: " +
        $"received={progress.MessagesReceived}, enqueued={progress.MessagesEnqueued}, " +
        $"persisted={progress.MessagesPersisted}, raw={progress.RawMessageCount}.",
        ErrorType.Failure);
}
```

Числа безопасны для diagnostics; payload, token и connection string в сообщение не попадают.

- [ ] **Шаг 3: написать падающие unit-тесты порядка вызовов**

Тест `CompleteAsync_WithConfirmedResolution_ShouldPersistDrainingBeforeStopAndReadAfterCheckpoint` использует общий журнал вызовов и требует порядок:

```text
session:DrainingRaw
runtime:Stop
progress:Complete
postgres:GetProgress
session:AwaitingNormalization
```

Дополнительные assertions:

```csharp
session.Status.Should().Be(CollectorSessionStatus.Stopping);
session.Phase.Should().Be(CollectorSessionPhase.AwaitingNormalization);
invalidation.Calls.Should().BeEmpty();
```

- [ ] **Шаг 4: написать падающие tests invariant и failure routing**

Покрыть таблицей:

```text
received enqueued persisted raw  result
1250     1250     1250      1250 success
0        0        0         0    accounting_mismatch
1250     1249     1249      1249 accounting_mismatch
1250     1250     1249      1249 accounting_mismatch
1250     1250     1250      1249 accounting_mismatch
```

Для каждой ошибки проверить `CollectorStopReason.PersistenceFailure`, durable invalidation и отсутствие перехода в `AwaitingNormalization`. Отдельно проверить: failure `runtime.StopAsync` не вызывает `progressCompletion`; failure `progressCompletion.CompleteAsync` не вызывает PostgreSQL read; CAS conflict перечитывает session не более трёх раз.

- [ ] **Шаг 5: запустить узкий failing test**

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter FullyQualifiedName~CollectorRawDatasetCompletionCoordinatorTests
```

Ожидается FAIL из-за отсутствующих interface/coordinator.

---

### Задача 2: Реализовать state machine, exact equality и invalidation

**Файлы:**

- Create: `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionCoordinator.cs`
- Modify: `PolymarketLab.DataCollection.Core/Application/DependencyInjection/DataCollectionApplicationDependencyInjection.cs`
- Test: `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionCoordinatorTests.cs`

**Интерфейсы:**

- Consumes: `ICollectorSessionRepository`, `ICollectorRuntime`, `ICollectorSessionProgressCompletion`, `ICollectorSessionProgressRepository`, `ICollectorSessionInvalidationCoordinator`, `TimeProvider`.
- Produces: persisted `Stopping/DrainingRaw` before producer stop and persisted `Stopping/AwaitingNormalization` after equality.

- [ ] **Шаг 1: реализовать основной порядок без параллельного lifecycle flow**

```csharp
public async Task<UnitResult<Error>> CompleteAsync(
    CollectorSessionId sessionId,
    CancellationToken cancellationToken)
{
    var draining = await MarkDrainingRawAsync(sessionId, cancellationToken);
    if (draining.IsFailure)
        return await InvalidateAndStopAsync(sessionId, draining.Error, cancellationToken);

    var stop = await runtime.StopAsync(sessionId, cancellationToken);
    if (stop.IsFailure)
        return await InvalidateAndStopAsync(sessionId, stop.Error, cancellationToken);

    var drain = await progressCompletion.CompleteAsync(sessionId, cancellationToken);
    if (drain.IsFailure)
        return await InvalidateAndStopAsync(sessionId, drain.Error, cancellationToken);

    var progress = await progressRepository.GetAsync(sessionId, cancellationToken);
    if (!HasExactRawDataset(progress))
    {
        return await InvalidateAndStopAsync(
            sessionId,
            CollectorRawDatasetCompletionErrors.AccountingMismatch(progress),
            cancellationToken);
    }

    var awaitingNormalization = await MarkAwaitingNormalizationAsync(
        sessionId,
        cancellationToken);
    return awaitingNormalization.IsFailure
        ? await InvalidateAndStopAsync(
            sessionId,
            awaitingNormalization.Error,
            cancellationToken)
        : UnitResult.Success<Error>();
}
```

- [ ] **Шаг 2: реализовать invariant в Application**

```csharp
private static bool HasExactRawDataset(CollectorSessionProgress progress) =>
    progress.MessagesReceived > 0
    && progress.MessagesReceived == progress.MessagesEnqueued
    && progress.MessagesReceived == progress.MessagesPersisted
    && progress.MessagesReceived == progress.RawMessageCount;
```

`Persisted >= Enqueued` намеренно не используется: surplus, deficit и расхождение с реальными rows одинаково недопустимы.

- [ ] **Шаг 3: реализовать CAS-переходы с перечитыванием**

`MarkDrainingRawAsync` допускает только durable confirmed session в `Running/AwaitingResolution`, вызывает существующий `session.MarkStopping()` и сохраняет через `TryUpdateAsync(..., CollectorSessionStatus.Running, ...)`. `MarkAwaitingNormalizationAsync` допускает `Stopping/DrainingRaw`, фиксирует текущее время через `TimeProvider`, вызывает `session.MarkAwaitingNormalization(awaitingNormalizationAt)` и сохраняет с expected status `Stopping`. При `ConcurrencyConflict` оба метода перечитывают session и повторяют решение максимум три раза; `Stopping/AwaitingNormalization` считается идемпотентным успехом.

- [ ] **Шаг 4: реализовать единый failure path**

```csharp
private async Task<UnitResult<Error>> InvalidateAndStopAsync(
    CollectorSessionId sessionId,
    Error failure,
    CancellationToken cancellationToken)
{
    var invalidation = await invalidationCoordinator.InvalidateAsync(
        sessionId,
        timeProvider.GetUtcNow(),
        CollectorStopReason.PersistenceFailure,
        failure,
        cancellationToken);
    if (invalidation.IsFailure)
        return UnitResult.Failure(invalidation.Error);

    if (invalidation.Value is null)
        return UnitResult.Failure(failure);

    return await runtime.StopAsync(sessionId, cancellationToken);
}
```

Повторный `StopAsync` после частично неуспешной остановки безопасен благодаря shared stop task в `CollectorRuntimeEntry`.

- [ ] **Шаг 5: зарегистрировать coordinator как scoped service и проверить lifetime**

```csharp
services.AddScoped<
    ICollectorRawDatasetCompletionCoordinator,
    CollectorRawDatasetCompletionCoordinator>();
```

- [ ] **Шаг 6: прогнать unit-тесты**

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter FullyQualifiedName~CollectorRawDatasetCompletionCoordinatorTests
```

Ожидается PASS.

---

### Задача 3: Передать durable consensus в controlled completion

**Файлы:**

- Modify: `PolymarketLab.DataCollection.Core/Application/UseCases/ResolutionConsensus/ResolutionConsensusCoordinator.cs`
- Modify: `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/ResolutionConsensus/ResolutionConsensusCoordinatorTests.cs`

**Интерфейсы:**

- Consumes: `ICollectorRawDatasetCompletionCoordinator`.
- Produces: один handoff после наличия durable `ResolutionConfirmationReference`.

- [ ] **Шаг 1: изменить существующий consensus test**

В `TickAsync_WithThreeSourceConsensus_ShouldPersistSessionAndReference` добавить:

```csharp
fixture.RawCompletion.Calls.Should().Equal(fixture.Session.Id);
fixture.Observations.Confirmation.Should().NotBeNull();
```

Журнал fake repository должен доказать, что `SetConfirmationReferenceAsync` завершён раньше `RawCompletion.CompleteAsync`.

- [ ] **Шаг 2: добавить restart-safe handoff test**

`TickAsync_WithPersistedConfirmation_ShouldCompleteWithoutPollingAgain` заранее сохраняет confirmation reference, оставляет session в `Running/AwaitingResolution`, вызывает tick и проверяет:

```csharp
fixture.Gamma.CallCount.Should().Be(0);
fixture.Clob.CallCount.Should().Be(0);
fixture.RawCompletion.Calls.Should().Equal(fixture.Session.Id);
```

Это закрывает сбой процесса между durable confirmation и началом producer stop.

- [ ] **Шаг 3: изменить ветку EvaluateConsensusAsync**

```csharp
var consensusResult = await EvaluateConsensusAsync(...);
if (consensusResult.IsFailure)
    return UnitResult.Failure(consensusResult.Error);

if (consensusResult.Value)
{
    return await rawDatasetCompletion.CompleteAsync(
        session.Id,
        cancellationToken);
}
```

Polling и scanning больше не продолжаются после durable consensus. Conflict/timeout до consensus по-прежнему используют существующий resolution invalidation path.

- [ ] **Шаг 4: прогнать узкие consensus tests**

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter FullyQualifiedName~ResolutionConsensusCoordinatorTests
```

Ожидается PASS без изменений контрактов Gamma, CLOB и WebSocket parser.

---

### Задача 4: Зафиксировать final checkpoint после in-flight batch

**Файлы:**

- Modify: `PolymarketLab.DataCollection.Core/Ports/ICollectorSessionProgressCompletion.cs`
- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/RawMessageIngestion/CollectorSessionProgressCompletion.cs`
- Create: `PolymarketLab.DataCollection.Infrastructure.Tests/Adapters/RawMessageIngestion/CollectorSessionProgressCompletionTests.cs`

**Интерфейсы:**

- Consumes: final stable telemetry после `runtime.StopAsync`.
- Produces: durable monotonic checkpoint; exact equality проверяет application coordinator отдельным PostgreSQL read.

- [ ] **Шаг 1: написать test ожидания in-flight batch**

Создать telemetry с `received=3`, `enqueued=3`, `persisted=2`. Запустить `CompleteAsync` и проверить, что task ещё не завершён и repository не вызван. После `RecordPersisted(sessionId, 1)` проверить checkpoint:

```csharp
checkpoint.MessagesReceived.Should().Be(3);
checkpoint.MessagesEnqueued.Should().Be(3);
checkpoint.MessagesPersisted.Should().Be(3);
```

- [ ] **Шаг 2: написать timeout test**

С `ShutdownTimeout = 50 milliseconds` оставить один enqueued message неподтверждённым и ожидать failure code `collector.progress.persistence_timeout`. Checkpoint не должен выполняться.

- [ ] **Шаг 3: написать checkpoint failure test**

Fake repository бросает `InvalidOperationException`; порт возвращает `collector.progress.persistence_failed`, не раскрывает exception message наружу и пишет structured log с `SessionId`.

- [ ] **Шаг 4: сделать final boundary явной в реализации**

```csharp
var finalEnqueued = telemetry.GetSnapshot(sessionId).Enqueued;
await telemetry.WaitUntilPersistedAsync(
    sessionId,
    finalEnqueued,
    linkedCts.Token);

var finalCheckpoint = telemetry.GetCheckpoint(sessionId);
await repository.CheckpointAsync(finalCheckpoint, linkedCts.Token);
```

Предусловие стабильности `finalEnqueued` обеспечено coordinator: `runtime.StopAsync` уже завершился, поэтому producer больше не может увеличить `received` или `enqueued`.

- [ ] **Шаг 5: прогнать infrastructure unit tests**

```powershell
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter "FullyQualifiedName~CollectorSessionProgressCompletionTests|FullyQualifiedName~RawMarketMessagePersistenceWorkerTests"
```

Ожидается PASS, включая существующий test partial batch/final flush.

---

### Задача 5: Доказать PostgreSQL equality, retry safety и restart-read

**Файлы:**

- Modify: `PolymarketLab.DataCollection.Infrastructure.Tests/Integration/Postgres/RawMarketMessageWriterPostgreSqlTests.cs`
- При необходимости Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Repositories/CollectorSession/CollectorSessionProgressRepository.cs` только если test покажет более одного SQL command.

**Интерфейсы:**

- Consumes: `ICollectorSessionProgressRepository.GetAsync`.
- Produces: один snapshot с durable counters и correlated `LongCount(raw_market_messages)`.

- [ ] **Шаг 1: добавить restart-safe exact equality test**

Записать `3 messages`, final checkpoint `3/3/3`, уничтожить write `DbContext`, создать новый context и один раз вызвать `GetAsync`. Проверить:

```csharp
progress.MessagesReceived.Should().Be(3);
progress.MessagesEnqueued.Should().Be(3);
progress.MessagesPersisted.Should().Be(3);
progress.RawMessageCount.Should().Be(3);
```

- [ ] **Шаг 2: доказать один authoritative SQL read**

Подключить test command interceptor только к restart context, обнулить счётчик перед `GetAsync` и проверить `ExecutedReaderCount.Should().Be(1)`. Текущий LINQ должен сформировать один `SELECT` с correlated `COUNT(*)`.

- [ ] **Шаг 3: добавить retry-after-ambiguous-commit test**

Дважды выполнить одинаковый final `CheckpointAsync(3/3/3)` и проверить, что monotonic upsert оставил `3`, а не `6`. Это моделирует повтор после ситуации, когда PostgreSQL commit прошёл, но подтверждение клиенту потерялось.

- [ ] **Шаг 4: проверить mismatch read**

Сохранить checkpoint `1250/1250/1250`, но создать `1249 raw rows`; новый context должен вернуть эти четыре разные величины без подмены `MessagesPersisted` фактическим count.

- [ ] **Шаг 5: запустить PostgreSQL tests**

```powershell
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter FullyQualifiedName~RawMarketMessageWriterPostgreSqlTests
```

Требуется доступный Docker daemon. Если Docker недоступен, ограничение фиксируется в итоговом отчёте, но unit tests всё равно выполняются.

---

### Задача 6: Документация и полная проверка

**Файлы:**

- Modify: `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/README.md`
- Modify: `docs/agent-context.md`

- [ ] **Шаг 1: описать успешную последовательность**

```text
durable consensus
-> CAS Stopping/DrainingRaw
-> CollectorRuntime.StopAsync (producer closed)
-> wait persisted to final enqueued boundary
-> durable final checkpoint
-> one PostgreSQL read: received=enqueued=persisted=raw>0
-> CAS Stopping/AwaitingNormalization
```

- [ ] **Шаг 2: явно отделить session drain от host shutdown**

Указать, что `RawMessagePersistenceWorker.CompleteProducers()` вызывается только при остановке host. Успешная session не закрывает singleton channel и потому не ломает последующие запуски collector.

- [ ] **Шаг 3: выполнить проверки от узких к широким**

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj
dotnet test .\PolymarketLab.slnx
dotnet build .\PolymarketLab.slnx
git diff --check
```

- [ ] **Шаг 4: проверить итоговый diff**

Убедиться, что нет изменений HTTP API, migrations, generated artifacts, fixtures и несвязанных пользовательских файлов. Отдельно проверить отсутствие payload, connection strings, tokens и credentials в тестовых diagnostics.

- [ ] **Шаг 5: подготовить итоговый отчёт без commit**

Отчёт должен содержать: реализованный порядок, новый invariant, список файлов, tests и их результаты, Docker-ограничения, а также подтверждение отсутствия migration и изменения HTTP-контракта. Commit предлагается только отдельным следующим действием после разрешения пользователя.

---

## Самопроверка плана

- Producer stop до drain: задачи 2 и 4.
- `market_resolved` в persisted dataset: consensus использует уже persisted raw provenance; final equality включает эту row и весь последующий tail.
- Final durable checkpoint: задача 4.
- Один PostgreSQL equality read и `> 0`: задачи 2 и 5.
- Запрет `persisted >= enqueued` как достаточного условия: задача 2.
- Stop/drain/checkpoint/equality failure в durable invalidation: задача 2.
- In-flight batch, timeout, retry, restart-safe read: задачи 4 и 5.
- Manual Stop и host shutdown не превращены в успешный completion: глобальные ограничения и карта намеренно неизменяемых файлов.
- Публичный HTTP-контракт и EF schema не меняются.
