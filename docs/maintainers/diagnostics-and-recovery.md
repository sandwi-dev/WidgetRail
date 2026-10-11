# Diagnostics and recovery

The first-party Settings widget exposes a controller-readable **Diagnostics**
page. It is intended for local troubleshooting while preserving the same
authority boundaries used by the rest of the platform.

## What the snapshot contains

The bridge produces a bounded schema-3 snapshot with these typed areas:

- bridge session connectivity;
- last-good widget-catalog revision, widget count, and bounded warning state;
- active appearance revision and whether an invalid theme reload retained the
  last-good theme;
- whether the audio/network provider composition is configured;
- consent-store revision, total decisions, and denied-decision count;
- one bounded worker row per catalog widget: public widget ID/name, running
  state, start count, last stable failure code and optional UTC event timestamp, and whether on-demand restart is
  still allowed;
- up to 64 sanitized authority-recovery rows with an opaque recovery ID, safe
  display label, closed status, and an exact host-owned confirmation token only
  when retry is currently allowed;
- explicit `Unavailable` rows for overlay-host and Guide telemetry until the
  native host publishes those facts through a structured contract.

`Unavailable` is deliberate. Settings does not infer Guide health from a log
line or claim overlay health merely because its own worker is running.

The snapshot never contains filesystem paths, command lines, process IDs,
pipe names, nonces, exception text, network identities, audio-session names, or
widget-provided diagnostic strings. Catalog, consent, appearance, provider, and
recovery inspection failures are reduced independently to stable bounded states
before crossing the diagnostics channel. One unavailable area cannot replace
the remaining last-good snapshot.

## Tray order during catalog discovery

The host saves tray order, last-used widget and reopen preference in
`%LOCALAPPDATA%\WidgetRail\overlay-state.ini` whenever those preferences change.
The saved order is separate from the currently available tray entries.

The private `list-widgets` response includes `isComplete` alongside its revision
and widgets. Startup discovery and rejected installed-catalog validation publish
incomplete catalogs: available widgets remain usable, but absent IDs retain their
saved positions. Reordering available widgets updates their saved slots without
discarding those missing entries. A successful complete refresh removes unavailable
IDs and appends new ones. Completeness changes publish a new revision even when
the widget list is unchanged. A failed reload that retains the last-good catalog
also retains its ordering authority; it does not invent removals.

## Transport and authority

Runtime diagnostics are not a community-widget capability. The bridge attaches
a fresh private diagnostics companion only when all of these trusted catalog
facts match:

- widget ID `settings`;
- package ID `widgetrail.firstparty.settings`;
- publisher ID `widgetrail.firstparty`;
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

Open **Settings → About & troubleshooting → Diagnostics**. Focus starts on
**Refresh diagnostics**, or the first pending authority recovery. D-pad or the
left stick navigates export, applicable recovery links, expandable service
details, refresh and back. `A` activates; `B` returns to the About page.
Guide remains host-owned. Busy operations retain their focusable buttons.

The summary distinguishes current failures from recovered failures and absent
host telemetry. Latest per-widget failures show local timestamps; technical
codes are available with service details. The list shows at most 16 failures and
explains when the support report contains more.

**Export support report** saves an allowlisted ZIP in Documents/WidgetRail/Support
using the same trusted channel. It does not copy logs, settings or arbitrary
files. See [Support reports](../reference/support-reports.md) for the data
contract, privacy boundary, cancellation and export-result semantics.

Refresh performs one bounded reload of settings, themes, installed catalog,
permissions, and the bridge diagnostics snapshot. It does not create a polling
loop or restart healthy workers.

## Recovery policy

Apart from the explicit local support export, Diagnostics remains read-only
except for one narrow AppContainer authority-recovery operation. A pending host journal record appears with a sanitized
package/generation label and an opaque recovery ID. Selecting it opens an
explicit confirmation whose initial focus is **Cancel**. Settings never renders
or speaks the 32-character current or 64-character legacy confirmation token.

