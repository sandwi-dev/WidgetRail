# Modal dismissal implementation

Implementation in progress, 2026-11-12. User direction: Escape and clicking outside
the modal dismiss it; do not add a Close button. Existing authored header content
and default SDK header behavior are unchanged.

## Ownership and source findings

`WidgetModal.DismissActionId` already owns cancellation. The SDK used to express
it only as a B shortcut, and the host had no backdrop policy. The new optional
`ViewNode.ModalDismissal` carries the exact cancellation action and
`DismissOnBackdrop`. `WidgetView.WithModal` supplies it, with backdrop dismissal
enabled by default for newly built widgets. Widgets can opt out without losing
Escape/B cancellation. The validator requires it to match the dialog's declared
B-pressed action; it grants no new command authority. Incremental changes carry
interaction/authority impact. Older snapshots remain valid and preserve their
keyboard/controller-only backdrop behavior.

`WidgetViewPresenter.Modals.cs` already handled Escape on presenter and portal
content. A modal paints beside the shell rather than beneath the presenter, so
Escape from native focus on sibling shell chrome does not traverse either route.
The portal now attaches an additional root handler for its exact lifetime.
Escape from outside the active modal scope is admitted during preview before
shell chrome can consume it; modal descendants retain bubbling behavior.
Handled child keys, native popups, browser keyboard ownership, inactive
presentations and stale scopes remain excluded. This source finding is a concrete
coverage gap; attributing the user's exact Playnite report still requires live
reproduction. No claim of physical validation follows from source inspection.

The host-owned scrim captures a primary press and release, with at most five DIPs
of movement. It dispatches the existing cancellation action only if the same
dialog/owner/policy remains admitted. Dragging out of content, dragging away and
back, right-click, a replacement runtime, and opening an inner popup do not dismiss
the modal. The scrim consumes pointer events rather than activating the obscured
background. Keyboard and pointer dismissal share pending-command coalescing.

## Consumer matrix

| Consumer | Treatment |
| --- | --- |
| Playnite details | New SDK default; same details-close action, controller hints retained, no added Close button. |
| Playnite filters/setup | New SDK default invokes existing cancel/back action; query drafts/setup side effects remain domain-owned. |
| PoB inspection/search/site/edit/confirmation dialogs | New SDK default; existing return/cancel actions remain authoritative. Browser/native popup keyboard ownership remains first. |
| Discord participant/voice dialogs | New SDK default; closes via existing visit/account-scoped action. No call/share commands are synthesized. |
| Game Help options/library/preview/confirmations | New SDK default invokes existing close/cancel; destructive confirmation is never implied. |
| Capture Library delete | Backdrop/Escape invoke delete.cancel, not delete.confirm; domain busy checks remain in effect. |
| Catalogue and Settings package reviews | Backdrop/Escape invoke review.cancel; full-access approval/install remains an explicit separate action. |
| SDK Gallery and native modal fixtures | Same default and explicit policy/legacy coverage. |
| Other shipping widgets/templates | No direct WithModal consumers found; native host chooser/flyout/browser dialogs retain their existing lifecycle. |
| Previously built packages/direct protocol snapshots | No new field means legacy backdrop blocking, existing Escape/B path preserved. |

There is no widget-specific keyboard hook, UI-tree rewrite, or manual focus-return
flag. Existing modal scope memory owns focus restoration. Protocol and generated
API version updates are coordinated with the other shared additions in this task.

## Validation

SDK tests cover explicit policy, opt-out, legacy snapshots, invalid placement and
action mismatch, required protocol version, serialization, incremental policy
updates and unchanged header/pin behavior. Anchored presentation tests retain
their independent legacy version coverage.

The native modal fixture has an optional `--native-modal-dismissal` mode, exposed
by `scripts/Test-WinUiModals.ps1 -NativeDismissal`. It delivers real Windows input
to the fixture's own foreground window and checks Escape from both dialog controls
and sibling chrome, primary backdrop click, drag cancellation, stale-runtime
release, and inner Select precedence for Escape and pointer dismissal.

Managed SDK validation passed: 177/177 using the repository executable test runner;
evidence is `artifacts/modal-sdk-tests.log` and `artifacts/modal-sdk-*.binlog`.
The integrated Debug build passes with zero warnings/errors. All 51 native modal
checks pass in `artifacts/input-discord/modal-native-trace-2/result.json`, including
Escape before a sibling chrome handler, child popup precedence, click-through
prevention, drag-out/back, opt-out and runtime retirement. The pointer fixture was
corrected to send real uncoalesced mouse movement; SetCursorPos alone had not
produced WinUI PointerMoved events. The original drag expectation was preserved.
The trimmed production frontend also opened the Discord details modal and returned
focus to its invoking control after real Escape. Live Playnite game/details testing
with the rebuilt package remains a user acceptance check.
