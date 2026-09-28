# WinUI shell navigation and preferences

The shell distinguishes browsing a widget in the tray from entering its controls.
Native tray focus selects a visible widget preview. It retains tray focus through
worker publications; pointer, accessibility or controller activation explicitly
enters the selected widget. Requests carry a selection revision, so a superseded
asynchronous selection cannot reclaim focus or publish its view after a newer one.
Tray navigation remains available while a preview is being established.

`WidgetViewPresenter.SetAutomaticFocusEnabled` gates only host-driven entry. It
does not change author input scopes, fabricate focus, or grant action authority.
Passive previews still receive publications. Pending native collection navigation
is canceled when the host withdraws entry ownership. A genuine pointer/UIA action
still passes the existing lifecycle and displayed-binding admission before
dispatch; enabling subsequent automatic focus does not itself move parent focus.
The host explicitly calls `Enter` when entering/restoring the widget. Native tray
entry waits on layout readiness for the exact current item, without polling or
forcing layout.

`ShellPreferencesStore` stores bounded widget order, last widget and reopen intent
under the explicit shell profile in `winui-shell-state.json`. It reads the existing
v2 `overlay-state.ini` only when the new file does not exist, and never rewrites that
native-host file. Reads and writes are bounded, IDs are validated, and writes use
unique temporary files and atomic replacement. A canceled startup does not save
unloaded defaults over prior preferences. Incomplete catalog discovery preserves
undiscovered saved positions; newly admitted widgets remain available even at the
saved-order bound. Complete discovery removes genuinely absent entries.

An explicit `InitialWidgetId` in a test configuration overrides saved startup
selection. Without it, a saved reopen enters the last widget; a fresh profile
starts with its tray owning input. In-memory hide/reopen retains the current
interaction domain. Ordering controls, full radial presentation, quick actions,
and bounded retention of inactive widget presentations are subsequent shell work;
this checkpoint does not claim that the temporary shell chrome has feature parity.

Validation at the initial navigation checkpoint:

- 29 managed shell tests, including bounded persistence, concurrent saves, legacy
  import, incomplete catalogs and reorder identity.
- 19 native focus-policy checks, including passive replacement/updates and explicit
  entry (`artifacts/winui-shell/tray-focus-native`).
- Four real-package tray browse/activation checks and a fresh-process saved reopen
  into YouTube Music without a pointer click (`tray-production`).
- The Playnite details regression exposed a separate pre-63 fixture-runtime wire
  mismatch: ordinary range requests included the new continuation request kind.
  `Kind=Range` now omits that default field; explicit Continue/Retry retain it.
  Strict wire tests and an actual enabled-row probe against the unchanged fixture
  runtime pass (`finite-range-probe/result.json`). The native details replay after
  this wire correction remains required before closing that regression.

None of these checks establishes complete production controller acceptance,
inactive-page scroll retention, radial behavior, or pinned-window interaction.
