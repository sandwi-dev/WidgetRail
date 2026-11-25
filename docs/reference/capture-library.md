# Capture Library

Capture Library is preinstalled in Production and available through Catalogue in
Bare. It automatically saves screenshots and clips taken with its capture controls. Optional replay recording
keeps a bounded recent buffer after you explicitly start it. Saving a replay
creates a library clip; neither browsing nor recording uploads or evicts saved media.

Recording checks the saved video constraints and requested audio mode. With
no saved profile, it uses the qualified Auto policy. Existing explicit settings
remain explicit. Opening Settings discovers supported choices.
Both paths share encoder results for the overlay process lifetime; recording does
not need to initialize every quality/audio combination before confirmation.

## Controller flow

- **Y / Take screenshot** uses the saved image settings (original resolution by default).
- **Menu / Options** opens the gallery toolbar menu from a capture. Close it with **B** to
  return to the card. Right-click a card to favorite it, or use Favorite in its details.
- **X / Record clip** uses the saved recording settings and shared library-output
  mode. It is independent of continuous replay's Enabled switch. One-off clips
  retain Windows' yellow capture border.
- On the main library, **RB / Save replay** saves the current buffer when ready;
  **LB / Start recording** or **Stop recording** controls continuous replay.
  These shortcuts are inactive during selection. Capture details keep LB/RB for
  previous/next capture, and dialogs and media controls retain their own input.
- In **Assign quick action**, choose a direction and expand **Capture Library**. Its five actions are **Take screenshot**, **Record clip**, **Start replay**, **Save replay** and **Stop replay**. Pressing an assigned direction can wake the widget in the background. When access is needed, the tray offers to open the widget.
- **Capture settings** groups screenshot resolution/format, recording quality,
  bitrate, maximum duration, audio, preparation delay/countdown and cursor.
  Settings persist privately across restarts. Unavailable saved settings remain
  intact and fail with an explanation; recording never silently downgrades.
- Screenshots default to **Immediately** and capture the application that had
  focus before WidgetRail opened. The overlay stays open. **Screenshot delay**
  and **Clip delay** can be set separately. Clips default to five seconds.
  With a preparation delay, WidgetRail hides and gives
  you time to bring the application forward. Recordings also hide the overlay. Reopen the
  overlay to finish a recording early. Reopening during preparation cancels.
- Successful captures appear in **Pictures → WidgetRail → Captures**. Screenshot, clip and saved-replay banners name the captured application when its identity is available. Open an item in the gallery to preview, edit, share or delete it later.
- The host owns accepted one-off recordings through the file commit. Clip staging is disk-backed and supports up to 2 GiB. Temporary attachments requested by widgets such as Game Help keep their existing save/discard flow.
- Browse a virtualized thumbnail grid with search, folder/application/type/date filters,
  favorites, sorting and date groups. Titles and favorites do not alter originals.
  Selected captures can be updated together, copied to a chosen folder, hidden,
  or deleted. **Hide…** preserves linked-folder originals; use **Type → Hidden**
  to restore them. Owned WidgetRail captures cannot currently be hidden and are
  explicitly kept if included in a mixed Hide selection. **Delete…** moves the
  selected originals—including linked files—to the Windows Recycle Bin after
  confirmation. Linked originals require their separate recycling permission.
  When more than 256 captures match, **Select first 256 matches** follows the current filter and sort order.
  Cancelling confirmation keeps the selection. After confirmation, progress shows the number processed.
  **Stop remaining** cancels outstanding work and keeps changes Windows has already made. If the host loses a receipt, the widget
  stops sending changes. **Review batch results** identifies unavailable files, changes awaiting
  confirmation and items skipped. Select remaining captures again to retry.
- **Open folder** opens the saved location in Windows Explorer through the host's
  normal release-aware foreground handoff. No separate export copy or file picker
  is required. Busy buttons retain their focus identity.

