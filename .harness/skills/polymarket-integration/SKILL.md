---
name: polymarket-integration
description: Use whenever implementing, changing, debugging, or researching integration between PolymarketLab and Polymarket APIs or protocols, including Gamma, Data API, CLOB market data, WebSocket feeds, identifiers, events, markets, outcomes, tokens, prices, or external payload mapping. Also use when an external Polymarket contract may have changed. Do not use to place real trades unless the user explicitly requests a separately reviewed trading capability.
---

# Polymarket Integration

Treat Polymarket as an external system whose contracts may evolve.

Never implement an API contract from memory when current authoritative information can be checked.

## 1. Determine the Integration Surface

Classify the task before coding:

* Gamma API;
* Data API;
* CLOB read-only API;
* WebSocket;
* market/event metadata;
* order book;
* prices/history;
* positions;
* token/condition identifiers;
* another Polymarket surface.

State which external contract is involved.

Do not mix several APIs merely because they expose similar concepts.

## 2. Research Authoritative Sources

For contract-sensitive behavior, verify current documentation.

Preferred order:

1. Official Polymarket documentation.
2. Official `Polymarket/agent-skills`.
3. Official SDK/source examples.
4. Observed API responses.
5. Secondary sources only when necessary.

Record URLs/references used when the contract is non-obvious.

Check publication/update dates where available.

Do not assume an archived or old repository describes the current production API.

## 3. Separate External Models From the Domain

Maintain a strict anti-corruption boundary:

```text
Polymarket payload
      |
      v
Infrastructure transport DTO
      |
      v
mapper/parser
      |
      v
Domain/Application model
```

Do not deserialize Polymarket JSON directly into domain entities.

External fields may be:

* nullable;
* missing;
* renamed;
* unexpectedly typed;
* inconsistent across endpoints.

Domain models should represent PolymarketLab's own invariants, not every field returned by the API.

## 4. Be Precise About Identity

Polymarket exposes several identifiers that must not be treated as interchangeable.

Before mapping identifiers, explicitly establish what each one represents, for example:

* event slug/id;
* market slug/id;
* condition ID;
* token ID;
* outcome;
* CLOB-related identifiers.

Do not infer identity from a similarly named field.

If the mapping between event and market is ambiguous, stop the implementation path and model the ambiguity explicitly rather than choosing the first element silently.

In particular, do not assume:

```text
one event = one market
```

unless the verified contract and requested behavior guarantee it.

## 5. Handle Multi-Market Events Deliberately

When a Polymarket event can contain multiple markets:

1. Inspect the actual external representation.
2. Determine the user's intended selection semantics.
3. Define how the Domain represents event/market relationships.
4. Add tests covering more than one market.

Never silently choose the first market, first active market, or first token unless that behavior is an explicit product rule.

## 6. HTTP Client Boundaries

Keep network interaction in Infrastructure.

Prefer existing typed clients/gateways.

Do not create a second Gamma/CLOB client if the current one can be extended.

HTTP handling should distinguish at least:

* successful response;
* not found;
* non-success status;
* malformed payload;
* cancellation/timeout;
* transport failure.

Do not convert every failure into "market not found".

Preserve cancellation tokens through async boundaries where the existing architecture supports them.

## 7. Contract Fixtures

For every significant external payload shape, prefer deterministic local fixtures/stubs.

Tests must normally run without:

* live Polymarket network access;
* PostgreSQL;
* wallet;
* secret keys.

Use stub/fake `HttpMessageHandler` or the project's existing testing mechanism.

Add fixtures for meaningful edge cases, such as:

```text
normal market
missing optional field
multiple markets
unexpected empty collection
malformed identifier
non-success response
changed/null field
```

Avoid gigantic payload snapshots when a minimal representative fixture is sufficient.

## 8. Mapping Tests

Test transport-to-domain mapping separately from live network behavior.

Important mapping assertions may include:

* correct identifier selected;
* slug preserved;
* outcomes/tokens paired correctly;
* nullability handled;
* ordering assumptions avoided;
* invalid input rejected explicitly;
* external optional data does not violate domain invariants.

A mapper should not silently fabricate missing business data.

## 9. Research vs Production Behavior

Research code and production integration are different.

A temporary probe may call the live API to understand a contract.

Production tests should remain deterministic.

If you create a temporary diagnostic script:

* do not commit it unless useful;
* do not include secrets;
* remove it when finished if it has no durable value.

## 10. Read-Only by Default

This skill is intended primarily for data and integration work.

Do not automatically introduce:

* private keys;
* wallet signing;
* order placement;
* token approvals;
* USDC transfers;
* split/merge/redeem;
* bridge operations;
* gasless transaction execution.

Real trading capability requires an explicit user request and a separately reviewed design.

Never use real funds as part of normal verification.

## 11. Changes to Persistence

If external identifiers or new Polymarket concepts require persistence changes:

1. Model the domain meaning first.
2. Determine uniqueness semantics.
3. Update EF configuration.
4. Add migration when appropriate.
5. Add repository tests.

Do not add a unique index simply because an external field "looks unique"; verify its meaning.

Do not broaden existing `23505` handling to hide unrelated constraint failures.

## 12. Debugging External Failures

When an integration breaks:

```text
reproduce
-> identify exact boundary
-> capture status/payload shape safely
-> compare with expected contract
-> form one hypothesis
-> add regression fixture/test
-> fix
```

Use `systematic-debugging` for non-trivial failures.

Do not start by rewriting the parser.

## 13. Validation

Run the narrowest relevant tests first.

Then:

```powershell
dotnet test .\PolymarketLab.slnx
```

For changes affecting DI/EF/project wiring:

```powershell
dotnet build .\PolymarketLab.slnx
```

Live API verification may be used as an additional check when appropriate, but it must not replace deterministic tests.

## 14. Integration Review

Before completion verify:

* current authoritative API contract was checked when necessary;
* transport models remain outside Domain;
* identifiers were mapped deliberately;
* multi-market behavior is explicit;
* no secrets were introduced;
* tests run without live network;
* no duplicate gateway/client/parser was created;
* failure semantics are preserved;
* no real trading action was introduced accidentally.

## Completion Report

Report:

* Polymarket surface used;
* authoritative sources checked;
* external contract assumptions;
* mapping decisions;
* edge cases handled;
* production files changed;
* fixtures/tests added;
* commands executed;
* any API uncertainty that remains.
