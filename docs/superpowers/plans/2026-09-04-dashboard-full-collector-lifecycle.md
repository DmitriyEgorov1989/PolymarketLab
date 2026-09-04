# Dashboard Full Collector Lifecycle Implementation Plan

> **For agentic workers:** use `analyzing-tasks` to re-check the issue and current code before execution. Use the available `tdd` workflow task-by-task; do not install unavailable execution skills. Do not commit without explicit user permission.

**Goal:** расширить существующий React dashboard так, чтобы он показывал весь backend lifecycle сборщика, позволял заранее выбрать future market и безопасно управлял единственным глобальным collector slot.

**Architecture:** backend DTO и существующие HTTP routes остаются источником истины. Frontend синхронизирует TypeScript types с фактическим DTO, хранит server state только в TanStack Query, вычисляет лишь presentation state (countdown, labels, derived counters) и использует существующие by-market reads как раннюю подсказку о занятом глобальном slot; HTTP `409` остаётся окончательной защитой гонки.

**Tech Stack:** React 19, TypeScript strict mode, Vite, TanStack Query, Vitest, Testing Library.

**Spec:** GitHub issues `#36`, `#14`, `#21`, `#25`, `#27`; `docs/frontend-api-contract.md`; `docs/agent-context.md`.

## Простое Объяснение

Сейчас dashboard умеет запустить и остановить сборщик, но видит только короткую часть его жизни. Оператор не видит будущий рынок, ожидание старта, готовность обоих токенов, подтверждение результата тремя источниками, нормализацию и очистку ошибочного набора данных.

После задачи dashboard не будет сам решать, когда сборщику переходить в следующее состояние. Он будет точно показывать решение backend и не позволит по ошибке нажать Start для второго рынка, если уже известна активная session первого рынка.

### Успешный Сценарий

Рынок открывается в `12:00:00 UTC`. Пользователь нажимает Start в `11:58:30 UTC`, то есть за `90 секунд`. Backend возвращает `Scheduled / WaitingForPreparation`; UI показывает countdown `00:30` до подготовки в `11:59:00 UTC`, затем отображает `Starting`, readiness двух токенов, `Running`, resolution consensus и итоговый `Stopped`.

### Сценарий Ожидания

Session находится в `Stopping / AwaitingNormalization`. Из `1 250` raw messages обработано `1 240`, ещё `10` имеют status `Pending`; UI продолжает GET polling каждые `2 000 миллисекунд`, показывает effective deadline в локальном времени и не объявляет session завершённой.

### Ошибочный Сценарий

Пользователь подтверждает Stop до успешного завершения. UI отправляет существующий Stop request, показывает фактический ответ `Invalidating / Cleaning`, продолжает polling, затем показывает `Failed`, исходный `failureCode` и cleanup counts. UI не подменяет этот путь локальным `Stopped`.

## Как Было / Как Станет

| Область | Было | Станет |
|---|---|---|
| Markets | `GET /api/Market?tradingNow=true` скрывает future markets. | `GET /api/Market` возвращает все зарегистрированные рынки; future market остаётся selectable. |
| Status | Известны только `Starting`, `Running`, `Stopping`, `Stopped`, `Failed`, `Interrupted`. | Точно отображаются также `Scheduled`, `Invalidating` и nullable `phase`; неизвестные значения показываются как `Unknown`. |
| Polling | Poll выполняется для трёх статусов. | Poll выполняется для `Scheduled`, `Starting`, `Running`, `Stopping`, `Invalidating`; останавливается для всех terminal и unknown. |
| Readiness | Не видна. | Видны current `connectionEpoch` и initial book каждого snapshot token. |
| Continuity | Видны только received/persisted/reconnect/last message. | Видны historical received/enqueued/persisted, remaining raw rows, epoch, readiness timestamp и reconnect count. |
| Resolution | Не виден. | Видны latest observations WebSocket/Gamma/Clob, exact confirmation evidence, winner и timestamps. |
| Normalization | Не видна. | Видны raw/ledger/status counts и обработка resolution raw item. |
| Cleanup | Не виден. | Видны сохранённая причина failure, version и deleted raw/ledger/event counts. |
| Global slot | Проверяется только session выбранного market; конфликт узнаётся после POST. | Известная exclusive session любого registered market заранее блокирует Start; backend `409` закрывает race. |
| Stop | POST отправляется сразу. | Перед destructive Stop требуется подтверждение; затем показывается server-driven `Invalidating -> Failed`. |

