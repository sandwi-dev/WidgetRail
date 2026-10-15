# Marketplace, navigation and persistent widget stop

## Acceptance and integration — 2026-10-15

The user accepted all current changes and authorized merging this work to local
`main`. This supersedes the earlier no-merge delivery boundary and pending
physical-acceptance notes below. The YouTube Music 0.3.38 → 0.3.39 settings-update
test was reported working. Candidate/build evidence remains in the relevant
sections; older candidate paths and PIDs are historical. Remote publication and
release are outside this integration request. Main's unrelated
`docs/implementation-agent-goal.md` edit is preserved.


Baseline: `235283dd` (preview.32), branch `codex/marketplace-widget-lifecycle`,
reusing the clean platform-feature-roadmap worktree. Main's unrelated
`docs/implementation-agent-goal.md` edit is preserved. Goal source:
`874cf683-4102-44a9-8e29-cc0431f4fcc3/goal-objective.md`.

## Delivery boundary

Implement all areas below, maintain evidence, review and build/verify/launch a
complete candidate for physical acceptance. No merge, push, release, distribution
installer, application update installation or worktree removal. May restart the
overlay; preserve normal profile, sign-ins and Guide. Synthetic fixtures/isolated
profiles only for automation; no credentials, messages, calls, personal captures
or previously blocked probes. No delegated agent work is authorized.

## Requirements and completion evidence

- [x] Reproduce false Marketplace → Settings intent failure. Cover cold/warm,
  delayed initialization/inspection, cancellation and missing destinations. Fix
  completion semantics and loading presentation without simply extending deadlines
  or suppressing failures. Preserve shared intent authority and existing consumers.
- [x] New installs enabled in Marketplace and Settings; updates preserve enabled,
  stopped, data and permissions; trust review and feature-triggered consent remain.
- [x] Marketplace Browse/Installed/Updates, nonblocking refresh on opening,
  centralized individual and bulk update queue, progress, cancellation, per-item
  outcomes/retry. Reuse verified package infrastructure; no extra repositories.
- [x] Settings All/Built-in/Community list, shared host-resolved icons, reliable
  focus/scroll, no community-only pagination or skipped navigation targets.
- [x] Radial + rail is the sole mode. Remove alternative UI/runtime branches;
  accept old saved preferences without losing other settings. Preserve shortcuts,
  Back precedence and rail behavior; update documentation/examples/tests.
- [x] Host-owned Stop widget closes owned workers, background work, browser/media,
  pop-outs and its pin using existing owners. Other widgets and shared runtimes
  continue. Saved sign-ins/data/permissions/pin geometry survive.
- [x] Persist stop across app/PC restarts. Enabled stopped widgets remain listed,
  render a host-only stopped surface with scoped X Reload, and allocate no widget
  worker/WebView until explicit Start/Reload. No resurrection from selection,
  overlay/app startup, update, intent or background tasks. Disable stays separate.
- [x] Race/failure/persistence/regression coverage, code review, docs, complete
  candidate verification and launch, concise physical checklist.

## Working sequence

Intent navigation first; Marketplace/Settings installation and organization;
radial-only cleanup; persistent stop and lifecycle integration; full qualification.
Preserve deferred startup and resource optimizations throughout.

## Initial source findings

Settings intent delivery is bounded by a two-second worker request deadline.
The original handler waited for initialization and an operation gate, reloaded the
catalog, selects/invalidates the target page, then awaits local-data/uninstall
inspection before returning Accepted. A delayed-inspection fixture reproduced
the deadline failure after the requested page was already selected.
Original host selection published Settings' initial home frame before intent
delivery. Current Stop webapp session closes host browser sessions only; it is
not a generic worker stop and does not provide persistent stopped authority.

## Evidence

Evidence is under `artifacts/marketplace-lifecycle/` in the working checkout.

### Settings intent navigation (implemented and focused checks passed)

- `intent-reproduction.log`: original handler failed the delayed-inspection
  acknowledgement check (87/88). This matches the two-second worker deadline;
  no deadline was extended.
