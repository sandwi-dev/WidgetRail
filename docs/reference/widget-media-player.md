# Native media player

`UI.MediaPlayer` (protocol 75) is the shared inline WinUI media element. Game Help
uses it for capture review; captures do not have a separate playback renderer.
The legacy `UI.CapturedMedia` declaration remains compatible and maps to this same
renderer. Captured still images use a shared zoom/pan viewer with image controls.

```csharp
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

UI.MediaPlayer(
    MediaPlayerSource.WebUrl("https://example.com/guide.mp4"),
    "guide.player",
    "Video guide",
    options: new MediaPlayerOptions(AutoPlay: false, Volume: 0.8));

UI.MediaPlayer(MediaPlayerSource.PackageAsset("media/tutorial.mp4"), "tutorial");
UI.MediaPlayer(MediaPlayerSource.FromCapture(attachment), "preview",
    options: new MediaPlayerOptions(Muted: true));

// Protocol 116: reuse the gallery's scoped thumbnail handle.
UI.MediaPlayer(MediaPlayerSource.FromSavedCapture(item.Id, item.Revision), "clip")
    .WithPoster(new WidgetArtworkHandle("clip.thumbnail"));

// Full-trust widgets only: ordinary local drive paths, not device/UNC paths.
UI.MediaPlayer(MediaPlayerSource.LocalFile(@"C:\Videos\guide.mkv"), "local-video");

// Protocol 129: images use the shared zoom/pan/fullscreen viewer.
UI.MediaPlayer(MediaPlayerSource.WebImage("https://example.com/photo.jpg"), "photo");

// One native viewer; only its selected item acquires resources.
UI.MediaGallery([
    new("photo", "Photo", MediaPlayerSource.WebImage("https://example.com/photo.jpg")),
    new("clip", "Clip", MediaPlayerSource.WebUrl("https://example.com/clip.mp4"))
], "attachments");

// Protocol 131: silent decorative video in a message or other collection row.
UI.Animation("https://example.com/loop.mp4", "message.animation", "GIF")
    .WithPosterSource("https://example.com/poster.png");

// Protocol 133: recommended for anonymous remote attachments and CDN artwork.
UI.MediaPlayer(MediaPlayerSource.PublicFile("https://cdn.example/clip.mp4"), "clip");
UI.MediaPlayer(MediaPlayerSource.PublicImage("https://cdn.example/photo.png"), "photo");
UI.PublicImage("https://cdn.example/avatar.png", "avatar", "Avatar");
UI.PublicAnimation("https://cdn.example/loop.mp4", "gif")
    .WithPosterSource("https://cdn.example/poster.png");

// Protocol 134: explicit Play, native row controls, no eager video acquisition.
UI.InlineVideo(MediaPlayerSource.PublicFile("https://cdn.example/clip.mp4"), "message.video", "Video attachment")
    .WithPosterSource("https://cdn.example/poster.jpg");

// Protocol 140. One stage above a directly navigable thumbnail strip.
UI.MediaGallery([
    new("trailer", "Release trailer", MediaPlayerSource.PublicDash("https://cdn.example/trailer.mpd"),
        new(AutoPlay: true, Muted: true)) { PublicThumbnail = "https://cdn.example/trailer.jpg" },
    new("scene", "Gameplay screenshot", MediaPlayerSource.PublicImage("https://cdn.example/scene.jpg"))
        { PublicThumbnail = "https://cdn.example/scene-small.jpg" }
], "game.media", "Game media");
```

Supply a public thumbnail for every gallery member to show the strip. The player
fetches thumbnails and one selected original. A newly opened gallery focuses
the first thumbnail. Left and Right preview neighboring items, Up reaches
the media controls, and activating the selected thumbnail opens
fullscreen. B returns to the strip. A single item uses the stage alone. A
failed autoplay trailer advances to another declared member if one is available.
When all members of a thumbnail gallery are failed autoplay trailers, the player
collapses for that declaration. Opening another gallery restores its media area.

