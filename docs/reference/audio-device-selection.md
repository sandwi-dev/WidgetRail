# Audio Mixer device selection

Audio Mixer offers host-owned Output device and Microphone selectors populated
from its existing device subscription. The selected option is Windows' confirmed
Multimedia default. A missing default is shown as Choose a device, not a guessed
selection. With no connected devices in a direction, that selector is disabled.
Focus uses stable selector IDs and explicit links through the scrollable mixer.

Audio Mixer stops its active subscriptions when hidden and unloads after two
minutes. Reopening reads the current audio state. Unloading the widget does not
change Windows volume, device choices, or playback in other applications.

The shared SDK adds HostServices.Audio.SetDefaultOutputDeviceAsync(deviceId) and
SetDefaultInputDeviceAsync(deviceId). Both require the optional Interactive-only
system.audio.devices.control.v1 capability. Settings describes it as Change
speakers and microphone. Read, volume/mute and device-selection permissions are
independent. No endpoint path or native identity is accepted from a widget.

WindowsAudioProvider resolves a current opaque device on its dedicated MTA
thread, validates direction and presence, and invokes the isolated policy adapter.
It sets Console and Multimedia roles only. Communications is never written.
Success requires native role readback and a refreshed device enumeration. On a
partial failure the policy attempts to restore only its own changed roles, and
never overwrites a newer external selection. Failure feedback does not claim
rollback necessarily succeeded; the UI reflects actual observed defaults.

IPolicyConfig is an undocumented Windows COM contract. The adapter confines its
activation, HRESULT handling, reference lifetime and vtable shape to one file.
ABI reference checked during implementation:
https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IPolicyConfig.cs
Windows' role/readback contract:
https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-getdefaultaudioendpoint
No registry edits, UI automation, elevation, polling service or external helper
is used for switching. Existing endpoint/default-device callbacks update the
widget; the provider rebinds sessions and volume controls after a switch.

Existing widget volume workers finish before an explicit device switch begins.
Provider volume/mute commands retain their observed default-device generation,
so commands queued before a switch cannot adjust its replacement endpoint.
Busy selector options prevent repeated commits while keeping selector focus.
Optional read failure after a successful switch cannot misreport it as failure.

Applications explicitly bound to a device may ignore a Windows default change;
the picker explains that their settings may need updating or the app restarting.
Per-application routing and communications-device selection are outside this task.

## Verification

- WindowsAudioProvider.Tests: 18 passed, including the real read-only policy
  COM-interface probe, default roles/rollback, stale queued volume work,
  direction validation, opaque identity, and confirmed selection.
- AudioMixerWidget.Tests: 46/46, including pending/confirmed selection, focus,
  optional permissions, failed selection, and disconnects.
- PlatformBroker.Tests: 56 passed, including new control consent/lifecycle and
  strict request validation.
- WidgetSdk.Tests: 115/115; SDK compatibility: 14/14 with additive baseline changes.
- SettingsWidget.Tests: 67/67; WidgetBridge.Tests: 129/129.
- AudioMixerScrollHostTests: passed with the added selector traversal. Its old
  Microphone-to-Master adjacency assumptions were updated to the new focus order;
  offscreen reveal, live snapshot changes and reopen checks remain exercised.
- Complete Release native build and coherent managed runtime publication passed.
  After the final feedback-only adjustment, the managed runtime was republished
  and Audio Mixer, WindowsAudioProvider and SDK payload hashes matched current
  build outputs. The unchanged native production binary was reused.

No physical audio endpoint was changed during automated verification. The new
control permission is initially unset in the local Audio Mixer consent record;
enable Change speakers and microphone in widget permissions before testing.
Actual output/microphone switching and user acceptance remain pending. Candidate
integration, ticket closure and push have not been performed.
