# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through DLV-262 is preserved in the
[2026-08-16 21:40 snapshot](history/delivery-plan/2026-08-16T21-40-35-07-00.md).
Earlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are
evidence only; this file is the sole authority for current work.

## Current baseline

- Accepted production/test integration baseline on main is `a552cbf`. Its
  prior `cd83b3a` baseline integrates DLV-258 as `e5e5643`, DLV-259 as
  `45d75cf`, DLV-260 production/residue work as `74c6ca1`, and the accepted
  test-only Phase B follow-ups as `7297197` plus `1ff96ba`. DLV-264 is
  integrated as production `7cdcc31` plus focused tests `15a26b9`. DLV-266 is
  integrated as accepted production `795e24d` plus focused tests `cd83b3a`.
  DLV-267 is integrated through main `a552cbf` as accepted production commits
  `3f1a09e` plus `7e46f7e` and focused test follow-up `d9c186b`.
- Exact accepted production PID 17212 was built from DLV-264 implementation
  commit `75f1c96`, with SHA-256
  `502720EA82F5764CCD52DD36036D18EF8531D73044063D600568357305692737`.
  Main contains that accepted production tree. PID 17212 was gracefully closed
  after exact path verification to stage the unaccepted cumulative DLV-266
  physical candidate described below.
- DLV-266 production commit `7235849` is reconciled with current planner main
  by clean merge `510e01c`. Independent source review found one existing
  activation channel, window/session owner, placement/composition owner, and
  bounded foreground-acquisition path. Its tests-skipped Release is visibly
  running as unaccepted PID 40292 with SHA-256
  `8AD2AC19CF2E5FE6D27F4EC4F3CE3DA243E4F908A8D7E63B6D8438DFCBAA1847`.
  Fresh startup elected one production process owner, initialized
  DirectComposition, and logged no startup error/failure/rejection. The user
  physically accepted Guide open/close, already-visible second invocation, and
  external-foreground activation on 2026-08-18. Focused follow-up `d085cd1`
  passed 109 transition checks, 17 foreground/input ownership checks, and 32
  process-owner checks. Independent review accepted the two-test-file diff and
  integrated the chain through main `cd83b3a`. PID 40292 was later gracefully
  closed through its exact owner window to stage DLV-265.
- DLV-265 production/docs commit `869dc7d` is reconciled with current planner
  main by clean merge `b52c07e`. Independent source review confirmed that the
  Spotify package reuses the bounded host text-entry contract, validates and
  atomically writes only the public Client ID, and deletes the prior OAuth
  refresh credential before activating a replacement ID. Its coherent
  tests-skipped Release has OverlayHost SHA-256
  `4429E83E747BD345EED867CB3B5346A371B9823296E4C2828772C152E603D032`;
  Spotify package 0.3.1 has SHA-256
  `65EF8BE722CE26122BCAA93EAC28D58B9FEE620BD140862976D6543E2F32C33E`.
  Reconciliation merge `47aedbf` combines that unchanged candidate with
  accepted main `de87693`. The exact tests-skipped Release is visibly running
  as unaccepted PID 48768 with SHA-256
  `F987126966CA83919F3168F4EC737696BF445FFBFAA08EE4C079FEBB57BC9C85`.
  Exact Spotify package 0.3.1 has SHA-256
  `CF34807C746AAB07F68C5EA10719423CAD5E42931DA16DF8EBE3E096B6A62907`;
  it is installed, selected, and enabled. Fresh startup elected one production
  process owner, initialized DirectComposition, and logged no startup issue.
  Physical review rejected this candidate on 2026-08-18: the existing Setup
  page is reachable while unconfigured or disconnected, but the configured
  Ready surface exposes no controller-reachable Setup action. The running log
  confirms the user's widget is in that Ready state, so an existing Client ID
  incorrectly hides the only replacement route. Keep the user's configuration
  intact. No DLV-265 tests or integration are authorized before a corrected
  production-only candidate is physically accepted.
  Correction commit `672c8c1`, based on clean reconciliation `920b78a`, adds
  one Ready-navigation Setup button that reuses the existing setup action and
  increments the immutable package to 0.3.2. Independent source review found
  no configuration, credential, PKCE, shared-host, shortcut, or existing
  destination change. The coherent tests-skipped OverlayHost has SHA-256
  `2E708ED155069E5D6C433C00DB98151B67908A6BE1E4B2F9CD7F9D2E1B73F9B0`;
  Spotify 0.3.2 has SHA-256
  `63609CB6C0FECDE87DC7B25487E9F71AA38CC461F3F1D734749CEB9D57B7B01B`.
  Version 0.3.2 is installed, selected, and enabled without changing the
  existing Client ID. Corrected candidate PID 75884 is visibly running; fresh
  startup elected one process owner, activated DirectComposition, and logged no
  startup issue. Physical review then rejected 0.3.2: activating Setup at
  07:15:43 produced `worker_request_failed` before any configuration or Spotify
  operation. The package passed its 128-character Client-ID backend maximum to
  `UI.TextEntry`, whose public protocol maximum is 96, so element construction
  threw `ArgumentOutOfRangeException` for `maximumLength` and rejected the
  snapshot. The existing Client ID remains unchanged. Correct this package-
  owned limit mismatch in a new immutable version; do not weaken the shared
  text-entry bound, reset configuration, run tests, or integrate before another
  physical verdict.
  Correction commit `13bd971`, based on clean reconciliation `5fd1a06`, adds a
  distinct 96-character package input bound while leaving the 128-character
  backend validator and every shared contract unchanged. Independent review
  accepted the six-file scope for another physical candidate. The coherent
  tests-skipped OverlayHost has SHA-256
  `A8530B3CEA1357ECFBFF0C349E8942B7C27B8DD1DC1748850373FD31EB017A37`;
  Spotify 0.3.3 has SHA-256
  `C8367B13A82996F5FE17AC2D4EEB44AB31DD860EAE30E6DB04D0B66FAB534A4E`.
  Version 0.3.3 is installed, selected, and enabled with the existing Client ID
  verified unchanged. PID 75884 closed normally through its exact owner window;
  corrected candidate PID 81628 is visibly running. Fresh startup elected one
  process owner, activated DirectComposition, and logged no startup issue.
  Physical review then exposed a separate visible renderer defect while a song
  is playing: repeated Spotify updates make the widget content move a few
  pixels vertically. The PID 81628 trace keeps the outer content window fixed
  at `1947,541,1225,631`, the logical shell/body fixed at `980x504.8`, the
  presented extent fixed at `980x505`, and the guide/tray chrome fixed. The same
  trace alternates full frames with bounded paint-only damage at
  `155.2,235.2,806.4,76.0`. Treat this as DLV-267 renderer work, not Spotify
  setup reflow. Keep 0.3.3, the Client ID, and all account state intact; do not
  integrate or add DLV-265 tests until a corrected cumulative candidate is
  physically accepted. On 2026-08-18 the user saved this exact DLV-265 state and
  deferred its remaining configured-Ready Setup/cancel verdict, tests, review,
  and integration until immediately after accepted DLV-271. Preserve clean
  widgets-lane tip `13bd971`, reconciliation `5fd1a06`, installed Spotify 0.3.3,
  and the user's unchanged Client ID/account state; do not resume DLV-265 early.
