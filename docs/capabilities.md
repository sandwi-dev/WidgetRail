# Widget capabilities

Status: typed SDK services, authenticated local transport, lifecycle/consent
enforcement, controller Settings review, deterministic simulators, and narrow
real Core Audio, Windows network/Bluetooth, foreground-activity, Start Menu
app-library, media-session, exact-loopback JSON, private-secret, and durable
private-state backends are implemented. The production bridge composes those
trusted providers. Hardware/
privacy matrices plus
broader performance evidence remain release gates; the current
automated packaged Release suite passes.

Network Controls is the second implemented first-party integration milestone.
Its explicit available-Wi-Fi scan/connect behavior, Windows privacy boundary, lifecycle pattern,
and remaining hardware/release gates are documented in the [Network Controls
reference](network-controls.md).

Capabilities are narrow host services for operating-system work that should
not become native overlay code or raw widget process access. Widget authors use
typed `HostServices` methods. They do not create broker requests, serialize
broker JSON, choose pipe names/nonces, or construct capability/operation IDs.
Installed/community code also cannot request AppContainer capability SIDs or a
desktop-token launch. Its worker is capability-free and network-denied; direct
audio/network Windows APIs are not an alternative to the typed broker.

## Declare the smallest authority

The current closed capability set is:

| Manifest capability | Typed SDK surface | Lifecycle |
| --- | --- | --- |
| `system.audio.sessions.read.v1` | `HostServices.Audio.GetSessionsAsync`, `OpenSessionsSubscriptionAsync`, and `WatchSessionsAsync` | Visible or Interactive |
| `system.audio.sessions.control.v1` | `SetSessionVolumeAsync` and `SetSessionMutedAsync` | Interactive only |
| `system.audio.output.read.v1` | `HostServices.Audio.GetOutputAsync`, `OpenOutputSubscriptionAsync`, and `WatchOutputAsync` | Visible or Interactive |
| `system.audio.output.control.v1` | `SetOutputVolumeAsync` and `SetOutputMutedAsync` | Interactive, or one exact declared dashboard gesture while Visible |
| `system.audio.devices.read.v1` | `GetDevicesAsync`, `OpenDevicesSubscriptionAsync`, and `WatchDevicesAsync`; sanitized input/output names and default markers only | Visible or Interactive |
| `system.audio.input.read.v1` | `GetInputAsync`, `OpenInputSubscriptionAsync`, and `WatchInputAsync` for current default microphone volume/mute | Visible or Interactive |
| `system.audio.input.control.v1` | `SetInputVolumeAsync` and `SetInputMutedAsync` for the current default microphone | Interactive only |
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
| `system.activity.recent.read.v1` | `HostServices.RecentActivity.GetRecentAsync`, `OpenSubscriptionAsync`, and `WatchAsync` | Visible or Interactive |
| `system.apps.library.read.v1` | `HostServices.AppLibrary.QueryAsync(query, cursor, direction, limit, refresh)` and `ResolveSavedAsync(savedIds)` for bounded cursor pages, sanitized names/source labels, conservative kinds, short-lived launch IDs, authority-scoped durable SavedIds, and up to 16 observation-only source-health rows bound to the page revision | Visible or Interactive |
| `system.apps.library.launch.v1` | `HostServices.AppLibrary.LaunchAsync(appId)` for one current broker-issued app ID | Interactive only; never dashboard gesture authority |
| `system.media.sessions.read.v1` | `HostServices.Media.GetSessionsAsync`, `OpenSubscriptionAsync`, and `WatchAsync` | Visible or Interactive |
| `system.media.sessions.control.v1` | `HostServices.Media.ControlAsync` for one broker-issued session ID | Interactive, or one exact declared dashboard gesture while Visible |
| `network.loopback:<port>` | `HostServices.Loopback.GetJsonAsync` and `PostJsonAsync` for that exact nonprivileged IPv4 loopback port | GET Visible/Interactive; POST Interactive or one exact dashboard gesture while Visible |
| `storage.private-secrets.v1` | `HostServices.PrivateSecrets.ExistsAsync`, `GetMetadataAsync`, `SaveAsync`, and `DeleteAsync`; values are never returned | exists/metadata Visible/Interactive; save/delete Interactive only |

