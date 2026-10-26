# PolymarketLab

PolymarketLab регистрирует рынки Polymarket, собирает исходные сообщения market
WebSocket в PostgreSQL и строит версионируемые нормализованные проекции.

## Настройки

API требует строку подключения `Database:ConnectionString`. Для локального
PostgreSQL из `docker-compose.yml`:

```powershell
docker compose up -d postgres
dotnet user-secrets set "Database:ConnectionString" "Host=localhost;Port=5433;Database=polymarket_lab;Username=postgres;Password=postgres" --project .\PolymarketLab.Api\PolymarketLab.Api.csproj
```

В контейнере API строка подключения уже задана через
`Database__ConnectionString`. Пароли из `docker-compose.yml` предназначены
только для локальной разработки.

Нормализатор настраивается секцией `Normalizer` в
`PolymarketLab.Api/appsettings.json`:

```json
{
  "Normalizer": {
    "Enabled": true,
    "ProjectionVersion": 1,
    "WorkerCount": 3,
    "BatchSize": 1000,
    "WriteBatchSize": 500,
    "IdleDelay": "00:00:00.250",
    "ClaimTimeout": "00:05:00",
    "ShutdownTimeout": "00:00:30"
  }
}
```

| Параметр | Назначение |
|---|---|
| `Enabled` | Запускает continuous worker и обновление метрик backlog. Не влияет на сбор и сохранение raw-сообщений. |
| `ProjectionVersion` | Версия создаваемых проекций. Должна быть больше нуля. Данные разных версий хранятся одновременно. |
| `WorkerCount` | Число параллельных worker loops от `1` до `16`. Одна collector session обрабатывается не более чем одним worker одновременно. |
| `BatchSize` | Максимальное число raw-сообщений в одном batch. Должно быть от `1` до `1000`. |
| `WriteBatchSize` | Максимальное число подготовленных результатов в одной PostgreSQL-транзакции. Должно быть от `1` до `1000` и не превышать `BatchSize`. Пакет из нескольких сообщений дополнительно ограничен примерно `10000` создаваемых строк; одно более крупное сообщение остаётся атомарным. |
| `IdleDelay` | Начальная пауза после пустого batch. Последовательные пустые результаты удваивают её до `max(IdleDelay, 2 секунды)`; непустой результат сбрасывает паузу. При включённом worker должна быть больше нуля. |
| `ClaimTimeout` | Время, после которого незавершённый `Processing` считается устаревшим и может быть захвачен повторно. |
| `ShutdownTimeout` | Максимальное время на завершение уже захваченного пакета после начала остановки. Новые пакеты при этом не захватываются. |

Continuous normalizer захватывает сообщения только sessions в `Starting`, `Running`
и `Stopping`, для которых не установлен `InvalidatingAt`. Метрики pending/lag
имеют ту же область: завершённый архив не участвует в поиске работы и подсчёте
backlog. Для исторических sessions используется явный replay; смена
`ProjectionVersion` сама по себе не запускает обработку завершённого архива.
Backoff применяется независимо для каждого worker, снижает частоту пустых
запросов, но не запрещает их одновременное выполнение. При стандартной настройке
добавочная задержка обнаружения новой работы составляет до двух секунд плюс
время запроса.

Любой параметр можно переопределить переменной окружения, например:

```powershell
$env:Normalizer__Enabled = "false"
$env:Normalizer__ProjectionVersion = "2"
```

Настройки проверяются при запуске приложения. Изменение файла или переменной
окружения требует перезапуска API.

При наличии backlog continuous worker сначала обрабатывает session в
`Stopping/AwaitingNormalization`, начиная с самой ранней `AwaitingNormalizationAt`.
Остальные raw-сообщения сохраняют глобальный порядок `raw.id`.

## Миграции

Приложение не применяет миграции автоматически. Перед первым запуском и после
получения новых миграций подними PostgreSQL, задай строку подключения и обнови
оба контекста:

```powershell
docker compose up -d postgres
$env:Database__ConnectionString = "Host=localhost;Port=5433;Database=polymarket_lab;Username=postgres;Password=postgres"

dotnet ef database update --project .\PolymarketLab.Markets.Infrastructure\PolymarketLab.Markets.Infrastructure.csproj --startup-project .\PolymarketLab.Api\PolymarketLab.Api.csproj --context MarketsDbContext -- --environment Development

dotnet ef database update --project .\PolymarketLab.DataCollection.Infrastructure\PolymarketLab.DataCollection.Infrastructure.csproj --startup-project .\PolymarketLab.Api\PolymarketLab.Api.csproj --context DataCollectionDbContext -- --environment Development
```

