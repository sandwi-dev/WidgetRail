# Capture Library

Capture Library is included in both installer editions. It keeps screenshots and
configurable clips only when you explicitly save them. There is no automatic game
recording, upload, or eviction of saved media.

## Controller flow

- **Y / Take screenshot** uses the saved image settings (original resolution by default).
- **X / Record clip** uses the saved recording settings. On first use, prefer
  qualified hardware 1080p30, otherwise another supported profile; default duration
  is five seconds and audio is off.
- **Capture settings** groups screenshot resolution/format, recording quality,
  bitrate, duration, stop behavior, audio, preparation delay/countdown and cursor.
  Settings persist privately across restarts. Unavailable saved settings remain
  intact and fail with an explanation; recording never silently downgrades.
- Confirm the host dialog, let WidgetRail hide, and bring your application forward.
  The configured countdown precedes capture (five seconds by default). Reopen the
  overlay to finish a recording early and review it. Reopening during preparation cancels.
- Preview uses the shared native media player. Choose **Save to library** or
  **Discard**. Leaving an unsaved capture does not retain it across worker restarts.
- Browse a responsive thumbnail grid by application, date and media type, with 20 items per page.
  Open an item to preview, export, or delete. Delete starts on Cancel and affects
  only the library original; exported files remain. Missing/damaged entries stay
  visible and can be deleted even when preview fails.
- Exports go to **Pictures → WidgetRail → Captures** with unique PNG/MP4 filenames.
  No native file picker is required. Busy buttons retain their focus identity.

Game Help's **Add context → Choose saved capture** and its in-chat context choices
open the same library. Selection creates a temporary context copy and opens the
existing review/question editor. Only **Send to Gemini** sends it to the provider.
Optional library read access is separate from permission to capture a new window.

## Resolution and resource limits

| Media | Stored format | Limits |
|---|---|---|
| Full-resolution screenshot | Lossless PNG, exact window pixel dimensions including odd sizes | 33,554,432 pixels; 16,384 per dimension; 128 MiB encoded |
| Context screenshot | PNG, no upscaling | At most 1920×1080 and 8 MiB |
| Library clip | H.264 MP4, currently no audio; optional cursor | Runtime-qualified profiles up to 4K30 or 1080p60, up to 120 seconds and 128 MiB; bitrate/duration must fit the byte budget |
| Game Help clip | H.264 MP4, silent | Five seconds, 1280×720, 15 fps, target 3 Mbps, 8 MiB |

Larger original screenshots fail explicitly instead of silently shrinking. The
limit includes 7680×4320 UHD 8K and suitable ultrawide windows. This remains a
window capture: it is not a full-desktop image and does not bypass protected
content. Target changes/loss, resize, cancellation, or reopening the overlay
still terminate a pending recording rather than substituting another window.

The library allows **200 items / 2 GiB**; it asks you to delete or export when full.
Original files remain unchanged when a consumer requests a context-sized rendition.
Game Help requests that smaller rendition to fit its existing API payload budget.
The shared Windows codec adapter contains no Gemini-specific logic.

Temporary attachments remain separate: four per widget, sixteen total, 256 MiB
aggregate encoded-media budget, thirty-minute expiry. The per-attachment image
ceiling is 128 MiB; Game Help and legacy context requests retain their 8 MiB limit. A saved
item survives worker retirement and permission revocation; open temporary copies
are retired with the owning worker or the authority that allowed the read.

## SDK and permissions

| Permission | Operations |
|---|---|
| `system.apps.windows.capture.v1` | Existing host-confirmed capture; original-resolution screenshot request is explicit |
| `media.capture-library.read.v1` | List/open saved captures, read/release temporary library references, obtain image thumbnails, including while background |
| `media.capture-library.save.v1` | Explicit interactive save of this widget's owned temporary media |
| `media.capture-library.manage.v1` | Explicit interactive deletion and export to the fixed Pictures destination |

Read access covers the user's shared saved library, including other widgets' saved
captures. It grants no authority to initiate screen capture. Save/manage operations
are not dashboard shortcuts and require interactive lifecycle. Both sandboxed and
full-trust widgets use the same service and checks.

```csharp
// Existing RequestAsync(kind, token) keeps context resolution for compatibility.
var ticket = await HostServices.Capture.RequestScreenshotAsync(
    new ScreenshotOptions(), cancellationToken: token);
var status = await HostServices.Capture.GetStatusAsync(ticket, token);
// After Ready and an explicit user Save:
var item = await HostServices.CaptureLibrary.SaveAsync(status.Attachment!, token);
var page = await HostServices.CaptureLibrary.ListAsync(offset: 0, token);
// Resolve lazily from Widget.OnResolveArtworkAsync for a UI.Artwork handle:
var thumbnail = await HostServices.CaptureLibrary.GetThumbnailAsync(item.Id, token);
var artwork = new WidgetEncodedArtwork(WidgetArtworkContentType.Png, thumbnail.Bytes);
var temporary = await HostServices.CaptureLibrary.OpenAsync(
    item.Id, CaptureImageResolution.Context, token);
var preview = UI.MediaPlayer(MediaPlayerSource.FromCapture(temporary),
    "preview", "Saved capture", new(Muted: true));
// These generic methods select the capture/library permission from the issued origin:
var bytes = await HostServices.Capture.ReadContentAsync(temporary, token);
await HostServices.Capture.DiscardAsync(temporary, token);
// The original remains until explicitly deleted:
await HostServices.CaptureLibrary.DeleteAsync(item.Id, token);
```

