# Browser extensions implementation

## Agreed experience

- Install an MV3 Edge/Chromium extension once into a shared library.
- Each extension has an Enabled by default switch. Existing and new eligible
  profiles inherit this default unless explicitly overridden On or Off.
- Tray → Preferences → Extensions retains compact current-profile dialogs.
  It never redirects to Settings. Existing library entries need no second import.
- Settings → Extensions is a dedicated advanced page for defaults, per-profile
  overrides, shared updates and removal. Extension options stays in the current-page tray flow.
- Shared files do not share browser cookies, sign-ins or extension-local settings.
- Updates and removal clearly identify their effect on all profiles. Choosing Off
  for one profile preserves its extension settings.

## Implementation

- Shared PlatformSettings read model; native extension operations remain owned by
  the frontend. Settings can only request commands declared in its current frame.
- One stable package path and native ID per library entry. Explicit profile policy
  and applied revision are persisted atomically. Native operations are serialized.
- Import validation runs against a temporary profile rather than an active website.
- Failed/interrupted updates restore files and effective profile policy. Interrupted
  removal completes during initialization. Disabled attachments retain their data.
- The user's candidate catalog has been converted to the shared format. Temporary
  legacy-catalog migration code and serialization types have been removed. Opening
  Extensions no longer initializes every old profile to recover its enable state;
  initialization only checks for interrupted extension transactions.
- Browser now uses persistent storage; existing bookmark/history paths are retained.
- Packaged background sessions and provider media remain outside extension profiles.
- Stopped widgets collapse the empty themed content panel behind their status card.
- Single-choice preference lists use the list and normal Back behavior; destructive
  confirmations retain Cancel as initial focus.

## Evidence and progress

Current shared-library validation under ignored `artifacts/browser-extensions/`:

- `shared-native03/result.json`: functional native checks passed on WebView2
  155.0.4283.45, including inherited defaults, explicit overrides, shared updates,
  isolated extension storage, rollback and interrupted-update recovery.
- `shared-restart01/result.json`: restart persistence and global removal passed.
- WinUiShell: 259 passed; WidgetPresentationSession: 349 passed.
- Settings: 92/92 passed, including real library/details snapshots and host-owned
  mutation boundary. The earlier intermittent uninstall lock failure did not recur.
- `shared-ui01/result.json`: fixture never acquired foreground at startup; no
  extension controls were reached. Subsequent isolated runs passed with the old candidate closed. Fixture control-loading waits were also corrected; screenshots wait for transitions to settle.
- `shared-ui07/result.json`: 23 controller checks passed; settled library/detail screenshots reviewed. Includes pinned dialogs, Back, single-choice focus and default preservation.
- `shared-stopped01/result.json`: 7 stopped-widget regression checks passed.
- `candidate04`: complete updated candidate, 15 widgets and 1,133 inventoried files. Package integrity and frontend cleanup verification passed. Launched on the existing profile on 2026-10-15, PID 57280; remained running after startup.
- Final Settings commands also verify resetting an override to Default and applying a shared default to the inherited profile. Physical acceptance remains with the user.

Earlier per-profile evidence (`managed02.json`, `restart01.json`, `ui05`,
`stopped01`, `playback-regression01.json`, `hidden-playback01`) establishes the
original runtime capability checks and stopped-widget fix; it does not substitute
for validating the new shared-library behavior.

## Runtime limits

- InPrivate Add/Enable succeeds but the functional probe does not execute the
  extension. Private profiles remain private and cannot enable extensions here.
- Browser-toolbar action popups are not exposed. Loading popup HTML as a page
  does not provide browser-toolbar active-tab context. Declared options pages work.
- Extension stores, automatic extension updates and native companion applications
  are outside this implementation. No extension performance benchmarking.

## Physical acceptance

1. Open Browser/webapp → Preferences → Extensions. Confirm it stays in the
   current widget; enable an existing library entry without importing again.
2. In Settings → Extensions, change Enabled by default. Verify profiles using
   Default follow it while explicit On/Off remains unchanged.
3. Open a new eligible profile and verify inheritance. Reload existing websites
   after enable/disable, since already executed content scripts cannot be undone.
4. Open extension options and return with B. Repeat with a pinned browser surface.
5. Update an extension once and check its settings in two profiles after restart.

No merge, push, release or installer is part of this work. Preserve normal profile,
sign-ins, Guide shortcut and unrelated changes.

## Settings home navigation regression

