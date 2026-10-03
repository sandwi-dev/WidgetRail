# Native media player

`UI.MediaPlayer` (protocol 75) is the shared inline WinUI media element. Game Help
uses it for capture review; captures do not have a separate playback renderer.
The legacy `UI.CapturedMedia` declaration remains compatible and maps to this same
renderer. Captured still images are displayed without video controls.

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
- X and Y remain available to the widget, including Game Help's retry/send actions.

The expanded view reuses the same player, position and native controls. Hiding the
presentation pauses playback; passive visible presentations can keep playing.
Capture expiry/revocation clears already decoded content. Dispose/source replacement
cancels outstanding loads and releases native player/source objects.

Inactive players release their decoder and retain lightweight position/volume/rate
state for a same-source reopen. The host bounds native video residency to four players; expansion does not consume
another slot. Additional players show a close-another-video message. Playback is
view-owned; a separate main/pinned projection resolves through the same source
authority but does not currently transfer its playback position between views.
