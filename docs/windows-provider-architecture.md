# Windows provider architecture: Audio Mixer and Network Controls

Status: **typed SDK, authenticated broker transport, controller consent,
simulator path, and narrow real Core Audio and Windows network providers
implemented; automated packaged Release verification passes, while the network
provider/widget remain under hardware/privacy and performance verification**. This note uses
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
| `system.audio.output.read.v1` | Read volume/mute for the current default multimedia render endpoint and subscribe to bounded changes; no endpoint identity. |
| `system.audio.output.control.v1` | Set master volume/mute for the current default multimedia render endpoint while Interactive; no device switching. |
| `system.audio.devices.read.v1` | Read sanitized endpoint names/direction/default markers and subscribe to changes; no raw endpoint IDs. |
| `system.audio.input.read.v1` | Read/watch volume and mute for the current default capture endpoint; no sample capture. |
| `system.audio.input.control.v1` | Set volume/mute for the current default capture endpoint while Interactive. |
| `system.network.read.v1` | Read sanitized connectivity/saved-profile state and subscribe to bounded network-change events. |
| `system.network.saved-profile.switch.v1` | Connect one opaque already-saved profile while Interactive; no profile creation or secrets. |
| `system.network.wifi.read.v1` | Read the cached available-network snapshot, explicitly request one scan while Interactive, and subscribe to bounded scan snapshots. |
| `system.network.wifi.connect.v1` | Connect one current generation-bound saved/open scan result while Interactive; no credential entry. |
| `system.network.wifi.radio.read.v1` | Read/watch effective software/hardware/policy Wi-Fi radio state. |
| `system.network.wifi.radio.control.v1` | Request software Wi-Fi radio On/Off while Interactive; hardware/policy remains authoritative. |
| `system.network.bluetooth.read.v1` | Read/watch sanitized Bluetooth radio/discovery/device state; no native IDs. |
| `system.network.bluetooth.radio.control.v1` | Request Bluetooth software radio On/Off while Interactive. |
| `system.activity.recent.read.v1` | Read/watch bounded eligible running foreground observations as opaque IDs. |
| `system.activity.recent.activate.v1` | Switch to one exact still-running observation while Interactive; no launch. |

Endpoint master-volume/mute, sanitized device visibility, and default-capture
volume/mute have separate grants. Output/default-role switching and audio-
sample capture remain outside this contract; capture volume control never
implies sample access. The broker currently rechecks authenticated package,
publisher, and instance identity, manifest declaration, durable grant/deny
state, and lifecycle on each operation. Read operations are allowed only while
Visible or Interactive; control operations require Interactive. Destroying
revokes subscriptions; Background denies these capabilities. Lifecycle/consent
changes also cancel already in-flight provider operations.

The typed contract is connected end to end through the generic worker host,
bridge-owned authenticated broker companion, Settings consent flow, and
deterministic backends. The trusted bridge composes
`WindowsAudioPlatformBackend`, `WindowsNetworkPlatformBackend`,
`WindowsBluetoothPlatformBackend`, and `WindowsActivityPlatformBackend`. They are
working narrow OS services for declared and explicitly granted widgets; this
does not close their hardware, privacy, performance, or hostile-code release
gates. See [widget capabilities](capabilities.md) for the author-facing API.

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

### Implemented provider design

A first audio request lazily starts a dedicated Core Audio MTA thread. It owns
the native object graph and:

1. creates one `IMMDeviceEnumerator` and registers endpoint notifications;
2. resolves the default multimedia render endpoint;
3. activates the session-manager interface;
4. registers new-session and per-session callbacks;
5. enumerates existing sessions and calls `GetCount` as required by the session
   notification contract; and
6. publishes one complete immutable snapshot before forwarding changed
   snapshots as bounded, coalesced events.

On default-device change or invalidation, construct a replacement endpoint
model completely, atomically publish it, then unregister/release the old graph.
Use one provider event-context GUID to reconcile optimistic widget feedback
without suppressing changes from other clients.

The implemented command set includes per-session and endpoint master volume/
mute through opaque targets, plus volume/mute for the current default capture
endpoint. Device enumeration publishes sanitized input/output labels and
default markers. It does not provide an endpoint/default-role setter. A
microphone activity meter or sample capture would access capture data and
requires a different capability and privacy review.

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

