# WinUI Select checkpoint

`WidgetViewPresenter.Controls.cs` maps the existing `Select` declaration to a
native button and anchored `MenuFlyout`. Options are commands, rather than an
optimistically mutated selected value: the widget publishes the committed
selection in its next snapshot. Activating the already selected option is valid.
This is why the adapter does not dispatch from `ComboBox.SelectionChanged`.

WinUI owns placement, popup scrolling, pointer/keyboard interaction, theme
resources and automation peers. The adapter owns semantic action authority and
controller routing. It does not add another command queue.

## Host integration

- Route movement through `MoveFocus` and A through `ActivateFocused`; an open
  Select consumes them before parent collection navigation or activation.
- Route B through `DismissTransientControl()` before widget shortcuts or modal
  dismissal. When it returns true, consume B.
- While `HasTransientControl` is true, do not dispatch other parent shortcuts or
  right-stick scrolling. System overlay toggling remains above this policy.
- Call `DismissTransientControl()` when hiding the host. Hiding a window need not
  unload its content.
- Host foreground restoration goes through `Enter(restoreNativeFocus: true)`;
  the current popup option receives focus before parent entry policy.
- A fragment presenter within a collection needs the same popup precedence in
  its input owner. The existing indexed action router does not yet provide that
  integration; this checkpoint validates top-level Select controls.

An unchanged snapshot preserves an open popup. Runtime/instance ownership,
active scope, opener lifetime, enabled state or option declarations changing
revoke it. Retained native popup items cannot dispatch after revocation. Action
admission uses the current presentation authority only after validating the
opening's unchanged semantic option bindings. An exiting popup loses authority
before asynchronous admission starts.

Selected state is displayed with native checked menu items. Authored glyphs and
package icons still require the shared glyph/icon renderer; this checkpoint does
not claim icon parity. There is no TextEntry implementation here: its SDK contract
opens a host-owned keyboard modal and sends only the committed value, so an
inline TextBox would change behavior. Keyboard overlay and sensitive-input
lifecycle are a separate shared-control task.

## Validation

`Validation/SelectControlValidationPage.cs` is an autonomous native-XAML probe
(no controller adapter, no synthetic desktop keyboard). A shell route can create
this page and it runs on Loaded. It writes
`%LOCALAPPDATA%/WidgetRail/WinUI/diagnostics/select-controls-result.json`.
It covers selection entry, disabled/busy skipping, boundary containment,
unchanged snapshots, exact action dispatch, selected-option activation,
cancellation, option rebinding, disabled/removal/scope/owner invalidation and
disposal. A WinUI-analyzer build passed with zero warnings/errors. Runtime probe
execution is pending coordinated deployment in the integration worktree.

This is control correctness coverage, not production widget performance,
physical controller acceptance, high-contrast visual acceptance, or complete
popup feature parity. The real worker action transport and indexed-fragment
popup routing still need integrated evidence.

## Integrated validation and input entry

The integration host exposes --validate-select. The native probe passes 17 checks,
including popup precedence and release suppression through HandleControllerButtonAsync.
Two initially invalid raw fixtures were corrected (missing selected accessibility value,
and initial focus still naming the removed picker). No validator was relaxed.

HandleControllerButtonAsync is the common normalized-button entry for ordinary widget
shortcuts and indexed row input. Open popups consume buttons before either path; B closes
the popup before a parent/modal action can run. Navigation uses MoveFocus. This does not
read controllers or create another worker action queue. The real bridge/worker probe
passes release suppression, ordinary shortcut entry and indexed ancestor dispatch while
retaining the focused row. Evidence: artifacts/winui-surfaces/input-route-result.json.

Production shell wiring, full popup icons and context menus remain separate work.

## Accessible current selection

The native opener button now exposes a read-only Value pattern with the selected
label and an ExpandCollapse pattern tied to the actual popup lifetime. The
button's accessible name remains its field label. Expand follows the same
declaration/input gate as pointer or controller opening; Collapse cannot dismiss
another control's popup. Revocation, native close, failure and selection commit
publish the collapsed state, without a late old-popup callback overwriting a
new popup's state. Value/expanded changes raise standard automation property
events. Selection continues through the existing per-option commands; this does
not introduce local selected-value state or a second action pipeline.

All 24 native Select checks pass, including value/label separation and expanded
state during opening and input revocation. Evidence:
`artifacts/winui-shell/control-values-native-01/`. The normal native Button
template and the existing resolved styles remain in use.
