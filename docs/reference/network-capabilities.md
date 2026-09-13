# Network capabilities

Declare the capability in the manifest, obtain user approval, then use its typed
service. See [Capabilities](capabilities.md) for consent, lifecycle, and error behavior.

| Capability | SDK operation | Availability |
|---|---|---|
| `system.network.read.v1` | `HostServices.Network.GetStatusAsync`, `GetSavedProfilesAsync`, `OpenStatusSubscriptionAsync`, and `WatchStatusAsync` | Visible or Interactive |
| `system.network.details.read.v1` | `GetConnectionDetailsAsync`, `OpenConnectionDetailsSubscriptionAsync`, and `WatchConnectionDetailsAsync`; bounded display-ready IP, default-gateway, and DNS values for one preferred connection only | Visible or Interactive |
| `system.network.saved-profile.switch.v1` | `SwitchSavedProfileAsync` | Interactive only |
| `system.network.wifi.read.v1` | `GetAvailableWifiAsync`, `RequestWifiScanAsync`, `OpenAvailableWifiSubscriptionAsync`, and `WatchAvailableWifiAsync` | Read/events while Visible or Interactive; scan Interactive only |
| `system.network.wifi.connect.v1` | `ConnectAvailableWifiAsync` for a current saved/open scan result | Interactive only |
| `system.network.wifi.radio.read.v1` | `GetWifiRadioAsync`, `OpenWifiRadioSubscriptionAsync`, and `WatchWifiRadioAsync` | Visible or Interactive |
| `system.network.wifi.radio.control.v1` | `SetWifiRadioAsync`; software state only | Interactive only |
| `system.network.bluetooth.read.v1` | `GetBluetoothAsync`, `OpenBluetoothSubscriptionAsync`, and `WatchBluetoothAsync`; sanitized radio/discovery/device state | Visible or Interactive |
| `system.network.bluetooth.radio.control.v1` | `SetBluetoothRadioAsync`; software radio only | Interactive only |
| `system.network.bluetooth.pair.v1` | `PairBluetoothDeviceAsync(deviceId)` for one current broker-issued opaque device ID; returns an authoritative bounded pairing outcome and never implies profile connection | Interactive only |
| `system.network.bluetooth.unpair.v1` | `UnpairBluetoothDeviceAsync(deviceId)` for one current paired opaque device ID; returns a bounded removal outcome and requires authoritative disappearance before UI success | Interactive only |
| `system.network.bluetooth.manage.v1` | `OpenBluetoothDeviceSettingsAsync(deviceId)` after validating one current opaque device ID; opens the Windows-owned Bluetooth Settings surface without placing the native ID in a URI | Interactive only |

## Important behavior

A scan is an explicit action. Bluetooth pairing establishes association; it
does not promise a connection for every device profile. Protected Wi-Fi credentials
use the separate host-owned prompt flow rather than entering the widget worker.
Generic sockets and profile-specific GATT/RFCOMM access are not granted by discovery.
