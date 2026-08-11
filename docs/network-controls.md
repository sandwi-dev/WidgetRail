# Network Controls reference

Status: **implemented and packaged prototype, automated Release verification
passed 2026-08-10**.
The typed SDK, authenticated capability transport, consent UI, lifecycle
enforcement, real event-driven Windows provider, first-party widget/worker,
trusted catalog entry, and Release packaging hooks exist. The focused network,
Bluetooth, broker, SDK, and widget suites plus the complete Release verifier
pass at milestone `8e8c90a`. The split Wi-Fi/Bluetooth presentation and real
pairing flow still need packaged controller, visual, privacy, and reversible
hardware verification. This is not yet a shipped or production-support claim.

Network Controls is the second first-party system-control reference widget,
after Audio Mixer. It must use the same public declarative SDK, worker
bootstrap, catalog, broker, permission, and lifecycle surfaces available to a
community widget. It is not a privileged `OverlayHost` panel.

## Implemented scope

The initial surface is intentionally narrow:

- show aggregate `None`, `Local`, or `Internet` connectivity;
- distinguish `None`, `Ethernet`, `Wifi`, and `Other` transport without
  disclosing raw adapter identity;
- report `Available`, `NoAdapter`, `RadioOff`, or `ServiceUnavailable` Wi-Fi
  availability;
- represent current-profile/signal access as `Available`, `PrivacyRestricted`,
  or `Unavailable` rather than guessing;
- read the provider's last bounded available-network snapshot without starting
  a scan;
- start one explicit available-network scan only after a controller action
  while the widget is Interactive;
- render currently visible networks with sanitized name, signal, security,
  credential-required, connected, and saved-profile state; and
- connect an exact visible saved-profile, unsaved open network, or supported
  WPA2/WPA3 Personal network by its opaque scan ID while Interactive; protected
  connections use a masked host-owned prompt that bypasses the widget worker;
- present Wi-Fi and Bluetooth as separate controller views rather than one
  overlong mixed list; and
- switch those views with LB/RB or a focused segmented-tab A action while
  retaining independent focus memory.

Focus, tab selection, radio state, and connection state are separate concepts.
A focused Wi-Fi/device row is only the current controller target; it must not
look connected or toggle merely because it has focus. The selected segmented
tab names the visible section, while connected/paired/radio-On state comes only
from the provider snapshot. Completing these semantics under scan/device churn
is tracked as [GBA-034](known-issues.md).

The bundled surface never prompts inside the widget process or stores a
password. The trusted host creates an attempt-unique per-user WPA2/WPA3
Personal profile without overwrite and tags it with random per-profile custom
user data. Terminal rollback rereads that tag and deletes only an exact match;
a missing, changed, or unreadable tag and a failed deletion are explicit
degraded outcomes, never permission to delete by SSID or common profile name.
Successful connection clears the temporary tag and retains the Windows-owned
profile. The host does not read stored profile XML or key material,
edit/delete pre-existing profiles, expose BSSID/MAC/IP/DNS/gateway values,
open captive portals, change airplane mode,
disconnect, or silently reorder Windows profile preference. It does not shell
out to `netsh`, edit the registry, install a service/driver, or elevate.

## Managed responsibility boundaries

The worker keeps one committed state and lifecycle owner. Before the current
split, `NetworkControlsWidget` also contained provider normalization and merge
rules, command admission/error mappings, string action routing, stable element
identity, and all declarative view construction. Those responsibilities now
have named internal boundaries:

- `NetworkControlsProviderPolicy` normalizes provider snapshots and derives
  authoritative selection, busy, status, and view-state outcomes.
- `NetworkControlsCommandPolicy` admits commands and maps bounded Wi-Fi and
  Bluetooth results without receiving host services or mutable widget state.
- `NetworkControlsActionPolicy` maps the closed action vocabulary to typed
  routes.
- `NetworkControlsPresentation` builds a view only from one immutable
  `NetworkControlsPresentationState` captured under the widget state lock.
- `NetworkControlsWidget` alone owns lifecycle callbacks, host-service calls,
  command execution, committed provider state, and invalidation.

The coordination inventory remains deliberately small: one widget state lock,
one command semaphore, and one active-run generation. The previous field-owned
run cancellation source and two detached provider-observer task roots are gone;
one SDK `Active` latest-operation lane now owns and drains the combined network
and Bluetooth observation run. A linked cancellation source exists only inside
the Wi-Fi observer to stop its sibling status, scan, and radio event loops when
one closes. The extracted policies and presenter own no locks, semaphores,
tasks, cancellation registries, host services, or persistent mutable
collections.

