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
interaction domain. Ordering controls, full radial presentation and quick actions
are subsequent shell work;
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
  runtime pass (`finite-range-probe/result.json`). The subsequent full native
  Library/tray/details replay passes all six checks, with visible real artwork
  (`finite-range-native-replay`). Music's five Home/Library/return checks also pass
  (`music-integrated-replay`). Neither sequence launches media or a game.

None of these checks establishes complete production controller acceptance,
radial behavior, or pinned-window interaction.

## Inactive widget retention

The shell retains up to three native widget presenters, including the current
widget, in a stable Grid. Leaving a widget suspends its presentation demand before
sending Background, then collapses its presenter. Return establishes the current
worker frame, applies it while suspended, shows the existing native controls and
reacquires demanded leases. Tray preview still cannot claim widget focus. Explicit
entry restores the remembered item using its current lease.

The least recently selected presenter is disposed before creating a fourth one.
Runtime, instance, presentation or package replacement invalidates a cached
presenter. A complete catalog removes absent widgets; incomplete discovery does
not evict unseen identities. Closing the shell drains every retained presenter.
Evicted widgets currently rebuild their native view; lightweight viewport/focus
restoration across eviction remains to be implemented for complete return parity.

The real-package retention regression passed five checks at Playnite item 169:
leave for Music, return with tray ownership, restore exact game focus and physical
bounds, open details through fresh authority, and retain the modal across another
switch. Before/after bounds were identical (x2075, y621, 187x278). Screenshots show
the same poster grid and returned details artwork. Evidence:
`artifacts/winui-shell/retention-production-final`. Earlier attempts exposed test
harness issues: the UIA tree is under `windows`, and unfocused tiles have different
bounds because of authored focus scale. The final check compares settled focused
states and captures the explicit main HWND instead of including tooltip windows.

Media suspension is separately validated: Unbind releases placement without
terminally retiring its native viewport. The presenter owns terminal retirement;
resume reattaches the same durable browser without resource readmission. All 101
native media checks pass (`artifacts/winui-shell/retention-media`). These checks do
not establish physical controller/performance or cache-eviction acceptance.