Game Help's **Add context → Choose saved capture** and its in-chat context choices
open the same library. Selection opens the existing review/question editor;
images use a bounded context rendition and videos retain a read-only original.
Videos exceeding the 8 MiB Game Help payload limit remain visible but unavailable
for context. Only **Send to Gemini** sends it to the provider.
Optional library read access is separate from permission to capture a new window.

## Replay recording

**Start replay** uses the approved application window that had focus before you
opened WidgetRail. If that window is unapproved or unavailable, choose a window
and select **Record** for this session or **Record and add to approved** for
future automatic starts. A notification names the app once recording starts.
The host owns that recording, so closing or idling Capture Library does not stop
it. **Save replay** saves the available recent video to the default capture folder;
the saved duration can be shorter while the buffer fills, at its memory limit,
or because the clip must begin on a decodable keyframe. **Stop** ends recording.

Capture settings contain duration, memory budget, hardware quality, application
audio, cursor and optional automatic start for approved applications.
Stop an active session before changing those settings; changes never silently
discard its buffer. Defaults are 60 seconds, 128 MiB and hardware 720p at 30 fps with application
audio. There is no continuous software fallback. Unsupported encoder/audio
configuration fails visibly. Minimize pauses the active recording timeline;
sleep retires the buffer. When an application exits, the host finishes its session before considering
another application for automatic recording. Auto-start requires one open
approved application with one eligible window. If multiple approved apps are
open, WidgetRail waits and checks again after five seconds. **Start now** lets you choose while it waits. Automatic
starts use the same recording notification as manual starts.

In **Capture settings → Replay recording → Approved applications**, use **Add application**
to choose an open app. Adding it saves permission for future automatic starts. Removing
an app while it is recording preserves the current session and its buffered video.

The host tray exposes recording status and Save/Stop independently of widget
residency. Configure the optional replay keyboard shortcut in host controller/input
settings; its disabled default is Ctrl+Shift+F10. Removing the controlling feature
or its capture permission retires its recorder. Removing library-save permission
cancels that caller's pending save without stopping an independently authorized
recording. Closing WidgetRail releases
the worker and buffer. An already-admitted save is drained before normal Stop.

Replay shortcuts show a transient result banner, subject to notification preferences. Saving can finish while the Capture Library widget is unloaded. Starting a replay requires the capture feature to be installed and authorized.

`HostServices.Capture.Replay` exposes `GetAsync`, `OpenChangesSubscriptionAsync`,
`ConfigureAsync`, `StartAsync`, `StopAsync` and `SaveAsync`. Subscribe before reading
initial status, accept only newer revisions, and use widget active lifetime for
observation. Recording itself belongs to the host. Status requires library read;
controls require window capture; saving additionally requires library save.
Settings updates carry the displayed settings revision. Stop/Save carry the
displayed session ID, so stale controls cannot affect a replacement session.
Save returns a validated library item and actual duration, rather than a shortcut
dispatch acknowledgment. Do not automatically retry ambiguous save side effects.

Instant replay keeps buffering the selected window if its application audio becomes unavailable. The Capture Library shows an audio warning, preserves earlier sound and fills the missing interval with silence. It retries audio capture for the same application in the background and clears the warning when audio packets are accepted again. A quiet application needs no restart.

## External Windows captures

**Capture folders → Link Windows captures…** resolves Windows' configured
`KnownFolders.AppCaptures` folder, including relocation, and asks you to approve
linking it. You can choose another folder, or use the picker if Windows cannot
resolve the location. Linked files stay in place and use the existing discovery,
thumbnail, query and original-file playback paths. This does not duplicate videos.

Windows folder import is independent of replay recording. WidgetRail does not
toggle Windows recording settings or send Game Bar recording shortcuts.