## Подтверждённая База

- `PolymarketLab.Web/src/api/marketsApi.ts` жёстко добавляет `tradingNow=true`; backend `MarketController` уже принимает запрос без фильтра.
- `PolymarketLab.Web/src/api/collectorsApi.ts` содержит устаревший сокращённый DTO.
- Полный source-of-truth DTO находится в `PolymarketLab.DataCollection.Core/Application/UseCases/Common/CollectorSessionResponse.cs` и соседних response records.
- `collectorStatus.ts` одним predicate смешивает polling, exclusivity и возможность Stop.
- `useCollectorByIdQuery.ts` уже умеет dynamic polling и обновляет by-market cache, поэтому его нужно расширить, а не заменить.
- `CollectorPanel.tsx` показывает только status, три timestamps, базовые counters и failure.
- `CollectorControls.tsx` немедленно вызывает Stop и знает только session выбранного market.
- `docs/frontend-api-contract.md` описывает целевой DTO, но строка о Stop содержит устаревший code `collector.stop.requested_before_success`; фактический `StopCollectorErrors.RequestedBeforeSuccess` возвращает `collector.stop.requested`.

## Глобальные Ограничения

- Backend, routes, Envelope и orchestration не изменяются.
- Новые dependencies, router, global state manager, frontend WebSocket и Playwright не добавляются.
- Server state не копируется в React Context, localStorage или component state.
- Countdown является только отображением `effectiveDeadline`; он не создаёт status или phase.
- `null` отображается как `-`; timestamps переводятся в локальное время; counts форматируются через `Intl.NumberFormat`.
- Raw payload, credentials, stack traces и provenance в UI не добавляются.
- Application-code задачи `#36` не изменяется до отдельного одобрения этого плана.

## Трассировка Критериев Приёмки

| Критерий | Изменение | Доказательство |
|---|---|---|
| Future market selectable | убрать `tradingNow=true`, сохранить selection | API test и dashboard component test |
| Early Start показывает `Scheduled/WaitingForPreparation` | полный DTO, labels, pollable policy | `Scheduled -> Starting` test |
| Полный lifecycle evidence | новые focused presentation components | component tests для каждой slice |
| Poll пяти active statuses | отдельный `isPollableCollectorStatus` | table-driven model/hook tests |
| Terminal/unknown не poll | terminal policy и unknown fallback | тесты `Stopped`, `Failed`, `Interrupted`, arbitrary unknown |
| Другой market блокирует Start до POST | by-market query всех registered market IDs | отдельный dashboard/panel test, `startCollector` не вызван |
| Backend race protection сохраняется | mutation error не маскируется | HTTP `409` error component test |
| Destructive Stop подтверждается | native confirmation перед mutation | cancel/confirm tests |
| `Invalidating -> Failed` виден | Stop response cache + polling policy | fake-timer lifecycle test |
| Status не только цветом | буквальный status и phase text | semantic queries в component tests |
| Desktop/mobile работают | wrapping, responsive grids/tables | build и обязательный ручной acceptance checklist для заданных viewports |
| Retry и terminal coverage | сохранить query warning/retry | transient error recovery + table tests |

---

### Task 1: Синхронизировать Frontend DTO

