# Capture Library storage

New captures save directly to Pictures/WidgetRail/Captures. The private profile
keeps `capture-library-v2` metadata, a SQLite index and bounded individual
thumbnails. There is deliberately no migration of the former private library;
its files are left untouched. Files copied arbitrarily into Pictures are not
automatically imported as trusted saved captures.

## Index and data ownership

- `captures` has indexes for date/ID ordering, content hash and filename. The
  gallery reads one SQL page. A singleton totals row is maintained by SQL
  triggers, so counts and bytes do not require a directory walk.
- Durable per-item metadata publishes after a successful media copy. SQLite is
  a rebuildable index, not the only record of a saved file. Interrupted saves
  have an explicit pending marker; only that marker authorizes cleanup.
- Startup, explicit Refresh and notification overflow reconcile metadata and
  file attributes. Ordinary page reads drain bounded file notifications and
  query the index. They do not decode or hash all originals.
- Live renames update known filenames. Edits invalidate the private thumbnail
  and change the public item's `Revision`, which widgets include in artwork
  handles. Visible artwork/open requests re-inspect edited media lazily.
- Integrity verification is cached against length, timestamp and expected hash.
  The read lease prevents writes during verification/copy. Unsupported edits or
  missing metadata never authorize deleting a public original.
- Delete removes the public file. A tombstone records an authorized deletion
  when an existing read lease delays physical removal. There is no automatic
  eviction and no fixed count/byte quota. Free-space checks retain a small
  working margin; disk-full failures remain explicit.
- Corrupt SQLite files are preserved under a diagnostic filename and rebuilt
  from metadata. No media migration or rewrite occurs during index recovery.

## Open folder

`HostServices.CaptureLibrary.OpenFolderAsync` requires the manage capability and
an interactive request. Widgets cannot provide a filesystem path. The broker
queues the library's fixed folder; the frontend verifies the originating widget
and uses the same release-aware main-window handoff as external web navigation.
The host rechecks request currency and consent immediately before launching
Explorer. An already-authorized request may complete after intentional hide;
new requests from a background widget remain denied. Cancellation retires its
request ID so a late reply cannot complete a replacement request.

The older Export operation remains a compatibility alias returning the existing
public filename. It does not produce a second copy. The widget shows Open folder
instead, and reports storage totals in Capture settings rather than the gallery.

## Validation

Evidence is in `artifacts/capture-storage`: 27 CaptureLibrary checks, two Game
Help saved-capture checks, the native frontend build and a Release Bridge publish.
The publish contains SQLite's native DLL, managed provider and license files.

The 240-item test exceeds the former count limit. Twelve indexed pages took about
26–31 ms locally, with no directory reconciliation or metadata-file reads during
paging. This excludes initial reconciliation, media decoding and thumbnail work.
The suite covers restart reuse, malformed metadata, linked paths, leased deletes,
free-space refusal, external rename/edit/delete, thumbnail invalidation, index
recovery, request cancellation and permission/lifecycle boundaries. An Explorer
handoff still needs physical acceptance in the complete candidate.

## Query revisions, metadata, and recoverable removal

The capture service remains domain-specific. Query predicates/order expressions
are selected from bounded typed fields with parameterized values. The disposable
SQLite index stores a random epoch and mutation counter; triggers advance the
counter on actual indexed changes. Rebuilding the index replaces the epoch, so
previous range revisions cannot accidentally match a rebuilt ordering. Repeated
queries read neither every sidecar nor original media. Explicit reconciliation
rereads sidecars even if their timestamps were preserved, avoiding a stale cache
on rapid corrupt/replace operations.

Custom titles, favorites, and metadata revision are retained in each capture's
JSON sidecar. Rebuilding or deleting only SQLite retains them and the originals.
Deleting sidecars loses their custom metadata; the index is not a backup.
Successful recycling retains a separate `.recycled` sidecar and an index of its
original filename. A restored original is reconciled back into the library with
its metadata. A malformed marker never authorizes deleting a public file, and
recycled rows are not revived merely by rebuilding SQLite.

The store coalesces file-change/commit invalidations; broker subscriptions retain
the existing bounded event queue, lifecycle deferral, and consent revocation.
Subscribe-before-query prevents the initial snapshot/event gap. Range revisions
reject mixed membership/order; UI selection/focus restoration remains the existing
SDK collection's responsibility.

The internal Windows recycler binds a filesystem `ITransferSource` and calls
`RecycleItem` with the system Recycle Bin shell item. It never calls `RemoveItem`
or falls back to permanent deletion. Unsupported volumes return failure. Native
qualification uses uniquely named temporary files only, confirms the exact item
in the Windows Recycle Bin, restores it with Windows Shell, and verifies bytes.
Service tests independently cover failed callbacks, partial batch results,
cancellation, metadata preservation, and restoration after a SQLite rebuild.

## Retained video playback

`CaptureLibraryStore.OpenAsync` now transfers a `CaptureLibraryReadLease` for
videos to `WindowCaptureRegistry.AdoptLibraryVideo`. Registry entries explicitly
mark borrowed originals; all discard, retirement, expiration, cancellation and
disposal paths release their lease instead of using temporary-file deletion.
Bounded image context copies retain the existing import path.

`CapturePinnedFile` pins the directory chain against rename/replacement, rejects
reparse components, and holds the file without write/delete sharing while the
frontend opens its resolved host-only URI. Native `FILE_ID_INFO` and
`FILE_BASIC_INFO.ChangeTime` detect replacement and in-place changes even if
length/mtime are restored. Linked imports do not hash full videos; owned originals
retain hash verification. Thumbnail/export/copy verification uses the same retained
lease, so none of those paths silently assumes an all-zero linked-video hash is
an owned-content checksum. Unsupported native file identity fails closed.

The existing two-second frontend authority probe renews a two-minute idle lease.
Its response must still name the same media and URI before updating the local
expiry. Source disconnect and index removal invalidate retained handles at that
probe. Capture permission is checked from the resolved source's required
capability, preserving separate library-read and new-window-capture authority.

Qualification covers a 134,221,824-byte synthetic two-hour source (codec facts
stubbed for this isolated store test), exact original-path resolution, no copies,
retained-directory rename rejection, preserved-mtime edits, chunk reads, ownership,
renewal, revocation, disconnect, idle expiry and repeated disposal. Native decoder
qualification is separate; the managed store test does not establish playback
quality or frame delivery.

The native production-widget deletion fixture (`--validate-capture-delete-playback`)
uses the shipping Capture Library widget and presenter, real original leases and
Windows recycling. It tests normal publication and a deliberately gated old scene,
without directly disposing the decoder. Both succeeded on the final protocol-114 build: 94 ms with ordinary
publication and 36 ms while the old native decoder still existed. On this Windows
Media Foundation path, releasing the broker's restrictive original lease permits
recycling even before the queued renderer update. This is evidence against adding
a new renderer-retirement acknowledgement for this flow, not a guarantee that
unrelated applications will never hold a file in use. The separate two-hour native
fixture covers actual decoding, seeking, renewal and source revocation.

A third native case keeps another widget's independent original read lease. The
same production delete flow then fails safely with
`capture_library_recycle_unavailable` (61 ms), preserves the exact original bytes,
reopens the requesting widget's preview, and leaves the other widget's authority
valid. The host does not revoke unrelated consumers to force a deletion through.
The fixture uses an in-process capability transport with real store/registry/OS
operations; scene application is explicitly gated to model delayed IPC delivery.
It does not claim to be a full shell/worker IPC test.
