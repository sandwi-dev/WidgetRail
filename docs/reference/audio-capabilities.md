# Audio capabilities

Declare the capability in the manifest, obtain user approval, then use its typed
service. See [Capabilities](capabilities.md) for consent, lifecycle, and error behavior.

| Capability | SDK operation | Availability |
|---|---|---|
| `system.audio.sessions.read.v1` | `HostServices.Audio.GetSessionsAsync`, `OpenSessionsSubscriptionAsync`, and `WatchSessionsAsync` | Visible or Interactive |
| `system.audio.sessions.control.v1` | `SetSessionVolumeAsync` and `SetSessionMutedAsync` | Interactive only |
| `system.audio.output.read.v1` | `HostServices.Audio.GetOutputAsync`, `OpenOutputSubscriptionAsync`, and `WatchOutputAsync` | Visible or Interactive |
| `system.audio.output.control.v1` | `SetOutputVolumeAsync` and `SetOutputMutedAsync` | Interactive, or one exact declared dashboard gesture while Visible or in an eligible running Background widget |
| `system.audio.devices.read.v1` | `GetDevicesAsync`, `OpenDevicesSubscriptionAsync`, and `WatchDevicesAsync`; sanitized input/output names and default markers only | Visible or Interactive |
| `system.audio.devices.control.v1` | `SetDefaultOutputDeviceAsync` and `SetDefaultInputDeviceAsync`; choose a currently enumerated opaque device for normal Console/Multimedia roles, preserving communications | Interactive only |
| `system.audio.spatial.read.v1` | `GetSpatialAsync`, `OpenSpatialSubscriptionAsync`; confirmed default/active formats and supported choices for current output | Visible or Interactive |
| `system.audio.spatial.control.v1` | `SetSpatialFormatAsync(deviceId, formatId)`; choose a supported spatial format for the same output, subject to Windows licensing | Interactive only |
| `system.audio.input.read.v1` | `GetInputAsync`, `OpenInputSubscriptionAsync`, and `WatchInputAsync` for current default microphone volume/mute | Visible or Interactive |
| `system.audio.input.control.v1` | `SetInputVolumeAsync` and `SetInputMutedAsync` for the current default microphone | Interactive only |

## Important behavior

Device selection uses currently enumerated IDs. Normal default-device selection
preserves communications roles. Spatial-format selection also depends on the
output device and provider licensing. These services control or describe audio;
they do not expose raw audio samples.