Миграция Markets `PersistEventIdentityAndSchedule` требует пустую таблицу
`markets`, потому что обязательные event identity и `eventStartTime` нельзя
достоверно восстановить из старой схемы. Для локального disposable-окружения с
существующими рынками пересоздай всю базу целиком, чтобы не оставить collector
sessions со ссылками на удалённые markets. Миграция завершится ошибкой вместо
частичного или вымышленного backfill.

Для нормализатора обязательны миграции `AddNormalizationSchema`,
`AddNormalizationReplayIndexes` и `PersistNormalizationErrorField`. Проверить
применённые миграции можно так:

```powershell
docker compose exec postgres psql -U postgres -d polymarket_lab -c 'SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";'
```

Если `dotnet ef` недоступен, установи совместимую с `.NET 10` версию
`dotnet-ef`. В репозитории нет локального tool manifest.

## Включение и отключение

Для обычной continuous-нормализации оставь:

```text
Normalizer__Enabled=true
```

Для временного отключения:

```powershell
$env:Normalizer__Enabled = "false"
dotnet run --project .\PolymarketLab.Api\PolymarketLab.Api.csproj --launch-profile http
```

При `Enabled=false`:

- collector продолжает получать и сохранять raw-сообщения;
- новые строки нормализации не создаются;
- backlog накапливается;
- gauges нормализатора не обновляются и могут отсутствовать в `/metrics`;
- при следующем включении worker продолжает с необработанных сообщений.

В журнале отключение подтверждается сообщением
`Normalizer background service is disabled.`

## Состояния обработки

Состояние хранится в
`data_collection.raw_message_normalizations` отдельно для каждой пары
`(raw_message_id, projection_version)`.

| Значение | Состояние | Интерпретация |
|---:|---|---|
| `1` | `Pending` | Сообщение ожидает захвата. |
| `2` | `Processing` | Сообщение захвачено worker. |
| `3` | `Processed` | Проекция успешно записана. |
| `4` | `Unsupported` | Для `event_type` нет поддерживаемого нормализатора. |
| `5` | `Invalid` | Payload не соответствует ожидаемому внешнему контракту. |
| `6` | `Failed` | Произошла непредвиденная техническая ошибка. |

`Processed`, `Unsupported`, `Invalid` и `Failed` являются терминальными для
данной версии. Обычный continuous worker их автоматически не повторяет.

Для следующих запросов открой `psql`:

```powershell
docker compose exec postgres psql -U postgres -d polymarket_lab
```

В примерах используется `projection_version = 1`. Замени значение на активную
версию из настроек.

## Просмотр ожидающих сообщений

Следующий запрос соответствует `normalizer_pending_messages`: он показывает
сообщения без ledger-строки, явные `Pending` и устаревшие `Processing`.

```sql
SELECT
    raw.id AS raw_message_id,
    raw.session_id,
    raw.received_at,
    normalization.status,
    normalization.attempt_count,
    normalization.claimed_at
FROM data_collection.raw_market_messages AS raw
LEFT JOIN data_collection.raw_message_normalizations AS normalization
  ON normalization.raw_message_id = raw.id
 AND normalization.projection_version = 1
WHERE normalization.raw_message_id IS NULL
   OR normalization.status = 1
   OR (
       normalization.status = 2
       AND (
           normalization.claimed_at IS NULL
           OR normalization.claimed_at < CURRENT_TIMESTAMP - INTERVAL '5 minutes'
       )
   )
ORDER BY raw.id
LIMIT 100;
```

Интервал должен совпадать с `Normalizer:ClaimTimeout`.

## Просмотр Invalid

`Invalid` означает ожидаемую ошибку входного контракта: невалидный JSON или
UTF-8, отсутствующее поле, неподдерживаемая форма или значение вне допустимого
диапазона. Это обычно требует анализа источника либо обновления контракта, а не
перезапуска worker.

```sql
SELECT
    normalization.raw_message_id,
    raw.session_id,
    raw.received_at,
    normalization.attempt_count,
    normalization.completed_at,
    normalization.error_code,
    normalization.error_message,
    normalization.error_field
FROM data_collection.raw_message_normalizations AS normalization
JOIN data_collection.raw_market_messages AS raw
  ON raw.id = normalization.raw_message_id
WHERE normalization.projection_version = 1
  AND normalization.status = 5
ORDER BY normalization.raw_message_id DESC
LIMIT 100;
```

