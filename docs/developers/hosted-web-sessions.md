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

## Remote page actions

Remote sessions can opt into a sealed page adapter (protocol 86). This is separate
from packaged-page RPC: ordinary remote websites still cannot post widget messages.
Set `WebSessionDefinition.PageAdapter` with a package-local `ScriptAsset`, exact
HTTPS `Origins`, and optional `Responses` rules. A response rule names an allowed
origin and a slash-terminated path prefix. Response access is explicit; declaring
an origin alone does not grant response bodies.

The default `IsolatedPageActions` mode runs the script in an isolated JavaScript
world in the top-level document. It
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

## Background website backends

A full-access application widget can use `WebSessionPageAdapterMode.SessionBackend`
(protocol 102) for native controls backed by a website's own JavaScript application.
This is a separate trust contract from isolated page actions: the sealed adapter
runs in the website's main document world and can access its application state.
The host admits it only for full-trust widgets, exact declared HTTPS origins and
`BrowserBackgroundMode.KeepAlive`. `Responses` must be empty. Ordinary webapps,
plain remote sessions and sandboxed widgets do not acquire this authority.

```csharp
backend = await HostServices.WebSessions.OpenAsync(
    new("provider", "Provider account", WebSessionSource.Remote("https://provider.example/app"))
    {
        Browser = new()
        {
            Storage = BrowserStorage.Persistent,
            ProfileKey = "provider-account",
            Background = BrowserBackgroundMode.KeepAlive,
        },
        PageAdapter = new()
        {
            Mode = WebSessionPageAdapterMode.SessionBackend,
            ScriptAsset = "payload/web/backend.js",
            Origins = ["https://provider.example"],
        },
    }, cancellationToken);
```

The SDK selects the `OpenBackend` operation from the definition; an unsupported
host fails instead of falling back to a less capable adapter. Present
`UI.WebView(backend.Reference, "provider.account")` for sign-in, challenges or
account management. Removing that view leaves the same backend session alive.
Opening a remote session starts navigation; it does not prove that the provider
or its adapter is ready. Handle temporary unavailability without replaying mutations.

The backend receives `host.onRequest(...)` and `host.batchResult(context, items)`.
Current hosts also expose `host.invalidate()` for payload-free state-change
notifications. Feature-probe this method when supporting older hosts. The host
coalesces bursts over 100 ms and permits only one outstanding notification until
the widget issues another admitted backend request. Consume
`{ "kind": "backend-invalidated" }` through `ReadMessagesAsync` and refresh the
appropriate resource; changes during an in-flight read need a subsequent read,
not just joining the old one. The notification authorizes no action and carries
no provider state. Navigation, context retirement and teardown cancel pending
notifications. Provider-store subscriptions and their account semantics remain
the widget adapter's responsibility; remove subscriptions when the document ends.
Use `backend.RequestAsync<TRequest,TReply>(method, payload, token)` from native
widget code. Correlation, cancellation and document retirement follow the shared
request protocol, including the 16-request limit and 30-second deadline. Unlike
page actions, backend requests remain available while its view is hidden. The
backend cannot emit unsolicited native actions or register network-response
observation; site-specific state and commands remain the adapter's responsibility.
Website data and replies are untrusted, including those produced in this shared
document world. Validate account, call/item identity and response shape before use.

Backend response batches permit up to 1,000 items. Isolated page-action batches
remain limited to 100. Both retain the same 2 MiB aggregate item budget, 48 chunks,
48,000-byte chunk budget and 64 KiB message envelope. The SDK selects the matching
assembler automatically. Put large participant/item collections in `items`, not
in `context`, which repeats in every chunk. An invalid or oversized batch fails
as a whole; the host does not silently truncate it.

## Microphone admission from native controls

For a backend session, `await backend.RequestMicrophoneAsync(token)` (protocol 103)
opens the host's microphone consent flow for the current document origin and
browser profile. Request it from an explicit interactive user action, before the
provider's Join/unmute operation, rather than from a polling loop. Run long consent
and provider work through `WidgetOperations` so the initiating control stays usable.

The returned `WebSessionMicrophoneResult.State` is `Granted`, `Denied`, `Cancelled`
or `Unavailable`. `Remembered` describes an existing saved decision or a grant
the user chose to remember when the document first uses its microphone. Navigation
before use retires that pending choice. A grant admits the website; it does not
prove Windows privacy settings, the device or the provider allowed audio capture.
Revalidate the account and selected call after awaiting consent.

