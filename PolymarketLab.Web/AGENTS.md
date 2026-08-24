# PolymarketLab.Web Agent Contract

## Контекст

Перед frontend-задачей прочитай:

- `../AGENTS.md` и `../docs/agent-context.md`;
- `../docs/frontend-context.md` для продуктового MVP;
- `../docs/frontend-api-contract.md` для документированного HTTP-контракта;
- фактические backend controllers и DTO, которые имеют приоритет при расхождении.

Frontend является одной React dashboard-страницей. Он управляет backend и отображает рынки, collector sessions, counters и ошибки. Браузер не подключается напрямую к Polymarket WebSocket.

## Архитектура

- Server state хранить в TanStack Query; React state использовать только для UI-состояния.
- Не дублировать server state в Context, localStorage или component state.
- Не выставлять server statuses вручную после mutation: отображать фактический ответ и обновлять queries.
- HTTP-вызовы держать в `src/api` и вызывать через query/mutation hooks, не из компонентов.
- Не использовать `any`; неизвестные значения принимать как `unknown` и проверять.
- Сохранять точные значения backend enum и структуру `Envelope`.
- Не выдумывать endpoints или DTO. Backend errors показывать безопасно, не теряя HTTP status, error code и полезное сообщение.
- Не добавлять абстракции, router или global state manager до появления реальной необходимости.

## UI И Тесты

- Поддерживать loading, empty, error и success states.
- Даты показывать в локальном времени, counters форматировать через `Intl.NumberFormat`, `null` показывать как `-`.
- Неизвестный статус отображать как `Unknown`; token ids и таблицы не должны ломать mobile viewport.
- Основной сценарий должен работать с клавиатуры; inputs имеют labels, focus видим, статус не передаётся только цветом.
- Форму регистрации не очищать при ошибке и блокировать во время mutation; после успеха очистить и обновить список.
- Start блокировать без выбранного рынка, во время mutation и при активной session. Stop разрешать только для рабочей session и блокировать до ответа или изменения статуса.
- `unpersisted = messagesReceived - messagesPersisted` считать только производным отображаемым значением, не server state.
- Новую логику проверять на подходящем уровне: API/error parsing и formatters unit-тестами, пользовательские состояния component-тестами.
- Component tests должны покрывать loading/empty/error/success, регистрацию success/error, выбор рынка, Start/Stop disabled states, Failed/lastError и polling активной session.

Проверки из этой директории:

```powershell
npm run test
npm run typecheck
npm run build
```

Отдельного `npm run lint` нет. Не заявляй его результат.

## Границы

Без отдельного задания не меняй backend, не добавляй authentication, wallet, trading, SignalR, frontend WebSocket, raw-message viewer, Redux, Playwright или новые dependencies. Не отключай TypeScript strict mode и не игнорируй TypeScript errors.