Cross-boundary dependencies are values rather than shared mutable state. The
widget passes provider records into pure policies, applies returned value
records while holding its existing lock, and gives the presenter cloned Wi-Fi
and Bluetooth lists in a single state capture. Commands return typed admission
or feedback values; stable element IDs are derived from opaque provider IDs and
never authorize an operation. Exact current provider IDs are still resolved
from the widget's committed snapshot before any host command is sent.

## Wi-Fi radio and Bluetooth slice

Available-network read/scan/connect is implemented end to end in the typed
SDK, authenticated broker, Native Wi-Fi provider, bundled manifest/catalog,
Settings consent descriptions, first-party widget, and deterministic tests.
Software Wi-Fi radio read/control, Bluetooth radio/discovery, explicit
association pairing/removal, and a Windows-owned management fallback are also
implemented behind separate closed grants. Remaining work is deliberately
separate: enterprise provisioning and profile-specific Bluetooth communication.
Unknown future capability names continue to fail closed.

The implemented Wi-Fi contract is:

1. An explicit Interactive action starts one `WlanScan` under Windows'
   precise-location decision, waits for completion or a bounded timeout, and
   reads one `WlanGetAvailableNetworkList` snapshot. There is no scan loop.
   Denial renders a stable instruction to enable permission in Windows
   Settings; the worker does not own or fabricate an OS consent dialog.
2. Every result receives a generation-bound opaque scan ID. It is valid only
   for that scan/provider generation and must never be persisted, logged, or
   treated as the SSID/BSSID/interface identity.
3. An exact current result may start a connection when it has a saved Windows
   profile or is an unsaved open network. Supported unsaved WPA2/WPA3 Personal
   rows open a masked native modal only for the exact current Interactive
   Network Controls generation. The host revalidates authority after the modal
   closes and sends the mutable password directly to the trusted provider; it
   never dispatches the password or profile XML through the widget worker or a
   JSON value. Native frame bytes, managed characters, command storage, profile
   XML, and the edit control are mutable owners cleared at their terminal
   boundaries. The provider creates one attempt-unique `WLAN_PROFILE_USER`
   profile with overwrite disabled, binds a random ownership token through
   Native Wi-Fi custom user data, starts one `WlanConnect`, and trusts only the
   matching ACM completion. Failure or cancellation rereads and compares that
   token before deletion; replacement, unreadable ownership, or deletion
   failure stays fail-closed. Success removes the token and retains the
   Windows-owned profile.
4. Enterprise/802.1X, certificate, SIM, domain-credential, hidden-network, and
   captive-portal provisioning are unsupported initially. Stored Windows keys
   are never read or exposed.
5. Software radio control uses `WlanSetInterface` with
   `wlan_intf_opcode_radio_state`; it cannot override a hardware switch,
   airplane-mode policy, administrator policy, or missing/disabled adapter.
   Multi-PHY writes reconcile the current state and return `partial_failure`
   when targets disagree instead of reporting false success.

Windows treats `WlanScan` and `WlanGetAvailableNetworkList` as precise-location
sensitive. Consent must align with an explicit controller action and denial or
revocation must be readable without automatic retries. See Microsoft's
[Wi-Fi access/location changes](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes),
[WlanScan](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanscan),
[WlanGetAvailableNetworkList](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlangetavailablenetworklist),
[WlanConnect](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanconnect),
and [WlanSetInterface](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlansetinterface)
documentation.

The trusted Bluetooth provider uses WinRT radio state plus event-driven device
enumeration/change events to publish bounded sanitized names and paired/
present/connected state. Radio changes are separately consented and reconcile
the effective Windows state, including typed partial failure. An explicit A
action on one current unpaired opaque device invokes Windows Association
Endpoint `PairAsync`; every completed Windows result maps to a bounded typed
outcome, and the provider always refreshes authoritative device state. A result
never claims profile connectivity. A paired row opens a nested confirmation;
confirm resolves that exact current opaque ID, invokes `UnpairAsync`, and shows
success only after the authoritative refresh no longer reports the pairing.
Cancel, stale identity, lifecycle loss, and replacement invoke nothing. Devices
already paired/connected, and
pairing outcomes that require a ceremony the overlay does not own, can open the
Windows Bluetooth Settings page through a separate manage grant. The validated
native device ID remains host-only and is never embedded in the Settings URI.
A generic Bluetooth device Connect/Disconnect operation is **not** promised: Windows
communication is profile-specific, such as GATT service/characteristic access
or RFCOMM sockets, and each future profile integration needs a separate narrow
capability. See Microsoft's [Radio](https://learn.microsoft.com/en-us/uwp/api/windows.devices.radios.radio?view=winrt-26100),
[DeviceWatcher](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.devicewatcher?view=winrt-26100),
[DeviceInformationPairing](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformationpairing?view=winrt-26100),
[UnpairAsync](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformationpairing.unpairasync?view=winrt-26100),
[Bluetooth GATT client](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client),
and [Bluetooth RFCOMM](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/send-or-receive-files-with-rfcomm)
documentation.

