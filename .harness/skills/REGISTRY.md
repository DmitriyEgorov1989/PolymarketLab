# Skill Registry

Search this registry on demand; do not load every skill into every session. Project rules in root and scoped `AGENTS.md` take priority over procedural skills.

| Skill | Canonical path | When to use |
|---|---|---|
| `brainstorming` | `.harness/skills/brainstorming` | Clarify ambiguous product behavior or compare approaches before implementation; do not create a spec artifact unless requested. |
| `code-review` | `.harness/skills/code-review` | Review a fixed diff against repository standards and the user's request or available specification. |
| `codebase-design` | `.harness/skills/codebase-design` | Evaluate an interface or test seam while preserving the repository's established module boundaries and terminology. |
| `domain-modeling` | `.harness/skills/domain-modeling` | Resolve domain language or invariants using `docs/agent-context.md`, current domain docs, and code as the knowledge source. |
| `research` | `.harness/skills/research` | Check changing facts against primary sources; create a repository artifact only when explicitly requested. |
| `systematic-debugging` | `.harness/skills/systematic-debugging` | Diagnose a reproducible failure or regression from evidence before proposing a fix, using project PowerShell and test commands. |
| `tdd` | `.harness/skills/tdd` | Implement behavior test-first at an established public seam using a narrow red-green loop. |
| `writing-plans` | `.harness/skills/writing-plans` | Plan multi-module work with several substantive stages, or when explicitly requested; keep it in the response unless a file is requested. |
| `writing-skills` | `.harness/skills/writing-skills` | Create, edit, or verify a project skill under the repository's explicit provenance and permission rules. |
