# Repository agent guidance

## Review widget and framework ownership

When investigating, implementing, or reviewing widget behavior, check whether
the widget is compensating for a missing SDK abstraction or host behavior.
Apply this check to affected code without waiting for a separate audit request.

Before adding manual plumbing, inspect the current SDK, protocol, host, and
representative consumers. Prefer an existing supported API; extend it when a
reusable contract is missing. Widget authors should supply domain data, policy,
and user intent. The framework should own generic presentation mechanics and
their lifecycle where it has the necessary authority.

Look especially for:

- Author-managed focus/reveal counters, return-focus flags, input-event
  correlation, and reconstruction of navigation or modal history.
- UI-tree traversal or rewriting to recover control semantics, parsing visible
  labels or style classes to determine behavior, and reliance on component child
  ordering or generated IDs.
- Repeated resource generations, cancellation wiring, loading/error/busy flags,
  polling loops, and page/pinned-layout demand coordination.
- Repeated collection identity/publication logic, suggestion-source routing,
  text splitting around protocol limits, disclosure state, notification delivery,
  and optimistic-command bookkeeping already handled elsewhere.

These are investigation signals, not blanket bans. Preserve domain revisions,
provider ordering, account boundaries, duplicate-item occurrence identity,
external side-effect reconciliation, and authorization checks. Cancellation
alone does not prove that a late result or external mutation is harmless.
Do not move provider-specific semantics into the host merely to reduce code.

Report material findings with a concrete source location, the failure mode or
maintenance risk, the proposed owner (widget, SDK, protocol, or host), and whether
the issue is reproduced or inferred from source. Distinguish an existing API
that needs adoption from a genuine framework gap. For proposed shared additions,
estimate complexity and cite a relevant framework precedent when useful.
Do not claim a source audit establishes runtime correctness or performance.

Resolve in-scope issues as part of authorized work. Record broader architectural
work in the applicable plan rather than silently expanding the task into a
framework rewrite or stopping for unnecessary confirmation.

## Preserve behavior and reassess proposed abstractions

Treat audit findings and implementation plans as hypotheses, not obligations to
refactor. Existing functionality and proven safeguards take priority over fewer
lines of widget code or completing every planned item.

Before migrating a representative consumer, establish its behavior and relevant
failure cases. Compare the proposed change with the existing implementation for
correctness, authoring complexity, lifecycle ownership, performance, and ease of
debugging. Passing tests alone does not justify a more brittle abstraction.

If implementation evidence contradicts the audit, or a proposal needs extra
special cases, hidden coupling, or weaker guarantees to fit real consumers, stop
that change. Retain the working implementation and remove only the unsuccessful
experiment's own edits, preserving unrelated and concurrent work. Do not force
the migration, weaken tests, or remove safeguards to make the plan appear done.

Record the corrected assessment and evidence in the plan, marking the proposal
rejected, narrowed, or deferred as appropriate. Explain the decision to the user
and continue independent authorized work. A justified decision to retain the
existing design is a valid audit outcome. Do not ship a known regression or claim
unverified behavior is preserved.

## Check every consumer of a shared change

For each SDK, protocol, or host fix, perform a repository-wide applicability
check alongside implementation. Inspect shipping widgets in `samples` and
`src/FirstPartyWidgets`, plus templates, SDK examples, and relevant tests.
Verify actual entry points: legacy fixtures are not production consumers.

Maintain a compact consumer matrix in the implementation plan or review:
affected and migrated, already compliant, not applicable, or deferred with a
specific reason. Search for equivalent manual patterns as well as direct API
calls; a consumer may need the fix precisely because it bypasses the API.

Reuse one shared mechanism where semantics match, preserving lazy realization,
bounded memory, stale-action rejection, scope isolation, and existing public
compatibility. Remove superseded widget plumbing after equivalent behavior is
covered. Do not force unrelated domain workflows into a generic abstraction.

Run proportional behavior-focused checks for affected consumers. Focus and
navigation changes require native interaction evidence; collection/lifecycle
changes need relevant delayed-result, teardown, identity, and performance checks.
Report deferred consumers and unverified behavior explicitly. A separate full
audit of every widget is not a prerequisite for each bounded shared fix.