**Files:**
- Modify: `PolymarketLab.Web/src/api/collectorsApi.ts`
- Modify: `PolymarketLab.Web/src/api/collectorsApi.test.ts`
- Modify: `PolymarketLab.Web/src/features/collectors/model/collectorSession.ts`
- Create: `PolymarketLab.Web/src/features/collectors/testing/createCollectorSession.ts`

**Interfaces:**
- Produces: exact TypeScript shapes for snapshot, readiness, normalization, resolution, cleanup and full `CollectorSessionResponse`.
- Produces: one reusable complete test fixture so later tests do not silently omit required fields.

- [ ] **Step 1: Расширить API test полным JSON response из фактического backend contract**

Зафиксировать, что GET и Stop возвращают один полный shape, а Start допускает `Scheduled`.

- [ ] **Step 2: Запустить test и получить ожидаемую ошибку типов/ожиданий**

Run: `npm --prefix .\PolymarketLab.Web run test -- src/api/collectorsApi.test.ts`

- [ ] **Step 3: Добавить точные interfaces**

Представительный будущий код:

```ts
export type CollectorSessionStatus =
  | 'Scheduled'
  | 'Starting'
  | 'Running'
  | 'Stopping'
  | 'Invalidating'
  | 'Stopped'
  | 'Failed'
  | 'Interrupted';

export interface CollectorSessionSnapshotResponse {
  externalEventId: string | null;
  eventSlug: string | null;
  externalMarketId: string | null;
  marketSlug: string | null;
  conditionId: string | null;
  eventStartsAt: string | null;
  eventEndsAt: string | null;
  projectionVersion: number | null;
  tokens: CollectorSessionTokenResponse[];
}

export interface CollectorSessionResponse {
  sessionId: string;
  marketId: string;
  snapshot: CollectorSessionSnapshotResponse;
  status: CollectorSessionStatus;
  phase: string | null;
  effectiveDeadline: string | null;
  createdAt: string;
  startedAt: string | null;
  subscriptionReadyAt: string | null;
  stoppedAt: string | null;
  invalidatingAt: string | null;
  stopReason: string | null;
  failureCode: string | null;
  failureMessage: string | null;
  readiness: CollectorReadinessResponse;
  messagesReceived: number;
  messagesEnqueued: number;
  messagesPersisted: number;
  remainingRawMessageCount: number;
  lastMessageAt: string | null;
  reconnectCount: number;
  normalization: CollectorNormalizationResponse | null;
  resolution: CollectorResolutionResponse;
  cleanup: CollectorCleanupResponse | null;
}
```

Добавить остальные nested interfaces поле-в-поле по C# records; source/status observation оставить `string`, чтобы UI мог безопасно показать будущие неизвестные backend values.

- [ ] **Step 4: Заменить неполные локальные `createSession()` на общий complete fixture**

- [ ] **Step 5: Запустить API test и typecheck**

Run: `npm --prefix .\PolymarketLab.Web run test -- src/api/collectorsApi.test.ts`

Run: `npm --prefix .\PolymarketLab.Web run typecheck`

---

### Task 2: Показать Future Registered Markets

**Files:**
- Modify: `PolymarketLab.Web/src/api/marketsApi.ts`
- Modify: `PolymarketLab.Web/src/api/marketsApi.test.ts`
- Modify: `PolymarketLab.Web/src/pages/CollectorDashboardPage.test.tsx`

**Interfaces:**
- Consumes: existing `GET /api/Market` route.
- Produces: all registered markets in the existing `useMarketsQuery` cache.

- [ ] **Step 1: Написать failing route test**

```ts
expect(fetchMock).toHaveBeenCalledWith('/api/Market', expect.anything());
```

- [ ] **Step 2: Изменить только path**

Было:

```ts
path: '/api/Market?tradingNow=true'
```

Станет:

```ts
path: '/api/Market'
```

- [ ] **Step 3: Добавить page test, где future market присутствует, выбирается и не исчезает после refetch**

- [ ] **Step 4: Запустить узкие tests**

Run: `npm --prefix .\PolymarketLab.Web run test -- src/api/marketsApi.test.ts src/pages/CollectorDashboardPage.test.tsx`