There is no capability-backed dashboard quick action. A dashboard
card is only Visible, while network switching requires Interactive lifecycle.
The user opens the widget before a connection-changing command can run.

## Capabilities and consent

Declare the smallest authority in `manifest.json`:

```json
{
  "permissions": [
    "system.network.read.v1",
    "system.network.wifi.read.v1",
    "system.network.wifi.radio.read.v1"
  ],
  "optionalPermissions": [
    "system.network.details.read.v1",
    "system.network.wifi.connect.v1",
    "system.network.wifi.radio.control.v1",
    "system.network.bluetooth.read.v1",
    "system.network.bluetooth.radio.control.v1",
    "system.network.bluetooth.pair.v1",
    "system.network.bluetooth.unpair.v1",
    "system.network.bluetooth.manage.v1"
  ]
}
```

| Capability | Public SDK surface | Allowed lifecycle |
| --- | --- | --- |
| `system.network.read.v1` | `HostServices.Network.GetStatusAsync`, `GetSavedProfilesAsync`, `OpenStatusSubscriptionAsync`, `WatchStatusAsync` | Visible or Interactive |
| `system.network.details.read.v1` | `GetConnectionDetailsAsync`, acknowledged invalidation subscription, and events that require a current re-query | Visible or Interactive |
| `system.network.saved-profile.switch.v1` | `HostServices.Network.SwitchSavedProfileAsync` | Interactive only |
| `system.network.wifi.read.v1` | `GetAvailableWifiAsync`, `RequestWifiScanAsync`, `OpenAvailableWifiSubscriptionAsync`, `WatchAvailableWifiAsync` | Snapshot/event read while Visible or Interactive; scan request Interactive only |
| `system.network.wifi.connect.v1` | `ConnectAvailableWifiAsync` | Interactive only |
| `system.network.wifi.radio.read.v1` | `GetWifiRadioAsync`, acknowledged subscription, and events | Visible or Interactive |
| `system.network.wifi.radio.control.v1` | `SetWifiRadioAsync` | Interactive only |
| `system.network.bluetooth.read.v1` | `GetBluetoothAsync`, acknowledged subscription, and sanitized device/radio events | Visible or Interactive |
| `system.network.bluetooth.radio.control.v1` | `SetBluetoothRadioAsync` | Interactive only |
| `system.network.bluetooth.pair.v1` | `PairBluetoothDeviceAsync` for one current opaque device and typed authoritative outcome | Interactive only |
| `system.network.bluetooth.unpair.v1` | `UnpairBluetoothDeviceAsync` for one explicitly confirmed current paired opaque device; authoritative removal gates success | Interactive only |
| `system.network.bluetooth.manage.v1` | `OpenBluetoothDeviceSettingsAsync` for one current opaque device; Windows owns the management UI | Interactive only |

`permissions` means the widget considers read access essential;
`optionalPermissions` means connection-details, connection/radio/pairing/removal/management enhancements
can degrade independently from coarse status, nearby-network presentation, and
Wi-Fi radio visibility. Pair and manage are intentionally different decisions:
granting read/discovery or radio control does not authorize either operation.
Neither is auto-granted, including for first-party widgets. The user reviews
each declaration from the Network Controls entry in **Settings → Installed
widgets → Permissions & configuration**. A grant is
confirmed explicitly; deny and revoke take effect immediately. Consent is
stored by package ID, publisher ID, and capability ID, while each live broker
session is also bound to the concrete widget instance and declared set.

The Connection details route shows only a closed connectivity value, transport,
and bounded normalized IP, default-gateway, and DNS display strings for one
Windows-preferred connection. Loopback, link-local, and temporary addresses are
excluded. Multiple plausible routes, including VPN ambiguity, are reported as
ambiguous instead of selecting an adapter by guess. The worker never receives an
interface GUID, MAC address, route table, DHCP lease, domain, proxy, traffic
sample, public-IP lookup, or Wi-Fi identity. IP Helper and connectivity change
notifications carry only a revision; the visible worker re-queries current truth
through an Active latest-wins operation, while Background owns no polling loop.

