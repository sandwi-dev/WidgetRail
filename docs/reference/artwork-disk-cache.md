# Shared artwork disk cache

The host keeps reusable HTTPS image bytes in LocalAppData/WidgetRail/cache/artwork-v1,
limited to 256 MiB and 4,096 files. Static package assets already live in package
storage; opaque provider artwork keeps its existing provider authority and is not
persisted under transient handles.

The CPU decoded-image budget is 160 MiB (previously 192), and the renderer bitmap
budget is 80 MiB (previously 96). Entry counts, per-image bounds and visible-image
retention protections stay in place. Disk files store compressed source bytes,
not full-size decoded buffers; requests still decode to the required display size.

Reads/writes occur only on artwork workers. Hashed URL keys, bounded headers,
size checks and SHA-256 checksums cover metadata and contents. A corrupt or
undecodable hit is discarded and fetched again. Writes use a temporary file and
atomic replacement; an exclusive writer lock coordinates host processes. LRU
trim failure, file locks, denied writes and disk-full errors disable that write
without losing the successfully decoded image. No writes occur below 64 MiB free
space. Orphaned temporary files are removed by the next successful writer.

Persistence is conservative: it requires explicit positive Cache-Control max-age,
subtracts Age/apparent age from Date, and caps retention at 30 days. No-store,
no-cache, private, unsupported Vary, Set-Cookie and automatic redirect responses
are not persisted. Expired entries are fetched again. No conditional revalidation
or stale-on-error serving is introduced. Cache IO exceptions do not escape the
optional storage helper.
