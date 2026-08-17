# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through DLV-262 is preserved in the
[2026-08-16 21:40 snapshot](history/delivery-plan/2026-08-16T21-40-35-07-00.md).
Earlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are
evidence only; this file is the sole authority for current work.

## Current baseline

- Accepted main is `1ae21df`. It contains physically accepted DLV-263 through
  `110b42b`, test-only DLV-261 as `c519df4`, and test-only DLV-262 as
  `88b21b1`, atop the accepted DLV-256 production baseline.
- Exact accepted production PID 28612 was built from DLV-263 production commit
  `a181731`. The post-acceptance DLV-263 test commit, DLV-261, DLV-262, and
  planner documentation do not change runtime inputs, so no rebuild or relaunch
  is required.
- DLV-263 acceptance evidence: zero logged error/failure/stale/timeout/rejection
  lines; exact-anchor and retained-overlap admissions; focus remained inside
  the viewport; 33 frames averaged 26.9 ms, one reached 52.0 ms, none exceeded
  100 ms; focused renderer coverage passed 4,915 checks.
- DLV-261 passes Platform Settings 18/18 after correcting only two stale
  button-scale expectations. DLV-262 passes Widget Bridge 90/90 after a
  serialized dependency rebuild and two exact stale-reference corrections.
- DLV-248 remains deliberately deferred by the user.
- The user selected the new display identity **WidgetRail** and tagline
  **WidgetRail — a controller-first widget platform for Windows.** DLV-257 is
  the current planner gate. DLV-258 through DLV-260 remain serialized and
  blocked until its exact mapping is approved and committed.

## Avalonia disposition

The Avalonia experiment is closed and failed by user decision. Retained
Avalonia branches, worktrees, sources, and AVP history are evidence only. Do not
dispatch, integrate, relaunch, delete, or resume them without a new explicit
user decision. The native overlay is the sole production presentation path.

## Execution and review rules

- Operate exactly the standing `widgets` and `platform` production tasks. The
  rejected DLV-245 red-test task is preserved evidence and is not active.
- Implementation tasks select only Assigned/Ready work from this file, remain
  inside their lane, never edit reviewer-owned documents, and never push.
- The planner reviews actual diffs/evidence, integrates only accepted commits,
  commits reviewer-owned documents separately, never authors implementation or
  test code, and never pushes.
- Shared protocol, architecture, public identity, package, and persistence work
  is serialized to the named lead lane. No partial rebrand is launched or
  integrated as the accepted product.
- Preserve unrelated user work. Do not reset, rebase, amend, discard, rewrite,
  or perform destructive recovery.
- Verification is proportional: focused affected suites by default, the
  smallest cross-component group only when required, and Tier 3 only at the
  named DLV-260 checkpoint.
- Visible UI corrections use physical-first ordering when assigned: production
  code and Release build, user verdict, then focused tests. Tests never
  legitimize rejected behavior.
- Rebuild/repackage/relaunch after an accepted integrated milestone only when
  production/runtime artifact inputs changed. Retain the running accepted
  instance for tests-only or planner-document-only integration.
- Stop for credentials, destructive migration/reset, substantial conflict,
  undocumented APIs, external publication/account action, physical-only
  evidence, or a material architecture/security/product choice.

## Durable architecture decisions

- Taffy is the sole declarative Flex/Responsive Grid geometry owner. Native code
  retains scroll, clipping, DPI/pixel policy, focus-follow, input,
  accessibility, rendering, animation, and HWND placement.
- One tightly bounded content HWND and one tightly bounded fixed-chrome HWND are
  coordinated endpoints of one overlay session/window, graphics, GameInput,
  focus, and accessibility owner. One backdrop HWND may cover the monitor.
- Chrome placement is client-local-first. Only the chrome HWND has an absolute
  screen rectangle; guide/tray screen bounds are downstream projections.
- The tray remains small host-owned arithmetic. Do not migrate it to Taffy
  without new evidence.
- Games & Apps is bundled first party. Spotify, Game Launcher, and YT Music are
  ordinary Community applications. Core code contains no service-specific
  identity, DTO, API, or known-tree behavior.
- Complete snapshots are retained last-admitted checkpoints. Generic typed
  operations update them; semantic, resolved-resource, focus, scroll, press,
  slider, layout, UIA, composition, and placement authority remain separate.
- Sandboxed and full-trust widgets share the bounded host-admission protocol.
  Full trust grants no overlay HWND/render/input/focus authority.
- No second GameInput reader, bridge transport, semantic schema,
  lifecycle/cache owner, overlay session, graphics owner, or focus tree.
- The product is pre-release and single-user. Intentional breaking changes may
  atomically migrate/reset only explicitly owned local state; external provider
  data, credentials, accounts, applications, and user files are preserved.