A healthy endpoint with no application sessions returns an empty list. A
failed endpoint bind or enumeration instead returns `platform_unavailable`,
and the coalesced session event carries `IsAvailable = false`; this prevents a
live native failure from being rendered as “nothing is playing.” The next
explicit read performs one bounded recovery attempt, and successful recovery
publishes `IsAvailable = true`, including when the recovered list is empty.

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
- `WlanScan` requests an asynchronous available-network scan and clients use
  ACM notification plus a bounded timeout before reading results.
  `WlanGetAvailableNetworkList` retrieves the resulting available networks.
  Both can return `ERROR_ACCESS_DENIED` without precise-location consent. See
  [WlanScan](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanscan),
  [WlanGetAvailableNetworkList](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlangetavailablenetworklist),
  and [Wi-Fi access/location changes](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes).
- `WlanSetInterface` with `wlan_intf_opcode_radio_state` changes only the
  software radio state of a specific PHY; it cannot change a hardware radio
  switch. See
  [WlanSetInterface](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlansetinterface).

### Platform design

The active implementation uses two native API families and three change
registrations behind one bounded model. IP Helper's read-only
`GetNetworkConnectivityHint` supplies coarse aggregate connectivity:

1. IP Helper `NotifyIpInterfaceChange` and
   `NotifyNetworkConnectivityHintChange` callbacks drive aggregate/Ethernet
   refreshes. The connectivity-hint entrypoint is optional on older Windows and
   fails soft; interface notifications remain. Coalesce bursts; never refresh
   on a presentation timer. The current backend classifies managed
   `NetworkInterface` snapshots with a route-table-only `GetBestInterface`
   preference rather than returning address/interface identity to widgets.
2. One WLAN client handle per provider lifetime enumerates Wi-Fi interfaces and
   saved profiles. A consentless `wlan_intf_opcode_radio_state` query
   distinguishes enabled-but-disconnected Wi-Fi from explicit hardware or
   software radio-off; it never queries `current_connection`. Register ACM only
   for connection lifecycle; never request `ALL`, MSM, scans,
   available-network lists, or BSSID data.

Failed IP-interface, connectivity-hint, or ACM registrations remain degraded;
an explicit subsequent read retries the missing registration. The provider
does not clear degraded health merely because a cached snapshot can still be
read.

Initial saved-profile switching calls `WlanConnect` with
`wlan_connection_mode_profile` and acknowledges only that the request was
accepted. Matching ACM completion/failure later updates the authoritative
`Connecting`/`Failed` attempt state and publishes the observed snapshot. It
never calls
`WlanSetProfile`, asks for a password, reads profile XML/key material, changes
profile preference, or connects to an unsaved discovery result. Disconnect and
radio-toggle commands are excluded until separately reviewed.

Version 1 does not automatically query location-sensitive current-connection
identity or signal. Wi-Fi details therefore default to `PrivacyRestricted` and
the active profile/signal fields remain absent. Saved-profile enumeration alone
must not be presented as current-connection evidence. A future explicit Windows
access-request flow needs its own capability/privacy review, stable packaged
application identity, controller-triggered prompt, and deterministic
required/denied/revoked states without retry loops.

### Implemented available-network and software-radio path

The available-network broker/SDK contracts, provider, bundled Network Controls
surface, manifest/catalog declarations, Settings consent copy, and dedicated
behavior tests are implemented behind separate `system.network.wifi.read.v1`
and `system.network.wifi.connect.v1` capabilities. Radio read/control is
implemented as separate `system.network.wifi.radio.*.v1` grants rather than an
extension of `system.network.read.v1` or the saved-profile-switch grant:

1. An explicit Interactive controller action aligns the location-sensitive
   Windows call with the user's intent. A denial or revocation
   returns a stable privacy state and never starts a retry/prompt loop.
2. The provider issues one `WlanScan`, waits for ACM completion or a bounded
   timeout, then reads one `WlanGetAvailableNetworkList` snapshot. It does not
   scan while Background, on dashboard selection, or on a timer.
