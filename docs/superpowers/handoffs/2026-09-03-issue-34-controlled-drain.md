# Handoff: issue #34 — controlled drain и durable raw equality

## Статус

- Задача: GitHub issue `#34` — «Завершить controlled drain и durable raw equality».
- Ветка: `feature/first-full-five-minute-market`.
- Исходный commit: `e0406a0 Implement atomic dataset cleanup and startup recovery`.
- Реализация не начата.
- Подготовлен подробный план: `docs/superpowers/plans/2026-09-03-controlled-drain-durable-raw-equality.md`.
- Зависимости `#24`, `#31` и `#29` закрыты.

## Что требуется получить

После durable resolution consensus выполнить последовательность:

```text
CAS Running/AwaitingResolution -> Stopping/DrainingRaw
-> остановить WebSocket producer
-> дождаться persisted до final enqueued boundary
-> durably записать final checkpoint
-> одним PostgreSQL read получить counters и raw count
-> проверить received=enqueued=persisted=raw count>0
-> CAS Stopping/DrainingRaw -> Stopping/AwaitingNormalization
```

Любая ошибка producer stop, drain, checkpoint, PostgreSQL equality или state transition должна вызвать существующий `ICollectorSessionInvalidationCoordinator` и оставить durable diagnostic.

## Текущее поведение

- `ResolutionConsensusCoordinator` сохраняет winner и `ResolutionConfirmationReference`, но после consensus оставляет session в `Running/AwaitingResolution`.
- `CollectorRuntime.StopAsync` уже идемпотентно останавливает producer через shared stop task.
- `CollectorSessionProgressCompletion.CompleteAsync` уже ждёт `Persisted >= final Enqueued` и сохраняет checkpoint, но не проверяет точное равенство с PostgreSQL raw rows.
- `CollectorSessionProgressRepository.GetAsync` уже одним LINQ query возвращает durable `MessagesReceived`, `MessagesEnqueued`, `MessagesPersisted` и correlated `RawMessageCount`.
- `CollectorSession.MarkStopping()` и `MarkAwaitingNormalization(awaitingNormalizationAt)` реализуют нужные domain transitions; второй переход сохраняет начало окна нормализации.
- Manual Stop и host shutdown уже направляются в `Invalidating/Cleaning` и не должны использовать успешный completion flow.

## Принятые решения

1. Создать application coordinator `CollectorRawDatasetCompletionCoordinator`; не помещать business invariant в Infrastructure.
2. Проверять точное равенство:

   ```csharp
   progress.MessagesReceived > 0
       && progress.MessagesReceived == progress.MessagesEnqueued
       && progress.MessagesReceived == progress.MessagesPersisted
       && progress.MessagesReceived == progress.RawMessageCount;
   ```

3. Не закрывать `RawMarketMessageChannel` при завершении одной session. Это singleton-channel; `CompleteProducers()` остаётся только host-shutdown механизмом.
4. Не менять `StopCollectorHandler`, `CollectorRuntimeShutdownService` и production-код `RawMarketMessagePersistenceWorker`, если новые tests не выявят фактический пробел.
5. Не создавать EF migration: схема задачи `#31` уже содержит необходимые counters и `ConnectionEpoch`.
6. Не менять HTTP API, frontend и Polymarket payload parsing.
7. Для failure reason использовать существующий `CollectorStopReason.PersistenceFailure`; наружу сохранять исходный безопасный error code/message.
8. Повтор final checkpoint проверять как retry after ambiguous commit: одинаковый checkpoint не должен удваивать counters.

## Основные файлы реализации

Создать:

- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRawDatasetCompletion/ICollectorRawDatasetCompletionCoordinator.cs`
- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionCoordinator.cs`
- `PolymarketLab.DataCollection.Core/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionErrors.cs`
- `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/CollectorRawDatasetCompletion/CollectorRawDatasetCompletionCoordinatorTests.cs`
- `PolymarketLab.DataCollection.Infrastructure.Tests/Adapters/RawMessageIngestion/CollectorSessionProgressCompletionTests.cs`