Pass `inlineCarousel: true` to the `UI.MediaGallery` overload to use the spare
width around the selected media for adjacent stills (protocol 142). Give the
player a bounded height for a predictable preview area. The first item aligns
left, interior items center, and the last aligns right. A single item uses the
ordinary centered player. The thumbnail strip owns keyboard and controller
navigation. Clicking a neighboring still selects its thumbnail.

Inline carousel screenshots use the thumbnail strip for interaction. Open the
selected thumbnail in fullscreen for zoom and Fit. Fullscreen image navigation
keeps focus on controls, and the right stick pans a zoomed image from there.
Returning to the carousel fits the image and restores its thumbnail.

For a public screenshot, supply the original with `MediaPlayerSource.PublicImage`
and its small image as `PublicThumbnail`. A trailer can additionally declare an
anonymous HTTPS `PublicPreviewImage` for a larger poster. The host sizes still
decodes to the display, starts from available thumbnail artwork, and cancels
requests when a neighbor leaves the carousel. Only the selected trailer plays.

Protocol 141 also accepts `MediaPlayerSource.EmbeddedSession(sessionId, mediaKey)`
in a gallery. The widget declares its embedded session on the same view, with
its entry asset, allowed origins and supported commands. Selecting that item
binds the private player to the stage. The native play and mute controls reflect
the playback state reported by the adapter. Keep an asynchronous adapter command
pending until the provider reports the requested state. Fullscreen retains the
player, and selecting another source releases its viewport.

`PublicDash` accepts static, clear H.264/AAC MP4 presentations with one period
and fixed-duration numbered segments. The host reconstructs a local manifest
and resolves only its declared segment addresses through anonymous same-origin
transport. It caps manifest size at 128 KiB, duration at ten minutes, declared
video size at 1920 × 1080, individual segments at 8 MiB and total transfer work
at 128 MiB per player source. Unsupported manifests enter normal media failure
handling.

`UI.InlineVideo` is a host-owned interactive control, including inside a
`ReadingGroup`. Its poster may load through artwork demand, but the video source
and decoder are acquired only after explicit Play. It reuses normal native seek,
audio, playback options and fullscreen controls. Scrolling it offscreen or hiding
its presentation stops playback and releases its decoder; returning does not
autoplay. After a short scroll away, a still-realized player can reuse checked
compressed data from its last playback. The host keeps at most one such entry
for 15 seconds and checks the current source grant before using it. Closing the
chat, replacing the source, or recycling the row ends this reuse. Decoder slots
are released offscreen. Audio preferences use the same per-widget host store
as other players.
Its declaration cannot autoplay, be a passive animation, contain a gallery, or
use an image-only source. Indexed playback resolves through the exact current
row lease; it does not fabricate a widget action for native transport controls.

The `Public*` APIs request host-owned, anonymous, bounded HTTPS acquisition.
The widget supplies an authorized provider URL; the host pins all redirects to
that exact origin and validates the connected public IP address. It sends no
cookies, credentials or session headers, rejects private/local destinations,
non-default ports and embedded credentials, and passes owned bytes or a checked
random-access stream to native decoders. Signed query parameters are preserved
for the request but are not included in error logs. Native format inspection
uses the content, not the filename or advertised MIME type. Failure remains an
unavailable-media state and never opens an external URL automatically.

Public files support native MP4/WebM and MP3/Ogg containers within host limits;
this is not an adaptive-playlist API. Images use the existing 8 MiB and source
pixel bounds. File reads have a 512 MiB logical size limit, a 4 MiB block cache,
an 8 MiB whole-file fallback for servers without stable range support, and a
finite 1 GiB transfer-work budget per source lifetime. Containers with video
metadata above 4096 pixels per axis, 16,777,216 pixels or 120 FPS are rejected;
audio metadata is limited to eight channels and 192 kHz. Four concurrent decode
admissions cover public acquisition/inspection. Pending work belongs to the
presenter's bounded live resource demands, waits before allocating media bytes,
and cancels when that presentation is retired.
Interactive and passive player budgets still apply separately.

