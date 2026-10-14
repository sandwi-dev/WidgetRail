# Pinned dialogs and responsive choice lists

## Scope and behavior

The reported Twitch pop-out retained Page actions after the pin became passive.
The shell intercepted all controller frames for that dialog but required active
pin ownership to deliver them. View could therefore never reach pin reentry.

The accepted design direction keeps dialogs and keyboards in pins. Leaving pin
interaction revokes and hides its host dialog immediately; native asynchronous
teardown cannot restore obsolete focus or clear a newer dialog. View dismisses
the choice and leaves the pin, while B only dismisses the choice. Foreground loss,
overlay hiding and stale ownership receive the same cleanup. A new host dialog
waits for an explicitly dismissed dialog's native teardown before opening.

Browser site-decision completion reconciles interaction after the browser's
deferral finishes. This fixes the separate case where a pop-out's slot regained
focus while the browser still considered its dialog open. Current input and focus
authority are checked, so completion does not reactivate a passive pin.

Choice-dialog presentation is shared across the framework, not specific to pins:
16-DIP padding, an 8-DIP title gap, and a three-row grid for details, a bounded
scrolling list, and optional remember controls. The existing native action footer
stays outside the list. Page actions retains the origin but removes the redundant
widget-name line. Notifications retain their existing anchored-panel placement.
No new widget-author parameter or browser-to-main-window handoff is introduced.

## Validation

Synthetic full-widget and pop-out tests exercise real Page actions, B, View,
foreground loss, hide/reopen, stale passive ownership and the controller keyboard.
A 695×450-pixel pin exercises all 24 list options, verifies the final option is
inside the list viewport, and checks that Down reaches the visible Cancel button.
Fixtures use local synthetic pages and preserve the browser navigation count.

The first run exposed missing pop-out interaction restoration after native dialog
completion; the shared reconciliation corrected it. `lifecycle-02.json` passed
140 checks including notification and broader browser regressions. The first final
run timed out at startup (zero checks). Visual capture subsequently exposed a
fixture focus race: it focused the list before ContentDialog.Opened restored Cancel.
The fixture now joins Opened before navigating; production focus behavior is unchanged.

The final run `final-05.json` passed all 140 checks through shutdown. Four native
pin screenshots verify the top and bottom of the 24-item list for full-widget and
pop-out pins. Debug build and trimmed Release publication succeeded. Review checked
dialog ownership revocation, obsolete completion guards, native teardown joining,
keyboard cancellation, bounded list layout and unchanged notification placement.

The complete candidate is running with Twitch selected at
`artifacts/pinned-dialogs/candidate-01/WidgetRail-Developer-0.1.0-preview.31-win-x64`
(PID 40708 at launch). All 136 deployed top-level DLLs match `published-02`.
The normal profile and sign-ins are preserved. Evidence is under
`artifacts/pinned-dialogs`; no merge, push, release or installers were performed.

Physical checks: open Page actions in a full-widget or pop-out pin, scroll to the
last action, use B to remain in the pin, use View to leave and reenter, hide/reopen
with the dialog open, and repeat with the controller keyboard. Physical acceptance
remains pending.
