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
