# Frontend Context

## Назначение

Этот документ фиксирует продуктовый контекст frontend MVP для PolymarketLab.

Агентские правила разработки frontend находятся в
`PolymarketLab.Web/AGENTS.md`. Документ самодостаточен и не зависит от локальных
файлов вне репозитория.

## Контекст продукта

PolymarketLab - приложение для регистрации рынков Polymarket и запуска серверного коллектора рыночных данных.

Основной пользовательский сценарий:

```text
Пользователь вставляет ссылку на рынок Polymarket
        -> backend получает метаданные рынка и token ids
        -> пользователь запускает CollectorSession
        -> backend подключается к Polymarket WebSocket
        -> backend сохраняет raw JSON в PostgreSQL
        -> frontend показывает состояние, counters и ошибки
        -> пользователь корректно останавливает сбор
```

Frontend не собирает данные Polymarket самостоятельно. Он только управляет backend и отображает состояние.

## Цель MVP

Пользователь должен иметь возможность:

- добавить рынок по Polymarket URL;
- увидеть зарегистрированные рынки, на которых сейчас доступны торги;
- выбрать рынок;
- увидеть вопрос, event/market identity, schedule timestamps, outcomes и token ids;
- запустить CollectorSession;
- наблюдать статус коллектора;
- видеть количество полученных и сохранённых сообщений;
- видеть reconnect count и последнюю ошибку;
- корректно остановить активную сессию.

Главный критерий успеха:

```text
Пользователь может выполнить полный сценарий от добавления рынка до graceful stop без Swagger и прямого доступа к PostgreSQL.
```

## Не входит в MVP

Не реализовывать без отдельного задания:

- авторизацию;
- подключение кошелька;
- торговлю;
- выставление заявок;
- графики цен;
- реконструкцию стакана;
- просмотр raw messages;
- backtesting;
- SignalR;
- frontend WebSocket к Polymarket;
- AI-интерфейс;
- административную панель;
- удаление и редактирование рынков;
- сложную дизайн-систему;
- Redux, MobX или другой глобальный state manager.

## Основной экран

Первая версия - одна dashboard-страница.

Desktop layout:

```text
+------------------------------------------------------------+
| PolymarketLab Collector                                    |
+------------------------------------------------------------+
| Форма добавления рынка                                     |
+----------------------+-------------------------------------+
| Список рынков        | Детали выбранного рынка             |
|                      | Outcomes и token ids                |
|                      | Управление коллектором              |
|                      | Status / counters / errors          |
+----------------------+-------------------------------------+
```

Mobile layout:

```text
Header
Форма добавления рынка
Список рынков
Детали выбранного рынка
Collector panel
```

## Backend API

Зафиксированный контракт первого вертикального среза описан в
`docs/frontend-api-contract.md`. Фактические endpoints:

```http
GET  /api/Market?tradingNow=true
GET  /api/Market/{marketId}
POST /api/Market
GET  /api/Collector/{sessionId}
GET  /api/Collector/by-market/{marketId}
POST /api/Collector
POST /api/Collector/{sessionId}/stop
```

Все ответы используют `Envelope`. Collector session read API возвращает durable
received/persisted counters, время последнего сообщения и reconnect count.
`messagesReceived` означает число полных WebSocket text messages, а не число сделок;
активные краткосрочные рынки могут генерировать сотни таких сообщений в секунду.

`GET /api/Market` без query parameters возвращает все зарегистрированные рынки.
Frontend использует `GET /api/Market?tradingNow=true`: backend проверяет каждый
рынок через Gamma и возвращает только рынки с активными торгами. При ошибке live-
проверки frontend скрывает устаревший список. Список обновляется каждые 30 секунд.
Schedule timestamps остаются отображаемыми метаданными: Gamma status flags имеют
приоритет, поскольку orders могут приниматься после формального `eventEndsAt`.

Перед созданием collector session backend выполняет live-проверку Gamma. Сбор
доступен только при `active`, отсутствии `closed`, включённых `acceptingOrders` и
order book. Ошибка
доступности возвращается как `409 market.collection.unavailable`, а integration
errors Gamma сохраняют исходные код и сообщение.

При прямом обращении браузера к API допустимые origins задаются массивом
`Cors:AllowedOrigins`. В Development разрешён `http://localhost:5173`.
Для production список передаётся конфигурацией, например
`Cors__AllowedOrigins__0=https://app.example.com`. Credentials не включены.
Локальный Vite proxy `/api` продолжает работать без CORS.

## Минимальные frontend-модели

Модели должны соответствовать реальным backend DTO. Ниже - целевая форма для проектирования, а не разрешение выдумывать контракт.

```ts
export interface MarketTokenDto {
  tokenId: string;
  outcome: string;
  outcomeIndex: number;
}

export interface MarketDto {
  marketId: string;
  externalEventId: string;
  eventSlug: string;
  externalMarketId: string;
  marketSlug: string;
  conditionId: string;
  question: string;
  discoveredAt: string;
  externalCreatedAt: string | null;
  ordersOpenedAt: string | null;
  gammaStartDate: string | null;
  eventStartsAt: string;
  eventEndsAt: string;
  externalClosedAt: string | null;
  scheduleRefreshedAt: string;
  tokens: MarketTokenDto[];
}

export interface CollectorSessionDto {
  sessionId: string;
  marketId: string;
  status: 'Starting' | 'Running' | 'Stopping' | 'Stopped' | 'Failed' | 'Interrupted';
  createdAt: string;
  startedAt: string | null;
  stoppedAt: string | null;
  failureCode: string | null;
  failureMessage: string | null;
  messagesReceived: number;
  messagesPersisted: number;
  lastMessageAt: string | null;
  reconnectCount: number;
}
```

## Первый рекомендуемый milestone

```text
Открыть frontend
    -> добавить активный рынок по URL
    -> увидеть рынок и его token ids
    -> запустить CollectorSession
    -> дождаться Running
    -> увидеть рост MessagesReceived и MessagesPersisted
    -> остановить сессию
    -> дождаться terminal status
    -> убедиться, что MessagesReceived == MessagesPersisted
```

Это основной вертикальный срез. Визуальные улучшения вторичны, пока сценарий не работает надёжно.

## Definition of Done frontend MVP

- Приложение запускается локально.
- TypeScript strict mode включён.
- Список рынков загружается.
- Список зарегистрированных рынков с активными торгами периодически обновляется.
- Loading/empty/error states реализованы.
- Рынок регистрируется по URL.
- Backend errors отображаются корректно.
- Созданный рынок появляется в списке.
- Рынок можно выбрать.
- Детали и token ids отображаются.
- CollectorSession запускается.
- Перед запуском CollectorSession backend проверяет актуальную доступность рынка.
- Вторая активная сессия не запускается из UI.
- Статусы отображаются без искажения смысла.
- Polling работает для активной session.
- Messages received отображается.
- Messages persisted отображается.
- Unpersisted отображается.
- Reconnect count отображается.
- Last error отображается.
- Session можно остановить.
- Frontend ждёт фактический terminal status от backend.
- Responsive layout работает.
- Основные элементы доступны с клавиатуры.
- Unit/component tests проходят.
- `npm run build` проходит.
- Frontend не подключается напрямую к Polymarket.
- Secrets не находятся в Git.