`PublicAnimation` retains the visibility, reduced-motion and decoder-lifetime
rules below. Its poster also uses anonymous same-origin acquisition relative to
the poster URL. Public gallery policy belongs to each source, survives item
selection and cannot be downgraded by a resolution request. Protocol 133 is
required so older hosts cannot silently ignore these guarantees. Existing
`WebUrl`, `WebImage` and `UI.Image` APIs retain their previous compatibility
behavior; local/package/capture authorities are unchanged.

`UI.Animation` is noninteractive and does not take focus from its containing
row. It requires a direct HTTPS video URL and always loops silently. The host
starts playback only while the presentation and its viewport are visible,
releases the decoder offscreen/hidden, and leaves the poster under reduced
motion. Provide a poster using the ordinary bounded artwork-source contract.
The host reserves at most two additional decoders for passive animations;
overflow keeps its poster until a slot becomes available. These do not consume
the four interactive player slots or publish system media controls.

Use `MediaPlayerOptions.IsAnimation = true` for an animation inside an
interactive player or gallery. This applies visible-only and reduced-motion
autoplay policy while retaining explicit Play/Pause. Explicit Play can override
reduced motion; a user pause survives a same-source hide/reopen. Ordinary video
options are unchanged: autoplay/loop/muted alone do not imply decorative motion.
Indexed animations resolve only the exact body declaration on a live row lease;
this does not authorize ordinary interactive media in a row or relax preview,
capture, file-access or gallery authority.

## Saved captures in a timeline

`UI.CaptureStrip` builds a horizontal thumbnail row with an optional preview
above it. Use it as an indexed item. Supply up to 16 distinct available saved
captures in display order, each with its own thumbnail handle, accessible name
and selection action. Only the expanded capture declares an original media
source. Images fit inside a 16:9 frame and video thumbnails carry a play icon.

```csharp
return UI.CaptureStrip(ids.Id("row"), "Friday captures", items,
    selectedCaptureId: row.SelectedCaptureId, expanded: row.Expanded,
    closeActionId: "captures.close");
```

The widget owns the selection and the expansion state. Update the same
occurrence after a selection action. Keep its selected capture ID when closing.
An explicit collection entry request for that occurrence can then restore the
selected thumbnail. Selecting another thumbnail switches the full-width preview.
Activating the open thumbnail again, pressing B, or pressing Escape on a
thumbnail invokes the close action. Widgets can customize the
`wrail-capture-strip` classes in WRSS. Protocol 139 requires the
exact current row lease, an interactive reading group and the declared saved
capture revision. Capture Library read permission still applies.

## Sources and authority

- Packaged assets are resolved inside the widget's sealed package inventory. The
  Bridge checks the file size/digest and rejects path traversal and reparse points.
- Direct HTTP(S) media URLs can be used by both sandboxed and full-trust widgets.
  The native Windows player determines available codecs and stream support.
- `WebImage` (protocol129) explicitly declares an HTTPS image. It uses the bounded
  native artwork decoder, with an 8MiB encoded bound, at most4096 pixels per axis
  and16,777,216 source pixels. Within those limits, the viewer retains original
  image resolution for zoom. The host does not guess image intent from a URL's
  extension. Image formats must be supported by the installed native decoder.
- Capture references retain ownership, permission, expiry and revocation checks.
  Captured application metadata remains attached to the source.
- `MediaPlayerSource.FromSavedCapture(item.Id, item.Revision)` (protocol 116)
  declares saved-library media without opening it in the widget. The host checks
  the current declared element, library read permission and exact content revision
  before acquiring playback custody. Videos retain the authorized original file;
  they are not copied to temporary storage. Cached declarations contain only IDs
  and revisions, never file handles or attachment leases.
- Arbitrary local file paths require full trust. A sandboxed widget cannot ask the
  host to read an arbitrary file. User-selected file grants are not implemented.

The native player supports media resources, not webpages or provider authentication
flows. YouTube webpage links still use the YouTube widget's embedded player.
Custom HTTP headers, DRM license workflows and arbitrary widget-supplied HTML or
scripts are not part of this source contract. Unsupported formats fail inside the
player rather than replacing the whole widget with an error.

