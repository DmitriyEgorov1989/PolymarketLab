# Frontend API Contract

## Назначение

Этот документ фиксирует HTTP-контракт первого вертикального среза PolymarketLab.
Backend-код и frontend API-слой должны изменяться вместе с этим документом.

Base URL локального HTTP-профиля: `http://localhost:5285`.

## Общий формат ответа

Все ответы API, включая ошибки model binding, неизвестные маршруты и неожиданные
ошибки, используют `Envelope`.

Успех:

```json
{
  "result": {},
  "listErrors": [],
  "createdUtc": "2026-08-06T12:34:56.789Z"
}
```

Ошибка:

```json
{
  "result": null,
  "listErrors": [
    {
      "errorCode": "collector.query.session.not_found",
      "errorMessage": "Collector session '11111111-1111-1111-1111-111111111111' was not found.",
      "invalidField": "sessionId"
    }
  ],
  "createdUtc": "2026-08-06T12:34:56.789Z"
}
```

`invalidField` равен `null`, если ошибка не относится к конкретному полю.
Неожиданный `500` не содержит текст исключения или stack trace.

## HTTP-коды

| Код | Значение |
|---:|---|
| `200` | Успешный запрос, включая идемпотентную регистрацию или запуск |
| `400` | Ошибка validation, malformed JSON или некорректный request |
| `404` | Market, session или route не найден |
| `409` | Конфликт состояния или уникальности |
| `500` | Неожиданная, integration или persistence ошибка |

`201 Created` и `204 No Content` в этом контракте не используются.

## Статусы CollectorSession

Поле `status` всегда является строкой с одним из точных значений:

```text
Starting
Running
Stopping
Stopped
Failed
Interrupted
Scheduled
Invalidating
```

Поле `phase` уточняет нетерминальную session и является строкой с одним из точных
значений:

```text
WaitingForPreparation
Connecting
AwaitingInitialBooks
AwaitingHeartbeat
ReadyBeforeWindow
CollectingWindow
AwaitingResolution
DrainingRaw
AwaitingNormalization
Cleaning
```

Для terminal statuses (`Stopped`, `Failed`, `Interrupted`) и legacy session `phase`
равен `null`.

Поля `source` и `status` resolution observations являются строками с точными
значениями:

```text
source:  WebSocket, Gamma, Clob
status:  Rejected, NonTerminal, Terminal, Failed, Conflict
```

Числовые значения enum в HTTP-контракте не используются.

## Market DTO

```json
{
  "marketId": "11111111-1111-1111-1111-111111111111",
  "externalEventId": "67890",
  "eventSlug": "example-event",
  "externalMarketId": "12345",
  "marketSlug": "example-market",
  "conditionId": "0xcondition",
  "question": "Will the event happen?",
  "discoveredAt": "2026-08-01T09:00:00Z",
  "externalCreatedAt": "2026-07-31T12:00:00Z",
  "ordersOpenedAt": "2026-08-01T09:30:00Z",
  "gammaStartDate": "2026-08-01T09:45:00Z",
  "eventStartsAt": "2026-08-01T10:00:00Z",
  "eventEndsAt": "2026-08-02T10:00:00Z",
  "externalClosedAt": null,
  "scheduleRefreshedAt": "2026-08-01T09:55:00Z",
  "tokens": [
    {
      "tokenId": "token-yes",
      "outcome": "Yes",
      "outcomeIndex": 0
    }
  ]
}
```

`discoveredAt`, `eventStartsAt`, `eventEndsAt` и `scheduleRefreshedAt` обязательны.
`externalCreatedAt`, `ordersOpenedAt`, `gammaStartDate` и `externalClosedAt` могут
быть `null`. `eventSlug` идентифицирует родительский event, а `marketSlug` - его
дочерний market; эти значения не обязаны совпадать.

## GET /api/Market

Возвращает все зарегистрированные рынки, отсортированные backend по `marketSlug`.
Schedule timestamps являются внешними метаданными и не используются для фильтрации:
Gamma может продолжать принимать orders после формального `eventEndsAt`.

Опциональный query parameter `tradingNow=true` оставляет только рынки, для которых
свежий ответ Gamma одновременно содержит `active: true`, `closed: false`,
`acceptingOrders: true` и `enableOrderBook: true`. Проверка выполняется для каждого
зарегистрированного рынка. Если доступность хотя бы одного рынка проверить не
удалось, endpoint возвращает integration error и не выдаёт частичный или устаревший
список за актуальный.

Frontend использует:

```http
GET /api/Market?tradingNow=true
```

Успешный `result`:

```json
{
  "markets": []
}
```

Отсутствие подходящих рынков является успешным состоянием и возвращает пустой массив.