Loopback ports are dynamic declarations from 1024 through 65535, but each exact
port is a separate closed consent item. The widget supplies only an origin-form
path, bounded JSON, safe headers, and timeout; it never supplies a host, URI
authority, DNS name, proxy, redirect policy, or socket. Private secrets are a
separate grant and are write-only to widget code. A loopback request may name a
slot so the trusted provider injects it as Bearer authorization while holding
both lifecycle/consent leases. It may also opt into
`InvalidateBearerSecretOnUnauthorized`; on an actual 401 the host deletes that
exact scoped slot before returning without granting general Visible delete
authority. See [local companion HTTP and private
secrets](community-companion-services.md) for APIs, limits, errors, identity
scope, and testing.

The following authority domains are not public widget capabilities. Their final
capability IDs and typed SDK surfaces are not assigned, the manifest validator
does not accept them, and no widget may infer them from the implemented network
grants. The protected Wi-Fi row describes the implemented exact first-party
host extension; profile-specific Bluetooth communication remains planned:

| Planned closed authority | Intended boundary | Initial lifecycle |
| --- | --- | --- |
| Protected Wi-Fi credential flow | Exact first-party host-owned WPA2/WPA3 Personal prompt and attempt-owned profile creation; mutable credential buffers bypass the worker and are zeroed | Interactive only |
| Profile-specific Bluetooth communication | A separate reviewed GATT/RFCOMM/service contract, never authority inherited from discovery | Feature-specific |

Pairing is implemented as association only. A `Paired` result does not mean a
headset, controller, GATT service, or RFCOMM service is connected or usable.
Unsupported ceremonies and management use the separately granted Windows
Settings fallback; there is no widget-owned credential/PIN dialog. Removal is
a separate destructive grant: show explicit confirmation, send only the exact
current opaque paired-device ID, and wait for the authoritative refreshed
snapshot before claiming success.

There is deliberately no generic Bluetooth Connect/Disconnect grant. Windows
communication is profile-specific (for example GATT or RFCOMM), so a future
device function must define a narrower profile/service capability rather than
inherit authority from enumeration or pairing. Enterprise Wi-Fi provisioning
is likewise outside the initial expanded network contract.

### Spotify provider foundation

The broker now defines `external.spotify.configuration.v1`,
`external.spotify.authorization.v1`, `external.spotify.playback.read.v1`, and
`external.spotify.playback.control.v1`, with strict configuration,
authorization, playback, command, restriction, and event DTOs. A focused
trusted Windows provider implements the exact PKCE callback, protected refresh
tokens, playback projection/control, bounded rate-limit handling, and sanitized
errors. Package-scoped public Client IDs can be managed locally through
`gbar config`; they are not secrets.

`WidgetBridge` composes this provider, and Spotify Community addon 0.1.6 is
locally packageable through the same public SDK/AppContainer path as an
independent addon. Its controller setup instructions and player core are
implemented; editing the Client ID remains a local `gbar config` workflow and
no live allowlisted-account evidence exists. Authors must not infer Spotify
authority from `network.loopback`, add arbitrary Internet access, store OAuth
tokens in private widget state, or bind to provider-internal wire DTOs. Device,
queue, search, recent, library, playlist, album, artist, and local Web Playback
SDK contracts remain planned. See [Spotify Web API
integration](spotify-integration.md).