---

### Task 3: Разделить Lifecycle Policies И Countdown

**Files:**
- Modify: `PolymarketLab.Web/src/features/collectors/model/collectorStatus.ts`
- Modify: `PolymarketLab.Web/src/features/collectors/model/collectorStatus.test.ts`
- Create: `PolymarketLab.Web/src/features/collectors/model/collectorCountdown.ts`
- Create: `PolymarketLab.Web/src/features/collectors/model/collectorCountdown.test.ts`

**Interfaces:**
- Produces: `isPollableCollectorStatus`, `isExclusiveCollectorStatus`, `isStoppableCollectorStatus`, `getCollectorStatusLabel`, `getCollectorPhaseLabel`.
- Produces: `formatCollectorCountdown(deadline: string | null, nowMs: number): string`.

- [ ] **Step 1: Написать table-driven tests для пяти pollable/exclusive, stoppable, трёх terminal и unknown values**

```ts
it.each(['Scheduled', 'Starting', 'Running', 'Stopping', 'Invalidating'])(
  'polls %s',
  (status) => expect(isPollableCollectorStatus(status)).toBe(true),
);

it.each(['Stopped', 'Failed', 'Interrupted', 'FutureStatus'])(
  'does not poll %s',
  (status) => expect(isPollableCollectorStatus(status)).toBe(false),
);
```

- [ ] **Step 2: Реализовать независимые policies**

```ts
const POLLABLE_STATUSES = new Set([
  'Scheduled',
  'Starting',
  'Running',
  'Stopping',
  'Invalidating',
]);

export function isPollableCollectorStatus(status: string | null | undefined): boolean {
  return status !== null && status !== undefined && POLLABLE_STATUSES.has(status);
}
```

Не использовать один predicate одновременно для polling, Start и Stop: `Invalidating` занимает slot и poll-ится, но повторный destructive Stop не нужен.

- [ ] **Step 3: Написать countdown tests**

Проверить `null`, invalid timestamp, `65 секунд`, ровно deadline и прошедший deadline. После нуля возвращать `00:00`, но не синтезировать новый status.

- [ ] **Step 4: Реализовать pure formatter и запустить tests**

Run: `npm --prefix .\PolymarketLab.Web run test -- src/features/collectors/model/collectorStatus.test.ts src/features/collectors/model/collectorCountdown.test.ts`

---

### Task 4: Исправить Polling И Mutation Cache

**Files:**
- Modify: `PolymarketLab.Web/src/features/collectors/hooks/useCollectorByIdQuery.ts`
- Modify: `PolymarketLab.Web/src/features/collectors/hooks/useStopCollector.ts`
- Modify: `PolymarketLab.Web/src/features/collectors/hooks/collectorHooks.test.tsx`

**Interfaces:**
- Consumes: lifecycle predicates from Task 3.
- Produces: polling through all five nonterminal statuses and immediate cache of actual Stop response.

- [ ] **Step 1: Добавить failing transitions**

Проверить `Scheduled -> Starting`, `Stopping -> Invalidating -> Failed`, recovery после transient error и остановку polling отдельно для `Stopped`, `Failed`, `Interrupted`, unknown. Отдельный цельный panel test начинает с нажатия Start: mutation возвращает `Scheduled`, первый GET показывает `Scheduled / WaitingForPreparation`, следующий poll показывает `Starting / Connecting`.

- [ ] **Step 2: Заменить старый active predicate на pollable predicate в interval и cache protection**

```ts
return status === undefined || isPollableCollectorStatus(status)
  ? ACTIVE_COLLECTOR_POLL_INTERVAL_MS
  : false;
```

Первый read после Start продолжает retry-путь; фактический unknown status останавливает interval.

- [ ] **Step 3: На успешный Stop записать backend response до invalidation**

