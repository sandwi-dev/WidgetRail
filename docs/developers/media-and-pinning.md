# Media and pinned layouts

You can build a media interface from native controls while the host presents
the video or preview inside it. Your widget still owns the surrounding actions,
navigation, and content selection.

## Choose the kind of content

| You want to show… | Start with |
|---|---|
| Ordinary artwork | Image elements or poster artwork |
| A live application window | `UI.WindowPreview` and its preview permission |
| Embedded media with a player adapter | The `embedded-media` template |
| Compact playback controls | The `media` template and pinned layouts |
| A website session behind native controls or a pinned receiver | [Hosted web sessions](hosted-web-sessions.md) and `UI.WebView` |
| Share one application through a website backend | `HostServices.Capture.WebStreams` and its native chooser |

Window previews are live, view-only content. They do not forward controller
input into the source application. Put an action on the surrounding poster
when you want the user to switch to that application.

## Recover unavailable artwork

When artwork supplied by a widget fails, the host shows a fallback and remembers
the failure. Ordinary redraws and snapshot updates do not keep requesting it.
Replacing the widget's runtime or package lets the visible artwork try again.
The tray's Reload action also clears that widget's failed artwork.

Successfully loaded images stay cached. Use a new artwork handle when the image
content changes; reloading a widget does not force existing images to download again.

## Design a view for pinning

A good pinned view has one purpose: show the video, identify the current track,
or expose a few important controls. It does not have to reproduce the full widget.

The widget declares pinning support and publishes its supported layouts. The
user chooses among those layouts in the tray's pin controls. Full-widget pinning
is a separate opt-in; enabling pinned layouts does not offer it automatically.

A native “Watch” or “Pin call” action can also await
`HostServices.Pinning.RequestOpenAsync(layoutHandle, cancellationToken)` from its
action handler. The host owns replacement, saved dimensions and passive placement.
Do not create a second window or simulate the tray interaction. A successful
receipt admits the request; use the handle's selection lifetime to start the
receiver only once that layout is actually selected. See
[direct pin requests](../reference/pinned-layouts.md#open-a-pin-from-a-widget-action).

The `media` template demonstrates **Compact media** and **Media and queue**
layouts. The `embedded-media` template demonstrates a player with host-owned
presentation. Start from these examples before managing a media session from scratch.

## Keep presentation and playback separate

Hiding the overlay or moving between routes may remove the visible player area
without ending playback. Preserve the session when the widget's chosen lifecycle
requires it. Avoid reloading the provider every time focus changes.

Removing a session, restarting the worker, or shutting down the host is different:
the old media session is no longer usable. Recreate it through the supported API.

Providers can refuse playback or report unavailable content. Show a useful state
and keep the surrounding UI usable. Pinning does not bypass provider restrictions.

A call backend and an incoming-video pin can have different lifetimes. Keep the
call session alive when its main view disappears; create the receiver lazily under
`PinnedLayoutHandle.SelectionCancellationToken` and dispose it when the pin is
replaced. The backend can remain the audio owner while the pinned receiver shows
video only, avoiding duplicate playback. WidgetRail owns browser/pin windows and
generic lifecycle; the widget owns which account, call and stream to receive.

Outgoing sharing is separate from pinning and live window previews. The capture
service asks the user to select an application, optional application audio and
quality, then returns a ticket bound to the backend session. It does not start a
provider broadcast itself. Keep local capture state distinct from the provider's
broadcast state, and stop the exact ticket on source/account/call retirement. See
[sharing through a backend](hosted-web-sessions.md#share-an-application-through-a-backend)
for consent, quality and confirmed-cleanup rules.

Continue with the [embedded-media guide](embedded-media-widget.md),
[live preview reference](../reference/window-previews.md), or
[SDK pinned-layout APIs](../reference/sdk-reference.md).
