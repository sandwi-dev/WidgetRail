# Capture Library

Capture Library is preinstalled in Production and available through Catalogue in
Bare. It keeps screenshots and
configurable clips only when you explicitly save them. There is no automatic game
recording, upload, or eviction of saved media.

Recording checks only the saved video profile and its requested audio mode. With
no saved profile, it selects the first verified recommendation (1080p/30 hardware
preferred, with supported fallbacks). Opening Settings discovers the full catalog.
Both paths share encoder results for the overlay process lifetime; recording does
not need to initialize every quality/audio combination before confirmation.

## Controller flow

- **Y / Take screenshot** uses the saved image settings (original resolution by default).
- **X / Record clip** uses the saved recording settings. On first use, prefer
  qualified hardware 1080p30, otherwise another supported profile; default duration
  is five seconds and audio is off.
- **Capture settings** groups screenshot resolution/format, recording quality,
  bitrate, maximum duration, audio, preparation delay/countdown and cursor.
  Settings persist privately across restarts. Unavailable saved settings remain
  intact and fail with an explanation; recording never silently downgrades.
- Confirm the host dialog, let WidgetRail hide, and bring your application forward.
  The configured countdown precedes capture (five seconds by default). Reopen the
  overlay to finish a recording early and review it. Reopening during preparation cancels.
- Preview uses the shared native media player and grows with the available viewport.
  Images support controller zoom/pan and an expanded viewer. Choose **Save to library** or
  **Discard**. Save writes the original directly to **Pictures → WidgetRail → Captures**.
  Leaving an unsaved capture does not retain it across worker restarts.
- Browse a virtualized thumbnail grid with search, application/type/date filters,
  favorites, sorting and date groups. Titles and favorites do not alter originals.
  Selected captures can be updated together, copied to a chosen folder, or removed.
  Removal starts on Cancel: owned files go to the Recycle Bin; linked provider
  files remain untouched; unavailable entries lose only their stale metadata.
- **Open folder** opens the saved location in Windows Explorer through the host's
  normal release-aware foreground handoff. No separate export copy or file picker
  is required. Busy buttons retain their focus identity.

Game Help's **Add context → Choose saved capture** and its in-chat context choices
open the same library. Selection opens the existing review/question editor;
images use a bounded context rendition and videos retain a read-only original.
Videos exceeding the 8 MiB Game Help payload limit remain visible but unavailable
for context. Only **Send to Gemini** sends it to the provider.
Optional library read access is separate from permission to capture a new window.

## Resolution and resource limits

| Media | Stored format | Limits |
|---|---|---|
| Full-resolution screenshot | Lossless PNG, exact window pixel dimensions including odd sizes | 33,554,432 pixels; 16,384 per dimension; 128 MiB encoded |
| Context screenshot | PNG, no upscaling | At most 1920×1080 and 8 MiB |
| Newly recorded library clip | H.264 MP4, optional application AAC audio and cursor | Runtime-qualified profiles up to 4K30 or 1080p60, up to 120 seconds and 128 MiB; combined video/audio bitrate and duration must fit the byte budget |
| Game Help clip | H.264 MP4, silent | Five seconds, 1280×720, 15 fps, target 3 Mbps, 8 MiB |
| Existing linked video | Windows-decodable MP4 | Up to 8 hours and 64 GiB; these playback/import limits do not enlarge recording budgets |

Larger original screenshots fail explicitly instead of silently shrinking. The
limit includes 7680×4320 UHD 8K and suitable ultrawide windows. This remains a
window capture: it is not a full-desktop image and does not bypass protected
content. Target changes/loss, resize, cancellation, or reopening the overlay
still terminate a pending recording rather than substituting another window.

Saved captures have no fixed item-count or aggregate-byte quota. Saves check free
disk space and report write failures without evicting existing files. Total capture
count and size appear in Capture settings, not above the gallery. Per-capture and
temporary-attachment bounds remain separate from saved storage.
Original files remain unchanged when a consumer requests a context-sized rendition.
Game Help requests that smaller rendition to fit its existing API payload budget.
The shared Windows codec adapter contains no Gemini-specific logic.

