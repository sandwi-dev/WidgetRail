# Spotify Web API integration

## Persistent page storage (0.3.63)

Memory now retains up to 16 playlists, 1,024 track records and 64 pages. The
optional disk tier retains visited pages within 100 playlists and 50 MiB total
across saved authorization partitions, under LocalAppData/WidgetRail/community-apps/
widgetrail.samples.spotify/cache/playlists-v1. Pages are checked against freshly
fetched snapshot_id metadata before reuse; disk persistence introduces no new
Spotify endpoint or eager collection fetch. Artwork URLs are stored, not audio.

A random non-secret partition ID belongs to the saved OAuth grant. It survives
refresh-token rotation and application restarts; new authorization gets a new ID.
Legacy credentials acquire the optional ID without changing their token/scopes.
A failed metadata-only credential write leaves authorization usable and disables
disk persistence for that observation. No credentials are stored in cache files.
Disconnect/configuration reset retires the partition and attempts to delete its
pages. Normal widget/application shutdown preserves the cache and drains pending
writes within the existing shutdown deadline.

Disk work runs off the presentation thread. Reads have a 250 ms fallback deadline;
fetched pages publish without waiting for disk writes, with at most four writes
pending. JSON depth, file size, item count and media fields are checked before use.
Writes replace files atomically, use a cross-process writer lock and evict least
recently used entries. Storage corruption, locks, denied access and full disks
remain cache misses. Optional writes stop below 64 MiB free space; cleanup failure
never prevents playback or browsing. Add to queue remains individual-track only.

## Versioned playlist cache (0.3.62)

The package preserves Spotify's opaque snapshot_id on playlist summaries.
Each new playlist entry still fetches metadata once. Matching versions reuse
cached pages; a changed or missing version invalidates those pages. Metadata is
not polled while browsing. Closing/reopening the overlay on the same detail route
keeps the active cursor without another metadata check.

The per-widget memory cache retains at most four playlists, sixteen pages and
192 track records in total, evicting least recently used entries/pages. Page keys
include offset and requested limit; no entire-playlist prefetch or disk cache is
introduced. Explicit detail Refresh bypasses cached pages and rechecks metadata.
Disconnect/configuration reset clears the cache. Canceled loads and writes with
an obsolete cache entry cannot repopulate a replaced or cleared version. The API
does not support reading historical track pages by snapshot_id; edits made during
an active visit are discovered on the next entry or explicit refresh.

Widget context menus now survive unrelated progress snapshots. The host checks
the source node, scope, collection reset identity and exact context actions
before keeping or dispatching a menu; it still rejects instance/runtime changes,
removed or disabled sources, altered actions and navigation away. This is shared
host behavior and does not pause Spotify's progress display.

## Playlist opening (0.3.61)

Playlist selection is prepared before the navigator publishes the detail route.
Concurrent progress renders may observe that intermediate state. Page content now
follows the captured route, matching the navigation entry links: the list remains
visible until detail navigation is published. This avoids a loading detail page
whose header points at a removed playlist row. A deterministic intermediate-state
test reproduces the production invalid_focus_target path, and slow-loading detail
snapshots are validated before completion. No extra Spotify requests are added.

## Playback controls and request budget (0.3.60)

X remains Play/Pause. Menu on a playable Search track or playlist track exposes
Add to queue with a themed, five-second confirmation toast. One selection sends
one queue write; it does not load the whole collection. Album, artist and playlist
Search results continue to start their Spotify context; playlist track selection
uses the playlist context and track offset. Album track browsing is not currently
a separate widget route.

The first upcoming Queue entry uses Next. A later entry starts a new playback
list containing that occurrence and the remaining playable cached entries (at
most 50), preserving duplicates. Spotify has no public arbitrary queue-index jump:
this replaces the original context and cannot preserve an unseen tail. It never
infers a queue occurrence's origin from the current context or album metadata.

Active playback polling waits 5 seconds playing, 15 paused, or 30 idle after a
read; the 250 ms progress tick is local only. Device and collection caching stay
unchanged. Starting playback or transferring to Play here reschedules the same
poller after its immediate read. If playback is still stale, at most three extra
reads use 1/2/4-second delays, ending early once the expected playing state appears.
Transfers always request one follow-up observation. Failure backoff takes priority;
deactivation cancels polling. There is no second background polling loop.

