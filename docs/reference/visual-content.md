# Artwork, backgrounds, and feedback

Use images to identify content and feedback to explain actions. They should
support navigation without becoming unexpected focus targets.

## Images and posters

An image is presentation content. A poster combines artwork, labels, and an
action in one focusable surface. Use `UI.PosterTile` when choosing that item
is the main action, or `UI.ActionSurface` for a custom card.

Provide a meaningful label when artwork is missing. Loading or unavailable
artwork should not remove the item's action or make its identity change.

The host owns image loading and caches. A widget supplies a supported image
reference, not a decoder or a desktop file-access bypass. See [Artwork caching](artwork-disk-cache.md).

## Icons

Semantic icons can follow the user's theme. A package can also declare supported
SVG icon assets and refer to them through `WidgetIcon`, with a semantic fallback.
The manifest controls those assets; raw SVG bytes do not belong in a view snapshot.

Preserve provider-brand colors when appropriate rather than treating every logo
as a monochrome symbol. See [Icon assets and provenance](widget-icon-provenance.md).

## Background surfaces

`UI.BackgroundSurface` places artwork behind a section. A background can follow
the focused item, such as the currently highlighted game. The host owns the
transition between artwork states.

Keep surface identities stable across ordinary updates. Unrelated text or loading
changes should not create a new surface or clear its current artwork. Moving
focus to the tray is not by itself a request to restore an initial background.

Backgrounds are decoration. Text and controls still need readable contrast when
the image is bright, unavailable, or changing.

## Toasts

Use `UI.Toast` for brief success or error feedback. A toast is non-focusable and
expires; a persistent problem should also have a useful state or retry action
where the user can address it.

Keep error messages plain: explain what failed and what the user can try.
Use theme-aware tones instead of a fixed white text label for every outcome.

## Live content

`UI.WindowPreview` shows an application window as live, view-only content.
Embedded video uses a separate host-managed media session. Neither means arbitrary
web content can be placed into an ordinary image element.

## Focus-associated content and encoded artwork

`FocusPresentationSurfaceElement` displays the presentation associated with the
current focus, followed by ordinary content. The associated fragment is
non-interactive: only the content owns focus and actions. Use it for a detail
summary that changes with selection, without duplicating the list itself.

`WidgetEncodedArtwork` represents value-owned PNG, JPEG, or WebP bytes for the
supported trusted-artwork path. Declaring an encoded format is not permission
to read arbitrary files or a promise of animated WebP playback.

For diagnostic text, `UI.CodeText` provides a semantic text helper. It is not
a browser editor or an arbitrary syntax-highlighting runtime.

See [Window previews](window-previews.md), [Media and pinning](../developers/media-and-pinning.md),
and the exact APIs in [`TileComponents.cs`](../../src/WidgetSdk/TileComponents.cs),
[`BackgroundSurface.cs`](../../src/WidgetSdk/BackgroundSurface.cs), and
[`Toast.cs`](../../src/WidgetSdk/Toast.cs).
