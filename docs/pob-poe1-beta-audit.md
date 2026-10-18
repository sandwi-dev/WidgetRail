# PoE1 beta candidate audit

## Candidate and boundaries

Prepared on 2026-10-18 with `Prepare-EngineCandidate.ps1 -Game 1 -Channel beta`.
The exact upstream revision is `274d129cc1f9ebad68d319e2e3117c9e948f5e10`;
its manifest reports `2.67.2-16de4b82d` and its beta changelog is dated 2026-10-16.
The candidate is in `artifacts/pob/poe1-beta-plan-20261018/engine`.
The scheduled/manual configured candidate channel now follows beta for both games; explicit stable/pinned modes remain available. This changes candidate selection only, without resuming deferred CI repairs or publishing a release.

PoE2 remains pinned to `84a11f1295f67a25f434329197444438514983f1`.
No upstream sources or calculation rules have been patched.

This audit distinguishes upgrading the engine from exposing all its desktop UI.
Native acceptance and final package verification are separate gates.

## Inherited engine changes

Upstream `changelog.txt` supplies the following changes since v2.67.2:

- Calculations: area-of-effect, critical chance, reservation/cost/conversion rounding,
  Sirus Meteor chaos damage, Iron Grip double application and several skill/modifier fixes.
- Game data and parsing: Chain Hook of Angling, Rhex talisman conversion, Pearl of
  Tsoatha, Baleful Dominion, Gaze of the Lich, Mercenary mods and Pantheon Aspect skills.
- Import/behaviour: mirrored-item text, keystone cluster imports and invalid granted
  skills/support modifiers. These run inside the existing upstream engine paths.
- Engine calculation, pathfinding and jewel-radius performance changes. Their presence
  does not establish a widget speedup; measure the widget protocol separately.

The adapter already enumerates upstream config options, skill controls and modifier
sources, so added eligible entries can appear without game-specific rules. This must
be checked against real engine responses, not inferred from changelog wording alone.

## Desktop additions requiring separate widget decisions

| Upstream addition | Current adapter evidence and boundary |
| --- | --- |
| Required/blocked trade modifiers | `TradeQueryGenerator.lua` offers `modNotSelector` controls; `trade-search.lua` supplies weighted search settings but does not expose these selectors. This remains an advanced-filter gap, not an inherited feature. |
| Additional corruption / vestigial implicit editing | Upstream `ItemsTab:CorruptDisplayItem` and Add Implicit are distinct dialogs. Existing `item-authoring.lua` covers normal modifier source selection and affixes, not these dialogs. Imported items retain upstream calculation support. |
| Automatic matching Forbidden Flame/Flesh | Upstream invokes counterpart creation/update from `AddDisplayItem`; adapter inventory mutations call `AddItem` directly. Do not claim counterpart automation is inherited. Any adoption must respect one explicit edit/Undo boundary and preview before adding extra items. |
| Timeless auto-socketing and extra finder filters | `timeless-tools.lua` uses the upstream finder, selected search settings and explicit result addition. Newly added `socketAllocate` control is not exposed. Keep explicit addition behaviour until reviewed; no automatic allocation on inspecting a result. |
| Mod browser including tree sources; sidebar hover breakdowns | Dedicated desktop UI is not imported by changing the pin. Widget calculation breakdown/source inspection remains its own upstream-backed presentation. Reusable rich previews are already in the approved plan. |
| Collapsible config sections | Widget layout owns this interaction. The engine pin cannot supply it; use the agreed shared presentation work. |
| Special unique crafting expansions | Upstream source catalogs are consumed generically where eligible; special dialogs need individual validation before parity can be claimed. |

These are audit findings, not authorization for a desktop parity rewrite. The agreed
workflow improvements and existing feature regression checks remain the acceptance scope.

## Evidence and remaining checks

- Direct Lua protocol startup and empty `new` request pass against the clean beta
  checkout, returning valid XML and 17 summary statistics. Evidence:
  `artifacts/pob/poe1-beta-plan-20261018/initial-result.jsonl` and `initial-errors.log`.