- DLV-267 production commits `3f1a09e` plus compiler-only correction `7e46f7e`
  normalize the DirectComposition draw boundary to one physical-pixel raster
  space while retaining the existing logical scene, incremental renderer, and
  composition owner. Independent review accepted the two-file production
  scope (`OverlayCompositionSurface.h`, `main.cpp`). Its coherent tests-skipped
  Release built successfully with OverlayHost SHA-256
  `A56A8656030F41CE61FAFFBD3A544E41937718AEC46E55E3BA49C6234CEC94AB`.
  PID 81628 was first surfaced through authenticated `--show`, then closed
  normally through its exact verified `WidgetRail.OverlayHost` HWND. Corrected
  PID 69904 is visibly running from the exact platform-lane artifact. Fresh
  startup elected one production owner, activated DirectComposition, and logged
  no startup error. The user physically accepted stationary Spotify playback
  updates on 2026-08-18. The exact PID 69904 session contains 422 Spotify paint
  records, 97 bounded composition commits, and 430 raster-origin records with
  zero nonzero origin errors and no real error/failure/rejection line. Focused
  Focused follow-up `d9c186b` adds 12 deterministic full/bounded mapping checks
  across four scale profiles; the exact OverlayChrome suite passed 131/131
  within its 20-second bound. Independent review accepted the three-file
  cumulative scope and integrated it through main `a552cbf`. PID 69904 is
  intentionally retained because the post-verdict delta is tests and reviewer
  documents only.
- DLV-264 production candidate `75f1c96` changed only
  `WidgetSessionCoordinator.{h,cpp}`. Its coherent tests-skipped Release has
  SHA-256 `502720EA82F5764CCD52DD36036D18EF8531D73044063D600568357305692737`
  and was physically accepted as PID 17212 after repeated rapid Now Playing ->
  Settings -> Now Playing switching on 2026-08-18. The exact session retained
  visible lifecycle authority and valid checkpoints without publishing a
  hidden/suspended failure. Test follow-up `fb4fc85` passed 20 coordinator
  scenarios, OverlayState, WidgetLifecycle, and 305 action-feedback checks.
  Independent review accepted the cumulative chain and integrated it through
  main `15a26b9`; PID 17212 was later gracefully closed to stage DLV-266.
- DLV-263 acceptance evidence: zero logged error/failure/stale/timeout/rejection
  lines; exact-anchor and retained-overlap admissions; focus remained inside
  the viewport; 33 frames averaged 26.9 ms, one reached 52.0 ms, none exceeded
  100 ms; focused renderer coverage passed 4,915 checks.
- DLV-261 passes Platform Settings 18/18 after correcting only two stale
  button-scale expectations. DLV-262 passes Widget Bridge 90/90 after a
  serialized dependency rebuild and two exact stale-reference corrections.
- DLV-260 is accepted and integrated. Phase A was physically accepted on PID
  33088. Phase B replaced 214 Release-disabled catalog assertions with 215
  always-on checks, passed Catalog, Wrail 65/65, Runtime 77/77 in the one Tier-3
  run, and corrected the directly exposed renamed configuration ambiguity
  fixture to 5/5 without rerunning Tier 3. The retained Tier-3 result is 14
  passed steps and one now-dispositioned stale-fixture failure.
- DLV-248 remains deliberately deferred by the user.
- The user selected the new display identity **WidgetRail** and tagline
  **WidgetRail — a controller-first widget platform for Windows.** On
  2026-08-17 the user approved all four remaining DLV-257 naming decisions.
  The identity contract is frozen. The user subsequently approved a clean
  local-state break: WidgetRail uses only `%LOCALAPPDATA%\WidgetRail`; it does
  not read or migrate the old root or carry compatibility code. DLV-258 through
  DLV-260 are accepted and integrated.

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
- Input, focus, free-scroll, and re-entry decisions bind to one fully admitted
  interaction generation. The prior committed generation remains authoritative
  until its replacement is complete; an ordinary same-widget refresh must not
  expose a temporary interaction-authority gap or let rendering reacquire focus.
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
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-268 root-Scroll correction `e46af735` is source-reviewed and visibly running tests-skipped as PID 85924. Await the renewed Spotify nested/ancestor free-scroll verdict; preserve the three uncommitted focused test files and do not rerun them, integrate, or start later work before acceptance. After accepted correction and focused-test review/integration, DLV-272 precedes DLV-269. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | DLV-265 is saved at clean tip `13bd971` plus reconciliation `5fd1a06` and explicitly deferred until immediately after DLV-271. Preserve installed Spotify 0.3.3 and the unchanged Client ID/account state; do not resume, test, integrate, reinstall, or reset it early. DLV-270 follows accepted DLV-265; DLV-248 remains deferred. |