- Settings now accepts valid navigation data promptly and resolves it using an
  Active-lifetime latest-operation. Loading has its own destination scope, Back
  cancels it, and unavailable destinations show an inline error with retry.
  Optional inspection no longer holds the ordinary action gate or acknowledgement.
- Host selection has a reusable before-reveal preparation callback. Intents use
  it to accept the destination and obtain its snapshot before committing the
  incoming surface; passive delivery remains unchanged.
- `intent-tests03.log`: 89/89 Settings tests pass, including delayed inspection,
  cold initialization, Back cancellation, supersession, invalid input and genuine
  missing destinations. `bridge-settings-intent03.log`: real Settings cold/warm
  delivery passes through both pipes with delayed inspection.
- `frontend-intent-build02.log` and `bridge-intent-build04.log`: builds pass with
  zero warnings/errors. Native host validation added in
  `Test-WinUiSettingsNavigation.ps1`; uses an isolated synthetic package catalog,
  records actual committed scopes and checks cold/warm/missing destinations.
  `native-intent04/result.json`: all 10 native checks pass; all three Settings
  commits use `intent.destination`, never the home scope. Cold/warm details,
  genuine missing destination/retry and controller Back are verified. Earlier
  native fixture failures were incorrect action identifiers and incomplete
  synthetic package declarations (intents/icons); these were corrected without
  weakening production admission. `bridge-intent-regression01.log`: 18/18 pass.
- Review also corrected caller cancellation being mislabeled as a selection
  timeout, and synchronized intent-generation reads. Rebuild/regression of those
  final small changes passes in `frontend-install-build01.log`,
  `settings-install-tests02.log` (89/89) and `native-intent05/result.json` (10/10).
  Re-run relevant native coverage after the remaining lifecycle integration.

### Installation policy (in progress)

- Added a shared internal reviewed-install path for Marketplace and local Settings
  installs. A genuinely new compatible ID is enabled after verified publication;
  existing entries/repairs retain their prior policy. Generic public catalog/CLI
  staging remains disabled. Full-trust approval is still mandatory.
- Reviewed updates retain Enabled while changing only ActiveVersion. Saved
  permission/configuration files are untouched. Persisted Stop authority will be
  integrated in the lifecycle area; no stopped-state proof is claimed yet.
- Local completion now uses `installed` instead of the obsolete
  `installed-disabled`; session, host dialog and tests are updated together.
  Refresh failure after durable publication no longer reports install failure.
- First marketplace gate passed all seven behavior checks; the inventory check
  still expected seven entries although the shipped catalog includes Twitch
  (eight). The stale count/required-Twitch assertion is corrected for the next run.
- Final focused gates: `marketplace-install-tests02.log` 8/8,
  `local-install-tests02.log` 5/5, `session-install01.log` 13/13, and
  `settings-install-tests02.log` 89/89. Duplicate import, cancelled/stale review,
  wrong publisher/version/identity, expired review, network cancellation, enabled
  and disabled updates, consent preservation and full-trust approval are covered.

### Marketplace views (in progress)

- Browse / Installed / Updates now use the shared semantic segmented tabs.
  Reopening requests a background refresh instead of inventory-only; an active
  job/review is preserved. Tabs, details and Back remain usable during refresh.
- `marketplace-views-tests03.log`: 6/6 pass, including a deliberately held refresh
  request, navigation to cached details, remembered-card return and a fresh check
  on reactivation. Individual update rows now download, open the exact package
  review modal and install without leaving Updates. Hidden/unopened reviews
  cannot be approved. Native visual/controller qualification remains to do after
  the queue is integrated.
- Installed currently filters the curated inventory. Broader installed inventory
  remains a pending design decision alongside the shared Settings inventory.
  Queue design must keep exact package/trust review, bounded retained downloads,
  cancellation after durable publication, per-item outcomes and retry. Do not
  implement silent approvals or let closing Marketplace strand a widget-owned
  update task. Additional GitHub sources remain excluded.
