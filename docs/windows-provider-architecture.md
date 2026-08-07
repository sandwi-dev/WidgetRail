# Windows provider architecture: Audio Mixer and Network Controls

Status: **typed SDK, authenticated broker transport, controller consent, and
simulator path implemented; real Windows providers remain planned**. This note uses
Microsoft documentation as the API authority. Items labeled **Documented
fact** describe published Windows behavior. Items labeled **Platform design**
are Game Bar Alternative decisions; their implementation status is called out
where it matters.

Audio Mixer and Network Controls must remain ordinary out-of-process widgets.
They receive bounded snapshots/events and invoke narrow broker commands; they
never receive COM interfaces, Windows handles, process IDs, endpoint IDs,
interface GUIDs, BSSIDs, profile XML, or credentials.

## Provider boundary

**Platform design.** Put both Windows integrations in a trusted capability-
broker process, not in `OverlayHost` and not in a widget worker:

```text
Windows callbacks -> trusted provider -> revisioned broker model/events
                                     -> capability check -> widget worker
widget command -> capability check -> provider command -> Windows API
```

The provider owns subscription handles, COM apartments, callback objects, OS
identity mapping, and recovery. A widget sees opaque IDs scoped to its grant,
sanitized labels, finite values, and stable error codes. The implemented v1
broker foundation already validates those bounds. Model revisions and
expected-revision command guards are future hardening so an action cannot
silently hit a replacement device, session, adapter, or profile after churn.

The implemented closed v1 broker vocabulary is deliberately smaller than the
eventual widgets:

| Capability | Implemented bounded authority |
| --- | --- |
| `system.audio.sessions.read.v1` | List sanitized render sessions and subscribe to bounded session-change events. |
| `system.audio.sessions.control.v1` | Set volume/mute for one opaque session while Interactive. |
| `system.network.read.v1` | Read sanitized connectivity/saved-profile state and subscribe to bounded network-change events. |
| `system.network.saved-profile.switch.v1` | Connect one opaque already-saved profile while Interactive; no profile creation or secrets. |

Endpoint master-volume/mute and capture-endpoint control need separately
versioned capabilities before implementation; capture control never implies
audio-sample access. The broker currently rechecks authenticated package,
publisher, and instance identity, manifest declaration, durable grant/deny
state, and lifecycle on each operation. Read operations are allowed only while
Visible or Interactive; control operations require Interactive. Destroying
revokes subscriptions, while Background retains only the latest bounded event.

The typed contract is connected end to end through the generic worker host,
bridge-owned authenticated broker companion, Settings consent flow, and
deterministic backend. It is callable by declared/granted widgets but is not a
working OS service: the production bridge still supplies only
`SimulatedPlatformBrokerBackend`. No real provider should be connected until
the remaining provider, audit, privacy, churn, and isolation gates below pass.
See [widget capabilities](capabilities.md) for the author-facing API.

## Audio provider

### Microsoft-documented API facts

- `IMMDeviceEnumerator` enumerates endpoint devices, retrieves the default
  endpoint for render/capture roles, and registers an
  `IMMNotificationClient`. The callback reports device add/remove/state/
  property events and default-device changes. See
  [IMMDeviceEnumerator](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immdeviceenumerator)
  and [device events](https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-events).
- `IMMDevice::Activate` can obtain `IAudioEndpointVolume`,
  `IAudioMeterInformation`, and `IAudioSessionManager2` for an endpoint. See
  [IMMDevice::Activate](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdevice-activate).
- `IAudioEndpointVolume` changes endpoint master volume/mute;
  `IAudioEndpointVolumeCallback` reports those changes. Scalar volume is
  bounded to 0.0–1.0, and an event-context GUID identifies self-originated
  changes. See
  [SetMasterVolumeLevelScalar](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmastervolumelevelscalar),
  [SetMute](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmute),
  and [RegisterControlChangeNotify](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-registercontrolchangenotify).
- `IAudioSessionManager2::GetSessionEnumerator` supplies the initial sessions
  for one endpoint. Microsoft warns that the enumerator alone can miss newly
  reported sessions, so clients should maintain their own list. New-session
  notifications require `RegisterSessionNotification`; calling the session
  enumerator's `GetCount` enables delivery during startup. See
  [GetSessionEnumerator](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager2-getsessionenumerator)
  and [RegisterSessionNotification](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager2-registersessionnotification).
- `IAudioSessionEvents` reports session volume/mute, display-name, state, and
  disconnect changes. `ISimpleAudioVolume` controls session volume/mute and
  also accepts an event-context GUID. See
  [IAudioSessionEvents](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nn-audiopolicy-iaudiosessionevents)
  and [ISimpleAudioVolume::SetMasterVolume](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-isimpleaudiovolume-setmastervolume).
- Session metadata can include a process ID through `IAudioSessionControl2`.
  That is an OS-facing identifier, not data that must cross the broker. See
  [IAudioSessionControl2](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nn-audiopolicy-iaudiosessioncontrol2).

### Platform design

Run a dedicated Core Audio MTA thread. On startup it should:

