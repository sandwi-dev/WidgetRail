# YT Music community addon

This is a source port of `YtMusicGameBar`, rewritten as a controller-first
community addon for the declarative `WidgetSdk`. It uses the same immutable
`.gbarwidget` package, generic worker, AppContainer, consent, lifecycle, and
broker path available to an independent developer. It has no bundled-worker or
trusted-catalog fallback and does not attempt binary compatibility with Xbox
Game Bar.

The reusable host-service contract is documented in [local companion HTTP and
private secrets](../../docs/community-companion-services.md). This README
covers the YTMDesktop2-specific integration and package workflow.

The addon keeps the useful integration behavior while moving privileged work
behind public host services:

- Playback remains owned by YTMDesktop2.
- The manifest declares exactly `network.loopback:13091`; the host fixes the
  origin to `127.0.0.1`, disables proxies and redirects, and exposes bounded
  JSON GET/POST operations rather than raw sockets or arbitrary URLs.
- API error bodies are discarded rather than rendered or logged. Stable broker
  codes and HTTP status classes map to short recovery guidance.
- Artwork is accepted only from HTTPS URLs.
- Pairing tokens are written to the public authenticated-publisher/package/slot-scoped
  `storage.private-secrets.v1` service. The addon can query only existence and
  metadata; it cannot read a stored secret. For authenticated loopback calls,
  the host injects the named `ytmdesktop2.bearer` slot as a Bearer value.
- Pairing requests never carry an existing Bearer token. A successful pairing
  replaces the prior credential. Authenticated requests opt into
  `InvalidateBearerSecretOnUnauthorized`; on HTTP 401 the trusted host removes
  that exact scoped token before returning, then the addon clears only its local
  connection state and returns to pairing without racing a second delete.

## Controller map

| Input | Action |
| --- | --- |
| `X` | Play or pause |
| `LB` | Previous track |
| `RB` | Next track |
| `Y` | Refresh now playing |
| D-pad + `A` | Navigate and invoke all visible controls |
| Home/Guide | Reserved by the overlay host, never handled by this widget |

When the connected widget card is selected on the dashboard, its snapshot also advertises three host-routable quick actions: `LB → previous`, `X → toggle-playback`, and `RB → next`. The SDK resolves those through the dashboard input context and reports `SourceElementId = "dashboard-card"`. `Y` is intentionally absent because the dashboard host owns it for widget reordering; after the widget is opened, its own `Y → refresh` shortcut remains available.

The widget renders explicit disconnected, connecting, pairing, connected, and
error states. During pairing it displays the approval code returned by
YTMDesktop2, then allows up to 40 seconds for companion approval. A configured
server may continue to report `authRequired` after pairing; the widget
reconnects using host-side bearer injection and treats an actual HTTP 401—not
that configuration flag—as credential rejection. A returned 401 means the host
has already removed the rejected durable slot; a deletion failure is surfaced
instead. `IYtMusicClient` remains an
injectable test seam, while the production default client is created lazily
after `HostServices` attachment and uses only `HostServices.Loopback` and
`HostServices.PrivateSecrets`.

Each entry into the shared Visible/Interactive lifetime admits a non-blocking
automatic connection attempt when the presentation is disconnected; a mere
render does not connect. Auto-connect, progress repainting, polling, and
transport reconciliation use SDK operation lanes rather than widget-owned task
fields or cancellation sources. Their failures are observed by the SDK and
their exact Active lifetime is canceled and drained by the lifecycle runtime.
While `Visible` or `Interactive`, the widget repaints locally interpolated
progress at the SDK's bounded four-Hz cadence and reconciles authoritative
YTMDesktop2 state every two seconds. Returning to `Background` cancels the
connect, interpolation, and polling work and awaits completion. The worker
process remains resident in Background by default. Connection, snapshot,
pending-command, progress, status, and pairing-code inputs are published as one
immutable render-facing revision. A cancellation-ignoring companion completion
cannot update that revision after its operation is superseded or deactivated.

## Responsibility boundaries

The widget keeps one lifecycle and committed-state owner. Before the current
split, the 1,365-line, 57,993-byte `YtMusicWidget.cs` also contained action
routing, connection-status policy, optimistic confirmation/rollback and
progress reconciliation, plus every semantic view branch. Those helpers all
had lexical access to the mutable client, presentation, locks, time source, and
SDK operation registry even when they did not need that authority.