There are four independent gates:

1. **Manifest declaration** requests the exact required/optional authority and
   grants nothing by itself.
2. **Overlay capability consent** decides whether the authenticated widget may
   ask the trusted broker for coarse network data, available Wi-Fi, or a
   connection attempt.
3. **Broker identity/lifecycle enforcement** rechecks the fixed package,
   publisher, instance, declaration, decision, and current lifecycle.
4. **Windows privacy/location consent, hardware, and policy** decide whether the trusted
   provider may read location-sensitive Wi-Fi identity and signal information.

An overlay grant cannot override a Windows denial, disabled WLAN service,
administrator policy, unsupported adapter, or missing hardware. The widget
must represent those states without repeatedly prompting or retrying. The
coarse status path does not query location-sensitive current-connection
details automatically: those fields remain `PrivacyRestricted` and omitted.
Nearby-network access begins only from the separate explicit scan action.
Opening the overlay, selecting its tray item, or entering the widget does not
scan, prompt, or retry.

The protected prompt uses native password semantics, exposes no accessibility
value, rejects clipboard copy/cut/paste and context-menu transfer, and clears
the edit control plus every mutable native-frame, managed, command, P/Invoke,
and profile-XML password owner after the attempt.
Cancel or stale widget/snapshot authority performs no profile or connect call.

## Public data model

Widget code receives only the bounded SDK records in `WidgetSdk`:

```csharp
public enum WidgetNetworkConnectivity
{
    None,
    Local,
    Internet,
}

public enum WidgetNetworkTransportKind
{
    None,
    Ethernet,
    Wifi,
    Other,
}

public enum WidgetNetworkWirelessAvailability
{
    Available,
    NoAdapter,
    RadioOff,
    ServiceUnavailable,
}

public enum WidgetNetworkDetailsAccess
{
    Available,
    PrivacyRestricted,
    Unavailable,
}

public enum WidgetNetworkConnectionAttemptState
{
    None,
    Connecting,
    Failed,
}

public sealed record WidgetNetworkStatus(
    WidgetNetworkConnectivity Connectivity,
    WidgetNetworkTransportKind Transport,
    WidgetNetworkWirelessAvailability WirelessAvailability,
    WidgetNetworkDetailsAccess DetailsAccess,
    WidgetNetworkConnectionAttemptState ConnectionAttemptState,
    string? AttemptProfileId,
    string? ActiveProfileId,
    string? ActiveProfileName,
    int? SignalPercent);

public sealed record WidgetSavedNetworkProfile(
    string ProfileId,
    string DisplayName,
    bool IsConnected,
    int? SignalPercent);

public enum WidgetWifiScanState
{
    NotScanned,
    Scanning,
    Ready,
    PreciseLocationDenied,
    Unavailable,
}

public enum WidgetWifiSecurityKind
{
    Open,
    Personal,
    Enterprise,
    Unknown,
}

public sealed record WidgetAvailableWifiNetwork(
    string NetworkId,
    string DisplayName,
    int SignalPercent,
    WidgetWifiSecurityKind Security,
    bool CredentialRequired,
    bool IsConnected,
    bool HasSavedProfile);

public sealed record WidgetAvailableWifiNetworks(
    WidgetWifiScanState ScanState,
    IReadOnlyList<WidgetAvailableWifiNetwork> Networks);
```

`ProfileId` is a broker-issued opaque value. Treat it as a command token for
the current provider lifetime, not a WLAN profile name, interface GUID, stable
account identifier, telemetry key, or value to persist. `DisplayName` is
sanitized presentation data and can still be sensitive. Do not log it.
`SignalPercent` is nullable and, when present, is bounded from 0 through 100.
Absence is not zero signal: it means no safe/current value is available.

`ConnectionAttemptState` and `AttemptProfileId` are authoritative asynchronous
command feedback. `Connecting` identifies the opaque target that Windows
accepted for an attempt; `Failed` is a terminal provider event, not an
exception string to parse. `None` clears attempt presentation. A widget must
not infer success solely from a command acknowledgement.

The public connectivity enum deliberately avoids exposing Windows adapter,
address, route, and cost structures. Widget authors must not use direct WLAN or
IP Helper calls as a fallback when a broker value is unavailable.

