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

Packaged sessions admit at most 32 sealed resources, 256 KiB per file and 2 MiB
total decoded bytes. The host verifies package inventory and hashes before serving
them. These transfer bounds are separate from embedded-media adapters (16 resources,
512 KiB total); neither is a decoded image-memory budget. Applications should load
artwork on demand, bound their decoded cache and release unused resources.

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

## Remote websites in widget modals

Remote sessions can opt into a sealed page adapter (protocol 86). This is separate
from packaged-page RPC: ordinary remote websites still cannot post widget messages.
Set `WebSessionDefinition.PageAdapter` with a package-local `ScriptAsset`, exact
HTTPS `Origins`, and optional `Responses` rules. A response rule names an allowed
origin and a slash-terminated path prefix. Response access is explicit; declaring
an origin alone does not grant response bodies.

The script runs in an isolated JavaScript world in the top-level document. It
receives `host.onResponse(callback)` for declared JSON responses already loaded
by the site, and `host.addAction(parentElement, actionKey, label, getPayload)` to
insert a normal, accessible button. Actions require trusted browser clicks and an
interactive visible session. Page-script `.click()` calls are ignored. No cookies,
request headers, arbitrary CDP methods, or additional authenticated fetch API are
exposed by this contract. Website DOM and response data remain untrusted inputs.

Read results through `session.ReadMessagesAsync()`. An action event has `kind:
"page-action"`, `action`, `document`, and `payload`; recoverable failures use
`kind: "page-action-error"` and an error code. Validate the site's schema and
selection identity before changing widget state. Keep site selectors/converters
in the adapter and widget, rather than the host.

For a user-selected collection, `host.addBatchAction(parentElement, actionKey,
label, getPayload)` accepts `{ context, items }`. It requires the same trusted
click and visible-session authority as `addAction`. The host validates the complete
payload before delivery, then sends ordered `page-action` chunks with `batchId`,
`index`, `total`, `context`, and `items`. Limits are 100 items, 2 MiB of item data,
48 chunks, and 48,000 bytes per chunk before the message envelope. An oversized
item or batch is rejected without sending partial results. Feature-detect this
optional API when supporting older hosts. `WebSessionPageActionBatchReader` in the
SDK assembles a complete ordered batch with a 30-second deadline; use one reader
per session and validate the resulting website-specific records before use.
The chunk budget also reserves space for the current document URL. Page actions
and ordinary messages use the same `MaximumMessageDepth` (16) and 64 KiB wire
limit; JSON transport preserves UTF-8 rather than expanding Unicode into escapes.

An adapted session can also use the existing SDK `RequestAsync` request/cancel
protocol. Register `host.onRequest(async (method, payload, operation) => ...)`
inside the sealed adapter. `operation.signal` supports cancellation and
`operation.progress(value)` delivers a bounded `kind: "progress"` event with the
request ID through `ReadMessagesAsync`. Return an ordinary JSON value, or
`host.batchResult(context, items)` for the same bounded collection format above.
The SDK assembles response batches before completing the request; incomplete,
expired, cancelled, and navigated requests cannot complete with partial data.
There are at most 16 requests per session and a 30-second request deadline.
Closing/hiding the page aborts active adapter work. Request messages go only to
the declared origin's isolated adapter, never to the website's main JavaScript
world. Plain remote sessions without an adapter still reject messaging. This
does not expose a network-fetch API or bypass the site's normal loading flow.

Adapted sessions also emit `page-location` with `url` when the top-level location
changes within a declared origin. This includes same-document navigation. A widget
that persists return destinations must allowlist appropriate public paths and
query parameters; do not save sign-in redirects or action tokens. This observation
does not permit the website to invoke widget actions.

Scripts are capped at 64 KiB. Responses default to 256 KiB and may declare up to
1 MiB; the host bounds network buffering and concurrent body reads. Results use
the existing 64 KiB message bound. Navigation retires the document context and
pending response authority. Only matching top-level frame/loader responses are
delivered; worker and unrelated-frame traffic is not part of this contract.
`OpenAdapted` fails on older hosts rather than silently opening without the adapter.

Use the same session API for a website that should open above the widget and
return to it when dismissed. No cross-widget intent is needed:

```csharp
website = await HostServices.WebSessions.OpenAsync(
    new("websites", "Reference website", WebSessionSource.Remote(url))
    {
        Browser = new()
        {
            Storage = BrowserStorage.Persistent,
            ProfileKey = "websites",
            Background = BrowserBackgroundMode.Automatic,
            Chrome = BrowserChrome.Toolbar,
            Popups = BrowserPopupMode.Hosted,
        },
    }, cancellationToken);

// Render the ordinary view underneath. Set websiteVisible=false on website-close.
return websiteVisible
    ? view.WithModal(new("website-modal", "Website",
        UI.WebView(website.Reference, "website-view",
            interactionMode: BrowserInteractionMode.InteractOnFocus)
            .Classes("website-view"),
        "website-view", "website-close") { ScrollContent = false })
    : view;
```

Give the modal a generous preferred size with an ID selector and give the
WebView `flex-grow: 1; min-height: 0px; min-width: 0px;`. The shared modal clamps
its size to the widget viewport. The browser supplies its own page scrolling.
Normal modal dismissal restores focus in the underlying widget.

Retain `website` to reopen the same page without reloading. Dispose it when done
and during widget destruction; the host also retires sessions with their worker.
The profile's cookies and website storage survive session disposal and overlay
restart. Profile keys are widget-specific: matching keys in different widgets
do not share logins. This does not use the Browser widget's profile.

Remote open/navigation completes when navigation has been started, not when the
website finishes loading. Present the view to show loading, sign-in, challenges,
or recoverable network errors. Packaged sessions still wait for page readiness
before opening their message channel.

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
64 KiB of UTF-8 and depth 16. The `widgetRail` envelope namespace is reserved for the library.
The host transfers at most two messages per broker batch. Each queue can hold at
most 4 MiB of decoded JSON (plus encoding/object overhead); small-event burst
tolerance is unchanged. Packaged image resources also support transparent WebP.

For larger collections, use `WebSessionMessageBatcher.Create(items, chunk =>
JsonSerializer.SerializeToElement(new { type = "items", items = chunk }))` and send
each returned message with `PostMessageAsync`. The SDK measures the complete JSON
envelope, including escaping and Unicode, preserves order, and rejects an oversized
individual item. Use application revisions or begin/end markers when several
messages must update a page atomically. This helper is explicit collection batching,
not automatic fragmentation of arbitrary messages. Larger messages require an
updated host and the matching packaged `WebSessionRuntime.js`.

## Controller zoom in packaged pages

L2/R2 normally use browser pinch zoom. A packaged page with its own map, chart or
canvas can instead handle the cancelable `widgetrailzoom` event on `window`.
`event.detail.direction` is `-1` for L2 and `1` for R2. Call `preventDefault()`
synchronously and use the same zoom function as the page's minus/plus controls:

```javascript
window.addEventListener('widgetrailzoom', event => {
  event.preventDefault();
  zoomCanvas(event.detail.direction);
});
```

Each trigger press produces one step; holding follows the host's normal repeat
delay and cadence. The event stays within the active packaged page and does not
travel through the widget process. Without a handler that cancels the event,
browser pinch zoom remains the default. Remote websites do not receive this event.
The page owns the scale limits and keeps its toolbar at its normal size.

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

Controller hints follow the active page's available actions. Packaged pages omit
Address and Page actions; Previous and Forward appear only when navigation history
allows them. Reload changes to Stop during loading. These rules also apply to
ordinary browser pages and use the same availability checks as command handling.

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