`HostServices.CaptureLibrary.ChooseKnownSourceAsync(
CaptureLibraryKnownLocation.WindowsGameCaptures, "Windows captures", token)` uses
the existing source-choice grant and requires library manage permission. Manual
`ChooseSourceAsync` callers, including Playnite achievements, retain their normal
folder picker. Integrations provide domain metadata separately; Windows filenames
are not reliable game or achievement identifiers.

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
| `media.capture-library.save.v1` | Explicit interactive save of owned temporary media, or host-owned Edit copy with separate source permission |
| `media.capture-library.manage.v1` | Explicit interactive metadata edits, bounded batches, recycling/deletion, and opening/revealing captures in the fixed Pictures folder |
| `media.capture-library.recycle-linked.v1` | Explicitly recycle exact indexed originals in connected capture folders, including folders connected by other widgets; no permanent-delete fallback |

Read access covers the user's shared saved library, including other widgets' saved
captures. It grants no authority to initiate screen capture. Save/manage operations
are not dashboard shortcuts and require interactive lifecycle. Both sandboxed and
full-trust widgets use the same service and checks.

### Edit a copy

`HostServices.CaptureLibrary.EditCopyAsync` accepts either an exact saved
`CaptureLibraryMutationReference` or this widget's issued, owned temporary
`CaptureAttachment`. The host supplies crop/quarter-turn rotation for PNG images
and start/end trimming for supported MP4 videos. Widgets do not supply recipes,
file paths, crop handles, or editor focus state. Video output is limited to ten
minutes and 2 GiB; imported playback limits do not enlarge the edit output budget.

Saved sources require both library read and save consent. Temporary sources require
capture and save consent and cannot impersonate an opened library reference. Both
permission leases remain live through encoding and commit. Entry requires an
interactive widget; the host closes the editor when its owner or source retires.

The result is `Saved` with a new owned `CaptureLibraryItem`, or `Cancelled` with no
item. Source, permission, revision, and encoder failures remain typed failures.
Neither the original bytes nor linked-provider files are modified. Preserve the
current preview on cancel/failure and open the returned item only on success.
There is no automatic retry or fallback to another operation after transport
failure: a committed result may already exist. Duplicate host Save submissions
share one result identity, and a late Cancel cannot erase a committed copy.

The editor and encoder each have a ten-minute deadline. The host polls a bounded
status record while encoding; the ordinary bridge request lane does not wait for
the encoder. Terminal status remains available briefly while the dialog closes.

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

`FoldersAsync(query, revision)` returns the WidgetRail-owned folder and enabled linked
folders across the library, bounded by the global source limit plus one. Counts
honor other query filters but ignore the selected folder. Empty `SourceId` selects
owned captures; null `CaptureLibraryQuery.SourceId` means all folders; otherwise
use the opaque source ID. The query applies to all indexed rows, including paging,
month groups, application facets and anchor lookup. Removed sources match no rows;
clients must not silently clear that selection. Friendly labels can contain a short
folder-name qualifier, but no absolute path or source-management authority is granted.
The separate source-management API remains owner-scoped. Pass the query revision
to reject a folder result from a different library ordering.

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

**Associating existing captures.** A capture from an identified window can include a process basename and a `SourceProcess` interval. Its opaque instance key identifies one execution of a Windows process. Integrations observing a launch can compute the same key with `CaptureProcessInterval.InstanceId(processId, creationFileTime)`. The file time is Windows UTC process creation time. Clip and screenshot intervals bound the capture operation. Replay intervals conservatively cover the recording session through save admission, including any pauses. A missing interval is ineligible for automatic matching.

Use `CaptureLibraryQuery.SourceIntervals` for an indexed containment query over up to 64 intervals. Combine `AvailableOnly`, `OriginPackageId`, `Reference` and `ExcludeReference` to read unlinked, available matches. With the manage permission, `AttachContextAsync(id, contentRevision, context)` attaches immutable caller-owned references to a visible, existing capture. It preserves the media and its original owner. Use a stable idempotency key per capture and relationship. Reusing that key for another capture or changing the reference identity is rejected. Each publisher/package can attach up to 64 associations per capture through this operation. The existing related-item query and gallery intent resolve the references.