```ts
onSuccess: ({ session }) => {
  queryClient.setQueryData(collectorKeys.detail(session.sessionId), { session });
  queryClient.setQueryData(collectorKeys.byMarket(session.marketId), { session });
}
```

Это не ручная установка status: cache получает неизменённый фактический DTO backend.

- [ ] **Step 4: Запустить hook tests**

Run: `npm --prefix .\PolymarketLab.Web run test -- src/features/collectors/hooks/collectorHooks.test.tsx`

---

### Task 5: Обнаружить Global Exclusive Slot

**Files:**
- Create: `PolymarketLab.Web/src/features/collectors/hooks/useCollectorSlotsQuery.ts`
- Modify: `PolymarketLab.Web/src/features/collectors/hooks/collectorHooks.test.tsx`
- Modify: `PolymarketLab.Web/src/pages/CollectorDashboardPage.tsx`
- Modify: `PolymarketLab.Web/src/features/collectors/components/CollectorPanel.tsx`
- Modify: `PolymarketLab.Web/src/features/collectors/components/CollectorControls.tsx`

**Interfaces:**
- Consumes: `marketIds: string[]` and existing `GET /api/Collector/by-market/{marketId}`.
- Produces: `{ exclusiveSession, isPending, errors, retryFailed }` where `exclusiveSession` is a known five-status session, а ошибки и retry доступны управляющему UI.

- [ ] **Step 1: Написать failing hook test с двумя markets**

Market A возвращает `Running`, market B возвращает `null`; результат должен указать session A. После terminal response A slot освобождается. Отдельно проверить свободный slot, где все ответы содержат `session: null` и Start разрешён.

- [ ] **Step 2: Реализовать один `useQueries` aggregate поверх существующих query keys**

```ts
const queries = useQueries({
  queries: marketIds.map((marketId) => ({
    queryKey: collectorKeys.byMarket(marketId),
    queryFn: ({ signal }) => getCollectorByMarketId(marketId, signal),
    refetchInterval: (query) => query.state.data === undefined
      || isPollableCollectorStatus(query.state.data.session?.status)
      ? ACTIVE_COLLECTOR_POLL_INTERVAL_MS
      : false,
  })),
});
```

Выбирать только known exclusive status. Если часть initial reads ещё pending или завершилась ошибкой, не объявлять slot доказанно свободным: Start остаётся disabled, а UI показывает причину и retry.

- [ ] **Step 3: Передать список IDs из dashboard в panel и вычислить блокировку**

```tsx
<CollectorPanel
  marketId={selectedMarketId}
  registeredMarketIds={marketsQuery.data.map((market) => market.marketId)}
/>
```

- [ ] **Step 4: Сохранить backend race protection**

Добавить короткий комментарий только у решения о блокировке:

```ts
// This read improves the controls; the backend 409 remains authoritative for races.
const isBlockedByAnotherMarket = exclusiveSession !== null
  && exclusiveSession.marketId !== marketId;
```

- [ ] **Step 5: Добавить component test: session другого market блокирует Start и POST не вызывается**

Добавить test свободного slot, где Start доступен. Добавить test частично неуспешного discovery: Start заблокирован, видна ошибка, `retryFailed()` повторяет только ошибочные reads и разблокирует Start после успешного ответа.

Добавить отдельный test, где slot между read и click занят, backend возвращает HTTP `409`, а UI явно показывает status `409`, `collector.start.global_session_conflict` и исходное безопасное backend message. Для этого расширить error markup в `CollectorPanel.tsx`, используя `ApiError.status` и `ApiError.errors`, а не только агрегированное `.message`.

- [ ] **Step 6: Запустить hook/page/panel tests**

Run: `npm --prefix .\PolymarketLab.Web run test -- src/features/collectors/hooks/collectorHooks.test.tsx src/pages/CollectorDashboardPage.test.tsx src/features/collectors/components/CollectorPanel.test.tsx`

---

### Task 6: Подтвердить Destructive Stop

