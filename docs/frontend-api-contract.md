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

```json
{
  "sessionId": "22222222-2222-2222-2222-222222222222",
  "marketId": "11111111-1111-1111-1111-111111111111",
  "status": "Running",
  "createdAt": "2026-08-06T12:00:00Z",
  "startedAt": "2026-08-06T12:00:01Z",
  "stoppedAt": null,
  "failureCode": null,
  "failureMessage": null,
  "messagesReceived": 120,
  "messagesPersisted": 118,
  "lastMessageAt": "2026-08-06T12:29:59Z",
  "reconnectCount": 0
}
```

`startedAt`, `stoppedAt`, `failureCode`, `failureMessage` и `lastMessageAt` могут быть `null`.
Для `Failed` backend возвращает сохранённые `failureCode` и `failureMessage`.
`messagesReceived` считает полностью собранные text messages, а не сделки. В него
входят `price_change`, `book`, `best_bid_ask`, `last_trade_price` и другие типы
Polymarket WebSocket events. При `custom_feature_enabled: true` принимаются также
глобальные события, например `new_market`. `messagesPersisted` считает сообщения,
подтверждённые PostgreSQL. Counters накопительные в пределах session. Reconnect пока
не реализован, поэтому `reconnectCount` остаётся `0`.

## GET /api/Collector/{sessionId}

Возвращает session по GUID.

Успешный `result`:

```json
{
  "session": {
    "sessionId": "22222222-2222-2222-2222-222222222222",
    "marketId": "11111111-1111-1111-1111-111111111111",
    "status": "Running",
    "createdAt": "2026-08-06T12:00:00Z",
    "startedAt": "2026-08-06T12:00:01Z",
    "stoppedAt": null,
    "failureCode": null,
    "failureMessage": null,
    "messagesReceived": 120,
    "messagesPersisted": 118,
    "lastMessageAt": "2026-08-06T12:29:59Z",
    "reconnectCount": 0
  }
}
```

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
  "status": "Running"
}
```

Backend возвращает фактический сохранённый status. Для новой session после
успешного запуска это обычно `Running`. Если активная session уже существует,
новая не создаётся и возвращается существующая session со статусом `Starting`,
`Running` или `Stopping`.

Перед созданием новой session backend повторно запрашивает Gamma. Рынок доступен
для сбора только при `active: true`, `closed: false`, `acceptingOrders: true`,
`enableOrderBook: true`. Внешние даты не переопределяют эти status flags.
Недоступный рынок возвращает `409` с кодом `market.collection.unavailable`;
ошибка Gamma возвращается без замены исходного кода и сообщения.

Для новой session проверка выполняется до её создания и запуска runtime. Повторный
Start при существующей активной session остаётся идемпотентным и возвращает эту
session без запроса Gamma. Live-проверка является авторитетной, даже если закрытый
рынок ещё присутствует в нефильтрованном `GET /api/Market`.

Удалённое закрытие WebSocket переводит session в `Failed` с кодом
`collector.runtime.receive.closed`, в том числе если Polymarket закрывает connection
после завершения краткосрочного рынка. Автоматическое преобразование такого случая
в `Stopped` не выполняется.

## POST /api/Collector/{sessionId}/stop

Останавливает session. Request body отсутствует.

Успешный `result`:

```json
{
  "session": {
    "sessionId": "22222222-2222-2222-2222-222222222222",
    "marketId": "11111111-1111-1111-1111-111111111111",
    "status": "Stopped",
    "createdAt": "2026-08-06T12:00:00Z",
    "startedAt": "2026-08-06T12:00:01Z",
    "stoppedAt": "2026-08-06T12:30:00Z",
    "failureCode": null,
    "failureMessage": null,
    "messagesReceived": 120,
    "messagesPersisted": 120,
    "lastMessageAt": "2026-08-06T12:29:59Z",
    "reconnectCount": 0
  }
}
```

Повторный Stop является идемпотентным и возвращает фактическое терминальное
состояние. Для ранее завершившейся с ошибкой session сохраняется `Failed` с
`failureCode` и `failureMessage`; статус вручную не заменяется на `Stopped`.