DLV-257 is closed. Its approved identity decisions and evidence are preserved
in `docs/widgetrail-identity-migration-contract.md` and the linked delivery-plan
snapshot; it is not active implementation authority.

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

## Accepted integrated deliverable — DLV-260: audit and accept complete rebrand

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
owner, admitted Settings, and logged no startup error. The user physically
accepted this exact current Release on 2026-08-17. The prior candidate PID
34364 later accepted an authenticated `Show` activation while its logical and
Win32 visibility state still treated the overlay as already visible, even
though the user could not see it. The host reapplied placement but did not
produce an observable fresh foreground transition; the confirmation diagnostic
is edge-triggered, so its absence alone does not prove foreground acquisition
was skipped. Restarting the same artifact recreated the window/presentation
state, restored foreground activation, and loaded all seven tray entries. The
earlier DirectComposition fallback is not a demonstrated cause because many
successful open/close transitions followed it. Await the user's visible verdict
before any test repair, Tier 3, or integration; retain the reopening observation
and its presentation-state observability gap unless the user confirms it was
environmental. Phase B commit `f1dd235` replaced 214 Release-disabled
assertions with 215 always-on checks and corrected only stale catalog fixture
indices/appearance data. Catalog and Wrail 65/65 passed; the one Tier-3 run at
that exact commit passed Runtime 77/77 and 14 total steps, then exposed a stale
renamed configuration ambiguity fixture. Correction `473f95f` models two valid
owning dotted publishers and passes the focused configuration route 5/5; Tier 3
was proportionally not rerun. The accepted chain is integrated through main
`1ff96ba` while PID 33088 is retained under the tests/docs-only no-relaunch
rule.

Stop for unresolved old active identity, external Store/repository/domain
action, credential need, destructive cleanup outside validated generated
outputs, any old-root access, unexplained package residue, substantial conflict,
or legal/product name uncertainty. Never push or publish.

## Accepted integrated widgets deliverable — DLV-264: prevent stale hidden-snapshot failure publication

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

Candidate result: merge `f6fcf6c` reconciled the widgets lane to accepted main
with a byte-identical tree, then production commit `75f1c96` changed only the
existing session coordinator. Source review confirms lifecycle-stamped snapshot
requests, bounded supersession of mismatched queued/in-flight snapshots, typed
wrong-lifecycle rejection, and retention of the last valid checkpoint. The
coherent tests-skipped Release is visibly running as PID 17212 with SHA-256
`502720EA82F5764CCD52DD36036D18EF8531D73044063D600568357305692737`.
The user physically accepted repeated rapid Now Playing -> Settings -> Now
Playing switching on 2026-08-18. Exact session traces kept the returning widget
on visible lifecycle requests, retained current/valid presentations, and
contained no persistent hidden/suspended failure. Test-only follow-up `fb4fc85`
adds deterministic lifecycle supersession, cancellation, wrong-lifecycle
rejection, and checkpoint-retention coverage. The focused group passed 20
coordinator scenarios, OverlayState, WidgetLifecycle, and 305 action-feedback
checks. Independent review accepted the cumulative commits and integrated them
as main production `7cdcc31` plus tests `15a26b9`; PID 17212 is intentionally
retained because the post-verdict delta is tests and reviewer documents only.

Stop for a protocol redesign, renderer changes, loss of authoritative provider
failure reporting, destructive state action, substantial conflict, or evidence
that the incident has a different owner. Never push.

## Saved later widgets deliverable — DLV-265: controller-first Spotify onboarding

Owner/baseline: widgets lane from accepted main `15a26b9`, containing the
complete DLV-258/DLV-259/DLV-260 rename and accepted DLV-264. Keep the work inside the
Spotify Community full-trust package and the existing public `UI.TextEntry`
contract; do not add service-specific behavior to WidgetSdk, WidgetProtocol,
WidgetBridge, PlatformBroker, OverlayHost, or Settings.

Current scheduling override: preserve clean production-only correction tip
`13bd971`, reconciliation `5fd1a06`, immutable installed Spotify 0.3.3, and the
user's existing Client ID/account state. Do not resume the configured-Ready
Setup/cancel verdict, author tests, integrate, reinstall, replace configuration,
or reset state until DLV-271 is accepted and integrated. At that boundary the
planner must supply current main to the widgets lane and the lane must reconcile
the saved correction without rewriting it before continuing physical-first
acceptance.

Replace the terminal-only Client ID setup route with an in-widget,
controller-first flow. The setup page must explain the Spotify developer-app
step, provide package-owned actions to open the developer dashboard and copy
the exact non-secret redirect URI, and let the user enter or replace the public
Client ID through the existing bounded host text-entry modal. The committed
value is validated by the Spotify application owner, saved atomically to the
existing package-scoped non-secret WidgetRail configuration store, and followed
by a fresh configuration check. Changing Client ID must retain the existing
fail-closed behavior that removes an OAuth refresh credential belonging to the
old client before the new identity becomes active.

The Spotify Client ID is public configuration, not a secret. Never request or
store a Spotify Client Secret. Browser authorization continues to use PKCE;
actual refresh credentials remain in the package-owned Windows Credential
Manager vault and never enter snapshots, ordinary text-entry state, logs, or
the non-secret JSON configuration document. The `wrail config` command may
remain a documented developer/diagnostic route, but the product UI must not
require a terminal, repository checkout, source directory, or copyable command
to complete ordinary setup. Do not create a new generic clipboard, secret-
entry, configuration, or credential protocol in this milestone.

Acceptance follows physical-first ordering. Build one coherent Release and let
the user complete setup from the overlay using controller plus keyboard text
entry, with no terminal command. Verify the dashboard-open and redirect-copy actions,
valid and invalid Client IDs, replacement behavior, immediate configured-state
refresh, browser PKCE connection, cancellation, and readable controller focus.
Only after the user's visible verdict add focused Spotify presentation/action,
configuration-write, old-credential invalidation, and failure/cancellation
tests. No Tier 3 is required unless the assignment changes a shared boundary.

Stop for a new shared protocol or capability, a request for Client Secret,
Credential Manager migration, third-party authentication needed for automated
evidence, provider-dashboard automation, substantial conflict, or behavior
outside the Spotify Community package. Never push.