Never pass `CaptureLibraryItem.Media` directly to the player or read API: it is
stored metadata, not an issued temporary reference. Call `OpenAsync` first.
`CaptureAttachment.Origin` describes which service issued a reference; it is not
permission and changing it cannot grant access. Protocol **77** is required to
render library-origin or larger-than-context captures. Old capture request and
constructor signatures remain available; original requests use a distinct
`capture.request-original` operation so an older host cannot silently downgrade it.
Configured captures additionally require protocol **79** for their output metadata;
see [configurable capture](widget-capture-and-game-help.md#capture). Saved originals
retain this metadata, including source pixels and recording encoder/frame counts.
The production Capture Library widget and Game Help are working author examples.

## Storage and failure behavior

Profile storage is `%LOCALAPPDATA%\WidgetRail\capture-library` (or the selected
host profile). Metadata contains IDs, dimensions, type, application/window display
context, save time and SHA256; widgets never receive local storage paths.

Media is copied under a held read lease, then metadata is atomically published.
Identical saved content/metadata deduplicates, making repeated Save safe after a
lost acknowledgement. Open/export recheck integrity. Failed saves clean their own
partial output; later operations clean recognized orphan copies and finish only
previously requested deletions. Linked storage/export paths are refused. Export
publishes a new file only after its complete copy succeeds.

Thumbnails are host-derived PNGs bounded to 256×144 and 160 KiB, cached alongside
the original and deleted with it. Up to 200 derivatives add at most 31.25 MiB
outside the 2 GiB original-media quota; there is no automatic original eviction.
The SDK reads each thumbnail in bounded chunks under library-read authority.
It consumes no temporary capture slot. The grid uses lazy artwork handles, never
original media or inline image blobs in widget snapshots. Invalid cached derivatives
are regenerated; videos currently use a labeled placeholder.

A temporary copy is scoped to its requesting broker/worker. Library-read revocation
cannot be bypassed by capture permission, and permission to preview windows cannot
read pixels. Missing codec, expired references, missing files, full storage and
permission failures are reported; the library does not silently discard saved data.

## Capture cost assessment

Full-resolution screenshots trade a short allocation/encoding burst for fidelity.
One BGRA pixel buffer is approximately **31.6 MiB at 4K** and **126.6 MiB at 8K**.
The native path also owns GPU capture/output/staging resources, so these values
are **not total process memory**. PNG size depends strongly on image content and
cannot be estimated reliably from an average screenshot. Disk use is shown in MiB.

The shared API now supports qualified hardware encoding profiles. Raw pixel throughput rises
from **13.8 million pixels/s** at 720p15 to **62.2 million** at 1080p30 (4.5×), or
**248.8 million** at 4K30 (18×). Hardware encoding keeps these pixels on the GPU
and bounds its NV12 queue to three tracked surfaces. Software encoding retains a
readback path. At a 3 Mbps target, five seconds is approximately 1.8 MiB before
container/VBR variation. Available profiles come from runtime encoder probes;
choosing a longer duration/higher bitrate must also fit the requested byte budget.
The settings page shows estimated clip size and rejects combinations that exceed
the attachment budget before capture. Only runtime-advertised audio modes appear;
currently that is **No audio**. See `docs/platform-feature-improvements.md` for
remaining work and physical acceptance.

Synthetic animated 4K-window validation delivered all requested output slots for
hardware 720p15, 1080p30, 1080p60 and 4K30. Encoder-only tests also decoded every
generated frame and checked dimensions, colors, ordered timestamps and no audio.
This establishes functional support and local collection cost, not in-game GPU/FPS
impact. Do not extrapolate these generated-window measurements to loaded games.

Automated checks cover native geometry, a real Windows-codec odd-sized 4K rendition,
original preservation, storage lifecycle, quotas, corruption, linked paths,
permission/ownership, controller presentation and Game Help's explicit-send boundary.
Physical capture in actual games, HDR color fidelity (the current pipeline remains 8-bit BGRA), protected content, memory/encoding behavior
under game load, preview controls/scaling and export locations remain acceptance
checks. No private desktop/game screenshot was taken for these automated tests.

For widget authors, ReadContentAsync allocates the encoded attachment size (up to 128 MiB for an original image), so size the worker budget appropriately. The library's own preview/export flows do not materialize the original in widget memory; Game Help reads only its bounded context copy.

A missing metadata file is not treated as proof of an unfinished save: its original remains as an unavailable entry until you explicitly delete it. Automatic partial-copy cleanup requires a transaction marker written before copying media.

LB/RB change pages in Capture Library and Game Help's saved-capture picker. Page changes explicitly focus and reveal the first new row; the footer also shows the page number.
