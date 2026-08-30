# Embedded-media widget template and lifecycle

Use the provider-neutral `embedded-media` starter when a widget needs a native
controller shell around one host-owned media plane:

```powershell
& $wrail new widget MediaDeck `
  --template embedded-media `
  --output .\scratch\MediaDeck `
  --id dev.example.media-deck `
  --publisher dev.example
cd .\scratch\MediaDeck
dotnet build .\MediaDeck.csproj --configuration Release
dotnet test --project .\tests\MediaDeck.Tests.csproj `
  --configuration Release --no-ansi --progress off --output Detailed `
  --minimum-expected-tests 4
& $wrail preview . --scenario ready
& $wrail validate .
```

The generated widget is a complete two-source example. It declares one native
full player, a configurable seek step, an optional host-owned compact pinned
presentation, a retained-hidden library route, exact command terminals, bounded
provider errors, and sealed local fake media. It needs no provider SDK,
credential, account, network, browser automation, or native-host source.

## Runtime ownership

`EmbeddedMediaAdapterRuntime.js` is the canonical implementation of document
initialization, authority generations, one in-flight command, exact terminal
correlation, unsolicited observations, cancellation, and bounded error tokens.
It is opt-in WidgetSdk `contentFiles`; the host never injects it.

The generated project explicitly stages the canonical file beside
`adapter.html` as `media/adapter-runtime.js`. The surface declares the exact
package path `payload/media/adapter-runtime.js` with
`application/javascript`, and the document loads `adapter-runtime.js` before
provider code. The provider-neutral driver calls
`WidgetRailEmbeddedMediaAdapter.create` with focus, bounds, snapshot, and the
applicable load/cue/activate/pause/toggle/seek/volume/navigation hooks. Driver
hooks operate a player; they never post host envelopes or duplicate runtime
authority.

Repository samples use explicit MSBuild Link/Copy from the source tree and
package scripts may use explicit `Copy-Item`. An external NuGet widget instead
copies from `contentFiles/any/any/WidgetRail`. These are author-owned staging
steps, not automatic injection.

## Lifecycle state table

| Boundary | Widget publication | Pending/terminal rule |
| --- | --- | --- |
| First visible activation | Viewport plus one `Load` | Wait for one exact correlated terminal; do not retry or poll. |
| Stable player | Viewport plus current observation | Admit command-sequence-zero observations without treating them as terminals. |
| One action pending | Same viewport and one `PendingCommand` | Keep one in flight; only its exact terminal clears it. |
| Library/hidden route after load | Same surface, no viewport, `RetainSessionWhenHidden = true` | Do not send `Load` or `Cue`; returning reattaches the resident document. |
| Library route before load | No surface and no viewport | Hidden retention cannot create a session. |
| Provider error | Bounded package status | Clear the exact command; retain last resident identity only when it existed. |
| Controller/document replacement | New authoritative declaration/generation | Abort old driver work and reject late events. |
| Removal, restart, eviction, adapter crash, or host shutdown | No retained contract | Treat as terminal; do not persist pending/browser authority. |

The template's declared command matrix and its driver are checked by the same
provider-neutral WIDGE-66 conformance gate used for repository media packages.
That gate derives adapter messages from the serialized public declaration and
requires one successful or explicitly induced error terminal, exact command
correlation, closed in-flight state, and rejection of unsolicited false
acknowledgement. It uses a deterministic fake player rather than a WebView or
live provider.

For field-level limits and fullscreen/compact behavior, continue with the
[declarative UI embedded-media reference](declarative-ui.md).