Payload хранится в `raw.payload`, но не выводится этим запросом, чтобы случайно
не отправить большой или чувствительный вход в терминал и журналы.

## Просмотр Unsupported

`Unsupported` означает, что сообщение распознано, но для его `event_type` нет
зарегистрированного нормализатора текущей версии.

```sql
SELECT
    normalization.raw_message_id,
    raw.session_id,
    raw.received_at,
    normalization.attempt_count,
    normalization.completed_at,
    normalization.error_code,
    normalization.error_message,
    normalization.error_field
FROM data_collection.raw_message_normalizations AS normalization
JOIN data_collection.raw_market_messages AS raw
  ON raw.id = normalization.raw_message_id
WHERE normalization.projection_version = 1
  AND normalization.status = 4
ORDER BY normalization.raw_message_id DESC
LIMIT 100;
```

После добавления поддержки запускай новую `ProjectionVersion`; терминальные
строки старой версии не изменяются.

## Просмотр Failed

`Failed` означает технический дефект или сбой записи, а не ошибку внешнего
контракта. Сначала сопоставь `raw_message_id`, `session_id` и `error_code` со
структурированными журналами API.

```sql
SELECT
    normalization.raw_message_id,
    raw.session_id,
    raw.received_at,
    normalization.attempt_count,
    normalization.claimed_at,
    normalization.completed_at,
    normalization.error_code,
    normalization.error_message,
    normalization.error_field
FROM data_collection.raw_message_normalizations AS normalization
JOIN data_collection.raw_market_messages AS raw
  ON raw.id = normalization.raw_message_id
WHERE normalization.projection_version = 1
  AND normalization.status = 6
ORDER BY normalization.raw_message_id DESC
LIMIT 100;
```

Не меняй `Failed` на `Pending` до исправления причины: это может создать цикл
повторяющихся ошибок. После исправления используй новую версию проекции.

## Восстановление устаревших Processing

Найти захваты, возраст которых превышает `ClaimTimeout`:

```sql
SELECT
    normalization.raw_message_id,
    raw.session_id,
    normalization.attempt_count,
    normalization.claimed_at,
    CURRENT_TIMESTAMP - normalization.claimed_at AS processing_age
FROM data_collection.raw_message_normalizations AS normalization
JOIN data_collection.raw_market_messages AS raw
  ON raw.id = normalization.raw_message_id
WHERE normalization.projection_version = 1
  AND normalization.status = 2
  AND (
      normalization.claimed_at IS NULL
      OR normalization.claimed_at < CURRENT_TIMESTAMP - INTERVAL '5 minutes'
  )
ORDER BY normalization.raw_message_id;
```

Ручной `UPDATE` не требуется. Для активных sessions без invalidation fence включённый worker автоматически захватывает такие
строки повторно, увеличивает `attempt_count`, очищает предыдущую ошибку и
обновляет `claimed_at`. Порядок восстановления:

1. Убедись, что предыдущий экземпляр API действительно остановлен.
2. Проверь строку подключения, миграции и `Normalizer__Enabled=true`.
3. Запусти один экземпляр API.
4. Дождись `ClaimTimeout`, если захват ещё не считается устаревшим.
5. Убедись, что строка стала терминальной либо получила новый `claimed_at` и увеличенный `attempt_count`.

Принудительное изменение ledger во время работающего worker опасно: старый
обработчик может завершить транзакцию после ручного вмешательства.

## Повторная нормализация

### Полный повтор в новую версию

Изменение `Normalizer:ProjectionVersion` задаёт версию continuous normalizer,
но не запускает обработку завершённого архива. Для повторной обработки используй
`ReplayNormalizationCommand`, описанную ниже, с нужной исходной версией и без
фильтров session/event type. Replay обрабатывает доступные записи исходной версии;
он не является восстановлением missing ledger произвольного terminal dataset.
Исходные проекции не удаляются. Не переиспользуй номер версии после изменения
логики или схемы проекции. Не меняй активную версию во время незавершённых sessions:
их immutable snapshot должен совпадать с runtime-версией.

### Выборочный replay

В Application реализован `ReplayNormalizationCommand` с фильтрами по исходной
версии, session и `event_type`. Целевая версия должна быть строго больше
исходной. Replay использует snapshot и batches не больше 100 сообщений, поэтому
новые raw-сообщения не расширяют уже запущенный проход.

