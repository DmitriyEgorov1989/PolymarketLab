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
  "externalMarketId": "12345",
  "slug": "example-market",
  "conditionId": "0xcondition",
  "question": "Will the event happen?",
  "startsAt": "2026-08-01T10:00:00Z",
  "endsAt": "2026-08-02T10:00:00Z",
  "tokens": [
    {
      "tokenId": "token-yes",
      "outcome": "Yes",
      "outcomeIndex": 0
    }
  ]
}
```

`startsAt` и `endsAt` могут быть `null`.

## GET /api/Market

Возвращает зарегистрированные рынки, отсортированные backend по slug.

Успешный `result`:

```json
{
  "markets": []
}
```

Отсутствие рынков является успешным состоянием и возвращает пустой массив.

## GET /api/Market/{marketId}

Возвращает рынок по GUID.

Успешный `result`:

```json
{
  "market": {
    "marketId": "11111111-1111-1111-1111-111111111111",
    "externalMarketId": "12345",
    "slug": "example-market",
    "conditionId": "0xcondition",
    "question": "Will the event happen?",
    "startsAt": null,
    "endsAt": null,
    "tokens": []
  }
}
```

Неизвестный `marketId` возвращает `404`.

## POST /api/Market

Регистрирует рынок по Polymarket URL.

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
  "failureMessage": null
}
```

`startedAt`, `stoppedAt`, `failureCode` и `failureMessage` могут быть `null`.
Для `Failed` backend возвращает сохранённые `failureCode` и `failureMessage`.
Counters в первый контракт не входят.

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
    "failureMessage": null
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
    "failureMessage": null
  }
}
```

Повторный Stop является идемпотентным и возвращает фактическое терминальное
состояние. Для ранее завершившейся с ошибкой session сохраняется `Failed` с
`failureCode` и `failureMessage`; статус вручную не заменяется на `Stopped`.