Adding the seventh home card exposed a shared scroll-boundary bug. The recorded
Down-only sequence was reproduced with real Settings: General → Accessibility →
Widgets → About → About → Widgets. After reaching the scroll boundary, the host
ranked from the viewport centre while still allowing controls inside that viewport.

The presenter now excludes the exhausted scroll subtree from exit candidates and
preserves the original perpendicular lane when using viewport boundary geometry.
No Settings-specific focus links or card-order workarounds were added.

Evidence in `artifacts/settings-home-navigation/`: `repro01/result.json` records
the original failure; `fixed03/result.json` passes at normal and reduced heights;
`navigation-regression01` passes 77 native scrolling/navigation checks and three
actual keyboard-routing checks; seven directional-policy unit tests passed.
The focused reproduction is available through `Test-WinUiSettingsNavigation.ps1
-HomeOnly`. One earlier combined fixture failed during unrelated extension staging
before reaching Settings; the focused fixture avoids that dependency.
Complete candidate05 was assembled from the full updated publication, verified (15 widgets, 1,133 files), and launched on the existing profile. PID: 51532. Includes the navigation fix and migration-code removal; no merge or release.

## Extension page styling and options handoff

Both extension pages now use the shared Settings form cards. Library entries have
separate name/status text and a chevron. Each profile has one card containing its
override selector and Extension options action; shared package maintenance has a
separate section. Refresh and maintenance actions use visible button styling.
Responsive action grids retain geometric navigation when wrapping, while stacked
controls inside a setting card have sequential Up/Down links.

Settings previously checked visible browser targets immediately after switching
widgets. A running webapp can still be attaching its native browser at that point.
Options now joins browser placement before resolving the selected profile, with a
bounded timeout and selection/visibility guards. Pinned targets acquire pin input
before opening options. A genuine failure uses concise retry text rather than
incorrectly instructing the user to open an already-running widget.

Evidence: `options-repro01/result.json` reproduces the false warning with a
synthetic running browser held during native attachment. `options-fixed02` passes
27 native/controller checks, with reviewed normal/narrow screenshots and two
profiles. Settings tests pass 91/92; the previously reproduced baseline uninstall
`.catalog-operation.lock` failure remains unrelated and unchanged.

Settings navigation regression: 30/30 native checks passed. Complete candidate06 was verified (15 widgets, 1,133 files) and launched on the existing profile; PID 1636. No merge, push or release.

## Cold-start simplification

The Settings options shortcut was removed following physical testing: joining
native placement solved the warm reattachment case, but cold widget startup and
initial navigation added further dependencies. The shortcut and its cross-widget
selection/readiness code are now removed instead of extending that coupling.
Settings keeps shared defaults, profile overrides and package maintenance. Open
extension-specific options from the current widget's Preferences → Extensions
flow, which already owns an initialized browser and its exact profile. The prior
warm-handoff evidence above is historical and does not establish cold-start support.
## Profile scope and installation defaults

The tray Extensions entry now sits beside Website settings under Preferences.
It manages a browser profile across its websites and hosted pages; multiple pages
in one profile do not create duplicate choices. A profile chooser appears only
when the selected widget has more than one available profile. Site settings no
longer expose Extensions, and their old forwarding callbacks were removed.

New imports start disabled in every profile. Installing from a widget no longer
creates an automatic On override. The installation review keeps its global-enable
checkbox unchecked; only explicit opt-in enables the default. Settings explains
that each enabled profile runs the extension separately and can add memory/CPU
usage. Existing enable choices are preserved.
Latest validation: `profile-menu-ui01` passed 28 native/controller checks, including
sibling Preferences entries, disabled imports, actual current-profile tray options,
absence of the Settings shortcut, and normal/narrow layouts. Settings passed 92/92
on this run (the known intermittent uninstall-lock failure did not recur). Native
functional/profile policy checks are recorded in `profile-menu-native01`.

Complete candidate07 was verified (15 widgets, 1,133 files) and launched on the existing profile; PID 51960. Native functional validation passed 37 recorded checks. No merge, push or release.

## Tray options regression and shared presentation follow-up

Physical testing found that Extension options could close its menu without opening
a page. `options-tray-repro01` reproduces this through the actual tray entry: modal
teardown restores tray ownership and disables presenter input. The previous
implementation set the interactive flag and immediately requested admission before
that asynchronous transition completed. The earlier test had called the browser
management helper while widget input was already active and missed this boundary.
The action now awaits the normal widget selection/entry transition before opening
the extension's options page. `options-tray-fixed01` passes all 28 native checks,
including that path and replacement of an existing pinned options page.