## Active task map

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | Clean and idle at DLV-262 commit `b4acea1`; accepted patch is integrated as `88b21b1`. DLV-248 remains deferred. Do not begin DLV-259 before accepted DLV-258 is supplied on the exact serialized baseline. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | Clean and idle after DLV-253 test commit `8db298a`; accepted chain is integrated through `91177ec`. Do not begin DLV-258 before DLV-257 approval/commit. |

## Assigned planner deliverable — DLV-257: select and freeze WidgetRail identity

Owner/baseline: user plus planner from accepted main `1ae21df`. This is a
decision and inventory deliverable, not implementation authority.

Required output:

- Record the exact display name, technical PascalCase namespace root, lowercase
  package/repository slug, publisher attribution, and whether the existing
  `gbar` CLI/config prefix is retained or renamed.
- Verify basic web, source-repository, Microsoft Store-name, domain, and relevant
  trademark-search availability. Availability is evidence, not legal clearance.
- Obtain the user's explicit approval of the final identity. Recommend qualified
  trademark counsel before public release; do not contact counsel, Microsoft,
  registrars, or third parties without new authority.
- Produce an exact old-to-new inventory for visible strings, SDK/NuGet identity,
  namespaces, assemblies, manifest provider types, package metadata, local-data
  roots, pipe/mutex/window/UIA identifiers, user-agent strings, templates,
  samples, active docs, store collateral, repository name, and build outputs.
- State which legacy identifiers are migration inputs, which are removed at
  cutover, and which historical records remain intentionally unchanged.

Current artifact: `docs/widgetrail-identity-migration-contract.md` contains the
complete proposed mapping and preliminary availability evidence. It awaits the
user's decision on publisher/technical ID root, archive/style names,
tool/config prefixes, and existing credential-target disposition.

Acceptance: one reviewer-owned identity/migration contract committed locally,
with the chosen name approved by the user and every DLV-258/DLV-259 input
unambiguous. No production/test changes, Store reservation, external account
action, push, publication, directory move, build, or relaunch.

Stop for an unchosen name, meaningful legal ambiguity, unavailable Store or
repository identity, required third-party account/credential, competing
migration policies, or external action.

## Blocked serialized deliverable — DLV-258: cut over public managed identity

Owner/baseline: widgets lead after DLV-257 completes, from the then-current
accepted main. This is a serialized cross-lane identity milestone: no other
lane edits SDK, package, assembly, manifest, template, style, or managed
namespace identity concurrently.

Rename the user-facing product brand and public managed identity coherently:
SDK/NuGet package ID, public namespace root, assembly/root namespaces, manifest
provider types, managed package metadata, CLI discovery, package/style formats,
templates, samples, first-party and Community package sources, active public
documentation, and directly affected deterministic tests. Use only the
DLV-257 mapping. Do not retain dual public SDK namespaces, compatibility
facades, type forwarders, duplicated manifests, or old/new publication paths.
Do not touch immutable history, closed Avalonia sources, native IPC/HWND
identifiers, or local persisted state in this milestone.

Acceptance: all managed projects and public examples compile from the new
identity; manifests resolve only new provider types; a newly packed SDK exposes
only the new package/namespace identity; current packages publish without old
managed assemblies; and a repository scan reports no unexplained old active
managed identity. Run only focused managed build/package/contract suites; no
Tier 3 and no launch. Commit one coherent DLV-258 milestone and leave it
unintegrated/unlaunched pending DLV-259.

Stop for a public compatibility requirement, external package publication,
third-party credential/account action, generated-output ambiguity, substantial
conflict, user-data change, native identity change, or a second SDK/package
owner. Never push.

## Blocked serialized deliverable — DLV-259: migrate native and persisted identity

Owner/baseline: platform lead after accepted DLV-258 is supplied on its exact
serialized baseline. Own native display/accessibility identity, singleton
activation identity, local product-owned state root, runtime path consumers,
and directly affected tests. No other lane edits those identities concurrently.

Apply the DLV-257 mapping to native titles, accessibility product names, app
manifest assembly identity, HWND/text-entry/pinned-surface classes, singleton
mutex and activation pipe, user agent, startup/log/runtime paths, and the new
product-owned local-data root. Implement one bounded, atomic and idempotent
migration from `%LOCALAPPDATA%\GameBarAlternative` into the approved new root
for current settings, consent decisions, installed-widget catalog/packages,
trusted private state, and other explicitly inventoried overlay-owned data. New
code writes only the new root after success. Preserve external provider data,
credentials, accounts, installed applications, and user files. Do not keep dual
live singleton/pipe/window-class owners; old names are migration/cleanup inputs,
not a compatibility runtime.

Acceptance: focused migration cases cover absent old state, clean migration,
already-migrated restart, collision/incomplete destination, access denial,
interrupted migration recovery, exact consent/catalog preservation, and no
external-data movement. Focused native cases prove one new singleton/activation
owner, expected UIA identity, new paths, bounded failure behavior, and clean
shutdown. Build the coherent combined DLV-258/DLV-259 Release but do not launch
or integrate the partial chain before DLV-260 review. Tier 2 is limited to the
smallest managed/native/package boundary; no Tier 3 yet.