Сейчас команда не опубликована через HTTP или CLI. Её можно вызвать только
внутри host/tool с настроенным DI-контейнером через MediatR:

```csharp
await sender.Send(
    new ReplayNormalizationCommand(
        SourceProjectionVersion: 1,
        TargetProjectionVersion: 2,
        SessionId: sessionId,
        EventType: "book"),
    cancellationToken);
```

Если continuous worker включён, target выборочного replay не может совпадать с
его активной `ProjectionVersion`. Операционный replay требует host/tool с DI;
публичного HTTP или CLI запуска пока нет.

## Интерпретация отставания

API публикует gauges с меткой `projection_version`:

- `normalizer_pending_messages` — сообщения, которые можно захватить сейчас: без ledger, `Pending` и устаревшие `Processing`;
- `normalizer_lag_messages` — все незавершённые сообщения: без ledger, `Pending` и любые `Processing`, включая ещё не устаревшие активные захваты.
- `normalizer_phase_duration_ms` — длительность фаз `claim`, `build` и `write` одного прохода с метками `projection_version` и `phase`.

Следовательно:

```text
lag - pending = свежие Processing
```

Метрики учитывают только `Starting`, `Running`, `Stopping` без invalidation fence.
`Invalid`, `Unsupported`, `Failed` и `Processed` в lag не входят. Метрики backlog
обновляются раз в 10 секунд только при `Enabled=true`.

Интерпретация динамики:

| Наблюдение | Возможная причина |
|---|---|
| `lag = 0` | Активная версия догнала все сохранённые raw-сообщения. |
| `lag > 0`, значение уменьшается | Worker обрабатывает накопившийся backlog. |
| `lag` стабилен около размера batch | Обычно это текущие активные `Processing`; сравни с `pending`. |
| `lag` растёт, `pending` растёт | Raw ingestion быстрее нормализатора либо worker не работает. |
| `lag` растёт, `pending = 0` | Сообщения находятся в свежем `Processing`; проверь длительность batch и транзакции. |
| `pending > 0` долго не уменьшается | Worker отключён, не имеет доступа к БД или повторяет ошибки итерации. |

Счётчики `messages_received - messages_persisted` относятся к in-memory raw
ingestion и не равны normalization lag. Сначала raw-сообщение должно быть
сохранено в PostgreSQL, и только после этого оно попадает в backlog
нормализатора.

## Порядок диагностики

1. Проверь, что raw ingestion работает: `messages_received`, `messages_persisted`, `last_message_at` и состояние collector session.
2. Проверь `Database:ConnectionString` и наличие всех миграций обоих контекстов.
3. Проверь активные `Enabled`, `ProjectionVersion`, `WorkerCount`, `BatchSize`, `WriteBatchSize`, `IdleDelay` и `ClaimTimeout` в окружении процесса.
4. Найди в журнале запуск API, сообщение об отключённом worker или `Normalizer background iteration failed`.
5. Сравни `normalizer_lag_messages` и `normalizer_pending_messages` для нужной версии.
6. Выполни запрос ожидающих сообщений и отдельно проверь свежие и устаревшие `Processing`.
7. Проверь `Failed` как технические ошибки, затем `Invalid` как нарушения контракта и `Unsupported` как отсутствующую поддержку типа.
8. Для отдельного сообщения сопоставь `raw_message_id`, `session_id`, `attempt_count` и `error_code` со структурированным журналом. Полный payload по умолчанию не логируй.
9. После аварийной остановки дай worker автоматически восстановить устаревшие claims; не редактируй ledger параллельно с работающим экземпляром.
10. После исправления нормализатора запусти новую `ProjectionVersion` и наблюдай, пока lag новой версии не станет равен нулю.

Метрики доступны на `http://localhost:5285/metrics`. Инструкции по локальным
Prometheus, Grafana и Loki находятся в `observability/README.md`.

## Host acceptance

Deterministic acceptance suite запускает настоящий ASP.NET host и отдельную
PostgreSQL database на тест. Gamma, CLOB и WebSocket заменены управляемыми
transport fakes; обращения к live Polymarket отсутствуют. Нужен работающий
Docker daemon:

```powershell
dotnet test .\PolymarketLab.Acceptance.Tests\PolymarketLab.Acceptance.Tests.csproj
```

Suite применяет migrations `MarketsDbContext` и `DataCollectionDbContext` к
чистой базе и проверяет HTTP routing, model binding, Envelope, автоматическое
создание collector job, параллельный сбор нескольких рынков, временные границы,
restart и cancel cleanup, а также изоляцию сбоя одного рынка от остальных.
Transport fakes делают эти сценарии детерминированными, но не доказывают работу с
live Polymarket. Каждая временная database удаляется после теста.

