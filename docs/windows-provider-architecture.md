# Windows provider architecture: Audio, Network, and Games & Apps

Status: **typed SDK, authenticated broker transport, controller consent,
simulator path, and narrow real Core Audio, Windows network, and Start Menu
app-library providers implemented; automated packaged Release verification
passes, while hardware/privacy and performance verification remains open**. This note uses
Microsoft documentation as the API authority. Items labeled **Documented
fact** describe published Windows behavior. Items labeled **Platform design**
are WidgetRail decisions; their implementation status is called out
where it matters.

Audio Mixer, Network Controls, and Games & Apps must remain ordinary
out-of-process widgets.
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
| `system.audio.output.control.v1` | Set master volume/mute for the current default multimedia render endpoint while Interactive, or through one exact snapshot-bound dashboard gesture while Visible; no device switching. |
| `system.audio.devices.read.v1` | Read sanitized endpoint names/direction/default markers and subscribe to changes; no raw endpoint IDs. |
| `system.audio.input.read.v1` | Read/watch volume and mute for the current default capture endpoint; no sample capture. |
| `system.audio.input.control.v1` | Set volume/mute for the current default capture endpoint while Interactive. |
| `system.network.read.v1` | Read sanitized connectivity/saved-profile state and subscribe to bounded network-change events. |
| `system.network.details.read.v1` | Read bounded display-ready IP, default-gateway, and DNS values for one preferred connection; subscribe to revision-only invalidation and re-query current truth. No raw interface or route identity. |
| `system.network.saved-profile.switch.v1` | Connect one opaque already-saved profile while Interactive; no profile creation or secrets. |
| `system.network.wifi.read.v1` | Read the cached available-network snapshot, explicitly request one scan while Interactive, and subscribe to bounded scan snapshots. |
| `system.network.wifi.connect.v1` | Connect one current generation-bound saved/open scan result while Interactive. Bundled Network Controls may additionally use its trusted masked host prompt for one supported WPA2/WPA3 Personal result; the secret and profile XML bypass the worker/public SDK. |
| `system.network.wifi.radio.read.v1` | Read/watch effective software/hardware/policy Wi-Fi radio state. |
| `system.network.wifi.radio.control.v1` | Request software Wi-Fi radio On/Off while Interactive; hardware/policy remains authoritative. |
| `system.network.bluetooth.read.v1` | Read/watch sanitized Bluetooth radio/discovery/device state; no native IDs. |
| `system.network.bluetooth.radio.control.v1` | Request Bluetooth software radio On/Off while Interactive. |
| `system.activity.recent.read.v1` | Read/watch bounded eligible running foreground observations as opaque IDs. |
| `system.apps.library.read.v1` | Page sanitized app-library names/kinds and resolve authority-scoped durable SavedIds to current short-lived launch IDs while Visible or Interactive. |
| `system.apps.library.launch.v1` | Launch one current exact provider-revalidated opaque app ID while Interactive. |

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
`WindowsBluetoothPlatformBackend`, `WindowsActivityPlatformBackend`, and
`WindowsAppLibraryProvider`. They are
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
   The exact first-party host-owned credential prompt may create/connect one
   attempt-unique WPA2/WPA3 Personal profile without exposing the secret to the
   widget worker or JSON. The edit control, native frame, managed command, and
   profile XML are bounded mutable owners and are cleared. The provider creates
   without overwrite, stores a random attempt token as Native Wi-Fi per-profile
   custom user data, and rereads/fixed-time-compares it before rollback. A
   neighbor replacement, unreadable token, or failed delete is explicit and
   never authorizes deletion by SSID/common name. Success clears the token and
   retains the Windows-owned profile.
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
- `UnpairAsync` returns the closed `DeviceUnpairingResultStatus` result set;
  access denial, an operation already in progress, and failure are not success.
  See [UnpairAsync](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformationpairing.unpairasync?view=winrt-26100).
- Bluetooth communication is profile-specific. GATT requires knowledge of the
  intended services/characteristics, while RFCOMM establishes a socket to a
  service. The public API therefore does not justify a generic device-level
  Connect/Disconnect promise. See [Bluetooth GATT client](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client)
  and [Bluetooth RFCOMM](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/send-or-receive-files-with-rfcomm).

### Implemented boundary and remaining work

Bluetooth radio read/control, event-driven device enumeration, pairing,
destructive removal, and management fallback are separate closed broker
capabilities. The trusted WinRT
adapter publishes bounded opaque IDs, sanitized names, and paired/present/
connected state; native device IDs, addresses, handles, and pairing secrets
never cross the broker. Software-radio changes reconcile effective state and
report hardware/user/system denial, no adapter, unavailable, or partial
failure.

