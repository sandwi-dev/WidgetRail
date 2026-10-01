# Native tray commands

The rail keeps native ListView focus/selection and uses MenuFlyout for its command
surface. Pointer context request or controller Menu opens commands for the exact
catalog identity. Quick actions retain their existing dashboard bridge route under
Visible lifecycle; they never synthesize ordinary widget input or enter its view.
Changes in selected widget, worker/package identity, quick-action declarations,
visibility or foreground ownership invalidate an open command surface.

Y remains host-owned: a tap toggles reorder, while holding for 700 ms restarts the
selected widget once. A canceled hold consumes its release, so returning to the
same widget cannot turn it into a tap. Reorder mode is tap-only. Controller Left/
Right moves the selected widget without changing its identity; A/B/Y finishes.
Keyboard F2, Left/Right and Enter/Escape provide the same reorder operation.
The native command menu provides Reorder and Restart for pointer/accessibility use.
Current catalog identity is checked again after obtaining serialized admission.

The hint uses a reserved native layout slot so tray/widget focus changes cannot
resize the collection viewport. Final production chrome still needs themed role
styling and shared controller-family glyph hints. Radial layout, pin commands and
recovery-chord parity remain separate open shell work.

Validation at this checkpoint:

- 36 managed shell tests pass, including tap/hold boundary, exactly-once hold,
  selection/incarnation loss, cancellation and reconnect/reorder semantics.
- 233 integrated presentation-session tests pass after shell palette integration.
- Five native command checks cover menu/reorder/dismissal, preserved identity and
  reversing the reorder to its original saved order. No real quick action or worker
  restart is invoked by this production-profile test.
- Four real tray browsing checks cover rapid reversal and explicit entry after
  retained-view and appearance integration.
- Five deep Playnite/Music return checks still pass, including exact focused bounds
  and current modal action authority. The ordered retention test now takes explicit
  native navigation ownership with End. An early direct UIA ScrollPattern movement
  could be overtaken by pending initial focus; that separate interaction remains to
  investigate rather than being claimed fixed by the ordered retention gate.

Evidence: `artifacts/winui-shell/tray-command-native`, `tray-retained-appearance`,
`retention-appearance-native-entry`, and associated uniquely named analyzer/test
binlogs. Screenshot helpers select the main HWND because the production backdrop
and native tooltip windows share its process. This is not physical controller,
real-provider quick-action, multi-monitor or complete shell acceptance.
