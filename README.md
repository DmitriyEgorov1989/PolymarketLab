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
    "BatchSize": 500,
    "IdleDelay": "00:00:00.250",
    "ClaimTimeout": "00:05:00"
  }
}
```

| Параметр | Назначение |
|---|---|
| `Enabled` | Запускает continuous worker и обновление метрик backlog. Не влияет на сбор и сохранение raw-сообщений. |
| `ProjectionVersion` | Версия создаваемых проекций. Должна быть больше нуля. Данные разных версий хранятся одновременно. |
| `BatchSize` | Максимальное число raw-сообщений в одном batch. Должно быть больше нуля. |
| `IdleDelay` | Пауза после пустого batch. При включённом worker должна быть больше нуля. |
| `ClaimTimeout` | Время, после которого незавершённый `Processing` считается устаревшим и может быть захвачен повторно. |

Любой параметр можно переопределить переменной окружения, например:

```powershell
$env:Normalizer__Enabled = "false"
$env:Normalizer__ProjectionVersion = "2"
```

Настройки проверяются при запуске приложения. Изменение файла или переменной
окружения требует перезапуска API.

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

Для нормализатора обязательны миграции `AddNormalizationSchema` и
`AddNormalizationReplayIndexes`. Проверить применённые миграции можно так:

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
    normalization.error_message
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
    normalization.error_message
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
    normalization.error_message
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

Ручной `UPDATE` не требуется. Включённый worker автоматически захватывает такие
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

Чтобы повторно обработать все сохранённые raw-сообщения, увеличь
`Normalizer:ProjectionVersion` и перезапусти API:

```powershell
$env:Normalizer__Enabled = "true"
$env:Normalizer__ProjectionVersion = "2"
dotnet run --project .\PolymarketLab.Api\PolymarketLab.Api.csproj --launch-profile http
```

Для версии `2` ledger сначала отсутствует, поэтому continuous worker обработает
всю таблицу `raw_market_messages`, а затем продолжит обрабатывать новые
сообщения. Версия `1` и её проекции не удаляются. Не переиспользуй номер версии
после изменения логики или схемы проекции.

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
его активной `ProjectionVersion`. До появления административного endpoint для
операционного полного replay используй увеличение `ProjectionVersion`.

## Интерпретация отставания

API публикует gauges с меткой `projection_version`:

- `normalizer_pending_messages` — сообщения, которые можно захватить сейчас: без ledger, `Pending` и устаревшие `Processing`;
- `normalizer_lag_messages` — все незавершённые сообщения: без ledger, `Pending` и любые `Processing`, включая ещё не устаревшие активные захваты.

Следовательно:

```text
lag - pending = свежие Processing
```

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
3. Проверь активные `Enabled`, `ProjectionVersion`, `BatchSize`, `IdleDelay` и `ClaimTimeout` в окружении процесса.
4. Найди в журнале запуск API, сообщение об отключённом worker или `Normalizer background iteration failed`.
5. Сравни `normalizer_lag_messages` и `normalizer_pending_messages` для нужной версии.
6. Выполни запрос ожидающих сообщений и отдельно проверь свежие и устаревшие `Processing`.
7. Проверь `Failed` как технические ошибки, затем `Invalid` как нарушения контракта и `Unsupported` как отсутствующую поддержку типа.
8. Для отдельного сообщения сопоставь `raw_message_id`, `session_id`, `attempt_count` и `error_code` со структурированным журналом. Полный payload по умолчанию не логируй.
9. После аварийной остановки дай worker автоматически восстановить устаревшие claims; не редактируй ledger параллельно с работающим экземпляром.
10. После исправления нормализатора запусти новую `ProjectionVersion` и наблюдай, пока lag новой версии не станет равен нулю.

Метрики доступны на `http://localhost:5285/metrics`. Инструкции по локальным
Prometheus, Grafana и Loki находятся в `observability/README.md`.
