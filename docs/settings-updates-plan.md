# Settings, tray and application updates

Baseline: `67e249f5`, branch `codex/settings-updates`. This is the first release
batch; completion is a verified complete candidate for physical acceptance.
Do not merge, push, release, create distribution installers or remove worktrees.
Preserve profiles, sign-ins, Guide shortcut and unrelated edits. Use synthetic
fixtures and isolated profiles; no credentials, personal captures or live calls.

## Accepted scope and decisions

- Shared host menu infrastructure with one submenu level, native WinUI menus,
  controller/keyboard/mouse support, focus restoration, safe dismissal, theme,
  scrolling and screen-edge placement. Tray only supplies commands and grouping.
  Keep direct pin commands and Website settings. Group notification commands
  and widget management. No unused public SDK menu contract.
- B from rail opens radial in radial + rail mode; nested UI consumes B first.
  Retain rail-only behavior and radial return behavior.
- Settings Restart/Quit bypass ordinary loading and acknowledge immediately;
  deduplicate requests and retain graceful shutdown. Fix About header Down target
  and audit equivalent page entry targets.
- One automatic app-check preference, enabled on new installs, shared by Settings
  and installer, preserving saved choices. Check once after startup settles,
  independent of opening Settings. No automatic downloads or installation.
- Available updates can notify each startup. No persistent dismissal suppression
  or startup-check cooldown. One pending entry per update in the running session.
  Quiet existing banners and actionable notification inbox; no Windows UI.
- Reusable optional notification intent, validated and executed only on explicit
  user activation through normal intent authority. Retain existing widget-open,
  informational and website callback behavior. Settings accepts an app-update
  navigation intent; no custom notification action rows or replies.
- Recovery initiated from Settings only. Remove automatic desktop recovery
  shortcuts; no new Start-menu recovery entry or app-cannot-open recovery feature.
  Reuse verified backup/update machinery and preserve existing backups/data.

## Deferred

Marketplace Browse/Installed/Updates, automatic refresh on open, bulk updates,
install-enabled defaults, Widgets-list redesign and catalog icons follow the
first published/installed release. Mouse hiding and extra repositories deferred.

## Work and evidence

- [x] Reusable menus and rail Back navigation
- [x] Responsive Settings application controls and navigation audit
- [x] Notification intent contract and Settings destination
- [x] Startup update check preference/service/notification and installer wiring
- [x] In-app recovery simplification
- [x] Focused regression checks, native synthetic validation and review
- [ ] Documentation, complete candidate, payload verification and launch

Initial implementation: shared `NativeMenuNavigator`, recursive menu theme/input
ownership, grouped tray commands, rail Back behavior, and Settings direct host
application controls. About's Back-to-content target now selects App updates.
Restart/Quit validate the current Settings frame and invoke the existing graceful
host shutdown outside the worker's sequential action queue. Worker fallback also
admits controls before initialization and deduplicates accepted requests.

Evidence so far: `artifacts/settings-updates/menu-native-01/result.json` passes
177 native checks (new tiered menu/rail tests plus existing browser regression).
Settings console harness passes 87/87; presentation-session MTP suite passes
335/335. Initial compilation needed a missing SDK using; corrected build passes.
`dotnet test` does not discover the Settings console harness; use its build plus
direct executable, as done here. No candidate has been replaced yet.

## Implementation and validation checkpoint

Deferred startup is preserved: no Bridge, widgets or update task before first
opening (Guide). The check runs ten seconds after first initialization settles,
once per Bridge session. It needs neither Settings nor a WebView. The saved
preference uses one atomic file shared with setup; no persisted dismissal state.

Notification intents retain a bounded, source-run-scoped grant. Publication,
replacement, withdrawal, dismissal and activation revalidate/release authority.
The host update action is restricted to the trusted Settings destination, even
if another widget declares the same contract. Native notification activation
uses existing routing without claiming an A release already handled by its dialog.

Recovery reuses existing helpers and backups, is reviewed inside Settings, and
does not create desktop/Start shortcuts. Invalid recovery sets do not hide another
valid backup. No app-cannot-start recovery entry point is added.

Current evidence under `artifacts/settings-updates/`:

- `settings-native-06/result.json`: 44/44 native checks. Submenu A/Right/B/Left,
  parent focus, long-menu scroll/wrap/edge placement, reorder/Back priority,
  synthetic startup update transport and activation, same-session dedup,
  ordinary-widget notification intents, banner-only/metadata/informational behavior,
  and native banner layer ordering. Only synthetic pages/media were used.
- `hidden-startup-02/result.json`: 7/7 native checks including no startup update
  task before first opening, hidden windows/input, first show and reuse on reopen.
  The old test's fixed 200ms hide assertion was changed to await actual completion.
- `settings-tests-03.log`: 87/87; `session-tests-02.log`: 335/335;
  `shell-tests-02.log`: 234/234; `bridge-intents-04.log`: 17/17;
  `appupdates-tests-03.log`: 10/10. Tests include loading controls, declared/forged,
  expired/replaced notification authority, opt-out persistence, no automatic
  downloads, recovery confirmation and damaged recovery metadata.
- `installer-syntax-02/result.json`: synthetic Inno compilation passed;
  executable never run. Installer contract and isolated recovery checks passed
  (13 recovery checks). No real installation or user-profile recovery performed.
- Native theme screenshot reviewed: `settings-native-06/result.tray-widget-submenu.png`.
  Shared menu focus/theming, authority boundaries, disposal, async shutdown and
  bounded storage reviewed; no unresolved issue found in the new scoped paths.

Extended browser harness limitation: `full-native-04` reached 64 checks before
an immediate popup-toolbar assertion failed. `full-native-05` reached 45 checks
before child Page actions timed out; an earlier run reached further. The suite
is not clean. Its readiness assumptions were tightened (painted synthetic link,
replacement identity/retirement, browsing readiness), without changing production
browser behavior. Preserve this evidence for a separate popup timing investigation;
do not describe it as a passing full-browser regression. The original 177-check
run above and the targeted 44-check run are separate results.

Other fixture corrections: synthetic installation required a catalog marker;
Bridge console E2E must run its EXE (workers use Environment.ProcessPath); song
banner checks must wait through the previous banner's original expiry plus quiet
interval, even when that banner was dismissed early. Production quiet policy is
unchanged. New native submenus bring focused rows into view without animation,
including a guarded reconciliation after initial layout.

## Acceptance checkpoint

Remaining: build/verify a complete Developer candidate and launch with the normal
profile, sign-ins and Guide preserved. Physical checks: tiered menu/controller
navigation and shortcuts, rail B, Settings header Restart/Quit during loading,
About Back-to-App updates navigation, preference persistence, and notification
opening. Full real upgrade/recovery remains for the subsequent release test;
candidates intentionally cannot replace the installed app. Marketplace/Widgets
redesign remains deferred.
