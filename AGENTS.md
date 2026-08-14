# AGENTS.md

## Общение

- Отвечай на русском языке, если пользователь явно не попросил другой язык.
- В общении с пользователем не смешивай русские и английские слова без необходимости; английские обозначения оставляй только для имён переменных, классов, методов, файлов и других элементов кода.
- Пиши комментарии понятным русским языком и не чередуй русские и английские слова без необходимости; английские обозначения оставляй только для имён типов, методов, свойств, статусов и других элементов кода.
- В финале кратко укажи изменения и причины, затронутые файлы, выполненные тесты и сборки.

## Структура и wiring

- Единственный executable host — `PolymarketLab.Api/Program.cs`; папки `/src/...` в `PolymarketLab.slnx` виртуальные, физической `src` нет.
- `PolymarketLab.Markets.Core/PolymarketLab.Markets.Core.csproj` содержит Domain, Application и Ports; имя папки, сборки и root namespace — `PolymarketLab.Markets.Core`.
- `Program.cs` подключает Markets Application, Infrastructure и controllers из Presentation. Application DI сканирует MediatR handlers и FluentValidation validators; общий validation pipeline возвращает ожидаемые ошибки как `ErrorList`. Infrastructure DI регистрирует Npgsql context, repository и Gamma typed client.
- DataCollection Application, Infrastructure и Presentation подключены к host. `CollectorController` публикует read/start/stop endpoints; Infrastructure регистрирует `DataCollectionDbContext`, repositories, singleton collector runtime и bounded raw-message ingestion worker. При запуске активные сессии предыдущего процесса переводятся в `Interrupted/ProcessTerminated`; при штатной остановке текущие сессии проходят `Stopping -> Stopped/ApplicationShutdown`. WebSocket collector принимает text messages, собирает fragments и сохраняет исходные UTF-8 bytes batch-ами.
- Все проекты используют `net10.0`; `global.json`, package lock и repo-local tool manifest отсутствуют.

## Проверка

Запускай команды из корня репозитория:

```powershell
dotnet build .\PolymarketLab.slnx
dotnet test .\PolymarketLab.slnx
dotnet test .\PolymarketLab.Markets.Domain.Tests\PolymarketLab.Markets.Domain.Tests.csproj
dotnet test .\PolymarketLab.Markets.Infrastructure.Tests\PolymarketLab.Markets.Infrastructure.Tests.csproj
dotnet test .\PolymarketLab.Markets.Domain.Tests\PolymarketLab.Markets.Domain.Tests.csproj --filter "FullyQualifiedName~PolymarketUrlExtensionsTests"
dotnet test .\PolymarketLab.Markets.Domain.Tests\PolymarketLab.Markets.Domain.Tests.csproj --filter "FullyQualifiedName~RegisterMarketHandlerTests"
dotnet test .\PolymarketLab.Markets.Infrastructure.Tests\PolymarketLab.Markets.Infrastructure.Tests.csproj --filter "FullyQualifiedName~MarketRepositoryTests"
```

- Во время разработки сначала запускай самый узкий тест, затем `dotnet test .\PolymarketLab.slnx`; при изменении project references, EF model или host wiring также запускай solution build.
- PostgreSQL integration tests используют `Testcontainers.PostgreSql`, сами запускают изолированный контейнер и не требуют локально настроенной БД. Для полного test suite нужен доступный Docker daemon; repository unit tests используют EF InMemory, model tests только строят Npgsql metadata, Gamma tests используют stub `HttpMessageHandler`.
- Отдельных migration-application и API end-to-end тестов пока нет.

## Локальный запуск

1. Задай `Database:ConnectionString` через API User Secrets или `Database__ConnectionString`; значения нет в `appsettings`.
2. Запусти PostgreSQL: `docker compose up -d postgres`; проверь `docker compose ps`.
3. Примени миграции командой ниже: приложение не вызывает `Migrate()` или `EnsureCreated()` автоматически.
4. Запусти API: `dotnet run --project .\PolymarketLab.Api\PolymarketLab.Api.csproj --launch-profile http`.