Изменить:

- `PolymarketLab.DataCollection.Core/Application/UseCases/ResolutionConsensus/ResolutionConsensusCoordinator.cs`
- `PolymarketLab.DataCollection.Core/Application/DependencyInjection/DataCollectionApplicationDependencyInjection.cs`
- `PolymarketLab.DataCollection.Core/Ports/ICollectorSessionProgressCompletion.cs`
- `PolymarketLab.DataCollection.Infrastructure/Adapters/RawMessageIngestion/CollectorSessionProgressCompletion.cs`
- `PolymarketLab.DataCollection.Core.Tests/Application/UseCases/ResolutionConsensus/ResolutionConsensusCoordinatorTests.cs`
- `PolymarketLab.DataCollection.Infrastructure.Tests/Integration/Postgres/RawMarketMessageWriterPostgreSqlTests.cs`
- ближайший application DI test
- `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/README.md`
- `docs/agent-context.md`

## Следующий шаг

Начать test-first с `CollectorRawDatasetCompletionCoordinatorTests`:

1. Зафиксировать порядок `DrainingRaw -> runtime.Stop -> progress.Complete -> PostgreSQL GetAsync -> AwaitingNormalization`.
2. Добавить таблицу exact equality и mismatch cases.
3. Проверить failure routing и CAS retry.
4. Запустить только новые Core tests и убедиться, что они падают по ожидаемой причине.
5. Реализовать минимальный coordinator и регистрацию DI.
6. Подключить его к durable consensus.
7. Затем добавить infrastructure и PostgreSQL tests.

## Обязательные tests

- success `1250=1250=1250=1250>0`;
- empty dataset `0=0=0=0` отклоняется;
- in-flight batch ожидается до final persisted count;
- drain timeout инициирует invalidation;
- checkpoint exception инициирует invalidation;
- `persisted=1250`, `raw=1249` инициирует invalidation;
- persisted confirmation после restart вызывает completion без нового Gamma/CLOB polling;
- identical final checkpoint retry не удваивает counters;
- restart context получает все четыре значения одним SQL read.

## Команды проверки

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter FullyQualifiedName~CollectorRawDatasetCompletionCoordinatorTests
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter FullyQualifiedName~ResolutionConsensusCoordinatorTests
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter "FullyQualifiedName~CollectorSessionProgressCompletionTests|FullyQualifiedName~RawMarketMessagePersistenceWorkerTests"
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter FullyQualifiedName~RawMarketMessageWriterPostgreSqlTests
dotnet test .\PolymarketLab.slnx
dotnet build .\PolymarketLab.slnx
git diff --check
```

PostgreSQL integration tests требуют доступного Docker daemon.

## Состояние рабочего дерева и осторожность

До начала работы уже существовали чужие изменения:

```text
M  .harness/harness.lock
M  .harness/health.ps1
M  .harness/skills/REGISTRY.md
M  AGENTS.md
M  PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Migrations/20260831121534_PersistConnectionEpochAndExactRawAccounting.Designer.cs
M  PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Migrations/20260831121534_PersistConnectionEpochAndExactRawAccounting.cs
?? .harness/licenses/mattpocock-skills-v1.2.3-MIT.txt
?? .harness/skills/handoff/
```

Не изменять, не форматировать и не включать эти файлы в работу по `#34`. Созданные в текущей сессии документы — plan и этот handoff. Commit, push, branch и pull request не выполнять без отдельного разрешения пользователя.

## Выполненные проверки

- Прочитаны issue `#34`, зависимости `#24`, `#31`, `#29` и спецификации `#14`, `#12`, `#16`, `#17`.
- Изучены текущие consensus, runtime stop, ingestion completion, PostgreSQL accounting, domain transitions и связанные tests.
- `git diff --check` прошёл; показаны только предупреждения о будущей нормализации line endings в ранее изменённых пользовательских файлах.
- Tests и build не запускались, поскольку production-код ещё не менялся.
