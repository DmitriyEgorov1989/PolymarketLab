# PolymarketLab.Web

Frontend для PolymarketLab Collector.

Dashboard позволяет:

- зарегистрировать доступный рынок по Polymarket URL;
- выбрать рынок и просмотреть event/market identity, schedule, outcomes и token ids;
- запустить и остановить collector session;
- наблюдать статусы, durable counters, отставание persistence и ошибки.

## Стек

- React
- TypeScript
- Vite
- TanStack Query

## Команды

```powershell
npm install
npm run dev
npm run test
npm run typecheck
npm run build
```

## Локальный запуск

По умолчанию frontend запускается отдельно от backend:

```powershell
npm run dev
```

Backend API должен быть запущен отдельно из `../PolymarketLab.Api` на
`http://localhost:5285`. В development Vite направляет относительные запросы
`/api` через proxy на backend.

Пример запуска backend из корня репозитория:

```powershell
dotnet run --project .\PolymarketLab.Api\PolymarketLab.Api.csproj --launch-profile http
```

Для API необходима настроенная `Database:ConnectionString` и заранее применённые
EF Core migrations. Локальное окружение с PostgreSQL можно поднять командой:

```powershell
docker compose up -d postgres
```

Если API запускается через Docker Compose, после изменений backend его образ нужно
пересобрать. Обычный restart продолжит использовать старый код:

```powershell
docker compose up -d --build api
```

## Доступные рынки

Frontend запрашивает `GET /api/Market` и обновляет список всех зарегистрированных
рынков каждые 30 секунд. Поэтому будущий рынок можно выбрать и запустить заранее.
Frontend показывает `marketSlug` в списке, а в деталях различает identity
родительского event и дочернего market и отображает все schedule timestamps.
Nullable timestamps показываются как `-`. Schedule не определяет доступность:
Gamma может продолжать принимать orders после формального `eventEndsAt`.

Перед запуском collector backend повторно проверяет identity, schedule и terminal
state через Gamma. Readiness flags проверяются backend на lifecycle boundaries;
frontend не повторяет эту orchestration.

Если выбранный рынок исчезает после обновления списка, frontend сбрасывает выбор
и не переключает управление автоматически на другой рынок.

## Счётчики сообщений

`Messages received` — количество полностью собранных text messages из Polymarket
WebSocket, а не количество сделок. В счётчик входят `price_change`, `book`,
`best_bid_ask`, `last_trade_price` и другие типы событий. Для активных краткосрочных
рынков поток может составлять сотни сообщений в секунду.

`Messages persisted` — количество этих сообщений, подтверждённых PostgreSQL.
Разница между счётчиками показывает текущий backlog сохранения. При включённом
`CollectorWebSocket:CustomFeatureEnabled` поток также содержит глобальные события
вроде `new_market`.

## Lifecycle Сборщика

Dashboard показывает точные status/phase, effective deadline и countdown,
readiness каждого snapshot token, connection epoch, historical counters,
remaining raw rows, resolution WebSocket/Gamma/Clob, normalization и cleanup audit.
Polling выполняется для `Scheduled`, `Starting`, `Running`, `Stopping` и
`Invalidating`, а для terminal и неизвестного status останавливается.

Известная exclusive session любого зарегистрированного рынка блокирует Start до
POST. Backend HTTP `409` остаётся авторитетной защитой гонки. Досрочный Stop требует
подтверждения и отображается как фактический переход `Invalidating -> Failed`.

## Проверки

```powershell
npm run test
npm run typecheck
npm run build
```

Отдельной команды lint в проекте пока нет.

Зафиксированные endpoints и DTO описаны в `../docs/frontend-api-contract.md`.
