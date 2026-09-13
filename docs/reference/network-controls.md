# Network Controls

Network Controls provides Wi-Fi and Bluetooth controls through the same typed
capability services available to sandboxed widget authors.

## Wi-Fi

The widget distinguishes radio availability, current connection, and available
networks. A scan is an explicit interactive action, not a request made on every
render. Selecting a network uses the currently observed network identity.

Supported password-based connections use a masked host-owned prompt. The widget
does not need to hold the Wi-Fi password. Windows privacy settings or adapter
availability can limit what the provider returns.

## Bluetooth

Bluetooth has its own page and focus state. Available operations depend on the
radio and device. Readable status should remain available when a particular
operation cannot be performed.

Focus does not mean connected: highlighting a network or device must not make
it look like the current connection. Keep focus, selection, busy state, and
connection status visually distinct.

## A useful pattern for authors

Subscribe to status updates for the relevant lifecycle, request scans explicitly,
and show an unavailable state for a denied permission. Losing one optional feature
should not disable an unrelated working section.

See the [capability reference](capabilities.md),
[Windows network provider](../../src/WindowsNetworkProvider/), and
[Network Controls implementation](../../src/FirstPartyWidgets/NetworkControlsWidget/).
