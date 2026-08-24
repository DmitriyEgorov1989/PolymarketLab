# PolymarketLab Agent Contract

## Проект

PolymarketLab регистрирует рынки Polymarket, собирает исходные WebSocket-сообщения в PostgreSQL и строит нормализованные проекции. Backend является источником истины; frontend управляет им через HTTP API.

Стек: .NET 10, ASP.NET Core, MediatR, FluentValidation, EF Core, PostgreSQL, React 19, TypeScript, Vite, TanStack Query, xUnit и Vitest.

## Карта репозитория

- `PolymarketLab.Api` - единственный executable host.
- `PolymarketLab.Markets.*` - регистрация и чтение рынков.
- `PolymarketLab.DataCollection.*` - collector runtime, raw ingestion и проекции.
- `PolymarketLab.Framework`, `PolymarketLab.SharedKernel` - общие HTTP и domain primitives.
- `PolymarketLab.Web` - React dashboard; действуют также `PolymarketLab.Web/AGENTS.md`.
- `docs` - контракты и проектный контекст.
- `observability` - Prometheus, Grafana, Loki и Alloy.

Архитектурные инварианты и ссылки на scoped-документы находятся в `docs/agent-context.md`. Перед изменением соответствующего модуля прочитай этот документ и ближайший `AGENTS.md`.

## Команды

Setup из корня:

```powershell
dotnet restore .\PolymarketLab.slnx
npm ci --prefix .\PolymarketLab.Web
.\.harness\setup.ps1
```

Локальный запуск:

```powershell
docker compose up -d postgres
dotnet run --project .\PolymarketLab.Api\PolymarketLab.Api.csproj --launch-profile http
npm --prefix .\PolymarketLab.Web run dev
```

Проверки:

```powershell
dotnet test .\PolymarketLab.slnx
dotnet build .\PolymarketLab.slnx
npm --prefix .\PolymarketLab.Web run test
npm --prefix .\PolymarketLab.Web run typecheck
npm --prefix .\PolymarketLab.Web run build
git diff --check
```

Сначала запускай самый узкий подходящий тест, затем расширяй проверку. Полные .NET integration tests требуют доступный Docker daemon. Отдельная lint-команда в репозитории не настроена.

## Рабочие правила

- Отвечай по-русски, если пользователь не попросил другой язык.
- Все объяснения, планы, вопросы, промежуточные отчёты, результаты ревью и финальные ответы должны быть на русском языке.
- Исходный код, имена классов, методов, переменных, файлов, директорий, команды, конфигурационные ключи, названия API и другие технические идентификаторы оставляй на языке проекта.
- Не переводи сообщения об ошибках, логи и цитаты из внешней документации. При необходимости объясняй их смысл на русском языке.
- Комментарии в коде пиши в соответствии с уже существующими соглашениями проекта. Не переводи существующие комментарии без явной просьбы пользователя.
- Не отвечай на английском языке, если пользователь явно не попросил об этом.
- Сохраняй существующую архитектуру и делай минимальные изменения в рамках задачи.
- Фактический backend-код контроллеров и DTO имеет приоритет над документацией API.
- Для C# моделей и интерфейсов добавляй содержательные XML-комментарии к типам и членам, включая семантику `null`.
- Ожидаемые ошибки не превращай в исключения и не скрывай исходный код или сообщение integration error.
- Не изменяй и не удаляй чужие незавершённые изменения.
- Не добавляй credentials, токены, connection strings или полные raw payload в код, документы, команды и отчёты.

Можно самостоятельно менять код, тесты и документацию в пределах поставленной задачи и запускать локальные проверки. Сначала спроси разрешение на:

- изменение публичного HTTP-контракта или границ модулей;
- создание или применение EF migration;
- обновление версий зависимостей;
- добавление MCP, hooks, CI, runtime permissions или credentials;
- destructive Docker, database или Git-операции;
- commit, push, rebase, создание branch или PR.

Не изменяй вручную migration snapshots, сгенерированные артефакты, файлы под `bin`, `obj`, `node_modules`, `dist` и byte-sensitive fixtures в `PolymarketLab.DataCollection.Infrastructure.Tests/Fixtures/Polymarket`.

## Definition Of Done

- Поведение покрыто тестом на подходящем уровне либо явно объяснено, почему тест не нужен.
- Выполнены релевантные test, typecheck и build-команды; ограничения окружения указаны.
- `git diff --check` проходит, а итоговый diff не содержит несвязанных изменений и секретов.
- Документация и API-контракты обновлены, если поведение изменилось.
- Финальный отчёт содержит причины, изменённые файлы и результаты проверок.

## Project Skills

Канонические project-local skills находятся в `.harness/skills`, их происхождение закреплено в `.harness/harness.lock`. OpenCode обнаруживает их через локальную `.agents/skills` junction, создаваемую явной командой `.\.harness\setup.ps1`. После clone или перемещения репозитория выполни setup и `.\.harness\health.ps1`.

Project contract всегда выше procedural skill. Действуют следующие overrides:

- `superpowers:test-driven-development` соответствует local `tdd`, а `superpowers:verification-before-completion` - Definition of Done и командам проекта.
- Отсутствующие `using-git-worktrees`, `subagent-driven-development`, `executing-plans` и прочие capabilities не устанавливай автоматически; используй доступные runtime-возможности и repo contract.
- `brainstorming` используй для крупных или неоднозначных capabilities, не как обязательную церемонию перед bounded-задачей; `writing-plans` - для многомодульной работы с несколькими существенными этапами или по прямому запросу.
- `brainstorming`, `domain-modeling`, `writing-plans` и `research` не создают постоянные docs, `CONTEXT`, ADR или plans и не commit-ят без явного запроса; для domain knowledge используй `docs/agent-context.md` и текущие domain docs.
- `codebase-design` не переопределяет established DDD/.NET/React terminology или module boundaries.
- `code-review` не требует issue tracker: specification может быть запросом пользователя, текущим diff или существующим документом проекта.
- `systematic-debugging` не показывает secrets или raw payload, использует PowerShell и test-команды проекта; POSIX helper опционален.
- `writing-skills` не разрешает удалять user changes, commit/push или создавать external artifacts.
