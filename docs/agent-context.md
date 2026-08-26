# Agent Context

Этот документ хранит project-specific знания, которые слишком подробны для operational contract в `AGENTS.md`. Код остаётся источником истины; при расхождении сначала проверь текущую реализацию.

## Структура И Wiring

- Единственный executable host - `PolymarketLab.Api/Program.cs`. Папки `/src/...` в `PolymarketLab.slnx` виртуальные; физической директории `src` нет.
- Все .NET проекты используют `net10.0`. `global.json`, NuGet lock и repo-local tool manifest отсутствуют. Frontend имеет `package-lock.json`, поэтому setup использует `npm ci`.
- `PolymarketLab.Markets.Core` содержит Domain, Application и Ports. Application DI сканирует MediatR handlers и FluentValidation validators; общий validation pipeline возвращает ожидаемые ошибки как `ErrorList`.
- `Program.cs` подключает Markets и DataCollection Application, Infrastructure и Presentation. Не меняй порядок hosted services и DI-регистраций collector runtime без проверки lifecycle.
- Framework возвращает `Envelope`, включая model binding, unknown routes и неожиданные ошибки. Exception handler скрывает exception details; Problem Details не является основным контрактом.

## Markets

- Инварианты находятся в Domain, orchestration - в MediatR handlers, persistence и внешние интеграции - в Infrastructure.
- Value objects и entities с инвариантами создаются через фабрики; приватные пустые конструкторы существуют для EF Core.
- Ports/domain возвращают `Result<T, Error>` или `UnitResult<Error>`; command/controller boundary использует `Result<T, ErrorList>`.
- Повторная регистрация того же рынка успешна: тот же ID и `Created = false`. Новая запись возвращает `Created = true`.
- Расширяй существующий parser/gateway/repository flow, не создавай параллельную реализацию.
- Identity рынка защищают отдельные constraints для `slug`, `external_market_id` и `condition_id`. Только их PostgreSQL `23505` преобразуется в `MarketInsertStatus.UniqueConflict`; token conflicts и остальные database errors не маскируются.
- Repository queries используют `AsNoTracking()` и загружают `Tokens`. Aggregate нельзя возвращать частично материализованным.
- Регистрация и запуск collector требуют live-проверки Gamma. Внешние даты являются метаданными и не заменяют status flags Gamma.

Фактический frontend HTTP-контракт описан в controllers/DTO и отражён в `docs/frontend-api-contract.md`.

## Data Collection

- `CollectorController` публикует read/start/stop endpoints. Infrastructure регистрирует `DataCollectionDbContext`, repositories, singleton collector runtime и bounded raw-message ingestion worker.
- WebSocket collector принимает text messages, собирает fragments и передаёт полные исходные UTF-8 bytes в bounded ingestion pipeline. Silent drop недопустим.
- При старте активные сессии предыдущего процесса переводятся в `Interrupted/ProcessTerminated`. При штатной остановке текущие сессии проходят `Stopping -> Stopped/ApplicationShutdown`.
- Переходы `CollectorSession` сохраняются compare-and-set по ожидаемому `Status`; `status` является EF concurrency token. При конфликте перечитай состояние и разреши переход, не выполняй unconditional update.
- Автономная ошибка collector переводит сохранённую активную session в `Failed`; ошибка сохранения этого перехода останавливает приложение.
- Startup reconciliation рассчитан на один экземпляр приложения. Multi-instance ownership, reconnect и automatic resume не реализованы.
- До изменения runtime, ingestion, ownership буферов или hosted-service ordering прочитай `PolymarketLab.DataCollection.Infrastructure/Adapters/CollectorRuntime/README.md`.
- До изменения normalizer прочитай `docs/normalizer-input-contract.md` и root `README.md`. Raw payload не логируй и не включай в agent context без необходимости.
- Fixtures в `PolymarketLab.DataCollection.Infrastructure.Tests/Fixtures/Polymarket` имеют зафиксированные SHA-256 и помечены `-text`; не форматируй их и не меняй line endings.

## Локальное Окружение

- Connection string задаётся через API User Secrets или `Database__ConnectionString`; значения нет в `appsettings`.
- PostgreSQL из Compose опубликован на host port `5433`. Перед сменой порта проверь занятость.
- Приложение не вызывает `Migrate()` или `EnsureCreated()` автоматически. Миграции применяются отдельно для `MarketsDbContext` и `DataCollectionDbContext` командами из root `README.md`.
- HTTP profile: `http://localhost:5285`. Swagger `/swagger` и OpenAPI `/openapi/v1.json` доступны только в Development.
- Полные integration tests используют `Testcontainers.PostgreSql` и требуют Docker. Gamma tests используют stub `HttpMessageHandler`.

## Известные Ограничения

- Mapping новых `ErrorType` нужно добавлять в `ResponseExtensions`, иначе неизвестный тип может стать HTTP 500.
- Миграции не применяются автоматически; отдельного migration-application или полного API end-to-end test suite нет.
- Первый registration flow принимает только single-market events; multi-market
  events отклоняются без неявного выбора дочернего market.
- Reconnect collector, multi-instance session ownership и automatic resume отсутствуют.