Temporary attachments remain separate: four per widget, sixteen total, 256 MiB
aggregate encoded-media budget, thirty-minute expiry. The per-attachment image
ceiling is 128 MiB; Game Help and legacy context requests retain their 8 MiB limit. A saved
item survives worker retirement and permission revocation; open temporary copies
are retired with the owning worker or the authority that allowed the read.

Saved-video playback keeps a verified read-only original handle and directory
chain instead of copying the whole recording to temporary storage. These leases
use the existing bounded per-widget/global handle counts, without charging the
original's bytes to temporary storage. A two-minute idle deadline is renewed by
the native player's existing authority checks or bounded content reads. A stopped
widget, revoked read permission, disconnected source, removed entry, explicit
release, or idle expiry releases the lease. Originals are never deleted by lease
cleanup. Linked file identity includes Windows file ID and change time, so edits
that preserve length and modification time do not bypass identity checks.

`CopyContentToAsync` continues to stream in 32 KiB chunks. The convenience
`ReadContentAsync` rejects media above 128 MiB before allocation; use bounded
streaming for larger videos. Native playback does not route original video bytes
through widget IPC. The attachment's initial expiry remains a conservative
reopening hint; the native player uses the renewable host lease deadline.


## SDK and permissions

| Permission | Operations |
|---|---|
| `system.apps.windows.capture.v1` | Existing host-confirmed capture; original-resolution screenshot request is explicit |
| `media.capture-library.read.v1` | List/query/group saved captures, receive change invalidations, open/read/release temporary library references, obtain image/video thumbnails |
| `media.capture-library.save.v1` | Explicit interactive save of this widget's owned temporary media |
| `media.capture-library.manage.v1` | Explicit interactive metadata edits, bounded batches, recycling/deletion, and opening/revealing captures in the fixed Pictures folder |

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

Profile metadata, the SQLite index and derived caches are under
`%LOCALAPPDATA%\WidgetRail\capture-library-v2` (or the selected host profile).
Original PNG/MP4 files are in Pictures → WidgetRail → Captures. Metadata contains
IDs, dimensions, type, application/window display context, save time, filename,
content revision and SHA256; widgets never receive arbitrary local storage paths.

Media is copied under a held read lease, then metadata is atomically published.
Identical saved content/metadata deduplicates, making repeated Save safe after a
lost acknowledgement. Open rechecks integrity, caching successful verification
against the file's length, timestamp and expected hash. Failed saves clean their own
partial output; later operations clean recognized orphan copies and finish only
previously requested deletions. Linked storage/media paths are refused. The index
is disposable: a corrupt index is preserved for diagnostics and rebuilt from
metadata without modifying saved originals.

Thumbnails are host-derived PNGs bounded to 256×144 and 160 KiB, kept in the private
cache and deleted with their capture. There is no automatic original eviction.
The SDK reads each thumbnail in bounded chunks under library-read authority.
It consumes no temporary capture slot. The grid uses lazy artwork handles, never
original media or inline image blobs in widget snapshots. Invalid cached derivatives
are regenerated. Video frames come from Windows' thumbnail service and pass through
the same bounded PNG encoding/cache path. Unsupported or unavailable thumbnail
providers do not prevent opening the original clip. The original is never rewritten.

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
these are **No audio** and, after successful process-loopback/AAC probes,
**Application audio**. Application audio captures the app and its child processes;
it includes no microphone or other applications. It uses 48 kHz stereo AAC at
128 kbps, adding about 16 KB/s before container overhead. Audio stays off by
default, and its saved preference is retained if temporarily unsupported. Review
shows actual frame rate, encoder and audio mode, and reports missed frames. Clips
with audio start unmuted when the user plays their preview; they do not autoplay.
Use the [release checklist](../maintainers/release-checklist.md) to qualify capture
against the exact release, hardware and audio mode being shipped.

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

