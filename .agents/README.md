# OpenCode Skill Discovery

`.agents/skills` создаётся локально как Windows junction на канонический каталог `.harness/skills`:

```powershell
.\.harness\setup.ps1
```

Junction намеренно не хранится в Git: репозиторий использует `core.symlinks=false`, а absolute junction target зависит от расположения clone. После clone повторно запусти setup и health. После перемещения сначала удали только stale junction командой `Remove-Item -LiteralPath .\.agents\skills`, затем повтори setup и health.
