# Capture Library and shared interaction improvements

Status: active. User authorized implementation and thorough testing on 2026-11-15.
Base: 2ae52d5e. Branch: codex/capture-library-upgrade.

## Scope and ownership

- Host tooltip cleanup: remove redundant tips on labeled controls; retain useful icon,
  truncated-label/status tips and accessibility names. Adjust pin Save/Cancel/Opacity
  currently share a blanket tooltip assignment (PinnedAdjustmentToolbar.cs:81).
- Shared graph contract: bounded optional empty text; domain widget maps actual FPS
  state. WidgetLineGraph.cs:18 currently assumes every empty graph is collecting.
- Shared media collapse mouse bug: reported opening Notifications, even though a
  widget tray icon was underneath. Do not assume click-through. Reproduce native
  press/release and trace focus restoration; utility icons open panels on GotFocus.
- Capture confirmation: per-widget/per-kind skip, saved only on Proceed, resettable
  in Settings. Retain permission checks, countdown, target validation and stop controls.
- Capture Library: bounded indexed search/filter/sort, favorites/titles, revisioned live
  updates, virtualized gallery with density controls, viewer previous/next and return
  position. Stage two: reusable multi-selection and bounded safe batch file/metadata
  operations with recoverable deletion, export and reveal-file behavior.
- Keep existing cards/media backgrounds and the accepted 1180 x 700 preferred extent.
- Capture service owns files/index/authority; widget owns gallery policy and workflow;
  SDK/host owns reusable collection input, rendering, selection and lifetimes.
- EXCLUDED: capture pipeline consolidation, border behavior changes, trimming,
  annotations, cloud sync, visual-content search. Do not modify real captures in tests.

## Reuse approach — agreed refinement

The framework shares interaction mechanics, not all widget data or product policy.
Reuse existing indexed collections, row artwork, media playback/zoom, navigation and
subscriptions. Add opt-in collection multi-selection as the concrete shared gap.
Do not introduce a universal gallery, provider query engine, favorites database or
batch-operation framework. Capture queries/metadata/revisions remain capture-service
contracts, usable by Capture Library and Game Help. Widgets choose filters and
available operations; the broker executes authorized domain commands. Shared internal
Windows helpers may implement recycle/reveal/export, but widgets submit authorized
capture IDs rather than unrestricted paths. No other widget is forced into selection.

Game Help's picker benefits from richer discovery but does not become a management
screen. Existing List/Open consumers remain compatible. Extend navigator/collection
continuity only if real large-query requirements reveal a concrete missing contract;
retain proven behavior if a proposed abstraction fails representative consumers.

## Consumer matrix

| Change | Affected/migrated | Already compliant or not applicable |
|---|---|---|
| Graph empty states | Performance Monitor | Discord ping conditionally shows graph; SDK Gallery verify compatibility |
| Capture query API | Capture Library; Game Help adopts query search, filters, favorites and live updates | Legacy List/Save response shapes remain unchanged for older packages |
| Capture confirmation | Capture Library, Game Help, other Capture SDK consumers | Discord live sharing uses separate chooser |
| Media collapse | Captured image/video, Game Help, embedded player and browser expansion audit | Pins/banner shared focus boundary regression checks |
| Indexed selection | Capture Library representative migration; documented opt-in example | Playnite, PoB, Discord, music widgets, Games & Apps, Settings and full-app reference remain opt-out; YouTube has no indexed collection |
| Tooltips | Adjust pin and host labeled controls | Keep meaningful icon/status/truncation tips |
| Linked folder sources | Capture Library settings and read-only removal; Playnite setup and metadata companions | Game Help consumes the resulting query/open path; SDK Gallery/templates keep owned-capture flows |
| Related-item intents | Capture Library and Playnite | Existing dynamic availability filters handlers before chooser and rechecks delivery; no new routing engine |
| Saved-video read leases | Capture Library/native player; Game Help keeps its small attachment limit and disabled oversized rows | Temporary recording budgets and explicit copy/export behavior remain bounded separately |

## Validation requirements

- Behavior tests: queries across full library; stable ordering/revisions; stale results;
  bounded memory; titles/favorites persistence; external edits/deletes; permission
  revocation; cancellation and partial batch outcomes; Game Help compatibility.
- Native mouse/keyboard/controller: media collapse, rapid transitions, no unintended
  utility panel, exact return focus, multi-select range semantics, collection viewport
  restoration, dialogs/cancel/reset and tooltip overlap. Real press/release evidence.
- Performance: thousands of synthetic entries, bounded row/thumbnail demand, first
  load vs warm query costs; profile thumbnail lock before any concurrency rewrite.
- Theme/scaling: light/dark/high contrast, acrylic toggle, narrow and wide viewport,
  no clipping, unchanged preview/media appearance. No source audit treated as runtime proof.
