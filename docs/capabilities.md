# Widget capabilities

Status: typed SDK services, authenticated local transport, lifecycle/consent
enforcement, controller Settings review, deterministic simulators, and narrow
real Core Audio and Windows network backends are implemented. The production
bridge composes both real providers. Network hardware/privacy matrices plus
broader performance evidence remain release gates; the current
automated packaged Release suite passes.

Network Controls is the second implemented first-party integration milestone.
Its saved-profile-only behavior, Windows privacy boundary, lifecycle pattern,
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
| `system.audio.output.control.v1` | `SetOutputVolumeAsync` and `SetOutputMutedAsync` | Interactive only |
| `system.network.read.v1` | `HostServices.Network.GetStatusAsync`, `GetSavedProfilesAsync`, `OpenStatusSubscriptionAsync`, and `WatchStatusAsync` | Visible or Interactive |
| `system.network.saved-profile.switch.v1` | `SwitchSavedProfileAsync` | Interactive only |

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

The bridge watches the installed package catalog without polling. After a
complete validated reload, an enable/disable, install, update, manifest, or
style change publishes a new semantic catalog revision. A worker whose fixed
package/publisher/instance, process, declared capabilities, arguments, or
memory policy changed is retired; a later use starts a fresh authenticated
session with the new declaration set. Presentation-only changes preserve a
compatible running worker while atomically replacing its validated style and
quick-action metadata. Invalid catalog state retains the complete last-good
revision. Consent decisions remain stored independently and take effect without
a catalog or worker restart.

## Use typed host services

The reusable provider definitions and DTOs live in `WidgetSdk`:

- `WidgetAudioCapabilities`, `WidgetAudioSession`, `WidgetAudioOutput`,
  `WidgetAudioSessionsChanged`, and `WidgetAudioOutputChanged`;
- `WidgetNetworkCapabilities`, `WidgetNetworkStatus`,
  `WidgetNetworkConnectivity`, `WidgetNetworkTransportKind`,
  `WidgetNetworkWirelessAvailability`, `WidgetNetworkDetailsAccess`,
  `WidgetNetworkConnectionAttemptState`, `WidgetSavedNetworkProfile`, and
  `WidgetNetworkStatusChanged`.

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

1. **Permissions & capabilities** lists packages, five per page.
2. A package page lists supported required/optional declarations, four per
   page, with Granted/Denied/Not decided state.
3. A decision page requires an explicit focused confirmation before grant.
   Deny/revoke is immediate from that same scope.

Each page owns B-back; LB/RB change pages only where another page exists.
Decisions are atomically stored by package ID, publisher ID, and capability ID.
The broker channel additionally binds the concrete instance ID. Unknown
declarations and consent entries no longer declared by that package/publisher
are hidden and never actionable. Malformed/unavailable catalog or consent data
fails closed with sanitized diagnostics. First-party packages are not
auto-granted.

## Testing

Widget unit tests should build transport-free services with
`WidgetTestHostServicesBuilder`, attach them with `WidgetTestHost.Attach`, and
drive creation/lifecycle/destruction with the public `WidgetTestHost` helpers.
Assert:

- the widget calls the published `WidgetAudioCapabilities` or
  `WidgetNetworkCapabilities` descriptors rather than ad-hoc strings;
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
AppContainer selected from a host-computed authority key bound to the asserted
publisher, package ID, and exact immutable unsigned version. Updates therefore
receive a different profile and require fresh broker consent; selecting the
same reviewed version during rollback restores only that version's identity. The token
is Low integrity and has zero capability SIDs, including no network capability;
the process receives a stripped environment and explicit read/execute access
only to its generic runtime and exact package roots. Job Object policy adds
bounded memory, one active process, kill-on-close,
die-on-unhandled-exception, and UI restrictions. Isolation establishment and
token verification fail closed with no desktop-token fallback.

The main widget and capability-broker endpoints are separate random global
single-client pipes. Their ACLs name only the desktop host and exact
AppContainer SID and carry a Low mandatory label; the host also verifies the
worker PID. Runtime hello validation and broker nonce plus package, publisher,
instance, declaration, consent, and lifecycle checks remain mandatory after
the OS boundary. The broker—not the AppContainer worker—owns Core Audio and
Windows network access.

This materially constrains hostile widget authority, but community
distribution still requires publisher signing/revocation, CPU-rate controls,
disk/profile quotas and cleanup, a security audit/history UI, and broader real-
provider evidence. Trusted bundled Settings and YT Music workers temporarily
remain Job-only because their desktop-user resource dependencies have not yet
been brokered; installed/community packages cannot select that exception.
Win32k system-call disable is also not active: the tested mitigation prevented
CoreCLR DLL initialization with `0xC0000142`, so compatibility currently relies
on the AppContainer token plus Job Object UI restrictions instead.

For the implemented audio and network backends and their privacy
constraints, see
[Windows provider architecture](windows-provider-architecture.md). For the
broader trust decision, see [security and trust](security-and-trust.md).
