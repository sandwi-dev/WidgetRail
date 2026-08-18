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
- DLV-258 is independently reviewed and accepted as the unintegrated commit
  `5b924f4`. Its 544-file public managed cutover passed the focused managed,
  SDK, CLI/package, WRSS, catalog, Bridge, conformance, and documentation
  evidence. DLV-259 is independently reviewed and accepted as unintegrated
  commit `188cc64`; its native cutover and coherent tests-skipped Release build
  are complete. DLV-260 Phase A is independently source/output reviewed as the
  corrected two-commit implementation `70af468` plus `85a021a`. Its exact
  tests-skipped Release is visibly running as PID 33088 for the user's verdict;
  the cumulative chain remains unintegrated.
- DLV-248 remains deliberately deferred by the user.
- The user selected the new display identity **WidgetRail** and tagline
  **WidgetRail — a controller-first widget platform for Windows.** On
  2026-08-17 the user approved all four remaining DLV-257 naming decisions.
  The identity contract is frozen. The user subsequently approved a clean
  local-state break: WidgetRail uses only `%LOCALAPPDATA%\WidgetRail`; it does
  not read or migrate the old root or carry compatibility code. DLV-258 is
  accepted, DLV-259 is accepted under that amendment, and production-first
  DLV-260 Phase A is assigned to the platform lane.

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
  start from fresh explicitly owned local state. Do not add old-version
  migration or compatibility code without a later explicit user decision;
  external provider data, credentials, accounts, applications, and user files
  are preserved.

## Active task map

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | Clean at corrected DLV-260 Phase A commits `70af468` plus `85a021a`. Exact reviewed Release SHA-256 `1E816334FD5B48ABB9449CB66FEA66CE0A93E14E8DFECEB259FE6C1753BE503D` is visibly running as PID 33088 for the user's verdict. Spotify 0.3.0 and YT Music 0.2.9 are installed/enabled in the fresh WidgetRail catalog. Do not run or repair tests before acceptance. DLV-248 remains deferred. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | Clean at accepted DLV-258 commit `5b924f4` on `codex/impl-widgets-widgetrail-managed-identity`. Hold this exact accepted evidence; the platform lane is the single serialized DLV-260 lead. Do not start new work. |

## Completed planner deliverable — DLV-257: select and freeze WidgetRail identity

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
- State whether any legacy identifiers are migration inputs, which are removed
  at cutover, and which historical records remain intentionally unchanged. The
  later user-approved amendment sets the active answer to no migration inputs.

Completed artifact: `docs/widgetrail-identity-migration-contract.md` contains
the complete approved mapping and preliminary availability evidence. On
2026-08-17 the user approved `WidgetRail Project`/`widgetrail`, all four new
archive/style names, the `wrail` tool/config family, and re-authentication rather
than automatic Credential Manager migration.

Acceptance: one reviewer-owned identity/migration contract committed locally,
with the chosen name approved by the user and every DLV-258/DLV-259 input
unambiguous. No production/test changes, Store reservation, external account
action, push, publication, directory move, build, or relaunch.

Stop for an unchosen name, meaningful legal ambiguity, unavailable Store or
repository identity, required third-party account/credential, competing
migration policies, or external action.

## Accepted serialized deliverable — DLV-258: cut over public managed identity

Owner/baseline: widgets lead from a fresh branch at planner main containing the
completed DLV-257 contract. This is a serialized cross-lane identity milestone:
no other lane edits SDK, package, assembly, manifest, template, style, or
managed namespace identity concurrently.

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

Accepted result: commit `5b924f4` changes 544 files with 6,291 insertions and
6,247 deletions. Focused Release evidence passes the managed/conformance build
with zero warnings/errors, WidgetSdk 89/89, compatibility 12/12, Wrail CLI and
external package flow 65/65, WRSS 23/23, catalog 35/35, Bridge 90/90,
first-party/Community conformance 6/6, 75 active Markdown files, and the
verification-runner self-test. Review found zero approved old project-owned
package IDs or old public archive/style formats in active managed scope. The
remaining old names are deferred native/AppContainer identities, persisted
paths, exact retired-output cleanup sentinels, opaque test IDs, and
immutable/closed evidence. No native suite, Tier 3, launch, integration,
publication, push, credential access, or user-state migration occurred.

Stop for a public compatibility requirement, external package publication,
third-party credential/account action, generated-output ambiguity, substantial
conflict, user-data change, native identity change, or a second SDK/package
owner. Never push.

## Accepted serialized deliverable — DLV-259: cut over native identity and fresh local state

Owner/baseline: platform lead after accepted DLV-258 is supplied on its exact
serialized baseline. Own native display/accessibility identity, singleton
activation identity, local product-owned state root, runtime path consumers,
and directly affected tests. No other lane edits those identities concurrently.