3. Each row crosses the broker as sanitized presentation plus a
   generation-bound opaque scan ID. The ID expires on the next scan or provider
   generation. BSSID, interface GUID, raw SSID bytes, authentication structures,
   profile XML, and keys never cross the broker or enter logs.
4. Connection accepts current saved-profile-backed and unsaved open results.
   A later host-owned credential prompt may create/connect a new WPA/WPA2/WPA3
   Personal profile without exposing the secret to the widget worker.
   Enterprise/802.1X, certificate, SIM, domain-credential, hidden-network, and
   captive-portal provisioning are unsupported initially.
5. Software radio control uses `WlanSetInterface` only after a separate
   Interactive grant/consent check. Hardware-off, airplane-mode,
   administrator policy, service loss, or unsupported PHY remains authoritative
   and is never represented as a successful toggle. Multi-PHY mutation either
   rolls back or returns typed `partial_failure`, then publishes the refreshed
   authoritative state.

WLAN callbacks only enqueue bounded notification codes and interface identity.
Unregister/close from the provider thread, never from the callback.
`CancelMibChangeNotify2` must likewise run outside the callback being canceled;
Microsoft warns that cancellation from that callback can deadlock.

The implemented provider is lazy: construction and event subscription do not
open native handles. The first read/control starts its dedicated MTA owner.
Command and event queues are bounded, status/profile reads use the last complete
snapshot, burst callbacks coalesce, and disposal unregisters native resources
on the owner thread. No provider timer runs while the system is unchanged.

## Bluetooth provider

### Microsoft-documented API facts

