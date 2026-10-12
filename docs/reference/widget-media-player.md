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

// Full-trust widgets only: ordinary local drive paths, not device/UNC paths.
UI.MediaPlayer(MediaPlayerSource.LocalFile(@"C:\Videos\guide.mkv"), "local-video");
```

## Sources and authority

- Packaged assets are resolved inside the widget's sealed package inventory. The
  Bridge checks the file size/digest and rejects path traversal and reparse points.
- Direct HTTP(S) media URLs can be used by both sandboxed and full-trust widgets.
  The native Windows player determines available codecs and stream support.
- Capture references retain ownership, permission, expiry and revocation checks.
  Captured application metadata remains attached to the source.
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

For still images, LT/RT zoom and the right stick pans while the
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
Native and embedded video remain attached to a shared resident XAML media layer
across fullscreen entry and return. Each renderer's viewport stays at the layer's aspect-fit rendering
size; transforms place those pixels in the authored inline slot, following its
scrolling, rectangular clipping and ancestor opacity. This avoids the native
renderer unload/reconnect and resize that can briefly shrink or blank a frame.
The separate pinned window still uses the existing asynchronous cross-root transfer.
A floating toolbar overlays the media, with safe margins for focus outlines;
there is no dialog title, fixed 1280-by-900 cap, or nested card border. Video controls
auto-hide while playing, park focus on the media, and reveal without executing a
hidden action. Pausing reveals controls. Image controls remain visible. Inline
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
