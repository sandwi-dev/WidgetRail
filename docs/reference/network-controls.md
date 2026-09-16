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

Select a connected Wi-Fi network to disconnect it without turning off the radio.
Open **Saved networks** to connect to a remembered network, change **Connect
automatically**, or **Forget network**. Forget asks for confirmation and removes
the Windows profile; connecting again may require its password. Windows-managed
profiles remain read-only when policy or access rights prevent changes.

These controls require the optional Wi-Fi management permission. Connecting from
the saved list uses the separate saved-network connection permission. The list
refreshes when opened, after an action, or when you choose Refresh; it does not poll.

**Connection details** shows transport, internet access, addresses, gateways and
DNS servers in a scrollable page. Each address has its own line, with wrapping for
long IPv6 values.

## Bluetooth

Bluetooth has its own page and focus state. Available operations depend on the
radio and device. Readable status should remain available when a particular
operation cannot be performed.

Choose **Scan for devices** after putting the device into pairing mode. This requests
an active Windows Bluetooth inquiry for up to 12 seconds and stops when you leave
the interactive widget. Ordinary status updates do not continuously scan.
Nearby results are replaced once at the end of an explicit scan. Existing rows
keep their order while connection status updates remain live. A nearby device
that disappears is marked unavailable and moved below nearby results until the next scan.

Press A on a nearby unpaired device to pair. On a paired device, A opens its
options; removal is a secondary action with confirmation. Confirmation-only
pairing uses your explicit Pair action. PIN display, PIN entry, and numeric
comparison ceremonies remain in Windows Settings. X opens Windows
Bluetooth Settings. Pairing does not guarantee connection, and the widget does
not claim a universal Bluetooth Connect/Disconnect control.

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
