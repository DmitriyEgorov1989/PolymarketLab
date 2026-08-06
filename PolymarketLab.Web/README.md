# PolymarketLab.Web

Frontend для PolymarketLab Collector.

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

Зафиксированные endpoints и DTO описаны в `../docs/frontend-api-contract.md`.