## Opt-in live run

Live run является отдельной ручной операцией. Он не входит в `dotnet test`, не
заменяет deterministic acceptance и выполняется только после явного решения
оператора использовать выбранный реальный рынок.

1. Зафиксируй `git rev-parse HEAD` и UTC-время через `(Get-Date).ToUniversalTime().ToString("o")`.
2. Запусти PostgreSQL, примени migrations обоих DbContexts и настрой `Database:ConnectionString` через user secrets или переменную окружения. Не помещай строку подключения в журнал evidence.
3. Запусти один экземпляр API: `dotnet run --project .\PolymarketLab.Api\PolymarketLab.Api.csproj --launch-profile http`.
4. Зарегистрируй выбранный market через `POST /api/Market` или dashboard. Регистрация автоматически создаёт долговечный collector job; ручной Start не требуется.
5. Дождись terminal DTO от `GET /api/Collector/{sessionId}`. Успешное завершение сбора имеет `status=Stopped`, `phase=null`, `stopReason=MarketClosed` и `cleanup=null`. Подтверждение winner проверяется отдельно и не является обязательным условием завершения сбора.
6. Проверь в PostgreSQL, что `messages_received = messages_enqueued = messages_persisted = raw rows = Processed rows` для snapshot `projection_version`, и каждое значение больше `0` сообщений.

В комментарии issue сохраняются commit SHA, UTC-время, обезличенные event/market
идентификаторы, `sessionId`, безопасные настройки, terminal DTO без сообщений об
ошибках интеграции и итоговые counts. Не публикуются raw payload, credentials,
строка подключения и stack traces. Transient внешний сбой означает неуспешную
live-попытку, а не дефект deterministic suite.

Если процесс завершился до terminal state, не запускай второй экземпляр API
параллельно. После остановки старого процесса перезапусти один экземпляр на той
же базе. Future `Scheduled` job переживает restart и снова обрабатывается
scheduler без браузера. Уже начатая partial session не возобновляется: startup
reconciliation переводит её через invalidation, атомарно удаляет неполный
raw/normalization/projection dataset, сохраняет cleanup audit и завершает как
`Failed` с `stopReason=ProcessTerminated`. Успешный `Stopped/MarketClosed`
dataset остаётся нетронутым.

## Детерминированный эксперимент на архиве

Существующий API поддерживает отдельный локальный режим. Он не запускает HTTP,
collector, reconciliation, normalizer и cleanup, не обращается к текущим внешним
ценам и читает PostgreSQL в `REPEATABLE READ / READ ONLY`.

```powershell
dotnet run --project .\PolymarketLab.Api\PolymarketLab.Api.csproj --launch-profile http -- --simulation .\experiment.json
```

Используется обычная настройка `Database:ConnectionString` из окружения или User
Secrets. Строка подключения не входит в файл сценария и отчёт. Миграции этим
режимом не применяются.

Приложение пишет JSON-результат в stdout, а служебные сообщения и краткую сводку
в stderr. Сам `dotnet run` может предварить stdout своей строкой о launch profile;
эта строка не входит в канонический отчёт и его отпечаток.

Минимальный `experiment.json` для получения манифеста выбранной сессии:

```json
{
  "version": 1,
  "sessionId": "33a17de5-2b29-4de2-929c-2d1c0ee46588",
  "projectionVersion": 1,
  "expectedManifest": null,
  "initialCash": "100.00",
  "episodeEndsAt": "2026-10-16T09:25:00Z",
  "orders": [],
  "rules": [],
  "btc": [],
  "maximumBtcAge": null,
  "requireFreshBtc": false,
  "settlement": [],
  "settlementPolicy": null
}
```

Без `expectedManifest` выводятся `Session`, её `Tokens`, манифест #57 и отчёт
пригодности; сделок нет. До закрепления задайте все настройки, заявки и внешние
входы. Для запуска скопируйте весь `ExpectedManifest` из ответа в файл. Изменение
сценария или профиля после закрепления требует повторного получения манифеста.
`RawFingerprint` использует исходный SQL-алгоритм эпика #56; полный контракт
находится в `docs/simulation-dataset-contract.md`. При изменении архива или
настроек команда вернёт `experiment.dataset.raw_fingerprint_mismatch` либо
`experiment.dataset.manifest_fingerprint_mismatch`.