Playback mutations share a single command gate. Overlapping ordinary mutations
receive a wait message instead of being replayed later against a changed queue;
read-only refresh remains independent. Playback-start follow-up reads refresh a
demanded queue once, without a second invalidation from the same observation.
Ambiguous 5xx responses to queue/skip POST commands are not retried; 429 retry
delays and safe-read retries remain bounded. No new permissions or shared SDK or
host changes are required.

## Search (0.3.60)

Search is the first section and the initial route for a new widget instance.
Submit a query through the controller keyboard, then choose Tracks, Albums,
Artists or Playlists from Results. A plays a track or starts the selected album,
artist or playlist on the existing playback device, subject to Spotify's normal
availability and playback restrictions.

The package owns a typed `/v1/search` endpoint and reuses the authenticated
session without additional OAuth scopes. Queries run only when submitted.
Pages contain at most ten items, within Spotify's 1,000-result search window.
Search rankings and totals may change between requests. The cursor therefore
uses unknown virtual extent and occurrence identities, rather than promising
fixed absolute positions or rejecting repeated results across page boundaries.
Paging errors retain existing rows and require an explicit retry; the shared
SDK versions error/retry boundary metadata without resetting collection focus.
Query/type changes reset the SDK cursor; late responses cannot replace current
results. Closing/reopening preserves completed queries and results. Loading,
empty, unavailable and retry states stay inside Search. Existing queue,
playlist, device, setup and pinned behavior is preserved. No Spotify-specific
host or SDK changes are required.

Status: **autonomous full-trust Community application 0.3.3 implemented;
live-account and WebView2 playback proof remains manual as of 2026-08-13**.

The shipped reference is a separately installable Community application. Its
immutable package owns the Web API/PKCE backend, protected refresh-token storage,
package configuration reader, response parsing, queue/playlists/devices,
playback policy, and lazily started WebView2 Web Playback child. It enters the
product only through the generic `full-trust-application-v1` supervisor and
generic overlay protocol. It does not request `external.spotify.*`, load a
product-owned Spotify assembly, or add a Spotify contract to product core.
The retired product-owned provider, broker domain, typed SDK service, and
playback-host source paths have been removed; the package remains the sole
Spotify domain owner.

The metadata/control path uses Spotify's official Web API and reviewed OpenAPI
schema, not desktop-client reverse engineering, browser scraping, or private
endpoints. A separate trusted Web Playback SDK host now implements the bounded
local-audio process boundary; it is not a general WebView or Community-worker
web permission. Provider orchestration is implemented; live device proof
remains. The Spotify application, OAuth login, quota,
Premium eligibility, and terms remain external requirements.

### Implemented foundation versus remaining product work

| Area | Current status |
| --- | --- |
| Public configuration | `WidgetConfigurationStore` persists bounded non-secret values under `widget-config`, isolated by publisher and package; `wrail config` provides the current local workflow. |
| Application contract | Package-private bounded DTOs connect the widget to its package-owned backend; no Spotify DTO or capability is added to product core. |
| Package backend | Implemented PKCE, exact loopback callback, Credential Manager refresh-token storage, player snapshot/control projection, bounded `Retry-After` handling, scope allowlist, and sanitized application errors. |
| Product composition | Generic catalog, full-trust supervisor, authenticated overlay IPC, lifecycle, restart, and presentation only; no Spotify construction or authorization. |
| Community application | Version 0.3.3 publishes the accepted responsive Player, Queue, continuous occurrence-keyed Queue/Playlist/detail collections, Devices surfaces, and controller-first Client-ID onboarding through the generic full-trust package path. Wide and compact branches retain the same controller-first vertical-rail hierarchy and focus identities. |
| Setup UI | The controller setup route opens Spotify's developer dashboard, copies the exact non-secret redirect URI, and invokes the host-owned text-entry modal with an explicit 96-character input maximum for Client ID entry or replacement. The package backend independently validates the committed value up to its 128-character defense-in-depth ceiling. A visible Setup action on the configured Ready navigation rail keeps replacement reachable without disconnecting. The CLI remains a developer/diagnostic route, not an ordinary setup requirement. |
| Live evidence | No allowlisted-account login/playback evidence has been captured yet. |
| Local Web Playback SDK audio | Isolated singleton WebView2 host, lifecycle/token orchestration, sanitized local-device projection, and offline protocol/process tests implemented; live account/device proof remains. |