- `Windows.Devices.Radios.Radio` enumerates radios, reports kind/state, requests
  access, and can request an On/Off state subject to hardware and policy. See
  [Radio](https://learn.microsoft.com/en-us/uwp/api/windows.devices.radios.radio?view=winrt-26100).
- `DeviceWatcher` performs initial enumeration and then reports added, updated,
  and removed devices. See
  [DeviceWatcher](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.devicewatcher?view=winrt-26100).
- `DeviceInformationPairing` exposes explicit `PairAsync` and `UnpairAsync`
  operations. Desktop UI-dependent pairing objects must be associated with the
  owner window. See
  [DeviceInformationPairing](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformationpairing?view=winrt-26100).
- Bluetooth communication is profile-specific. GATT requires knowledge of the
  intended services/characteristics, while RFCOMM establishes a socket to a
  service. The public API therefore does not justify a generic device-level
  Connect/Disconnect promise. See [Bluetooth GATT client](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)
  and [Bluetooth RFCOMM](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/send-or-receive-files-with-rfcomm).

### Implemented boundary and remaining work

Bluetooth radio read/control and event-driven device enumeration are
implemented as separate closed broker capabilities. The trusted WinRT adapter
publishes bounded opaque IDs, sanitized names, and paired/present/connected
state; native device IDs, addresses, handles, and pairing secrets never cross
the broker. Software-radio changes reconcile effective state and report
hardware/user/system denial, no adapter, unavailable, or partial failure.

Pair/unpair is not implemented. A future host-owned pairing ceremony requires
desktop owner-window, consent, cancellation, and hardware evidence. Generic
Connect/Disconnect remains out of scope; a future GATT or RFCOMM integration
must declare its exact profile/service authority and resource/lifecycle policy.

## Foreground-activity provider

The trusted activity backend lazily installs out-of-context foreground and
destroy WinEvent hooks after the first authorized read, then updates a bounded
16-entry running-app model without polling. It excludes shell/secure/overlay/
widget windows, sanitizes version-resource names, rotates the opaque ID when a
process lifetime changes, and publishes no PID, path, command line, HWND, or
process key. Every entry is currently classified conservatively as
`Application`; most-recent is ordering metadata, not a game/foreground claim.

Activation accepts only one current opaque observation, revalidates the exact
window/process lifetime, restores it if minimized, and requests a normal
`SetForegroundWindow`. It cannot relaunch an exited app or target arbitrary
processes. Windows may refuse the switch. Observation remains event-driven
until backend disposal after it first starts; consent revocation blocks broker
delivery/activation and cancels in-flight work, but immediate native-observer
shutdown/history clear on revocation is future hardening. See the
[Recent Apps reference](recent-apps.md).

## Privilege and privacy boundary

### Verified Windows constraints

- The classic Core Audio pages above target desktop apps and do not prescribe
  administrator elevation or a package capability for endpoint/session volume.
- Packaged/AppContainer access to privacy-sensitive resources is governed by
  Windows app capabilities. Microsoft's capability guidance says microphone
  capability covers access to the microphone audio feed, and users can revoke
  sensitive-resource access. See
  [app capability declarations](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations).
- Community widget AppContainers deliberately receive zero Windows capability
  SIDs, including no network/microphone authority. The trusted broker process,
  not the widget worker, owns the provider APIs and returns only the narrow
  declared/consented/lifecycle-valid typed results.
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
- Network snapshots omit BSSID, MAC/IP/DNS/gateway addresses, interface GUIDs,
  profile XML, authentication/cipher details, and keys. Version 1 does not
  automatically query the active SSID/signal. Saved-profile display names are
  still sensitive and leave the broker only under the network read grant.
- Logs contain stable error codes and opaque IDs, not profile names, SSIDs,
  endpoint names, application paths, or credentials.
- A capture-endpoint control grant is not permission to capture audio. Any
  future sample capture, loopback recording, or microphone activity meter needs
  a new capability and consent review.

## Deterministic provider simulators

**Implemented foundation plus planned fixtures.** `PlatformBroker` defines the
provider-neutral interface and a deterministic simulated backend. Its contract
tests cover the closed capability vocabulary, fail-closed consent, identity
binding, strict bounded requests, sanitized DTOs, explicit available-network
scan/read, generation-bound saved/open connection targeting,
atomic consent updates, lifecycle/event coalescing, revocation, and
cancellation. The richer churn fixtures below and hardware integration remain
planned.

| Simulator | Required deterministic fixtures |
| --- | --- |
| Audio | Empty machine; endpoint add/remove/default change; session create/state/volume/disconnect; self versus external event-context GUID; device invalidation during a command; duplicate/out-of-order callback; Audio service unavailable. |
| Network | Ethernet up/down/cost change; no WLAN service/adapter; explicit scan success/timeout/deny; generation rollover; saved/open connect success/failure/timeout; credential-required/unsupported authentication; adapter removed mid-connect; stale completion after a newer command. |

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

The managed foundation supplies typed SDK services/DTOs, versioned bounded
request/result/event contracts, nonce/identity-bound pipe transport, a closed
capability vocabulary, manifest/consent/lifecycle checks, controller grant/
deny/revoke UI, sanitized DTO validation, durable consent storage, coalesced
subscriptions, and an initial simulator. Windows workers also have pre-launch
Job Object containment with a trusted memory ceiling, one-process limit, UI
restrictions, and kill-on-close cleanup. Installed/community workers require a
capability-free Low-integrity AppContainer and exact-SID/Low-label/PID-bound
broker endpoint in addition to nonce/full-identity authentication. The audio
implementation adds a lazy, event-driven Core Audio session backend on top of
those pieces; publisher trust and production-support evidence remain separate.

Both providers remain deliberately limited prototypes while these production
gates are open:

1. stale-revision command rules and a security audit/history surface;
2. publisher signing/revocation, CPU and disk/profile quotas/cleanup, and
   broader AppContainer/broker abuse evidence;
3. the full denial/churn/race simulator matrix above;
4. opt-in Windows hardware tests and hidden/background wakeup measurements;
5. a public supported output-routing decision; and
6. privacy review proving that raw OS identifiers and secrets cannot cross the
   broker.

Win32k system-call disable is not an active mitigation because its tested
configuration prevented CoreCLR DLL initialization (`0xC0000142`); Job Object
UI restrictions remain enabled.

Audio Mixer, Network Controls, and Recent Apps are implemented first-party
integration references. They do not imply output/default-role switching,
microphone sample capture, Bluetooth pairing/generic connection, current-SSID
privacy access, or production security support. Hardware/privacy/performance gates remain. See the
[Network Controls reference](network-controls.md) for its authoring,
controller, test, and packaging contract.
