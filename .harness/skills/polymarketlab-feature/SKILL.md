---
name: polymarketlab-feature
description: Use when implementing or changing a PolymarketLab application feature that affects domain behavior, MediatR commands or queries, persistence, API behavior, or multiple architectural layers. Do not use for trivial formatting, documentation-only changes, or isolated infrastructure investigation.
---

# PolymarketLab Feature

Implement features by following the architectural boundaries already established in PolymarketLab.

Do not begin by writing code.

## 1. Establish Current Behavior

Read the relevant `AGENTS.md`, implementation, and tests first.

Identify:

* bounded context;
* domain aggregate/entity/value objects involved;
* current command/query handler;
* ports involved;
* infrastructure adapters involved;
* HTTP/API boundary, if any;
* existing tests covering the flow.

Do not infer behavior only from class names.

## 2. Define the Change

Before implementation, state briefly:

* current behavior;
* desired behavior;
* affected domain concepts;
* affected boundaries;
* observable acceptance criteria.

If the task introduces a new business concept or changes an invariant, use `domain-modeling` before implementation.

If multiple reasonable designs exist, prefer the smallest change consistent with the existing model.

## 3. Respect the Architecture

Follow this dependency direction:

```text
Presentation
    |
    v
Application
    |
    v
Domain / Ports
    ^
    |
Infrastructure adapters
```

### Domain

Put here:

* business invariants;
* entity behavior;
* value objects;
* state transitions;
* domain decisions independent of HTTP/EF/Polymarket API.

Do not put here:

* HTTP DTOs;
* JSON parsing;
* EF-specific behavior;
* Npgsql exceptions;
* Gamma/CLOB transport models.

### Application

Use MediatR handlers for orchestration.

Handlers may:

* coordinate domain objects;
* call ports;
* transform expected failures;
* decide application flow.

Handlers should not become containers for domain invariants.

### Ports

Use ports to represent dependencies required by application/domain-facing code.

Do not expose infrastructure implementation details through ports.

### Infrastructure

Put here:

* EF Core/Npgsql;
* repositories;
* external HTTP clients;
* Gamma/CLOB/Data API adapters;
* serialization;
* infrastructure-specific exception handling.

### Presentation

Keep controllers/endpoints thin.

Map:

```text
HTTP request
-> command/query
-> result
-> existing Envelope/error mapping
```

Do not reimplement business decisions in controllers.

## 4. Extend Existing Flows

Before adding a parser, gateway, repository, mapper, HTTP client, or service, search for the existing equivalent.

Prefer extending the established implementation.

Do not create a parallel flow simply because it is easier locally.

## 5. Preserve Important Markets Semantics

Unless the feature explicitly changes them:

* repeated registration of the same market remains successful;
* existing market returns the same ID with `Created = false`;
* newly inserted market returns `Created = true`;
* identity rules remain consistent with current DB constraints;
* aggregate loading must not silently return incomplete market/token state;
* unexpected DB failures must not be converted into expected business conflicts.

If a requested feature conflicts with these semantics, make that conflict explicit before changing them.

## 6. Use Factories and Result Types

Follow existing project conventions.

For entities/value objects with invariants:

```text
input
-> factory
-> validation/invariant enforcement
-> valid domain object
```

Do not bypass factories with public mutable state just to simplify mapping.

At domain/port boundaries use the project's established:

```text
Result<T, Error>
UnitResult<Error>
```

At command/controller boundaries follow the existing `ErrorList` conventions.

Do not introduce a second error abstraction unless required by a broader refactor.

## 7. Implement Test-First Where Practical

For behavioral changes:

1. Add or modify the narrowest failing test.
2. Confirm the test fails for the expected reason.
3. Implement the smallest production change.
4. Make the test pass.
5. Refactor without changing behavior.

Prioritize tests in this order:

```text
Domain test
-> handler/application test
-> infrastructure test
-> broader solution test
```

Do not use a large integration test if a deterministic unit/domain test captures the behavior.

## 8. Validation Sequence

During implementation run the narrowest relevant test first.

Examples:

```powershell
dotnet test .\PolymarketLab.Markets.Domain.Tests\PolymarketLab.Markets.Domain.Tests.csproj
```

or:

```powershell
dotnet test .\PolymarketLab.Markets.Infrastructure.Tests\PolymarketLab.Markets.Infrastructure.Tests.csproj
```

When stable, run:

```powershell
dotnet test .\PolymarketLab.slnx
```

If changing project references, host wiring, DI, EF model, or migrations, also run:

```powershell
dotnet build .\PolymarketLab.slnx
```

## 9. Database Changes

If the domain/persistence model changes, determine whether a migration is required.

Do not silently modify an old migration to represent a new schema change unless the repository's migration policy explicitly permits it.

Check:

* indexes;
* unique constraints;
* nullability;
* foreign keys;
* owned/value-object mappings;
* aggregate loading.

Keep PostgreSQL-specific handling inside Infrastructure.

## 10. Finish With a Feature Review

Before declaring completion, verify:

* acceptance criteria are covered;
* no domain rule leaked into Infrastructure/Presentation;
* no external DTO leaked into Domain;
* no duplicate implementation was introduced;
* tests exercise the important behavioral change;
* unrelated files were not modified;
* public/API behavior changes are intentional.

When appropriate, use `code-review`.

## Completion Report

Report:

* behavior implemented;
* architectural layers touched;
* files changed;
* domain decisions made;
* tests added/changed;
* commands executed;
* unresolved risks or follow-ups.
