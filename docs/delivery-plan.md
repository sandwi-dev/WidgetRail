# WidgetRail — Delivery Workflow Contract

## Authority

Plane's `WidgetRail` project is the sole live delivery control plane. It owns
all active Plane work items, lane assignment, ordering, dependencies, status, blockers,
review disposition, and delivery evidence. Its work-item descriptions are the
implementation assignments.

Repository documents retain stable engineering rules, architecture and design
records, public documentation, and Git history. They do not duplicate Plane's
live queue, review status, or routine evidence.

The detailed pre-Plane delivery plan is preserved in Git history and in the
existing timestamped snapshots under `docs/history/delivery-plan/`.

## Plane work-item contract

Every implementation item must state:

- Native Plane work-item identifier (for example, `WIDGE-9`) and lane.
- Baseline, dependencies, and integration constraints.
- Bounded objective, scope, and explicit exclusions.
- Acceptance criteria and verification tier.
- Stop and escalation conditions.
- Links to relevant repository records, branch, commits, and retained evidence.

Use the `lane:platform` and `lane:widgets` labels for exclusive delivery lanes.
Use `gate:blocked`, `gate:manual`, and `gate:tests` for incomplete acceptance
conditions; use `kind:recovery` and `kind:integration` where applicable.

`Todo` means Ready and authorized. `In Progress` means assigned work is active.
`Done` means independently accepted, integrated, and fully closed. Do not mark
accepted production Done while focused regression work remains; retain it open
with `gate:tests`. `Cancelled` records rejected or superseded work and its
evidence. A blocked item stays in Backlog with `gate:blocked` and names the
exact evidence needed to reopen it.

## Operating loop

1. The reviewer reads the stable goals and queries Plane's lane, review, and
   blocked queues.
2. The reviewer orders each lane's Backlog and Todo columns in Plane, promotes
   the top unblocked Backlog item into Todo when ready, and dispatches at most
   one active item per lane.
3. An implementation agent reads only its assigned Plane item and linked
   technical records, works within scope, and posts a completion comment.
4. The reviewer independently checks the commit and evidence, then records a
   Plane comment and moves the item to its resulting state.
5. Update Plane only on a real delivery transition: dispatch, blocker, commit,
   review result, physical verdict, test closure, or integration.

Plane does not authorize pushes, credential use, destructive recovery, or a
material product decision requiring the user. The reviewer goal separately
records the user's standing authority to push only accepted integrated `main`.
Do not bulk-import historical DLVs or duplicate source-level evidence into Plane
Pages.