`NetworkId` is a different token from the legacy saved `ProfileId`. It is
valid only for the current ready scan/provider generation. A new scan clears
the prior token map before returning `Scanning`, so retaining a row token for a
later command fails with `resource_not_found`. It never contains the SSID,
BSSID, interface GUID, or profile name. `DisplayName` remains sensitive
presentation data and must not enter logs or telemetry. `SignalPercent` is
bounded from 0 through 100. `CredentialRequired` is declarative state, not an
invitation for the widget to collect a secret.

## Race-free event observation

Network state is event driven. Open the acknowledged subscription before
fetching the current snapshots so a one-off change cannot be lost between
fetch and subscribe:

```csharp
private WidgetNetworkStatus? _status;
private WidgetAvailableWifiNetworks? _wifi;

protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
{
    _ = ObserveNetworkAsync(activeLifetime);
    return ValueTask.CompletedTask;
}

private async Task ObserveNetworkAsync(CancellationToken cancellationToken)
{
    try
    {
        await using var statusSubscription = await HostServices.Network
            .OpenStatusSubscriptionAsync(cancellationToken);
        await using var wifiSubscription = await HostServices.Network
            .OpenAvailableWifiSubscriptionAsync(cancellationToken);

        var statusTask = HostServices.Network.GetStatusAsync(cancellationToken).AsTask();
        var wifiTask = HostServices.Network.GetAvailableWifiAsync(cancellationToken).AsTask();
        await Task.WhenAll(statusTask, wifiTask);
        _status = statusTask.Result;
        _wifi = wifiTask.Result;
        Invalidate();

        // Production code runs both acknowledged streams concurrently. Each
        // event already carries a complete bounded snapshot; neither loop
        // polls or starts a scan.
        await Task.WhenAll(
            ObserveStatusAsync(statusSubscription, cancellationToken),
            ObserveAvailableWifiAsync(wifiSubscription, cancellationToken));
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // Normal lifecycle exit.
    }
    catch (WidgetCapabilityUnavailableException)
    {
        ShowUnavailable("Platform network services are unavailable in this host.");
    }
    catch (WidgetCapabilityException exception)
    {
        ShowProviderState(exception.ErrorCode);
    }
}
```

The `OnActivatedAsync` token preserves one observer across a Visible →
Interactive transition and cancels it before Background. If the widget needs
different Visible and Interactive behavior, start it from
`OnLifecycleStateChangedAsync` with the `stateLifetime` token instead; that
token is canceled before every state transition. Never use
`WidgetLifetimeToken` to keep presentation/network observers alive while
hidden.

Do not start a periodic adapter, profile, or scan refresh. The trusted provider coalesces
native change callbacks into complete bounded snapshots, and the broker keeps
only the newest pending event for a slow consumer. `Render()` must remain pure
and nonblocking; Windows and broker calls belong in lifecycle/action methods.

`GetAvailableWifiAsync` returns the current cached scan state. It does not call
`WlanScan`. Call `RequestWifiScanAsync` only from an explicit Interactive
action such as the focused Scan button. The provider publishes `Scanning`
immediately, then `Ready`, `PreciseLocationDenied`, or `Unavailable` through
the available-Wi-Fi subscription. Its one-shot six-second timeout is completion
machinery for that requested scan, not recurring polling.

## Connecting a visible network

A switch is an explicit open-widget action:

```csharp
public override async ValueTask OnActionAsync(
    WidgetActionEvent action,
    CancellationToken cancellationToken = default)
{
    if (action.ActionId != "wifi.connect.item" ||
        !_networksByElementId.TryGetValue(action.SourceElementId, out var network))
        return;

    // The immutable map was rebuilt with the rendered snapshot from opaque
    // scan IDs. Never infer the target from a display name or row index.
    SetConnectBusy(network.NetworkId);
    try
    {
        await HostServices.Network.ConnectAvailableWifiAsync(
            network.NetworkId, cancellationToken);
        // Acknowledgement means Windows accepted the command path. Keep the
        // busy state until the authoritative status event confirms success or
        // a bounded failure/timeout is reported.
    }
    catch (WidgetCapabilityException exception)
    {
        ClearConnectBusy();
        ShowProviderState(exception.ErrorCode);
    }
}
```

The provider calls `WlanConnect` for a current result backed by an existing
saved profile or an unsaved open network and observes the matching asynchronous
WLAN completion/failure. Command
acknowledgement is not a reason to fabricate a connected state. Keep optimistic
feedback limited to a busy indicator, reconcile from the authoritative event,
and reject a stale completion if the adapter/profile/provider generation
changed.

The UI must make the target profile and state-changing action unambiguous.
Controller focus stays on a stable semantic profile ID when possible; if that
profile disappears, move to the nearest valid item and announce the change.
Never create list element IDs from row indexes.