1. create one `IMMDeviceEnumerator` and register endpoint notifications;
2. resolve the default render endpoint for the chosen console/multimedia role;
3. activate endpoint volume and session-manager interfaces;
4. register endpoint, new-session, and per-session callbacks;
5. enumerate existing sessions and call `GetCount` as required by the session
   notification contract; and
6. publish one complete immutable snapshot before forwarding deltas.

On default-device change or invalidation, construct a replacement endpoint
model completely, atomically publish it, then unregister/release the old graph.
Use one provider event-context GUID to reconcile optimistic widget feedback
without suppressing changes from other clients.

The first safe command set is endpoint/session volume and mute. Capture-
endpoint control is a separate grant and may expose configured input volume and
mute, but never opens an audio stream or returns samples. Peak/activity meters
would reveal microphone use patterns and require a separate privacy review.

#### Output-device switching gate

**Documented fact.** `IMMDeviceEnumerator::GetDefaultAudioEndpoint` retrieves a
default endpoint; the documented `IMMDeviceEnumerator` method list contains no
system-default setter.

**Platform design inference.** Do not use reverse-engineered `PolicyConfig`
interfaces, registry writes, or shell automation. Until a supported Microsoft
API is selected and validated for the product's packaging/minimum-Windows
model, Audio Mixer may discover endpoints and react to an externally changed
default, but output-device switching must remain disabled. If no suitable
public API passes the spike, remove switching from the initial scope.

### COM and callback lifetime

**Documented fact.** COM must be initialized per calling thread, and every
successful `CoInitializeEx` (including `S_FALSE`) must be balanced with
`CoUninitialize`. Session notifications specifically require a non-UI MTA
initialized with `COINIT_MULTITHREADED`. See
[CoInitializeEx](https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-coinitializeex)
and [RegisterSessionNotification](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager2-registersessionnotification).

Audio callbacks must be nonblocking. Microsoft says not to unregister or
release the final WASAPI/EndpointVolume reference inside a callback. Successful
registration takes a reference to the callback; unregister before releasing
the provider interfaces.

**Platform design.** Callback methods only copy bounded fields into a lock-free
or short-held queue and signal the provider thread. All enumeration, COM calls,
model mutation, unregister, and final release occur on that owning MTA thread.
Never call widget IPC synchronously from a Core Audio callback.

## Network provider

### Microsoft-documented API facts

- `NotifyIpInterfaceChange` reports local IPv4/IPv6 interface changes.
  `NotifyNetworkConnectivityHintChange` reports aggregate connectivity-level
  and cost-hint changes on Windows 10 version 2004 or later. Registrations are
  canceled with `CancelMibChangeNotify2`. See
  [NotifyIpInterfaceChange](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-notifyipinterfacechange),
  [NotifyNetworkConnectivityHintChange](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-notifynetworkconnectivityhintchange),
  and [CancelMibChangeNotify2](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-cancelmibchangenotify2).
- `GetAdaptersAddresses` returns IPv4/IPv6 adapter/address information but is a
  synchronous, comparatively expensive snapshot operation. Microsoft
  recommends a preallocated 15 KiB working buffer rather than probing size on
  every call. See
  [GetAdaptersAddresses](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getadaptersaddresses).
- Native Wi-Fi starts with `WlanOpenHandle`. `WlanGetProfileList` returns basic
  saved-profile information in preference order; its returned memory must be
  freed with `WlanFreeMemory`. See
  [WlanOpenHandle](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanopenhandle)
  and [WlanGetProfileList](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlangetprofilelist).
- `WlanConnect` can connect by an existing profile name, returns immediately,
  and requires `WlanRegisterNotification` to learn success or terminal failure.
  An all-user profile also requires execute access. See
  [WlanConnect](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanconnect)
  and [WlanRegisterNotification](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanregisternotification).
- `WlanQueryInterface` can return interface state, current-connection
  attributes, radio state, RSSI, and other values. The current-connection query
  is among the Wi-Fi APIs that can return `ERROR_ACCESS_DENIED` without Windows
  precise-location consent. See
  [WlanQueryInterface](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanqueryinterface)
  and [Wi-Fi access/location changes](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes).
- Microsoft documents that BSSID-bearing Wi-Fi APIs, scans, the WinRT Wi-Fi
  namespace, and MSM notifications are location-sensitive. MSM notification
  registration requires the `wiFiControl` device capability; requesting it
  requires user location consent. Access can be denied or revoked.

### Platform design

Use two event sources behind one revisioned model:

1. IP Helper notifications drive aggregate/Ethernet state. Coalesce bursts and
   call `GetAdaptersAddresses` only after an event, initial subscription, or
   explicit recovery—not on a presentation timer.
2. One WLAN client handle per provider lifetime enumerates Wi-Fi interfaces and
   saved profiles. Register only the notification sources required for
   connection lifecycle; do not request `ALL`, MSM, scans, or BSSID data by
   default.

Initial saved-profile switching calls `WlanConnect` with
`wlan_connection_mode_profile`, waits asynchronously for matching ACM
completion/failure, then publishes the observed state. It never calls
`WlanSetProfile`, asks for a password, reads profile XML/key material, changes
profile preference, or connects to an unsaved discovery result. Disconnect and
radio-toggle commands are excluded until separately reviewed.

