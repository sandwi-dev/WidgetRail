# WinUI host-owned text entry

`UI.TextEntry` and `UI.SensitiveTextEntry` retain the existing SDK contract. Widgets
publish a bounded value/placeholder and final-value action; they never receive
keystrokes or intermediate edits. No SDK, protocol or trust-level fork is added.

The trusted presenter renders a focusable opener. Activation creates a native
`ContentDialog` with `TextBox` or protected `PasswordBox`, plus native buttons in a
controller keyboard. WinUI owns popup placement, scaling, clipping, native edit
semantics, focus geometry and automation peers. This is the **host keyboard**, not
an implementation of widget-authored details modals: those keep the separate
widget-local modal layer. Pinned/presentation-only fragments render no editable
control and cannot open a keyboard.

- A activates a virtual key; B cancels; X removes the preceding character; Y clears.
- LT changes letter case; RT commits; LB/RB move the edit insertion point.
- D-pad/normalized directional movement uses native XY focus within the keyboard.
- Physical text editing/paste uses the native editor. Printable input also works
  while a virtual key has focus. Enter commits from the editor; Enter/Space on a
  focused virtual key activate that native button, including Cancel. Escape cancels.
- The host has no second controller reader or raw-button queue. The existing
  normalized input ingress routes the keyboard before widget shortcuts.
- Prompts use the existing licensed controller fonts and update with controller
  family. Theme follows the owning presenter. Dialog width follows its XamlRoot;
  short windows use native vertical scrolling.

An edit captures opener identity and the widget/runtime/presentation/session/scope
owner, action, original value, input kind and maximum length. Harmless snapshots
keep the edit and dispatch with current snapshot authority. Changing any of those
semantic fields, removing/disabling the opener, changing scope/owner, hiding the
host or disposing the presenter cancels without dispatch. Closing restores only a
still-valid opener. Dismissal revokes authority and clears buffers immediately;
a late native close cannot commit or steal focus from a newer edit. Reopening
waits for the previous native dialog to close.

Sensitive declarations start empty, use PasswordBox's protected UIA semantics,
never permit reveal/paste and never copy intermediate values into snapshots or
widget state. Commit transfers one bounded string and clears the editor. Cancel
transfers nothing. Callback failures are replaced with a fixed diagnostic without
the original exception or inner exception, because callbacks could echo a secret.
Managed/native string allocation is not represented as guaranteed memory zeroing.

## Validation

Build the WinUI x64 project with the analyzer and a unique binlog. Launch packaged
mode with `winapp run ... --no-build --arch x64 -p Platform=x64 --detach --json
--args '--validate-text-entry'`. No physical controller owner is acquired.

The autonomous page exercises native controls, focus and normalized controller
routing, not a mock dialog. It writes pass/fail and check names only to
`%LOCALAPPDATA%/WidgetRail/WinUI/diagnostics/text-entry-result.json`; committed
values are never written. It covers initial/repeated opening focus, native keyboard
navigation, family changes, max length, caret editing, commit/cancel, scope/owner
and declaration replacement, secret cleanup, UIA password semantics and callback
redaction. Production-widget keyboard integration, physical controller acceptance,
full high-contrast/scaling coverage and global motion-policy integration remain
part of migration validation, not claims established by this fixture.

Validation checkpoint (2026-09-28): 26 autonomous native checks passed twice;
x64 analyzer build passed with no warnings. The final keyboard was visually
inspected at 125% Windows scaling with native focus on its first letter, equal
key columns and rendered Xbox prompts. Add `--text-entry-preview` to the validation
arguments for a persistent ordinary field for pointer/keyboard/screenshot checks.
This preview does not acquire the physical controller adapter.

## Unicode editing and accessible values

Controller caret movement, insertion, selection replacement and backspace use
`.NET StringInfo` text-element boundaries. Emoji, combining sequences, joined
emoji and regional-indicator flags are not split into UTF-16 fragments. Native
selection endpoints inside a text element expand to safe boundaries for deletion
or replacement. The SDK limit remains UTF-16 length; insertion rejects overflow
without truncating input. Ordinary and protected editors share this policy.

The opener remains a native command button. Its native automation peer exposes
the current ordinary value through a read-only Value pattern, independently of
the accessible label. Editing still requires invoking the button and committing
the native dialog; UIA SetValue cannot bypass that session. Authored value updates
raise the standard value-property event. A sensitive opener reports protected
semantics, stores no accessible value and exposes no Value pattern. Even a
previously acquired value provider returns no value after protection is enabled.

The combined checks pass 89 managed tests and 38 native text-entry assertions.
Native coverage includes ordinary/protected Unicode edits, cancellation/secret
clearing, current-value peers and input-revoked popup admission. An external
WinApp UIA `get-value` reads the ordinary preview's value successfully. Evidence:
`artifacts/winui-shell/{unicode-managed.log,control-values-native-01,control-values-uia-02}`.
These are behavior/automation checks, not new screenshot or physical-controller
acceptance.

## Popup scaling and retained edits

The host keyboard's native ContentDialog is outside OverlayScaleRoot. It therefore
receives interface zoom once through owned popup metrics, like native menus and
tooltips. Key/editor font sizes also receive the theme's text scale and Bold Text
policy; controller glyphs and layout gaps follow interface zoom alone. A native
TextBlock title avoids ContentDialog's fixed-size default title template. Width
remains bounded by the current XamlRoot, with native vertical scrolling for short
viewports. Live preference changes retain the same controls, edit value and focus;
restoring defaults resets the owned metrics without creating a second edit session.

Validation:70 native text-entry checks pass, including15 new scaling/retention
assertions and popup raster captures at0.5/1/1.25 zoom and1.5 text scale. Theme font
removal, secret cleanup, native Enter/Space behavior and current-authority commit
checks remain passing. Evidence: `artifacts/winui-shell/popup-typography-20260930/`.