Physical rejection and bounded correction: package 0.3.1 exposes
`spotify.setup.open` only from the Unconfigured and Disconnected surfaces.
When a valid Client ID and account are already active, the Ready surface has no
controller-reachable Setup or Change Client ID action, contradicting the
required replacement behavior. Preserve the current Client ID and account
state. Reuse the existing setup route and text-entry flow from a visible
configured-state action, retain all existing Ready navigation and shortcuts,
and publish a new immutable package version for another production-only
physical candidate. Do not reset configuration, duplicate the setup UI, add a
shared settings surface, run tests, or integrate before the user's verdict.

Correction candidate status: commit `672c8c1` changes one Ready presentation
route plus immutable version/public documentation. Spotify 0.3.2 is installed,
selected, and enabled with the prior Client ID preserved. Tests-skipped PID
75884 exposed that physical activation of Setup was rejected because the
package supplied maximum length 128 to a text-entry contract bounded at 96.
Immutable 0.3.3 correction `13bd971` now uses a package-owned 96-character
input bound while retaining the stricter backend validation as defense in
depth. It is selected in visible PID 81628. Physically verify that Setup opens
and cancels in configured Ready wide and compact layouts and leaves Player,
Queue, Playlists, Devices, account state, and existing shortcuts intact. Do not
test replacement with a different Client ID unless the user chooses to perform
that account action.

## Accepted integrated platform deliverable — DLV-267: identical scene origin for bounded updates

Owner/baseline: platform lane from accepted main `cd83b3a`. The installed
Spotify Community package 0.3.3 remains the physical reproducer and must not be
modified, reinstalled, reset, or used as the location for a host-renderer fix.

Correct the visible few-pixel vertical movement that occurs while Spotify is
playing and publishing progress updates. Current PID 81628 evidence rules out
window or shell placement: content HWND, logical body/viewport, desired and
presented extent, fixed guide, and tray coordinates remain unchanged. The
renderer alternates full frames with bounded paint-only damage for the player
strip. Establish whether the difference is in DirectComposition requested-
area/backing-atlas mapping or retained declarative paint coordinates, then make
full and bounded updates rasterize the same logical scene origin at 125% DPI
with interface scale 1.0 and animations Off. The correction must be generic to
the existing composition/declarative renderer authority; do not add Spotify-
specific host behavior, suppress progress publications, force every update to
a full frame as a permanent workaround, or create another placement owner.

Follow physical-first ordering. Add only bounded diagnostics needed to prove
the two update paths, build a coherent tests-skipped Release, and return the
exact commit, artifact path, hash, changed-file scope, and reproduction
evidence to the planner. The planner will launch it against the already
installed 0.3.3 package. Only after the user confirms the movement is gone may
focused mapping/renderer coverage be added and reviewed. Stop for a protocol or
snapshot redesign, widget-package changes, a second composition authority,
substantial conflict, or evidence that the outer surface really moves. Never
push.

Candidate status: commits `3f1a09e` and `7e46f7e` change only
`OverlayCompositionSurface.h` and `main.cpp`. The tests-skipped Release built
successfully and exact PID 69904 is visibly running with OverlayHost SHA-256
`A56A8656030F41CE61FAFFBD3A544E41937718AEC46E55E3BA49C6234CEC94AB`.
The user physically accepted stationary Spotify playback updates on 2026-08-18.
The exact PID 69904 trace contains 422 Spotify paints, 97 bounded composition
commits, and 430 full/bounded raster-origin records at 125% DPI; every record
reports `raster-origin-error=0,0`, and the session contains no real error,
failure, or rejection line. Add focused deterministic coverage proving that
full and bounded DirectComposition requests share one raster space and scene
origin across the supported DPI/interface-scale mapping. Focused test-only
follow-up `d9c186b` adds 12 checks across 100%, 125%, 150%, and combined
fractional scaling; OverlayChrome passed 131/131 within its 20-second bound.
Independent review accepted the cumulative chain and integrated it through main
`a552cbf`. PID 69904 remains the accepted running candidate under the
tests/docs-only no-relaunch rule.

## Assigned platform deliverable — DLV-268: right-stick free scrolling and focus re-entry

Owner/baseline: platform lane after accepted DLV-267 production and focused
tests are integrated. Keep the feature inside the existing controller-input,
declarative Scroll, retained offset, clipping, focus graph, and accessibility
owners. Do not add widget-specific handling, a second focus/scroll authority,
or a public snapshot/protocol change.

For every host-rendered scrollable interface, use the right analog stick to
scroll the active viewport quickly without changing the selected/focused
element. Apply a dead zone and bounded continuous rate proportional to stick
deflection so a held full deflection traverses long collections quickly while
small intentional movement remains controllable. Vertical stick movement owns
vertical Scroll nodes; horizontal movement owns horizontal Scroll nodes. For a
nested surface, the deepest eligible scroll ancestor of the current focus owns
the gesture. If that ancestor cannot scroll farther in the requested direction,
fall back only through the existing ancestor chain; do not move focus or route
the gesture to widget actions.

When right-stick scrolling actually changes an offset, retain semantic focus
on its prior element, clip any offscreen focus visual normally, and mark only
that scroll viewport for one-time focus re-entry. Suppress focused-descendant
follow while the right stick is moving. Returning the stick to its dead zone
ends free scrolling but must not immediately re-enable focus-follow, because
the old offscreen focus would pull the viewport back to its starting position.
Instead, retain a pending re-entry state without moving focus or viewport. The
next directional D-pad or left-stick navigation event is consumed to move focus
to the first fully visible enabled focus target in that viewport: topmost for a
vertical surface and leading-most for a horizontal surface. If no target is
fully visible, use the first partially visible enabled target. That successful
re-entry clears the pending state and restores ordinary directional navigation
and focus-follow. Clear pending re-entry without moving focus when the
view/widget/input scope changes, the scroll container disappears, a pointer/UIA
action establishes focus, or no offset change occurred. Do not let provider
refresh, responsive reflow, collection-anchor reconciliation, or an unrelated
nested viewport redirect the pending target.