Pairing resolves one current opaque ID to a retained native association
endpoint only inside the trusted provider, calls `PairAsync`, returns a bounded
typed Windows outcome, and refreshes/publishes authoritative state even after
failure or cancellation. It does not claim that a Bluetooth profile connected.
Removal similarly resolves only a current paired opaque ID, calls `UnpairAsync`,
maps the closed Windows outcome, and refreshes/publishes authoritative state in
all completion paths. Native IDs and provider exceptions remain host-only.
The separate manage operation validates the same opaque ID and opens
`ms-settings:bluetooth` without placing that native ID in the URI. That fallback
lets Windows own unsupported ceremonies and profile-specific management.
Physical remove/re-pair still needs reversible hardware evidence.
Generic Connect/Disconnect remains out of scope; a future GATT or RFCOMM
integration must declare its exact profile/service authority and resource/
lifecycle policy.

## Foreground-activity provider

The trusted activity backend lazily installs out-of-context foreground and
destroy WinEvent hooks after the first authorized read, then updates a bounded
16-entry running-app model without polling. It excludes shell/secure/overlay/
widget windows, sanitizes version-resource names, rotates the opaque ID when a
process lifetime changes, and publishes no PID, path, command line, HWND, or
process key. Every entry is currently classified conservatively as
`Application`; most-recent is ordering metadata, not a game/foreground claim.

Foreground activation is not part of this capability. The unreliable switch
operation and broad bridge delegation were removed; it cannot relaunch an
exited app or target a process/window. Observation remains event-driven until
backend disposal after it first starts; consent revocation blocks broker
delivery and cancels in-flight work, while immediate native-observer shutdown/
history clear on revocation remains future hardening. Recent Apps is retained
only as the [read-only activity reference](recent-apps.md), not a bundled widget.

## Start Menu, AppsFolder, and Steam application-library provider

The trusted application-library provider merges current-user/all-user Start
Menu Programs shortcuts, the current user's Shell `AppsFolder`, and registered
Steam library manifests. Shortcut
enumeration is bounded by candidate count, directory depth, shortcut size,
catalog size, and sanitized display-name length; it never follows reparse
points. It accepts `.lnk` registrations whose
resolved target is an `.exe` or `.com`, deduplicates an internal target/
arguments identity, and returns random short-lived provider launch IDs plus one
host-only stable identity. The broker uses the latter with a persisted host key
and authenticated publisher/package IDs to derive a non-reversible durable
SavedId. Raw stable identities, paths, targets, arguments, shortcut
fingerprints, AUMIDs, package identities, PIDs, and HWNDs stay inside the host.

The normalized owner does not switch on these launch formats. The internal
`IGameLibrarySource` contract projects bounded installed/available records with
one stable opaque source identity, sanitized attribution, supported actions,
conservative kind, artwork revision, source health, and monotonic source
version. `WindowsInstalledGameLibrarySource` owns Start Menu/AppsFolder exact
resolution, icon demand, and constrained activation; `SteamGameLibrarySource`
owns manifest resolution and constrained Steam launch. Their raw authority is
held behind source-private opaque record identities, so the authoritative
library can merge, cache, and route an exact operation without learning a path,
AUMID, Steam AppId, or source-specific launch rule. Each source retains its own
last-good snapshot when it reports an isolated failure, rejects a late refresh
generation, and cancels/drains admitted work at terminal disposal. The
`WindowsAppLibraryProvider` is the exact terminal owner for both sources: its
single lifetime cancellation is linked into scan, resolve, launch, and artwork
operations, and the existing scan gate is also the publication-drain boundary.
Composite broker shutdown reaches that terminal path, waits within the bounded
deadline for cooperative publication drain, disposes every
distinct source exactly once even when another reports a bounded drain failure,
and clears cached authority and artwork only after terminal publication has
been closed. An uncooperative operation yields a bounded terminal failure and
cannot publish if it later completes. Repeated disposal joins the same outcome;
calls admitted after the terminal transition fail before Shell or source work
begins.

AppsFolder enumeration runs on one process-wide bounded STA queue, visits at
most 2,048 Shell items, and retains only sanitized display text plus a strict
canonical AUMID. A malformed or disappearing item is skipped without failing
the other source. The AUMID remains provider-private and contributes only to a
host identity/revalidation digest; neither it nor a Shell object crosses IPC.

Steam discovery is separately bounded to 32 library roots, 4,096 top-level
manifests, 1 MiB per metadata file, and bounded quoted-token parsing. A
manifest is accepted only when its filename and payload contain the same
positive numeric AppId. These reviewed registrations are the only current
source classified as Game. AppIds, manifest paths, and library paths stay
provider-private.

Widgets persist only SavedIds in private state. The read capability's bounded
resolver accepts at most 64 unique SavedIds, refreshes the provider, preserves
request order, omits unavailable registrations, and returns fresh launch IDs.
Another widget authority cannot correlate or resolve those SavedIds.

The running-app observer also supports an explicit portable-registration lane.
It admits only visible top-level windows whose process is in the current user
session and is not elevated. Packaged or installed identities retain the normal
catalog owner. For an otherwise unmatched local DOS-drive `.exe`, the provider
rejects relative, UNC, device, remote-drive, reparse, and canonical-alias paths,
then captures its canonical handle path plus volume/file ID. The caller-visible
path envelope is at most 32,762 characters so the private extended-DOS native
form, prefix, and terminator remain inside the bounded Windows buffer. Actual
files remain subject to Windows component and volume limits. Registration
re-runs the bounded observation and requires the exact observation revision and
process-instance evidence.