Чтобы задать сделку, добавьте в `orders` элемент, подставив фактический `assetId`
из `Session.Tokens` ответа первого запуска:

```json
{
  "sequence": 1,
  "at": "2026-10-16T09:20:01Z",
  "assetId": "TOKEN_ID_FROM_MANIFEST",
  "side": "Buy",
  "quantity": "10",
  "limitPrice": "0.49",
  "latency": "00:00:00.200"
}
```

Правила обязательны для сделки. Следующий элемент массива `rules` — **пример
экспериментального допущения**, а не историческая комиссия Polymarket:

```json
{
  "id": "experiment-only-v1",
  "source": "Explicit hypothetical profile, not historical exchange rules",
  "availableAt": "2026-10-16T09:19:00Z",
  "effectiveAt": "2026-10-16T09:19:00Z",
  "minimumQuantity": "1",
  "quantityQuantum": "1",
  "moneyQuantum": "0.000001",
  "feeBps": "100"
}
```

Здесь `100` базисных пунктов означают комиссию `1%` от денежной стоимости.
Неизвестная комиссия не равна нулевой. Поддерживается только экспериментальная
денежная комиссия; историческое удержание комиссии токенами этим профилем не
моделируется. Минимум применяется к запрошенному размеру, частичный fill может
оказаться меньше. Стоимость и комиссия округляются накопительно по заявке:
списания вверх, выручка вниз, комиссия вверх к `moneyQuantum`.

`btc` — отдельный импортированный ряд. Каждая точка содержит `sequence`, `source`,
`pair` (например `BTC/USD`), `sourceAt`, обязательный `availableAt`, `price` и
`isGap`. Для разрыва `isGap=true`, `price=null`. Отсутствующее `availableAt` не
восстанавливается из времени свечи. При непустом ряде нужен явный
`maximumBtcAge`, например `00:00:02`. Значение примера не является рекомендуемым
порогом. Состояния Missing, Stale, Gap, UnknownSourceTime и ClockSkew отличимы
от Fresh; `requireFreshBtc=true` запрещает отправку и прибытие заявки без Fresh.
Ряд в репозитории не подразумевается существующим, источник и provenance задаются оператором.

`settlement` импортируется независимо: `source`, `reference`, `sessionId`,
`conditionId`, `winningAssetId`, `confirmedAt`, `availableAt`. Политика
`settlementPolicy` задаёт `requiredSources` и `winnerPayout`. Все выбранные
источники должны согласиться; конфликт запрещает выплату. В этой бумажной модели
расчётные деньги начисляются в `episodeEndsAt`, а причинное время достаточного
подтверждения записывается отдельно. Это не реальное погашение токенов.

Порядок версии `1`: доступные market-данные, BTC и правила предшествуют прибывшим
заявкам того же времени; затем выполняется фиксированный сценарий. Нулевая
задержка исполняется непосредственно после своей отправки. Торговое окно
полуоткрытое: `[EventStartsAt, EventEndsAt)`. Все raw-заголовки участвуют в
монотонной доступности `max(ReceivedAt, previousAvailableAt)`; коррекции часов
явно журналируются. Исходный `SourceTimestamp` не переставляет торговые события.
Смена epoch требует новых baseline обоих исходов.

Заявки имеют семантику FAK/IOC: исполнить доступную часть по глубине в пределах
лимита, остаток отменить. Деньги/токены резервируются на задержку. Продажа без
свободных токенов запрещена. Долг потреблённой ликвидности сохраняется по token,
стороне и цене на весь эпизод, включая повторные book, delta и reconnect.
Исторический рынок не реагирует на наши действия; модель не доказывает реальное
контрфактическое исполнение, влияние на рынок или положение в очереди.

Отчёт содержит input/journal hashes, исполнения, комиссии, свободные и
зарезервированные деньги/токены, реализованный результат, неполноту данных и
ликвидационную оценку на конце окна. Оценка не создаёт сделку. Если bids покрывают
только часть позиции, остаток остаётся неоценённым. Без подтверждения исхода
позиция сохраняется; конец 300-секундного окна не означает выплату.

Проверки без реальных внешних запросов:

```powershell
dotnet test .\PolymarketLab.DataCollection.Core.Tests\PolymarketLab.DataCollection.Core.Tests.csproj --filter "FullyQualifiedName~Simulation"
dotnet test .\PolymarketLab.DataCollection.Infrastructure.Tests\PolymarketLab.DataCollection.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Simulation"
```