## GET /api/Market/{marketId}

Возвращает рынок по GUID.

Успешный `result`:

```json
{
  "market": {
    "marketId": "11111111-1111-1111-1111-111111111111",
    "externalEventId": "67890",
    "eventSlug": "example-event",
    "externalMarketId": "12345",
    "marketSlug": "example-market",
    "conditionId": "0xcondition",
    "question": "Will the event happen?",
    "discoveredAt": "2026-08-01T09:00:00Z",
    "externalCreatedAt": null,
    "ordersOpenedAt": null,
    "gammaStartDate": null,
    "eventStartsAt": "2026-08-01T10:00:00Z",
    "eventEndsAt": "2026-08-02T10:00:00Z",
    "externalClosedAt": null,
    "scheduleRefreshedAt": "2026-08-01T09:55:00Z",
    "tokens": []
  }
}
```

Неизвестный `marketId` возвращает `404`.

## POST /api/Market

Регистрирует рынок по Polymarket URL.

Backend интерпретирует URL как event URL и запрашивает Gamma
`/events/slug/{eventSlug}`. Event должен содержать ровно один дочерний market:
нулевое или множественное количество markets отклоняется без неявного выбора.
Event slug и slug дочернего market являются разными идентификаторами и не обязаны
совпадать.

Request:

```json
{
  "marketUri": "https://polymarket.com/event/example-market"
}
```

Успешный `result`:

```json
{
  "marketId": "11111111-1111-1111-1111-111111111111",
  "created": true
}
```

`created: true` означает, что текущий запрос создал рынок. Повторная регистрация
того же рынка возвращает тот же `marketId`, `created: false` и HTTP `200`.

Новый рынок можно зарегистрировать до открытия заявок: `active` и
`acceptingOrders` не ограничивают регистрацию. Требуются `closed: false`,
отсутствующий `closedTime`, `umaResolutionStatus`, отличный от `resolved`, и
`enableOrderBook: true`.
Закрытый или разрешённый новый рынок возвращает `409` с кодом
`market.registration.unavailable`; выключенный order book сохраняет более точный
код `market.registration.order_book_disabled`.

Identity включает event ID/slug, market ID/slug, condition и упорядоченные
`(tokenId, outcome, outcomeIndex)`. Частичное совпадение возвращает
`market.registration.identity_conflict`. При полном совпадении backend сохраняет
тот же `marketId`, оставляет `discoveredAt` неизменным, обновляет внешнее расписание
и `scheduleRefreshedAt`.

## CollectorSession DTO

GET и Stop возвращают одинаковый полный снимок session:

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

Правила nullable:

- `snapshot` всегда присутствует; `externalEventId`, `eventSlug`,
  `externalMarketId`, `marketSlug`, `conditionId`, `eventStartsAt`,
  `eventEndsAt` и `projectionVersion` равны `null` только у legacy session.
  `tokens` всегда массив в порядке `outcomeIndex`.
- `phase` равен `null` для `Stopped`, `Failed`, `Interrupted` и legacy session.
- `startedAt`, `subscriptionReadyAt`, `stoppedAt`, `invalidatingAt`,
  `stopReason`, `failureCode`, `failureMessage` и `lastMessageAt` могут быть `null`.
- `readiness.tokens[].initialBookEnqueuedAt` равен `null`, если для токена нет
  durable observation текущей connection epoch; timestamp не переносится между
  epoch.
- `normalization` равен `null`, если session legacy без `projectionVersion` либо
  committed cleanup уже удалил dataset; иначе содержит текущие remaining counts
  snapshot-версии.
- `resolution` всегда присутствует; `signaledAt`, `confirmedAt`, `winningTokenId`,
  `winningOutcome`, `connectionEpoch` и `lastPollingCycleAt` равны `null`, а
  `sourceStates` и `confirmationSources` пусты, пока durable observation нет.
- `cleanup` равен `null` до committed cleanup. После cleanup содержит
  `invalidatingAt`, `cleanedAt`, сохранённые `projectionVersion`,
  `failureCode`/`failureMessage` и deleted counts.

`effectiveDeadline` вычисляется только для фаз с фиксированной границей:

| Фаза | Граница |
|---|---|
| `WaitingForPreparation` | `eventStartsAt - 60s` |
| `Connecting`, `AwaitingInitialBooks`, `AwaitingHeartbeat` | `eventStartsAt - 10s`; при позднем `startedAt` (в диапазоне `T-10s..T`) — `eventStartsAt` |
| `ReadyBeforeWindow` | `eventStartsAt` |
| `CollectingWindow` | `eventEndsAt` |
| `AwaitingResolution` | `eventEndsAt + 5m` |
| `AwaitingNormalization` | `awaitingNormalizationAt + 5m` |
| `DrainingRaw`, `Cleaning`, terminal statuses | `null` |

