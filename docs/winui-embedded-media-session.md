# Embedded media through the managed presentation session

The WinUI frontend resolves a widget's declared adapter through `WidgetPresentationSession`. It must not read widget package paths itself or navigate WebView2 directly to package/user URLs. The bridge still validates the installed package inventory and hashes before transferring assets.

## Frontend API

```csharp
var document = await session.ResolveEmbeddedMediaAsync(frame.Authority, cancellationToken);
var state = session.GetEmbeddedMediaState(document);
if (state is null) return; // Retired while resolving or scheduling UI work.

foreach (var resource in document.Resources)
{
    // Path is a normalized declared package-relative resource identifier.
    // Serve only exact declared resources from the frontend's private origin.
    using Stream content = resource.OpenRead();
}

// After authenticating a message from the exact live adapter/controller:
await session.SendEmbeddedMediaPlaybackEventAsync(document, observation, cancellationToken);
```

`WidgetPresentationEmbeddedMediaDocument` exposes its admission `Authority`, `SessionId`, `EntryAsset`, and immutable `Resources`. Each resource exposes `Path`, `ContentType`, `Sha256`, `Length`, and an independent read-only `OpenRead()` stream. Its backing array cannot be exported or written through that stream. The document does not expose a filesystem path, URL, browser object, or arbitrary execution primitive.

`GetEmbeddedMediaState(document)` returns the latest authority and a defensive, read-only copy of the protocol `EmbeddedMediaSession` declaration. Reconcile on `PresentationChanged`, marshaling to the frontend UI thread and rechecking there. The document's original `Authority` does not advance; use the returned state for current playback commands and presentation hints.

## Document lifetime

Identity matches the native resource contract: widget runtime, presentation and local session generation; widget instance; media session ID; entry asset; ordered resource path/content-type declarations; ordered frame origins and domain families. A change to any of these retires the document. Omission, widget failure, catalog removal/replacement, restart, session disposal and terminal transport failure also retire it. Returning to an identical-looking declaration never revives an earlier document.

New snapshot sequences, input scopes, playback commands, accessible names, sizing, aspect ratio, supported presentations and viewport placement do not recreate the document. Declaring media without a viewport parks the retained document. A widget Background lifecycle transition does not itself retire media, allowing the existing retained/background playback contract. The frontend remains responsible for visibility and playback policy.

The session holds only document identity, cancellation and event-order state. It does not cache transferred bytes or own WebView controllers. The frontend must bound controller/document retention, dispose retired browser objects, and release document/resource references. Existing native policy permits at most four retained controllers. Do not create another renderer whenever `PendingCommand` changes.

## Admission and resource limits

Resolution starts against an exact current snapshot authority. A semantically compatible successor arriving while bytes are in flight does not invalidate the result. A changed or removed document cancels the pending local wait and rejects late bytes. Resolution does not retry stale requests.

The response must echo the exact request identity and match all fields of the admitted declaration. The existing protocol validator enforces resource paths, content types, entry HTML, exact HTTPS origins, registrable domain families, command vocabulary and presentation bounds. Every decoded resource must match its SHA256. Transfers are capped at 16 resources, 256 KiB per resource and 512 KiB aggregate. Four resolution exchanges and sixteen pending local resolutions are permitted at once; transport admission retains its own independent bounds.

`EmbeddedMediaTimeout` defaults to 15 seconds and can be configured from 100 ms to two minutes. It bounds media resolution and observation waits independently of artwork. Caller cancellation does not imply that the bridge canceled server work; canceled wire exchanges retain their correlation until their replies arrive.

## Playback observations

The frontend authenticates the WebView message source and its exact environment/controller/document/command generation before constructing an `EmbeddedMediaPlaybackEvent`. This API is not a web-message parser. It does not authorize arbitrary JavaScript, navigation, origin access, DOM operations, windows or network requests.

The session enforces finite bounded playback values, safe identifiers, a current media document, monotonically increasing event sequence, and the exact current pending command sequence/media key. Preference command terminals must report the requested rate, mute or loop value unless reporting an error. The bridge independently performs final command admission and prevents duplicate command terminals.

Observation sends are serialized per semantic document and revalidated after waiting, with a maximum of sixteen pending observations. Sequence numbers are consumed once a send starts, even if cancellation leaves delivery uncertain. Never replay an uncertain event. Ordinary unsolicited observations (`CommandSequence = 0`) use current snapshot authority; command terminals must still match current pending command state. Compatible snapshot updates are not a reason to recreate a player.

## Evidence and remaining responsibility

Deterministic tests cover wire authority, response substitution, malformed base64/digests, traversal/content type/origin/family violations, per-resource and aggregate bounds, document replacement/return, harmless pending replies, command updates, cancellation/timeouts, admission saturation, restart/catalog retirement and uncertain-event replay prevention. These tests validate the session boundary, not browser rendering or real playback. The frontend separately owns private-origin request interception, frame navigation policy, generation-authenticated messages, HWND/composition integration, focus, resource cleanup and actual playback validation.