Portable records are stored in canonical package-scoped provider documents at
`<resolved installed catalog root>/broker/portable-apps`, with adjacent
exclusive locking, atomic replacement, strict
duplicate/unmapped-property rejection, a 2 MiB document limit, and at most 64
records. There is no silent eviction. The durable authority is
publisher/package rather than a worker instance, so a worker restart keeps its
records. An unsigned content-generation replacement receives its distinct
verified publisher authority and cannot inherit the old generation's records;
restoring those exact reviewed bytes restores that exact authority. Another
package cannot query, forget, clear, or launch them. Settings local-data
clearing removes only the current authority under revision confirmation. Exact
package uninstall instead retires the package-grouped store for every installed
or pruned publisher generation before its catalog commit. Default and explicit
custom catalogs therefore never share portable records. The catalog's bounded
staging marker makes interrupted cleanup idempotently resumable and blocks only
recreation of that same package until recovery completes.

Resolution and launch each recapture the canonical path and volume/file ID.
A moved, missing, or replaced executable is omitted until the user registers a
fresh exact observation; re-registration keeps the same SavedId while rotating
the private authority. The file ID proves file replacement, not content
integrity. During launch the provider retains non-reparse directory and file
handles across the path-based `Process.Start` call, preventing those namespace
objects from being renamed, deleted, or rewritten between the final identity
check and process creation; Windows is not asked to launch by handle. Launch
uses `UseShellExecute=false`, an empty argument list, no verb or elevation, and
the executable's containing directory as its explicit working directory.
Installed catalog registrations always take precedence and are never persisted
or forgotten through the portable lane, even when their executable is not
eligible for new portable registration.

Launch is a separate Interactive-only capability. The provider resolves a
current opaque ID and re-enumerates the exact source on its STA lane. A
shortcut requires exactly one unchanged scope, target identity, full path, and
content fingerprint before Shell `open` with no supplied arguments, working
directory, elevation verb, or owner window. An AppsFolder entry requires
exactly one unchanged canonical AUMID/revalidation identity before
`IApplicationActivationManager.ActivateApplication` with null arguments; its
returned PID is discarded. Stale, moved, modified, duplicated, and unknown
registrations fail closed. For Steam, launch re-reads the exact manifest and
requires the same identity, path, and content hash before Shell-opening only
the constrained `steam://rungameid/<numeric-id>` URI.

Installed Microsoft/Xbox games require the exact current package generation,
AUMID, and bounded `MicrosoftGame.config` evidence. Epic and GOG are separately
opt-in host sources. Epic reads only its fixed ProgramData installed-manifest
root. GOG reads only the fixed machine-wide GOG game registry in the 32-bit and
64-bit Windows views, then validates the matching bounded
`goggame-<product-id>.info` file. Both sources keep provider identifiers, paths,
and file evidence inside the trusted provider and publish explicit per-source
health. Epic requires a fresh exact generation before its constrained launcher
adapter can run. GOG discovery is best-effort installed evidence only: it is
not an official GOG API or exhaustive catalog, exposes no Launch capability,
and never invokes a Galaxy process.

The present source does not enumerate other launcher or account libraries.
Start Menu and AppsFolder entries remain conservatively classified as Application.
AppsFolder is a
Windows Shell view, not a guarantee that every installed package, alias, or
launcher-owned game is returned under every Windows policy. For the user's
curated library only, the provider resolves the shortcut or AppsFolder Shell
icon on demand, rasterizes it to a 48 by 48 RGBA PNG, and exposes only bounded
pixels. Discovery pages remain text-only; at most 32 resolved icons and 384 KiB
of source pixels cross one broker response, with a semantic fallback for
missing or malformed icons. Shortcut, executable, icon-location, AUMID,
package, and PID data never cross the broker boundary. See the [Games & Apps
reference](games-and-apps.md).

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
Job Object containment with complete process-tree accounting, UI restrictions,
and kill-on-close cleanup. Installed/community workers require a
capability-free Low-integrity AppContainer and exact-SID/Low-label/PID-bound
broker endpoint in addition to nonce/full-identity authentication. The audio
implementation adds a lazy, event-driven Core Audio session backend on top of
those pieces; publisher trust and production-support evidence remain separate.

These providers remain deliberately limited prototypes while these production
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

Audio Mixer, Network Controls, and Games & Apps are implemented first-party
integration references. They do not imply output/default-role switching,
microphone sample capture, Bluetooth unpair/generic connection, current-SSID
privacy access, launcher-library coverage, authoritative game
classification, or production security support. Hardware/privacy/performance gates remain. See the
[Network Controls reference](network-controls.md) for its authoring,
controller, test, and packaging contract.
