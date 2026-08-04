# PolymarketLab.Web AGENTS.md

## Назначение

Этот файл содержит правила для разработки frontend-части PolymarketLab.

Перед frontend-задачами также читать:

- `../AGENTS.md` - общие правила репозитория;
- `../docs/frontend-context.md` - продуктовый контекст и MVP;
- backend-код контроллеров и DTO - фактический API-контракт.

Если документы расходятся с backend-кодом, приоритет у фактического backend-контракта.

## Роль frontend

Frontend не собирает данные Polymarket самостоятельно. Он управляет backend и отображает состояние рынков, collector sessions, counters и ошибки.

Не подключаться из браузера напрямую к Polymarket WebSocket.

## Базовый стек

- React;
- TypeScript;
- Vite;
- TanStack Query;
- CSS Modules или обычный CSS.

Для тестов:

- Vitest;
- React Testing Library;
- MSW;
- Playwright только для критического smoke/e2e-сценария.

Zod допустим для runtime-проверки API-ответов, если это не усложняет MVP. React Router добавлять только при появлении нескольких реальных страниц; первая версия может быть одной dashboard-страницей.

## Архитектурные правила

- Backend - единственный источник истины для статусов и данных.
- Не выставлять `Running`, `Completed` или другие server statuses вручную после mutation.
- Server state хранить в TanStack Query.
- В локальном React state хранить только UI-состояние: введённый URL, выбранный market id, раскрытые панели, локальные флаги.
- Не дублировать server state в Context, localStorage или component state.
- Компоненты не должны выполнять HTTP напрямую.
- HTTP-вызовы размещать в API-слое и использовать через query/mutation hooks.
- Не использовать `any`; для неизвестного значения использовать `unknown` и проверку.
- Не менять смысл backend enum; в TypeScript-модели сохранять исходные значения backend.
- Не скрывать backend errors за общим сообщением.

## Проверка фактического API

Перед реализацией каждого запроса проверить текущие backend endpoints и DTO в коде.

Текущее состояние backend на момент создания этого файла:

- `POST /api/Market/register`;
- `POST /api/Collector/start`;
- `POST /api/Collector/stop`;
- ответы проходят через `Envelope`, а не через Problem Details;

Не выдумывать отсутствующие endpoints, DTO и поля. Если frontend требует read endpoints для списков, деталей или counters, сначала явно согласовать backend-задачу.

## Рекомендуемая структура

```text
PolymarketLab.Web/
  public/
  src/
    app/
      App.tsx
      providers.tsx
      queryClient.ts
    api/
      httpClient.ts
      envelope.ts
      marketsApi.ts
      collectorsApi.ts
    features/
      markets/
        components/
        hooks/
        model/
      collectors/
        components/
        hooks/
        model/
    pages/
      CollectorDashboardPage.tsx
    shared/
      components/
      formatters/
      styles/
      utils/
    main.tsx
    vite-env.d.ts
  .env.example
  index.html
  package.json
  tsconfig.json
  vite.config.ts
```

Не создавать абстракции заранее. Новый helper или слой должен решать уже существующую проблему.

## UI-правила MVP

- Первая версия - одна dashboard-страница.
- Форма добавления рынка: одно поле URL, submit по кнопке и Enter, disabled во время mutation, значение не очищать при ошибке, после успеха очистить.
- Список рынков: loading, empty, error, success states.
- Детали рынка: question, slug, condition id, dates, outcomes, token ids.
- Token id не должен ломать layout; добавить перенос, обрезку или copy-кнопку.
- Collector panel: status, createdAt, startedAt, stoppedAt, lastMessageAt, messagesReceived, messagesPersisted, reconnectCount, lastError.
- `unpersisted = received - persisted` считать производным отображаемым значением, не server state.
- Start недоступен без выбранного рынка, во время mutation и при активной session.
- Stop доступен только для рабочей session и блокируется после нажатия до ответа или изменения статуса.

## Ошибки

HTTP client должен:

1. проверить `response.ok`;
2. попытаться прочитать JSON;
3. распознать backend `Envelope` и возможный Problem Details;
4. сформировать безопасную fallback-ошибку для пустого или невалидного body;
5. не терять HTTP status.

Не показывать пользователю `[object Object]`, stack trace браузера или сырую HTML-страницу proxy.

## Форматирование данных

- Даты парсить как ISO 8601 и отображать в локальном времени пользователя.
- Исходные значения в query cache не изменять.
- `null` отображать как `-`.
- Counters форматировать через `Intl.NumberFormat`.
- Неизвестный статус отображать безопасно как `Unknown`, не падать при рендере.

## Accessibility и responsive

- Каждый input имеет label.
- Кнопки имеют понятные accessible names.
- Focus state видим.
- Статус не передаётся только цветом.
- Ошибки связаны с полем через `aria-describedby`, если применимо.
- Таблицы и token ids не ломают mobile viewport.
- Основной сценарий доступен с клавиатуры.

## Тестирование

Проверять новую логику подходящим уровнем тестов.

Минимальные unit tests:

- разбор backend errors/envelope;
- форматирование дат;
- форматирование counters;
- определение активного статуса;
- вычисление `unpersisted`.

Минимальные component tests:

- форма добавления рынка success/error;
- список рынков loading/empty/error/success;
- выбор рынка;
- disabled-состояния Start/Stop;
- отображение Failed и lastError;
- polling активной session.

## Порядок реализации

Работать маленькими этапами:

1. Каркас Vite React TypeScript.
2. QueryClientProvider и базовые styles.
3. Environment config и HTTP client.
4. API layer под фактический backend.
5. Markets read model.
6. Регистрация рынка.
7. Collector status и polling.
8. Start/Stop collector.
9. Тесты, responsive, accessibility, README.

После каждого этапа проект должен собираться.

## Запреты

Не делать без отдельного задания:

- менять backend;
- добавлять авторизацию;
- добавлять wallet connection;
- добавлять trading/order placement;
- добавлять Redux или другой global state manager на будущее;
- добавлять SignalR;
- добавлять frontend WebSocket к Polymarket;
- добавлять просмотр raw messages;
- делать большой refactoring вне текущей задачи;
- отключать TypeScript strict mode;
- игнорировать TypeScript errors;
- добавлять зависимости без объяснимой пользы.

## Проверки после изменений

Выполнять доступные команды из `package.json`:

```powershell
npm run lint
npm run typecheck
npm run test
npm run build
```

Если конкретной команды нет, не выдумывать результат. Указать, что именно было выполнено.

Также проверять:

```powershell
git diff --check
git diff
```

## Формат отчёта

После frontend-задачи кратко указать:

1. что реализовано;
2. какие файлы изменены;
3. какие архитектурные решения приняты;
4. какие проверки выполнены;
5. результаты build/tests/lint/typecheck;
6. какие ограничения или несоответствия API обнаружены;
7. что осталось следующим шагом.
