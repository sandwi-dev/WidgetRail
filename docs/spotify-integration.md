# Spotify Web API integration

Status: **trusted provider, isolated Web Playback host orchestration, and
Community addon 0.2.11 implemented; live WebView2 playback proof remains in
progress as of 2026-08-08**.

The intended product is a separately installable Community addon backed by a
trusted, reusable Spotify provider. The current repository implements the
typed v1 broker surface, a trusted Web API/PKCE provider, protected refresh-token
storage, package configuration storage, local configuration CLI, production
`WidgetBridge` composition, Spotify Community addon package 0.2.11, and a lazily
started isolated WebView2 Web Playback SDK host. The provider owns its process
lifecycle, short-lived streaming-token handoff, safe local-device alias,
transfer, queue, and playlist operations. It does not yet expose a
controller-native text editor or prove live encrypted audio playback in the
supported WebView2 runtime.

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
| Public configuration | `WidgetConfigurationStore` persists bounded non-secret values under `widget-config`, isolated by publisher and package; `gbar config` provides the current local workflow. |
| Broker contract | Implemented capability IDs and strict DTOs for configuration, authorization, playback read/control, and playback-change events. |
| Trusted provider | Implemented PKCE, exact loopback callback, refresh-token vault, player snapshot/control projection, bounded `Retry-After` handling, scope allowlist, and sanitized errors. |
| Native composition | `WidgetBridge` constructs the Windows Spotify provider through the same typed broker used by every widget. |
| Community addon | Version 0.2.11 implements responsive Player, Queue, continuous keyed Queue/Playlist/detail collections, and Devices surfaces through the same public SDK/AppContainer path as third-party addons. Its seek control uses the public `UI.Scrubber` contract and authors Left to the selected responsive rail destination or compact Player tab. |
| Setup UI | Compact controller setup/instructions are implemented with a VerticalScroll, responsive actions, and a fresh Scroll identity on every explicit setup entry; a controller-native Client-ID editor is planned, so the CLI below remains the current testable configuration path. |
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

Package 0.1.7 selects explicit `keep-alive` residency so the bridge cannot idle-
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
broker Connect deadline is seventeen minutes. Its remaining two minutes cover only
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
`PlatformSettings.WidgetConfigurationStore` validates and stores it under the
declared publisher/package configuration identity using the key `client-id`.
An unsigned content-digest runtime may resolve exactly one configuration whose
publisher namespace owns that package ID; ambiguous matches fail closed. This
exception is limited to explicitly non-secret configuration: consent, private
state, OAuth tokens, and credentials remain bound to the exact authenticated
runtime authority. Documents
are bounded, strict, atomic, cross-process locked, and reparse-safe. They are
readable public configuration, so secrets, passwords, credentials, and tokens
do not belong there. OAuth tokens remain in the provider's Windows credential
vault.

Until the controller-native Settings editor lands, the local workflow is:

```powershell
dotnet run --project .\tools\GbarCli\GbarCli.csproj -- config set org.gbar.samples.spotify client-id <spotify-client-id> --publisher org.gbar.samples
dotnet run --project .\tools\GbarCli\GbarCli.csproj -- config get org.gbar.samples.spotify client-id --publisher org.gbar.samples
dotnet run --project .\tools\GbarCli\GbarCli.csproj -- config list org.gbar.samples.spotify --publisher org.gbar.samples
dotnet run --project .\tools\GbarCli\GbarCli.csproj -- config remove org.gbar.samples.spotify client-id --publisher org.gbar.samples
```

`gbar config clear` removes every public configuration value for the selected
publisher/package authority. `--settings-root <path>` is available for isolated
local tests. The CLI rejects secret/password/token/credential-like keys. This
workflow configures the provider used by the overlay. Connecting still requires
an explicit user gesture because it opens Spotify's authorization page.

The current setup/instruction surface and planned native editor together contain
exactly:

- a bounded **Client ID** field;
- the fixed read-only redirect URI
  `http://127.0.0.1:43827/callback/` and exact-registration instruction;
- Connect/Reconnect and Disconnect actions with explicit status; and
- no client-secret field.