## Current external gate

New Spotify applications start in Development Mode. As of August 2026, Spotify
documents a maximum of five allowlisted authenticated users per Development
Mode app and requires the app owner to have Premium. Development Mode also has
per-developer-account endpoint quotas distinct from the rolling API rate limit.
See [Quota modes](https://developer.spotify.com/documentation/web-api/concepts/quota-modes).

That is acceptable for local development and a small tester group, but not
proof that a public Community addon can serve arbitrary users. Extended Quota
Mode is a separate partner/review gate. The widget must render an honest
unsupported/not-allowlisted state rather than presenting a failed login as a
platform bug.

## Authorization contract

Use Authorization Code with PKCE (`S256`) as a public native client:

1. Generate a cryptographically random `state` and PKCE verifier in the trusted
   provider. Keep both only for the bounded authorization attempt.
2. Start a temporary loopback listener only on the fixed callback
   `http://127.0.0.1:43827/callback/`. Never use `localhost`, another port/path,
   or omit the trailing slash.
3. Open Spotify's authorization page in the user's browser with
   `response_type=code`, the exact redirect, `state`, challenge, and smallest
   required scope set.
4. Require the exact path/state, accept one callback, stop the listener, and
   exchange the code with the original verifier.
5. Never use Implicit Grant, put a token in a redirect fragment, ship a client
   secret in the addon, log the authorization code, or expose the verifier to
   widget IPC.

Connect is the only authorization operation with a continuation lease. It must
start from an explicit Interactive action. The input action acknowledges
immediately instead of waiting for the browser flow, and the resulting
authorization task uses the widget's Created-to-Destroying lifetime rather than
an action or Visible/Interactive token. Opening the system browser may therefore
move the widget through Visible and Background without canceling that
already-started request. This does not permit a new connect or any other control
from an inactive state.

The package selects explicit `keep-alive` residency so the supervisor cannot idle-
unload the worker while this one user-started browser authorization is in
flight. Active polling, progress interpolation, snapshots, invalidations, and
ordinary presentation work still follow their Visible/Interactive lifetimes;
`keep-alive` is not background provider authority.

The temporary callback listener is created only by that explicit Connect action
and waits at most fifteen minutes; there is no idle/background listener. Browsers,
endpoint-security tools, and proxy helpers may speculatively connect to the
loopback port. The receiver therefore tolerates at most 16 malformed or early-
close local probes inside the same fifteen-minute window instead of consuming the
only accept. Every accepted callback still requires a loopback peer, exact
`Host: 127.0.0.1:43827`, `GET`/`HTTP/1.1`, exact callback path, and matching
OAuth state. The exact
application Connect deadline is seventeen minutes. Its remaining two minutes cover only
bounded authorization-code exchange, HTTP retry/backoff, and credential-vault
persistence after the human callback window. Disconnect is not exempt.
Destroying, consent revocation, caller/pipe cancellation, or callback/deadline
timeout always terminates the listener and request.

Spotify requires loopback redirects to use an explicit IPv4/IPv6 literal. The
platform intentionally chooses one fixed URI so setup, validation, diagnostics,
and callback ownership cannot disagree. Settings must display
`http://127.0.0.1:43827/callback/` verbatim with an instruction to register that
exact match in the Spotify Developer Dashboard. See [Redirect
URIs](https://developer.spotify.com/documentation/web-api/concepts/redirect_uri)
and [Authorization Code with
PKCE](https://developer.spotify.com/documentation/web-api/tutorials/code-pkce-flow).
Spotify is deprecating Implicit Grant; see its [migration
guide](https://developer.spotify.com/documentation/web-api/tutorials/migration-implicit-auth-code).

### User-provided Client ID

The Spotify Client ID is public configuration entered by the user for this
widget/integration; it is never compiled into the addon. The implemented
package-owned `SpotifyClientConfigurationFileStore` validates and stores it
under the declared publisher/package configuration identity using the key
`client-id`. Documents are bounded, strict, atomic, and cross-process locked.
They are readable public configuration, so secrets, passwords, credentials,
and tokens do not belong there. OAuth tokens remain in the package-owned
Windows Credential Manager vault under the same publisher/package authority.

Ordinary setup is completed from the widget's **Setup** route: open the
developer dashboard, copy/register the exact redirect URI, and enter or replace
the public Client ID through the bounded host-owned text-entry modal. The
package validates and atomically stores the committed value, then immediately
rechecks configuration. Replacing it deletes the old client's refresh
credential before the new identity becomes active. The same Setup route remains
visible in the configured Ready navigation rail.

The following CLI remains available for developer diagnostics and automation:

```powershell
dotnet run --project .\tools\WrailCli\WrailCli.csproj -- config set widgetrail.samples.spotify client-id <spotify-client-id> --publisher widgetrail.samples
dotnet run --project .\tools\WrailCli\WrailCli.csproj -- config get widgetrail.samples.spotify client-id --publisher widgetrail.samples
dotnet run --project .\tools\WrailCli\WrailCli.csproj -- config list widgetrail.samples.spotify --publisher widgetrail.samples
dotnet run --project .\tools\WrailCli\WrailCli.csproj -- config remove widgetrail.samples.spotify client-id --publisher widgetrail.samples
```

`wrail config clear` removes every public configuration value for the selected
publisher/package authority. `--settings-root <path>` is available for isolated
local tests. The CLI rejects secret/password/token/credential-like keys. This
workflow configures the provider used by the overlay. Connecting still requires
an explicit user gesture because it opens Spotify's authorization page.

The current setup surface contains exactly:

- a bounded **Client ID** field;
- the fixed read-only redirect URI
  `http://127.0.0.1:43827/callback/` and exact-registration instruction;
- Connect/Reconnect and Disconnect actions with explicit status; and
- no client-secret field.

The setup route places the full instruction card in a controller VerticalScroll,
assigns a fresh Scroll node identity on every explicit setup entry so a prior
bottom offset cannot hide the title/first step, uses compact responsive spacing/
wrapping, and keeps Connect/Setup/Refresh/
Disconnect actions on the shared centered icon-and-label button geometry. These
are layout and navigation fixes; they do not weaken the authorization boundary
or make OAuth start automatically.

PKCE does not require a client secret, and a secret embedded in a desktop app or
Community package would not be confidential. Changing the Client ID through the
implemented provider deletes the old refresh token and clears its cached access
token before saving the new identity. Clearing configuration remains a
store/CLI operation. The package must never reuse tokens across Client IDs,
package authorities, or Windows users.

## Minimal scopes

Request scopes by feature and do not ask for profile/email, playlists, history,
or social data merely because Spotify exposes them:

| Feature | Minimum planned scope |
| --- | --- |
| Current item/progress | `user-read-currently-playing` |
| Playback/device/shuffle/repeat state | `user-read-playback-state` |
| Play, pause, previous, next, seek, shuffle, repeat, volume, and device transfer | `user-modify-playback-state` |
| Determine whether the current URI is saved | `user-library-read` |
| Save/remove the current URI | `user-library-modify` |
| Optional trusted local Web Playback SDK engine | `streaming` (requested only when enabled) |

The first slice should request only the scopes required by its enabled
features. Playback mutation is unavailable for some accounts/endpoints and is
documented by Spotify as Premium-only; every 401/403/404/restriction response
must degrade the exact feature without erasing a healthy current-item surface.
Use the current generic `/me/library` APIs rather than deprecated entity-
specific saved-track endpoints. See [Currently Playing](https://developer.spotify.com/documentation/web-api/reference/get-the-users-currently-playing-track),
[Save Items to Library](https://developer.spotify.com/documentation/web-api/reference/save-library-items),
and the endpoint-specific reference for each control.

Later nested surfaces add scopes incrementally when the user enables the
feature: `user-read-recently-played` for history; `playlist-read-private` (and
`playlist-read-collaborative` only if collaborative lists are shown); and
`playlist-modify-private`/`playlist-modify-public` only for explicit editing.
Artist/album catalog metadata and search do not justify profile/email scopes.
Following artists through generic library endpoints requires the applicable
follow scope and stays separate from ordinary library consent.

## Product surfaces and exact Web API subset

The target is as close to Spotify's native information architecture as the Web
API and overlay interaction model permit. It remains one controller widget with
nested SDK input scopes; it is not a WebView or a replacement audio client. Each
surface restores its own stable focus/Scroll position and B returns exactly one
level.

Queue and playlist-detail actions keep the Spotify media URI as semantic
identity. Because Spotify may return the same track or episode more than once,
the widget adds only a bounded collection-context occurrence discriminator for
focus and action routing. Distinguishable occurrences retain their key through
refresh and page churn; otherwise-identical occurrences use the deterministic
nearest equivalent inside the 24-row retained window. This does not alter which
URI Spotify plays or create a provider identity outside the current collection.

| Stage | Nested surface | Current OpenAPI endpoints |
| ---: | --- | --- |
| 1 | Player / Now Playing | `GET /me/player`, `GET /me/player/currently-playing`; `PUT /me/player/play`, `PUT /me/player/pause`, `PUT /me/player/seek`, `PUT /me/player/repeat`, `PUT /me/player/volume`, `PUT /me/player/shuffle`; `POST /me/player/previous`, `POST /me/player/next`; `GET /me/library/contains`, `PUT /me/library`, and `DELETE /me/library` for current-item saved state |
| 2 | Devices and queue | `GET /me/player/devices`, `PUT /me/player` for transfer, `GET /me/player/queue`, `POST /me/player/queue` |
| 3 | Search | `GET /search` for track, album, artist, playlist, show, episode, and audiobook; Development Mode currently caps each type page at 10, so use bounded pagination |
| 4 | Recent | `GET /me/player/recently-played` with cursor paging and `user-read-recently-played` |
| 5 | Library | `GET /me/tracks`, `GET /me/albums`, `GET /me/episodes`, `GET /me/shows`, `GET /me/audiobooks`, and `GET /me/following`; `GET /me/library/contains`, `PUT /me/library`, and `DELETE /me/library` for URI-based membership changes |
| 6 | Playlists | `GET /me/playlists`, `GET /playlists/{id}`, `GET /playlists/{id}/items`; later editing uses `POST /me/playlists`, `PUT /playlists/{id}`, `POST /playlists/{id}/items`, `PUT /playlists/{id}/items`, and `DELETE /playlists/{id}/items` with separate modify scopes |
| 7 | Albums and artists | `GET /albums/{id}`, `GET /albums/{id}/tracks`, `GET /artists/{id}`, and `GET /artists/{id}/albums`; the removed artist-top-tracks endpoint must not be generated or emulated |

This list follows Spotify's February 2026 endpoint changes: playlist contents
use `/items`, playlist creation uses `POST /me/playlists`, library membership
uses generic Spotify-URI endpoints, batch entity fetches are gone, and search
pages are smaller. The pinned official schema remains authoritative if the
reference changes again. See the [February 2026 migration
guide](https://developer.spotify.com/documentation/web-api/tutorials/february-2026-migration-guide).

Stage 1 is the locally testable core, not the definition of the final widget.
Stages 2–7 land behind nested controller surfaces and incremental scopes after
the provider/SDK contract is stable. The Player already uses `UI.Scrubber` so
Left/Right emits coalesced absolute millisecond targets while Up/Down remains
ordinary navigation. Search result and collection tiles should use the public
`Tile` contract plus the existing Picker/ActionSheet
components instead of private layout hacks.

### Playback boundary

The Web API reports and controls playback on Spotify/Spotify Connect clients; it
does not stream audio bytes. The Web API slice must not download, decode, proxy,
cache, mix, broadcast, or synchronize Spotify audio. Start/resume, context/URI
selection, device transfer, queue, seek, and transport commands target a
Spotify Connect device and must show **No active Spotify device** when none is
available.

For actual local playback, the package uses Spotify's Web Playback SDK in one
application-owned singleton WebView2 child process. That child is a narrowly
configured Spotify Connect device, not an arbitrary browsing surface or
product-global web/network authority. It requests the `streaming` scope
only when the user enables local playback, requires an eligible Premium
account, handles activation/autoplay/account/playback error events explicitly,
and keeps access tokens out of DOM logs, worker IPC, navigation URLs, and widget
state. Navigation and resource origins are allowlisted, new-window/download/
external-scheme behavior is blocked, CSP is restrictive, and lifecycle policy
suspends or destroys the engine when it is no longer meant to play. Only one
local Spotify playback host/device may exist per user session.

Spotify documents additional approval requirements for commercial streaming
integrations. Local development success is not permission to ship this playback
mode publicly.

## Community application architecture

The Community application is equivalent to an independent desktop application.
Its manifest requires explicit full-trust approval and honestly receives
ordinary current-user authority; it receives no product capability or special
Spotify host API.

- One package-owned Spotify integration backend owns package identity, OAuth/PKCE,
  the credential vault, access-token refresh/401 replacement, lifecycle, local
  playback, and event publication. It supplies only an exact method/URI/scope/
  body authenticated-request seam to internal playback and collection endpoint
  families. A separate bounded HTTP policy owns retry and retained rate limits;
  a strict response parser owns wire validation and sanitized projection. Those
  endpoint/policy/parser owners have no browser, vault, token, integration-
  session, local-player, or event authority. Transport and parser boundaries
  both enforce the 512 KiB response limit, including injected test transports.
- Pin a reviewed copy/digest of Spotify's [official OpenAPI
  schema](https://developer.spotify.com/reference/web-api/open-api-schema.yaml),
  generate only the endpoint subset used by the provider, and review schema
  diffs before regeneration. Generated DTOs stay provider-internal; the public
  widget SDK exposes smaller stable sanitized contracts.
- Store access/refresh tokens in package-owned use of Windows protected storage
  (Credential Manager and/or DPAPI with publisher/package/user scoping). Tokens
  and token metadata needed for replay never cross widget IPC, snapshots,
  diagnostics, crash reports, or WRSS.
- Authentication and enabled feature scopes remain explicit package policy.
  Revoke/disconnect cancels work and deletes only this package identity's token.
- Current playback reads may use bounded adaptive polling only while Visible or
  Interactive; interpolate progress locally between authoritative snapshots.
  Background/hidden state must stop polling. Controller mutations are
  Interactive or one exact declared dashboard gesture, never ambient.
- Dashboard actions remain ordinary generic widget actions bound to one exact
  snapshot and widget generation; there is no Spotify broker lease.
- Host-owned public configuration is separate from secrets and consent. The
  package-scoped Client ID comes only from Settings; the provider must reject a
  token whose recorded Client ID differs from current configuration.
- Artwork goes through the existing bounded HTTPS image/cache path with
  Spotify-host allowlisting, response limits, cancellation, and no arbitrary
  URL/file escape.
- The optional Web Playback SDK engine is a package-owned singleton process/
  WebView2 host with only the Spotify playback page and token handoff it needs.
  It never exposes raw WebView2 access through the public SDK or product core.

Spotify's application contract remains private to the package. The public SDK
contains only generic application bootstrap, widget lifecycle, semantic tree,
actions, and presentation contracts.

DLV-034 reduced the integration/token owner from 1,724 lines (81,619 bytes) to
1,032 lines (48,862 bytes). The extracted internal owners are the playback plus
collection endpoint boundary (368 lines), HTTP retry/rate-limit policy (103),
and strict response parsing (564). Direct fixtures construct endpoint families
without browser, vault, token session, or local-player dependencies and cover
explicit scopes, concurrent token demand, 401 refresh, 429/backoff, malformed
and oversized responses, and stale client identity. A manually gated backend
fixture also completes a refresh transport after caller cancellation and proves
that its access token and rotated refresh credential are neither published nor
reused; the next request performs a fresh exchange. A credential-free local-host
fixture proves disconnect stops playback, deletes the vault entry, clears the
cached access token, and prevents a later request from reaching Spotify with the
pre-disconnect session. No public product protocol changed.

## Rate limiting and recovery

Spotify calculates an application rate limit over a rolling window. On HTTP
429, honor the `Retry-After` seconds before retrying, coalesce duplicate reads,
cancel stale work, and never spin. Development quota exhaustion can also return
429 with `reason: QUOTA_EXCEEDED`; that is a longer-lived bounded unavailable
state, not an immediate retry. See [Rate
Limits](https://developer.spotify.com/documentation/web-api/concepts/rate-limits).

Token refresh must be single-flight. A refresh failure or revoked grant returns
to an explicit **Connect Spotify** state without discarding unrelated widget
preferences. Optimistic play/like/shuffle/repeat feedback must reconcile with
the next authoritative response and roll back on failure, following the same
stable-focus pattern as YT Music.

The Community application now classifies every playback refresh and poll failure
without inspecting provider messages. Spotify `forbidden`, authorization
expiry/scope loss, and invalid package configuration are fatal presentation
changes: they clear provider-derived data and select the account, reconnect, or
configuration screen. Provider unavailability,
malformed/invalid responses, and unexpected request failures are transient when
a `Ready` revision already exists. They retain the last accepted playback,
route, collection detail, and focus; render one warning containing only a
widget-owned diagnostic code; and move automatic polling through bounded 5,
15, and 30 second delays. Repeated failures saturate at 30 seconds rather than
spinning. Y/manual refresh uses the same classification and can recover
immediately. The next successful authoritative playback response clears the
warning without navigating. Poll work, delay, and late completion remain owned
by the Active widget generation and cannot publish after deactivation.

## Terms, attribution, and product limits

Before any packaged release, review Spotify's current [Developer
Policy](https://developer.spotify.com/policy), [Developer
Terms](https://developer.spotify.com/terms), and [Design & Branding
Guidelines](https://developer.spotify.com/documentation/design).

At minimum:

- attribute Spotify metadata/artwork with the approved Spotify mark;
- link metadata and cover art back to the applicable Spotify content/service;
- do not crop artwork, overlay text/logo on it, download audio, expose preview
  clips as a standalone product, or imply Spotify endorsement;
- use the installed Spotify app as the playback destination rather than
  streaming audio through the overlay; and
- provide required privacy disclosure, disconnect, and deletion behavior.

These are acceptance requirements, not optional visual polish.

## Local acceptance before roadmap closure

1. Generated-client schema pin and deterministic regeneration test.
2. Settings/config tests for missing/malformed Client ID, exact displayed
   `http://127.0.0.1:43827/callback/`, fixed-port collision, no secret field,
   configuration authority isolation, and Client-ID-change token revocation.
   The package store and CLI are implemented; controller UI and runtime-linked
   clear/revoke behavior remain open.
3. PKCE success, denial, mismatched state, wrong path/port/trailing slash,
   late callback, timeout, refresh rotation, revoke, and restart tests with no
   token leakage.
4. Fake-provider widget tests for current item, no playback, no active device,
   Free/Premium restrictions, all controls, library state, 401/403/404/429, and
   stale response reconciliation.
5. Nested-surface tests for player, device transfer, queue, search, recent,
   library, playlists, albums, and artists with B hierarchy, focus restoration,
   paging, incremental scope denial, and removed/unknown response fields.
6. Generic full-trust install/consent/catalog/supervisor evidence proving the
   package declares no product capabilities and returns a valid credential-free
   setup snapshot through ordinary authenticated overlay IPC.
7. Live Development Mode playtest with an allowlisted account, followed by
   controller/lifecycle/resource and attribution screenshots.
8. Keep public distribution explicitly limited until Spotify grants a quota
   mode appropriate for non-allowlisted users.
9. Before enabling local audio, add trusted Web Playback SDK tests for singleton
   ownership, `streaming` scope consent, Premium/account errors, autoplay/user
   activation, origin/navigation/CSP isolation, token non-disclosure, lifecycle
   suspension, device transfer, and resource budgets; obtain any required
   Spotify streaming approval before distribution.