Authorization has one exact lifecycle continuation, not background authority.
A `connect` operation must begin with an explicit action while Interactive. The
action acknowledges immediately, and its authorization task uses the widget's
Created-to-Destroying lifetime. If opening the system browser moves the widget
through Visible/Background, that already-created request lease may continue.
The package selects `keep-alive` residency so idle unload cannot destroy that
already-started explicit authorization task while the browser owns foreground;
this does not keep its active polling or presentation work running. The
temporary callback listener exists only during this explicit attempt and
waits at most fifteen minutes. It tolerates at most 16 malformed or early-close
local probes within that same window, but accepts only loopback origin, the
exact host/path and GET/HTTP/1.1 shape, and the matching OAuth state. The exact broker Connect deadline is seventeen minutes,
leaving two minutes for bounded token exchange, retry/backoff, and vault
persistence. A new Background connect, disconnect, playback control, or other
provider request is still denied. Destroying, consent revocation, pipe/caller
cancellation, and provider timeout cancel the retained request.

### Host-provided private widget state

`HostServices.PrivateState` is deliberately absent from the manifest table.
It is a host-provided, non-consent service for one strict canonical JSON
document per authenticated publisher/package authority. Declaring
`storage.private-state.v1` in `permissions` or `optionalPermissions` rejects
the manifest; worker IPC cannot add the host grant. Read, write, and clear are
available in Background, Visible, and Interactive, but not Destroying. This is
the bounded persistence exception to the normal Background capability rule,
not general OS or network authority. Documents are capped at 64 KiB, mutations
are rate limited, revisions support optional compare-and-exchange, and secrets
must remain in the separate consent-gated write-only service. See
[Private widget state](private-widget-state.md) for the SDK, identity/update,
retention, storage, and test contracts.

Declare a capability in `permissions` when the widget cannot provide its core
purpose without it. Put enhancements in `optionalPermissions`:

```json
{
  "permissions": [
    "system.audio.sessions.read.v1",
    "system.audio.output.read.v1"
  ],
  "optionalPermissions": [
    "system.audio.sessions.control.v1",
    "system.audio.output.control.v1"
  ]
}
```

Required does not mean auto-granted. Missing, denied, or revoked decisions fail
closed for both lists. The distinction tells the user and widget which features
are essential versus degradable; authors must still render a useful unavailable
state. An installed package declaring an unknown capability is skipped by the
bridge rather than receiving an open-ended permission.

For manifest-declared capabilities, the permission boundary has four
independent layers:

1. The author declares required or optional capability IDs in the manifest.
   A declaration requests review; it grants nothing.
2. The user explicitly allows, blocks, or revokes each declaration in Settings.
   Required declarations are still blocked until allowed.
3. The authenticated broker binds package, publisher, instance, declared set,
   durable decision, and current lifecycle before every operation or event.
4. The trusted native provider and Windows apply their own API, privacy,
   hardware, and policy rules. Overlay consent cannot bypass Windows precise-
   location access, a hardware radio switch, or device policy.

### Dashboard user-gesture authority

Control capabilities remain Interactive by default. A widget may associate a
dashboard quick action with one published typed capability operation, for
example `WidgetMediaCapabilities.Control.CapabilityId` and `.OperationId`.
When that exact prompt is pressed while the widget remains Visible, the bridge
may create a dormant host-owned reservation for at most 10 seconds. That
reservation is not broker authority; it only lets bounded serial widget work
reach the capability call associated with that exact input and snapshot.

The bundled Audio Mixer uses this exception only for the current default
master output: LB/RB request one clamped five-percentage-point decrease/increase
and X requests the exact current mute toggle. Application sessions, microphone
controls, device selection, and every undeclared audio operation remain
Interactive-only. Rapid volume presses converge through the widget's existing
latest-target command policy, then reconcile against a fresh provider result.

This is not extra ambient permission. The bridge revalidates the cached
snapshot, button, input sequence, snapshot sequence, closed operation, and
manifest declaration. The SDK attaches the two sequences only while executing
that queued dashboard action. Only when the exact typed capability operation is
actually invoked does the runtime atomically match and remove the dormant
reservation, then ask the identity/PID-bound companion to activate one broker
lease for at most two seconds. The broker rechecks user consent and normal
payload/provider rules before consuming that exact lease once. Dormant
reservations are bounded to the controller queue capacity; dormant and active
state are revoked on lifecycle change, consent loss, expiry, worker replacement,
or shutdown. Direct actions, open-widget actions, subscriptions, and background
tasks do not receive dashboard authority.

