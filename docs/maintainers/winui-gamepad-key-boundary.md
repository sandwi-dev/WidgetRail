# WinUI gamepad key ownership

The platform adapter owns controller sampling, repeat and semantic dispatch.
WinUI can independently map gamepad virtual keys to keyboard-shaped events
(`GamepadA` becomes `Key=Space`, for example). `GamepadKeyBoundary` consumes that
additional route at PreviewKeyDown/PreviewKeyUp using **OriginalKey**. Physical
keyboard keys, pointer input, UI Automation and explicit WinUI focus operations
remain native. No controller reading, timer suppression or widget-specific
navigation is added here.

MainWindow creates a boundary only when it has a platform input pump. Its
admission requires a live, visible pump and foreground ownership. Pump failure,
window retirement and boundary disposal end admission. A no-controller fixture
has no owner and retains WinUI's native gamepad behavior.

The boundary registers one owner per XamlRoot. MenuFlyout and ContentDialog trees
are separate from the main scene, so host popup factories call ObserveFlyout or
ObserveDialog before opening. At native open/load the owner attaches preview
handlers to those popup roots. Native unload and owner disposal detach them;
opening the same flyout again reattaches. New host-owned popup kinds must use the
same registration rather than adding their own gamepad filtering.

Handlers registered with handledEventsToo must use the ownership predicate before
interpreting mapped keys. This includes collection pending-navigation cancellation,
focus-group entry cancellation, pressed-style observation and the text dialog's
keyboard commands. Checking only Key or assuming Handled stops these observers
would permit a duplicate gamepad event to interfere with the semantic route.

## Native validation

Launch `--validate-gamepad-boundary`, then use
`scripts/Test-WinUiGamepadKeyBoundary.ps1` with that process ID. The fixture sends
native virtual keys via SendInput and verifies actual WinUI OriginalKey delivery.
A positive control first proves that native GamepadA activates a Button without
the owner. It then checks single semantic activation with the duplicate native
route, list navigation, context/select popup isolation, text-dialog commands,
down/up handling, physical keyboard and UI Automation, disposal, inactivity and
owner recreation. It refuses to inject when another process is foreground.

The initial native run passed 26 checks. The first fixture run lacked the
production presenter's enabled keyboard XY navigation and selected starting row;
the fixture was corrected to establish and assert native starting focus. The
production boundary required no correction from that failure.

This proves the virtual-key ownership boundary. It does **not** reproduce or
establish the cause of the user's physical double-movement report: the available
physical trace contained stick movement, with no recorded gamepad key events.
Physical controller validation and the separate foreground-delivery correction
remain distinct evidence.