Use `EnsureSubjectAsync` when the user wants to associate an existing capture with one destination. The broker checks the active package, kind and ID under the store lock before creating a receipt. Inherited references and references from another installation of the same package satisfy the request. The operation returns only the outcome. Explicit selection can restore the caller's own detached receipt. Use `GetDestinationsAsync` for a paged list of related destinations. It groups matching references before pagination and preserves their related-item actions.

For automatic matching, also set `ExcludeDetachedReference`. The manual picker can still offer a previously removed capture. A repeated `AttachContextAsync` for a detached receipt returns `Detached`, including after an index rebuild.

`FindOwnedLinksAsync(captureIds, reference)` returns editable links for up to 16 visible captures. The lookup is scoped to the authenticated publisher, package and widget instance. Multiple matching associations, compound references and timed events do not offer a single removable link. A copied capture has its own association identity.

Use `SetContextLinkedAsync(contentRevision, association, linked: false)` after an explicit unlink action. Keep the returned association for Undo and pass it back with `linked: true`. The broker checks the capture content revision and the association's link revision. A repeated request can confirm its previous result. An older command cannot overwrite a newer decision. Ordinary media queries, related actions, edits and copies see only active links.

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
(the capture ID, content revision, and metadata revision). The host opens its folder chooser. **Copy here** selects the browsed folder for the whole batch. Widget requests never include paths. The
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
metadata, reveal and copy operations work on linked items. The original Delete
and `RecycleAsync` contracts reject them with `capture_library_read_only_source`.
`HideAsync` (the compatible `RemoveLinkAsync` alias) hides a linked entry from
normal discovery without changing its original or revoking known-ID reads.
Capture Library opens with `AvailableOnly`, as do Playnite's linked galleries and Game Help's attachment picker. Filters > Type > Unavailable uses `UnavailableOnly` to browse missing originals. Both options are optional on the wire and are mutually exclusive. The same filter applies to paging, counts, groups and position lookup. Hidden captures remain accessible through the separate Hidden filter.

Linked files with stable Windows file IDs retain their title, favorite, and game links when the original returns to the same source path. A different physical file at that path gets its own capture ID. Existing path-based capture IDs remain valid for their original file. Source watchers reconcile folder departures and coalesce file changes. Offline sources retain their records.

`CaptureLibraryQuery.HiddenOnly` selects hidden entries through the same bounded
Query/Groups/Find/facet pipeline; default requests omit the new flag, and item
response envelopes remain unchanged. `UnhideAsync` accepts an exact content and
metadata revision under Manage permission and returns the restored item. Both
operations advance MetadataRevision. Missing originals can be unhidden while
the source remains authorized; retired sources are omitted and unreadable.

Hidden choices and their last-known metadata snapshots live transactionally in
`library-user-state.sqlite`, separate from rebuildable `library.sqlite`. New
hides retain the original volume/file identity: editing that file stays hidden,
while replacing it at the same source-relative path creates a visible entry
without inheriting the old hidden metadata. Renames retain existing path-based
capture identity semantics. Legacy `.unlinked` markers migrate conservatively:
the path remains hidden even after replacement until explicit Unhide. Legacy
IDs lacking snapshots gain labels only through normal bounded discovery.
A missing/corrupt canonical database is never recreated empty after initialization.
Linked discovery pauses; owned captures remain usable. Restore a backup and
restart after `capture_library_user_state_unavailable`; originals are untouched. `RemoveSourceAsync` removes a source's linked index
entries and stops its watcher. Neither operation removes provider media or its
metadata companions. Older list clients see only owned library items.

`RecycleLinkedAsync` is a separate, optional-permission operation using the same
bounded mutation references and per-item results as `RecycleAsync`. Request
`WidgetCaptureLibraryCapabilities.RecycleLinkedPermission` only after an explicit
user action that clearly identifies deletion of the original file. Existing
manage grants and read-only source grants do not imply this permission. Capture
Library presents both **Hide from library** and **Move to Recycle Bin** for an
available linked item; its existing mixed-source batch removal behavior is
unchanged.

