# Native pinned window foundation

`PinnedWidgetWindow` owns a peer WinUI window, separate from the main overlay's
visibility. Native XAML controls render its content. It uses the existing WinUIEx
window APIs for topmost placement, click-through/noactivate styles, message
handling and whole-window opacity. No native renderer, polling loop or second
controller reader is introduced.

Showing the window does not activate it. Passive mode combines layered transparent
hit testing with no-activation policy and disables XAML pointer hit testing. Switching
to interaction removes those styles. This primitive does not grant widget action
authority: the production coordinator separately gates native entry and pointer/
automation/controller action admission, and use the pinned presenter's genuine
frame/projection binding. Passive mode deliberately does not set IsEnabled, since
that would restyle otherwise-enabled widget content. Opacity clamps to 30–100%.

`PinnedPlacementPolicy` stores DIPs and relative work-area anchors. It resolves the
saved monitor when available, otherwise the primary/first valid monitor, constrains
placement to the work area and honors supplied content limits. Invalid data falls
back; displays too small for the minimum surface are rejected. Placement does not
persist physical focus ownership. `PinnedPreferencesStore` bounds JSON reads,
validates identities and finite values, isolates the explicit settings profile and
uses atomic latest-request writes. Main/native legacy files are not modified.

Validation:

- The integrated shell suite passes 40 tests, including negative-coordinate/DPI
  placement, monitor removal, rotation, malformed data, tiny work areas and atomic
  profile persistence. The migrated Full Application sample passes all five tests.
- The native window fixture passes 14 checks. Actual mouse clicks pass through to
  the underlying window in passive mode and reach the pin exactly once in interactive
  mode. Native flags/alpha, keyboard focus, main-window hide, pin hide/reopen and
  control-enabled presentation are checked. This is a same-process peer-window
  test, not proof of interaction with an arbitrary external application.
- Reviewed screenshots use a solid blue pin surface: 45% blends underlying content,
  100% presents only the pinned content, and 60% returns to passive blending.

Evidence: `artifacts/winui-shell/pinned-window-pixels` and unique analyzer/test
binlogs. The fixture is `--validate-pinned-window`; the batch driver is
`scripts/Test-WinUiPinnedWindow.ps1`.

The production Pin command now uses this foundation; see
[production coordination](winui-production-pinning.md). Genuine projections,
shared lifecycle/controller ownership, layout selection and persisted passive
restoration are integrated. Remaining work includes move/resize/opacity controls,
live monitor and per-display scale reconciliation, compact-media transfer and
physical/provider acceptance. Earlier fixture evidence above covers the window
primitive; it does not establish complete production pin parity.