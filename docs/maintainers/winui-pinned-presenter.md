# WinUI pinned presenter binding

The presenter renders a pin through the same control reconciliation, layout,
focus, style and motion paths as a main widget. `WidgetPresentationBinding`
separates the genuine session frame from the effective view. Its frame is never
copied or rewritten to stand in for a pinned layout.

## Shell integration

Set `WidgetViewPresenter.Session`, then call
`ApplyPinned(selection, projection)` using the session's current selection and a
projection resolved from a genuine published frame. Ordinary `Apply(frame)` is
unchanged. On later publications, resolve the same selected layout again and
apply its new projection; do not select again merely because a snapshot arrived.

The presenter uses the projection's root, initial focus, active scope and
prefix-stripped styles. Surface identity includes the selection object and
layout ID, so switching layouts at the same snapshot sequence cannot hit the
ordinary unchanged-frame shortcut. A main-page modal does not alter a pin's
active scope, controls or focus. Removing or replacing a selection makes its
captured binding unusable for input.

The existing `SetPresentationActiveAsync`, `SetAutomaticFocusEnabled` and `Enter`
methods remain the lifecycle/entry seams. Await suspension before resuming.
A shell that observes an invalid selection must suspend/retire the corresponding
presenter; a failed `ApplyPinned` does not invent a replacement layout.

`EnsureInteractionAsync` receives the original frame's authority **for widget
lifecycle admission**. A pin manager must check the selected widget incarnation
and its own interaction policy; it must not compare that main-page scope with
the pin's scope. The presenter independently compares the captured binding's
selection and effective scope before and after that callback, then calls the
session's pinned input route. Returning false denies primary, shortcut, slider,
Select, text, context and indexed action dispatch, including UIA invocation.
It does not set authored controls to disabled or change their disabled styles.

`DispatchActionAsync` continues to handle ordinary main-view actions. Pinned
commits use `SendPinnedActionAsync` directly and never reach that delegate.
Pinned media/fullscreen host effects and pinned native-window placement belong
to the shell/media integration; they are not reinterpreted as ordinary actions
by this binding.

## Indexed and visual content

The native indexed view and source retain the same binding. Data reads include
`PinnedLayoutId`; discovered continuation uses the pinned session API. A pending
range may use a newer genuine frame for the unchanged query, but interaction
retains its actual displayed frame. Native activation checks use the pin's scope;
changing selection retires source reuse even when collection IDs match.

Indexed artwork stays attached to its exact lease. Ordinary pin artwork calls
`ResolvePinnedArtworkAsync`, including inherited focus artwork and fragments.
A harmless main-page publication does not restart still-valid pinned artwork.
Fragment and row rendering use an explicit style-map view; they no longer clone
`WidgetPresentationFrame` merely to replace `RenderStyles`.

Main-widget mementos must stay in the main-widget cache domain. If the shell later
persists pinned mementos, their key/validation must include the selected layout
and selection lifetime rather than only the genuine frame's widget authority.
The binding exposes `SameSurface`/`SameInput` for that distinction.

## Validation fixture

The bridge test executable provides `--serve-pinned-validation <pipe>`. Use a pipe
name beginning `pinned-validation-`; launch the analyzer-built WinUI app through
WinApp with `--indexed-validation-pipe=<pipe>`. The existing validation dispatch
selects `PinnedWidgetValidationPage`; no production shell path is modified.

The page exercises genuine-frame identity, independent main modal scope,
layout-prefixed styles, contrasting decoded artwork, ordinary/Select/text/context/
slider input, virtualized indexed activation/context menus, passive UIA denial,
same-sequence layout switching, stale selection rejection and removal/suspension.
Run `scripts/Test-WinUiPinnedWidget.ps1 -AppPid <owned-pid>` to capture the UI
result and screenshot. This requires the team's exclusive native deployment slot.

The pure binding is also linked into the managed presentation-session test
assembly. Its regression checks prove that pin/fragment styles never manufacture
an input frame, that main and pinned domains differ, and that stale selections
cannot create new frontend bindings. Native runtime acceptance must be reported
separately from managed and analyzer-build results.

## Native validation result

The dedicated pinned fixture passed all 19 checks at the desktop's 125% scale.
Nearby native gates passed 19 Select, 26 text-entry, 21 slider, 38 context-menu
checks, and all 20 indexed-view checks (including modal, retained artwork and
navigation probes). The tested build also contained the separately committed
presentation-memento changes from 94d4776b. These are automated native checks,
not a user physical-controller acceptance claim.

The initial pinned fixture attempted Select navigation before the native flyout
had assigned focus. Its wait now observes the native option's focus instead of
assuming opening is synchronous. Context menus use the same explicit readiness
check. No production presenter correction was required by these native runs.
The runner handles the initial Connecting state and uses a screen capture for
transparent WinUI windows; ordinary window capture omitted composed text pixels.
Evidence is under `artifacts/pinned-presenter/native-03`, `nearby-controls` and
`nearby-indexed` in the validation worktree.
