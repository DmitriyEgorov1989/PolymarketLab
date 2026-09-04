---
name: analyzing-tasks
description: Use when a user asks to study, explain, assess, take, or plan a repository issue or task before implementation.
---

# Analyzing Tasks

## Overview

Turn an issue into an evidence-based implementation brief that a non-specialist can understand and an engineer can execute. The issue states intent; current code and contracts establish the actual starting point.

## Required Evidence

Read, in this order:

1. Repository and nearest scoped `AGENTS.md` files.
2. The issue body, all specification comments, linked issues, dependencies, and acceptance criteria.
3. Current domain and API documentation.
4. Actual implementation and tests. Controllers and DTOs override API prose when they disagree.
5. Existing worktree changes, without modifying or reverting unrelated work.

Record contradictions, already-delivered dependencies, and assumptions. Ask one focused question before planning only when an unresolved choice changes a public contract, module boundary, data migration, or observable behavior.

## Output Contract

Present these parts in order:

| Part | Required content |
|---|---|
| Purpose | The user problem and why the task exists, in plain language. |
| Scenarios | One successful, one waiting/in-progress, and one error scenario with concrete values and units. |
| Before / after | A comparison of observable behavior, not only internal architecture. |
| Confirmed baseline | Exact files, symbols, routes, statuses, and tests found in the repository. |
| Scope | What changes, what remains authoritative, and what is explicitly excluded. |
| Acceptance trace | Every acceptance criterion mapped to code and a test. |
| Implementation plan | Small ordered steps with exact paths, interfaces, representative before/after code, and comments explaining non-obvious decisions. |
| Verification | Narrow tests first, then relevant test, typecheck, build, documentation, and `git diff --check` commands. |
| Risks | Races, unknown values, stale data, compatibility, responsive/accessibility, and environment limits that actually apply. |

Code excerpts in a plan are proposed code, not proof that implementation already exists. Keep them minimal and consistent with repository conventions; do not invent endpoints, DTO fields, dependencies, or abstractions.

## Example

```text
Purpose: a future market must remain selectable before trading opens.
Success: Start at T-90s shows Scheduled / WaitingForPreparation.
Waiting: at T-30s the UI polls Starting every 2,000 ms.
Error: another market is Running, so Start is disabled; a race is still handled by backend HTTP 409.

Before: GET /api/Market?tradingNow=true hides the future market.
After: GET /api/Market returns all registered markets.
```

## Completion Gate

After presenting the brief and plan, stop before application-code implementation unless the user has explicitly approved implementation after seeing the plan. Creating the requested plan or task documentation is allowed before that approval.

## Common Mistakes

- Summarizing only the issue while ignoring comments, dependencies, or current code.
- Describing layers without showing what the operator sees before and after.
- Listing generic steps such as "add tests" without cases, paths, or expected outcomes.
- Treating frontend checks as authoritative when the backend protects a race.
- Starting implementation before the user has reviewed the requested plan.