- Next queue implementation should remain Bridge-owned, reuse the verified
  download/install routines and retain exact reviewed bytes. An aggregate review
  for downloaded updates can preserve one explicit trust decision without silently
  approving future packages. Keep partial failures visible and retry only failed
  entries; cancellation must distinguish untouched work from committed installs.
  The host-owned queue described below is now implemented.

### Update all (implemented; native integration/review still pending)

- New bounded private `download-updates` / `install-updates` commands carry an
  exact package selection and aggregate review token. Broker grants remain
  intrinsic to trusted Marketplace; batch writes require Interactive lifecycle
  and cannot be smuggled through read operations.
- `BridgeWidgetMarketplace.Updates.cs` reuses the single-package checksum,
  identity, compatibility and reviewed-install paths. Downloads/installations
  are sequential host jobs, independent of Marketplace worker polling. A single
  explicit review approves only ready exact bytes; failures remain per-item.
  Retry is a new download/review of unfinished IDs, never a replayed approval.
- Download/review bounds: 16 selected updates, 256 MiB retained archives and
  128 KiB aggregate review metadata. Excess items fail visibly for individual
  retry, without truncating permissions. Maximum manifest permission sets were
  exercised through the actual BrokerJson response limit.
- Cancellation retires approval immediately, preserves committed item results,
  and cancels untouched items. File cleanup is asynchronous; subsequent jobs and
  shutdown await it. Review expiry/baseline changes reject stale approval.
- Regression caught an existing cancellation ordering race: an inline completion
  could be overwritten with `Cancelling…`. The request is now published before
  cancelling, preserving the terminal result.
- UI has Update all, a shared aggregate review starting on Cancel, per-item
  status/results, Retry update and Retry remaining updates. Individual list
  updates use the same batch path with one item. Settings' legacy Marketplace
  page directs active batches to Marketplace instead of pretending to review one
  package. Current-version checks prevent stale batch labels after a newer release.
- `marketplace-batch-tests06.log`: 15/15 service checks pass (mixed trust,
  exact approval/replay, partial failure/retry, cancellation during network and
  after a durable commit, expiry, baseline drift, bounds, cleanup and previous
  individual-package regressions). `marketplace-batch-ui-tests03.log`: 7/7 UI
  checks pass including maximum-sized review rendering and full-access approval.
  `marketplace-broker-tests02.log`: all 80 broker checks pass, including authorized
  batch requests, lifecycle denial and read-operation mutation smuggling.
- `settings-batch-build01.log` compiles the legacy Settings route with zero
  warnings/errors. No native Marketplace view qualification yet. All recorded
  build/test processes have completed; no pending tool handle remains.

### Settings inventory (navigation implemented; SVG resolution pending)

- All / Built-in / Community now share one SDK indexed collection with immutable
  rows and stable package/kind keys. No eager row controls or community pagination.
  List preparation is deferred until Widgets opens. Metadata-only invalidations
  avoid rebuilding rows; content changes preserve query membership and focus.
- Normal row actions use indexed input admission and resolve current package
  identity. Legacy index-based row actions were removed. Return from details
  supplies an exact stable-key focus target, remapped after reorder. Filtering
  retires old leases. Version paging remains; recovery paging has its own named
  state/actions and cannot paginate the normal list.
- The shared Settings row-linker now recognizes indexed collection boundaries.
  Headers/actions stay outside the finite list viewport. Rows currently render
  semantic glyphs; the required shared verified SVG source is still pending.
- `settings-list-tests05.log`: 91/91 pass. Coverage includes real SDK range/input
  admission, All/Built-in/Community membership, stale filtered-row rejection,
  stable return key after reorder, unchanged-refresh query identity, catalog
  recovery, and a 258-row inventory whose parent snapshot stays under 60 nodes.
- Native styled fixture `native-settings-list03` passed navigation but visual
  inspection exposed insufficient visibility assertions. The tightened
  `native-settings-list04/result.json` reproduces an actually focused row below
  the viewport after refresh's toast reduces height from 292.8 to 223.2 DIPs.