Acceptance follows physical-first ordering. Implement production behavior and
bounded diagnostics, source-review the generic ownership/lifecycle paths, and
build one coherent tests-skipped Release. The user verifies fast vertical and
horizontal scrolling, focus stationarity during the right-stick gesture,
one-event re-entry at the visible leading item, ordinary navigation afterward,
nested viewport ownership, scroll boundaries, and compact/wide layouts. Only
after the user's visible/controller verdict add focused deterministic input,
dead-zone/rate, retained-offset, nested-owner, re-entry, invalidation, and
accessibility coverage. No Tier 3 is required unless the implementation changes
a shared ABI or protocol.

Stop for a required public widget contract change, a second input/focus/scroll
owner, ambiguous nested-surface authority that the existing ancestor graph
cannot resolve, undocumented controller APIs, substantial conflict, or
evidence that a non-host-rendered application surface is in scope. Never push.

Physical acceptance: earlier tips `63105f7`, `0e558b1`, and `9d5a236` were
rejected before tests. Correction `b217358` preserves only the matching binding
and focus-follow suppression through `RefreshRetained`, keeps inert semantics
non-actionable, and revalidates the next current snapshot. The exact tests-
skipped artifact (SHA-256 `01802A0E114C32EB15568B9450D3A17D946EF4028E797443F7B111DE070A8853`)
is accepted by the user on PID 71216. The physical session recorded 13 scroll
starts/re-entries and 52 retained-refresh deferrals paired with 52 restorations,
with zero missing/stale/render/geometry authority clear and zero real issue
lines. Freeze production and proceed only to focused post-verdict tests.

Post-verdict coverage passed ControllerNavigation 122/122 and FocusNavigation
54/54, then stopped red because the root-boundary `BuildLocalLayout` fallback
enabled focused-descendant follow and replaced a planned 264-DIP ancestor
offset with 0. One-line correction `e46af735` propagates the existing suppression
policy through that full-layout fallback; independent source review accepted
its generic scope and unchanged ordinary focus-follow semantics. A coherent
isolated Release built from the exact commit with 22/22 required runtime files,
OverlayHost SHA-256
`1B8902910799E743F76F93530B8E247188C2FEC4973F243E242CB2CBA52C84F5`.
The prior PID 71216 exited normally through its exact owner HWND; unaccepted
corrected PID 85924 is visibly running after clean DirectComposition startup
and Settings admission. Await the renewed Spotify nested/ancestor free-scroll
verdict before rerunning or committing the preserved focused tests.

## Ready platform deliverable — DLV-272: extract committed widget interaction session

Owner/baseline: platform lane after DLV-268 is physically accepted, focused-
tested, and integrated. Before DLV-269, extract the existing focus, free-scroll,
pending re-entry, slider, and pressed-presentation state into one
`WidgetInteractionSession`; provide a before/after field, method, and authority
map. It accepts immutable admitted semantics, runtime/presentation generation,
viewport/focus geometry, current scroll authority, and time, and returns typed
focus, follow-suppression, scroll/re-entry, presentation-override, damage, and
widget-action requests. `OverlayApp` retains controller polling, lifecycle,
bridge dispatch, text-entry authorization, retained offset mutation, and final
action arbitration. Add no second focus tree, scroll owner, cache, or coordinator.

This is behavior-preserving hotspot reduction except for closing any proven
authority gap required to preserve already accepted DLV-268 behavior. Follow
physical-first ordering: source review, one tests-skipped Release, then the
user verifies Games & Apps and live-refreshing Spotify right-stick scrolling,
stationary focus, one-event re-entry, sliders/pressed feedback, route changes,
hide/show, and restart. After acceptance run focused focus-navigation, widget-
surface-focus, slider, pressed, text-entry, retained-refresh, stale-generation,
and invalidation coverage. No Tier 3 or public protocol change.

Stop for another mutable-authority cluster, a back-reference/service-locator
design, changed widget contract, new HWND/input/render owner, broad `main.cpp`
rewrite, substantial conflict, or behavior beyond the named invariants. Never push.

## Ready platform deliverable — DLV-269: viewport-driven paged-scroll prefetch

Owner/baseline: platform lane only after DLV-272 is physically accepted,
focused-tested, and integrated on planner main. This is a generic
native-host paged-Scroll correction using the existing public pagination action
IDs, threshold, retained offset, rendered geometry, focus graph, and bridge
action route. Spotify playlists and playlist tracks are the required physical
proof, not a source of package-specific host behavior.

Replace focused-row-driven adjacent-page admission with viewport-driven
prefetch. A paginated Scroll must request its before/after page when its rendered
visible range reaches the authored threshold, regardless of whether the offset
was changed by right stick, pointer/UIA scrolling, D-pad/left-stick focus follow,
or collection-anchor reconciliation. D-pad and left-stick navigation must never
be consumed merely to start, join, or wait for an adjacent load. Preserve the
current semantic focus and scroll anchor while an ordinary background prefetch
is pending; do not force focus to the first item in the arriving page. If the
user reaches a still-unloaded edge, retain the current valid focus and offset
until content arrives, then let the next ordinary navigation event proceed.

Keep admission single-flight and bounded per exact Scroll/action/cursor edge.
Repeated layout, paint, focus, or right-stick frames at the same edge must not
emit duplicate worker actions, IPC refresh loops, or unbounded retries. A
successful append/prepend, cursor change, movement away from the threshold,
route change, widget/input-scope change, or terminal failure must update or
clear that edge authority deterministically. Retain the existing widget-owned
load/error state and the host's focus, scroll, accessibility, renderer, and
action authorities. Do not introduce polling, service-specific branches, a
second collection model, or true presentation-tree virtualization in this
milestone.