The gallery uses a persistent SQLite index with an ordered page query and cached
count/byte totals. File notifications update individual entries; startup, explicit
Refresh and watcher overflow reconcile the folder. List requests do not decode or
hash every capture. Thumbnails are generated lazily and cached; content revisions
invalidate artwork after edits. Recognized renames while the host is running are
followed, and missing files become unavailable entries. Externally edited media
is re-inspected when preview/artwork is requested, within the same media bounds.

The new index, metadata and thumbnail cache live in the profile's
`capture-library-v2` directory. There is no migration of the old private library:
old files remain untouched, and new captures use the public folder. The gallery
indexes explicitly saved captures, not arbitrary files copied into Pictures.
The older SDK `ExportAsync` entry point returns the existing public filename for
compatibility; it no longer creates another copy. New code should use
`OpenFolderAsync`. `RefreshAsync` explicitly reconciles external changes;
ordinary `ListAsync` reads indexed pages. Include each item's `Revision` in artwork
handles so a changed file cannot keep showing cached pixels.

LB/RB change pages in Capture Library and Game Help's saved-capture picker. Page changes explicitly focus and reveal the first new row; the footer also shows the page number.

Opening a saved capture or Capture settings uses the host's page-navigation
transition; Back reverses it and restores the library's selected item and scroll
position. Loading the capture does not start another page transition. Native
video remains attached while its page moves and is released when the outgoing
transition ends. Choose the effect in Settings → Appearance → Page animation.

## Indexed discovery and organization

`QueryAsync` searches metadata across the whole library, not just a displayed
page. `CaptureLibraryQuery` selects text, application, screenshots/clips, favorites,
inclusive saved-date bounds, and Newest/Oldest/Title/Application/Largest/Longest
ordering. Text search matches custom title, original filename, and source application;
it does not inspect image/video contents. A response contains at most 64 items.
`ListAsync` retains its original 20-item newest-first compatibility contract.
Legacy list/save responses retain their original closed item shape for older clients;
use `GetMetadataAsync(id)` after saving when current title/favorite metadata is needed.

The first query returns an opaque revision. Pass that revision to subsequent ranges
or `FindAsync` anchor lookup. A changed/rebuilt index returns
`capture_library_query_changed` instead of mixing two different orderings. Start a
fresh query and remap the capture ID rather than blindly continuing the old offset.
Use the existing SDK indexed collection and row-artwork resolver for virtualized
presentation; the service does not dictate gallery UI.

`ApplicationsAsync` returns up to 128 application/count facets plus `HasMore`, with
an optional application-name search. It honors the query's other filters but ignores
its selected application. Empty application name is the explicit unknown-source
bucket; null `CaptureLibraryQuery.Application` means all applications.
`GroupsAsync` returns the complete month partition for Newest/Oldest ordering using
the host's local calendar. Its at most 256 keys are `yyyy-MM`, `unknown` for a date
outside the platform conversion range, and a final `older`/`newer` remainder when
needed. The widget provides localized headers; group counts sum to the query total.

For live reads, await `OpenChangesAsync` before the initial query, then consume
`ReadAllAsync`. Events are bounded/coalesced invalidations, not full snapshots.
Their generation is notification-local; only query revisions pin ordering. Dispose
the subscription when its view/lifecycle no longer needs updates. Game Help uses
these same query and event APIs for search, type, and favorites in its context
picker; it does not gain management authority or send a question on selection.

Titles/favorites live in the library metadata sidecar, not inside originals.
`MetadataRevision` is distinct from the content `Revision` used for artwork.
`UpdateMetadataAsync` requires the last metadata revision; conflicting edits return
`capture_library_metadata_conflict`. `UpdateMetadataBatchAsync` and `RecycleAsync`
accept at most 64 distinct references and return an outcome for every requested
item. Successful earlier items are not rolled back when a later item fails.
Cancellation or a lost transport reply can occur after an external effect; refresh
before retrying rather than assuming nothing happened.

