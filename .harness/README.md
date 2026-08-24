# Project Agent Harness

## Назначение

`.harness/skills` является единственной физической копией одиннадцати project-local skills: `brainstorming`, `code-review`, `codebase-design`, `domain-modeling`, `polymarket-integration`, `polymarketlab-feature`, `research`, `systematic-debugging`, `tdd`, `writing-plans` и `writing-skills`. Runtime-specific MCP, credentials, permissions, hooks и model settings harness не управляет.

Snapshot адаптирует принципы `agent-harness`, но не является совместимым installation target его CLI: набор собирается из нескольких pinned sources, а upstream health требует symlink, ненадёжный при текущем Windows `core.symlinks=false`.

## Setup И Health

После clone или перемещения рабочей директории выполни из корня:

```powershell
.\.harness\setup.ps1
.\.harness\health.ps1
```

Setup создаёт игнорируемую Git Windows junction `.agents/skills` на `.harness/skills`. Команда ничего не скачивает и отказывается заменять существующий обычный каталог или junction с другим target.

После перемещения уже настроенной рабочей директории junction может сохранить старый absolute target. Setup безопасно остановится. Удали только stale junction командой `Remove-Item -LiteralPath .\.agents\skills` и повторно запусти setup; canonical `.harness/skills` эта операция не удаляет.

Health проверяет pinned repositories, revisions и licenses всех sources, SHA-256 управляемых файлов, полный inventory, manifests, registry, placeholders и discovery junction. Он не доказывает, что конкретная версия OpenCode активировала skill; это проверяется в новой agent session.

## Provenance

- Harness approach: `https://github.com/KirillSachkov/agent-harness.git` at exact commit `5ab9b5d44c57bfa042e2f62730af95c0e9ab7dc4`, package version `1.2.0`.
- Matt Pocock skills: byte-for-byte snapshot from `https://github.com/mattpocock/skills.git` at exact commit `9c9f36ccd3995266cd675468af71639c8dde1ec5`, vendored through the pinned harness revision: `code-review`, `codebase-design`, `domain-modeling`, `research` and `tdd`.
- Superpowers skills: byte-for-byte direct snapshot from `https://github.com/obra/superpowers.git` at exact commit `b36e0829c6d0140e93cfef2ca599b1b07d4a7797`: `brainstorming`, `systematic-debugging`, `writing-plans` and `writing-skills`.
- Project-authored skills: `polymarket-integration` and `polymarketlab-feature`; their provenance is recorded as `project-local` and no upstream source or license is attributed to them.
- `writing-skills` выбран вместо Anthropic `skill-creator`; `skill-creator` не vendored.
- Selected source paths and every managed file hash are recorded in `harness.lock`.
- Licenses are preserved under `.harness/licenses`.

## Explicit Update

Skills не обновляются при setup, health или старте agent session. Обновление является отдельным dependency change:

1. Получи каждый source repository (`agent-harness` и прямой `superpowers`) в отдельную временную директорию и checkout конкретных commits, не плавающий `main`.
2. Проверь multi-source provenance: harness revision и vendored Matt Pocock revision, direct Superpowers revision, licenses и verify workflow.
3. Сравни каждый выбранный source directory с `.harness/skills/<name>`. Для Matt Pocock используй paths из exact harness checkout; для Superpowers - direct exact checkout.
4. Если локальный файл отличается от текущего `harness.lock`, остановись и отдельно реши, сохранять ли локальную модификацию. Не применяй force replacement.
5. Замени только явно одобренные snapshots, обнови revisions, source paths, licenses и SHA-256 в `harness.lock`, затем обнови `REGISTRY.md`.
6. Выполни `.\.harness\health.ps1`, релевантные проверки, `git diff --check` и review полного diff.

Новые project-authored skills можно добавлять только с явным назначением. Зафиксируй их в lock как `project-local`, не приписывая им upstream provenance.