Acceptance follows physical-first ordering. Build a coherent tests-skipped
Release with bounded diagnostics for scroll ID, visible item range, direction,
edge/cursor authority, admission, suppression, and completion. The user must
fast-scroll long Spotify playlist and playlist-track surfaces with the right
stick across multiple page boundaries, then verify uninterrupted D-pad/left-
stick navigation, stable focus/viewport position, backward paging, compact and
wide layouts, nested-scroll ownership, and no skipped or forced-focus row. Only
after that verdict add focused deterministic coverage for viewport thresholds,
all scroll input sources, in-flight deduplication, success/error/retry clearing,
route/scope invalidation, retained anchors, accessibility, and non-consumed
directional input. Measure adjacent-action count and input-to-visible-page
latency before and after; no Tier 3 is required.

Stop for a required public SDK/protocol change, inability to derive a stable
visible range from the existing renderer result, a second scroll/focus/action
owner, widget-specific native behavior, destructive state action, substantial
conflict, or missing accepted DLV-268/DLV-272 baseline. Never push.

## Ready widgets deliverable — DLV-270: Spotify collection paging efficiency

Owner/baseline: widgets lane after saved DLV-265 is resumed following DLV-271,
then physically accepted, focused-tested, and integrated on planner main. Keep
all provider calls, page-size/retention policy, queue parsing, package
diagnostics, and immutable package-version changes inside the Spotify Community
full-trust package. Reuse the accepted generic cursor resource, viewport-
prefetch, and virtualized-window contracts; do not add Spotify behavior to
WidgetSdk, WidgetProtocol, WidgetBridge, PlatformBroker, or OverlayHost.

Remove avoidable provider work from Playlists and playlist detail. Load and
validate selected-playlist metadata once per exact selection/configuration
generation, then fetch each adjacent track page through only the paged `/items`
request. Preserve cancellation, latest-generation authority, stale-result
rejection, duplicate-occurrence identity, provider correction, retry, and
bounded error copy. Measure the current 12-item page/24-item retained window
against one or more bounded candidates no larger than Spotify's documented
collection limit. Select the smallest measured page and retention settings that
avoid visible boundary stalls without materially increasing snapshot size,
layout/render work, decoded artwork, worker memory, or provider calls. Do not
increase a constant merely because a larger value is permitted.

Reconcile the Queue's current 50-item cursor-page declaration with its 100-item
parser ceiling into one explicit bounded unpaginated queue contract. The same
bound must govern parsing, cursor-resource admission, presentation count,
truncation/error behavior, tests, and diagnostics; no response accepted by the
parser may later be rejected solely by a smaller internal page-size setting.
Do not invent queue cursors that Spotify does not expose, poll the Queue on the
playback-progress timer, silently truncate without the existing explicit signal,
or refresh an unchanged collection into avoidable tree churn.

Acceptance follows physical-first ordering because this is package-owned
visible latency work without a shared contract change. Build and install one
new immutable Spotify package candidate. Before tests, retain bounded request-
count and timing evidence showing one playlist-metadata request per selection,
one items request per adjacent page, no duplicate edge load, the chosen page/
retention window and snapshot-size effect, and consistent Queue admission at
its declared bound. The user must traverse long playlist and playlist-track
collections forward/backward with right stick and D-pad in compact and wide
layouts and confirm timely rows, stable focus/anchor behavior, artwork, playback
actions, refresh, and error recovery. Only after the verdict add focused
provider-call, cursor, cancellation/stale-generation, bound, truncation,
snapshot-size, and presentation tests. No Tier 3 is required.

Stop for Spotify authentication/account action needed for automated evidence,
a provider API or rate-limit ambiguity that changes the product contract, a
shared host/SDK/protocol change, destructive configuration or credential action,
unbounded retention, substantial conflict, or missing accepted DLV-265/DLV-271
baseline. Never push.

## Ready platform deliverable — DLV-273: extract committed widget content presenter

Owner/baseline: platform lane after DLV-269 is accepted and integrated and
before DLV-271 changes collection presentation. Extract the declarative widget-
body painter, renderer/cache lifetime, committed visual checkpoint, incremental
damage plan, last render result, and declarative-motion state into one
`WidgetContentPresenter`, with a before/after authority map. It receives one
immutable, already-admitted `WidgetPaintFrame` containing exact presentation
and interaction generation, geometry, appearance, focus/slider/pressed
overrides, retained offset, and layer target; it returns one typed paint outcome
containing render result, accessibility regions, committed checkpoint, motion,
damage, and diagnostics. Keep lifecycle, session admission, focus mutation,
input, chrome, placement, HWND, and device-recovery arbitration in their
existing owners.

The previous complete frame remains authoritative until the replacement frame
is admitted and committed atomically; no ordinary same-widget refresh may
publish half-old geometry/semantics or a transient inert interaction gap.
Follow physical-first ordering with Settings, Audio, Games & Apps, and live
Spotify full/bounded updates, device/resource refresh, focus/scroll continuity,
and no flicker/stale pixels. After acceptance run focused renderer, semantic-
churn, shared-geometry, incremental/no-raster, retained/failure-retained,
accessibility-projection, and device-loss coverage. No Tier 3 or protocol change.

Stop for direct `OverlayState`/session/HWND back-references, a graphics service
locator, second render checkpoint, protocol work, interaction-state ownership,
broad `main.cpp` rewrite, substantial conflict, or changed product behavior
beyond atomic committed-frame handoff. Never push.

## Ready serialized deliverable — DLV-271: virtualized collection presentation windows

Owner/baseline: platform lane as the serialized cross-layer lead after DLV-273
is accepted and integrated on main. This is deliberate public
architecture work spanning the generic managed SDK/cursor resource, versioned
protocol and admission, bridge/runtime publication, native semantic/layout/
accessibility/render owners, and directly affected author documentation. No
widgets-lane work may edit those boundaries concurrently. Spotify is one
real consumer; a provider-free large-collection reference scenario is the
deterministic scale proof.

Introduce one generic virtualized collection-window contract so a widget may
retain an application-scale private collection while submitting only a bounded
keyed window around the host viewport. Define explicit stable item identity,
window/cursor authority, known or unknown extent, before/after availability,
estimated versus measured row extent, request generation, stale-window
rejection, and bounded append/prepend/replace semantics. The native host remains
the sole scroll-offset, clipping, focus-follow, navigation, UI Automation,
layout, renderer, and HWND authority; the widget remains the sole private-data
and item-materialization owner. Existing eager Scroll content remains the simple
default. Do not make ordinary small widgets adopt an application framework, let
authors construct raw wire patches, expose provider-specific DTOs, or retain two
simultaneously authoritative semantic trees.

