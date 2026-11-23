# Public media transport

Host-owned acquisition of anonymous HTTPS media. This library is a transport
boundary, not a decoder or a browser. It has no access to Discord sessions,
cookies, credentials, WebView profiles, or widget permissions.

`PublicMediaClient.DownloadAsync` applies the supplied `PublicMediaPolicy` on
the initial URI and every redirect. A policy may list up to sixteen exact,
canonical origins; it never interprets suffixes as authorization. The default
public-HTTPS policy allows arbitrary public origins and is not a substitute for
a provider's verified origin list.

Production connections resolve once and dial a checked IP endpoint, preserving
the HTTP handler's standard TLS/SNI and certificate validation. Private,
loopback, link-local, multicast, documentation, transition and selected virtual
host-service destinations are rejected. A mixed public/private DNS result is
rejected before dialing. Proxies, cookies, credentials, automatic redirects,
decompression and response-body draining are disabled. HTTP/1.1 is required so
the checked connection callback remains the only network path.

The caller sets the encoded byte limit. Active requests, waiting requests,
redirects, headers, connection time and total request time have independent
bounds. Cancellation/teardown covers both queued and active requests. Errors
contain a category and fixed text, without response bodies, signed URLs or raw
HTTP exceptions. Returned bytes are owned by the caller; no remote URL is handed
to a native decoder by this API.

## Current integration boundary

The renderer's protocol-133 `Public*` path now uses this module in source.
`PublicMediaPolicy.SameOrigin` locks redirects to the canonical source origin;
the provider remains responsible for choosing an appropriate initial URL.
`NativeArtworkStream` supplies checked bytes to bounded image decoding and
`NativePublicMediaSource` inspects container metadata before passing its WinRT
reader to `MediaSource.CreateFromStream`. Merely checking a URL here and later
calling `MediaSource.CreateFromUri` would bypass these guarantees. Legacy
`WebUrl`/`WebImage`/`UI.Image` remain separate compatibility paths. Native
integration and visual qualification are tracked in the Discord media plan;
this source integration is not a claim of completed live acceptance.

Local files, capture-library grants, sealed package assets and browser-owned
media remain separate authorities. Do not route them through this downloader.
`PublicMediaBlockSource` provides version-pinned random reads for a native stream
adapter. The opening request may follow admitted redirects. Later reads go
directly to the final URL that issued the strong ETag, including its query
string. A redirect from that URL during a versioned read retires the source.
ETags are scoped to a resource, so the same tag on a different URL cannot
establish byte continuity. The native media player requests 256 KiB blocks so
Windows can inspect container metadata and begin decoding with fewer serial
HTTP round trips. The player keeps the existing 4 MiB range-cache and aggregate
transfer limits. Whole-image downloads use their own byte limit.
The reader also validates Content-Range and length,
keeps a bounded LRU cache, limits aggregate transfer work, and cancels queued/inflight
reads on teardown. Missing range support or a strong validator is accepted only
as a small bounded whole-file snapshot. Invalid continuation data invalidates
the source; cached bytes cannot mask a changed resource. `PublicMediaFormat`
identifies container signatures; native metadata/codec validation supplies the
remaining format checks. Identification alone does not establish valid content.

Validation is in `tests/RemoteMediaTransport.Tests`. It covers address/origin
policy, DNS pinning, redirects, response/byte bounds, queue admission, timeout,
cancellation, teardown and safe errors. The recorded live CDN probe verifies a
small public PNG over production TLS/connection handling; it does not establish
native decoding, video playback or every network configuration.

The opt-in WinUI fixture `--validate-styles --public-media-stream-output=<file>`
qualifies the `NativePublicMediaStream` WinRT adapter against the public W3C
Sintel MP4. It checks independent clone positions, tail/EOF reads, native open
metadata, playback-clock advancement, forward/backward seeks and reader
retirement. `--public-media-visual-check` additionally leaves two paused frames
visible for inspection. It is excluded from shipping builds and uses no account
or user attachments. Clock advancement and frame-availability callbacks alone
are not proof of correct visible pixels; visual decoding remains a separate gate.
The fixture also exercises the opted-in image/video source implementation.
It does not qualify all native codecs, every network environment or live Discord
attachments; those gates remain explicit in the plan.
