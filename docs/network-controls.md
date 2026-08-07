# Network Controls reference

Status: **implemented and packaged prototype, automated Release verification
passed 2026-08-07**.
The typed SDK, authenticated capability transport, consent UI, lifecycle
enforcement, real event-driven Windows provider, first-party widget/worker,
trusted catalog entry, and Release packaging hooks exist. The focused Release
provider and widget suites pass 18/18 and 14/14 respectively; the full managed suite, native host
suite, packaged hidden-startup smoke, and controller input-probe smoke also
pass. Hardware/privacy matrices and performance evidence remain open, so this
is not yet a shipped or production-support claim.

Network Controls is the second first-party system-control reference widget,
after Audio Mixer. It must use the same public declarative SDK, worker
bootstrap, catalog, broker, permission, and lifecycle surfaces available to a
community widget. It is not a privileged `OverlayHost` panel.

## Version 1 scope

The initial surface is intentionally narrow:

- show aggregate `None`, `Local`, or `Internet` connectivity;
- distinguish `None`, `Ethernet`, `Wifi`, and `Other` transport without
  disclosing raw adapter identity;
- report `Available`, `NoAdapter`, `RadioOff`, or `ServiceUnavailable` Wi-Fi
  availability;
- represent current-profile/signal access as `Available`, `PrivacyRestricted`,
  or `Unavailable` rather than guessing;
- list profiles that are already saved by Windows; and
- let the user select one saved profile and explicitly connect while the
  widget is open and Interactive.

Version 1 does **not** discover unsaved networks, prompt for or store a
password, create/edit/delete profiles, read profile XML or key material, expose
BSSID/MAC/IP/DNS/gateway values, open captive portals, change radio/airplane
mode, disconnect, or silently reorder Windows profile preference. It does not
shell out to `netsh`, edit the registry, install a service/driver, or elevate.

There is no capability-backed dashboard quick action in version 1. A dashboard
card is only Visible, while network switching requires Interactive lifecycle.
The user opens the widget before a connection-changing command can run.

## Capabilities and consent

Declare the smallest authority in `manifest.json`:

```json
{
  "permissions": [
    "system.network.read.v1"
  ],
  "optionalPermissions": [
    "system.network.saved-profile.switch.v1"
  ]
}
```

| Capability | Public SDK surface | Allowed lifecycle |
| --- | --- | --- |
| `system.network.read.v1` | `HostServices.Network.GetStatusAsync`, `GetSavedProfilesAsync`, `OpenStatusSubscriptionAsync`, `WatchStatusAsync` | Visible or Interactive |
| `system.network.saved-profile.switch.v1` | `HostServices.Network.SwitchSavedProfileAsync` | Interactive only |

`permissions` means the widget considers read access essential;
`optionalPermissions` means connection switching can degrade independently.
Neither is auto-granted, including for first-party widgets. The user reviews
each declaration in **Settings → Permissions & capabilities**. A grant is
confirmed explicitly; deny and revoke take effect immediately. Consent is
stored by package ID, publisher ID, and capability ID, while each live broker
session is also bound to the concrete widget instance and declared set.

There are two independent permission boundaries:

1. **Overlay capability consent** decides whether the authenticated widget may
   ask the trusted broker for network data or a saved-profile switch.
2. **Windows privacy/location consent and policy** decide whether the trusted
   provider may read location-sensitive Wi-Fi identity and signal information.

An overlay grant cannot override a Windows denial, disabled WLAN service,
administrator policy, unsupported adapter, or missing hardware. The widget
must represent those states without repeatedly prompting or retrying. The
active provider milestone deliberately does not query location-sensitive
current-connection details automatically: Wi-Fi details default to
`PrivacyRestricted`, with active profile/signal omitted. A future explicit
Windows access-request flow requires its own design and must follow a clear
controller action; opening the overlay or selecting its dashboard card must
not trigger it.

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

## Race-free event observation

Network state is event driven. Open the acknowledged subscription before
fetching the current snapshots so a one-off change cannot be lost between
fetch and subscribe:

```csharp
private WidgetNetworkStatus? _status;
private IReadOnlyList<WidgetSavedNetworkProfile> _profiles = [];

protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
{
    _ = ObserveNetworkAsync(activeLifetime);
    return ValueTask.CompletedTask;
}

private async Task ObserveNetworkAsync(CancellationToken cancellationToken)
{
    try
    {
        await using var subscription = await HostServices.Network
            .OpenStatusSubscriptionAsync(cancellationToken);

        _status = await HostServices.Network.GetStatusAsync(cancellationToken);
        _profiles = await HostServices.Network
            .GetSavedProfilesAsync(cancellationToken);
        Invalidate();

        await foreach (var change in subscription.ReadAllAsync(cancellationToken))
        {
            _status = change.Status;
            // A status event does not currently include the profile list.
            // Refresh it only in response to this event, not on a timer.
            _profiles = await HostServices.Network
                .GetSavedProfilesAsync(cancellationToken);
            Invalidate();
        }
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

Do not start a periodic adapter/profile refresh. The trusted provider coalesces
native change callbacks into complete bounded snapshots, and the broker keeps
only the newest pending event for a slow consumer. `Render()` must remain pure
and nonblocking; Windows and broker calls belong in lifecycle/action methods.

## Saved-profile switching

A switch is an explicit open-widget action:

```csharp
public override async ValueTask OnActionAsync(
    WidgetActionEvent action,
    CancellationToken cancellationToken = default)
{
    if (action.ActionId != "network.connect" || SelectedProfile is not { } profile)
        return;

    SetConnectBusy(profile.ProfileId);
    try
    {
        await HostServices.Network.SwitchSavedProfileAsync(
            profile.ProfileId,
            cancellationToken);
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

The production provider design calls `WlanConnect` in saved-profile mode and
observes the matching asynchronous WLAN completion/failure. Command
acknowledgement is not a reason to fabricate a connected state. Keep optimistic
feedback limited to a busy indicator, reconcile from the authoritative event,
and reject a stale completion if the adapter/profile/provider generation
changed.

The UI must make the target profile and state-changing action unambiguous.
Controller focus stays on a stable semantic profile ID when possible; if that
profile disappears, move to the nearest valid item and announce the change.
Never create list element IDs from row indexes.

When `WirelessAvailability` is `RadioOff`, `NoAdapter`, or
`ServiceUnavailable`, the Connect action remains visible for stable controller
focus but is disabled with a state-specific label. Shortcut routing rechecks
the same condition before invoking the broker, so X/A cannot issue a doomed
connection request during a stale render.

## Controller and presentation contract

The dashboard card is glanceable and read-only. It may show a connectivity
glyph, coarse status, and privacy/unavailable state. It must not expose a saved
profile name if the read capability or Windows privacy decision does not allow
it.

The implemented first-party package contract is:

- package ID `org.gbar.firstparty.network-controls`;
- source `src/FirstPartyWidgets/NetworkControlsWidget`;
- required `system.network.read.v1` and optional
  `system.network.saved-profile.switch.v1`;
- x64, `suspend` background policy, 64 MiB requested memory, and 10 Hz maximum
  widget update budget; and
- root controller input scope `network-controls`.

Its intended stable actions are:

| Context | Controller | Action ID | Behavior |
| --- | --- | --- | --- |
| Dashboard card | LB | `profile.previous` | Select the previous cached profile locally; shown only with multiple profiles. |
| Dashboard card | RB | `profile.next` | Select the next cached profile locally; shown only with multiple profiles. |
| Open widget | LB/RB | `profile.previous` / `profile.next` | Change the selected saved profile. |
| Open widget | X | `profile.connect` | Request connection to the selected saved profile while Interactive. |
| Focused Connect button | A | `profile.connect` | Make the same explicit Interactive request through normal focus activation. |
| Failure/unavailable surface | focused A | `retry` | Make one explicit refresh/recovery attempt; never start a retry loop. |

Dashboard LB/RB actions change widget-local selection only. They do not invoke
the switch capability. There is deliberately no dashboard connect action. B is
not bound by the widget root, so the host can return to the dashboard.

The open widget should provide:

- a clear coarse connectivity/transport header and an explicit
  privacy-restricted state when current Wi-Fi identity is unavailable;
- a controller-navigable saved-profile list with connected, busy, and signal
  states;
- one focused connect action or direct focused-row activation;
- concise permission-required, denied/revoked, no-adapter, service-disabled,
  offline, local-only, failure, and timeout states; and
- explicit focus neighbors for ambiguous layouts, with stable focus across
  status updates.

Home navigation reserves only D-pad/left stick, A, Y, and Guide. The open
widget may use other buttons through its active input scope, but Network
Controls should not overload dashboard shortcuts to bypass lifecycle or
consent. B remains the host fallback to return from the root widget surface
only when the active scope does not handle it; Guide always closes the overlay.

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
- One long-lived WLAN client per provider lifetime supplies ACM connection
  lifecycle and saved-profile behavior. A radio-state-only query distinguishes
  enabled/disconnected Wi-Fi from hardware or software radio-off without
  querying current connection identity. It never registers MSM notifications.

IP Helper's read-only `GetNetworkConnectivityHint` supplies aggregate `None`,
`Local`, or `Internet` state. `WlanRegisterNotification` is restricted to ACM,
`WlanGetProfileList` supplies saved profiles, and `WlanConnect` accepts one
already-enumerated opaque target. The provider does not call `WlanScan`,
`WlanGetAvailableNetworkList`, BSS APIs, profile XML APIs, or an automatic
current-connection identity query.

Callbacks enqueue bounded work and return. Native calls, model mutation,
unregistration, and handle disposal happen on the provider's owning thread.
Coarse interface classification uses managed `NetworkInterface` snapshots and
a route-table-only `GetBestInterface` preference after initial start, a native
change, or explicit recovery—not a UI timer. WLAN registration requests only
the notification sources required by the feature; no continuous scans, BSSID
observations, or MSM notification stream are part of version 1.

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

The worker may remain resident in Background under current host policy, but
that does not authorize background broker operations. General manifest
residency-policy enforcement is still planned.

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
        WidgetNetworkCapabilities.GetSavedProfiles,
        new WidgetSavedNetworkProfile[]
        {
            new("profile-1", "Home Wi-Fi", true, 82),
            new("profile-2", "Office", false, null),
        })
    .WithHandler(
        WidgetNetworkCapabilities.SwitchSavedProfile,
        (request, cancellationToken) =>
            ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)))
    .WithEvents(
        WidgetNetworkCapabilities.StatusChanged,
        new[]
        {
            new WidgetNetworkStatusChanged(
                new WidgetNetworkStatus(
                    Connectivity: WidgetNetworkConnectivity.Internet,
                    Transport: WidgetNetworkTransportKind.Wifi,
                    WirelessAvailability: WidgetNetworkWirelessAvailability.Available,
                    DetailsAccess: WidgetNetworkDetailsAccess.PrivacyRestricted,
                    ConnectionAttemptState: WidgetNetworkConnectionAttemptState.Connecting,
                    AttemptProfileId: "profile-2",
                    ActiveProfileId: null,
                    ActiveProfileName: null,
                    SignalPercent: null)),
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
- profile arrival/removal, active-profile and signal change;
- connect accepted, success, terminal failure, timeout, cancellation, adapter
  removal, and stale completion after a newer command;
- control grant denied/revoked independently from the read grant;
- no switching while Visible or Background;
- stable focus and busy state under list churn;
- zero timer polling and bounded event coalescing; and
- cancellation/disposal with no callback-after-free or hanging operation.

## Packaging and release evidence

Do not call Network Controls shipped or production-ready until all of these are
present and passing in the current worktree. Items 1–7 passed on 2026-08-07;
item 8 remains open:

1. first-party widget, worker, strict manifest, GBSS, and public-SDK-only tests;
2. real Windows network provider with deterministic adapter tests;
3. production bridge composition using the real provider instead of the
   simulator;
4. trusted catalog entry plus Release packaging and required-file checks;
5. Settings discovery/review for both declared network capabilities;
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