Virtual window shifts must preserve stable focus and collection anchors when
their keys remain available, never silently drop an actionable focused item,
and produce deterministic recovery when provider mutation removes it. Bound
window size, outstanding requests, request rate, cursor history, native nodes,
layout work, accessibility providers, decoded resources, and failure retries.
Off-window content must not be serialized, admitted, laid out, painted, hit-
tested, or represented as a live native accessibility node merely because it
exists in the widget's private collection. Provide accurate scroll range and
virtualized-item accessibility semantics without inventing a second accessibility
tree. Preserve complete-checkpoint fallback and last-valid-window retention on
malformed, stale, failed, or cancelled updates.

Because this changes a shared public protocol, use normal verification ordering,
not pre-test physical-first ordering. First produce a before/after responsibility
map and the smallest versioned contract; then run focused managed/native Tier 1
and the smallest linked Tier 2 boundary group. A provider-free scenario with at
least 10,000 stable variable-content items must prove that serialized snapshot
items, native semantic nodes, layout/paint work, accessibility providers, and
host memory remain proportional to the bounded viewport window rather than the
private collection. Verify right-stick and focus-follow scrolling, viewport
prefetch, forward/backward window shifts, dynamic insert/remove/move, compact/
wide reflow, cancellation, stale/failure retention, restart, and legacy eager-
Scroll coexistence. Then build and launch the coherent Release for the user's
physical long-list verdict. Tier 3 is required only if the final reviewed change
alters the repository verification manifest or a core security boundary beyond
the named protocol.

Stop for an accessibility model that cannot remain accurate while bounded, an
unbounded or author-controlled native allocation, a second semantic/scroll/
focus authority, raw author-authored wire mutations, service-specific core
behavior, silent truncation, incompatibility that requires preserving an unused
pre-release protocol generation, substantial conflict, or missing accepted
DLV-269/DLV-273 baseline. Never push.

## Accepted integrated platform deliverable — DLV-266: reconcile invisible resident Show activation

Owner/baseline: platform lane after reconciling accepted main `15a26b9` onto
the preserved production candidate `7235849`. Keep
the correction inside the existing OverlayHost process owner, window/session,
composition/fallback, placement, and foreground-input authorities. Do not add a
second activation channel, input owner, overlay window, watchdog process, or
product restart workaround.

Correct the reproduced state divergence in which the resident process accepts
an authenticated `Show`, logical surface state and `IsWindowVisible` still say
visible, placement is reapplied, but no overlay is physically observable. Add
typed diagnostics at the existing activation decision for logical surface,
Win32 visibility, foreground ownership result, presentation mode, current
window bounds/z-order inputs, and whether a real show/recovery transition was
performed. Reconcile an already-visible `Show` through one bounded idempotent
path that can restore the accepted presentation and foreground state without
requiring process restart or disturbing unrelated foreground applications.

Do not assume the earlier DirectComposition destination-draw fallback caused
the activation failure: many successful open/close transitions occurred after
that fallback. Preserve the legacy layered-HWND fallback and ordinary
already-visible activation. Do not continuously raise/topmost-poll the window,
steal focus while hidden, force-close another process, or hide a provider/window
failure behind a successful activation result.

Acceptance follows physical-first ordering. Implement production and typed
diagnostics only, source-review the documented Win32/DirectComposition paths,
build one coherent Release, and stop for planner launch/user testing. The user
must verify Guide open/close plus second-invocation `--show` across repeated
visible/hidden and external-foreground transitions. After visible acceptance,
add focused deterministic state-decision, idempotent recovery, failure-reporting,
and no-focus-while-hidden coverage. Inspect the exact candidate log; no Tier 3
unless the implementation changes a shared protocol or process boundary.

Candidate status: the platform lane produced clean production-only commit
`7235849`, changing only `main.cpp`, then reconciled current planner main by
merge `510e01c`. Independent source review found typed before/after diagnostics
and one idempotent visible-recovery path through the existing placement,
composition/fallback, transition, and foreground-input owners. The coherent
tests-skipped Release is visibly running as unaccepted PID 40292 with SHA-256
`8AD2AC19CF2E5FE6D27F4EC4F3CE3DA243E4F908A8D7E63B6D8438DFCBAA1847`.
Its fresh session elected one process owner, initialized DirectComposition, and
logged no startup error/failure/rejection. The user must now verify repeated
Guide open/close, already-visible second-invocation `--show`, and external
foreground transitions. Do not add/run tests or integrate before that verdict.

Stop for inability to distinguish a product defect from external z-order state,
an undocumented Windows API, a new watchdog/process/window/input authority,
destructive process termination, substantial conflict, or evidence that the
failure owner is outside the native host. Never push.

## Ready later — DLV-248: media optimistic command revision reconciliation

Prevent an older provider publication from overwriting a newer host-projected
play/pause command while that command is pending. Use one bounded command
revision/acknowledgement policy inside the existing media widget owner, preserve
authoritative provider correction and timeout/failure rollback, and avoid
special handling in the native host or snapshot protocol. This remains
deliberately deferred by user decision and requires explicit promotion.

## Serialized order

1. DLV-257 through DLV-260 are accepted and integrated through main `1ff96ba`.
2. DLV-264 is accepted and integrated through main `15a26b9`; accepted PID
   17212 was later gracefully closed to stage DLV-266.
3. DLV-266 is accepted, focused-tested, and integrated through main `cd83b3a`.
4. DLV-265 packages 0.3.1 and 0.3.2 are physically rejected. Spotify 0.3.3
   corrects their package defects and remains installed with user state intact,
   but cumulative PID 81628 is rejected for the DLV-267 update-origin defect.