The semantic input contract distinguishes `PhysicalController` from
`AccessibilityAutomation`. Only the physical origin can mint a dormant
dashboard reservation. Windows UI Automation Invoke/RangeValue may route a
revalidated ordinary open-widget action, but it is not evidence of physical
presence and cannot use the Visible-state gesture exception. The bridge omits
authority for automation origin, the worker and SDK independently omit their
private gesture context, and the runtime rejects any mismatched reservation
before transport. The broker adapter attaches gesture sequences only after the
host accepts the exact capability/operation activation; a denied activation is
sent as an ordinary non-authorizing capability request, where normal
declaration, consent, lifecycle, payload, and provider rules still apply.

### Installed application library

The app-library read and launch capabilities are deliberately separate. A
launcher can make `system.apps.library.read.v1` required while declaring
`system.apps.library.launch.v1` optional, as the bundled Games & Apps reference
does. Each read-page row contains opaque `AppId`/`SavedId` identity plus one
versioned normalized presentation: display name, closed kind, source reference,
explicit availability, role-keyed opaque artwork handles, optional attributed
metadata, closed supported actions, and optional active-operation summary.
Absent provider facts remain absent. `AppId` is an opaque current-provider
launch token and must never be
persisted. `SavedId` is a non-reversible durable token scoped to the
authenticated publisher/package authority; retain it in private state, then
use `ResolveSavedAsync` to obtain current launch tokens after restart. The
resolver accepts at most 64 unique SavedIds, preserves request order, and omits
apps that are no longer available. Neither ID is a Windows path, AUMID,
provider identity, or launcher identifier. The public page limit is 64. Forward
and reverse cursors are opaque, query/page-size/direction/revision bound, and at
most 128 characters; callers must not parse or persist them as durable state.
The broker retains only bounded current launch/artwork windows while the trusted
provider owns the normalized catalog, so neither widget IPC nor the capability
domain materializes the complete library.

Launch requires Interactive even if the widget has already listed the item.
The broker validates the opaque ID and the trusted owning source revalidates its
exact current shortcut, package/AUMID/game evidence, or launcher registration
immediately before launch. The worker never receives the shortcut path, target,
arguments, AUMID, package identity, PID, or HWND, and app launch is not eligible
for Visible dashboard-gesture authority. See the [Games & Apps
reference](games-and-apps.md) for provider limits and authoring behavior.

The bridge watches the installed package catalog without polling. After a
complete validated reload, an enable/disable, install, update, manifest, or
style change publishes a new semantic catalog revision. A worker whose fixed
package/publisher/instance, process, declared capabilities, arguments, or
memory policy changed is retired; a later use starts a fresh authenticated
session with the new declaration set. Presentation-only changes preserve a
compatible running worker while atomically replacing its validated style and
quick-action metadata. Invalid trusted shell state retains the complete last-
good revision. Invalid installed state/integrity publishes a trusted-only
revision and retires Community workers, so stale capability authority is not
retained. Consent decisions remain stored independently and take effect without
a catalog or worker restart.

## Use typed host services

The reusable provider definitions and DTOs live in `WidgetSdk`:

- `WidgetAudioCapabilities`, session/output/device/input DTOs, and their
  full-snapshot change events;
- `WidgetNetworkCapabilities`, `WidgetNetworkStatus`,
  `WidgetNetworkConnectivity`, `WidgetNetworkTransportKind`,
  `WidgetNetworkWirelessAvailability`, `WidgetNetworkDetailsAccess`,
  `WidgetNetworkConnectionAttemptState`, `WidgetSavedNetworkProfile`, and
  `WidgetNetworkStatusChanged`; and `WidgetWifiScanState`,
  `WidgetWifiSecurityKind`, `WidgetAvailableWifiNetwork`,
  `WidgetAvailableWifiNetworks`, `WidgetWifiRadio`, Bluetooth radio/discovery/
  device snapshots, and their full-snapshot change events; and
