# YT Music reference widget

This is a source port of `YtMusicGameBar`, rewritten as a controller-first consumer of the declarative `WidgetSdk`. It demonstrates a useful community-widget shape rather than attempting binary compatibility with Xbox Game Bar.

The port retains the original project's security and integration choices:

- Playback remains owned by YTMDesktop2.
- The client accepts only `http` loopback endpoints on ports 9999-39999.
- Proxies are disabled for the default HTTP handler.
- API error bodies are capped before they reach UI state.
- Artwork is accepted only from HTTPS URLs.
- Pairing tokens are stored as endpoint-scoped, per-user Windows Generic
  Credentials. They are never rendered, logged, placed in widget settings, or
  sent to a different endpoint.
- Pairing requests never carry an existing Bearer token. A successful pairing
  replaces the prior credential, and an HTTP 401 removes the rejected token
  before returning the UI to its pairing-required state.

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

The widget renders explicit disconnected, connecting, pairing, connected, and error states. During pairing it displays the approval code returned by YTMDesktop2, then continues automatically when the companion approves it. A configured server may continue to report `authRequired` after pairing; the widget reconnects with its stored credential and treats an actual HTTP 401—not that configuration flag—as credential rejection. The client and credential store are injected through `IYtMusicClient` and `IYtMusicCredentialStore`, so tests and future capability-broker adapters do not need a live companion application or real credentials.

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

The test executable has no test-framework or other NuGet dependencies. Its fake client, credential store, and HTTP handler cover UI state transitions, shortcuts, command routing, pairing, endpoint rejection, JSON parsing, durable Windows Credential Manager round trips, credential reload/replacement, authorization expiry, and manifest validity.

## Current platform gaps exposed by this port

The sample intentionally does not change the SDK/protocol to work around these gaps:

- The bundled trusted worker currently accesses Windows Credential Manager
  directly. Community AppContainer workers still require a future host secret
  broker; they must never copy this trusted-worker access pattern or keep tokens
  in plaintext widget settings.
- There is no endpoint settings/text-input primitive. This reference uses YTMDesktop2's standard `127.0.0.1:13091` endpoint. Advanced endpoint changes require future controller-friendly settings primitives.
- The manifest can declare `network.loopback:13091`, but enforcement through a capability broker is not implemented by this sample.
- Image, semantic-icon, selected, disabled, and busy protocol state now exist;
  this sample uses selected/busy state for its secondary toggles. The generic
  native renderer and all GBSS state maps are still incomplete.
- Progress is read-only. The current protocol has no slider/scrubber primitive.