- Compose публикует PostgreSQL на host port `5433` (container port `5432`) и хранит данные в named volume; не меняй на `5432` без проверки занятости порта.
- HTTP profile: `http://localhost:5285`. Только в Development доступны Swagger `/swagger` и OpenAPI `/openapi/v1.json`.
- Endpoint регистрации: `POST /api/Market`, body: `{ "marketUri": "https://polymarket.com/event/<slug>" }`.
- Регистрация требует доступных PostgreSQL и Gamma API. Parser извлекает event slug, а gateway вызывает `/markets/slug/{slug}`; соответствие multi-market events пока не решено.

## EF Core и миграции

```powershell
dotnet ef migrations add <MigrationName> --project .\PolymarketLab.Markets.Infrastructure\PolymarketLab.Markets.Infrastructure.csproj --startup-project .\PolymarketLab.Api\PolymarketLab.Api.csproj --context MarketsDbContext --output-dir Adapters\Postgres\Migrations -- --environment Development
dotnet ef database update --project .\PolymarketLab.Markets.Infrastructure\PolymarketLab.Markets.Infrastructure.csproj --startup-project .\PolymarketLab.Api\PolymarketLab.Api.csproj --context MarketsDbContext -- --environment Development
dotnet ef migrations add <MigrationName> --project .\PolymarketLab.DataCollection.Infrastructure\PolymarketLab.DataCollection.Infrastructure.csproj --startup-project .\PolymarketLab.Api\PolymarketLab.Api.csproj --context DataCollectionDbContext --output-dir Adapters\Postgres\Migrations -- --environment Development
dotnet ef database update --project .\PolymarketLab.DataCollection.Infrastructure\PolymarketLab.DataCollection.Infrastructure.csproj --startup-project .\PolymarketLab.Api\PolymarketLab.Api.csproj --context DataCollectionDbContext -- --environment Development
```

- Миграции и snapshots находятся в `PolymarketLab.Markets.Infrastructure/Adapters/Postgres/Migrations` и `PolymarketLab.DataCollection.Infrastructure/Adapters/Postgres/Migrations`; `dotnet-ef` не закреплён manifest-файлом.
- Уникальность identity рынка обеспечивают отдельные constraints для `slug`, `external_market_id`, `condition_id`. Только их PostgreSQL `23505` repository преобразует в `MarketInsertStatus.UniqueConflict`; token conflicts и прочие DB errors не маскируй.
- Repository queries используют `AsNoTracking()` и загружают `Tokens`; aggregate не должен возвращаться частично материализованным.
- Переходы `CollectorSession` сохраняются через compare-and-set по ожидаемому `Status`; `status` является EF concurrency token. Конфликт нужно перечитать и разрешить, не выполняй безусловный update aggregate.

## Соглашения Markets

- Инварианты держи в Domain, orchestration — в MediatR handler, внешние и persistence детали — в Infrastructure.
- Value objects и entities с инвариантами создавай через фабрики; приватные пустые конструкторы нужны EF Core.
- На ports/domain уровне ожидаемые ошибки возвращаются как `Result<T, Error>`/`UnitResult<Error>`; command/controller boundary использует `Result<T, ErrorList>`.
- Повторная регистрация того же рынка — success с тем же ID и `Created = false`; новая запись возвращает `Created = true`.
- Расширяй существующий flow, не добавляй параллельные parser/gateway/repository реализации.

## Известные пробелы

- Framework возвращает `Envelope`, не raw response DTO. HTTP mapping ошибок неполный: новые `ErrorType` могут уйти в 500, пока не обновлён `ResponseExtensions`.
- Нет exception-handler/problem-details middleware и автоматического применения миграций.
- Автономная ошибка сборщика переводит сохранённую активную сессию в `Failed` через обработчик прикладного слоя; ошибка этой записи останавливает приложение.
- Согласование сессий при запуске рассчитано на один экземпляр приложения и прекращает запуск при ошибке PostgreSQL. Владение сессией несколькими экземплярами, повторное подключение и автоматическое возобновление сбора пока не реализованы.