- `WidgetRecentActivityCapabilities`, `WidgetRecentActivity`, and
  `WidgetRecentActivitiesChanged`.

`GetAvailableWifiAsync` reads the last bounded scan snapshot and never starts a
scan. `RequestWifiScanAsync` is a separate explicit Interactive operation.
Every result ID expires on the next scan/provider generation and must not be
persisted or derived from display text. `ConnectAvailableWifiAsync` accepts
only that current opaque ID; credential-required and unsupported authentication
fail with typed codes instead of exposing a secret-entry surface to the worker.
Wi-Fi and Bluetooth radio setters always reconcile the authoritative state.
`partial_failure` means one native target changed and another did not; the UI
must show the refreshed state rather than pretending the requested state won.
Bluetooth device IDs are opaque and generation-bound. Pair or remove only an ID
from the current snapshot, handle every typed outcome without inventing success,
and wait for the following authoritative Bluetooth snapshot before changing
paired/connected presentation. `OpenBluetoothDeviceSettingsAsync` is a
Windows-owned management fallback, not a generic connect result.

Audio device enumeration reports which sanitized endpoints are currently
default. It intentionally has no default-device setter. Input read/control is
limited to volume/mute on the current default capture endpoint and does not
grant audio capture, an activity meter, endpoint IDs, or sample data.

Recent activity is a bounded, event-driven list of eligible foreground windows.
The worker receives only opaque lifetime IDs, bounded display names, running/
most-recent markers, and a conservative kind. Activation accepts only a live ID
already issued by the authorized read provider; it cannot launch an executable
or target an arbitrary process/window.

`WidgetAudioSessionsChanged.IsAvailable` distinguishes a live Core Audio
provider loss from a healthy empty session list. On `false`, clear stale
session controls and render a recoverable service-unavailable state; a later
`true` event carries the current authoritative list.

Most widgets should use `HostServices.Audio` and `HostServices.Network` rather
than the lower-level `IWidgetCapabilityClient`. For example:

```csharp
private IReadOnlyList<WidgetAudioSession> _sessions = [];
private string? _capabilityError;

protected override ValueTask OnLifecycleStateChangedAsync(
    WidgetLifecycleState previous,
    WidgetLifecycleState current,
    CancellationToken stateLifetime)
{
    if (current is WidgetLifecycleState.Visible or WidgetLifecycleState.Interactive)
        _ = ObserveSessionsAsync(stateLifetime);
    return ValueTask.CompletedTask;
}

private async Task ObserveSessionsAsync(CancellationToken cancellationToken)
{
    try
    {
        await using var subscription = await HostServices.Audio
            .OpenSessionsSubscriptionAsync(cancellationToken);
        _sessions = await HostServices.Audio.GetSessionsAsync(cancellationToken);
        Invalidate();

        await foreach (var change in subscription.ReadAllAsync(cancellationToken))
        {
            _sessions = change.Sessions;
            Invalidate();
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // Normal lifecycle exit.
    }
    catch (WidgetCapabilityUnavailableException)
    {
        _capabilityError = "Platform audio is unavailable in this host.";
        Invalidate();
    }
    catch (WidgetCapabilityException exception)
    {
        _capabilityError = exception.ErrorCode;
        Invalidate();
    }
}
```

Open the acknowledged subscription before fetching current state. The open
returns only after host registration, and the capacity-one full-snapshot event
buffer retains the newest change that occurs during the fetch. Fetching first
and calling `WatchSessionsAsync` afterward can lose a one-off change. The
`Watch*` helpers remain appropriate for event-only consumers and compatibility.

Use the state token for a watcher tied to exactly one state. The previous token
is canceled before every transition, including Visible to Interactive. A widget
that intentionally wants one subscription across both states may use
`ActiveCancellationToken`; it is canceled before Background. Do not use
`WidgetLifetimeToken` to keep provider UI subscriptions alive while hidden.