Protected networks without a saved profile return `credential_required`;
enterprise or unknown authentication without a saved profile returns
`unsupported_authentication`. The widget renders those stable outcomes and
directs the user to Windows where appropriate. It never shows a text field,
receives a credential, constructs profile XML, or guesses a different target.
An ID from an earlier scan returns `resource_not_found` and asks the user to
scan again.

## Controller and presentation contract

The dashboard card is glanceable and read-only. It may show a connectivity
glyph, coarse status, and privacy/unavailable state. It must not expose a saved
profile name if the read capability or Windows privacy decision does not allow
it.

The implemented first-party package contract is:

- package ID `org.gbar.firstparty.network-controls`;
- source `src/FirstPartyWidgets/NetworkControlsWidget`;
- required `system.network.read.v1`, `system.network.wifi.read.v1`, and
  `system.network.wifi.radio.read.v1`; optional connection, Wi-Fi radio control,
  Bluetooth read, radio-control, pairing, removal, and management grants;
- x64, `unload-after-idle` background policy with a 120-second bound, 64 MiB
  requested memory, and 10 Hz maximum widget update budget; and
- root controller input scope `network-controls`.

Its implemented stable actions are:

| Context | Controller | Action ID | Behavior |
| --- | --- | --- | --- |
| Dashboard card | — | — | Read-only summary; Network Controls declares no dashboard quick actions. |
| Open widget | LB/RB | `network.tab.previous` / `network.tab.next` | Switch between the mutually exclusive Wi-Fi and Bluetooth views from any root focus. |
| Focused tab | A or D-pad/left stick Left/Right | `network.tab.select` | Select Wi-Fi or Bluetooth through the shared segmented-tab component. |
| Active view | D-pad/left stick Up/Down | — | Move only through the current view. Down from its final row returns focus to the tray. |
| Focused Wi-Fi radio | A | `wifi.radio.toggle` | Request software radio On/Off while Interactive and reconcile the effective state. |
| Focused Scan action | A | `wifi.scan` | Request one bounded scan while Interactive; there is no automatic or repeating scan. |
| Focused network row | A or X | `wifi.connect.item` | Request connection to that exact current saved/open result while Interactive. |
| Focused Bluetooth radio | A | `bluetooth.radio.toggle` | Request software radio On/Off while Interactive and reconcile denial/partial failure. |
| Focused nearby Bluetooth device | A | `bluetooth.device.pair` | Start association for that exact current opaque row. |
| Focused paired Bluetooth device | A | `bluetooth.device.unpair.open` | Open explicit Remove device confirmation; B/Cancel changes nothing. |
| Removal confirmation | A | `bluetooth.device.unpair.confirm` | Invoke one exact current removal, then reconcile authoritative disappearance. |
| Focused Bluetooth device | X | `bluetooth.device.manage` | Open the Windows-owned Bluetooth Settings fallback for the exact current row. |
| Failure/unavailable surface | focused A | `retry` | Make one explicit refresh/recovery attempt; never start a retry loop. |

There is deliberately no dashboard selection or connect action. The open
surface does not keep a separate hidden selected-card model: generation-bound
opaque row IDs identify the target, and A/X resolves only against the exact
focused row.
LB/RB are view-switch shortcuts, not item-cycling shortcuts. LT/RT remain
unbound and neither shoulders nor triggers cycle profiles/devices. B is not
bound by the widget root, so the host can return to the dashboard.

The active views deliberately use different stable Scroll IDs:
`network.wifi.body.scroll` and `network.bluetooth.body.scroll`. The widget
retains one opaque selected network and one opaque selected Bluetooth device.
On a tab return it publishes that tab's selected stable element as initial
focus, and host focus-follow reconstructs the correct visible location. This
restoration is scoped to the current worker lifetime; an idle-unloaded runtime
starts from its safe default rather than persisting provider opaque IDs to
disk.

The open widget should provide:

- a clear coarse connectivity/transport header and an explicit
  privacy-restricted state when current Wi-Fi identity is unavailable;
- a controller-navigable currently available-network list with security,
  credential-required, saved, connected, busy, and signal states;
- direct A/X activation of the exact focused row;
- explicit Wi-Fi/Bluetooth software-radio controls plus sanitized Bluetooth
  discovery without implying pairing or generic connection;
- concise permission-required, denied/revoked, no-adapter, service-disabled,
  offline, local-only, failure, and timeout states; and