- Shared `WidgetIndexedCollectionView` now coalesces an event-driven focus reveal
  after a shrinking native viewport. It preserves focus and corrects only the
  necessary scroll delta, without polling or scheduling another navigation.
  `native-settings-list05/result.json`: 20/20 pass with actual native focus and
  full-row visibility; the row bottom moves from 292.8 to 223.2 with the viewport.
- `indexed-entry01/result.json`: 44/44 existing native entry/replacement checks
  pass after the shared fix. This covers list/grid replacement, deferred input,
  repeated trigger handling, focus parking and explicit departure.
- An additional unfocused-resize assertion in `native-settings-list06` detected
  offset drift while actual focus correctly stayed on Refresh.
  `native-settings-list07` proves native end anchoring: offset increases by the
  exact 69.6-DIP viewport reduction, preserving offset + viewport = 806.4 DIPs;
  host focus-reveal count stays 1. The final assertion now verifies actual focus,
  no host intervention and native end anchoring, instead of incorrectly requiring
  an unchanged numeric offset. No production change was needed for this case.
- Final `native-settings-list08/result.json`: 23/23 pass, including actual native
  focus, complete row visibility, unfocused resize anchoring and no host reveal
  after explicit focus departure. Styled synthetic control images are retained
  beside the results; no desktop/personal-window pixels were captured.

### Next implementation seams

- Existing PackageSvg references resolve within the declaring widget's sealed
  package. Settings cannot simply copy another widget's asset ID. Shared icon
  work needs a host-owned catalog reference/resolver using the existing verified
  icon cache, including disabled installed widgets, without starting their workers
  or giving widgets arbitrary file access. Check descriptor/asset count bounds and
  invalidation when target packages update before choosing the protocol seam.
- Current icon constraints: descriptors admit at most 32 package assets, and
  `WidgetPresentationSession.ResolvePackageIconAsync` requires a current enabled
  target descriptor. Do not stuff an entire installed inventory into Settings'
  own manifest or resolve another widget's bytes under Settings' package digest.
  Management icon references must distinguish built-in vs community copies of
  the same package and stay valid for disabled/stopped widgets without launching
  them. `BridgeCatalogMonitor.LoadTrustedForManagement()` already exposes the
  trusted management inventory. Reuse native icon/tint caches and maintain exact
  content identity, admission and bounded/coalesced demands.

- Marketplace native/inventory completion, Settings SVG icons, radial cleanup and
  persistent stop remain to do. No complete candidate is built yet.

## Earlier handoff before catalog icons and lifecycle

All changes are uncommitted in this worktree. Main still has only the preserved
`docs/implementation-agent-goal.md` edit. No merge/push/release/installers or app
updates were performed. Synthetic native fixtures were closed through their owned
shutdown path. The installed preview.32 app was cleanly closed for the isolated
44-check collection run, then restored hidden with its original executable and
normal profile (PID 35908). Original/relaunch evidence is in
`installed-before-indexed.json` and `installed-restored-after-indexed.json`.
Recheck process identity before any later close/relaunch. All current build/test
handles have completed, and the last synthetic fixture closed cleanly.

### Shared catalog icons (implemented and qualified)

- Protocol 82 adds a bounded catalog source to the existing icon reference. Only
  trusted Settings/Marketplace views can use it; manifest references reject it.
  The bridge resolves only verified presentation icons, without worker startup,
  including disabled built-ins and installed copies with the same package ID.
- Existing session capacity (16 demands, four slots, 128 entries/2 MiB) and native
  SVG/tint renderer are reused. Coalesced loads outlive individual row waits;
  package source/version/digest and catalog revision scope the authority/cache.