`RecycleAsync` requests recoverable removal only. Unsupported recycle destinations
fail without falling back to permanent deletion. Windows recycling uses the
internal `ITransferSource.RecycleItem` primitive. Recovery markers retain title and
favorite metadata; restoring the original filename from the Recycle Bin allows it
to rejoin the library. `RemoveUnavailableAsync` removes only stale library metadata when the referenced
original is still absent; a restored file is rejected and never permanently deleted.
`DeleteAsync` remains the explicit permanent-delete API for older consumers. `RevealAsync` accepts a capture ID and requests an owned Explorer
handoff; author-supplied paths are never accepted.

## Saving copies outside the library

`HostServices.CaptureLibrary.CopyAsync` accepts up to 256 capture mutation references
(the capture ID, content revision, and metadata revision). One host-owned Windows
folder picker supplies the destination; widget requests never include paths. The
manage permission and interactive admission are required. The admitted operation
can finish while the native picker owns focus, but permission revocation and worker
retirement cancel it.

Each original is verified and held for reading while it is copied. Copies are staged
in the chosen folder and published without replacing existing names. A collision
returns `capture_library_copy_exists` for that item; other items can still succeed.
Stale references return `capture_library_metadata_conflict`. A cancelled or failed
copy removes its own temporary output and leaves original media untouched. Completed
copies remain when later items fail. The result preserves requested item order and
reports each outcome; transport cancellation can prevent delivery of that final
report, so cancellation does not imply that no files were copied.

The destination picker has a two-minute budget and copying a ten-minute budget.
The existing `ExportAsync` compatibility API still returns original filename metadata;
it does not show a picker or create a copy.

## Capture context and related items

`SaveWithContextAsync` adds bounded domain references to a saved capture without
changing the original file. Each request carries a caller idempotency key and one
to eight references: an opaque kind and ID, optional display label, and generic
`Subject`, `Event`, or `Related` role. The host stamps the authenticated package,
publisher, and widget instance; the caller cannot supply an origin identity.
Multiple events may refer to the same deduplicated original. Association records
remain separate from the media bytes and survive rebuilding the disposable index.

An idempotency key identifies one media artifact, rather than an entire event
that may produce several screenshots or clips. Keys are namespaced by the
host-stamped origin. Retrying the same references and event time returns the
existing receipt; changing their identity returns
`capture_library_context_conflict`. Display-label changes and reference ordering
do not create new captures. When the original has been removed, retry returns
`Unavailable` and does not recreate the user's deleted media.

`GetContextsAsync` reads at most sixteen associations per page. Query revisions
protect paging; `CaptureLibraryItem.ContextCount` lets callers defer hydration
until details are requested. Query filters can match an origin package and an
opaque reference; both must match the same association. Reference filters require
an origin package. Free-text search also considers reference labels.

Association event time, actual capture time, and host receipt time are separate.
The actual capture time is nullable and is supplied only by a trusted host capture
path with that evidence. A provider event, import, file modification time, or save
completion must not be presented as an exact capture timestamp. Existing captures
without this evidence remain unknown.

The SDK's standard capture-library intent opens a scoped related-capture query.
References are display-only by default. Set `HasRelatedAction` explicitly only
when the integration implements the standard related-item action; this is a UI
hint and never grants execution authority. Metadata-only helpers need no widget UI.
The related-item intent carries the originating package and opaque kind/ID back
to a compatible widget. Receivers use existing dynamic intent availability to
reject unsupported identities before the host presents a chooser, and validate
again on delivery. These intents add no file-access or provider authority.

## Linked capture folders

`ChooseSourceAsync(label)` opens the host folder picker. The result is a durable,
read-only source grant owned by the authenticated package, publisher and instance.
`CanonicalFolderPath` is disclosed to that owner so it can configure its provider;
it is not authority for later file access. Use `SourceId` and a validated relative
path with `LinkAsync`. Absolute paths, parent traversal, reparse points and
 overlapping grants are rejected. The library's own media and metadata folders
cannot be registered as sources.

`GetSourcesAsync` lists only the caller's grants. It reports availability, linked
count and unsupported files discovered by the bounded scan. Linking the same exact
folder again for the same owner returns its existing grant; overlapping folders
from different owners fail with `capture_library_source_overlap`.