- explicit focus neighbors for ambiguous layouts, with stable focus across
  status updates.

Tray navigation reserves D-pad/left stick, A, B, Y, and Guide. The open
widget may use other buttons through its active input scope, but Network
Controls should not overload dashboard shortcuts to bypass lifecycle or
consent. The selected panel remains visible while the tray owns focus. B at
the widget root and Down from the last root control return focus to the tray;
B on the tray closes, and Guide always toggles the overlay. A on the tray
enters the already-visible widget rather than opening it. A future nested
network dialog must own its B and cannot fall through multiple levels.

Layouts must use bounded responsive units and semantic styles, not a fixed
desktop pixel width. Verify narrow, ultrawide, 100/150/200% DPI, Windows text
scale, and mixed-DPI monitor transitions. Missing or long localized profile
names must truncate/wrap without moving focus controls off-screen.

## Provider behavior and privacy

The trusted provider implementation uses two API families and three bounded
change registrations:

- IP Helper `NotifyIpInterfaceChange` and
  `NotifyNetworkConnectivityHintChange` registrations trigger aggregate and
  Ethernet snapshot refreshes.
- One long-lived WLAN client per provider lifetime supplies ACM scan/connection
  lifecycle, available-network snapshots, and saved-profile-backed connection
  behavior. A radio-state-only query distinguishes
  enabled/disconnected Wi-Fi from hardware or software radio-off without
  querying current connection identity. It never registers MSM notifications.

IP Helper's read-only `GetNetworkConnectivityHint` supplies aggregate `None`,
`Local`, or `Internet` state. `WlanRegisterNotification` is restricted to ACM.
Only an explicit scan command calls `WlanScan`; its completion reads one
`WlanGetAvailableNetworkList` snapshot. `WlanGetProfileList` is used privately
to mark visible results as saved, and `WlanConnect` accepts one current opaque
target. The provider does not use BSS APIs, profile XML/key APIs, an automatic
current-connection identity query, or a repeating scan loop.

Callbacks enqueue bounded work and return. Native calls, model mutation,
unregistration, and handle disposal happen on the provider's owning thread.
Coarse interface classification uses managed `NetworkInterface` snapshots and
a route-table-only `GetBestInterface` preference after initial start, a native
change, or explicit recovery—not a UI timer. WLAN registration requests only
the notification sources required by the feature; no continuous scans, BSSID
observations, or MSM notification stream are part of the implementation. A
six-second one-shot timer bounds only an outstanding user-requested scan and is
canceled on its terminal event.

Failed IP-interface, connectivity-hint, or ACM registrations remain degraded.
An explicit subsequent read retries the missing registration; there is no
background retry timer.

Logs and broker events must never contain profile names, SSIDs, profile XML,
keys, interface GUIDs, BSSIDs, MAC/IP/DNS/gateway addresses, or raw native
error text that embeds them. Use stable safe error codes and opaque IDs. Run
the provider unelevated and treat access denial/service/device loss as ordinary
recoverable states.

The full native API rationale and Microsoft source links are in [Windows
provider architecture](windows-provider-architecture.md).

## Error and lifecycle behavior

Widget code should select capability failures from stable
`WidgetCapabilityException.ErrorCode` rather than parsing messages. The shared
broker already uses codes such as `permission_denied`,
`capability_not_declared`, `unsupported_capability`, `lifecycle_denied`, and
`capability_revoked`. Normal network state belongs in the typed status fields:
transport, wireless availability, details access, and connection-attempt state.
`platform_unavailable` is the stable result when the provider cannot obtain a
trustworthy native snapshot. It is distinct from a successful snapshot that
reports `NoAdapter`, and an explicit retry performs one bounded recovery read.

- Do not retry permission, declaration, lifecycle, or Windows privacy denial.
- Retry transient channel/provider failures only after an explicit user action
  or a documented native recovery event.
- Cancellation caused by leaving Visible/Interactive is normal, not an error.
- Background cancels reads/subscriptions through broker lifecycle enforcement.
- Destroying cancels widget tokens before bounded cleanup.
- A revoked grant closes the subscription promptly and prevents every new
  operation fail closed.

The shipped Network Controls package uses `unload-after-idle` with a 120-second
bound. Background immediately cancels its lifecycle-owned subscriptions and
denies broker operations; after the bound the bridge sends `Destroying`, keeps
the last validated view, and releases the worker/companion process resources.
Returning to the widget cancels pending unload or lazily creates a fresh worker.

## Deterministic widget tests

Use the public SDK test host rather than reflection, a raw broker pipe, or the
developer's real network:

```csharp
var services = new WidgetTestHostServicesBuilder()
    .WithResponse(
        WidgetNetworkCapabilities.GetStatus,
        new WidgetNetworkStatus(
            Connectivity: WidgetNetworkConnectivity.Internet,
            Transport: WidgetNetworkTransportKind.Wifi,
            WirelessAvailability: WidgetNetworkWirelessAvailability.Available,
            DetailsAccess: WidgetNetworkDetailsAccess.PrivacyRestricted,
            ConnectionAttemptState: WidgetNetworkConnectionAttemptState.None,
            AttemptProfileId: null,
            ActiveProfileId: null,
            ActiveProfileName: null,
            SignalPercent: null))
    .WithResponse(
        WidgetNetworkCapabilities.GetAvailableWifi,
        new WidgetAvailableWifiNetworks(
            WidgetWifiScanState.Ready,
            new WidgetAvailableWifiNetwork[]
            {
                new("wifi_generation_7_a", "Home Wi-Fi", 82,
                    WidgetWifiSecurityKind.Personal, false, true, true),
                new("wifi_generation_7_b", "Guest", 61,
                    WidgetWifiSecurityKind.Open, false, false, false),
            }))
    .WithHandler(
        WidgetNetworkCapabilities.ConnectAvailableWifi,
        (request, cancellationToken) =>
            ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)))
    .WithEvents(
        WidgetNetworkCapabilities.AvailableWifiChanged,
        new[]
        {
            new WidgetAvailableWifiNetworksChanged(
                new WidgetAvailableWifiNetworks(
                    WidgetWifiScanState.Scanning,
                    Array.Empty<WidgetAvailableWifiNetwork>())),
        })
    .Build();

var widget = WidgetTestHost.Attach(new NetworkControlsWidget(), services);
await WidgetTestHost.InitializeAsync(widget);
await WidgetTestHost.SetLifecycleStateAsync(
    widget,
    WidgetLifecycleState.Visible);
```

Use `WithEventStream` for cancellation, race, and backpressure cases. Provider
unit/integration tests should use a native-adapter seam and deterministic
callbacks; ordinary CI must not change the developer's current connection.
Opt-in hardware tests may switch only between pre-provisioned disposable
profiles, must restore the prior connection, and must never create/read a
secret.

Required widget/provider evidence includes:

- no adapter/WLAN service, Ethernet-only, Wi-Fi-only, offline, and local-only;
- location permission unavailable/required/denied/revoked;
- initial snapshot plus a change during the subscription/fetch window;
- available-network arrival/removal, scan-generation replacement, connected
  and signal change;
- connect accepted, success, terminal failure, timeout, cancellation, adapter
  removal, and stale completion after a newer command;
- control grant denied/revoked independently from the read grant;
- no scan or connection command while Visible or Background;
- stable focus and busy state under list churn;
- LB/RB split-view switching from arbitrary root controls, independent Wi-Fi/
  Bluetooth selected-item restoration, and distinct stable Scroll IDs;
- zero timer polling and bounded event coalescing; and
- cancellation/disposal with no callback-after-free or hanging operation.

## Packaging and release evidence

Do not call Network Controls shipped or production-ready until all of these are
present and passing in the current worktree. Items 1–7 pass in the current
2026-08-07 worktree; item 8 remains open:

1. first-party widget, worker, strict manifest, GBSS, and public-SDK-only tests;
2. real Windows network provider with deterministic adapter tests;
3. production bridge composition using the real provider instead of the
   simulator;
4. trusted catalog entry plus Release packaging and required-file checks;
5. Settings discovery/review for all declared network capabilities;
6. focused broker/bridge/widget/provider suites and the full Release verifier;
7. packaged hidden-overlay startup plus controller-visible denial/unavailable
   behavior; and
8. hardware/privacy and hidden/background CPU/wakeup evidence labeled with the
   tested Windows/controller/network configuration.

Passing this milestone does not make arbitrary downloaded widgets safe.
Mandatory capability-free AppContainer launch and the exact-SID/Low-label/PID-
bound authenticated broker request path are implemented for installed/community
workers. Publisher signing/certificate/revocation infrastructure, CPU quotas,
disk/profile quotas and cleanup, a graphical/file-picker installer, safe
automatic updates, and a security audit/history UI remain separate release
gates. Win32k system-call disable is also not active because it prevents CoreCLR
initialization in the tested configuration. Controller package/permission
review and live catalog reload are already present; none of these establishes
publisher trust. See [security and trust](security-and-trust.md).