- Record rejected/narrowed abstraction proposals with evidence rather than force
  migrations. Existing protocol/API compatibility and integrity boundaries remain.

## Work log

- Started parallel graph, confirmation and library-service lanes. Parent owns
  media collapse/tooltip diagnosis, widget UI and shared selection integration.
- Protocol allocation: graph empty text v111; indexed selection reserved v112.

- Resumed after user scope review. Graph tests 15/15 and SDK transport 1/1 passed;
  confirmation PlatformSettings 35/35 passed; core capture service query tests 2/2
  passed before batch additions. These are partial evidence, not final qualification.
- Native baseline media-collapse clicks: six cycles did not reproduce the reported
  notification panel. Guard edits remain experimental until native fixture evidence.
- Gallery partial authoring is unfinished and not yet compiled; do not deploy this tree.

- Source-based review found and corrected: unsafe permanent-delete fallback for a
  previously unavailable capture (new RemoveUnavailable rejects restored originals);
  late canceled batch replies overwriting newer confirmation/selection; simultaneous
  initial and post-subscription query publications (now one SDK RunLatest lane);
  metadata-only edits upgrading a changed preview's destructive content revision.
  Deterministic regression tests are being integrated; no guard is justified by
  cancellation alone.
- Shared selection is bounded to 256 explicit query items; oversized gestures reject
  atomically. Select all is only offered for matching sets within the bound, never
  as an alias for loaded rows. Long-running unlimited query-wide jobs are not added.
- Added optional provider key-to-index lookup (v113) on the existing indexed request
  lane for large-query focus/viewport rebasing. Legacy consumers opt out unchanged.
- Capture browse uses existing grouped indexed grid/list for complete monthly
  partitions, application facets, and adjustable thumbnail density. No new gallery
  renderer or universal metadata/batch engine.
- Native media transition checks passed 48: WinUI pointer-focus fallback onto the
  notifications icon is confirmed in traces. Shared fullscreen entry/exit owns focus
  through detach/restore; no utility opens after the fix in those native cycles.
- Native graph checks passed 13; confirmation native checks passed 23 (automation
  peers, not physical clicks). Service tests include 3,001 indexed captures and
  SQLite deletion/corruption. Native temporary-file recycle/restore preserves bytes.

## Added scope: existing achievement captures and Playnite integration

User authorized integration on 2026-11-15 and then explicitly chose reusing
Playnite Achievements' existing screenshots/clips over a second automatic recorder.
The earlier automatic-screenshot proposal is superseded, not an implementation
requirement. Capture-pipeline consolidation remains excluded.

Provider evidence: installed Playnite Achievements 4.1.1; upstream Captures wiki
and README describe clean/notification/framed screenshots, buffered recording,
provider-owned folders and achievement/game galleries. Reusing that recorder avoids
competing capture sessions; no quality/performance superiority is claimed.

- Keep generic capture context: authenticated origin, bounded subject/event references,
  readable labels, optional provider event time, origin-scoped idempotency. Recorded
  time is distinct from actual capture time; unknown actual time stays unknown.
  Preserve strict legacy List/Save shapes. No Playnite semantics in host or SDK.
- Add generic linked-source registration only after checking existing authority APIs:
  user-authorized folder/source, opaque source ID, contained relative references,
  supported media validation, no traversal/reparse escape. Do not copy provider
  originals into our library. Thumbnails and bounded temporary playback leases may
  use caches. Persist source ownership explicitly; removing a linked library entry
  must not delete a provider original. Provider retention/deletion remains visible.
- Widget setup must expose supported provider capture configuration through the
  Playnite integration plugin, with version/capability checks, validation, persistence
  and readback. Inspect the actual public provider API before choosing controls.
  Do not silently edit settings JSON behind a running addon or claim unsupported
  settings can be configured safely. Missing addon remains a normal unavailable state.
- Preserve the widget's unload-after-idle behavior where possible. The recorder runs
  inside Playnite independently; source discovery/reconciliation must not rely on an
  always-interactive widget. Audit durable generic sources versus provider-plugin
  inventory and existing application lifetimes before selecting the smallest mechanism.
  Current source manifest defaults to 300 seconds; verify installed/user overrides
  rather than assuming the reported two minutes is the source default.
- Bidirectional UI: context captions and authorized Open related item; game/achievement
  details show linked captures and View in Capture Library. Uninstalled providers keep
  readable metadata but disable unavailable actions. Reuse generic intents/queries.
- Tests: no persistent media duplication; source-root authority/isolation; traversal,
  replacement, missing/moved roots and files; partial writes/recordings; rescans after
  widget unload/restart; dedup/replay; metadata/index recovery; provider missing/version
  mismatch; settings round-trip/cancellation; no deletion without original-file authority;
  old widget wire compatibility and Game Help browse/open of supported linked media.