Source resolution checks the displayed declaration and exact worker before and
after asynchronous resolution. A new source retires the old load. Compatible
snapshots preserve playback. Increment `revision` to reopen the same resource.
`MediaPlayerOptions` supplies initial autoplay, loop, mute, volume (0-1) and rate
(0.5-2). Changes to loop/mute/volume/rate also update an already loaded player.
Native user changes are not reset by unchanged widget snapshots.

## Multiple attachments

`UI.MediaGallery` (protocol129) takes1–16 `MediaPlayerGalleryItem` values with
unique stable IDs, labels and ordinary media sources. An item's optional
`MediaPlayerOptions` overrides the gallery's initial playback options for that
item. Supply provider occurrence IDs rather than display names; repeated URLs
can represent distinct attachments. The SDK copies the list on construction.

The host owns selection, previous/next buttons, LB/RB bindings and resource
replacement. Reordering or relabeling preserves a surviving selected ID. Removing
it chooses a bounded surviving position. These interactions do not require widget
action handlers, focus counters or a parallel selection model. Single-source
players retain their existing bumper controls. Gallery selection is scoped to
the native media owner and never authorizes an undeclared URL or capture.

A gallery still counts as **one** native player in a rich preview. Only the
selected member is resolved, decoded or given a playback lease. Source changes
cancel obsolete loads and release old playback custody; the broker checks the
complete current declaration and requested item key, including indexed previews
and pins. Images and video use the same fullscreen owner when changing items.

The fitted gallery shows a compact position counter with previous/next controls;
the selected item's label remains available to accessibility and pointer tooltip.
Image zoom/fit controls occupy a stable footer outside the picture. Fullscreen
floats the available navigation chevrons at the left and right midpoints over
the aspect-fit picture. They reserve no header space. The viewer uses the existing transient
playback controls. A widget can use `WidgetModal.ShowHeader=false` when the media
already supplies navigation, avoiding a second title bar while preserving the
modal's accessible name and dismissal contract.

For a larger attachment viewer, set `WidgetModal.OverlayPresentation = new()`
(protocol 135). The same gallery is centered against the overlay, independent of
the parent widget, with a maximum 1280-DIP width and 16:9 viewer area by default.
The host proportionally fits shorter displays and preserves an outside-click
margin. Attachment aspect ratios remain unchanged. The modal gains the
`wrail-modal-overlay` class, allowing a transparent, borderless, zero-padding
frame through WRSS. Its dimmed input-blocking backdrop is separate; Escape/B,
focus return, widget lifetime and media-source authorization remain unchanged.
Ordinary dialogs remain widget-local unless explicitly opted in. Overlay placement
cannot be combined with anchored/popover placement.

## Animated images

Native GIF images retain animation through the shared artwork pipeline and
`WebImage` viewer. Artwork pauses when offscreen, hidden, cached or retired;
reentering the viewport reuses the decoded image. Reduced motion disables
automatic animation. The image viewer exposes Play/Pause for an animated image,
including X and keyboard Space; explicit Play works with reduced motion enabled.
Its `AutoPlay` option controls initial viewer playback, and changing the motion
preference does not override a user's explicit pause. Static images have no
animation playback controller. Artwork elements retain a lightweight native
viewport observation from creation, so a late-loading GIF or replacement source
can use current visibility without waiting for the user to scroll. The observation
owns no decoded image and is collected with its element; widgets need no timers
or manual visibility notifications.

The existing encoded-size and source-pixel limits also apply to GIFs. The host
checks their logical canvas and every image descriptor before native pixel
decoding, including later delta frames. Decode sizing uses the logical canvas,
not a potentially smaller first frame. There is no arbitrary frame-count limit:
the native decoder retains composition/next-frame state rather than a decoded
full-size image for every frame. These limits are admission bounds, not a promise
about total process or GPU memory use.

`WithPoster(WidgetArtworkHandle)` (protocol 116) supplies optional loading artwork
through the existing scoped artwork resolver, decode sizing and lifetime. It uses
the native `MediaPlayerElement.PosterSource` with aspect-preserving fit. A changed
poster identity clears the prior image; late artwork cannot outlive its row or
preview authority. Other media declarations need no changes. The platform controls
the loading transition; its documented boundary is media opening, so this API does
not promise a separately observed first decoded frame. No frame-server rendering
or extra media decoder is introduced.