Control calls belong in the open Interactive surface:

```csharp
public override async ValueTask OnActionAsync(
    WidgetActionEvent action,
    CancellationToken cancellationToken = default)
{
    if (action.ActionId == "mute-session" && _sessions.FirstOrDefault() is { } session)
        await HostServices.Audio.SetSessionMutedAsync(
            session.SessionId, !session.IsMuted, cancellationToken);
}
```

The host owns lifecycle. A widget cannot promote itself to Visible or
Interactive, and worker-side broker lifecycle messages are not accepted.
Version-1 control capabilities are Interactive-only. Dashboard-card quick
actions run while Visible, so they cannot use audio/network control today; a
future host-mediated quick-action authority would require separate review.

## Denial, revocation, and errors

Check `HostServices.Capabilities.IsAvailable` only to distinguish a worker that
has no authenticated capability channel. It does not prove that a particular
declaration is granted or currently allowed. Make the typed call and handle:

- `WidgetCapabilityUnavailableException`: this worker has no broker channel;
- `WidgetCapabilityException`: the broker rejected the operation, the channel
  closed, or a response/event was malformed. Use its stable `ErrorCode` for
  state selection and a user-friendly message; and
- `OperationCanceledException`: the caller token/lifecycle ended. Do not turn
  normal lifecycle cancellation into an error banner.

Common broker codes include `permission_denied`, `capability_not_declared`,
`unsupported_capability`, `lifecycle_denied`, and `capability_revoked`.
`platform_unavailable` means the trusted OS provider could not obtain a
trustworthy current snapshot; it does not mean a healthy list is empty or that
hardware is absent.
Transport validation can also report codes such as `channel_closed`, `unsupported_protocol`,
`malformed_response`, or `malformed_event`. Treat unknown future codes as a
bounded generic provider failure rather than parsing exception text.

Consent changes are watched without polling and coalesced before broker
reconciliation. Denial/revoke closes matching live subscriptions. A malformed,
deleted, unreadable consent document or watcher failure revokes subscriptions
fail closed; the worker receives `capability_revoked`. Every new operation also
rechecks durable consent.

Do not retry permission denial, declaration errors, or lifecycle denial on a
timer. Keep controller focus usable, explain which feature is unavailable, and
let the user open Settings. Retry transient provider/channel failures only from
an explicit action or a bounded worker-recovery path.

## Controller permission review

Settings discovers installed and bundled first-party manifests when the
Settings widget enters a new Visible/Interactive lifetime; it does not poll.
The controller flow is:

1. **Installed widgets** lists built-in and community packages. A opens the
   exact widget management page; **Permissions & configuration** opens its
   bounded capability Scroll.
2. When unsupported declarations or inactive saved decisions exist, one
   focusable **Review unsupported or inactive access** row opens a nested,
   read-only bounded Scroll. It uses stable opaque row IDs and B-only return;
   there are no grant, cleanup, or removal actions on that page.
3. The selected widget page lists all supported required/optional declarations
   in its own bounded Scroll, with Granted/Denied/Not decided state.
4. A decision page requires an explicit focused confirmation before grant.
   Deny/revoke is immediate from that same scope.

Each page owns B-back and contains no redundant Back button. Up/Down scrolls
through the current list; LB/RB remain available to widget-owned actions.
Decisions are atomically stored by package ID, publisher ID, and capability ID.
The broker channel additionally binds the concrete instance ID. Unknown
declarations and consent entries no longer declared by that exact package/
publisher authority are never actionable. The Review page shares one 16-item
display budget across unsupported requests and inactive decisions, reports the
remaining count, and sanitizes/truncates display and accessibility strings.
Inactive-decision classification is shown only when the catalog projection is
complete and both catalog and consent are valid. Otherwise Settings says the
classification is unavailable and exposes no possibly false inactive rows.
First-party packages are not auto-granted.

## Testing