- Initial capability response correctly reports no loadout creation, node notes or
  weapon tree modes for this engine, and reports its available Timeless Jewel finder.
- Preliminary existing compiled harness: **511 passed, 0 failed** against current
  Lua sources (`preflight-tests.log`). This covers equipment, skills, sets, tree
  editing, Timeless search/addition, trade queries/ranking, calculation sources,
  save/reload, Undo/Redo, recovery, cancellation and timeout recovery.
- The newly built current-source harness subsequently passed the broad engine gate:
  **535 passed, 0 failed**, exit 0 (`current-gate.log`). Its binary predates only the
  subsequent PoE1 display-name string change; later package and native checks are recorded below.
- Current C# harness build: zero warnings/errors (`tests-build-01.log` and binlog).
  Empty/partial workflows: **23 passed, 0 failed** (`current-empty-02.log`);
  import/share-code/reload preservation: **7 passed, 0 failed** (`current-import.log`).
  An initial empty-test invocation passed a relative fixture path and failed before
  engine tests; corrected to an absolute path. This was a test invocation error.
- Constructor/module-loading compatibility is already handled by the shared
  capability-based helpers. PoE1 beta uses the same explicit constructors and
  proxy/metatable copy pattern as the pinned PoE2 beta. No new compatibility rules
  or upstream patches were needed.
- Repository lock and both PoE1 display/package names now identify the beta;
  PoE2's lock entry is unchanged. README, guide and developer naming agree.
- Final PoE1 packages have since passed release verification and installation through
  the ordinary verified CLI flow. The installed widget opened a fresh build, Search
  and its packaged tree; all six main-section transitions passed native checks.
  Both engines passed the later empty-build and changed-workflow gates. See the
  [verification ledger](pob-improvements-verification.md) for exact candidates.
  Remaining: the full integrated subpage/controller/couch walkthrough. The beta
  migration itself changes no functional shared Lua code.

## Bounded performance evidence

Local measurements use the pinned LuaJIT runtime, one engine process and synthetic
builds. They are warm-filesystem measurements from this machine, not cold-disk or
desktop PoB benchmarks. No game-service requests were made.

`protocol-perf.json` and its retained Python probe record:

| Operation | Observed result |
| --- | --- |
| Process readiness | 1,959 ms |
| Empty build request after readiness | 447 ms; 295.7 MiB working set |
| Synthetic equipped fixture import | 1,046 ms; 292.6 MiB working set |
| Ten repeated imports | Median 667 ms, maximum 872 ms; identical summary statistics |
| Working set after ten imports | 276.9 MiB |
| Six alternating level changes/recalculations | Median 268 ms |

The preliminary engine gate additionally records 100 synthetic candidates in ten-item
batches taking 1.90 s (12 protocol requests), versus 14.69 s individually (102 requests).
Working set during that run stayed near 284 MiB; idle CPU was 0 ms over 1.2 s.
These observations show bounded repeated loading and preservation of the existing
batch advantage.

A sequential comparison using the identical adapter, runtime, synthetic fixture and
probe against stable `b32759ab0f31a1c8499a0d420cb0f0633d4fe478` is retained in
`protocol-perf-stable.json`:

| Operation | Stable | Beta |
| --- | ---: | ---: |
| Process readiness | 2,210 ms | 1,959 ms |
| Empty build request | 239 ms | 447 ms |
| Fixture import | 502 ms | 1,046 ms |
| Repeated import median | 474 ms | 667 ms |
| Level recalculation median | 298 ms | 268 ms |
| Working set after ten imports | 309.8 MiB | 276.9 MiB |

Beta's import path was slower in this sample, despite lower resident memory and
slightly faster readiness/recalculation. This is an explicit migration tradeoff,
not evidence of a universal beta performance improvement. The single-machine,
single-run samples include normal system noise; final native inspection must still
check whether opening/switching builds feels acceptable. All ten imports retained
identical calculated summaries within each engine.