Apply the DLV-257 mapping to native titles, accessibility product names, app
manifest assembly identity, HWND/text-entry/pinned-surface classes, singleton
mutex and activation pipe, user agent, startup/log/runtime paths, and the new
product-owned local-data root. Also update native consumers of the now-approved
managed package IDs, `.wrwidget`/WRSS formats, catalog fields, styles, and CLI
names so the combined DLV-258/DLV-259 Release has no split identity. Implement
a fresh `%LOCALAPPDATA%\WidgetRail` store. New code reads and writes only the
new root and must not probe, read, copy, move, merge, reinterpret, stage, delete,
or otherwise touch `%LOCALAPPDATA%\GameBarAlternative`. Do not add old package
or persisted-format importers, legacy root fallbacks, old singleton/pipe
activation compatibility, version bridges, or other support for pre-release
versions. Preserve external provider data, credentials, accounts, installed
applications, user files, and the untouched old product root. Settings,
consents, packages, and credentials are configured again under WidgetRail.

Acceptance: focused native cases prove one new singleton/activation owner,
expected UIA identity, new paths, bounded failure behavior, clean shutdown, and
a fresh new-root startup. Focused path cases prove the new build never accesses
the old root and has no legacy fallback/import path. Build the coherent combined
DLV-258/DLV-259 Release but do not launch or integrate the partial chain before
DLV-260 review. Tier 2 is limited to the smallest managed/native/package
boundary; no Tier 3 yet.

Stop for any proposed legacy compatibility path, any access to or mutation of
the old root, credential/external-provider handling, simultaneous old/new
runtime ownership, undocumented Windows APIs, substantial conflict, or
data-loss risk. Never push.

Accepted result: implementation commit `188cc64` changes 52 files with 254
insertions and 194 deletions. Independent review confirms a mechanical native
cutover for UIA/window/singleton/pipe/AppContainer identity, runtime/local paths,
package/style consumers, persisted headers, and current scripts. Active
production source contains no old-root reader or compatibility bridge. The
coherent tests-skipped Release built successfully with `OverlayHost.exe`
SHA-256 `16D5BE7CC2D831BA72D701A8C5C63086C4EFCB790FF6ABAFAC69CAE6056A46F8`.
Focused native evidence passed for process ownership, package import, pinned
placement, surface coordination, accessibility, renderer, and settings.

Two disclosed verification routes are inconclusive rather than production
failures. Widget Runtime stopped after six cases at its existing seventh-case
hang. `WidgetBridgeCatalogTests.exe` appeared to hang but actually opened a
native null-read dialog: the Release build defines `NDEBUG`, while the fixture
uses disabled `assert(...)` guards around values later dereferenced outside the
guards. This test-harness defect predates DLV-259; the DLV-259 test diff only
adapts an already-changed result wrapper and `.wrwidget` spelling. Repair it
after the user's visible DLV-260 verdict, not before.

## Assigned serialized deliverable — DLV-260: audit and accept complete rebrand

Owner/baseline: serialized lead plus planner review after DLV-258 and DLV-259.
Complete active tests, scripts, examples, publication metadata, notices, and
nonhistorical documentation required by the DLV-257 mapping. Remove stale
old-name artifacts from validated build/package output before republishing the
coherent Release. Do not rewrite Git history, immutable reviewer snapshots,
closed Avalonia evidence, third-party notices that accurately quote historical
names, or external repositories/store listings without explicit user authority.
Renaming the local checkout directory and saved Codex project is optional and
performed last by the planner only if the user requests it.

Execution and acceptance are explicitly physical-first:

1. Phase A classifies every active old-name residue, corrects only active
   production/public identity and validated generated outputs, proves the SDK
   and installed packages expose only WidgetRail, and builds one coherent
   tests-skipped renamed Release. Do not run or repair tests in this phase.
2. After independent source/output review, launch that Release for the user's
   visible verdict. The user will configure fresh settings/consent/packages and
   re-authenticate as needed. The old local-data root remains untouched.
3. Only after the user accepts the visible Release, replace disabled/unsafe
   `assert(...)` use in `WidgetBridgeCatalogTests` with always-on checks without
   weakening coverage, disposition the existing Widget Runtime seventh-case
   hang, and run the smallest focused package/runtime routes plus the one named
   Tier-3 integration checkpoint.
4. Integrate the complete accepted DLV-258/DLV-259/DLV-260 chain after that
   evidence. If the only final delta is tests/docs, retain the already accepted
   running Release rather than rebuilding or relaunching it.

Phase A candidate result: implementation commit `70af468` completed the active
production/public residue and generated-output cleanup and built one coherent
tests-skipped Release. It was initially rejected for also editing two
reviewer-owned documents; correction commit `85a021a` restores those paths
exactly without changing production. Independent cumulative review passes
`git diff --check`, finds no retired identity in active production source, and
confirms `OverlayHost.exe` SHA-256
`1E816334FD5B48ABB9449CB66FEA66CE0A93E14E8DFECEB259FE6C1753BE503D`.
The exact candidate is visibly running as PID 33088 from the isolated Phase A
Release root. Fresh `%LOCALAPPDATA%\WidgetRail` startup elected one process
owner, admitted Settings, and logged no startup error. The prior candidate PID
34364 later accepted an authenticated `Show` activation without recording the
expected foreground transition after an external-foreground close; restarting
the same artifact restored foreground activation and loaded all seven tray
entries. Await the user's visible verdict before any test repair, Tier 3, or
integration; retain the reopening observation unless the user confirms it was
environmental.