Widget unit tests should build transport-free services with
`WidgetTestHostServicesBuilder`, attach them with `WidgetTestHost.Attach`, and
drive creation/lifecycle/destruction with the public `WidgetTestHost` helpers.
Assert:

- the widget calls published typed descriptors such as
  `WidgetAudioCapabilities`, `WidgetNetworkCapabilities`, or
  `WidgetAppLibraryCapabilities` rather than ad-hoc strings;
- denied/unavailable/error responses render controller-readable states;
- caller and lifecycle cancellation stop enumerations promptly;
- event bursts do not create overlapping UI refresh work; and
- optional capability loss removes only the optional feature.

`WidgetWorkerBootstrap.RunAsync` is the public worker entrypoint. It validates
the host launch contract, authenticates the optional broker before invoking the
widget factory, attaches host services before `OnCreatedAsync`, and owns
cancellation/disposal. Widget executables should not parse pipe/broker arguments
or construct transports themselves.

Transport contract tests exercise the real nonce/identity handshake, bounds, cancellation,
lifecycle, coalescing, unsubscribe, and disposal against
`SimulatedPlatformBrokerBackend`. Widget tests use the public fake host; normal
CI must not change the developer's audio device or network. Real Core Audio
smoke tests are read-only unless an explicitly isolated integration test opts
into bounded control and restores prior state.

## Exact security boundary

For every worker start/restart, the bridge fixes package ID, publisher ID,
instance ID, declared capability set, consent store, and backend. It creates a
fresh broker pipe/server and nonce, then supplies the worker bootstrap with the
connection arguments. The first message must match both nonce and full
identity. Requests cannot substitute identity or invoke an operation outside
the server-fixed closed declarations. All frames/messages, in-flight requests,
subscriptions, waits, DTOs, strings, percentages, and opaque IDs are bounded.

The SDK API deliberately exposes only typed DTOs and services—not pipe/nonce,
raw JSON, OS handles, raw OS identifiers, process IDs, paths, or credentials.
Session/profile targets are broker-issued bounded opaque IDs.
Installed/community assemblies run in a mandatory package-specific
AppContainer selected from a host-computed authority key derived from the
verified unsigned package content tree, not its asserted publisher label.
Changed bytes therefore receive a different profile, require fresh broker
consent, and cannot inherit private secrets or private widget state even when
ID/version text is reused;
rollback to the exact reviewed bytes restores only that content identity. The token
is Low integrity and has zero capability SIDs, including no network capability;
the process receives a stripped environment and explicit read/execute access
only to its generic runtime and exact package roots. Job Object policy adds
non-breakaway process-tree accounting, kill-on-close,
die-on-unhandled-exception, and UI restrictions without adding ambient OS
authority. Isolation establishment and
token verification fail closed with no desktop-token fallback.

The main widget and capability-broker endpoints are separate random global
single-client pipes. Their ACLs name only the desktop host and exact
AppContainer SID and carry a Low mandatory label; the host also verifies the
worker PID. Runtime hello validation and broker nonce plus package, publisher,
instance, declaration, consent, and lifecycle checks remain mandatory after
the OS boundary. The broker—not the AppContainer worker—owns Core Audio,
Windows network access, loopback sockets, and host-only secret reads.

This materially constrains hostile widget authority, but community
distribution still requires publisher signing/revocation, CPU-rate controls,
disk/profile quotas and cleanup, a security audit/history UI, and broader real-
provider evidence. Trusted bundled Settings temporarily remains Job-only for
desktop-user dependencies. YT Music now runs as a Community AppContainer addon
using the public loopback/secret broker; installed packages cannot select the
remaining Settings exception.
Win32k system-call disable is also not active: the tested mitigation prevented
CoreCLR DLL initialization with `0xC0000142`, so compatibility currently relies
on the AppContainer token plus Job Object UI restrictions instead.

For the implemented audio and network backends and their privacy
constraints, see
[Windows provider architecture](windows-provider-architecture.md). For the
broader trust decision, see [security and trust](security-and-trust.md).