5. DLV-267 is accepted and integrated through main `a552cbf`; retained PID
   69904 contained the accepted production commit without a redundant rebuild
   for its test/docs-only integration, then was gracefully closed to stage the
   DLV-268 physical candidate. DLV-265 remains saved/deferred as ordered below.
6. DLV-268 production through correction `b217358` is physically accepted on
   PID 71216; production is frozen while the platform lane adds focused tests.
7. DLV-272 is Ready immediately after accepted DLV-268 integration: extract one
   committed interaction session before adding more scroll behavior.
8. DLV-269 follows accepted DLV-272: generic viewport-driven, focus-independent
   adjacent prefetch with Spotify as the physical proof.
9. DLV-273 follows accepted DLV-269: extract one atomic committed widget-content
   presenter before virtualized collection architecture.
10. DLV-271 follows accepted DLV-273: bounded virtualized collection windows
   with a provider-free 10,000-item scale proof.
11. DLV-265 remains saved at clean correction tip `13bd971` and resumes in the
   widgets lane immediately after accepted DLV-271 integration for its configured-
   Ready Setup/cancel verdict, focused tests, review, and integration.
12. DLV-270 is Ready for the widgets lane only after resumed DLV-265 is accepted
    and integrated: eliminate redundant Spotify collection requests, measure/
    tune page retention, and reconcile the Queue bound against the accepted
    generic prefetch and virtualization contracts.
13. DLV-248 remains outside this sequence until the user promotes it.

## Manual, external, and blocked evidence

| Item | Blocker / required evidence |
| --- | --- |
| DLV-257 identity | Complete: all four exact mapping decisions approved on 2026-08-17. |
| Microsoft Store name | Partner Center availability/reservation through the user's account; public search is insufficient. |
| Domain | Live registrar/RDAP availability and optional registration through the user's account. |
| Trademark | Similar-mark clearance for related software/services; qualified counsel recommended before public release. |
| GitHub identity | User-selected owner plus repository/organization availability and optional rename/creation. |
| DLV-264 | Complete: physically accepted on PID 17212, focused-tested, and integrated through main `15a26b9`. |
| DLV-265 | Saved/deferred by user until immediately after accepted DLV-271. Preserve clean tip `13bd971`, reconciliation `5fd1a06`, installed Spotify 0.3.3, and the unchanged Client ID/account state; do not resume, test, integrate, reinstall, replace configuration, or reset it early. |
| DLV-267 | Complete: accepted physical evidence on PID 69904, 422 Spotify paints, 97 bounded commits, 430 zero-error raster-origin records, OverlayChrome 131/131, integrated through main `a552cbf`. |
| DLV-268 | Production through `b217358` is physically accepted on PID 71216 with paired retained-refresh defer/restore evidence and no authority clear. Focused tests and integration remain. |
| DLV-272 | Ready after accepted DLV-268 integration; requires a behavior-preserving interaction-session extraction, before/after authority map, tests-skipped physical verdict, then focused lifecycle coverage. |
| DLV-269 | Ready after accepted DLV-272 integration; requires a tests-skipped physical Spotify long-list verdict proving viewport-driven prefetch across right-stick and directional navigation without consumed input or forced focus. |
| DLV-273 | Ready after accepted DLV-269 integration; requires an atomic committed presenter extraction, tests-skipped multi-widget/full-bounded physical verdict, then focused renderer/lifecycle coverage. |
| DLV-270 | Ready after resumed DLV-265 is accepted following DLV-271; requires an immutable Spotify candidate, bounded provider-call/timing and snapshot evidence, then user long-list/Queue physical acceptance before tests. |
| DLV-271 | Ready for the platform lane as serialized cross-layer work after accepted DLV-273; shared protocol work requires focused Tier 1 plus the smallest linked Tier 2 evidence and a bounded 10,000-item reference scenario before physical acceptance. |
| DLV-266 | Complete: physical Guide, already-visible `--show`, and external-foreground activation accepted on PID 40292; focused tests passed and the chain is integrated through main `cd83b3a`. |
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
| DLV-267 | Physically accepted and integrated through `a552cbf`: one physical-pixel DirectComposition raster space for full/bounded updates, 430 zero-error origin records, 131/131 focused checks; exact accepted Release restored as PID 74176 after DLV-268 rejection. |
| DLV-264 | Physically accepted and integrated through `15a26b9`: lifecycle-owned snapshot supersession, stale wrong-lifecycle failure rejection, retained valid checkpoints, 20 coordinator scenarios, OverlayState, WidgetLifecycle, and 305 action-feedback checks; PID 17212 retained. |
| DLV-260 | Physically accepted and integrated through `1ff96ba`: complete active WidgetRail cutover, always-on catalog checks, focused configuration 5/5, retained one-shot Tier-3 evidence with 14 passed steps and the corrected stale-fixture red; PID 33088 retained. |
| DLV-259 | Accepted and integrated as `45d75cf`: complete native WidgetRail identity and fresh-root cutover with no old-root reader or compatibility bridge. |
| DLV-258 | Accepted and integrated as `e5e5643`: complete public managed WidgetRail cutover with focused managed/package/SDK evidence green. |
| DLV-263 | Physically accepted and integrated through `110b42b`: retained-overlap viewport position, exact-anchor behavior, conservative fallback, 4,915 renderer checks, accepted PID 28612 retained. |
| DLV-262 | Test-only integrated as `88b21b1`: exact failure ledger, current dependency graph, unchanged authenticated named-pipe path, Widget Bridge 90/90. |
| DLV-261 | Test-only integrated as `c519df4`: exactly two stale button-scale expectations corrected, Platform Settings 18/18. |
| DLV-256 | Physically accepted and integrated through `b0d53b0`: bounded artwork LRU, measured full-surface transport promotion, retained renderer work, focus-scroll convergence, 4,894 checks. |
| DLV-254 | Physically accepted and integrated through `57fb469`: optional widget-switch animation defaults Off; cold and warm Off paths snap to exact destination; explicit On remains animated. |

Do not mark the continuing delivery goal complete. Continue until the user
pauses/replaces it or all useful lanes are genuinely blocked. Never push.