`sourceStates` содержит последнее observation каждого источника по
`(observedAt, id)` в порядке `WebSocket`, `Gamma`, `Clob`. `confirmationSources`
содержит exact terminal evidence состоявшегося consensus: WebSocket observation,
сопоставленный с сохранёнными `resolutionSignaledAt`/winner/epoch, и Gamma/Clob
observations по идентификаторам из confirmation reference. Поэтому более позднее
non-terminal observation не скрывает evidence уже состоявшегося подтверждения.

Historical counters `messagesReceived`, `messagesEnqueued` и `messagesPersisted`
накопительные в пределах session и не обнуляются после cleanup.
`remainingRawMessageCount` — авторитетное текущее количество raw-сообщений в
PostgreSQL; после cleanup равен `0`, а `normalization` становится `null`, потому
что raw/ledger/projections намеренно удалены; cleanup audit объясняет отсутствие
данных.

Ответ не содержит raw payload, credentials, exception text, stack trace, raw
provenance (`rawMessageId`, `rawItemIndex`) и outcome arrays наблюдений.

## GET /api/Collector/{sessionId}

Возвращает session по GUID. `result.session` имеет форму `CollectorSession DTO`.

Неизвестный `sessionId` возвращает `404`.

## GET /api/Collector/by-market/{marketId}

Возвращает активную session рынка. Если активной session нет, возвращает последнюю
по `createdAt`. Если sessions нет, возвращает HTTP `200`:

```json
{
  "result": {
    "session": null
  },
  "listErrors": [],
  "createdUtc": "2026-08-06T12:34:56.789Z"
}
```

Endpoint не различает неизвестный market и рынок без истории sessions.

## POST /api/Collector

Запускает collector для рынка.

Request:

```json
{
  "marketId": "11111111-1111-1111-1111-111111111111"
}
```

Успешный `result`:

```json
{
  "sessionId": "22222222-2222-2222-2222-222222222222",
  "marketId": "11111111-1111-1111-1111-111111111111",
  "status": "Scheduled"
}
```

Backend возвращает фактический сохранённый status. Ранний Start создаёт session как
`Scheduled`; Start после `T-60s`, прошедший preparation checks, возвращает `Starting`.
Session сразу занимает глобальный exclusive slot. Если exclusive session
этого же рынка уже существует, новая не создаётся и возвращается существующая
session без повторного запроса Gamma. Если slot занят другим рынком, endpoint
возвращает `409` с кодом `collector.start.global_session_conflict`.

При свободном slot backend сначала читает сохранённый `EventStartsAt` без Gamma.
Если `EventStartsAt <= now`, endpoint возвращает `409` с кодом
`collector.start.market_already_open`, не вызывает Gamma и не создаёт session.

Перед созданием новой session backend повторно запрашивает Gamma и сохраняет
неизменяемый snapshot identity, расписания и ordered tokens. Временная readiness
policy не применяется на этом шаге, поэтому корректный future market может иметь
`acceptingOrders: false`. Ошибка Gamma возвращается без замены исходного кода и
сообщения.

Для новой session проверка выполняется до её создания. До `T-60s`
`POST /api/Collector` не подключает WebSocket: session остаётся
`Scheduled/WaitingForPreparation`. Начиная с `T-60s`, lifecycle scheduler требует
`active=true`, `closed=false`, `acceptingOrders=true`, `enableOrderBook=true`,
выполняет CAS в `Starting/Connecting` и запускает runtime. Обычный readiness
deadline равен `T-10s`; для Start в диапазоне `T-10s..EventStartsAt` deadline
равен `EventStartsAt`. Snapshot live-проверки остаётся неизменяемым для всей
session; mismatch инициирует `Invalidating/Cleaning`.

Удалённое закрытие WebSocket переводит session в `Failed` с кодом
`collector.runtime.receive.closed`, в том числе если Polymarket закрывает connection
после завершения краткосрочного рынка. Автоматическое преобразование такого случая
в `Stopped` не выполняется.

## POST /api/Collector/{sessionId}/stop

Останавливает session. Request body отсутствует.

`result.session` имеет форму `CollectorSession DTO`, как и GET endpoints, включая
full evidence slices. Для активной session после установки write fence статус
равен `Invalidating` с `failureCode: collector.stop.requested`.

Первый Stop до успешного завершения атомарно устанавливает durable write fence и
возвращает `Invalidating` с первой сохранённой причиной. Повторный Stop является
идемпотентным и возвращает фактическое состояние. Для ранее завершившейся session
сохраняются её status, `failureCode` и `failureMessage`.