**Files:**
- Modify: `PolymarketLab.Web/src/features/collectors/components/CollectorControls.tsx`
- Modify: `PolymarketLab.Web/src/features/collectors/components/CollectorControls.test.tsx`
- Modify: `PolymarketLab.Web/src/features/collectors/components/CollectorPanel.test.tsx`

**Interfaces:**
- Consumes: `isStoppableCollectorStatus` and existing `onStop` callback.
- Produces: one explicit user confirmation before Stop mutation.

- [ ] **Step 1: Написать cancel/confirm tests**

Первый test проверяет, что отказ не вызывает `onStop`. Второй подтверждает ровно один вызов. Panel test проверяет последующий `Invalidating / Cleaning -> Failed`.

- [ ] **Step 2: Добавить минимальное native confirmation**

```tsx
function confirmStop() {
  if (window.confirm(
    'Stop invalidates this dataset and starts cleanup. Continue?',
  )) {
    onStop();
  }
}
```

Native confirm не требует dependency, доступен с клавиатуры и блокирует request до явного решения пользователя.

- [ ] **Step 3: Использовать отдельный stoppable predicate**

Разрешить Stop для `Scheduled`, `Starting`, `Running`, `Stopping`; запретить для `Invalidating`, terminal и unknown.

- [ ] **Step 4: Запустить controls и panel tests**

Run: `npm --prefix .\PolymarketLab.Web run test -- src/features/collectors/components/CollectorControls.test.tsx src/features/collectors/components/CollectorPanel.test.tsx`

---

### Task 7: Отобразить Полный Lifecycle Evidence

**Files:**
- Create: `PolymarketLab.Web/src/features/collectors/components/CollectorTimeline.tsx`
- Create: `PolymarketLab.Web/src/features/collectors/components/CollectorReadiness.tsx`
- Create: `PolymarketLab.Web/src/features/collectors/components/CollectorResolution.tsx`
- Create: `PolymarketLab.Web/src/features/collectors/components/CollectorNormalization.tsx`
- Create: `PolymarketLab.Web/src/features/collectors/components/CollectorCleanup.tsx`
- Modify: `PolymarketLab.Web/src/features/collectors/components/CollectorMetrics.tsx`
- Modify: `PolymarketLab.Web/src/features/collectors/components/CollectorPanel.tsx`
- Modify: `PolymarketLab.Web/src/features/collectors/components/CollectorPanel.css`
- Modify: matching `*.test.tsx` files, creating focused tests for new components where useful

**Interfaces:**
- Consumes: exact session slices from Task 1 and pure formatters.
- Produces: semantic sections that render `null` as `-` and never derive backend lifecycle decisions.

- [ ] **Step 1: Написать component tests для visible status/phase/countdown и nullable values**

Проверить `Scheduled / WaitingForPreparation`, локальный deadline, legacy `Interrupted` с nullable snapshot и unknown status/phase fallback.

- [ ] **Step 2: Добавить timeline с 1-second display tick**

```tsx
const [nowMs, setNowMs] = useState(() => Date.now());

useEffect(() => {
  const timer = window.setInterval(() => setNowMs(Date.now()), 1_000);
  return () => window.clearInterval(timer);
}, []);
```

Timer обновляет только countdown. Query polling остаётся отдельным интервалом `2 000 миллисекунд` и единственным источником status/phase.

- [ ] **Step 3: Расширить continuity metrics**

Показать `messagesReceived`, `messagesEnqueued`, `messagesPersisted`, derived unpersisted, `remainingRawMessageCount`, current epoch, `subscriptionReadyAt`, reconnect count и last message.

Комментарий в UI/markup должен различать исторические counters и remaining PostgreSQL rows после cleanup.

- [ ] **Step 4: Показать per-token readiness**

Сопоставить `snapshot.tokens` с `readiness.tokens` по `tokenId`; для отсутствующего current-epoch observation показать `-`. Не переносить readiness между epoch на клиенте.

