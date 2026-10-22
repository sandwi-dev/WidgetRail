# Widget authoring improvements: review report

Branch: `codex/sdk-authoring-improvements`; baseline: `adb696ac`.

Status: Accepted by the user on 2026-10-22 for integration and preview.39.

The [implementation plan](widget-authoring-plumbing-plan.md) contains the full
consumer matrix, decisions, source evidence, and test artifacts.

## Implemented

- Shared focus request allocation and exact element targeting, with native
  admission rules preventing stale requests from replaying after a refresh.
- Captured navigator return targets and explicit collection viewport returns.
  The host owns geometry, lifecycle restoration, cancellation and bounded memory.
  Native row bindings now survive temporary range eviction without retaining
  provider authority or creating extra fetch demand.
- Single-current-query resources and an explicit-identity adapter for loaded
  lists, with applicable widget migrations and domain safeguards retained.
- Controlled disclosures with native accessibility and collapse focus recovery;
  explicit Settings field declarations; safe UTF-16 text chunking.
- Operation-admission fixes, shared coalesced notifications, and Spotify rollback
  that preserves newer provider observations.
- A shared captured focus-tree lookup for multi-target fallback decisions.

General suggestion registries, generic page/pin lease graphs, universal polling,
whole-document budgeting, blanket activity-cache removal, and mechanical
optimistic-command migration were rejected or narrowed where existing ownership
was safer. PoB build/provider identities, duplicate occurrences, account guards,
and external mutation reconciliation remain widget responsibilities.

## Validation

| Gate | Result |
| --- | --- |
| SDK / compatibility | 174/174; 17/17 |
| Playnite / Spotify / Settings | 219/219; 88/88; 94/94 |
| Shipping YTM / Full Application | 44/44; 5/5 |
| PoB comparison returns, both games | 62/62 each |
| PoB search returns | 95/95 PoE1; 166/166 PoE2 |
| Native delayed-return fixture | 51/51, including 29 source lifetime/reentrancy checks |
| Native shipping YTM navigation | 14/14 in each reordered/removed/ambiguous scenario |
| Native entry / reveal / continuity | 44/44; 10/10; 16/16 |
| SDK public surface / native protocol parity | 3876 symbols; 186 constants |
| Native Release build without validation code | Zero warnings/errors |

Protocol 101 makes viewport-return intent explicit. Existing source declarations
remain compatible; ordinary requests omit the new flag. As before, recompiling
indexed widgets advertises current SDK support for their deferred templates.

The 10,000-item YTM fixture still realizes at most 34 native rows. Compared with
the preceding host, frame intervals, reopen times and allocation ranges overlap.
This supports bounded behavior, not a claimed speed or memory improvement.
The fixture excludes artwork, network, playback and cold startup.

## Known limits

- Generic preview-first memento recreation and the broad grid fixture's fixed
  realization threshold still fail. Both failures were reproduced on separately
  compiled `223ffc16` using identical assertions and dimensions. They are not
  counted as passes; later checks in the broad script did not execute.
- Previously documented PoB constrained-layout, management/history and calculation
  baseline failures remain. Targeted changed paths pass their separate gates.
- One early uninstrumented YTM recreation timeout was not conclusively diagnosed.
  The fixture now adopts the latest frame and its event watermark together;
  subsequent diagnostic, repeated and final navigation runs pass.
- Some automated window captures are black. Native interaction/geometry evidence
  and inspected readable captures do not constitute full physical acceptance of
  every widget/theme or real controller hardware.

An isolated production-shell candidate was subsequently packaged and launched.
Smoke checks covered Settings navigation, Playnite achievements and suggestions,
Spotify and YouTube Music collection returns, PoE2 passive inspection, and overlay
hide/reopen. The Playnite material correction passed 56 focused layout/theme
checks and was inspected over bright artwork. These checks do not establish full
physical-controller, pinned-layout or all-theme visual coverage. The user accepted
the changes; unrelated primary-checkout edits remain intact.

The final restoration work is split into `75ba35be` (native binding lifetime),
`f021566a` (SDK/protocol contract), `cc54cbea` (host restoration and native fixtures),
and `f3eaded6` (Settings/PoB adoption). Earlier commits on this branch contain the
other completed findings.