The current follow-up also uses explicit close-versus-return disposition when a
full widget replaces its own pinned additional page. A single available layout
still requires confirmation; cancelling preserves the page. Layout and native
window preparation precede closing an outgoing page. Returning a page to its
widget remains a separate action, and replacement by another widget preserves it.

Live browser text entry now uses the same shared keyboard in a floating native
dialog, without changing the WebView viewport. Exact field/frame references supply
rendered bounds, translated through iframe scale/borders and the XAML transform.
Placement prefers below, then above, within the owning window. Only an open editor
runs the bounded, sequential 150 ms geometry check; subpixel changes are ignored.
Detached/hidden fields, replaced frames and navigation retire the editor. Physical
Space inserts text rather than activating the focused virtual key. Both buffered
and live keyboards explicitly focus Q with keyboard focus visuals.

Validation in progress: `keyboard-live02` passes 23 native checks, cross-process
frame editing/reload passes in `keyboard-frame01`, and shared shell policy tests
pass 259/259. Candidate07 was closed before interactive synthetic validation;
the complete replacement candidate will be launched after the remaining native
pin, buffered keyboard and visual checks.

### Full keyboard visibility and native spillover

The user clarified that keys must never require scrolling and that the keyboard
may extend outside a widget or pin. Both live and buffered modes now use one shared
native Popup presentation (`ShouldConstrainToRootBounds=false`) containing the
existing keyboard control. Width is responsive with a 440-DIP minimum at normal
interface/text scale, increasing for accessibility text; height fits all rows.
Position is clamped to the monitor work area and prefers below/above/beside the
rendered field. The popup retains its original XamlRoot and controller owner.

The previous window-bounded dialog approach and its clipping evidence are
superseded. `keyboard-spill-pin01` passes 180 checks, including a 320 × 220 pin,
unchanged WebView size, a complete keyboard outside the pin, and A/B routing.
`keyboard-buffered-floating01` passes 109 checks including interface scaling,
unscrolled keys, retained focus, buffered commit/cancel and secret clearing.
The native-popup live tests pass for normal and compact browser surfaces.

### Candidate09 delivery

Final native live runs (`keyboard-focus-final01`, `keyboard-focus-small01`) pass
28 checks each, including every key/action fitting without scrolling. Buffered
regression passes 109 checks in `keyboard-focus-buffered01`. Scaled cross-origin
frame editing/reload remains covered by `keyboard-final-frame`. The final opening
policy retains Q through WinUI's initial focus assignments, but stops redirecting
focus as soon as the user navigates or types.

`keyboard-native-events03` uses actual Windows keyboard events with the synthetic
window foreground: both live and buffered modes start on Q and insert exactly one
space while Q is focused. Their native popup screenshots were visually reviewed;
all rows and actions are visible. The preceding native event attempt lacked
foreground activation and is not validation evidence. No personal windows were
captured, and clipboard contents were not read or modified.

Candidate09 combines the freshly published production frontend (`frontend-final06`)
with the unchanged complete runtime/packages from `publication05`. Catalog,
package/icon integrity and published cleanup verification passed: 15 widgets,
1,133 inventoried files. Launched on the existing `%LOCALAPPDATA%/WidgetRail`
profile as PID 55656 and verified responsive after startup. The earlier candidate08
was superseded before delivery. No merge, push, release or installer was created;
the unrelated main-worktree document remains unchanged.

Physical acceptance: reopen the installed extension's options from the tray;
replace a widget's own pinned additional page with its full widget (including
Cancel); try live entry in a small pin and buffered widget entry; confirm Q's
highlight, complete unscrolled keys, one Space per physical press, and B behavior.

### Accepted for preview.33

The owner accepted all changes, including the separate Page actions destinations
and the shared keyboard focus/Space behavior. Candidate10 was the accepted complete
candidate. Final direct-new-window validation passed 179 checks, including repeated
explicit destinations, opposite remembered preferences, cancellation, replacement,
and the automatic website-requested window flow; screenshots were reviewed after
correcting wrapped footer labels. The profile menu/options checks passed 29 checks.
Task Switcher now includes a shared themed inline warning explaining that restarting
the overlay from Settings may be necessary when switching behaves unexpectedly;
its eight existing regression checks pass.

Release preparation uses preview.33 and SDK 0.4.3-dev. Community packages that still
used previously published versions receive fresh versions before rebuilding against
the current SDK. Installer/catalog validation must complete before publication.
