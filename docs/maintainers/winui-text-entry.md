# WinUI host-owned text entry

`UI.TextEntry` and `UI.SensitiveTextEntry` publish a bounded value/placeholder and
final-value action. Ordinary fields can explicitly opt into protocol 88
[whole-entry suggestions](../reference/text-suggestions.md), receiving transient
queries through a bounded read lane. Protocol 96 adds optional input purposes and local English word completion.
Existing ordinary fields remain commit-only.

The trusted presenter renders a focusable opener. Activation creates a native
`ContentDialog` with `TextBox` or protected `PasswordBox`, plus native buttons in a
controller keyboard. WinUI owns popup placement, scaling, clipping, native edit
semantics, focus geometry and automation peers. This is the **host keyboard**, not
an implementation of widget-authored details modals: those keep the separate
widget-local modal layer. Pinned/presentation-only fragments render no editable
control and cannot open a keyboard.

- A activates a virtual key; B cancels; X removes the preceding character; Y clears.
- LT changes letter case; RT commits; LB/RB move the edit insertion point.
- X and LB/RB repeat after 400 ms at 90 ms per character. After at least four
  character repeats and one second of holding, ordinary fields advance by words
  at 250 ms intervals. Character insertion and passwords never accelerate to words.
  Release, direction/focus/owner changes and conflicting input reset the gesture;
  there are no catch-up bursts. Buffered edits use Windows word segmentation;
  browser edits use native Ctrl+Backspace/arrow commands after validating the field.
- Menu inserts Space and View pastes on release. The View+Menu visibility chord,
  conflicting buttons and interrupted ownership cancel these pending shortcuts.
  Their ordinary clickable keys remain available; L3/R3 keep suggestion controls.
- D-pad/normalized directional movement uses native XY focus within the keyboard.
- Physical text editing/paste uses the native editor. Printable input also works
  while a virtual key has focus. Enter commits from the editor; Enter on a
  focused virtual key activates that native button, including Cancel. Space inserts
  a space. Escape cancels.
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
allow user-requested paste and reveal, and never copy intermediate values into snapshots or
widget state. Commit transfers one bounded string and clears the editor. Cancel
transfers nothing. Callback failures are replaced with a fixed diagnostic without
the original exception or inner exception, because callbacks could echo a secret.
Managed/native string allocation is not represented as guaranteed memory zeroing.

## Optional suggestion lane

Protocol 88 carries an ordinary field's source key and result limits. The host
debounces transient queries, rejects stale session/revision/owner responses, and
keeps fixed native button slots above the keys. Only result/status content changes;
arrival cannot move focus. The suggestion region grows upward while the editor
and keys retain their positions. Selection replaces the whole
field, keeps the keyboard open, and requires Done. There is no instructional text
inside the suggestion area. Native buttons retain mouse, keyboard and controller
activation, theme resources, accessible labels and full-label tooltips.

Protocol 89 adds bounded field timing: debounce, minimum request interval, and
provider deadline. The deadline travels through the worker RPC rather than being
cut short by the ordinary RPC default. A queued UI update must match the exact
current state object, not just its query revision: loading and results can share
a revision, but delayed loading must never replace ready results. Publication is
serialized with retirement, and closing suppresses all later UI callbacks.

The runtime executes at most one author suggestion callback per worker outside
the action lane. Its independent deadline cannot be blocked by synchronous author
code or cancellation callbacks. A timed-out callback retains capacity until it
drains. The bridge/session revalidate the active ordinary field and never start a
worker just for suggestions. Password and browser live-edit keyboards are excluded.

Main-window deactivation must distinguish transferring activation to an owned
keyboard PopupHost from leaving the process. Resetting input unconditionally
closes the popup before pointer activation can reach its buttons. Owned transfers
now retain transient UI; external process transfers keep the existing reset rule.

Validation for this addition is recorded in `artifacts/pob/text-suggestions-*`:
SDK contract/catalog and runtime/bridge admission regressions, 266 shell tests,
17 API compatibility tests, and PoB skill-management checks (PoE1: 40; PoE2: 42).
The native fixture passes 135 checks covering stable focus/geometry, whole-field selection, one Done,
Cancel, no matches, password exclusion, paste, and replies after closure. Installed
PoE2 mouse checks cover opening the picker, receiving real upstream suggestions,
clicking a suggestion, Done, and cancelling a changed query. These are automated
native/controller-routing checks; a physical gamepad acceptance run is separate.

The configurable-timing follow-up is recorded in `artifacts/pob/suggestion-options-*`:
270 shell tests (including 100 edit/reply/dispose races, same-revision delayed UI
updates, close/reopen identity, and opt-in empty queries), 17 compatibility tests,
SDK and bridge contract checks, two runtime scenarios, and 135 native keyboard
checks with custom timing. The process-boundary regression uses a 2.25-second
provider with a five-second suggestion deadline and the ordinary two-second RPC
default, then cancels another request and verifies that the same worker handles
the next query. Protocol/native constants are generated and checked for parity.

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

## Input purposes and offline completion

Protocol 96 extends `TextEntryInputKind` with `Integer`, `Decimal`, `Url`, `Email`
and `Prose`. Authors opt in through `TextEntryElement.InputKind`; existing
`Ordinary` and `Sensitive` declarations keep their behavior. Numeric fields use a
keypad; URL/email fields put their common punctuation on the main layer. Purposes
choose presentation, not validation: widgets still validate committed values.
Browser input type/inputmode selects compatible layouts without changing its DOM.

`Prose` permits host-local English word completion when no widget suggestion source
is declared and the UI language is English. It reuses the bounded suggestion
controller/row, resolves only a prefix at the caret and replaces only that word.
Selections, technical identifiers, sensitive fields and live browser editors do
not get this fallback. Widget-supplied suggestions retain whole-entry semantics.
No typed text is stored in the dictionary or sent to a service. No automatic
correction or acceptance occurs. Empty results collapse the local suggestion row.

The first prose lookup lazily loads a 10,000-word, frequency-ordered vocabulary
from the embedded resource; lookups use two-letter groups and bounded results.
Source and license notices are in `Assets/Words`. No prediction engine process,
model download, package dependency or dictionary request is needed at runtime.