Current SSID/profile and signal presentation is conditional on Windows Wi-Fi/
location consent. The provider must expose `permission-required`, `denied`, and
`revoked` states without retry loops. Prompt timing must follow an explicit
controller action, and the broker's stable package/application identity must be
settled before relying on per-app Windows privacy controls.

WLAN callbacks only enqueue bounded notification codes and interface identity.
Unregister/close from the provider thread, never from the callback.
`CancelMibChangeNotify2` must likewise run outside the callback being canceled;
Microsoft warns that cancellation from that callback can deadlock.

## Privilege and privacy boundary

### Verified Windows constraints

- The classic Core Audio pages above target desktop apps and do not prescribe
  administrator elevation or a package capability for endpoint/session volume.
- Packaged/AppContainer access to privacy-sensitive resources is governed by
  Windows app capabilities. Microsoft's capability guidance says microphone
  capability covers access to the microphone audio feed, and users can revoke
  sensitive-resource access. See
  [app capability declarations](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations).
- WLAN operations can return `ERROR_ACCESS_DENIED`; all-user saved profiles
  require execute access. Current Wi-Fi identity/scan surfaces are also subject
  to Windows location consent, and MSM notifications require `wiFiControl`.

### Platform privacy decisions

- Run providers unelevated. Treat access-denied, disabled services, policy, and
  device loss as ordinary states; never auto-elevate or install a service/
  driver to bypass them.
- Audio snapshots omit raw process IDs, executable paths, icon paths, grouping
  GUIDs, and endpoint IDs. The broker may use them internally to derive a
  sanitized app label and opaque lifetime ID.
- Network snapshots omit BSSID, MAC/IP/DNS/gateway addresses, profile XML,
  authentication/cipher details, and keys. SSID/profile display names are still
  sensitive and leave the broker only under the Wi-Fi read grant.
- Logs contain stable error codes and opaque IDs, not profile names, SSIDs,
  endpoint names, application paths, or credentials.
- A capture-endpoint control grant is not permission to capture audio. Any
  future sample capture, loopback recording, or microphone activity meter needs
  a new capability and consent review.

## Deterministic provider simulators

**Implemented foundation plus planned fixtures.** `PlatformBroker` defines the
provider-neutral interface and a deterministic simulated backend. Its contract
tests cover the closed capability vocabulary, fail-closed consent, identity
binding, strict bounded requests, sanitized DTOs, saved-profile-only switching,
atomic consent updates, lifecycle/event coalescing, revocation, and
cancellation. The richer churn fixtures below and hardware integration remain
planned.

| Simulator | Required deterministic fixtures |
| --- | --- |
| Audio | Empty machine; endpoint add/remove/default change; session create/state/volume/disconnect; self versus external event-context GUID; device invalidation during a command; duplicate/out-of-order callback; Audio service unavailable. |
| Network | Ethernet up/down/cost change; no WLAN service/adapter; saved-profile list; connect success/failure/timeout; adapter removed mid-connect; location prompt/deny/revoke; stale completion after a newer command. |

Every fake event carries a sequence/revision and virtual timestamp. Tests assert
coalescing, stale-event rejection, controller-readable errors, cancellation,
and zero timer polling while unchanged. A simulator never shells out to
`netsh`, changes registry state, or touches the developer's real default audio
device/network.

Optional Windows integration tests run only on an explicitly opted-in machine:

- Core Audio can create a disposable test render session, observe it through
  session callbacks, and restore endpoint/session volume after testing. The
  Microsoft [EndpointVolume sample](https://learn.microsoft.com/en-us/windows/win32/coreaudio/endpointvolume)
  is reference behavior, not a production dependency.
- WLAN tests use pre-provisioned disposable profiles and dedicated hardware,
  record/restore the prior connection, and never create/read a profile secret.
  Location-denied and service-unavailable cases must remain simulator tests in
  ordinary CI.

## Implementation gates

The managed foundation now supplies typed SDK services/DTOs, versioned bounded
request/result/event contracts, nonce/identity-bound pipe transport, a closed
capability vocabulary, manifest/consent/lifecycle checks, controller grant/
deny/revoke UI, sanitized DTO validation, durable consent storage, coalesced
subscriptions, and an initial simulator. Windows workers also have pre-launch
Job Object containment with a trusted memory ceiling, one-process limit, and
kill-on-close cleanup. These are necessary building blocks, not real OS
integration or a complete hostile-code sandbox.

Real OS providers remain planned until all of these exist:

1. stale-revision command rules and a security audit/history surface;
2. production provider identity and stronger community-worker isolation,
   including the AppContainer decision;
3. the full denial/churn/race simulator matrix above;
4. opt-in Windows hardware tests and hidden/background wakeup measurements;
5. a public supported output-routing decision; and
6. privacy review proving that raw OS identifiers and secrets cannot cross the
   broker.

Until those gates pass, Audio Mixer and Network Controls remain roadmap items,
not working first-party widgets. Their typed capability contracts currently
exercise the simulator only.