## Inline frame styling

The existing WRSS `padding`, `border-width` (including individual edges),
`border-color`, `corner-radius`, and `background` properties style the media
frame itself. For a player inside an already-framed preview, use
`padding: 0px; border-width: 0px; corner-radius: 0px; background: transparent;`
to avoid another inset or card. Without these declarations, the host retains its
8px padding, 1px border and theme surface. Floating transport controls keep their
accessible theme backing independently of the frame background.

Fullscreen uses its existing edge-to-edge layout. Returning restores the latest
inline styles; removing a style restores the host defaults. This does not create
a second player or alter media authority, controls, or input routing.

## Media in rich previews

An interactive `FocusPresentationSurface` can opt into one native media element
with `FocusPreviewOptions.AllowMedia = true` and an explicit `EnterButton`
(protocol 116). Existing previews remain unchanged without this option. Indexed
previews use the existing exact row/preview lease proof; no widget-managed open
request or hover timer is required. Inline and deferred preview declarations use
the same resolver and native player.

Set `FocusPreviewOptions.Placement` to `AnchorPlacement.CenterOnAnchor` (protocol
123) to overlay the source card instead of placing a second view beside it. The
host centers over the visible anchor and clamps the panel and its shadow inside
the widget viewport. The same placement works for `AnchoredPresentation`.
Existing directional/default placement, entry, hover and outside-click rules
remain unchanged; missing or fully offscreen anchors retain the bounded fallback.

Rich previews enter once per visible frame lifetime. Media-enabled previews use
a short fade without scaling their separate native video surface. The existing
poster can appear during entry; source authority resolves in parallel, while the
existing presentation-ready gate defers decoder creation until entry settles.
Reduced motion presents immediately. Updating content or entering interaction
does not restart entry, and closing retains immediate authority/lease teardown.

Passive preview playback is always muted and does not register system media
controls. Entering the preview exposes the normal playback controls; leaving
interaction restores mute. Reopening cannot restore an unmuted playback-memory
value. B/Escape finishes volume adjustment or collapses expanded media before
returning to the preview source. Expansion keeps the same player and position.
Withdrawal, hiding and disposal stop playback and release its host-owned custody,
including a pending open that completes after cancellation.

Capture Library opts available clips into this presentation after a short hover
or focus delay, with R3/F6 to interact. Its **Play clip previews** preference can
disable it; batch selection also suppresses previews. The grid retains its row
geometry. A query refresh, including a favorite/title update, can retire the
preview and restart it: the host does not reuse an old indexed proof merely
because the capture's content revision is unchanged.

## Shared controls

The timeline supports pointer scrubbing and controller left/right adjustment.
Controls include play/pause, replay, loop, playback speed, mute, volume and expanded
view. Volume uses A to enter/finish adjustment; B exits adjustment. Native keyboard,
mouse and focus visuals are retained. Narrow layouts keep the essential controls;
Replay/Loop remain available through their shortcuts, and expansion exposes the
full toolbar.

- A activates the focused control; on the timeline it plays/pauses.
- LT/RT seek: half a second for short clips, five seconds for longer media.
- LB replays; RB toggles looping; R3 expands/collapses.
- B closes expanded view or volume adjustment, otherwise follows outer navigation.
- X and Y remain available to the widget inline, including Game Help's retry/send
  actions. In fullscreen, X directly toggles playback, matching embedded video.

The expanded view reuses the same player, position and native controls. It shares
the shell fullscreen layer, focus handoff and gesture ownership with embedded
media; it no longer opens a ContentDialog. Expansion/collapse retains the focused
control after layout settles, including repeated transitions. Hiding the
presentation pauses playback; passive visible presentations can keep playing.
Capture expiry/revocation clears already decoded content. Dispose/source replacement
cancels outstanding loads and releases native player/source objects.

