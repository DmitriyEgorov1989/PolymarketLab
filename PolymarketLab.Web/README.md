# PolymarketLab.Web

Frontend для PolymarketLab Collector.

Dashboard позволяет:

- зарегистрировать доступный рынок по Polymarket URL;
- выбрать рынок и просмотреть его outcomes и token ids;
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

## Доступные рынки

Список обновляется каждые 30 секунд. Backend исключает рынки, которые ещё не
начались или уже завершились по сохранённым `startsAt` и `endsAt`.

Перед запуском collector backend повторно проверяет Gamma. Сбор разрешён только
при `active: true`, `closed: false`, `acceptingOrders: true`, включённом order book
и попадании текущего времени в окно рынка. Поэтому досрочно закрытый рынок может
оставаться в списке до `endsAt`, но запуск collector будет отклонён с `409`.

Если выбранный рынок исчезает после обновления списка, frontend сбрасывает выбор
и не переключает управление автоматически на другой рынок.

## Проверки

```powershell
npm run test
npm run typecheck
npm run build
```

Отдельной команды lint в проекте пока нет.

Зафиксированные endpoints и DTO описаны в `../docs/frontend-api-contract.md`.