- [ ] **Step 5: Показать resolution**

Отдельно отобразить latest `sourceStates` и immutable `confirmationSources`, winner, signal/confirmation/poll timestamps и безопасные source errors. Не считать latest state заменой confirmation evidence.

- [ ] **Step 6: Показать normalization и cleanup**

При `normalization === null` показать `-`, а при cleanup пояснить отсутствие remaining data. Показать все counts и `resolutionRawItemProcessed`; cleanup содержит version, failure и три deleted counts.

- [ ] **Step 7: Сохранить error/retry states**

Query warning не должен скрывать последний успешный session snapshot. Mutation errors должны сохранять HTTP status и доступные `errorCode`/message через существующий `ApiError`, не заменяя backend text.

- [ ] **Step 8: Сделать responsive layout**

Использовать существующие card/grid patterns; длинные IDs получают `overflow-wrap: anywhere`, таблицы не расширяют viewport меньше `320 пикселей`, focus остаётся видимым, status всегда имеет текст. Component tests проверяют семантику и наличие wrapping containers, но не объявляются доказательством layout без ручного viewport acceptance.

- [ ] **Step 9: Запустить focused component tests**

Run: `npm --prefix .\PolymarketLab.Web run test -- src/features/collectors/components`

---

### Task 8: Синхронизировать Документацию И Выполнить Полную Проверку

**Files:**
- Modify: `docs/frontend-context.md`
- Modify: `docs/frontend-api-contract.md`
- Modify: `PolymarketLab.Web/README.md`

**Interfaces:**
- Documents: all registered market listing, five-status polling, global-slot behavior, destructive Stop and full evidence.

- [ ] **Step 1: Удалить устаревшее требование `tradingNow=true` и описание сокращённого DTO**

- [ ] **Step 2: Исправить Stop failure code по фактическому backend**

Было в документации:

```text
collector.stop.requested_before_success
```

Станет:

```text
collector.stop.requested
```

- [ ] **Step 3: Запустить все frontend tests**

Run: `npm --prefix .\PolymarketLab.Web run test`

- [ ] **Step 4: Проверить TypeScript**

Run: `npm --prefix .\PolymarketLab.Web run typecheck`

- [ ] **Step 5: Собрать production bundle**

Run: `npm --prefix .\PolymarketLab.Web run build`

- [ ] **Step 6: Выполнить ручную проверку**

Записать результат обязательного ручного acceptance checklist: keyboard flow и viewports `320 пикселей`, `480 пикселей`, `760 пикселей` и desktop. На каждом viewport проверить отсутствие horizontal page overflow для длинных token IDs и source errors; отдельно проверить countdown и Stop confirmation.

- [ ] **Step 7: Проверить diff**

Run: `git diff --check`

Проверить, что backend/migrations, generated `dist`, `node_modules`, secrets и несвязанные пользовательские изменения не вошли в реализацию.

## Риски И Решения

| Риск | Решение |
|---|---|
| By-market reads устарели между проверкой и Start | не объявлять frontend авторитетным; показывать backend HTTP `409`. |
| Один by-market read завершился ошибкой | не считать global slot свободным, показать retry. |
| Browser clock отличается от server clock | countdown только информационный; backend status/deadline остаётся решением. |
| Unknown status появился после обновления backend | показать `Unknown`, прекратить polling, не скрывать raw безопасное значение в diagnostics. |
| Historical counters после cleanup выглядят как оставшиеся данные | явно отделить их от `remainingRawMessageCount` и cleanup audit. |
| Большие token/condition IDs ломают mobile | wrapping и responsive cards/tables, ручная проверка от `320 пикселей`. |
| Полный DTO раздувает test setup | один typed fixture с overrides, без production abstraction. |

## Граница Реализации

Этот документ является планом, а не реализацией issue `#36`. Следующий шаг после просмотра пользователем: получить явное одобрение и выполнить Task 1 по TDD, затем двигаться по задачам с узкими проверками.
