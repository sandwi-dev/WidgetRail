# WinUI host controls and Settings audit

Updated 2026-09-30. Branch: `codex/winui3-frontend`. No merge to main.

## Implemented

- Settings Quit and Restart now reach the WinUI application consumer through the existing trusted diagnostics request and Bridge `application-control` queue. The managed presentation session strictly validates None/Quit/Restart replies.
- The shell has one asynchronous 500 ms control-plane poll, independent of controller input and visible widget. At most one request is outstanding. The destructive Take request is cancelled only during host retirement: a local reply timeout could otherwise discard an already consumed Quit/Restart. Bridge transport failure reports recovery rather than silently losing the consumer. Shutdown cancels and joins the pump.
- Quit follows the existing asynchronous cleanup path. Restart waits for window/worker/media/input cleanup and releases profile election, then calls Windows App SDK `AppInstance.Restart` with Windows-quoted original arguments. It removes `--hidden` so explicit Restart reopens visibly. No raw packaged executable launch or second restart helper.
- The Bridge hello explicitly advertises exclusive controller control, held D-pad scrolling, and startup-registration support. The trusted Settings companion passes those immutable host features to Settings; they are not persisted preferences or community-widget capabilities. Native hello omission retains its implemented features; managed frontends must opt in.
- WinUI now advertises exclusive control when its input consumer is attached, through the original native controller engine and shared status/preference exchange. Held D-pad controls remain omitted. Unsupported stale actions cannot mutate preferences or invoke their provider. Ordinary Guide/View+Menu shortcut remains available. See the 2026-09-30 restoration entry in the implementation status for recovery and initialization evidence.
- Audit found that existing Settings startup registration writes the native `OverlayHost.exe` Run entry. WinUI does not yet own a packaged startup backend, so that control and its startup wording are omitted through the same feature contract. This avoids exposing a control that could start the wrong frontend. Implement packaged startup registration during deployment work, then opt in.

## Appearance and accessibility consumer audit

| Settings family | Existing WinUI consumer |
| --- | --- |
| Theme ID/version, shell and widget paint | Bridge appearance/style revisions; shell palette reload and presenter resolved styles |
| Interface size and per-display settings | `OverlayShellPage.Sizing` resolves display profile; MainWindow scale/placement |
| Text size and bold text | `NativeComputedStyleAdapter`, `NativePopupTheme`, shell chrome policy |
| Backdrop darkness, surface appearance | `MainWindow.Appearance`, `OverlayAppearancePolicy`, `OverlaySurfacePaint` |
| Position and Rail/Radial mode | Production placement and shell navigation policy |
| Focus/section/dialog presets and speed | `WidgetMotionOptions` and `WidgetMotionPolicy`; presenter appearance updates cancel obsolete motion |
| Widget switching animation toggle | `WidgetSurfaceResizeMotion` with WidgetSwitch reason; same-widget extent changes retain separate semantics |
| Reduced motion / OS motion | Policy duration suppression; UISettings change event; artwork, section, focus and dialog owners |
| Contrast / reduced transparency | Accessibility policy, native style adapter, popup resources and shell paint |
| Controller open shortcut | PlatformInputPump applies persisted shortcut from appearance/settings refresh |

These source-consumer findings are supplemented by the actual Settings workflow below; they are not physical acceptance for every visual preference. No live system-settings changes were made.

### Native Settings appearance workflow — 2026-09-30

`Test-WinUiAppearanceSettings.ps1` launches the real Settings worker/broker and
production shell with a fresh profile below its evidence directory. The native
adapter is used only to establish foreground; no physical controller events are
routed to fixture actions. Native UIA controls drive the same appearance actions
as the user. The isolated profile is restored afterward; the user's profile and
Windows display, network, power, startup and driver settings are untouched.

`artifacts/winui-shell/settings-appearance-20260930/native03/result.json` passes34
checks: per-display interface/text size, native heading typography and shell
alignment, backdrop darkness, rail/radial selection, all three positions, all
focus/section/dialog presets, dialog/switch animation toggles, speed, bold text,
reduced transparency, high contrast, reduced motion and fresh-reader persistence
after shell hide/reopen. Unavailable startup is absent and controller preferences
are unchanged. This proves native UI → broker/storage → live frontend settings,
not every rendered animation frame or subjective appearance. Theme picker and
real provider workflows retain their separate qualification scopes.

Frontend03 builds analyzer-clean. Native01/02 stopped because the fixture reopened
a popup without establishing native focus/open readiness and treated an already
selected value as completion. Native03 focuses/reveals the opener, waits for menu
focus and selected-state publication, and uses actual value changes. No production
Settings correction was justified or made by these fixture failures.

## Evidence

`artifacts/winui-shell/host-controls-20260929/`:

- Managed presentation tests: 254 passed, including actual framed-pipe Quit/Restart consumption without a widget presentation and explicit supported-host-feature hello tests.
- Managed shell tests: 99 passed, including restart argument quoting round trip (spaces, trailing backslashes, quotes and empty arguments).
- Settings tests: 77 passed, including unsupported control omission, snapshot validation, refusal of stale actions, supported shortcut persistence, and a throwing startup backend proving unavailable startup never reaches system mutation.
- Bridge and Settings worker compile cleanly, 0 warnings/errors.
- Parent combined build passed cleanly. Actual packaged Settings Restart replaced
  frontend 54796 with 32188 and Bridge 22748 with 35928, preserving the isolated
  16-widget profile/catalog. Old processes exited; Settings Quit then closed the
  replacement frontend/Bridge. Native Controllers page omits unsupported controls.
  Evidence: `artifacts/winui-shell/widget-lineup-20260929/combined/{restart-result,quit-result}.json`.
  No registry/startup change was performed; controller physical acceptance remains separate.

## Remaining

- Startup registration is a planned supported feature, explicitly confirmed by the user. Implement it during WinUI packaging/installation and restore its Settings control with the WinUI-aware backend. Its omission from the development candidate is temporary. Verify enable/disable, launch of the installed WinUI application, and update/uninstall cleanup at that milestone.
- Exclusive control is restored; physical game/controller containment remains for the user. Held D-pad scrolling is not requested; right analog remains the continuous-scroll path.
- Windows App SDK Restart failure is currently recorded as a startup Trace error and exit code 1 after cleanup. Its exceptional failure recovery presentation can be addressed with deployment/startup error handling; successful replacement needs the native workflow check above.