Stop for destructive reset, ambiguous source/destination authority, credential
or external-provider migration, simultaneous old/new runtime ownership,
undocumented Windows APIs, substantial conflict, or data-loss risk. Never push.

## Blocked serialized deliverable — DLV-260: audit and accept complete rebrand

Owner/baseline: serialized lead plus planner review after DLV-258 and DLV-259.
Complete active tests, scripts, examples, publication metadata, notices, and
nonhistorical documentation required by the DLV-257 mapping. Remove stale
old-name artifacts from validated build/package output before republishing the
coherent Release. Do not rewrite Git history, immutable reviewer snapshots,
closed Avalonia evidence, third-party notices that accurately quote historical
names, or external repositories/store listings without explicit user authority.
Renaming the local checkout directory and saved Codex project is optional and
performed last by the planner only if the user requests it.

Acceptance: one exact old-name residue audit classifies every retained match;
focused builds/tests/examples/package checks are green; installed packages and
the SDK contain only the new active identity; old generated runtime artifacts
are absent; one narrow local-state migration smoke preserves current settings,
consent and installed widgets; and the coherent renamed Release launches for
the user's visible verdict. This is the named Tier-3 integration checkpoint for
the cumulative DLV-258/DLV-259/DLV-260 chain, run once only after focused
evidence and exact commits. Integrate and relaunch only after independent review
accepts the complete chain.

Stop for unresolved old active identity, external Store/repository/domain
action, credential need, destructive cleanup outside validated generated
outputs, unexplained package residue, substantial conflict, or legal/product
name uncertainty. Never push or publish.

## Ready later — DLV-248: media optimistic command revision reconciliation

Prevent an older provider publication from overwriting a newer host-projected
play/pause command while that command is pending. Use one bounded command
revision/acknowledgement policy inside the existing media widget owner, preserve
authoritative provider correction and timeout/failure rollback, and avoid
special handling in the native host or snapshot protocol. This remains
deliberately deferred by user decision and requires explicit promotion.

## Serialized order

1. Complete and commit user-approved DLV-257.
2. Supply accepted current main to the widgets lane for DLV-258.
3. Review DLV-258 and supply its exact serialized chain to the platform lane for
   DLV-259; do not integrate or launch the partial rename.
4. Review cumulative DLV-258/DLV-259, complete DLV-260 residue audit and one
   exact Tier-3 checkpoint, then launch for the user's visible verdict.
5. Integrate the complete accepted chain only after that verdict.
6. DLV-248 remains outside this sequence until the user promotes it.

## Manual, external, and blocked evidence

| Item | Blocker / required evidence |
| --- | --- |
| DLV-257 identity | User approval of the four exact mapping decisions in the draft contract. |
| Microsoft Store name | Partner Center availability/reservation through the user's account; public search is insufficient. |
| Domain | Live registrar/RDAP availability and optional registration through the user's account. |
| Trademark | Similar-mark clearance for related software/services; qualified counsel recommended before public release. |
| GitHub identity | User-selected owner plus repository/organization availability and optional rename/creation. |
| DLV-248 | Deliberately deferred until explicit user promotion. |
| Avalonia | Failed/cancelled; requires a new explicit user decision. |
| YT Music catalog cleanup | Approval to remove only inactive, non-selected 0.2.0. |
| Trusted fixed-video/PiP | Changed resource budget or authorized experiment. |
| Audio default-device selection | Documented supported Windows setter plus reversible hardware/provider plan. |
| Native uninstall reconciliation | Deterministic disabled/nonresident removal evidence. |
| YouTube authenticated library | Approved minimum-scope OAuth/account; Watch Later is unsupported by the Data API. |

## Recent accepted milestones

| Milestone | Result |
| --- | --- |
| DLV-263 | Physically accepted and integrated through `110b42b`: retained-overlap viewport position, exact-anchor behavior, conservative fallback, 4,915 renderer checks, accepted PID 28612 retained. |
| DLV-262 | Test-only integrated as `88b21b1`: exact failure ledger, current dependency graph, unchanged authenticated named-pipe path, Widget Bridge 90/90. |
| DLV-261 | Test-only integrated as `c519df4`: exactly two stale button-scale expectations corrected, Platform Settings 18/18. |
| DLV-256 | Physically accepted and integrated through `b0d53b0`: bounded artwork LRU, measured full-surface transport promotion, retained renderer work, focus-scroll convergence, 4,894 checks. |
| DLV-254 | Physically accepted and integrated through `57fb469`: optional widget-switch animation defaults Off; cold and warm Off paths snap to exact destination; explicit On remains animated. |
| DLV-253 | Physically accepted and integrated through `91177ec`: Games & Apps retains resolved state, cold entry starts first row, in-session focus restores, 65/65. |

Do not mark the continuing delivery goal complete. Continue until the user
pauses/replaces it or all useful lanes are genuinely blocked. Never push.