Retry sends that exact token over the private process-bound channel. Runtime
reopens the pending record, verifies the original objects and restored ACLs,
and makes cancellation versus journal clear one atomic decision. A stale token,
failed verification, unavailable journal, refusal, timeout, or cancellation
cannot clear the record or accept replacement authority; Settings refreshes the
authoritative bounded list after every typed result. There is no raw clear,
force mode, caller-selected journal path, SID, security descriptor, or object-
identity input.

This operation is separate from ordinary diagnostics and other recovery
surfaces. Failed workers still restart lazily within their runtime budget;
installed packages are disabled or rolled back under **Widgets**;
appearance reset and permission changes retain their own confirmation pages.
Diagnostics does not offer a universal worker kill/restart/reset action.

## Presentation-transition log records

`overlay.log` records one bounded line when a visible widget transition changes
identity or extent. It identifies the public source/destination widget IDs, the
currently presented and requested logical extents, the identity/extent cause,
the in-place resize strategy, content reveal or retained-content authority, and
whether sizing came from an admitted snapshot or the last committed surface.
These fields diagnose the former one-frame worker-start surface and render-target
recreation path without logging snapshot content, focus text, process identity,
worker arguments, filesystem paths, or provider data.

## Action-failure log records

`overlay.log` may retain one bounded record for an accepted post-admission
controller failure: public widget ID, opaque runtime generation, stable
`controllerActionFailed` code, action ID, and source element ID. The
bridge-supplied presentation message, worker exception type or text, provider
response bodies, credentials, and private fixture sentinels are never logged.
Painted and accessibility-visible feedback uses separate fixed host copy rather
than replaying diagnostic fields.

## Media Sessions failure transitions

For the bundled Now Playing widget, `overlay.log` records one bounded line when
a Media Sessions snapshot read, subscription open, or subscription read enters
a new typed failure code. Repeated identical failures are suppressed until that
stage succeeds or changes code. The record contains only the public widget ID,
the owning stage, and the broker code; it never contains the player/session
identity, media metadata, process details, provider response, or credentials.
The diagnostic lane retains at most 256 stage transitions and 32 pending log
lines, and logging failure cannot affect capability transport.

## Verification

Run the focused suites from the repository root:

```powershell
dotnet build .\tests\PlatformDiagnostics.Tests\PlatformDiagnostics.Tests.csproj -c Release "-bl:artifacts/diagnostics/transport-$([guid]::NewGuid()).binlog"
dotnet .\tests\PlatformDiagnostics.Tests\bin\Release\net8.0\PlatformDiagnostics.Tests.dll
dotnet build .\tests\SettingsWidget.Tests\SettingsWidget.Tests.csproj -c Release "-bl:artifacts/diagnostics/settings-$([guid]::NewGuid()).binlog"
dotnet .\tests\SettingsWidget.Tests\bin\Release\net8.0\SettingsWidget.Tests.dll
dotnet build .\tests\WidgetBridge.Tests\WidgetBridge.Tests.csproj -c Release "-bl:artifacts/diagnostics/bridge-$([guid]::NewGuid()).binlog"
dotnet .\tests\WidgetBridge.Tests\bin\Release\net8.0-windows10.0.19041.0\win-x64\WidgetBridge.Tests.dll --test-prefix Diagnostics
```

The transport suite covers authenticated process-bound delivery, wrong-nonce
recovery, and invalid-snapshot rejection. The Bridge suite directly covers
bounded multi-area projection, sanitized partial failures, malformed recovery
state, exact-token retry, stale/failed/unavailable results, and cancellation
versus commit. The Settings suite covers nested controller scope, rejected
catalog/theme state, permission denial summary, worker failure rendering,
explicit refresh, token-hidden confirmation, and every closed retry result.
Support-report tests verify excluded private fields, timestamp round trips,
authenticated export, malformed results, cancellation and file-system failure.
