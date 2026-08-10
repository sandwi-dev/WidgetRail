# Diagnostics and recovery

The first-party Settings widget exposes a controller-readable **Diagnostics**
page. It is intended for local troubleshooting while preserving the same
authority boundaries used by the rest of the platform.

## What the snapshot contains

The bridge produces a bounded schema-1 snapshot with these typed areas:

- bridge session connectivity;
- last-good widget-catalog revision, widget count, and bounded warning state;
- active appearance revision and whether an invalid theme reload retained the
  last-good theme;
- whether the audio/network provider composition is configured;
- consent-store revision, total decisions, and denied-decision count;
- one bounded worker row per catalog widget: public widget ID/name, running
  state, start count, last stable failure code, and whether on-demand restart is
  still allowed;
- explicit `Unavailable` rows for overlay-host and Guide telemetry until the
  native host publishes those facts through a structured contract.

`Unavailable` is deliberate. Settings does not infer Guide health from a log
line or claim overlay health merely because its own worker is running.

The snapshot never contains filesystem paths, command lines, process IDs,
pipe names, nonces, exception text, network identities, audio-session names, or
widget-provided diagnostic strings. Catalog and consent failures are reduced to
stable bounded states before crossing the diagnostics channel.

## Transport and authority

Runtime diagnostics are not a community-widget capability. The bridge attaches
a fresh private diagnostics companion only when all of these trusted catalog
facts match:

- widget ID `settings`;
- package ID `org.gbar.firstparty.settings`;
- publisher ID `org.gbar.firstparty`;
- trusted Job-only worker policy;
- no declared platform capabilities.

The endpoint is current-user-only, uses a random 256-bit nonce, and is bound to
the exact Settings worker process ID before it accepts a client. Frames are
length bounded, strict JSON rejects unknown or duplicate properties, snapshots
have closed enum values and collection/string limits, and each Settings refresh
uses a two-second bounded request. A malformed or unauthenticated connection is
dropped without poisoning a later valid refresh.

This channel is intentionally outside `WidgetSdk`. Third-party widgets cannot
request it in a manifest, discover its endpoint, select another identity, or
turn it into a general host control API.

The separate `Measure-OverlayPerformance.ps1` startup contract is also not a
widget capability or Settings control channel. It is opt-in process-launch
diagnostics for a local developer: the host creates an ephemeral presentation
state, selects one already-installed widget by public ID, establishes exactly
Hidden, Visible, or Interactive, and suppresses every overlay-state persistence
write for that process. After the script's warmup it accepts one reset message
on the exact spawned HWND, then atomically publishes a nonce-bound bounded record
only during graceful shutdown. The record contains lifecycle/widget identity,
QPC interval, host timer classes, paint count, and successful Direct2D frame
count—never paths, widget content, controller data, or provider state. The
harness hashes that sidecar and records all unmeasured ETW/presentation facts.
The nonce binds a record to one invocation and detects mismatches; it is not a
security boundary against another process already running as the same user.

## Controller behavior

Open **Settings → Diagnostics**. Focus starts on **Refresh diagnostics**. D-pad
or the left stick moves between **Refresh diagnostics** and **Back**; `A`
activates the focused action; `B` returns to the Settings root through the
Diagnostics page's nested input scope. Guide remains host-owned.

Refresh performs one bounded reload of settings, themes, installed catalog,
permissions, and the bridge diagnostics snapshot. It does not create a polling
loop or restart healthy workers.

## Recovery policy

The Diagnostics page is read-only apart from Refresh:

- a failed worker already restarts lazily when the next legitimate request
  reaches it and the runtime restart budget permits it;
- installed packages can be disabled or rolled back from **Installed widgets**,
  where package identity and compatibility are already reviewed;
- appearance reset remains on the separate confirmation page;
- permission changes remain on the package/capability confirmation pages.

Diagnostics does not offer a universal kill/restart/reset button. Such a button
would conflate trusted and community workers, bypass confirmation flows, and
could destroy legitimate background work. A future recovery action must be a
typed host operation with exact target identity, lifecycle rules, confirmation,
and deterministic crash-loop tests before appearing here.

## Action-failure log records

`overlay.log` may retain one bounded record for an accepted post-admission
controller failure: public widget ID, opaque runtime generation, stable
`controllerActionFailed` code, action ID, and source element ID. The
bridge-supplied presentation message, worker exception type or text, provider
response bodies, credentials, and private fixture sentinels are never logged.
Painted and accessibility-visible feedback uses separate fixed host copy rather
than replaying diagnostic fields.

## Verification

Run the focused suites from the repository root:

```powershell
dotnet run --project .\tests\PlatformDiagnostics.Tests\PlatformDiagnostics.Tests.csproj -c Release
dotnet run --project .\tests\SettingsWidget.Tests\SettingsWidget.Tests.csproj -c Release
dotnet run --project .\tests\WidgetBridge.Tests\WidgetBridge.Tests.csproj -c Release
```

The transport suite covers authenticated process-bound delivery, wrong-nonce
recovery, and invalid-snapshot rejection. The Settings suite covers nested
controller scope, rejected catalog/theme state, permission denial summary,
worker failure rendering, and explicit refresh recovery.
