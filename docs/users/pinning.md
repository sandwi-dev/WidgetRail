# Pin video and compact widgets

A **pinned view** stays visible when you hide the main overlay. It is useful for
watching a video beside a game or keeping compact playback information nearby.

## Pin a view

1. Open a widget that supports pinning. For video, first choose the content you want to play.
2. Return to the tray and press **Menu** to open pin controls.
3. Choose an available layout and adjust its placement and size.
4. Hide the overlay. The pinned view remains on screen.

Follow the controller hints in the pin controls as you make adjustments. Later,
you can manage or remove the existing pin from another tray icon too; you do not
have to find the icon that originally created it.

## Why layouts differ

The widget author decides which views can be pinned. A video widget may offer a
player-only view. A music widget may offer compact controls or a larger queue view.
Some widgets have no pinned views at all.

Supporting pinning does not automatically make the whole widget pinnable.
This lets authors avoid offering a tiny, unusable copy of a complex interface.

## Video and game compatibility

The [YouTube add-on](../../samples/YouTubeWidget/README.md) demonstrates pinned
video playback. Its setup and playback depend on the provider. Pinning does not
remove account requirements or provider playback restrictions.

For games, try a windowed or borderless display mode. An always-on-top desktop
window is not a guarantee that a view will appear over every exclusive-fullscreen
game. [Read the compatibility notes](known-limitations.md).
