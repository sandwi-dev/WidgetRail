# Spotify integration

Spotify is a separately installed full-access application widget. It owns its
Spotify connection and playback integration. See the
[package README](../../samples/SpotifyWidget/README.md) for setup, account needs,
and configuration.

## Browsing

Search supports tracks, albums, artists, and playlists. Queries run when submitted;
completed results can survive closing and reopening the overlay. Changing the
query or result type starts a new collection. Late results must not replace a
newer query.

Playlists and search results use shared cursor behavior. Page loads retain stable
item identities and loading/error states. Searching is not a reason to reset
unrelated playlist data.

## Playback context

Playing a track from an album or playlist uses its Spotify context and an offset
when available. Sending only one track URI as a new playback list would replace
that context.

Selecting the first upcoming queue item uses Next track. Selecting a later item
uses that item and the remaining cached queue as a replacement playback list.
That is not a server-side jump to an arbitrary queue position, and it changes
the original context.

Add to queue is limited to individual tracks. Adding an entire album or playlist
would require a queue write for each track; the widget does not perform that bulk action.

## Requests and caching

Local UI ticks and interpolated progress are not automatically Web API requests.
The widget refreshes playback according to demand and lifecycle, and reconciles
explicit playback actions promptly. Do not add polling to `Render()`.

Playlist pages are cached by playlist identity and checked against fresh
`snapshot_id` metadata before reuse. The cache contains visited pages and track
metadata, not downloaded audio. Artwork uses separate image caching.

The disk cache is partitioned by the saved authorization grant. A new authorization
does not silently reuse another grant's pages. Cache I/O failures fall back to
ordinary loading without taking down the widget.

Exact capacities and request scheduling are implementation settings rather than
SDK promises; inspect the [Spotify source](../../samples/SpotifyWidget/) before changing them.

## Errors and provider limits

Respect `Retry-After` on rate limiting and avoid automatically repeating ambiguous
queue/skip writes. An uncertain response may still mean the provider performed
the action. Retain useful UI state and offer a bounded retry where appropriate.

Spotify account access, SDK eligibility, device availability, and licensing are
provider requirements. The widget cannot bypass them. Keep credentials out of
public logs, snapshots, and cache files.

This integration's direct provider access belongs to its full-access runtime.
It does not grant ordinary sandboxed widgets arbitrary network or credential access.