Package 0.1.7 places the full instruction card in a controller VerticalScroll,
assigns a fresh Scroll node identity on every explicit setup entry so a prior
bottom offset cannot hide the title/first step, uses compact responsive spacing/
wrapping, and keeps Connect/Setup/Refresh/
Disconnect actions on the shared centered icon-and-label button geometry. These
are layout and navigation fixes; they do not weaken the authorization boundary
or make OAuth start automatically.

PKCE does not require a client secret, and a secret embedded in a desktop app or
Community package would not be confidential. Changing the Client ID through the
implemented provider deletes the old refresh token and clears its cached access
token before saving the new identity. Clearing configuration is currently a
store/CLI operation; runtime integration must connect that mutation to provider
disconnect/token deletion before the controller-native editor is complete. The
host must never reuse tokens across Client IDs, package authorities, or Windows
users.

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
`MediaTile`/`AppTile` contracts plus the existing Picker/ActionSheet
components instead of private layout hacks.

### Playback boundary

The Web API reports and controls playback on Spotify/Spotify Connect clients; it
does not stream audio bytes. The Web API slice must not download, decode, proxy,
cache, mix, broadcast, or synchronize Spotify audio. Start/resume, context/URI
selection, device transfer, queue, seek, and transport commands target a
Spotify Connect device and must show **No active Spotify device** when none is
available.

For actual local playback, the approved later design uses Spotify's Web
Playback SDK in one trusted singleton WebView2 playback host. That host is a
host-owned Spotify Connect device, not a Community worker, arbitrary browsing
surface, or reusable web/network authority. It requests the `streaming` scope
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

## Provider and SDK architecture

The Community addon must be equivalent to an independent developer package. It
must not receive a trusted worker exception or ambient Internet access.

- One trusted Spotify integration backend owns package identity, OAuth/PKCE,
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
- Store access/refresh tokens in host-owned Windows protected storage
  (Credential Manager and/or DPAPI with publisher/package/user scoping). Tokens
  and token metadata needed for replay never cross widget IPC, snapshots,
  diagnostics, crash reports, or GBSS.
- Expose separate read/control/library capability decisions. Authentication is
  not capability consent; both must be valid, and revoke/disconnect cancels
  work and deletes only this provider's scoped tokens.
- Current playback reads may use bounded adaptive polling only while Visible or
  Interactive; interpolate progress locally between authoritative snapshots.
  Background/hidden state must stop polling. Controller mutations are
  Interactive or one exact declared dashboard gesture, never ambient.
- Dashboard X/LB/RB-style actions remain bound to one exact capability
  operation, snapshot, widget generation, and short-lived broker lease.
- Host-owned public configuration is separate from secrets and consent. The
  package-scoped Client ID comes only from Settings; the provider must reject a
  token whose recorded Client ID differs from current configuration.
- Artwork goes through the existing bounded HTTPS image/cache path with
  Spotify-host allowlisting, response limits, cancellation, and no arbitrary
  URL/file escape.
- The optional Web Playback SDK engine is a separate trusted singleton process/
  WebView2 host with only the Spotify playback page and token handoff it needs.
  It never expands the Community addon's capabilities or exposes raw WebView2
  access through the SDK.

The public SDK shape is not frozen until provider contract tests prove auth
loss, refresh rotation, scope denial, no active device, restrictions, stale
responses, and rate limiting without leaking Spotify wire models.

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
pre-disconnect session. No public provider/broker protocol changed.

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

The Community widget now classifies every playback refresh and poll failure
without inspecting provider messages. `permission_denied`/`capability_revoked`,
authorization expiry/scope loss, and incompatible capability declarations are
fatal presentation changes: they clear provider-derived data and select the
permission, reconnect, or compatibility screen. Provider unavailability,
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
6. AppContainer package/permission conformance proving the Community addon has
   no direct Internet or secret access.
7. Live Development Mode playtest with an allowlisted account, followed by
   controller/lifecycle/resource and attribution screenshots.
8. Keep public distribution explicitly limited until Spotify grants a quota
   mode appropriate for non-allowlisted users.
9. Before enabling local audio, add trusted Web Playback SDK tests for singleton
   ownership, `streaming` scope consent, Premium/account errors, autoplay/user
   activation, origin/navigation/CSP isolation, token non-disclosure, lifecycle
   suspension, device transfer, and resource budgets; obtain any required
   Spotify streaming approval before distribution.
