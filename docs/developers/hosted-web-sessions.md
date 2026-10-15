# Hosted web sessions

Use `HostServices.WebSessions` when a widget needs a live web application independent
of a visible browser element. Use `UI.WebBrowser` for ordinary declarative browsing,
or the existing embedded-media contract for its typed media controls. These paths
share the host browser environment; they do not share authority or cookies by default.

## Open and present

```csharp
var source = WebSessionSource.Package("web/index.html",
    [new() { Path = "web/index.html", ContentType = "text/html" },
     new() { Path = "web/web-session.js", ContentType = "text/javascript" }]);
var session = await HostServices.WebSessions.OpenAsync(
    new("application", "My application", source), cancellationToken);
// Optional: include this element in Render(). It borrows the same live session.
var view = UI.WebView(session.Reference, "application.page");
```

`wrail new --profile web-session` scaffolds a packaged page, typed request example,
SDK JavaScript library, project and author tests. The SDK ships `WebSessionRuntime.js`
as content; declare and seal it with your own page. Generated packages put these
files under `payload/web`, so use the generated paths rather than the example above.

A session key is unique within one widget worker. Its opaque reference expires with
that worker; do not persist it. Dispose the session when finished and again safely
from widget teardown. Removing the view preserves the session. Stop, disable, update,
uninstall or worker replacement retires its sessions. Observe `Completion` and handle
errors by offering a fresh open; the host does not replay sensitive commands after a crash.

Default options keep the session resident in the background and use private storage.
Set `Browser.Storage` to persistent only when the application needs saved browser state.
The default profile key isolates sessions. An explicit non-default `ProfileKey` opts
into profile sharing within the same widget, never with unrelated widgets. Background
residency is separate from the automatic Low memory hint after 15 seconds hidden or
passively pinned. The host never calls suspension for this optimization.

Sessions without a view use one lazy, shared offscreen native host inside the
frontend process. Its loaded 1x1 browser backing remains available when the overlay
closes: collapsing the WebView or hiding its native parent can defer new media
loads indefinitely. It never takes input or appears in window switchers. A view
borrows the existing controller; it does not create another playback engine.
Low-memory policy follows WidgetRail presentation, independently of this native
backing. Authors must not use DOM visibility as a signal that their widget has input
focus. A background session can keep page timers/rendering active; dispose it when
its work is finished. This does not change ordinary declarative browser parking.

## Messages

Only sealed packaged pages receive the channel; remote HTTP(S) sessions can navigate
and be presented but cannot exchange widget messages. Include the SDK script before
registering handlers:

```js
WidgetRailWebSession.handle('increment', (payload, { signal }) => {
  return { count: payload.amount + 1 };
});
WidgetRailWebSession.post({ type: 'ready' });
```

```csharp
var reply = await session.RequestAsync<CounterRequest, CounterReply>(
    "increment", new(1), cancellationToken);
await foreach (var message in session.ReadMessagesAsync(cancellationToken))
{
    // Validate application-specific event data before using it.
}
```

Requests are correlated, capped at 16 in flight and expire after 30 seconds. Caller
cancellation sends a best-effort cancellation signal to the page. Handlers should
observe `signal`; cancellation cannot undo effects already performed. Late responses
are discarded. Navigation invalidates pending requests. `PostMessageAsync` sends raw
application data, handled through the ordinary `chrome.webview` message listener.
Unsolicited page messages must be consumed; overflowing the 64-message queue faults
the session rather than silently dropping application events. JSON is limited to
16 KiB and depth 16. The `widgetRail` envelope namespace is reserved for the library.

## Network and security

Package resources must exist in the host-verified inventory and pass digest checks.
Packaged pages have a restrictive content security policy; declare required HTTPS
origins explicitly with `Source.NetworkOrigins` (exact origins or `https://*.example.com`).
The policy also covers subresources and WebSocket connections. Service workers,
workers, object content and form submissions are blocked. This is a bounded packaged
application contract, not unrestricted Electron hosting. Ordinary webapps continue
to use the browser contract and its existing website permissions.

`AllowAutoplay` is explicit for packaged media applications. Start playback through
a message after `OpenAsync` completes, as the music adapters do. Starting audio
during initial HTML parsing was rejected on a fresh private profile even with
Autoplay permission set to Allow; the post-ready command path passed repeatedly.
No global browser autoplay flag or synthesized gesture is used. Other device permissions
are denied for packaged pages. Package views retain page navigation, reload and zoom
controls; address editing, external opening, website permissions, downloads and
separate pop-outs are unavailable. Package links may navigate declared HTML assets. Full-trust widgets can authorize one exact loopback
media URL with `AuthorizeLocalResourceAsync`; replacing it revokes the previous URL.
This does not grant arbitrary localhost access, cookie access, raw host objects,
arbitrary script execution from widget code, or access to other sessions.

The frontend admits at most four resident browser surfaces across existing browsers
and sessions. Opening beyond capacity returns an error; callers should explain the
failure and let users close unused applications. Sharing a browser process can still
create separate renderer processes and does not promise one operating-system process.

## Validation

`BridgeWebSessionFixture` and the native `--validate-hosted-session` scenario exercise
the authenticated worker/broker/bridge path and real page messaging. The separate
`--validate-web-sessions` fixture checks audio, isolation, shared browser identity,
network denial and Low memory behavior. Use synthetic pages and streams; provider
account-dependent behavior still requires physical acceptance. The old YouTube Music
standalone playback executable probe is retired because playback now requires the
host; its Python import-integrity test remains available.

The `--measure-session-latency` variant measures complete SDK-to-page round trips
under events, concurrent sessions and idle/Low transitions. Its music fixture uses
the real packaged playback pages, a synthetic loopback stream or a synthetic
Spotify SDK. `--session-hidden-probe` checks starting media with the native overlay
window closed. These cover delivery and media behavior separately; counter-message
throughput alone is not playback acceptance. The developer-only
`--trace-web-sessions` option logs bounded command receipt/reply and media-state
metadata, never tokens, payloads, media URLs or song titles. Older packages without
SDK timestamps report widget-to-host time as unavailable.
