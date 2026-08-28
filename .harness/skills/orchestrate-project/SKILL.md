---
name: orchestrate-project
description: Use when the user asks an agent to coordinate a project, asks what to do next or parallelize, or wants an audit of a project's delivery pipeline, task hierarchy, blockers, reviews, and agent handoffs. Do not use for executing one already-scoped implementation task.
---

# Project Orchestrator

Act as the owner-facing coordinator for the current project. A session is a control surface, not project memory: reconstruct the current state from authoritative project sources whenever this skill starts.

## Attach To The Project

Locate the current project root and read its root agent instructions. Follow pointers for the tracker, workflow, domain, repositories, verification, and owner gates. Complete this step when you can name the project's sources of truth and the operations that require approval.

Discover the live work system described by the project: tracker items and hierarchy, dependencies, claims or assignees, pull or merge requests, reviews, CI, releases, and relevant Git worktrees. Use only the providers and concepts the project actually configures. Complete this step when every active workstream you report has a current source or is marked unknown.

Reconcile the views. Surface stale status, missing parent or dependency links, conflicting readiness, abandoned claims, finished work that was not closed, and decisions that exist only in conversation. Apply the project's precedence rules; distinguish confirmed facts, inferences, and owner decisions.

If the directory has no project contract or tracker, inspect its README and repository state, explain the missing coordination surface, and provide a minimal recommendation. Create or install a harness only when the user asks.

## Coordinate

Return the smallest useful control brief:

1. Current stage and outcome.
2. Meaningful changes and pipeline drift.
3. Blockers and the exact unblocking action.
4. Owner decisions and merge or release gates.
5. Work ready now, split into independent session-sized lanes.
6. The single next coordination action.

For each proposed lane, give its tracker link or concrete outcome, why it is unblocked, who should own it, any matching project skill, dependencies, and a verifiable stopping condition. Prefer one meaningful outcome per session. Use subagents only for bounded independent checks when the runtime and current authorization allow delegation; keep one writing owner per branch or worktree under the project's rules.

Keep the coordinator session focused on state, decisions, sequencing, and small tracker hygiene. Route context-heavy research, prototyping, specifications, implementation, and reviews to separate sessions so the coordinator can be replaced cheaply.

## Mutations

The default invocation is a read-only audit and recommendation. When the user asks to synchronize, organize, configure, create, close, or merge, perform only the requested mutations and verify the result against the project workflow.

Mechanical tracker hygiene may correct fields and links whose intended value is already explicit. Creating work requires a clear outcome, scope, acceptance criteria, blockers, and owner decisions. Follow every project owner gate. In the absence of a project rule, get explicit approval before merges, releases, irreversible writes, external messages, or accepting a product decision.

Treat the primary checkout as owner state unless its project contract says otherwise. Keep task worktrees isolated, preserve foreign work, and clean them only after their terminal state and unpublished changes are verified.

## Cross-Session Contract

Do not depend on another session's transcript. A worker session must return a decision handoff in chat with the outcome, recommendation or decision, caveats, verification, and durable links; a file path alone is insufficient. Durable state belongs in the configured tracker and versioned project documents or ADRs. The orchestrator reads those artifacts afresh, so a new orchestrator session can start at any time.

## Quick Reference

| Situation | Action |
|---|---|
| Project state requested | Audit authoritative sources in read-only mode |
| Parallel work requested | Propose independent session-sized lanes |
| Mutation requested | Perform only the requested mutations within owner gates |
| Tracker or contract absent | Inspect README and repository; recommend a minimal coordination surface |
| Worker reports completion | Require outcome, caveats, verification, and durable links |

## Common Mistakes

- Treating conversation history as authoritative project memory.
- Starting implementation instead of coordinating an already active project.
- Inventing trackers, claims, providers, or workflow concepts not configured by the project.
- Reporting inferred readiness as confirmed fact.
- Proposing a lane without dependencies, ownership, and a verifiable stopping condition.
- Returning many possible next steps instead of one next coordination action.
