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

Frontend запрашивает `GET /api/Market?tradingNow=true` и обновляет список каждые
30 секунд. Backend оставляет только рынки, для которых свежий ответ Gamma содержит
`active: true`, `closed: false`, `acceptingOrders: true` и включённый order book.
Frontend показывает `marketSlug` в списке, а в деталях различает identity
родительского event и дочернего market и отображает все schedule timestamps.
Nullable timestamps показываются как `-`. Schedule не определяет доступность:
Gamma может продолжать принимать orders после формального `eventEndsAt`.

Перед запуском collector backend повторно проверяет Gamma. Сбор разрешён только
при `active: true`, `closed: false`, `acceptingOrders: true`, включённом order book
по данным Gamma. При ошибке обновления frontend скрывает ранее загруженный список,
поскольку его актуальность больше не подтверждена.

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

## Проверки

```powershell
npm run test
npm run typecheck
npm run build
```

Отдельной команды lint в проекте пока нет.

Зафиксированные endpoints и DTO описаны в `../docs/frontend-api-contract.md`.