The broker starts discovery for persisted sources independently of widget
residency. It reconciles at startup, watches subsequent changes, and recovers
missed changes after watcher overflow. Work is bounded (16 sources, 16 directory
levels, 100,000 scanned files per source, 1,024 pending paths, 32 queued paths per
pass). It never scans an entire source during each gallery query. Files still
being written are retried. Unsupported media stays in its original folder.

A linked item's `SourceId` is non-null. Query, thumbnail, preview, favorite,
metadata, reveal and copy operations work on linked items. Delete/recycle reject
them with `capture_library_read_only_source`. `RemoveLinkAsync` removes only its
index/derived thumbnail and persists a suppression marker so discovery does not
immediately add it again. `RemoveSourceAsync` removes a source's linked index
entries and stops its watcher. Neither operation removes provider media or its
metadata companions. Older list clients see only owned library items.

Providers can optionally publish a generic companion next to each media file:
`example.png.widgetrail-context.json` or `example.mp4.widgetrail-context.json`.
This is an optional interchange route, not a prerequisite for ordinary folder
linking or SDK integrations that supply context directly through `LinkAsync`.
Write it atomically. Its closed schema is `CaptureSaveContext` using camel-case
JSON property names, at most 8,192 bytes on disk and the normal context limits
(4,096 serialized bytes and eight references). The host never parses provider
filenames to infer game or achievement identity. Context labels and event times
remain untrusted author claims; origin is stamped from the source grant, and
unknown actual capture time remains null. Give each physical variant its own
stable idempotency key. A duplicate key cannot silently move its association to
a different file. The companion allows context updates while the widget is not
loaded, without a second copy of the image or recording.
On Windows, set the Hidden attribute on a new temporary companion before its
atomic publication so metadata does not clutter ordinary capture-folder views.
When replacing an existing companion, preserve its current attributes, including
a user's deliberate choice to unhide it. Never change the media original's
attributes. The library reads companions explicitly, including hidden files.
After disconnecting and explicitly linking the folder again, the host creates a
new grant and rediscovers its files, including previously suppressed entries.
Library-only titles and favorites from the disconnected grant are removed.
Source context keys are namespaced by that grant, so an explicit reconnect may
attach its companions again without replaying an old `SaveWithContextAsync`
receipt. `FileModifiedAtUnixMilliseconds` is the external file's modification
time; `SavedAtUnixMilliseconds` is when this grant discovered it. Gallery ordering
uses the former when available. Neither field asserts when the image was captured.

### Compatibility and owner lifecycle

Extended saved-video scene metadata requires presentation protocol 114. This
includes library videos without capture encoder telemetry that exceed the older
5-second / 720p / 8-MiB envelope, and recordings above the older 120-second or
128-MiB limits. Existing compatible media keeps its earlier minimum version.
Native recording requests keep their separate capture budgets. The legacy list
endpoint omits linked items and owned videos outside its original validation
envelope; the new query endpoint includes them. A legacy save projection cannot
return new-only metadata to an older client; it reports
`capture_library_client_upgrade_required` instead.

Reviewed package updates preserve folder access for the exact verified successor
identity. They do not share grants with every instance of the same package or
publisher. The grant's original provenance remains unchanged. Permanent uninstall
records exact retired installation identities and removes only their access;
uninstalling an older copy therefore preserves an approved successor. When the
last owner is uninstalled, the host revokes playback and finishes index/watcher
cleanup in the background. Reinstallation requires a fresh folder selection.
Ordinary widget unload, disabling, missing partial catalog entries and window
closure do not infer permanent removal.

`SaveAsync` uses the additive `capture-library.save-current` operation and returns
current metadata, including an existing deduplicated recording whose decoder
facts have no capture encoder telemetry. It falls back to the closed legacy save
only when the broker explicitly rejects the modern operation as unsupported,
before any mutation. Permission failures, cancellation, unavailable authenticated
channels, transport errors and missing test handlers never trigger a second save.
Both operations retain the same interactive save permission and lifecycle rules.