Stop for unresolved old active identity, external Store/repository/domain
action, credential need, destructive cleanup outside validated generated
outputs, any old-root access, unexplained package residue, substantial conflict,
or legal/product name uncertainty. Never push or publish.

## Ready after WidgetRail rename — DLV-264: prevent stale hidden-snapshot failure publication

Correct the pre-existing Now Playing lifecycle race observed during a rapid
Now Playing -> Settings -> Now Playing switch. A background snapshot request
can be admitted after the widget is selected and visible, then fail as if the
widget were still hidden and suspended; that older failure can replace a valid
visible checkpoint and persist even after the newer interactive lifecycle
request succeeds.

Keep the fix inside the existing session/lifecycle authority. Do not issue a
background snapshot for the currently selected visible widget. Let a newer
visible/interactive request supersede or cancel an older background refresh.
Reject older snapshot failures using request and lifecycle authority rather
than runtime generation alone. Retain the last valid host checkpoint instead of
promoting this secondary hidden-cache miss to a persistent visible failure.
Do not mix this work into DLV-259/DLV-260, the renderer, or deferred DLV-248.

Acceptance follows physical-first ordering: implement the bounded correction
and build a coherent Release; the user rapidly switches away from and back to
Now Playing and confirms that valid content returns without the hidden-suspended
failure; only after that visible acceptance add focused lifecycle sequencing,
supersession, stale-failure rejection, and checkpoint-retention tests. Integrate
only the user-accepted production change and its accepted test follow-up.

Stop for a protocol redesign, renderer changes, loss of authoritative provider
failure reporting, destructive state action, substantial conflict, or evidence
that the incident has a different owner. Never push.

## Ready later — DLV-248: media optimistic command revision reconciliation

Prevent an older provider publication from overwriting a newer host-projected
play/pause command while that command is pending. Use one bounded command
revision/acknowledgement policy inside the existing media widget owner, preserve
authoritative provider correction and timeout/failure rollback, and avoid
special handling in the native host or snapshot protocol. This remains
deliberately deferred by user decision and requires explicit promotion.

## Serialized order

1. DLV-257 is complete: the user approved all four decisions on 2026-08-17 and
   the exact contract is frozen.
2. DLV-258 is accepted at `5b924f4` and DLV-259 is accepted at `188cc64`; both
   remain unintegrated and are included in the running DLV-260 candidate.
3. DLV-260 Phase A is source/output reviewed at corrected commits `70af468` plus
   `85a021a`; exact Release PID 33088 is awaiting the user's visible verdict.
4. After that verdict, repair/disposition the two disclosed test routes and run
   the one exact Tier-3 checkpoint.
5. Integrate the complete accepted chain only after that evidence.
6. Run DLV-264 next from the accepted complete WidgetRail rename baseline.
7. DLV-248 remains outside this sequence until the user promotes it.

## Manual, external, and blocked evidence

| Item | Blocker / required evidence |
| --- | --- |
| DLV-257 identity | Complete: all four exact mapping decisions approved on 2026-08-17. |
| Microsoft Store name | Partner Center availability/reservation through the user's account; public search is insufficient. |
| Domain | Live registrar/RDAP availability and optional registration through the user's account. |
| Trademark | Similar-mark clearance for related software/services; qualified counsel recommended before public release. |
| GitHub identity | User-selected owner plus repository/organization availability and optional rename/creation. |
| DLV-264 | Ready immediately after the complete WidgetRail rename is accepted and integrated. |
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
| DLV-259 | Accepted but deliberately unintegrated/unlaunched as `188cc64`: complete native WidgetRail identity and fresh-root cutover, coherent tests-skipped Release green; two pre-existing test-harness routes disclosed for post-visible-acceptance repair/disposition. |
| DLV-258 | Accepted but deliberately unintegrated/unlaunched as `5b924f4`: complete public managed WidgetRail cutover, focused managed suites green, residue classified for DLV-259/DLV-260. |
| DLV-263 | Physically accepted and integrated through `110b42b`: retained-overlap viewport position, exact-anchor behavior, conservative fallback, 4,915 renderer checks, accepted PID 28612 retained. |
| DLV-262 | Test-only integrated as `88b21b1`: exact failure ledger, current dependency graph, unchanged authenticated named-pipe path, Widget Bridge 90/90. |
| DLV-261 | Test-only integrated as `c519df4`: exactly two stale button-scale expectations corrected, Platform Settings 18/18. |
| DLV-256 | Physically accepted and integrated through `b0d53b0`: bounded artwork LRU, measured full-surface transport promotion, retained renderer work, focus-scroll convergence, 4,894 checks. |
| DLV-254 | Physically accepted and integrated through `57fb469`: optional widget-switch animation defaults Off; cold and warm Off paths snap to exact destination; explicit On remains animated. |
| DLV-253 | Physically accepted and integrated through `91177ec`: Games & Apps retains resolved state, cold entry starts first row, in-session focus restores, 65/65. |

Do not mark the continuing delivery goal complete. Continue until the user
pauses/replaces it or all useful lanes are genuinely blocked. Never push.