Losing interactive ownership returns `Cancelled`. Explicit caller/session
cancellation throws `OperationCanceledException`; unavailable/incompatible service
errors remain capability failures. A hidden backend cannot trigger its own website
permission prompt. A visible account WebView retains the ordinary website permission
UI. The widget never chooses the origin, grants itself consent or reads browser
credentials through this API.

## Share an application through a backend

The host's native web-capture service (protocol 104) delivers one user-selected
application to a backend session. Set `WebSessionDefinition.NativeCapture = true`
when opening that session and declare the optional capability
`system.apps.windows.capture.v1`. The opt-in installs the media adapter; it neither
grants capture permission nor selects a source.

Request capability consent through `HostServices.Permissions.RequestAsync`, then
use the typed service:

```csharp
var prepared = await HostServices.Capture.WebStreams.PrepareAsync(
    backend.Reference, new WebCaptureStreamOptions(720, 30), token);
if (prepared.Phase is not (WebCaptureStreamPhase.Prepared or WebCaptureStreamPhase.Capturing)
    || prepared.Ticket is not { } ticket) return;
// Retain ticket; use the returned quality/audio choice for the provider's share command.
var current = await HostServices.Capture.WebStreams.GetStatusAsync(ticket, token);
await HostServices.Capture.WebStreams.StopAsync(ticket, token);
```

The native chooser owns application enumeration, source identity, optional
application audio and quality. Widgets receive no HWND and need no separate
window-list permission. The original overload above retains exactly 480p or 720p
at 15 or 30 FPS. Protocol 105 adds a provider-constrained overload:

```csharp
var prepared = await HostServices.Capture.WebStreams.PrepareAsync(
    backend.Reference, preferredQuality, providerAllowedQualities, token);
```

`providerAllowedQualities` must contain 1–12 distinct supported values and include
`preferredQuality`. Local transport supports 480p, 720p, 1080p and 1440p at 15,30 or60
FPS. The host intersects that list with local support and rejects any returned
choice outside the admitted list before capture starts. This uses the distinct
`capture.web-prepare-constrained` operation, not a silent widening of old callers.
The widget's adapter owns provider eligibility, revalidation and any saved quality
preference; the host does not infer account entitlements or persist a cancelled
chooser selection. Discord uses its own persisted streaming settings and falls
back to baseline choices if its required policy/settings helpers are unavailable.

The default preference is 720p30. Height is a ceiling and source aspect ratio is
preserved. Static content can deliver fewer frames than the selected target.
Audio is optional application audio, not an implicit whole-system fallback.

The host supplies the selected media stream to that document's `getDisplayMedia`
path. The provider adapter still owns the provider-specific broadcast command;
choosing a source alone does not start a remote broadcast. Use returned `Quality`
and `ApplicationAudio`, and combine local `Capturing` with authoritative provider
state before showing “Sharing.” A delayed preparation reply can already be
`Capturing`. Do not replay an uncertain provider mutation automatically.

`PrepareAsync` requires interactive ownership. Cancelling its chooser does not
select anything; after confirmation, the admitted stream can continue while the
overlay is hidden. `GetStatusAsync` and `StopAsync` remain available in the background.
Retain the exact opaque ticket until cleanup is confirmed. Backend closure or
permission retirement requests native cleanup; queued cleanup is not a successful
Stop. Known ticket status survives backend closure for the same live owner, with
bounded terminal history. A failed stop must remain visible and retryable rather
than being reported as success. At most two unconfirmed/live captures are admitted
globally; uncertain teardown keeps its slot reserved.

These are local capture guarantees, not proof of another participant receiving
picture or sound. The [Discord WebView sample](../../samples/DiscordWebViewWidget)
demonstrates consent, account/call checks, provider reconciliation and simultaneous
incoming pin/outgoing capture. Its provider integration remains under development.

## Remote websites in widget modals

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

Sealed packaged pages receive the ordinary page message channel. Plain remote
HTTP(S) sessions cannot exchange widget messages; the explicit page-action and
backend adapters above use their scoped request contracts instead. Include the
SDK script before registering packaged-page handlers:

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

The default verification manifest includes a separate `discord` managed shard:
both Discord widget test executables and the shared provider, module-discovery,
command, generated-media and backend JavaScript tests. CI provisions Node 22 for
that shard. These use synthetic providers and media; they never sign in, join a
real call or broadcast an application. The SDK and broker suites cover shared
backend, pinning, microphone and capture contracts separately.

Native probes in `tools/DiscordWebViewPrototype` cover backend sessions, pin
requests, microphone consent and captured-media delivery. They require the matching
frontend build and an isolated profile. Passing those probes is not evidence of
remote Discord picture/audio reception or every physical controller path.

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
