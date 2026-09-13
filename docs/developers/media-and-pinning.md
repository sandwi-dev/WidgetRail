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

Window previews are live, view-only content. They do not forward controller
input into the source application. Put an action on the surrounding poster
when you want the user to switch to that application.

## Design a view for pinning

A good pinned view has one purpose: show the video, identify the current track,
or expose a few important controls. It does not have to reproduce the full widget.

The widget declares pinning support and publishes its supported layouts. The
user chooses among those layouts in the tray's pin controls. Full-widget pinning
is a separate opt-in; enabling pinned layouts does not offer it automatically.

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

Continue with the [embedded-media guide](embedded-media-widget.md),
[live preview reference](../reference/window-previews.md), or
[SDK pinned-layout APIs](../reference/sdk-reference.md).