The split makes those dependencies explicit without adding another
coordinator:

- `YtMusicWidget` alone owns lifecycle callbacks, the provider client, the
  `_stateLock`, both provider serialization gates, SDK Active operation lanes,
  committed `YtMusicPresentationState`, invalidation, and disposal.
- `YtMusicActionPolicy` is a closed action-ID-to-route table. It receives only
  an action ID and cannot access provider or presentation state.
- `YtMusicConnectionPolicy` maps immutable presentation values and sanitized
  exceptions to connection transitions and status copy.
- `YtMusicCompanionPolicy` receives immutable presentation/snapshot values,
  monotonic timestamps, and the bounded update policy. It returns confirmation,
  progress, and exact-feature rollback values; it owns no locks, client, task,
  cancellation source, or publication callback.
- `YtMusicPresentation` composes a `WidgetView` from one immutable committed
  presentation and its already projected playback snapshot. Repeating the
  composition with the same values produces identical semantic snapshot bytes.

Coordination therefore remains one committed-state lock, two existing provider
serialization semaphores, and the SDK operation registry. The split adds zero
revision counters, task registries, cancellation sources, or cross-boundary
mutable owner references. Provider calls and late-result admission remain in
the widget; the extracted policy and presentation seams are directly testable
as value transformations.

Each authoritative poll is reconciled against the widget's monotonic projected
position. Small stale backward reports are ignored while playback continues;
modest forward drift is eased in at a bounded rate instead of jumping. A drift
of at least three seconds is treated as a real seek and snaps immediately, as
do pause transitions and track changes. Every projected or authoritative
position is clamped to the current duration.

Playback, rating, shuffle, repeat, next, and previous commands update the view
optimistically. Play/pause and next/previous dispatch their accepted POST
without making the action queue wait for snapshot reconciliation; next/previous
also reset progress before network I/O. Each successful playback transport
command supersedes any older reconciliation and starts five bounded,
increasing-delay snapshot attempts owned by the widget's visible lifecycle;
normal two-second polling remains the fallback. A completed action request can
therefore release its cancellation token without abandoning an accepted
command, while hiding the widget still cancels and drains the burst. Stale
companion state cannot confirm its own optimistic presentation. This keeps
repeated X/LB/RB input responsive without allowing unbounded polling work.
Like, dislike, shuffle, and repeat publish the intended
`.Selected(...)` value and `.Busy(true)` immediately; Like/Dislike share their
busy feature. The selected state survives stale polls, and Busy clears on
authoritative confirmation, deadline reconciliation, or rollback. When a
YTMDesktop2 version omits shuffle/repeat state, a successful command commits
their widget-managed state instead of flashing back to a false default. This is
the reference pattern for responsive network-backed toggle controls.

For a bounded two-second confirmation window, each stale non-transport
companion field is replaced with its pending expected value while unrelated
authoritative metadata continues to update. Transport progress uses an
eight-second guard, matching the slower track-change path observed in
YTMDesktop2. After the deadline, authoritative state wins. An explicit refresh
forces authoritative reconciliation immediately, and a track change ends the
old track's rating guard. Snapshot metadata carries its own track identity:
empty metadata or a `/track` payload that does not match `/track/state` cannot
replace the last complete title, artist, album, or artwork. The complete
presentation changes only when both API views agree. Command failure restores
only the affected prior
feature state, clears Busy, keeps the widget connected, and shows a bounded
error status. Independent pending features reconcile without clobbering one
another. The progress display is extrapolation only: seeking/scrubbing is not
implemented.

## Build and tests

```powershell
dotnet build samples\YtMusicWidget\YtMusicWidget.csproj -c Release
dotnet run --project tests\YtMusicWidget.Tests\YtMusicWidget.Tests.csproj -c Release
```

Build a deterministic community package through the public CLI:

```powershell
.\samples\YtMusicWidget\Build-CommunityPackage.ps1 -Configuration Release
```

The helper publishes only `payload/YtMusicWidget.dll`, `manifest.json`, and
`styles/default.gbss` into a clean staging root, then runs `gbar validate` and
`gbar pack`. By default the package is written to:

```text
artifacts/community-addons/ytmusic/org.gbar.samples.ytmusic-0.2.6.gbarwidget
```

To install and enable it for the current user through the same public catalog
commands used by any addon publisher:

```powershell
.\samples\YtMusicWidget\Build-CommunityPackage.ps1 -Configuration Release -Install
```

`-Install` runs the equivalent public catalog operations:

```powershell
$gbar = '.\tools\GbarCli\bin\Release\net8.0\gbar.exe'
& $gbar install `
  .\artifacts\community-addons\ytmusic\org.gbar.samples.ytmusic-0.2.6.gbarwidget
& $gbar enable org.gbar.samples.ytmusic
& $gbar list
```

Installation/enablement does not grant capabilities. Open overlay Settings →
Permissions → YT Music and separately grant **Access local app on port 13091**.
Grant **Store private connection secrets** only if YTMDesktop2 requires pairing
and the addon should retain its bearer slot. Then return to the tray; the
manifest-declared `music` icon is discovered from the Community package and the
worker starts lazily when selected.

Before testing, enable YTMDesktop2's companion API on its standard port. If the
service is absent, the addon renders a bounded retry state; it never scans other
ports or falls back to direct network access. Required loopback denial prevents
the integration. Optional vault denial still permits an authentication-disabled
companion, but pairing cannot persist a token and must show an explicit error.

Installed versions are immutable. Bump `manifest.json` before installing a
replacement version. Pass `-Catalog <directory>` to exercise the complete
pack/install/enable workflow against an isolated catalog.

For automated update/rollback tests, `-Version <canonical-version>` overrides
only the generated staging manifest; it never edits the source manifest. The
helper writes strict BOM-free UTF-8 and still passes the staged tree through
the public validator and packer:

```powershell
.\samples\YtMusicWidget\Build-CommunityPackage.ps1 `
  -Configuration Release -Version 0.2.6 `
  -OutputDirectory .\artifacts\community-addons\ytmusic-update
```

To review or test version behavior with the public CLI:

```powershell
& $gbar disable org.gbar.samples.ytmusic
& $gbar version list org.gbar.samples.ytmusic
& $gbar version select org.gbar.samples.ytmusic 0.2.6
& $gbar enable org.gbar.samples.ytmusic
```

`gbar uninstall org.gbar.samples.ytmusic --catalog <directory>` is
disabled-only and removes every immutable YT Music package version plus its
catalog state. Unsigned authority is derived from the host-verified package
content tree, so changed package bytes use a new secret namespace and require
pairing again; rollback to the exact verified bytes regains the prior
namespace. Publisher signing and production Credential Manager secret
enumeration/purge are not implemented, so the addon should delete its known
Bearer slot before uninstall when a user requests private-data cleanup.

Run the full auth-free Community-addon acceptance workflow with:

```powershell
.\scripts\Test-YtMusicCommunityAddon.ps1 -Configuration Release
```

The script snapshots the real catalog's state hash and installed ID/version
names read-only, then runs every command against a unique temporary catalog.
It proves strict package contents, validate/pack/install, separate consent,
generic AppContainer launch, simulated pairing, dashboard and open-window
controller routing, suspend/resume, deliberate worker crash recovery, fresh
force reload, content-bound update consent, rollback, disable, uninstall, and
temporary consent cleanup. Its evidence JSON explicitly does not claim a real
YTMDesktop2 pairing, physical controller playtest, shell pixels, or production
Credential Manager purge.

The rich media surface prefers 760 x 440 logical DIPs, but its compact budget
is 480 x 340. Artwork, metadata, progress, and controller targets use flexible
widths inside that bound so the host can safely clamp the window for 720p,
portrait, high-DPI, and enlarged-text work areas. Surface hints remain hints:
the host owns the final work-area/DPI scale and may choose a smaller safe size.

The test executable has no test-framework or other NuGet dependencies. Its
fake widget client and typed host-service harness cover UI state transitions,
shortcuts, dashboard authority, command routing, pairing, exact-port requests,
JSON parsing, write-only secret replacement/removal, authorization expiry, and
manifest validity without a live companion or real credential.

## Current limitations

- There is no endpoint settings/text-input primitive. This reference uses YTMDesktop2's standard `127.0.0.1:13091` endpoint. Advanced endpoint changes require future controller-friendly settings primitives.
- Progress remains read-only in this addon; wiring the SDK slider to a
  companion seek endpoint is separate work.
- The host's semantic `music` tray glyph is intentionally package-declared;
  arbitrary icon files and executable drawing payloads are not accepted.
