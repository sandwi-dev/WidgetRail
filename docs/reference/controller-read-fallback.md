# Ordinary controller input selection

While the overlay is visible, ordinary navigation checks GameInput, then each
XInput slot, then the native DualSense reader. A successful idle reading does
not block the next backend: the first reading with button, trigger, or stick
activity supplies input. Stick activity uses separate left and right thresholds
so drift does not claim control. These internal thresholds are configurable;
this change does not add a controller settings UI.

The selected source keeps control while input is held. GameInput reads are bound
to that device, XInput reads to that slot, and DualSense reads to that HID
connection. Release is delivered before another source can act. Disconnects
produce a neutral frame before another controller can take over. Input tracking
is preserved across backend changes so the new controller's first press is not
lost. When controllers act simultaneously, the backend order above wins.

GameInput may initialize without supplying gamepad readings. Missing readings
allow the remaining backends to be considered; initialization alone does not
claim input. XInput and native HID reads are shared, not foreground-exclusive.
This policy does not change HidHide or virtual-controller configuration.
Exclusive control keeps its separate selected physical controller and routing.

The DualSense HID reader handles USB and Bluetooth reports on a background
thread; the UI consumes a freshness-checked copy. Hidden GameInput/XInput
navigation reads remain dormant. PS and Create + Options retain their existing
opening shortcuts, using the native reader without another HID connection.
View + Menu also retains its independent shortcut reader.