For ordinary inline still images, LT/RT zoom and the right stick pans while the
image viewport is focused. The left stick and D-pad navigate regardless of zoom;
Down reaches the toolbar. LB fits the image, and RB switches between the image
and its toolbar. A activates the focused control, R3 expands/collapses, and B
closes expansion before returning to ordinary widget navigation. The image keeps
its aspect ratio; host-issued original-resolution captures retain their supplied
resolution for zooming. The focused canvas consumes right-stick input even at Fit,
so panning never also scrolls the surrounding widget;
mouse dragging and wheel zoom are also supported. Fit and a
zoom percentage are available in the toolbar. Expansion retains the viewed image
position and scale. Aspect-fit images are centered in their viewport, with a compact
centered toolbar that wraps for smaller surfaces.
Fit remains Fit when the viewport changes. Touch zoom/scroll and native keyboard navigation remain
available. All of this stays inside the host; it does not launch Windows Photos.

Fullscreen media fills the available overlay viewport with aspect-fit rendering.
Images, native video and embedded video share a GPU scale/position transition,
with fading controls and backdrop. The default duration is 200 ms, defined by
`MediaFullscreenLayer.DefaultTransitionDuration` in
`src/OverlayFrontend.WinUI/Media/MediaFullscreenLayer.Motion.cs`; the host's
`TransitionDuration` property permits a code-level override. Existing widget
animation speed applies, and reduced motion skips the animation. No SDK option
or widget-specific transition code is required. Normal collapse animates;
source retirement, overlay hiding and disposal close immediately. Focus and
interaction resume after the placement settles, with playback and image zoom
retained throughout.
For native video, the shared layer uses a WinUI `RenderTransform` storyboard so
XAML can synchronize the media surface with its container. Images and embedded
video retain their compositor animation. This renderer distinction is host-owned;
it does not enable frame-server playback, pause the video, capture screenshots,
or introduce a new widget-authoring option.
Transition endpoints match the fitted video rectangles, including letterbox
offsets, rather than the surrounding card or fullscreen window dimensions.
For galleries the inline frame, thumbnail strip and its scroll position stay
in place beneath the transitioning picture. Native video uses the same XAML
`MediaPlayerElement` and decoder at both destinations. Its containing Viewbox
fits the decoded dimensions to each viewport. Returning after browsing uses
the current member's aspect ratio and restores its thumbnail when entry came
from the strip. The standalone embedded browser player keeps its resident-stage
placement policy.
The separate pinned window still uses the existing asynchronous cross-root transfer.
A preview starts with its transport hidden. Autoplay, source readiness, updates
to the same presentation, and a pointer resting over the picture leave it hidden.
Moving focus from the thumbnail strip into the video reveals and focuses a
control in one move. Clicking the picture also reveals the controls. An inline
video's Play button starts playback on explicit activation. During
playback, video controls hide after three seconds of inactivity, including when
a button has focus or the pointer rests over it. An active adjustment or open
options menu keeps the controls visible. Waking a focused toolbar restores the
previous control and playback continues. After entry, pausing reveals the controls.

Inline carousel screenshots use the thumbnail strip for navigation and omit the
image toolbar. Opening a screenshot from the strip places focus on Fit. In
fullscreen the image stays outside the focus order. The image controls support
zoom and right-stick panning, and returning to the strip fits the image again. Inline
height remains authored through WRSS; viewport-relative height is available for
responsive review pages. No new SDK declaration or widget-owned fullscreen view
is required. The same behavior serves Capture Library, Game Help and other authors.
Expansion is available in shell-hosted widget presentations. A peer without a
shell fullscreen destination does not advertise an expansion shortcut; promoting
a pinned peer to another window is not part of this media placement contract.
Expansion has no bottom Back button: use B, keyboard Escape, or the toolbar's
Collapse control to return to the same inline view.

Inactive players release their decoder and retain lightweight position/volume/rate
state for a same-source reopen. The host bounds native video residency to four players; expansion does not consume
another slot. Additional players show a close-another-video message. Playback is
view-owned; a separate main/pinned projection resolves through the same source
authority but does not currently transfer its playback position between views.