Research sources:
- https://github.com/justin-delano/PlayniteAchievements/wiki/Captures
- https://github.com/justin-delano/PlayniteAchievements#overlay

## Added scope: large saved recordings without temporary media duplication

User explicitly requested fixing whole-recording temporary playback copies after
reviewing Windows' two-hour recording option. This is shared media-library work,
not a Windows Game Bar-specific adapter and not capture-pipeline consolidation.

- Existing library Open obtains a verified original lease, but registry Import then
  copies it into temporary capture storage. Replace saved-video playback import
  with an explicit adopted read lease; release/revoke/expiry/disposal must close
  authority and never delete the original. Retain bounded image normalization and
  explicit export/copy operations where requested.
- Separate saved-library playback limits from WidgetRail recording-creation budgets.
  Use bounded streaming/inspection/thumbnail work for long or large media. Avoid a
  whole-file hash/copy on every playback open; preserve file identity, revision and
  containment safeguards using verified handles rather than trusting filenames,
  length and modification time alone.
- Audit parent-directory/reparse replacement between validation and media open;
  preserve the same verified resource through playback. Keep linked originals
  read-only. Reuse media authority renewal while a player remains active.
- Consumer checks: Capture Library, Game Help's bounded attachment/export path,
  native media player, expanded/pinned media, permission revocation and old widget
  wire compatibility. Measure large-file open/seek/memory/temporary-disk behavior;
  source review alone is not performance evidence.
- Generic folder linking also supports compatible external capture exports. Dedicated
  Windows settings controls, SteelSeries library/export/settings adapters, timelines
  and editing are not added to this implementation merely because discussed as examples.

## Additional qualification

- Full Capture Library suite passed 54/54 before the latest folder-management UI,
  file-modification timestamp and direct-video-playback changes. New context cases
  cover owner-scoped associations, replay/recovery, modal close versus late results,
  and related-item intents. These newer changes require another qualification pass.
- Native shared selection/lookup passed 84/84 twice. Checkbox hit target moved during
  parent-row press animation; suppressing that motion only for its own checkbox
  stabilized repeated list/grid/grouped clicks. Opt-out consumers retain normal press
  motion. Deferred focus dispatch removed the observed crash; its original exact
  cause is not conclusively established.
- Native embedded media passed 49 checks; source-folder picker passed 4 checks.
- Production gallery fixture at 1180 x 700 logical / 125% scaling shows ten complete
  medium cards with spacing and truncation. Selection uses overlay checkboxes; all
  1,200 synthetic entries stay virtualized. Other sizes/themes remain to qualify.
- Playnite capture settings adapter uses public provider ISettings transaction with
  active-edit/revision guards and persisted readback; 56 isolated API/framework checks
  passed. No live user settings were changed by those tests.

## Final integration evidence before installer update

- Capture Library 64/64, SDK 181/181, compatibility 17/17, catalog 52/52,
  documentation 250 files and native parity 200 constants passed. Modern SaveAsync
  uses an additive current-response operation; legacy wire shapes remain closed.
- Native long-media playback passed 8 checks with a real two-hour / 134 MiB MP4,
  including seek into the second hour, renewal and source disconnect. Actual widget
  confirm-delete passed normal and deliberately delayed scene updates; another
  widget's lease correctly blocks recycling and the failed preview recovers.
- Adjust pin native checks passed 104; text entry passed 207. Redundant labeled
  tooltips are absent; accessible names and useful icon/truncation/status tips remain.
- Gallery and selection inspected in light, high contrast and maximum text/interface
  scaling. Source management enlarged to 760 x 520 logical so two folder actions fit;
  real mouse open/close returns to Settings. Batch buttons use the existing responsive
  grid and selected count shares the existing count line.
- Source access survives idle unload, reviewed package updates and old-version
  cleanup. Actual catalog uninstall removes exact access; the final owner revokes
  discovery. Reinstall can register a fresh source. No catalog-absence heuristic.
- Playnite widget 0.4.6 and plugin 1.2.0: 246 widget, 119 community, 14 core,
  72 framework/API checks; native settings 29, details geometry 22, navigation 47.
  Six live community opt-ins were skipped. Actual provider API verification follows
  installation; no achievement unlock has been triggered on the user's account.
- Metadata companions remain optional; ordinary folder discovery needs none.
  API-based SDK integrations can submit context directly. New Playnite companions
  are hidden, preserve existing visibility and never change original attributes.
  Display-only references are plain text; related actions require explicit opt-in.
- Qualified candidate and Playnite/Game Help packages are running/installed. The
  registered host installer update is still pending, so the overall goal remains
  active. No merge to main is performed for this feature branch at this stage.