- `catalog-icons-bridge02.log`: disabled built-in and installed resolution, stale
  version/digest rejection, source distinction, and authenticated pipe resolution
  with zero workers pass. `catalog-icons-session01.log`: 19/19 tests pass including
  concurrent catalog readers, cancellation, corrupt replies and existing icons.
  `catalog-icons-sdk-tests01.log`: SDK materialization/version/manifest validation
  passes. `catalog-icons-native01.json`: 55 native checks pass, including same
  asset ID in different catalog sources and existing native raster/cache recovery.
  The synthetic native fixture (PID 20640) was closed through its owned UI action.
  No personal-window pixels were captured; the installed app was not restarted.

### Radial + rail cleanup (implementation in validation)

- Removed the switcher enum, persisted model field, Settings chooser/actions and
  host mode branches. Read-only settings load retires the legacy field; the next
  ordinary save drops it permanently while preserving unrelated preferences.
- Retained separate rail and radial entry paths in native fixtures, instead of
  testing two obsolete configuration modes. Menu/reorder/modal precedence remains
  in the shared input router. Corrected an existing Appearance focus link that
  still pointed at the removed switcher control on another page.
- Settings/store regression and native build are in progress. Persistent Stop
  widget and native Marketplace qualification remain outstanding.

### Stop widget implementation and validation

- Host-owned `stopped-widgets.json` holds bounded package IDs with atomic writes,
  cross-process locking and cancellation. It is separate from platform settings
  so rollback does not make an older release reject Guide/appearance preferences.
- The catalog retains stopped entries with `IsStopped` and retires runtime
  authority. Registry admission rejects implicit worker creation/restart; intent
  resolution excludes stopped targets. Only explicit Start clears persisted stop.
  Publication does not rescan packages; stale catalog discovery cannot overwrite
  it. Stop drains pending restarts for that widget before acknowledgement.
- Stopped selection uses neutral host status chrome, Reload and scoped X Reload.
  It has no worker, snapshot or WebView. The single Stop widget command replaces
  browser-session Stop, including the competing Website settings action.
  Cleanup reuses presenter/browser/media/pin retirement with per-widget drains.
- `final-stop-tests01.log`: 3/3 pass after the separate state-file change: real
  keep-alive worker retirement with neighbor preservation, restart persistence,
  explicit Start, in-flight restart and stale/background catalog discovery.
- `native-widget-stop01/result.json`: 30/30 pass for real Settings intents/list
  and host-only stopped chrome, Reload focus/guide, radial return, switching,
  hide/reopen and X Reload. `native-webapp-stop02/result.json`: 5/5 pass with local
  synthetic pages and a canvas stream: pin/pop-out retirement, geometry retention,
  neighbor preservation, no WebView on selection and profile-preserving reload.
  First webapp attempt used the wrong validation flag and had no result; corrected
  to `--validate-webapp-shell` without a production change. Both native fixtures
  predate separating persistence into its own file; final native rerun remains.
- `radial-settings-tests01.log`: 28/28; `radial-widget-tests01.log`: 91/91;
  `radial-policy01.log`: 6/6; `radial-native02.json`: 114/114. First radial fixture
  omitted B release between presses. Corrected full gestures and added explicit
  rail-to-radial Back coverage; production duplicate-press protection is retained.
- Review fixed two stop edge cases: do not revoke unrelated widget input, and
  suspend the outgoing preview renderer when entering stopped chrome. Icon
  authority checks now cover initial establishment and indexed range roots.
- Final registry tests: 18/18; existing/static and management icon bridge cases
  pass. Full presentation-session suite: 342/342, including new icon checks.
- Marketplace Installed intentionally covers curated packages; local packages
  remain in Settings. Help/docs state that scope. No new repository support.

### Qualification checkpoint superseded by completion below

Installed preview.32 (PID 35908) was cleanly closed for native fixtures. All
synthetic shells have been closed through owned shutdown. The normal profile and
sign-ins are untouched; main's unrelated edit is preserved. A complete candidate
has not yet been assembled. Final regression/review and native Marketplace
presentation checks are underway. All work remains uncommitted; no merge, push,
release, installers or app-update installation occurred.

## Autonomous completion