The broker verifies the current enabled source, opaque item identity, content and
metadata revisions, canonical relative path, and recorded filesystem identity.
It rejects traversal/reparse points, retains the directory chain during the OS
operation, and rechecks the current identity and request cancellation immediately
before Windows recycling. Revocation or a changed file while queued prevents the
operation. Completed OS moves are reconciled even if cancellation arrives during
the move; later items report cancellation. Unsupported recycling leaves the file
intact. Only the media original is recycled, not provider context companions.
Restoring the same original to its source path reuses retained metadata; a
different file at that name is discovered as new content.

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

## Default-folder copies

`HostServices.CaptureLibrary.SaveCopyAsync(reference)` creates a new owned capture in the host's default capture folder without opening a file picker. It requires the library manage permission and an interactive request, returns the new `CaptureLibraryItem`, and rejects stale content/metadata revisions. The host generates a collision-resistant filename and a title ending in `(copy)`; the widget can use its normal title editor afterward. Related contexts and the original capture timestamp are retained. Favorites are not copied.

Original bytes are streamed with bounded memory, including long linked videos; no codec, full temporary import, or trim-duration limit applies. The original is unchanged. Copies receive independent IDs and remain after cancellation once committed. For batches, call this operation sequentially with bounded selection and report individual outcomes. `CopyAsync` remains available for explicit export to a user-selected folder; its compatibility and picker behavior are unchanged.

### Named browse filters

`WidgetIntentContracts.CaptureLibraryV2` adds required `referenceLabel` (1–256 characters) to the existing `originPackageId`, `referenceKind`, and `referenceId` payload. The label is presentation text only and never participates in SQLite matching or authorization. Capture Library continues to handle the unchanged v1 contract, so existing senders remain compatible.

### Gallery previews

Screenshots and available clips use the shared [native media rich preview](widget-media-player.md#media-in-rich-previews), with saved ID/content revision declarations and retained-original access. Screenshots do not allocate a video decoder. **Play clip previews** controls passive autoplay; it is always muted until interaction and is suppressed during selection. R3/F6 enters the preview's normal controls. Open, Share and Favorite remain available there. Fullscreen preserves the same player and playback position, then returns to the valid preview. Preview expansion does not resize gallery rows or replace the grid. Changed or removed sources retire the preview rather than retaining obsolete actions or media access.

### Sharing captures

Share is available from a gallery preview, capture detail and the selection toolbar for up to ten captures. It uses the standard [file-sharing intent](file-sharing.md); any compatible installed widget can receive the offer. Capture Library does not choose a recipient or send a message. The receiving widget owns recipient review and explicit sending.

Selected sharing uses the indexed collection's bounded selection reader and independently retained original-file grants. Changing the query, selection or navigation during preparation cancels that preparation; changed or unavailable originals are rejected. Terminal feedback releases sender resources without revoking custody already accepted by a recipient. Sharing leaves the original files and gallery selection intact.


### Gallery query continuity

`QueryContinuityAsync` returns `CaptureLibraryContinuityPage(Page, OrderingRevision)`
through a separate Read operation. The original Query reply remains unchanged.
For the same exact query/filter and count, an unchanged OrderingRevision proves
that the existing indexed collection can refresh row content and keep its selection.
Inserts, removals, Hide/Unhide, and title or content changes invalidate the proof.
Changes to associations invalidate it for searches and origin/reference filters.
FavoritesOnly uses the full query revision because toggling a favorite can change
membership. Range reads use the new `Page.Revision`.

`RunsAsync` groups eligible captures by exact source process instance before
paging. Each legacy capture without that identity occupies its own row. Each run
includes a representative, time range and media counts. Source terms and a
preferred executable affect ranking. The query's filters determine membership.
Use the returned revision to read the reviewed members before a batch write.
