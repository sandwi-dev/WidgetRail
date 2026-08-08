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
- API error bodies are capped before they reach UI state.
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

The first entry into the shared Visible/Interactive lifetime starts a
non-blocking automatic connection attempt; a mere render does not connect.
While `Visible` or `Interactive`, the widget repaints locally interpolated
progress at the SDK's bounded four-Hz cadence and reconciles authoritative
YTMDesktop2 state every two seconds. Returning to `Background` cancels the
connect, interpolation, and polling work and awaits completion. The worker
process remains resident in Background by default.

Each authoritative poll is reconciled against the widget's monotonic projected
position. Small stale backward reports are ignored while playback continues;
modest forward drift is eased in at a bounded rate instead of jumping. A drift
of at least three seconds is treated as a real seek and snaps immediately, as
do pause transitions and track changes. Every projected or authoritative
position is clamped to the current duration.

Playback, rating, shuffle, repeat, next, and previous commands update the view
optimistically. Next/previous reset progress before network I/O and dispatch
their POST without waiting behind snapshot reconciliation. Each successful
transport command supersedes any older reconciliation and starts five bounded,
increasing-delay snapshot attempts; normal two-second polling remains the
fallback. This keeps repeated LB/RB input responsive without allowing unbounded
polling work. Like, dislike, shuffle, and repeat publish the intended
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
artifacts/community-addons/ytmusic/org.gbar.samples.ytmusic-0.2.1.gbarwidget
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
  .\artifacts\community-addons\ytmusic\org.gbar.samples.ytmusic-0.2.1.gbarwidget
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

To review or test version behavior with the public CLI:

```powershell
& $gbar disable org.gbar.samples.ytmusic
& $gbar version list org.gbar.samples.ytmusic
& $gbar version select org.gbar.samples.ytmusic 0.2.1
& $gbar enable org.gbar.samples.ytmusic
```

The current CLI has no version-removal command. Unsigned authority is derived
from the host-verified package content tree, so changed package bytes use a new
secret namespace and require pairing again; rollback to the exact verified
bytes regains the prior namespace. Publisher signing and uninstall secret
cleanup are not implemented, so do not promise authenticated-update retention
or uninstall cleanup yet.

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