Implementation, review and automated/native qualification are complete. Physical
acceptance remains the user's step. All source changes remain uncommitted on
`codex/marketplace-widget-lifecycle`; nothing was merged or pushed, and no release,
distribution installer, app-update installation or worktree removal was performed.

Final evidence (under `artifacts/marketplace-lifecycle`):

- `final-platform-settings-tests01.log`: 29/29, including atomic separate stop
  state, cancelled writes, cross-instance updates and unchanged ordinary settings.
- `final-settings-widget-tests01.log`: 91/91; `final-sdk-tests01.log`: 157/157;
  `final-icon-sdk-tests02.log`: final icon identity validation passes.
- `final-session01.log`: 342/342; `final-registry-tests01.log`: 18/18;
  `final-stop-tests01.log`: 3/3; `final-catalog-icons05.log`: 2/2 including optional
  corrupt-package fallback and untrusted initial/deferred icon admission.
- `final-marketplace-service-tests01.log`: 15/15, including stopped-state retention
  across a verified batch update; `final-marketplace-ui-tests01.log`: 7/7.
- `marketplace-native-fixtures01/native-result.json`: 28/28 across 900/560 DIP
  layouts, initial focus, overflow and explicit Cancel/approval. Synthetic XAML
  PNGs were reviewed. Initial fixture attempts assumed retained focus would reset
  and confused an action ID with an element ID; corrected the fixture without
  changing production focus policy. Actual transitions are covered by widget tests.
- Final `native-widget-stop02/result.json`: 30/30 and
  `native-webapp-stop03/result.json`: 5/5 after the separate state-store change.
  `radial-native02.json`: 114/114; previous indexed-entry regression: 44/44.
- Release-mode candidate publication completed with the published async-cleanup
  verifier. `candidate-verification01.json`: 1,122 files and 15 bundled widgets;
  full inventory and real catalog/package-integrity/icon admission pass.
- `candidate-smoke01/hidden-startup.json`: the complete Release candidate starts
  hidden and responsive with zero child bridge/worker processes, preserving lazy
  startup. Its owned smoke process exited cleanly.
- `candidate-runtime-identity01.json`: all 136 deployed frontend DLLs match the
  qualified Release build. `candidate-health01.json`: responsive, input-ready
  startup recorded, no nonzero error headers, normal platform settings unchanged.
- Encoding/whitespace review passes. Main still has only the preserved unrelated
  `docs/implementation-agent-goal.md` edit.

### Running physical candidate

`artifacts/marketplace-lifecycle/candidate01/OverlayFrontend.WinUI.exe` is running
as PID **12872** with the normal `%LOCALAPPDATA%/WidgetRail` profile. The Guide
shortcut is preserved, and the normal platform settings file is byte-for-byte
unchanged. This is a dirty-source acceptance candidate at the existing preview.32
version, not a new distribution release. Native verification used isolated
profiles and synthetic content; no account interactions or personal captures.

### Physical checklist

1. From Marketplace, Manage in Settings should show the destination/loading page
   directly, then the correct details, with no false failure dialog.
2. Check Browse, Installed and Updates. When updates are available, test individual
   and Update all review/cancel/retry. A fresh Marketplace or Settings file install
   should appear enabled and request permissions only when its features need them.
3. In Settings → Widgets, test All/Built-in/Community, SVG icons (including disabled
   widgets), long-list navigation, and Back restoring the selected row into view.
4. B at widget root and from the rail should open radial. Nested dialogs, menus and
   reorder operations still consume Back first; directional entry still uses rail.
5. Stop an ordinary widget and a webapp with a pin/pop-out. Its activity and pin
   should close while another widget continues. Selection, hide/reopen and app/PC
   restart should keep it stopped. X Reload or tray Start should resume it with
   saved sign-ins/data and remembered pin geometry intact.

Account-dependent behavior and real public-package update acceptance remain
physical checks. Marketplace Installed intentionally lists curated packages;
other local packages remain in Settings. Batch limits are documented in the
Marketplace reference. No unsupported runtime limitation was needed for this scope.
