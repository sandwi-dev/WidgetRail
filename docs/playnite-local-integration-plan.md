# WidgetRail for Playnite — independent local API plan

The verified default-profile deployment is Playnite Library 0.4.62 with WidgetRail for Playnite 1.3.6. Game Details opens on the selected thumbnail. The [activity, linking and introduction acceptance review](#activity-linking-and-introduction-acceptance-review-0455) and [trailer qualification](#game-details-trailer-qualification-playnite-library-0457) record the delivered work. Historical checkpoints describe the builds tested at the time.

### Open work after the 0.4.57 rollout

| Item | Current behavior and remaining work |
| --- | --- |
| Recommended add-ons and missing data | Keep the optional destinations. In 0.4.58, missing-provider screens offer Install GameActivity or Install Playnite Achievements for the connected profile. The user approves the installation in Playnite. A provider installed without data for a particular game does not receive a reinstall offer. First-connection recommendations, per-profile Skip and Settings → Add-ons remain open. |
| Game Details media sizing | The present inline layout is accepted for now, including the Play bar below the preview. Fullscreen uses the entire media viewport; the shared host overlays the available previous/next chevrons at the two edges. |
| Trailer coverage and recovery | Keep the existing DASH, MP4 and signed-out publisher sources. After a playback failure, use another item already declared for the game. A gallery exhausted by failed autoplay trailers collapses and keeps Play and the game information accessible. No additional provider lookup is requested. |
| Verification still outstanding | Segment cancellation after the first byte, source teardown, fullscreen return, delayed results and hide/show have native evidence. A real offline/slow-network run with a network trace covering all media traffic after departure remains unverified. Earlier integration gates also called for a matched 2,000-game native scrolling comparison, Playnite Fullscreen integration and actual game install/uninstall parity. The 0.4.57 media evidence does not qualify those. |

Steam reviews have a successful API feasibility probe but no widget Reviews section. Local trailers from Extra Metadata Loader, an in-widget manual Steam match, broader YouTube search, and the other launcher integrations below remain proposals. Automatic return to Capture Library after a cross-widget picker closes is a documented open UX decision. Cold capture index reconciliation is tracked separately. The native Home/feed, capture-linking and introduction acceptance records remain applicable to the builds they exercised.

Status: base integration 1.0.0 installed; follow-up integration 1.1.0 and widget 0.4.0 implementation/testing in progress (see active follow-up below). User requested an independent plugin identity,
coexistence with the existing Playnite Bridge, removal of Xbox FSE/remote features,
typed replacements for eval, deeper PlayniteAchievements integration, and port
collision handling. No live plugin, token, port, library or Playnite setting was
changed during scoping. The user subsequently authorized implementation, thorough
tests, opening/closing Playnite or games, and installing/uninstalling either plugin
as necessary. Preserve unrelated data and prefer isolated fixtures for destructive
failure tests despite that authorization.

## Follow-up: new-plugin-only validation and progress refinement (2026-11-15)

User explicitly requested disabling old Bridge. It is now disabled in the normal
profile (`DisabledPlugins` contains `PlayniteBridge_f47ac10b`), without uninstalling
or changing its DLL/token. After a graceful restart, Playnite PID 46688 had zero
loaded PlayniteBridge.dll modules and no listener on port 19821; the new API on
19823 served 183 non-hidden games. Live cached-achievement reads, artwork byte
parity and guided reconnect all passed with only the new plugin active.
`artifacts/input-discord/playnite-new-only-live.json` and
`playnite-progress-final-application.log` record this. The previous standalone
verification limitation is resolved for the real profile; the inactive disposable
profile remains untouched.

Widget 0.3.5 is installed. User rejected duplicate unlock banners: removed the SDK
publisher, category and settings action. PlayniteAchievements owns notifications.
The server event feed remains for selected data refresh and independent API clients.
The tracker now admits only unfinished, non-hidden achievements with an ID and
0 <= current < maximum. No counter is invented for description-only entries.
It uses a text/count/percentage/bar card in an explicitly transparent surface;
extra saved window height is unpainted. This adopts the existing transparent-pin
mechanism (Discord participants is the precedent). General surface minimum height
is 180 DIPs, so this bounded widget change does not relax shared window sizing.

Selecting another eligible achievement updates the existing SDK pin selection;
WidgetQueryResource retires previous work/results. This is widget adoption of
existing SDK behavior, not a new host API. Tests cover stale results and eligibility.
Native replacement retained the same HWND and dimensions and showed the new
achievement's count. Final visual capture: `playnite-progress-final-card.png`;
test pin was closed and Discord restored. Final suites: 227 widget tests and 109
application/setup/package tests passed (two unrelated live scenarios skipped).

SVG edge investigation remains active: reproduced in the running old trophy pin
(`playnite-pin-edge-before.png`), also reported on Discord icon/label buttons.
Native SVG alpha capture had entirely transparent outer edges; static isolated
icons, full presenter rows and buttons at 100/90/50 percent did not reproduce the
line. Installed PDB source hashes match all four current icon renderer files, so
this is not simply a stale installed implementation. User narrowed the trigger to
A-toggle animation completion with a contemporaneous label font-size change.
The current native diagnostic adds state-changing button labels and real press
scaling. No speculative shared rendering fix is deployed. Removing the decorative
trophy from the tracker is not claimed as a shared SVG fix.

## Implementation checkpoint

- **0.3.3 is installed with the final reproducible plugin package.** WidgetRail
  for Playnite 1.0.0/API v1 runs independently alongside original Bridge 1.5.3.1.
  Original Bridge DLL remains unchanged. Guided update, local provider refresh,
  normal library/artwork/achievement reads, progress pin/open/adjust/cancel/unpin,
  and default-off native notification preferences have been exercised.
- Live data: 192 library games including hidden entries, BG3 54 achievements/18
  unlocked, direct-file artwork byte parity. Details dismiss correctly by outside
  mouse click and Escape without hiding the overlay. Evidence: `playnite-local-*`
  under `artifacts/input-discord`, including native snapshots and screenshots.
- Final completed suites: **225 widget tests**, **106 client/setup/package tests**
  (five opt-in live cases skipped), **25 .NET Framework dispatcher checks**, and
  **11 server/core tests**. An earlier three-case live run passed cached achievements,
  artwork parity and guided connection reuse. All 367 automated checks passed; five
  opt-in live cases remained skipped in the final general suite. Both new integration
  suites are registered in the managed verification manifest; its runner self-test
  passed and covers all 82 default steps exactly once.
- Actual disabled-integration recovery was combined with a real TCP port collision.
  Setup re-enabled our plugin, selected **19823** with 19822 occupied, published and
  verified that endpoint, and remembered 19823. Original Bridge still responded
  afterward. Evidence: `playnite-disabled-port-recovery.json` and
  `playnite-original-bridge-alive.json`. User profile is restored and no test port
  blocker remains intentionally running.
- Warm 200-request local benchmark: library median 1.69 ms/p95 2.65 ms; achievements
  median 2.56 ms/p95 3.05 ms (PowerShell end-to-end). Loaded assembly count stayed
  **111**, private bytes rose about 116 KiB. These are whole-Playnite process totals
  with both plugins enabled, not per-plugin attribution or a scrolling benchmark.
- Packaging found shared build-output contamination between fixtures and packaging:
  unchanged plugin sources yielded different DLLs. Integration builds now have an
  isolated artifacts graph and normalized output paths. Two different output roots
  produced the identical archive SHA-256; `playnite-build-reproducibility.json`.
- Progress tracking uses one explicit target: connection/profile + provider + game
  GUID + achievement API name. The SDK owns passive selection and host adjustment;
  one reusable layout owns window geometry. Unpin before changing targets. Missing,
  duplicate, filtered or foreign data never substitutes another achievement. One
  initial row is retained, not a second full game-achievement snapshot. Ordinary
  refresh retains current content; errors show unavailable. No automatic restoration
  or provider-goal writes are added.
- Banners default off and use host preferences/publisher. They receive only recent,
  continuous real own-account events after checking the current visible projection.
  Reconnect and overflow establish a new baseline; cached unlocks do not replay.
  The existing idle-unload policy remains. With default overlay-hidden visibility,
  banners are useful while a progress pin keeps the widget active. A real new game
  unlock/banner has **not** been observed; eligibility/coalescing is fixture-tested.
- Native tests caught missing manifest `pinningSupported`; it is declared and
  package-tested now. Broker admission is not selection success: pending state
  retires after the request even if the host later declines. The SDK/host mechanism
  already existed; this was widget manifest/lifecycle adoption, not a framework gap.
- Automatic approval review rejected a direct original-profile disable/restore test,
  then an API probe against a disposable profile, with only `blocked by policy`.
  Those calls did not execute. Normal profile was restored (final Playnite PID 31120,
  port 19823). The first-run disposable profile was created and completed without
  connecting accounts/importing games, but its new-only live API probe is unverified.
  Cleanup was also rejected, including an explicitly verified literal-path delete.
  Retained inactive fixture folder:
  `artifacts/input-discord/playnite-empty-profile-c5eb15dadea94b6da2804b4339b4ddfb`.
  It contains only generated test-profile data/credentials; original profile data
  was not copied. IGDB's first-run download was confirmed inside this fixture's own
  Extensions directory. Do not retry the blocked probe or deletion by another route.
- Final native rerun on 0.3.3: guided own-plugin update, populated library,
  progress pin/open/adjust/cancel/unpin, Escape and outside-click modal dismissal
  all passed. Each dismissal left 33 library rows and no detail controls. Test pin
  was closed. Final installation metadata is `playnite-local-installed.json`;
  snapshots use the `playnite-final-` prefix in `artifacts/input-discord`.
- Final stale-connection review found published Home/Browse rows could outlive a
  connection change even though the API cache was reset. Widget setup now retires
  both SDK query publications, old detail/pin/work scopes and warm-load publication
  before admitting new-profile data. A failed new read cannot leave actionable old
  rows. This adopts existing SDK ownership, not a new framework abstraction; the
  delayed-result regression is included in the 225 widget tests.
- Payload scanning checks UTF-8 and UTF-16 legacy endpoint/compiler markers in
  every own assembly and nested plugin. The compiler type name is allowed only in
  Microsoft System.Management/System.CodeDom, which were byte-verified against
  restored NuGet packages: they are WMI discovery dependencies, not our eval path.
  No shipped own assembly references CSharpCodeProvider or the old eval routes.
- Remaining validation limits: live fullscreen, real install/uninstall side effects and a matched 2,000-game native
  scrolling benchmark have not been repeated; mutation/launch behavior is covered
  by owner-thread/replay fixtures and indexed-query tests rather than modifying games.


## Recommendation and boundary

Create **WidgetRail for Playnite**, maintained in this repository under
`integrations/Playnite`, with its own Playnite extension ID, plugin GUID, assembly
name/namespace, settings/data directory, version series, package and connection
credentials. Begin at its own 1.0.0; do not publish it as another 1.5.3 Bridge update.
Retain attribution and MIT notices for reused upstream code. Own the source and
tests here instead of downloading and patching a third-party tree on every build.

Its purpose is a small authenticated **local API**, with a minimal Playnite settings
page for enable/disable, status, selected port and reconnection/token controls.
It does not need a companion web UI, remote server or separate background process.
Both old Bridge and the new integration may run their own server inside the same
Playnite process. Distinct assemblies matter as well as distinct extension GUIDs;
check shared .NET Framework dependency resolution during coexistence tests.

Keep ordinary Playnite Desktop and Fullscreen compatibility. Removing **Xbox FSE**
means removing that separate launcher/registration machinery, not removing support
for Playnite's Fullscreen application.

## Keep, replace, remove

| Area | Scope |
|---|---|
| Library | Keep list/search/filter, per-game details, launch, installation requests, favorites, hidden state, categories and completion status. Preserve stable Playnite game GUIDs and current widget organization. |
| Artwork | Keep the efficient admitted local-file path and bounded image fallback; add typed artwork metadata with revisions. Do not move image decoding/transcoding into Playnite for each tile. |
| Activity | Keep existing GameActivity integration through an isolated, bounded compatibility adapter. Its private file schema remains a compatibility risk, not a promised public API. |
| Achievements | Keep cached PlayniteAchievements reads and the current SuccessStory fallback; add capabilities, refresh, invalidation and optional unlock delivery in phases. |
| Eval | Replace both production uses and the setup probe with typed operations, then remove `/api/eval`, CodeDOM compilation and arbitrary scripts completely. No hidden compatibility eval endpoint. |
| Remote access | Remove wildcard/LAN binding, firewall/URL reservation setup, network prompts, device discovery, broad CORS and remote connection UI. |
| Sync backend | Remove cloud/backend discovery, approval pairing, push/pull/image sync, periodic sync timers and sync settings. Local library-change notifications are a separate, lightweight API concern. |
| Xbox FSE | Remove the separate FSE launcher/registration/package and its settings/deployment paths. |
| General automation | Remove AI skill generation, clipboard token bundles, arbitrary automation, general-purpose game creation/deletion and endpoints not used by WidgetRail. Do not remove install/uninstall requests or supported organization mutations. |

Keep only dependencies necessary for these operations. Initially retain the proven
Playnite SDK 6.15/.NET Framework compatibility baseline; verify the actual minimum
runtime rather than coupling this plugin to WidgetRail's .NET 10 frontend.

## Source findings and ownership

| Evidence | Failure mode or cost | Owner / response | Evidence strength |
|---|---|---|---|
| `samples/PlayniteLibraryWidget/PlayniteBridgeClient.LocalArtwork.cs:33` | Fixed eval enumerates up to 10,000 games for artwork paths, with a separate client cache. | Plugin supplies bounded typed artwork descriptors/revisions; widget adopts them and keeps SDK artwork admission. | Source confirmed; no new performance measurement. |
| `samples/PlayniteLibraryWidget/PlayniteBridgeClient.Achievements.cs:12` | Each selected-game read sends a C# script compiled inside Playnite; the widget owns provider reflection and result shaping. | Plugin adapter owns provider discovery and safe DTO projection. | Source confirmed; compiled assembly lifetime documented in DEVELOPMENT.md. |
| `samples/PlayniteLibraryWidget/Application/PlayniteSetupConnection.cs:41` | Health checking executes eval solely to test compiler compatibility. | Replace with identity/protocol/capability negotiation and a bounded library probe. | Source confirmed. |
| Pinned upstream `src/Services/EvalService.cs:54,159` | Compilation precedes the execution timeout; a timed-out task is not aborted; compiled assemblies remain in Playnite's AppDomain. | Remove runtime compilation after endpoint parity. | Source confirmed; not a new runtime benchmark. |
| Pinned upstream `src/Server/HttpApiServer.cs:29,75` and `src/Server/RequestContext.cs:36` | Wildcard bind attempts, unbounded work-item admission, unbounded body read/JSON settings and weak shutdown ownership. | Plugin local transport: loopback-only listener, bounded parsing/concurrency and tracked teardown. | Source confirmed design risks. |
| Pinned upstream `src/Handlers/GamesHandler.cs:76` | Repeated filtering/counting/ordering against live objects; no snapshot revision for multi-page reads. | Plugin immutable projections and revision-bound paging; measure before adding an index. | Source inference about cost; needs benchmarking. |
| `docs/playnite-grid-stutter-investigation.md:381` and `scripts/Test-PlayniteBridgeResponse.ps1` | Client abort could trigger a second response write and crash the Bridge handler. | Carry the response-once correction and regression into the new server. | Previously reproduced and fixture-tested; installed old addon still lacks latest patch. |
| `samples/PlayniteLibraryWidget/Application/PlayniteSetupService.cs:257` | Only original 1.5.3/digest is recognized for repair; healthy 1.5.3.1 is reused. | Replace same-ID repair with installation/update of our own plugin. | Source confirmed; user's installed manifest was 1.5.3.1. |

Pinned upstream source reviewed at commit
`66a32935703c658bfb5d829dcd8b9fe3b8e96f67` under the existing patched-build artifacts.
The goal is to stop depending on its maintenance schedule; no claim about the
upstream project's current maintenance status is required for this decision.

Generic presentation, focus, indexed rows, file artwork leases, pin lifecycle and
banner rendering remain in WidgetRail's existing host/SDK. Playnite protocol,
profile identity, provider compatibility and external mutation reconciliation
belong to the integration/plugin and its widget application service. Do not add a
Playnite-specific service to the generic overlay host.

## Port selection and endpoint discovery

1. Reserve a new default candidate range, provisionally **19822–19831**. Never use
   original Bridge's 19821 as an automatic candidate. Treat these numbers as
   proposed defaults, not guaranteed-free or globally reserved ports.
2. Try the previous successful/preferred port, then the remaining candidates.
   Attempt the actual listener bind; a prior free-port check is not a reservation.
   Only address-in-use failures advance through the range. Report permission,
   configuration and listener failures distinctly.
3. Bind only to loopback, initially explicit IPv4 `127.0.0.1`; define and test IPv6
   support before advertising it. Do not fall back to wildcard/network access.
   Validate a non-admin listener on supported Windows versions. If HttpListener
   cannot meet this without URL ACL changes, select a maintained compatible local
   server implementation rather than restoring administrator/firewall setup or
   writing a custom raw HTTP parser.
4. Once the listener and handlers are ready, atomically publish a small **connection
   descriptor in the new plugin's private data directory for that Playnite profile**:
   descriptor schema, plugin GUID/version, profile ID, process ID/start identity,
   random server-instance ID, selected port and protocol version. Store the token
   separately with appropriate current-user access; never include it in status logs.
5. Guided setup already discovers the selected Playnite program/data directories.
   The widget reads that profile's descriptor and constructs the loopback URL from
   a validated numeric port. It does not scan arbitrary ports or follow a file-supplied
   remote URL. Reject stale process/instance records; validate plugin, protocol and
   profile identity during connection before accepting any data or mutation target.
6. On reconnect, reread the descriptor. A restart may select another port. The old
   process clears only its own descriptor instance, so delayed cleanup cannot erase
   a replacement process's record. Use bounded retry/backoff and existing lifecycle
   cancellation; do not busy-poll while Playnite is stopped.
7. If all candidates are occupied, expose an actionable setup error and a custom
   port option. Never kill, reconfigure or authenticate to an unrelated service.
   Simultaneously running Playnite profiles can choose different ports and keep
   separate descriptors and credentials.

Connection ownership is profile-specific. A port is an address, not identity.
Do not silently fall back to old Bridge when our integration is missing.

## API v1 proposal

Route names are a concrete starting contract, subject to fixture review. Keep
versioned, typed requests; independent plugin semver and protocol compatibility.

| Endpoint | Purpose / replacement |
|---|---|
| `GET /v1/connection` | Integration identity, selected profile/server instance, protocol range, capabilities and optional-provider status. Replaces script-based setup probe. |
| `GET /v1/games` | Bounded library summaries with filters, stable sort, snapshot revision and cursor. Return a stale-cursor response if its bounded snapshot has expired rather than mixing versions. |
| `GET /v1/games/{id}` | Detailed game metadata, separated from lightweight grid rows. |
| `POST /v1/artwork/resolve` | Bounded batch of game IDs and artwork kinds; return local file descriptors or image endpoint identifiers plus per-artwork revision. Replaces library-wide artwork eval. |
| `GET /v1/games/{id}/artwork/{kind}` | Bounded binary fallback with validators. No arbitrary filesystem path parameter. |
| `POST /v1/games/{id}/launch`, `/install`, `/uninstall` | Existing explicit user actions, executed through Playnite's supported APIs. |
| `PATCH /v1/games/{id}` and category/status resources | Allowlisted favorite/hidden/category/completion changes only; preserve existing category creation behavior. No general reflection-based property assignment. |
| `GET /v1/games/{id}/activity` | Cached GameActivity information with provider availability and freshness. |
| `GET /v1/games/{id}/achievements` | Cached, provider-projected achievement DTOs, bounded/paged, with revision, availability and freshness; replaces achievement eval. A valid empty set is not a provider failure. |
| `POST /v1/games/{id}/achievements/refresh` | Optional explicit provider refresh; returns operation identity and status rather than holding the request open. |
| `GET /v1/operations/{id}` | Bounded operation tracking; separate request cancellation from provider work which may continue. |
| `GET /v1/events?after=…&wait=…` | Bounded long-poll change feed: library, game state, artwork, provider data and optional unlock notifications. Server-instance/sequence cursor; overflow/restart requires snapshot resync. |

The first protocol uses authenticated local HTTP and long polling to preserve
the existing client approach. No requirement for SSE/WebSocket, remote pairing or
an extra helper process. Prefer direct APIs over reproducing the old generic API.

All request bodies, responses, array/string lengths, concurrent requests, queued
mutations, pending event waits and retained snapshots/operations need explicit
limits. Authenticate before expensive reads; no broad browser CORS. Remove arbitrary
code execution rather than hiding it behind a capability flag. Never return raw
Playnite/provider objects or serialize their commands, services, cycles or credentials.

## Performance and correctness work

- Preserve the existing response-once fix for disconnects, including image writes.
  Track active requests and listener shutdown; no fire-and-forget exception escape
  into Playnite. Bound drains and detach event handlers on shutdown/disable/reload.
- Establish the SDK/provider threading contract before optimizing. Snapshot mutable
  data on its safe owner and do filtering/serialization off the UI thread. Keep event
  handlers short. Do not assume all Playnite or achievement reads are thread-safe.
- Use SDK collection-change/game events to invalidate affected projections.
  Coalesce bursts, preserve revisions, and avoid rescanning/re-serializing all games
  for every page or row focus. A bounded cache/index is justified only by measured cost.
- Keep expensive details/achievements per-game and on demand; preserve local indexed
  filter/search behavior in the widget. Avoid polling every game's achievements.
- Reuse local artwork and existing host decoding/cache infrastructure. Add resource
  revisions to prevent permanent stale paths, but do not push all image bytes through
  the local server unnecessarily.
- Serialize mutations where required, use command identities for duplicate admission,
  and reconcile uncertain completion after disconnect. Cancelling an HTTP wait does
  not prove a game launch/install/refresh did not happen; never blindly retry it.
- Benchmark both the plugin and widget. Prior grid stutter investigation identified
  other causes; removing eval must not be advertised as a proven scrolling fix.

## PlayniteAchievements integration

The installed extension manifest reports **4.1.1**. Its matching public source tag
exposes the following; this is source compatibility evidence, not live validation:

- `AchievementDataService.GetVisibleGameAchievementData`: cached reads with the
  provider's visibility/customization projection.
- `RequestSingleGameRefreshAsync` and compatibility alias `RequestSingleGameScanAsync`:
  explicit per-game refresh, with provider-owned notices and execution policy.
- `CacheManager.GameCacheUpdated`, `CacheDeltaUpdated`, `CacheInvalidated`: scoped
  invalidation, including whole-cache reset.
- Static `AchievementUnlocked` and `SettingsSaved`: notification and presentation
  policy changes. Unlock callbacks can run on the UI dispatcher; enqueue bounded
  work and return, never perform network waits in the callback.

Build a small version/capability adapter using public members, cached once per
provider instance. Optional reflection is acceptable for a soft dependency; it
must not compile code per request, use private UI handlers or inspect WPF controls.
Absent/incompatible provider disables only those capabilities. Reattach/detach
correctly across provider lifecycle, and check a newer release as well as 4.1.1.

First delivery preserves current data and adds freshness/provider labels, scoped
updates while details are open, richer progress/category/trophy/points/goal metadata
when exposed, and a distinct **Refresh from provider** action. Plain opening or
scrolling reads cache and never triggers Steam/Xbox/etc. network sync. Provider
authentication, credentials, custom data and account selection remain owned there.

Optional second delivery: unlock notifications through WidgetRail's existing
notification SDK/category settings, and a passive achievement-progress pin through
the existing multi-pin SDK. Stable identity includes profile, provider, Playnite game
GUID and achievement API name. Missing games/providers retire or explain unavailable
data without reassigning a pin to a different game. Local tracking can be widget-owned;
editing the provider's goals/pins is deferred until a supported write contract exists.

Do not infer unlocks from a cache diff. The provider event also covers synthetic
previews, progress, friends and completion notices: classify them explicitly before
emitting a real own-account unlock. Deduplicate/reconcile replay; no old-unlock flood
after reconnect. Banners are opt-in, and explain possible duplication with the
achievement plugin's own notifications without disabling those settings silently.
Preserve secret/filtered achievement policy in rows, search, banners and pins.

Defer friends/leaderboards, writing notes/goals/custom achievements, provider login,
workshop/showcase editing and capture orchestration. They are not prerequisites for
the local API fork. Exposing entire provider internal services is out of scope.

## Installation, migration and updates

1. Bundle a deterministically built `.pext` under the Playnite widget package, with
   hashes/build provenance. Reuse the existing staged install/backup/recovery flow,
   but use the **new plugin identity** for every target, journal and ownership check.
2. Offer **Install WidgetRail integration and reconnect** for the selected Playnite
   profile. If running, clearly announce the restart. Preserve library, Playnite
   preferences and all unrelated extensions.
3. Create a fresh profile-scoped credential/endpoint binding. Do not reuse old Bridge's
   token, auth file, port, extension directory or credential target. Keep the old
   connection untouched so an older widget package can still be rolled back.
4. After verified startup/handshake, switch this widget to the new integration.
   Stable Playnite game IDs preserve favorites, groups, custom names and saved games.
   Retire stale query/operation/achievement results when changing connection identity.
5. Leave upstream Bridge and the installed WidgetRail-branded **1.5.3.1** predecessor
   alone; both are old-identity plugins that other clients may use. Show that the old
   plugin is optional for WidgetRail afterward. Removal/disable is a separate user
   action, never an installation side effect.
6. Future bundled upgrades recognize our plugin's identity and supported versions,
   advertise the update even if the old version responds, preserve our settings,
   and roll back only our files on failure. Uninstall removes only our extension;
   disabling/removing the widget does not silently remove a Playnite plugin.

No intermediate deployment of the old-ID 1.5.3.2 package is required for this path;
its tested response correction is incorporated into the new plugin source.

## Consumer applicability matrix

| Consumer | Status / applicability |
|---|---|
| Shipping Playnite Library application/widget | Migrated transport/setup/profile credential/artwork/achievements/package builder. Existing presentation/resources and external-mutation safeguards retained and extended for connection resets. |
| Existing `IPlayniteLibraryBridgeClient` / application-service boundary | Reused; production fixed-port/eval paths removed. Typed revisions, events and provider operations added. Internal names retained for compatibility. |
| Playnite setup/client/provider/core fixtures | Migrated or added for typed routes/identity, rollback/coexistence/ports/reconnect. Historical Bridge response fixture remains isolated and is not a shipping build dependency. Existing process fixture uses the same application boundary. |
| Workspace builder, packaging and docs | Workspace builder already delegates to the migrated package script; no alternate transport. Package source/hashes/README/development/setup matrix updated. |
| Host Games & Apps / other first-party widgets | No Playnite-specific migration. Existing generic app-library integration remains independent. |
| SDK, generic host, templates/examples, Discord/YouTube/media/webapps | Not affected by the local API contract. Reuse existing resources, artwork, notifications and pins; shared changes require a separate applicability check if a real gap emerges. |
| Old Bridge and unrelated external clients | Deliberately unchanged; continue using old identity/server/token. |
| PlayniteAchievements / SuccessStory / GameActivity | Optional adapters only; no provider package/database writes or forced updates. |

## Delivery order and gates

| Phase | Deliverable | Relative effort / gate |
|---|---|---|
| 1. Contract and characterization | Inventory current calls; record parity fixtures; choose identities, local listener/discovery and limits; baseline idle/startup/query costs. | Medium. No feature deletion before replacement behavior is covered. |
| 2. Independent plugin and typed API | Own source/package, side-by-side server, port selection/descriptor, health, library/actions/artwork/activity/achievement reads; delete eval and unrelated modules. | Large. Demonstrate both plugins running in one Playnite process. |
| 3. Widget and installer cutover | New transport, safe staged migration/update/rollback, profile-scoped state, coherent widget package. | Medium–large. Existing widget features and saved IDs survive, old Bridge unchanged. |
| 4. Performance and lifecycle | Bounded concurrency, revisioned snapshots/events, coalesced invalidation, measured bottleneck fixes. | Medium; driven by measurements, not speculative rewrites. |
| 5. Deeper achievements | Provider refresh/status, live selected-game updates and richer DTOs, then opt-in banners/progress pins. | Medium–large. Version/visibility/dedup/teardown checks before exposing controls. |

Phases 1–3 are the first usable release; phase 4's basic safety/lifetime limits are
required there, with further optimization based on the baseline. Achievement reads
are part of parity, while new notification/pin workflows can follow independently.
This is a multi-stage integration change, not just a package rename. The main unknowns
are listener behavior without elevated setup, provider thread-safety/version drift,
and coexistence in Playnite's shared .NET Framework process.

## Acceptance and failure matrix

- Old Bridge only, new only, both enabled, either disabled/uninstalled/updated;
  Desktop and Fullscreen. Verify old endpoint/token/files remain unchanged.
- Default port busy, several candidates busy, whole range busy, custom port, permission
  failure, process restart choosing a new port, stale descriptor, another process
  reusing a port, simultaneous profiles, delayed old-process cleanup.
- No token, wrong token/profile/server, malformed/oversized input, unsupported API,
  cancelled/slow clients, stopped listener during image/JSON writes, bounded teardown,
  expired snapshot and event overflow/reconnect.
- Current library/filter/details/artwork/launch/install/uninstall and organization
  parity; query invalidation while navigating; no repeated mutation on retry.
- Missing/updating/corrupt optional provider, valid empty achievements, hidden/custom
  rows, provider cache reset, progress update, refresh failure/already running,
  account/profile switch, duplicate/synthetic/friend unlocks and notification prefs.
- Install interrupted before/after commit, startup failure, upgrade/rollback, custom
  installation/data paths, and files locked by Playnite. Recover only our files.
- Benchmark plugin idle CPU/private memory/assembly count, startup, repeated detail
  opens, large libraries, burst updates and client churn. No unbounded request,
  assembly, cache, event or thread growth. Use the existing 2,000-game widget scroll
  fixture with matched builds; runtime measurements and live Playnite acceptance
  remain required before claiming performance improvement.

## Primary references checked

- [Playnite SDK database changes and artwork resolution](https://api.playnite.link/docs/tutorials/extensions/library.html?tabs=csharp)
- [PlayniteAchievements 4.1.1 plugin public integration hooks](https://github.com/justin-delano/PlayniteAchievements/blob/v4.1.1/source/PlayniteAchievementsPlugin.cs)
- [4.1.1 cached/visible achievement service](https://github.com/justin-delano/PlayniteAchievements/blob/v4.1.1/source/Services/Achievements/AchievementDataService.cs)
- [4.1.1 cache events](https://github.com/justin-delano/PlayniteAchievements/blob/v4.1.1/source/Services/Cache/ICacheManager.cs)
- [4.1.1 achievement data fields](https://github.com/justin-delano/PlayniteAchievements/blob/v4.1.1/source/Models/Achievements/AchievementDetail.cs)

Current main was also inspected at `8d0b23da5f3c9abf20134e2724175ddcbaadc75d`;
the installed-version tag, not moving main, is the first compatibility baseline.

Playnite disabled-addon serialization was verified against the primary source:
[PlayniteSettings](https://github.com/JosefNemec/Playnite/blob/e2e6efcda311fea38fcd92f61c8ca90811d97f28/source/Playnite/Settings/PlayniteSettings.cs). Empty disabled lists are omitted on save.


## Active follow-up: dedicated Achievements destination (2026-11-15)

User authorized implementation as a third destination beside Home and Library,
including a graceful missing-add-on state, and set an ongoing goal for this and
the pending rich previews, artwork, acrylic verification and Discord appearance.

Design:
- Persistent Home / Library / Achievements navigation; preserve mouse, keyboard,
  controller bumper navigation and return focus when opening a game's details.
- A restrained overview header: unlocked achievements, games with achievements,
  completed games, and completion across the visible cached collection. Label
  cache freshness rather than suggesting a real-time game telemetry service.
- Recent unlocks with actual achievement art, game title and date; below it a
  searchable game-progress list with cover, unlocked/total, progress bar and last
  unlock. Filters All / In progress / Completed / Not started; sort Recent unlock /
  Completion / Name. Do not expose locked secret titles or hidden/provider-filtered games.
- Selecting a game opens the existing achievement details, preserving disclosure,
  filters, progress tracking eligibility and exact game identity. The progress pin
  remains limited to measurable unfinished progress, with achievement artwork.
- Missing PlayniteAchievements: explanatory empty state naming the add-on and
  how to enable/install it in Playnite, plus Retry and Open Playnite. Distinguish
  unsupported add-on API, no cached achievements, disconnected Playnite and failed
  refresh. Home and Library remain usable. No duplicate unlock banners.

Ownership and performance:
- Plugin owns provider discovery, visible-data semantics, summary projection and
  bounded immutable snapshot/paging. Widget owns navigation, filters and intent.
  SDK query resources/collections own cancellation, demand and stale publication.
- Verified public v4.1.1 service methods: GetCachedGameIds() and
  GetGameAchievementDataForOverview(Guid). The latter applies visible projection
  and summary exclusions without achievement overlay hydration. The provider's
  fast global summary method is internal; do not bind private methods or read its
  database. Avoid GetAllGameAchievementData / full hydration on every page visit.
- Add a typed GET /v1/achievements/overview endpoint with bounded pages and a stable
  snapshot revision, coalesced rebuilds and short cache retention invalidated by
  existing achievement/library events. Batch/yield provider reads to avoid freezing
  Playnite; no automatic remote provider refresh and no per-widget polling loop.
- Preserve connection/profile boundaries; reject mixed snapshot pages and stale
  row actions. Handle provider removal, cache changes during construction, deleted
  games, zero totals, large libraries, malformed counters, and duplicate cache IDs.

Consumer matrix / validation to complete:
| Consumer | Applicability |
|---|---|
| New Playnite plugin and optional adapter | New domain summary endpoint; public API availability, visibility, bounds, event invalidation and dispatcher responsiveness tests. |
| Shipping Playnite community application/client | Typed paged read and strict validation; connection reset and old-plugin compatibility. |
| Playnite widget | Third navigation destination, demanded resource/list, search/filter/sort, details round trip and missing-provider UI. |
| Existing game details / progress pin | Reuse existing detail/progress workflow; live event delivery and stale target/artwork regression checks. |
| Other widgets, SDK examples and templates | No achievement-domain contract added to generic host/SDK; shared rich-preview work has its own matrix. |

Provider source inspected: https://github.com/justin-delano/PlayniteAchievements/blob/v4.1.1/source/Services/Achievements/AchievementDataService.cs
Status: plugin endpoint, typed client, third destination, filter/sort/search and existing-details
round trip implemented in source. Framework/API checks: 32 passed. Widget checks: 234
passed, including missing/unsupported/outdated provider, lazy loading, stale leases,
filters and return focus. Eight new client cases passed; the full application suite
still requires its API allowlist update and rebuilding its source-parity package fixture.
Native UI, live installed-addon performance, additional delayed-result/teardown checks,
coherent packaging and installation remain pending. No new plugin or widget installed yet.


Native follow-up: the production widget with a fixture provider was inspected at
1180x760 and 520x560 DIPs. The third tab initially clipped at minimum width; its
existing navigation row now fits available width and hides redundant bumper
adornments in compact mode (the footer still advertises controller navigation).
Compact summary text replaces three tall metric cards, recent unlocks are hidden
at that width, and the game collection adapts between one and two columns.
Native mouse selection opens the existing Achievements detail tab; Escape restores
focus to the exact selected game. Missing-addon UI and restoration via Refresh were
verified. Captures: `artifacts/input-discord/achievement-hub-native/overview-v2.png`,
`overview-520-v3.png`, `missing-addon.png`. Fixture artwork is deliberately synthetic,
not a verification of real provider artwork. Live installed-addon checks remain due.

The overview now projects up to 16 cached games / 8 ms per background-priority
Playnite dispatcher turn, then yields to input. A 501-visible-game fixture passed
input yielding, bounded first-page output and one projection per game; Framework/API
suite now 33 checks. This is behavior evidence, not a live-library latency benchmark.


Installed-provider evidence (2026-11-15): WidgetRail for Playnite 1.1.0 installed
with backup of its prior binaries. Old Playnite Bridge remains disabled and its
DLL hash is unchanged. The real authenticated local client successfully reads
126 games / 8,979 achievements / 430 unlocked / 1 completed game and 8 recent
unlocks. First overview request took 190.7 ms; warm request 16.9 ms on this profile.
This is an observed sample, not a latency guarantee. The opt-in application run
passed 118 checks with 5 unrelated live opt-ins skipped; normal run passed 117
with 6 opt-ins skipped. Widget suite: 237 passed. Framework suite: 33 passed.
Evidence: `artifacts/input-discord/live-achievement-overview.json`,
`achievement-plugin-installed.json`, `live-achievement-overview.log`.
Host/widget activation and real artwork inspection remain in progress.


Completion: Achievements is now installed as a third destination beside Home and
Library. Final live UI verifies real recent-unlock art, game covers, totals,
search for007, existing Achievements details, and restoring the exact original
progress target with its artwork. Wide and520-DIP fixture layouts, missing-addon
recovery, delayed/stale selections, profile changes and removed games are covered.
The plugin overview uses only the public cached provider projection, bounded
pages and background-priority dispatcher batches; no eval endpoint or provider
refresh loop was added. Source audits identify no new generic host/SDK abstraction
needed for this provider-specific overview. Existing widget resources, collections,
scoped artwork and captured targets own the shared lifecycle mechanisms.

The usual profile now runs widget0.4.0/plugin1.1.0 and the matching protocol110
host/runtime; old Bridge remains disabled and unchanged. Original progress pin
007 First Light / A collector’s habit is restored at13/92 with real artwork.
Evidence and limitations are consolidated in the final completion section of
`docs/input-discord-refinements-plan.md`. Earlier pending notes above are historical
checkpoints, superseded by this completion. No live gameplay counter increment
was claimed; the pin follows updates that PlayniteAchievements publishes.


### Achievements page refinement (2026-11-15, widget 0.4.1)

User requested the existing navigation partition, less vertical chrome, desktop
acrylic, game-details search styling and at least six complete game cards at the
normal page size. The existing shared navigation helper and Home/Library header
style are reused; Refresh shares that header at normal width. The duplicate title
and snapshot/time/status copy are removed. Compact mode moves Refresh into the
existing game-list heading. Metrics and recent cards are shorter while game names
remain17DIP; game row pitch is100DIP. Native measurement at1180x760DIP confirms
six complete rows/cards (two columns by three rows) within the list viewport.
520x560DIP native inspection confirms all three tabs, full search, Clear, filters
and Refresh fit without clipping. Missing-addon recovery still works.

Search uses the exact game-details TextEntry styling, left-stick hint and Clear
button. The user clarified this was a visual consistency request, not a request
for a new inline editor; no new SDK text-input abstraction is planned here.
Desktop acrylic is declared through the existing WidgetSurfaceAppearance contract;
the widget's former opaque/in-app-acrylic base paint is removed. Reduced
transparency/high-contrast fallback remains host-owned. No host/SDK changes.

Freshness findings (reproduced in behavior tests):
- `PlayniteLibraryWidget.AchievementHub.cs`, AchievementsOpen: EnsureLoaded could
  reuse its30-second widget cache after an event received on another destination.
  Widget owner: refresh on entry using the existing WidgetResource API. Server
  snapshot caching still avoids unnecessary projections when revisions match.
- `PlayniteLibraryWidget.Updates.cs`, overview event handling: Refresh joins an
  in-flight read, so a later invalidation could be lost behind that older read.
  Widget owner: Cancel(false) then Refresh fences the old read and queues the newest
  work through the SDK's existing bounded operation machinery. A deliberately
  cancellation-ignoring old provider result cannot overwrite the newer snapshot.
  No additional polling loop, revision counters or custom cancellation sources.

Consumer matrix: Achievements overview affected/fixed; Home/Library styles and
navigation are reused unchanged; selected-game details and progress pins retain
their existing event paths; other widgets/templates/SDK examples are not affected
because this change adds no shared contract or host behavior. A broader audit of
other consumers' in-flight invalidation semantics is outside this bounded page fix.

Validation:239 widget checks passed, including open-page event updates, inactive
route behavior, current data on return and in-flight invalidation. Native evidence:
`artifacts/input-discord/achievement-hub-native/six-games-verified.json`,
`refined-wide.png`, `refined-narrow-final.png`, `refined-missing-addon.png`.
Application/package verification and normal-profile update are recorded below.

Final application suite:117 passed,6 explicit live opt-ins skipped. Widget0.4.1
installed and selected in the normal catalog without restarting the host or Discord.
Live geometry confirms six fully visible game cards; search/clear, details round
trip and restoring the original007 progress tracker passed. Final UI:
`artifacts/input-discord/rich-preview-achievements-candidate/achievements-refined-live.png`;
installation/bounds evidence: `refinement-installed.json`. Host and Playnite plugin
binaries are unchanged. The user's latest clarification is honored: no snapshot,
last-updated, or extra event-explanation copy is shown on the page.


Follow-up (in progress):0.4.2 installs a100-DIP recent-unlock card in a124-DIP
strip, with single-line game/date metadata. Native date bounds end19 physical
pixels above the card bottom at the user's scale; six game cards remain visible.
0.4.3 is built but NOT installed: removes the entire redundant game-list heading;
compact Refresh moves beside Search/Clear. Applies theme data-blue to source/info,
data-green to progress, data-gold to rewards/ratings and data-purple to time fields
across Home, Library summaries, game details, Achievements and the progress pin.
Titles/prose remain ordinary text.239 widget checks pass including10 theme variants;
16 opaque dark/light palette combinations exceed4.5:1 (not an acrylic contrast claim).
Native visual checks and final installation are pending while the newly reported
Discord preview dismissal/stuck drag issue is investigated.


0.4.3 validation follow-up:239 widget checks and117 application checks pass
(6 explicit live opt-ins skipped).10 theme variants verify semantic text resolves
through the selected theme's existing data-color tokens. Light-theme native rendering
shows the gold reward, blue source, green progress and purple time text; recent dates
fit with bottom padding and six complete game cards remain visible. The entire game
list heading/count row is absent. Primary game titles, prose, control labels and focus
remain ordinary theme text. A high-contrast check found the existing host surface
palette mismatch documented in the input/Discord plan; fix is shared host-owned.
Native captures: `artifacts/input-discord/playnite-ink-validation/`.


Completed: Playnite0.4.3 installed in the normal user catalog. The new host release
also contains the shared preview handoff/capture and high-contrast palette fixes.
Live dark-theme overview and detail captures show the themed data colors, removed
redundant heading, fully visible recent-unlock dates and six complete game cards.
Light and high-contrast rendering are checked in an isolated fixture. Source/layout
changes are widget-owned; tokens, text entry, indexed collection lifecycle, native
capture, and surface accessibility remain framework-owned. Recent-card clipping
was a widget height regression and is fixed by adequate card/strip space plus
single-line metadata; no host workaround was added. The original artwork progress
pin is restored. Final evidence: `playnite-ink-validation/dark-live-achievements.png`,
`dark-live-detail.png`, `geometry-result.json`, `high-contrast-corrected.png`.


### Desktop acrylic navigation (reverted at user request)

The user reported the appearance did not match expectations and requested a revert.
The panel-material experiment and Playnite navigation opt-in have been removed.
Previous native target/lifecycle checks did not establish visual acceptance; do not
treat their passing result as justification to reintroduce this design.

Restored the accepted Playnite0.4.3 package, previous participant-preview-release
host, and rich-preview-achievements-candidate runtime configuration. The source
stylesheet matches the installed0.4.3 package exactly. Removed only this experiment's
WRSS keyword, panel adapter/lifecycle integration, native fixture, tests and docs;
the preexisting whole-surface/pin/banner desktop acrylic implementation and earlier
preview, accessibility, achievement and theme fixes remain.

Rollback checks:38 styling and239 Playnite widget tests pass. No remaining source
references to the experimental desktop-acrylic panel keyword or adapter hooks.
Normal-profile host restarted successfully. The0.4.4 artifacts are historical and
not selected; they must not be promoted without a new design and visual review.
Evidence: artifacts/input-discord/desktop-nav-reverted-screen.png and
artifacts/input-discord/desktop-nav-reverted.json.

### Unified achievement refresh (implemented, Playnite0.4.5)

Removed the separate Refresh from provider button. Existing details Refresh/Y
also requests provider synchronization on the Achievements tab when CanRefresh
is available. Other tabs and missing-provider cases retain ordinary cached refresh.
Repeated actions during provider work are ignored before OpenDetails can cancel
and recreate the request. Opening/returning to the widget does not initiate sync.
Pin controls remain; their row is omitted entirely when no progress pin is selected.

Ownership: PlayniteLibraryWidget.Details.cs refresh action chooses provider policy;
PlayniteLibraryWidget.Updates.cs reuses SDK RunSingleFlight and WidgetQueryResource.
No SDK/protocol/host changes; other widgets are not affected. A source-inferred
overlapping-read risk was confirmed by the delayed-result test: completing provider
work must retire the cache read started before it. Existing SDK Cancel + refresh
provides this, including serialization until the canceled reader exits. Provider
generation checks, cancellation and no-resubmit-on-resume behavior remain intact.

Validation:240 widget tests, including duplicate actions, Y binding, non-achievement
refresh, missing-provider fallback, cancellation and late pre-sync cache results.
Package validated and installed in the normal catalog; host remains the reverted
participant-preview-release. Native screenshot unified-refresh-installed.png verifies
the extra button/empty row are gone. The native Y guide command was invoked; live
provider completion was not observed, so automated service tests establish the
combined command path, not an end-to-end provider availability guarantee.

### Accepted source qualification — 2026-11-15

The user accepted the completed source through Playnite0.4.5 for merge. The shared
merge verification covered all84 managed steps and7 native steps, including the
Playnite widget, local API, Framework fixture, package and application suites.
See the accepted merge qualification in docs/input-discord-refinements-plan.md.
Live provider opt-in tests remain explicitly skipped by the default application
suite; existing recorded live checks do not imply universal provider availability.
The navigation desktop-acrylic experiment remains reverted.

## Capture session attribution and reusable associations

| Owner / consumer | Scope |
| --- | --- |
| Windows capture, clip drafts, replay | Record the observed process instance independently of task-switcher naming. Save and edit paths retain the original source evidence. |
| Capture Library store, protocol and SDK | Index bounded source intervals. Attach revision-checked, caller-scoped references to saved captures. A project editor and a game library can both reference the same original. |
| Playnite add-on and widget | Retain profile-scoped launch evidence, reject pre-existing or ambiguous processes, and reconcile in game details. Explicit selections use the same API as automatic links. |
| Achievement companions and other widgets | Keep existing source registration and save-with-context semantics. Existing readers, templates and examples require no new capability. |

The host does not map process names to games or run an always-on Playnite watcher. The source-side history is bounded. Large collections still pay the existing filesystem reconciliation cost on first access and full refresh. Track incremental cold reconciliation separately. Evidence for the 3,001-capture fixture and the 128-link backfill is in `artifacts/playnite-capture-links/scaling-final.log`.

In that fixture, 24 warm queries over 64 process intervals took 387 ms total without re-reading metadata files. Writing 128 links took 3.37 seconds and added 88,320 bytes of association receipts and 237,568 bytes to SQLite. Reopening the existing index and serving the first four matches took 5.63 seconds. These measurements cover local generated data and include ordinary library work. The first access cost remains a separate scaling concern.

### Silksong and manual session linking

**Original design sequence (presentation superseded below).** Move Game Details to a standalone widget page before the broader Captures visual redesign. Home, Library, Achievements and related-item intents must open the same page with the correct game, section and originating focus. Let the SDK navigator own Forward and Back. Restore the originating collection item and scroll offset, including after a filter change, removal, interruption or stale provider response. Preserve the current game identity, Play, favorite, completion status, options and bumper section switching. Design and verify Playnite pages in the expanded widget with controller, keyboard and mouse. A dedicated compact layout is outside the design and acceptance scope. Keep focused tasks such as playback, choosing captures and destructive confirmation in dialogs. Use the additional page space to design the media timeline and session review; avoid further layout tuning around the existing details dialog's dimensions.

- Correct automatic attribution when Playnite reports a Unity crash handler. Verify the actual player against process ancestry, launch time and the game's install directory. Preserve ordinary exact-process matching and reject ambiguous evidence.
- Make the game’s Captures tab useful for viewing media. Use the shared native viewer, surface capture type and time, and exercise tab entry, player focus, linking, dismissal and return using directional input.
- Next, offer suggested sessions using distinctive title words in application names, executable names and window titles. Group by exact process instance. Show thumbnails, time range and screenshot/clip counts, and require user confirmation before linking. Prefer a learned association after the user confirms a match. Shared executables such as emulators still require a game choice.
- Allow reviewing and deselecting captures in a session, and add a grouped recent-runs picker when no suggestion is available. Implement caller-owned unlink, Undo and a durable exclusion for dismissed automatic links before enabling one-press bulk linking.
- Add the reverse entry from Capture Library selection to a searchable Playnite game chooser. Preserve focus, the source widget and existing unrelated references. Exercise both entry points with mixed games and sources, changing pages, interrupted writes and retries.

### Earlier standalone Game Details design and acceptance (superseded for presentation)

- Open with the host's forward navigation transition. Supply the selected game's title, cached cover, source and Play or Install action in the first frame. Honor the configured motion preset, speed and reduced motion. Late artwork and provider results must preserve layout and focus.
- Use a shallow artwork header with a legibility gradient and a themed fallback when background art is missing. Keep Play, favorite and game options within reach. Place completion status in game options. Render the existing section strip below the header. LB/RB changes only the section body. Direct achievement and capture entries focus the requested content.
- Use the expanded Overview width to place the description alongside secondary facts, with room for long text to wrap. On Captures, feature the newest session with an aspect-preserving preview, time and media counts. Follow it with earlier sessions and the link action.
- Record Home and filtered Library round trips through Details, Captures, viewer and Back. Check rapid interruption, slow or missing artwork, long titles, enlarged text and high contrast in the expanded widget. Verify the exact originating tile, reading position and focus after return, including when the source collection has changed.
- Evaluate an optional multi-hue theme on the same native screens. Compare selected, focused, selected-and-focused, muted and disabled states on artwork and acrylic. Also sample the radial tray and notification banners before considering a built-in theme change.

**Expanded page implementation.** Home, Library and Achievements open a nested Game Details page. The SDK navigator captures indexed return occurrences. The original root query still owns the game action target while the child is open. The artwork header and section strip stay in place as the tab body changes. The strip, optional notes and earlier captures have theme-aware dividers. Game options offers a completion-status chooser. Capture selection, playback and uninstall confirmation use dialogs.

**Native design review.** Screens of Home, Library, Achievements, Overview and Captures on a 5120 × 1440 display are in `artifacts/playnite-details-page/final-native`. The first pass stretched Details across the display. The revised page centers its controls within 1180 DIPs, places play history beside the description, enlarges the featured capture and keeps bullets with their text. Library covers were dim during the first artwork load and clear in the settled frame. Afterglow FM 1.0.1 uses a darker selected shell pill so the A prompt stays legible. The selected widget tab keeps the blush fill.

**Native return-cache review.** A direct Back restored the cached Home tree. After cycling through Information, Achievements and Captures, native diagnostics instead reported a cold return and showed empty cover tiles during reload. The presenter now favors the enclosing return page over a previously visited child tab when evicting a cached view. Existing limits remain two trees per presenter and three across the host.

| Consumer | Applicability |
| --- | --- |
| Playnite Home, Library and Game Details | Nested tabs can evict the list used for Back. Verify the real package and the nested-tab native regression. |
| Settings and Capture Library | Existing page transitions share the cache. Ordinary page-to-page eviction, invalidation and scope checks still apply. |
| Section-only widgets and SDK gallery | The policy requires a matching enclosing page transition. Section-only behavior uses the existing eviction order. |
| Templates and SDK examples | No API or protocol changes. |

**Release candidate verification.** Widget 0.4.17 passed all 252 widget tests. Native screenshots in `artifacts/playnite-details-page/native-017` cover the main screens and the Details page at 150% text size in high contrast. The source label blended into the artwork in the first high-contrast pass. The title area now carries both source and status on a solid surface. The real Silksong round trip reported a cache hit after three child tabs, and the immediate return screenshot showed the original focused poster and cover rail. The native regression checked the two-entry limit, restored control identity, global eviction and scope invalidation. A separate slider pointer-capture check required foreground ownership and could not finish in that run.

**Reversible links.** Playnite 0.4.19 offers Menu → Unlink from this game on an editable capture and Undo in the Captures tab. The broker records a detached state in the original receipt. It checks publisher, package, widget instance, media revision and link revision on changes. Automatic reconciliation skips detached references. A manual selection can restore one. The picker and the gallery now keep separate SDK query resources so returning from the picker retains the Link button and the visible gallery.

| Consumer | Reversible-link behavior |
| --- | --- |
| Playnite game captures | Look up editable receipts for the displayed captures in one bounded request. Explicit unlink and Undo operate on the association ID, including for a saved copy. |
| Capture Library search, metadata, derived copies and edits | Read active associations. Rebuilding SQLite preserves a detached receipt and the original media. |
| Achievement companions and source registration | Existing event and compound receipts still use their original write and read paths. Ambiguous ownership or multiple matching receipts suppress the widget's Unlink action. |
| Other widgets, SDK examples and templates | No existing attach consumer needs a new permission. The read lookup and link mutation are optional. |

Native input on a copied Silksong profile verified manual link, return focus, the capture context menu, unlink and Undo. The original media and the other game reference were preserved. Unsigned test builds have content-specific publisher identities. Links from an earlier identity remain viewable and read-only to the new widget. Any user-directed removal independent of the original widget belongs in Capture Library. Evidence is in `artifacts/playnite-reversible-links`. Some window-capture PNGs repeated an old frame, so they do not establish frame-by-frame behavior. Focus, UI Automation, receipt revisions and the release-host smoke check establish the functional round trip.

**Current Details presentation.** At the user's request, Game Details opens as a native modal over the unchanged originating collection. Keep the 0.4.19 composition and its 1180 by 940 DIP preferred bounds, clamped to the available viewport. The modal owns dismissal and focus, and only the selected section body transitions. Five short vertical separators distinguish the six tabs without adding focus stops. The SDK navigator still preserves the originating collection occurrence and the lifetime of detail work. Opening a focused task replaces the foreground dialog; Back returns to Details. This supersedes the standalone page and full-background navigation transition.

**Session and association implementation.** The native modal now features the latest linked session with a time range and media counts, followed by earlier sessions. The manual picker groups by the exact process instance and ranks title matches and the last explicitly confirmed executable per profile/game. The widget retains at most 64 executable preferences. Preferences never create links automatically. A multi-capture run opens an indexed review with up to 256 captures preselected. A toggles a capture and X links the selection. The widget resolves the entire selection against the reviewed revision before the first write. An older broker retains the individual picker.

**Reverse entry.** Capture Library offers the capture-association intent from a saved item's detail view and its selection toolbar. Playnite opens a searchable, paged game chooser and asks for confirmation. It rechecks the profile, connection, permissions, content revisions and game before attaching individual references. Failed items stay available for retry when appropriate. The handoff uses the existing intent array limit of 64. Each receiver supplies its own chooser and permissions. The host retains its ordinary cross-widget navigation behavior.

| Consumer | Behavior and verification |
| --- | --- |
| Playnite Library | Grouped gallery, session review, learned ranking, individual picker fallback, reverse chooser and caller-owned unlink. Release tests cover 258 captures, deselection, repeated commands, stale revisions, a changed profile and partial results. |
| Capture Library | Sends exact saved IDs and revisions without opening originals. A link change updates row content in a plain gallery. Text, origin and reference filters still invalidate ordering when their membership may change. |
| Other shipping widgets, SDK examples and templates | No other capture-association sender or receiver. Existing file sharing and related-item navigation keep their contracts. Game Help reads ordinary capture queries. |

**Native checks.** Evidence in `artifacts/playnite-capture-runs` covers Home, Library, Achievements, Details, the capture timeline, the session picker/review and the cross-widget chooser. The short-list modal shrank after screenshot review. Waiting for the initial picker response before opening corrected focus landing on Back. A toggled a review row and B returned through its session and the Link button to the originating Home rail. Two copied capture records were linked to another game while their original Silksong associations stayed active. After the broker continuity correction, returning via the tray kept the reviewed gallery selection and focus on Link to game. With an empty installed catalog the unavailable-action dialog appeared and selection survived. A cold Library transition showed toolbar and summary before its covers appeared. The settled screenshot shows the populated grid. The captures do not establish frame-by-frame smoothness.

**Verified interim fixes.** Silksong's reported process was its Unity crash handler. The add-on now resolves the player through the exact parent and installation, including when 32-bit Playnite observes a 64-bit game. A new screenshot taken after launching through the updated widget appeared automatically. The user's original screenshot was separately recovered via the manual picker. The Captures tab uses actionable media rows, an overlay viewer that fits ultrawide images, and a separate picker with B-to-return. Native input replay covered D-pad entry and return, A/B viewer and picker focus, and LB/RB section navigation. The earlier fixed-height grid and its no-op activation have been removed. Evidence is in `artifacts/silksong-capture-fix`.

**Final transition review.** Timed screen-region samples reproduced an empty game grid for about 0.9 seconds on first entering Library. The count and native item containers already existed while the widget's first ranges were in flight. WinUI now paints static, non-interactive loading tiles at the grid's estimated cell size and releases each placeholder on success or range failure. Warm returns retain their covers. In the filtered Silksong round trip, closing the viewer restored its originating screenshot, and closing Details restored the one-result Library grid at index zero. At 150% text with reduced motion and high contrast, a border keeps all five tab dividers visible. The enlarged-text review also exposed the fixed width of the Home navigation tabs. The main navigation retains its distinct minimum hit targets and gives Achievements more space than the earlier 128 DIP allocation.

| Indexed grid consumer | Applicability |
| --- | --- |
| Playnite Library, Favorites and achievement progress | Shared adaptive-grid template. Native cold-entry, warm return and focus checks in `artifacts/playnite-modal-transitions`. The horizontal Home rail uses the existing list template. |
| Capture Library | Same adaptive grid. Verified native selection in the bundled widget with two saved screenshots. |
| Games & Apps, standalone YouTube Music recommendations and Spotify playlists | Same host template when they publish adaptive grids. Their providers, item actions and discovery policies are unchanged. No signed-in music-session test was used for this visual change. |
| List-only widgets, non-indexed grids, templates and examples | No authoring or protocol change. |

**Interrupted associations.** A regression fixture pages a 13-game chooser forward, backward and forward, backgrounds the widget after the first durable write but before acknowledgement, then resumes and retries. The same two receipts survive retry and a mixed-executable selection teaches no process preference.

**Complete linked gallery.** Preview-only coverage left older members of a large batch inaccessible to Playnite's Unlink action. Version 0.4.29 reuses the existing picker resource to browse all matching links in pages of 12 without leaving Game Details. Receipt lookup, viewer activation, Undo and content-revision checks share the timeline implementation. A 13-item test exercises unlink on the sole item on the last page, Undo from the empty page and rejection of the superseded removal action. On a copied 15-capture library the shipping host focused capture 13 after a page turn, Undo after unlink, and the same capture after Undo and viewer dismissal. B then returned to Browse all. SHA-256 matched for all 15 original files. The trimmed host, native screenshots, navigation results and test logs are in `artifacts/playnite-modal-transitions`.

### Proposed next change — per-game Home and section focus

Valve’s controller-first Big Picture uses the Deck interface. The Deck game screen places Play above the activity and information tabs. Valve’s library design describes a post-game summary that brings achievements and captures from the latest play session together above the feed. [Big Picture announcement](https://steamcommunity.com/games/593110/announcements/detail/3394051164709183117) · [Deck game screen, figure 64](https://www.theseus.fi/bitstream/handle/10024/809528/Nieminen_Jukka.pdf?sequence=2) · [Steam game pages](https://store.steampowered.com/libraryupdate/?l=english)

1. Fix section switching independently. Native controller replay on 0.4.30 from the Link captures button reached Activity on RB, moved focus to Back and stayed on Activity on the next RB. The shortcuts are attached to the body and Back belongs to its sibling heading. Bind LB/RB on the persistent Game Details frame. For a bumper change, use the existing SDK focus request to land on the newly selected tab. Down enters its content. Nested viewers and dialogs keep their own controls. Test repeated and alternating bumpers from headers and section content, including while results are loading. Evidence is in `artifacts/playnite-home-plan/bumpers.json`.

   **Implementation checkpoint.** In isolated Playnite 0.4.31, Game Details binds its shortcuts on its modal frame. A bumper requests the destination tab through the SDK's exact focus-target API. The native test focused the same capture-link control as the original failure and observed the selected Activity → Links → Activity → Captures → Achievements → Captures tabs across six bumper presses. Evidence is in `artifacts/playnite-home-plan/bumper-regression31.json`. The 259 widget tests passed, including the indexed achievement-row bumper routing test updated to check the selected target and retirement of the previous search request. Nested picker and viewer behavior and the expanded navigation matrix remain part of end-to-end verification.
2. Replace Overview with Home inside the current modal. Keep the artwork header and Play/Install as the initial action. Lead with the latest known play session, with duration and associated unlocks or captures where supported by reliable evidence. Follow it with a single newest-first timeline grouped by local day. Bundle related unlocks and show a bounded static media preview. Use existing destinations for details and return focus to the originating entry.
3. Move Description, Notes, Time played and Last played into Information alongside the existing facts and completion status. Recommend renaming Activity to Sessions to distinguish the full play log from the mixed Home feed. Preserve direct achievement and capture entries.
4. Compose the feed in the Playnite widget from existing game activity, achievement and linked capture reads. Load sources independently on demand, cap the preview window, and retain stable occurrence identity and scroll position. Reading Home should not trigger capture reconciliation, permission prompts or media playback. A missing GameActivity provider, undated unlocks, denied capture access or an empty history must still leave a useful opening screen.
5. Review native screenshots and transitions for a busy game, a never-played game and a partially available provider. Verify controller, mouse and keyboard navigation, viewer round trips, rapid tab changes, late data, reduced motion, and 150% text in high contrast.

   **Per-game Home checkpoint (isolated 0.4.33).** The modal now opens on Home with Play focused. A virtualized recent-history list combines GameActivity sessions, dated unlocks and captures already linked to this game. The capture path checks read access without requesting it or running reconciliation. Actual event times can join exactly one session; overlaps, session end boundaries and save-only timestamps stay separate. DateSession is recorded at game start in the GameActivity provider. The latest session leads, with static thumbnails and year-aware day labels. Information now contains Description, Notes and the existing game facts, and Activity is labeled Sessions. The first native pass revealed that a 14-second session displayed as “0m” and that rows lacked hierarchy. The second pass on Silksong verified the revised lead card, two sessions with capture thumbnails, achievement entries, gallery entry and return to the same session, and RB from that session to Information. Evidence and 261 passing tests are in `artifacts/playnite-home-plan` (GameHome33-review.png, return33.json, information-focus33.json, refined-tests.log). Further work includes hero collapse/snap, larger text, interrupted transitions and access/error-state critique; this checkpoint does not cover the library-wide Home.

**Proposed main Home interaction.** Use Jump back in as the only game shelf. Keep saved favorites accessible through the existing Favorites filter in Library. Place Recent activity immediately below the covers. At the default expanded size, show its heading and a readable portion of the first real entry at rest. If the available sources have no activity, avoid implying there are entries below. Left/right browses games. One Down from a game reveals the feed and focuses the first entry. Up from that entry restores the exact game, including its horizontal rail position. The activity list owns ordinary navigation and scrolling once entered. Preserve shell navigation and the backdrop. Prototype native vertical viewport snapping independently of the Game Details modal. The SDK has virtualized lists and directional focus, but has no public vertical snap-point contract. Evaluate a bounded host/SDK section-boundary behavior, with reduced motion, short viewports, nested lists, repeated input and pointer/keyboard access. Check frame pacing on the actual Home screen before committing to motion. For the library-wide feed use the achievement overview and bounded reads of linked captures; assess a bounded recent-sessions query in the Playnite integration because the current GameActivity API is per game. Keep browsing games independent of those reads.

**Steam recording reference.** The supplied Silksong clip shows Game Details. Before the transition, Friends content is already visible below Play and the section tabs. At about 1.9 seconds the hero leaves view and Activity gains focus in a tab strip near the top. That strip stays put as focus moves through entries and their contents scroll. Use that continuity in Game Details, including LB/RB from within the feed. Adapt the same idea to main Home: the activity preview beneath Jump back in is the beginning of the actual feed, and the first Down lands directly on the first usable entry without an extra tab stop. The 30 fps clip shows the two settled positions clearly but offers little evidence for a specific spring curve; judge easing and reversal on the native prototype.

**Capture linking and virtualization refinements**

Use the installed Hades-inspired Asphodel Ember 1.0.0 as the reference theme in isolated test profiles for the upcoming Playnite, capture-linking and introduction walkthroughs. Keep the user's profile selection under their control. Baseline: all 45 default root tokens explicitly authored, package and computed preview validated, native Playnite Home and Settings inspected. During implementation check semantic ink, data/status colors, selected controls, focused controls, glyphs, scrollbars and popup/modal surfaces on artwork. Include reduced transparency and large-text/high-contrast runs and investigate incorrect fallbacks or hard-coded colors where they arise.

- Treat an active link to the same destination as already linked, including inherited links on edited copies. In the default profile, the cropped Silksong screenshot inherited two references to the same game from the original and acquired a third direct association. Capture Library currently renders each as a separate button. Group identical navigation destinations across receipt pages, preserving distinct game and achievement IDs, and retain receipt-level ownership checks for edits. Cover edited copies, repeated selection, mixed batches of new and existing links, and unlink/Undo with multiple owners.
- Add **Unlink from this game** beside the game in Capture Library → Related details, with **Undo**. Give Playnite's existing Unlink command the same result. A user-initiated unlink clears every active link between this capture and this exact game destination, including inherited and older-installation records. Scope the broker operation to the user's chosen capture and destination. Preserve unrelated references on compound records, other games, and the original capture when unlinking a copy. Undo restores only links cleared by this action. Verify interrupted writes, retries, index rebuild and automatic reconciliation against the three-link Silksong case.

- Home draws from GameActivity play sessions, unlocked achievements and linked captures. Use a virtualized list for the growing timeline, with stable event identities across late provider updates. Keep the small latest-session summary outside the list and avoid duplicating its events.
- Replace the capture-link game chooser's stack of 12 results and Previous/More buttons with a single SDK virtualized list. Keep a fixed search field above the scrolling results. Offer game-title suggestions through the existing SDK text suggestion contract, scoped to this picker and the connected Playnite library. Suggestions fill the query. Each result shows the title and source so duplicate names stay distinguishable.
- Title the chooser “Link 2 captures to a game” (using the actual count), show an A / Link hint on the focused result, and make explicit row activation commit once. Browsing, cancelling and accepting a search suggestion perform no writes. Remove the separate destination confirmation and success dialog. On success dismiss the picker and announce the count and game through nonblocking feedback. Preserve validation of profile, permissions, capture revisions and stale actions. On partial failure give the user the actual count and recovery action for outstanding items only.
- The existing short capture dialog style specifies a fixed 480px height and is shared by session review and the small linked gallery. Size remaining short prompts around their content, cap at the available viewport, and give long game lists their own scrollable space. Preserve the dimensions of Game Details and the media viewer.
- Verify native controller and keyboard search (including suggestion acceptance), selection after scrolling through a large library, duplicate game names, focus on dismissal, and error recovery. Adapt the interrupted-batch regression to direct activation. Cover repeated A, stale search results, partial retry, and large text with long titles. Check that realized rows stay bounded as the library and timeline grow.

### Final follow-up — refresh the WidgetRail introduction

After the Playnite work, revise the host-owned optional first-run tour against shipping input routing and the live guide. The current welcome card describes D-pad and left stick as interchangeable even on the radial tray, omits right-stick scrolling and gives no introduction to tray quick actions.

- Teach the radial tray in context: use the left stick to choose a widget, read the directional hints, and use an empty D-pad direction to open the assignment chooser. For mouse and keyboard show Quick actions beside Manage pins and retain arrow-key navigation. The tour should work with every direction empty or assigned and without optional widgets. Let users learn the controls without invoking a real assigned action.
- At the first widget, explain focus, activation, Back and right-stick scrolling. Use the live guide for widget-specific actions and current controller glyphs. Keep the configured overlay-opening shortcut visible. Review pinning, notifications and task switching in their actual contexts, including tap versus hold. Keep Catalogue and Appearance concise.
- Reuse existing tour cards, real controls and input routing. Keep the welcome short enough to scan from the couch. Derive step numbers from the visible sequence, preserve pause/resume and Settings replay, and keep completed tours completed on update.
- Extend the isolated native introduction fixture. Verify fresh, paused and completed profiles, available and missing optional widgets, assigned and empty shortcuts, and controller/mouse/keyboard paths. Review screenshot sequences at large text size and check focus, hints and transitions as the tray, menus and destination widgets open.
- After refreshing the cards, complete at least three end-to-end native walkthroughs. Include a normal first run, a tour interrupted and resumed across a restart, and a replay from Settings after completion. Exercise inputs while Commands or a utility panel is open, rapid Continue/Back during destination loading, hide/show, and a change of input device mid-tour. Verify the saved step and actual focus after each interruption, then finish the tour.

### Final Playnite design review

**Focus reveal implementation and applicability.** Protocol 138 adds an optional `RevealBodyOnFocus` flag to explicit grids. The SDK exposes it on GridLayoutElement. WinUI owns the two native scroll positions and preserves a constrained viewport for nested collections as the header moves. No widget publishes focus events or animation frames. Existing grids omit the field and keep the native Grid path.

| Consumer | Adoption and evidence |
| --- | --- |
| Playnite Game Details | 0.4.36 with Asphodel Ember. Native normalized D-pad replay moved Play → Home → first day → Home → Play. Tabs moved from physical Y 510 to 190, then returned to 510. At 150% text in high contrast with reduced motion they moved from 574 to 229 and returned. Native gallery dismissal restored the same day. Results and screenshots are in `artifacts/playnite-focus-reveal`. |
| Playnite main Home | Uses the same opt-in for Jump back in and the new library-wide activity feed. The feed replaces the Favorites shelf. Library's Favorites filter is retained. Native testing of installed add-on 1.3.3 and return to the poster rail is pending. |
| Other shipping widgets, templates and SDK examples | No opt-in. Ordinary scrolling, tab selection, media viewer and disclosure semantics continue on their existing controls. Protocol round-trip, version rejection, invalid header tracks and ordinary grids are covered by SDK tests. |

After the feature changes land, use Impeccable and WinUI design guidance on the actual Home, Library, Achievements, Game Details sections, capture viewers and linking flows. Review native screenshots and transitions at couch distance. Walk the common paths with controller, keyboard and mouse. Include Asphodel Ember, high contrast, larger text, reduced motion, sparse libraries, slow or unavailable data, and rapid navigation. Record each concrete fault, its consequence for a user, the correction and the result on the same screen and input path. Repeat after corrections until an entire pass finds no further meaningful visual, navigation, accessibility or performance improvement within scope.

The review must answer the concern raised by the 0.4.33 screenshot. From the first viewport, can a player recognize the game, see what happened in the last session, and tell where Down will take them? Verify this with capture-rich, achievement-only and sparse histories. Record which screens and paths were actually reviewed in the last pass. An untested path stays open.

**Main Home checkpoint (0.4.38).** All 262 widget tests passed. The Playnite 1.3.3 adapter passed 78 Framework/API checks, including bounded recent sessions and unlocks, newest-session selection, and exclusion of hidden games and repeated game IDs. These checks used fixture data. Live review of the main Home feed remains pending while the running Playnite instance hosts the prior add-on. Evidence is in `artifacts/playnite-focus-reveal/home38-tests.log` and `artifacts/playnite-focus-reveal/adapter-test.log`.

**Main Home native critique (0.4.39).** The prior Home discarded the feed if the installed shelf was empty. It now gives that space to activity and keeps View library reachable. A native fixture with three uninstalled games exposed missing hero handles and a large empty background panel. The bounded recent query now registers the recent games' artwork references independently of installed membership. WinUI loads images on demand. A further pass with two real local game images and one missing image verified the themed placeholder on the third post. Capture-derived posts also check current catalog membership and hidden status. In the installed case, controller Down from the Hades II poster reached the first Silksong post, Up returned to Hades II, and opening Silksong then pressing Back restored its post. Evidence is in `artifacts/playnite-focus-reveal/home-review/final39` (shelf.png, activity.png, sparse.png, controller.json) and `revised/sparse-focus.json` under the same home-review directory. This uses production widget and application code with fixture history. The widget suite passed 263/263. The application suite passed 123 with six opt-in live checks skipped. The release package and embedded integration hashes were verified. Default profile and running Playnite were left alone.

**Next critique findings.** The installed shelf still shows a blank poster for a game whose declared cover fails to resolve. Its capture-independent activity tile now has a working missing-art fallback. Inspect the SDK poster and native artwork completion path before changing more widget-local tree manipulation in `PlayniteLibraryPresentation.Tile`. In the activity resting position, the backdrop still belongs to the last poster even when the selected post is for a different game. Evaluate the existing focus-artwork contract on realized posts, label contrast over bright art, readability of post details at couch distance, and a stronger visual hierarchy for the most recent session. All remain open for the next visual pass.

**Purpose review and correction (0.4.40).** The player pointed out that the shelf and daily game summaries both emphasized the game's name and opened the same details screen. Navigation and screenshot checks had missed the duplicated task. Home now presents timestamped events across games. An unlock shows its name and available rarity and opens the unlocked achievements filtered by its name. A session shows its duration and opens Sessions. Nearby captures of the same game form a single media entry with two previews, and opening it goes directly to the gallery. Consecutive captures can group within 30 minutes on the same local day, bounded by the viewer's capacity. An intervening event ends the group. Focused events use the existing SDK backdrop contract. The poster shelf retains its game-opening action.

An isolated Asphodel Ember run used the same Silksong title in both sections, fixture GameActivity and unlock data, and copies of actual linked captures. The initial media grid measured at zero width in WinUI despite having two valid images. After giving the grid explicit bounds, both previews rendered at 668–669 × 244 physical pixels. Controller activation reached three distinct destinations, advanced to the second attachment and returned focus to the originating entry each time. Native evidence is in `artifacts/playnite-focus-reveal/home-review/events40/gallery40` (activity.png, destinations.json and second-attachment.json). The poster fallback and broader final review are still open.

The user's 0.4.33 Silksong screenshot exposed two problems that the earlier focus checks missed: a 14-second session got the most prominent position and screenshots appeared as tiny trailing thumbnails. Isolated 0.4.34 now uses panoramic hero artwork, an action strip, a daily activity feed with large capture previews, and a separate achievement summary. The native screenshot and gallery-return focus check are in `artifacts/playnite-home-plan/Journal34-review.png` and `journal-return34.json`. This is an intermediate visual pass. The focus-driven hero reveal, large-text input checks and final critique remain open.

**Playnite 0.4.41 feed review.** The three event types in 0.4.40 still shared a full-width row whose right side was often empty. Home now pairs events in a two-column virtualized grid. Unlocks combine the provider emblem, unlock condition and global rarity. Sessions pair the recorded time window and duration with a game scene. Missing art exposes a clock and time of day. Captures open with an image spread and keep titles and dates below. Each card is one focus target. Game Details has a dated journal with separately actionable awards, session summaries and photo spreads, a daily play-window diagram and a seven-day playtime comparison ending on the last recorded session date. Session totals in the comparison are assigned to their local start day. The optional icon and description fields are projected from the eight retained unlocks per game in integration 1.3.4.

The native Asphodel Ember pass exposed an empty missing-art panel and repetition between the most recent session and the sidebar. A second pass at 150% text with high contrast and reduced motion exposed a clipped achievement condition and invisible chart fills. The revised award panel measures its text, chart marks have borders, and the feed heading has a themed backing over bright artwork. Native screenshots, controller destinations and automated test logs are in `artifacts/playnite-story-review`. These runs used the production widget and application with fixture event times, actual local Silksong achievement art and copied capture metadata. Live add-on and default-profile rollout remain pending.

| Capture destination contract consumer | Adoption and verification |
| --- | --- |
| Playnite automatic links, manual picker, reviewed sessions and Capture Library handoff | All use the broker's atomic ensure-subject operation. Already-linked and newly linked outcomes are separate. The broker fixture exercised concurrent callers, inherited compound references, a different publisher/instance, an independent package namespace, and SQLite rebuild. |
| Capture Library Related details and Playnite Home captures | Both use the broker's distinct destination page. Tests cover repeated receipts across pages, actionable and display-only references, and stale modal completion. |
| Capture creation, source indexing, receipt editing, SDK examples and templates | Receipt-level APIs retain their existing behavior. No other shipping destination browser or subject-link writer was found. |

Duplicate prevention and grouped reads are implemented. Grouped cross-owner Unlink and scoped Undo are verified below. Duplicate prevention and grouped-read evidence is in `artifacts/playnite-unique-links`.

Final screenshots for this iteration are in `artifacts/playnite-story-review/verified` at normal size and `final` at 150% text, high contrast and reduced motion. Controller destination-and-Back checks are in `refined/navigation.json`. The fixture's playtimes were seeded for the chart comparison. Two attempts to capture transitions at 10–12 fps returned a single distinct image despite confirmed focus changes, so the recordings do not establish frame-by-frame smoothness. The Playnite widget suite passed 264/264, the application suite passed 123 with six opt-in checks skipped, and the add-on passed 78 Framework/API checks. The SDK suite passed 205/205.

**Grouped unlink store checkpoint.** User choices live in the canonical `library-user-state.sqlite` alongside existing hidden-item choices. An attached SQLite transaction records which original receipts contributed the selected reference and updates the derived library index. Receipt JSON retains the original provenance. Reads, edits and copies use the effective references. The isolated three-link fixture verified rollback on an injected write failure, rebuild, Undo, new copies, and preservation of unrelated references. The broker now handles the grouped operation through the SDK. A host review names the capture and destination when removing links from another publisher or instance. Cancel makes no change, a successful retry is idempotent, and only the actor who requested the change can undo it.

**Grouped Unlink and Undo in the native host (Playnite 0.4.42 and Capture Library 0.2.8).** The isolated profile contained one Silksong capture, three contributing game receipts and an independent achievement reference. In Capture Library, Cancel returned focus to the same Unlink button. Approve removed three game references while Liberated remained visible. Undo restored all three and focused the Silksong destination. A native UI Automation pass also verified that each Unlink button names its destination to assistive technology. A separate widget test covers the last destination, closing and reopening the empty Related details dialog, and returning to the restored game. In Playnite, the native Game Details Captures section initially fell back to Back to games after unlink. The widget now uses the SDK’s existing directed focus request after the mutation. The second native pass focused Undo and then the restored capture card on Enter. These runs used the production broker, bridge, host and widget code with a fixture Playnite provider. Native trees, focus records, the release package and test logs are in `artifacts/playnite-grouped-undo`. Playnite passed 264/264 on a full rerun after one test timed out in the earlier run. Capture Library passed 157/157 and the SDK passed 205/205. Native expiry, review-time unrelated capture changes, the reverse-intent chooser, missing-cover shelf, refreshed introduction, transition sampling and final cross-screen critique still require follow-up. The default profile was not updated.

**Home rail, activity and modal material (0.4.43).** Home reserves the natural height of the focused summary and poster row. The heading is now a full-width section with a display title, an extending divider and native acrylic. Its title is inset like Jump back in. The heading and activity panel meet both outer edges of the poster rail. The two-column feed retains its inner gutter. Each realized activity card owns one acrylic backing with a separate contrast wash for text over bright artwork. The Game Details modal uses the theme's acrylic preset. Its capture settings dialog retains its solid fill and the media viewer its transparent surface.

Native Asphodel Ember checks at 100% and 150% text size measured the rail and heading from x=568 through x=1993 on a 2560-pixel display. The cards spanned x=568 through x=1992, within native pixel rounding, and the selected poster had 26 physical pixels of bottom clearance. Opening Hades II and returning restored its poster. A native capture of the unoccupied portion of Game Details showed red-channel values from 19 to 116 with acrylic, versus 24 to 25 with acrylic disabled. Settled screenshots, native measurements and the 45 modal lifecycle checks are in `artifacts/playnite-home-material`. A 30-second layout test timed out under load and passed individually. The final 264-test run passed with two workers. Frame-by-frame transition qualification remains open.

| Shared change | Applicability |
| --- | --- |
| Acrylic modal veil and shadow | The WinUI host observes the material on the retained modal panel and strengthens the veil again when fallback or ordinary paint takes over. The existing outer-shadow primitive is shared with rich previews and notification banners. Playnite opts into the material. Other shipped widgets and SDK examples retain their authored modal fills. The native modal suite covers scope, motion, dismissal and viewport retention. |
| Signed WRSS margins | The WRSS catalog accepts signed margins. The WinUI spacing parser previously discarded a declaration containing any negative side. It now honors signed margins while preserving nonnegative padding. Playnite's virtualized feed uses a trailing compensation for the inter-card gutter. No other negative margin was found in shipping widget, theme or template WRSS. |

**Home and journal revision (0.4.44).** The selected game has a horizontal action and stats strip with the available Y shortcut, title/source/status, last played and playtime. Home groups the cross-game feed by local calendar date and category. Play sessions for the same game on the same day share one entry with total duration and session count. Captures from a game and day share a gallery. Achievements retain separate, actionable tiles with provider artwork or a trophy fallback, title, game, unlock time, and available condition and rarity. Each pair shares a row in the virtualized list. Game Details uses native day headings outside the focusable journal entries with matching left edges and space between days.

The Asphodel Ember fixture showed 55% of the first achievement row at standard text size and 26% at 150%, with full card content on entry. Native activation of each tile opened its matching achievement search and Back returned to the original pair. The large-text run also exercised controller A, Right, A and B through the shell input replay. The fixture included two sessions for Silksong on one day and a separate Hades II session. The widget suite passed 266 tests. Screens, focus records, tests and the verified package are under `artifacts/playnite-daily-home`. The running candidate catalogue selected and enabled 0.4.44 without restarting WidgetRail, Playnite or the game. Its overlay was hidden during the version check.

**Recent activity and Game Details date separators (0.4.45).** The earlier paired achievement row let one focus outline surround two unlocks. Its shorter child painted only the height of its copy. Home now uses independent keyed grid items, two columns where space permits, and an acrylic panel that fills each tile. The description and footer occupy separate grid tracks. Session and capture cards use the same outer dimensions. Daily game and category aggregation is preserved.

Both feeds now have a transparent collection background. The first group of each day draws a line from its date heading to the trailing edge. Subsequent categories retain their names under that date. Home tints the game artwork underneath the foreground so dates remain legible over bright scenes. Game Details uses its existing modal material.

| Group header consumer | Applicability |
| --- | --- |
| Playnite Home and Game Details Home | Opt into the native header rule at date boundaries. The widget supplies the date and category. The host sizes the line and keeps it out of focus and accessibility navigation. |
| Capture Library month groups and YouTube Music Home groups | Continue using the default plain grouped header. |
| Other shipping widgets, templates and SDK examples | No other production grouped indexed collection entry points found. Choosers have their own group templates. |

The normal and 150% text fixtures exercised directional movement between three same-day unlocks, opening each achievement, exact Back focus, and returning from Game Details. Native panel and focus bounds agreed within one physical pixel on all edges. A third pass used a missing icon, missing rarity and description, and a long neighboring unlock. The screenshot descriptions fit at both text sizes. Longer descriptions truncate above the footer. The Playnite suite passed 266 tests and the SDK harness passed 205. Native captures and focus records are in `artifacts/playnite-unlock-grid`. The trimmed default-profile candidate was launched with Playnite Library 0.4.45 and verified on the live Recent activity feed. Playnite and Silksong kept their original processes.

**Missing cover and Related details follow-up (Playnite 0.4.48, Capture Library 0.2.9).** The library service advertises artwork handles before resolving individual files. On Home and Library, Celeste therefore had a focusable poster with no visible name after its missing cover resolved. Playnite opts its poster artwork into WRSS `image-fallback: label`. The WinUI image renderer displays the authored accessible label on an unavailable image without changing focus or issuing a second artwork request. A valid cover keeps its artwork. Native tests covered pending, missing and recovered sources, changing the style on a retained view, controller activation and Back on both Home and Library at normal and 150% text. The YouTube Music and SDK Gallery posters use their existing captions and omit the property. Other widgets and templates keep their current image rendering.

The live trimmed frontend logged a missing generated JSON contract for the unlink decision. The broker never received the response, so its next poll returned the same outstanding request. The trusted first-party Capture Library now applies the reversible grouped edit directly after manage-permission admission. Other callers still require review for edits to another owner's receipts. The trimmed host has serialization metadata for the decision and the adjacent replay picker response. On an isolated three-link capture, direct unlink retained an independent achievement and focused Undo. Undo restored the game links and focused the game. Capture Library's failed game-link handoff now describes the receiving game library generically. An unavailable handler does not identify the cause of rejection. Evidence is under `artifacts/playnite-missing-cover`.

**Game Details capture strips (0.4.49, protocol 139).** Activity now uses the SDK's controlled `UI.CaptureStrip` for each dated capture group. It publishes separately selectable 16:9, aspect-preserving thumbnails, an accessible name per capture and a play glyph for a video. A selects an original in place above its own strip. Previous, Next and Close belong to that group. Only one capture is expanded across the journal. An unchanged capture survives refresh, and navigating away and returning to the row preserves its expansion. Fullscreen uses the shared native media player. The Game Details modal and journal collection retain their native identities.

The fixed preview above the whole feed was rejected after interaction review. With an expanded row the host preserves the thumbnail's viewport position where space permits and makes room for the preview on shorter viewports. It does one layout change rather than animating the height of the virtualized list. At normal text the focused thumbnail measured Y 956 before and after expansion, with successive frames recorded in `artifacts/playnite-journal-strip/normal`. At 150% it moved from Y 600 to 797 to expose the preview. Both settings passed horizontal traversal beyond the initially visible thumbnails, leave and return, Previous/Next boundaries and exact focus on close. The mixed-media run included a portrait still, a 4K still, a clip with transport controls, and dismissal with controller B. The fixture operated on isolated linked media.

| Reading group consumer | Behavior and coverage |
| --- | --- |
| Playnite Game Details timeline | Uses the new capture strip, selected thumbnail and a row-local saved-media declaration checked against its current lease and content revision. Home activity and the dedicated Captures tab retain their separate presentations. |
| Discord message body | Adopts shared direct entry into available inline actions. Message-level Back, text-only stops, selection, disabled actions, keyboard activation, pointer activation, stale leases and modal return were exercised in the native messaging fixture. |
| Other shipping widgets and templates | No other production callers of `UI.ReadingGroup` found. Existing action surfaces, passive reading surfaces and media galleries keep their controls. SDK authors can use the new component with saved captures and their own scoped thumbnail handles. |

**Introduction refresh.** The host teaches left-stick widget choice, D-pad radial shortcuts, right-stick scrolling, live guide hints, tray Commands, pinning and tap/hold Y. Controller entry actually opens the radial for shortcut rehearsal. The host intercepts an assigned or empty slot while this lesson is active. Keyboard/mouse entry teaches the bottom rail. Optional destinations and the step counter are derived from the available catalog. Existing persisted steps and completed tours remain valid.

Native fresh-controller, interrupted/restarted keyboard and Settings-replay walkthroughs passed in isolated profiles. The controller run also exercised Commands open during Continue, the utility panel, delayed Appearance navigation, button-family changes, hide/show and bounded large-text cards. A fourth walkthrough finished on a profile without Settings or Catalogue. The WinUI shell suite passed 418 tests. See `artifacts/playnite-introduction-refresh` for progress files, native results and screenshots.

**Final native review, Playnite Library 0.4.50.** Library used to offer “Choose a game to see its details” alongside “No games.” The widget now supplies the focus summary only when the indexed catalog has selectable games. A real Playnite library was filtered to zero matches and restored by activating Clear search and filters with the controller. Search retained focus in both states, and the summary returned with the populated grid. A separate empty-library fixture at large text and high contrast showed one empty state and focused Refresh. The widget suite passed 266 tests. The verified production package was installed on the default profile and its 0.4.50 worker opened Home. Evidence is in `artifacts/playnite-final-review/{normal,empty}`.

The normal and large-text review covered the Home rail, Library, Achievements, Game Details tabs and capture association picker. The isolated 323-game picker opened on Search. Accepting the native “Library game 300” suggestion filtered the result, and selecting it linked the isolated capture. Cancel was exercised in the large-text pass. Both paths closed onto Playnite Home. Switching back to Capture Library restored the initiating capture's Link to game button. The current intent handoff in `OverlayShellPage.Intents.cs` finishes after delivery and lacks a later picker-completion return. Direct return to the source widget remains an open UX decision. The native focus snapshots and screenshots are in `artifacts/playnite-final-review`.

**Bumper replay correction.** In the initial keyboard-driven replay, two F7 keystrokes sometimes landed on Library. Instrumentation found a single LB press reaching the widget presenter during each of four such runs. To remove the dependency on WinUI keyboard focus during page replacement, the diagnostic host delivered the second normalized press and release directly through the controller frame entry point. At observed press intervals of 93, 125, 266, 453 and 812 ms, all five runs advanced Achievements → Library → Home and restored the first poster. This exercises the controller path downstream of hardware sampling. It establishes no need to queue a stale action across page scopes. No production input routing changed. Scope and focus traces are in `artifacts/playnite-final-review/bumper-trace`.

**Unlink while review is pending (0.4.51).** In Playnite, a cross-owner confirmation expired after 120.2 seconds. The link remained visible and focus returned to its capture. A second run added an unrelated achievement reference to the isolated library during confirmation. The broker rejected the stale approval. Playnite now says “The capture library changed. Check the link and try again.” It refreshes automatically. A fresh attempt removed only the game and Undo restored it alongside both achievements. These runs used the real broker and WinUI host, the fixture Playnite worker and a copied profile. Capture Library's direct reversible edit has a separate path. Native evidence is in `artifacts/playnite-final-review/expiry-050` and `expiry-051`. The 266 widget tests passed on 0.4.51 and its production worker opened Home in the default-profile candidate.

**High contrast and tab-specific feedback (0.4.52).** At 150% text with high contrast, Recent activity and its dates used system foreground ink directly over the focused game's wallpaper. The host's native style adapter now provides the system canvas behind the content of a focus-driven background surface. It updates with the current system palette and releases that backing when ordinary colors return. The shared native fixture verified both transitions while retaining the same decoded artwork and focus presentation. Playnite also moved completion-status feedback beside the Information selector. The Captures, Sessions and Links tabs can no longer inherit an unrelated setup warning.

| Surface or consumer | Applicability and verification |
| --- | --- |
| Playnite Home and Library | Focus-driven wallpaper uses the high-contrast canvas. Native screenshots of the same Silksong fixture in Asphodel Ember and large-text high contrast show the heading, dates, foreground covers and fallback title. Back from Game Details restored the poster at both settings. |
| Playnite fixed Game Details hero and capture thumbnails | The change applies only to focus-driven backgrounds. Native Game Details screenshots retained the fixed hero and Play bar. Thumbnail artwork uses separate image controls. |
| SDK Gallery | The focused-background examples inherit the same policy. Fixed background examples keep their declared art. The shared presentation-surface fixture exercised a live contrast change and source retention. The Gallery itself was source-checked. |
| Other shipping widgets, templates and examples | No other production declarations of focused background artwork were found. The ordinary artwork, poster, media and focus presentation controls keep their existing paint paths. |

The 266 Playnite tests, 418 shell tests and 58 native presentation-surface checks passed. Evidence is under `artifacts/playnite-final-review/contrast-052`, including normal and high-contrast native captures and the verified running default-profile host and 0.4.52 worker.

The high-contrast screenshots exposed an outline to the right of Celeste on a three-game shelf. Native bounds identified the unpainted size reservation that maintains shelf height during lazy loading. The WinUI accessibility opacity policy had made it fully opaque. The host preserves explicit zero opacity in high contrast and reduced transparency. Positive partial opacity still becomes opaque. Playnite's shelf is the only exact-zero subtree declaration found in shipped widget and template WRSS. The fix keeps the reservation, the three poster bounds and their focus order. A before-and-after comparison found no change in the measured bounds and no remaining outline pixels beside the final poster. Keyboard Right on that poster stayed on Celeste. Native screenshots and measurements for both preferences are under `artifacts/playnite-final-review/contrast-reservation`.

The full native style suite exposed outdated fixture lookups for the spacer and group header after their presentations changed. The checks now measure the current spacer control and the visible group label. All 586 native style checks passed, including zero and partial opacity in both preferences. The final release host opened the 0.4.52 worker on the default profile. Its verified paths are in `contrast-reservation/default-verified.json`.

**Keyboard endpoints in the game chooser.** With 323 unfiltered games, End from game 1 scrolled the native list and moved keyboard focus to Cancel. It stayed there until the next input. The original focus and viewport sequence is in `artifacts/playnite-final-review/deep-picker/end-sequence.json`. The WinUI indexed collection now keeps the keyboard within the list while the first or last game is being realized, checks the current row lease, and focuses the available endpoint. Unmodified End uses native extent changes to keep the last row visible as estimated heights become actual heights. Wheel, analog or pointer input, a new navigation or focus departure releases that adjustment. Text editors, inline controls, modified keys and collections with undiscovered items keep their existing input paths.

| Consumer | Applicability and evidence |
| --- | --- |
| Playnite capture game picker | Native Home/End between games 1 and 323, wheel away from End, return to End and directional selection of two same-title editions. At 150% text both full titles and store labels were visible. The two receipts identify the expected distinct Steam and GOG game IDs on a copied capture. The native list exposed 24–25 row containers in the measured first and last viewports. |
| Playnite, Capture Library, Settings, Games and Apps, Path of Building, Spotify, YouTube, YouTube Music and the full application sample | Applicable to focused native indexed rows with known endpoints. The shared native selection fixture exercised flat lists, grids and grouped grids, activation, selection, mouse, keyboard, and endpoint navigation. It passed 207 checks. Individual music and launcher widgets were source-checked. |
| Discord and other nested reading controls | Inline controls retain their own keys. Row-only endpoint handling uses the current collection. The synthetic native reading-group fixture passed 18 checks. No live Discord account was used. |
| Templates and non-indexed controls | No template-specific keyboard endpoint handler needs migration. Native text, slider and browser keyboard behavior stays on its existing path. |

The 418 shell tests passed. The staged release host was launched and its Playnite Library 0.4.52 worker opened Home on the default profile. Evidence, including the store labels, saved receipts, return focus and process paths, is under `artifacts/playnite-final-review/deep-picker`.

**Partial link recovery (0.4.53).** An isolated eight-capture batch held one capture's copied metadata offline during activation. The other seven receipts were committed. Restoring the metadata and retrying produced exactly one additional receipt and preserved the earlier receipt IDs. At 150% text the previous 225-DIP dialog clipped the retry button and left focus on Close. Playnite now uses WRSS `height: auto` with a viewport-bounded maximum and one SDK focus request when the outstanding set is ready to retry. The outcome identifies the count and chosen game. The native panel remeasures when busy content is replaced by the recovery action. Normal and large-text runs, focus data, and the two receipt sets are in `artifacts/playnite-final-review/partial-053`.

| Modal sizing consumer | Applicability |
| --- | --- |
| Playnite link status | Opts into a centered, intrinsically sized dialog. Verified for a partial result, retry and successful dismissal. |
| Game Details, virtualized chooser, other shipping widgets and templates | Existing explicit dimensions remain in effect. The WRSS scan found no previous author declaration of `height: auto`. Anchored popovers retain their measured opening and placement. Overlay media viewers retain their aspect-ratio sizing. |

**Opening Details from Achievements.** The widget had published the new game with its Home tab and only then switched to Achievements. It now passes the requested section and the game details it already read into the initial modal state. The existing regression checks the first published game state. The production widget opened the ACE COMBAT 8 achievements and Back restored the originating library row in the native host. See `partial-053/live-qa`.

The final 0.4.53 package opened Home on the default profile with the updated host and styling runtime. Its package passed the integration hash checks. The widget suite passed 266 tests, the shell suite 418, the styling suite 39, and the native style fixture 586. The default profile and process paths are recorded in `partial-053/default-verified.json`.

**Cross-widget return assessment.** The current `WidgetIntentResult.Accepted` is returned when Playnite opens the picker, before selection, cancellation or retry. The routing ticket is released at that point. Treating it as a successful link would return too early. If direct return is adopted, the framework needs a scoped completion signal from the receiving widget for this interactive request. The host should bind it to the retained source and destination selection, allow one return, and discard it on a superseding user selection, source removal, worker replacement or session teardown. A partial result keeps the picker active. Direct navigation, ordinary share intents, notification activation and a user who manually leaves the picker must preserve their current destinations. No receiver should be able to choose another widget to activate via this signal. The only shipping source and receiver of the capture association contract are Capture Library and Playnite Library, respectively. This extension remains a separate decision.

### Activity, linking and introduction acceptance review (0.4.55)

| Agreed behavior | Verification |
| --- | --- |
| Home shelf, play action and recent activity | `artifacts/playnite-daily-home`, `playnite-unlock-grid` and `playnite-story-review/refined/navigation.json` cover the aligned shelf, dated category groups, independent achievement cards, session summaries and capture previews. Activating the three kinds of entry opened their respective destinations and Back restored their source items. The no-installed-games fixture retained activity and View library. |
| Game Details Home, Information and section navigation | The current modal, activity timeline, Information and return to the originating Silksong poster were sampled on the real local library. The screenshot series in `artifacts/playnite-final-review/transition-054` covers Home → Library, Library → Home, mouse opening of Details, controller section change and modal dismissal. The five 3.2-second sequences contain 67–74 foreground samples each, with at most 63 ms between consecutive samples. The incoming Library initially shows reserved poster tiles. On return, Home briefly shows its default summary while the focused game resolves. These samples establish visible states and return focus, and do not measure compositor frame rate. Repeated bumpers, interrupted transitions and exact tab focus are covered by `bumper-trace` and `playnite-home-plan/bumper-regression31.json`. |
| Sparse and accessible presentations | A never-played Celeste with no sessions or unlocks and denied capture access opened with Play and a single empty activity message. No permission prompt appeared. Back restored its exact poster. See `artifacts/playnite-final-review/sparse-054`. Hero space is reserved when the provider has a handle but no usable image. This is a reference case for the planned store-media fallback. Other runs cover partial providers, missing covers, Asphodel Ember, high contrast, 150% text, reduced motion and reduced transparency. |
| Media interaction | `artifacts/playnite-journal-strip` contains the native strip, transition frames, selection and viewport measurements at both text sizes. Opening an original, switching images, entering a clip, leaving and returning, and closing kept the dated list mounted and restored the selected thumbnail. The shared direct-entry behavior was also exercised with the native messaging fixture. |
| Searchable capture association | `artifacts/playnite-final-review/link-hint-054` shows the A / Link hint on the focused result. Search, Cancel and unfocused rows omit it. Both text sizes retained their row geometry, reached both ends of 323 games, and linked the selected edition. `deep-picker` records separate GOG and Steam IDs for the same long title. The suggestion and partial-batch runs cover query entry, suggestion acceptance without writes, explicit activation, feedback, recovery of the remaining capture, and preservation of earlier receipts. Returning to Capture Library through the tray restored Link to game on the original capture. Automatic navigation to the sender after dismissal remains the separate proposal above. |
| Duplicate links, grouped unlink and Undo | The store suite in `artifacts/playnite-grouped-undo` covers inherited links, repeat attempts, rollback and index rebuild. Native receipts and focus records in `playnite-missing-cover/unlink-native` and `playnite-final-review/expiry-051` cover direct removal, independent achievement references, stale approvals, retry and scoped Undo. |
| Introduction | `artifacts/playnite-introduction-refresh/fresh-final/check.json` records 32 native checks, including completing the controller tour, shortcut rehearsal, task switching, hiding, resuming, and input-family changes. Separate interrupted-process, Settings-replay and bare-install walkthrough profiles persisted the complete step. The controller fixture subsequently replayed the introduction intentionally. |

The capture chooser is the sole current opt-in to the new WRSS `interaction-state-source: indexed-item` declaration. The host applies native item focus and press styling to opted-in, nonfocusable decorations. Focus movement updates only registered decorations on the realized row. Ordinary controls, other widgets and templates use their own existing interaction state. Native style validation covers opting in and removing the declaration from a retained element. The widget suite passed 266 tests on a full rerun after a timeout during concurrent builds. The shell suite passed 418 tests, the style compiler suite 39, and the native style fixture 589. The default-profile release host opened the 0.4.54 worker. See `link-hint-054/default-verified.json` for paths and process identities.

The long-timeline fixture supplied 360 sessions and 120 unlocks across 120 days. Game Details exposed its bounded 96-entry journal. The native tree contained 21 realized rows near the start and 19 at the end. End reached visible row 95. Home returned to the first date's capture thumbnail, and Back returned to Silksong on the shelf. The focus records are in `artifacts/playnite-final-review/long-timeline-054`.

The final widget-only 0.4.55 change adopted the verified content-sizing behavior for the existing short capture list and session review. In the two-capture fixture the list measured 408 physical pixels tall and the review 552. At 150% text they measured 464 and 576, with both footer actions reachable. Back returned through the selected session to Link captures. Longer paged lists, Game Details and the media viewer retain their own size policies. All 266 Playnite tests passed on this version. `artifacts/playnite-final-review/capture-dialog-055` contains both screenshot sets, bounds, package verification and the running default-profile worker path.

### Optional launcher integrations — feasibility, November 2026

The table records features considered for prioritization. The recommended add-on setup flow below is agreed work and remains open in 0.4.57. Connection setup installs and updates WidgetRail for Playnite. The first-connection recommendations and the Settings → Add-ons entry still need implementation and verification on a profile missing the providers. The installed extension manifests on the development profile include GameActivity, Playnite Achievements, Steam integration and SteamGridDB. Neither Steam Trailers nor Steam Reviews Viewer was present during discovery.

| Feature | Evidence and proposed owner |
| --- | --- |
| Steam reviews | Reviews Viewer exposes a WPF theme control and manages its coordinator and LiteDB cache privately. A native widget Reviews section can use Valve's documented `IUserReviewsService/GetAppReviews/v1` on demand. A keyless read for Silksong (1030300) returned a score summary, one review and a paging cursor. Map the Playnite game to a verified Steam app ID. Format review text as untrusted text, with language selection and bounded page caching. [Add-on source](https://github.com/darklinkpower/PlayniteExtensionsCollection/blob/3a4edb9d196d7ed032deef5ba725ead7caa67600/source/Generic/ReviewViewer/ReviewViewer.cs) · [Valve API](https://partner.steamgames.com/doc/webapi/IUserReviewsService) |
| Trailers and store screenshots | Steam Trailers 2.17 registers Playnite game-menu actions and opens a WebView. Its SD/HD path selects the first movie's WebM field. The US/English store response for Silksong returned two movies with HLS H.264 and DASH H.264/AV1 manifests, and ten screenshots. WidgetRail public media supports bounded direct files, not adaptive playlists. Start with a store screenshot gallery and user-selected, verified local trailer from Extra Metadata Loader where available. Streaming needs a separate playback prototype. For non-Steam copies accept a verified store link or an explicit match. [Trailer script](https://github.com/darklinkpower/PlayniteExtensionsCollection/blob/3a4edb9d196d7ed032deef5ba725ead7caa67600/source/Generic/SteamTrailers/SteamTrailers.psm1) · [Extra Metadata Loader](https://github.com/darklinkpower/PlayniteExtensionsCollection/wiki/Extra-Metadata-Loader) |
| Other candidates | HowLongToBeat could place main-story and completionist estimates near Play. Verify the supported game-scoped data contract. DuplicateHider could supply an existing preferred copy for a store/version chooser. Verify compatibility with widget-managed variants. Ludusavi could expose last backup and Back up before launch, with configuration and restore treated separately. Steam News and Players Viewer suggests a distinct What's new surface for official news. [HowLongToBeat](https://github.com/Lacro59/playnite-howlongtobeat-plugin) · [DuplicateHider](https://github.com/felixkmh/DuplicateHider) · [Ludusavi](https://github.com/mtkennerly/ludusavi-playnite) · [Steam News and Players Viewer](https://playnite.link/addons.html#NewsViewer_15e03ffe-90f6-4e8e-bd4d-94514777481d) |

**Optional setup flow.** Offer an Install in Playnite action for each supported, known catalogue ID in first-run setup and Settings. Detect installed, disabled and loaded state through `IAddons`. Target the selected Playnite installation and profile. The official `playnite://playnite/installaddon/<AddonId>` handler selects a compatible release, asks for approval, handles the licence when applicable, queues the installation and offers a restart. Recheck the actual provider after reconnect, then return focus to the originating action. A cancelled prompt, pending restart, disabled add-on, unavailable catalogue, or add-on needing its own configuration is a distinct outcome. Defer restart during gameplay. Validate controller handoff on installed and portable Playnite before shipping. The existing WidgetRail setup service manages our own bundled and verified integration separately. [Catalogue install URI](https://github.com/JosefNemec/PlayniteAddonDatabase#readme) · [Playnite installer implementation](https://github.com/JosefNemec/Playnite/blob/master/source/Playnite/App/PlayniteApplication.cs) · [SDK add-on discovery](https://api.playnite.link/docs/api/Playnite.SDK.IAddons.html)

**Recommended add-ons.** Offer highly recommended add-ons once after the first successful connection to the chosen Playnite profile. Start with GameActivity for recorded play sessions and Playnite Achievements for per-game unlocks, recognizing compatible providers already supplying the same data. Explain each benefit, let the user select installations and offer **Skip for now** with “You can add these later in Playnite widget Settings → Add-ons.” Remember the decision per profile. Keep setup available in Settings. Omit unavailable features from normal browsing and retain all available types in a partially populated game. Distinguish absent data from an in-flight request or a refresh failure with usable cached data. Prefer a direct, bounded provider integration for straightforward public data such as Steam reviews. Retain add-ons for complex cross-store integrations and locally recorded history.

**Game Details store media.** Use one stable, aspect-preserving stage and a horizontal strip of trailer stills and gameplay screenshots beneath it. Replace the repeated header artwork when usable store media exists. The selected trailer or screenshot can occupy more than half the visible height of the Game Details modal. Keep the existing game play bar, including its actions and stats, visible with the thumbnail strip in the opening view. Measure the bar and strip at the current text scale before sizing the media stage. Preserve the media aspect ratio and keep section navigation directly reachable. Verify on a shorter viewport and at 150% text size that switching between a trailer and a screenshot leaves the play bar in place. On a fresh opening from Home or Library, put the first playable trailer first in the strip, select it and begin muted playback as soon as it is ready. Focus its thumbnail so Left and Right browse immediately. Up reaches the playback controls. A single trailer without a strip opens on its playback control. If only screenshots are available, select and focus the first screenshot. If no store media is available, focus the active details tab. Keep Play directly reachable from Details and through the existing Home shortcut. Left/right or pointer selection changes the preview in place. A/Enter expands a selected screenshot. A late media response must respect navigation that has already moved focus or changed the selection. Returning from fullscreen or a child view restores the prior selection, focus and playback state. Provide fullscreen and return to the same thumbnail on Back. Mark trailer thumbnails with a play glyph and an accessible name. Keep local user captures in their existing section. With a single item, omit redundant strip navigation. Without usable media, retain a compact game header. Fetch small thumbnails first, load the selected full asset on demand, keep one active player, and cancel obsolete work on selection, game switch or hide. Reuse host focus-presentation and native player lifecycles. Check fixed stage placement, stable selection and return-focus behavior before considering an SDK extension.

The installed Steam Big Picture client showed Silksong's muted trailer with pause, unmute and fullscreen commands. Selecting the adjacent gameplay screenshot brought it to the center and removed the video commands. Big Picture used large neighboring slides. The desktop storefront for the same game showed a fixed preview above a strip with a play glyph on the trailer and several distinct gameplay thumbnails visible together. Use the desktop composition in Game Details. Bound the 16:9 preview so the section rail and the beginning of activity remain visible at the standard widget size. On wide layouts use the neighboring space for Play, last played and playtime. Borrow Big Picture’s context-sensitive controller hints and clearly outlined selection. From the media strip, keep a direct route to Play and a direct route down to activity without traversing the remaining thumbnails. The US English store data contained ten screenshots and two trailers. A standalone muted Windows probe played the release trailer through both HLS/H.264 and DASH/H.264, reporting a 95.4-second duration, 640×360 video and an advancing clock. A second pass supplied the initial manifest as a stream and supplied the requested initialization and media segments through `AdaptiveMediaSource.DownloadRequested`; HLS also requested a child manifest. This establishes native feasibility on this machine. It does not qualify playback in WidgetRail, visible frames, or transport policy.

**Adaptive playback acceptance.** Playnite owns the verified game-to-store identity and the media ordering. The SDK/protocol and host own an explicit adaptive source, separate from the single-file public source used by Discord. Start with clear on-demand H.264 trailers. Inspect and bound manifest and nested-resource resolution, redirects, per-request and aggregate bytes, track dimensions, duration, concurrency and cancellation. Demonstrate that unsupported manifests and failed resource requests cannot cause a native network fallback. Test seek, slow/offline playback, rapid navigation, fullscreen and resume, and verify no background traffic after leaving the game. Compare neighboring items and screens on controller, keyboard and mouse including reduced motion and large text. [Windows adaptive streaming](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/adaptive-streaming)

**Trailer Hero reference and signed-out YouTube fallback.** TrailerHero looks up movies by Steam app ID, prefers a highlighted movie, and recognizes H.264 HLS and DASH. Its YouTube search ranks title matches and trailer wording and penalizes fan-made videos. In Playnite, prefer a publisher-supplied video ID or official trailer link when Steam has no playable trailer. A search match needs enough evidence to distinguish the game, sequel and edition before autoplay. Keep Steam screenshots available if video discovery fails. [TrailerHero Steam resolver](https://github.com/LoZazaMastro/TrailerHero/blob/fe46415c620edfec4c226dba639d39ca308b4126/main.py#L2176-L2289) · [YouTube ranking](https://github.com/LoZazaMastro/TrailerHero/blob/fe46415c620edfec4c226dba639d39ca308b4126/main.py#L3073-L3134)

Use YouTube’s privacy-enhanced embedded player in a dedicated signed-out browser context. Keep it independent of the user's browser, YouTube and YouTube Music sessions. A login requirement, embedding restriction or unavailable video advances to the next playable item or screenshot without prompting for credentials. Keep controller focus in Game Details and release playback when the user leaves. Verify the real WebView2 embed origin, muted autoplay, controls, fullscreen return and teardown. [YouTube embedding guidance](https://support.google.com/youtube/answer/171780) · [IFrame player events and errors](https://developers.google.com/youtube/iframe_api_reference#Events)

In a public US-English Steam metadata check, Silksong returned two trailers and ten screenshots. Team Cherry’s Silksong site linked release video `6XGeJwsUP9c`. In a fresh Edge browser context, the privacy-enhanced embed reported playing and its clock advanced past one second with muted audio and no sign-in. WidgetRail WebView2 playback remains to be verified during implementation. [Steam response](https://store.steampowered.com/api/appdetails?appids=1030300&cc=us&l=english) · [Team Cherry’s game site](https://hollowknightsilksong.com/)

### November 21 store media implementation and qualification

Playnite Library 0.4.56 and integration 1.3.5 build and pass package verification. The detailed game projection resolves the built-in Steam plugin GUID and numeric GameId. Other providers can supply an unambiguous Steam store link. An older add-on can supply that link through its existing details response. The installed add-on returned one official store link in Silksong's fourteen links. Library traversal incurs no store requests. Each Game Details opening owns its cancellable metadata query. A delayed response from a closed game is covered by the widget suite.

| Consumer | Ownership and qualification |
| --- | --- |
| Playnite Game Details | Widget orders highlighted H.264 trailers before screenshots. Protocol 140 adds public thumbnail declarations and a checked DASH source. WinUI owns the strip, selected member, decoder and fullscreen return. Both Steam trailers and ten screenshots appeared in the production widget driven by an isolated Playnite provider fixture. Native controls played the release trailer, expanded a screenshot and returned focus to its tile. |
| Discord attachments and inline video | Existing direct-file, poster and gallery declarations omit public thumbnails and adaptive streaming. The legacy native gallery passed 34 checks with its original arrow and bumper navigation, pointer activation, delayed source, fullscreen and suspension. No live Discord account was used. |
| Playnite saved captures, Capture Library and Game Help | Local files and saved-capture grants keep their current presentation and source admission. The store viewer introduces no capture-library reads. |
| SDK sample, templates and other shipping widgets | No other thumbnail-gallery or DASH adopter identified. The SDK contract suite and checked transport suite cover the unchanged defaults. |

Native review found a focus handoff bug. At 150% text, WinUI could focus Home when replacing the loading control with the player, scroll the header away and remember Home as the user's selection. The host now preserves the replacement when the prior focused element is also the incoming initial target in the same scope and occurrence. Direct navigation to Information during a 3.5-second detail delay retained Information when the media arrived. The 150% high-contrast capture shows Pause focused with the strip, Play bar and section navigation visible. The guide now yields the bumpers to the enclosing details tabs. The store viewer test also exercised failure of an autoplay trailer and fallback to its first screenshot.

The DASH player reconstructs a static single-period manifest using synthetic segment addresses. A WinRT HTTP filter rejects native network access. Only mapped same-origin segment requests can reach the existing public transport. The implementation currently plays supported H.264 DASH and direct MP4 sources. HLS-only titles fall through to stills. Right-trigger seeking advanced live Silksong playback, fullscreen browsing returned to the newly selected thumbnail, and hiding before source resolution cancelled the pending original. Remaining qualification includes cancellation during segment transfer, smaller viewports and repeated overlay hide/return.

The sealed embedded-media validation surface played Team Cherry's public YouTube trailer muted past one second in its private WebView2 media profile using the privacy-enhanced iframe. Signed-out provider discovery, production gallery integration and restricted-video fallback remain open. The user's default profile is still on the preceding candidate. The packaged-widget test reached the normal add-on update prompt for 1.3.5. Full widget UI checks used the isolated provider and actual Steam media while the user's Playnite installation remained running.

### Game Details trailer qualification, Playnite Library 0.4.57

Playnite accepts an explicit trailer link in the game's metadata. When the Steam response has no supported native trailer, it can also inspect the game's published website for one unambiguous YouTube trailer labelled with the matching game title. It uses the existing anonymous, bounded public transport. The widget does no title search on YouTube. Team Cherry's actual website supplied the Silksong release trailer in the publisher-only native test.

| Consumer | Result |
| --- | --- |
| Playnite store media | Protocol 141 binds a declared embedded session to a gallery member. Native Game Details played the real signed-out trailer, acknowledged play and mute changes, returned from fullscreen to its invoking control, switched to a Steam screenshot and reopened the trailer. A denied video with screenshots selected the first screenshot. A denied video alone restored the compact header and Play. |
| Shared thumbnail gallery | The host fits embedded video into the same stage as a still. Its native command bar overlays the stage, and image controls leave the thumbnail strip clear. On re-entry the player remembers a thumbnail by occurrence ID within the same gallery owner. Fullscreen and source switching keep separate focus behavior. |
| Discord, Game Help, Capture Library, SDK examples and templates | Existing direct files, legacy galleries, captured media and standalone embedded surfaces have no publisher trailer declaration. Playnite is the only shipping caller of the embedded gallery source. The existing gallery fixture covers the shared player's focus, pointer, failure and return paths. |

At 1280 × 720 DIPs and 150% text the Play bar and strip stayed in place from YouTube to screenshot. The sections were below the initial viewport. RB brought Information into view. Two hide/show cycles retained the selected shot and the next Right press advanced one tile on each return. Native pointer selection used movement followed by a click after controller input. The stationary cursor guard intentionally intercepts an immediate click without movement. On the live Steam DASH path the host cancelled and drained a segment stalled after its first byte. A fresh private WebView2 player and provider fixture exercised unavailability without involving a signed-in account.

Native screenshots and results are under `artifacts/playnite-store-gallery/live/qa-*`. The suites recorded 272 Playnite widget passes, 107 public transport passes and 124 community application passes with six opt-in installation tests skipped. The community package includes the verified 1.3.5 add-on. At qualification, the default profile was running add-on 1.3.4 and Silksong was active. Deployment requires a coordinated Playnite and WidgetRail restart.

After Silksong exited, the default profile upgraded from Playnite Library 0.4.55 to 0.4.57. **Update integration and reconnect** restarted Playnite and installed add-on 1.3.5. All six installed add-on files matched the packaged hashes. The shipping host and production worker opened the real Silksong Game Details with two trailers, ten screenshots and the Play bar. The deployment receipt is `artifacts/playnite-store-gallery/default-057-verified.json`.

The 0.4.58 shared fullscreen gallery places the available 52-DIP chevrons over the media at the side midpoints. Both attachment and Playnite gallery fixtures measured the fullscreen image viewport against all four edges. They checked focus, endpoint navigation, fullscreen return and high-contrast button backings. A failed sole autoplay trailer retired its stage and a different game restored its media. The widget and application suites exercised the contextual add-on command, connected profile, catalogue ID, and installed/stopped cases. The official Playnite installation confirmation was not exercised live on the default profile, which already had both providers. Native results, screenshots and the running release receipt are in artifacts/playnite-store-gallery/fullscreen-frame.

### Fullscreen gallery transition

The WinUI player keeps the inline frame and its thumbnail scroller mounted, reserves the preview slot, and moves the live XAML media controls into the same window's existing fullscreen host. Motion uses the fitted picture bounds in both directions. Return uses the current gallery member and restores its thumbnail when the strip initiated fullscreen.

| Consumer | Verification |
| --- | --- |
| Playnite store media | Normal motion and large-text reduced-motion fixtures checked strip coordinates, native video element and decoder identity, stable native video size, Back during entry, and return after browsing. Release film sequences cover opening and returning for a Silksong trailer and screenshot. |
| Playnite capture and Discord attachment modals | Both use the shared gallery. The overlay-modal and high-contrast attachment fixtures exercised navigation, image/video changes, return focus and clipping. No live Discord account was accessed. |
| Standalone YouTube Video, other non-gallery players and widget templates | No widget or SDK change. Standalone players retain their existing placement. The private publisher-trailer gallery smoke check exercised the existing embedded player and fullscreen return. |

Resizing a native video during the animation exposed transient clipping in the recorded frames. The final implementation keeps the gallery's MediaPlayerElement sized to the decoded video inside an in-tree Viewbox. WinUI scales that element without a second renderer or a per-frame layout loop. Evidence is under `artifacts/playnite-store-gallery/gallery-transition`, including the four sequences in `fit-youtube-provider-normal`.

### Timeline capture preview in 0.4.59

Playnite Game Details is the shipping consumer of the SDK capture strip. The SDK now uses the open thumbnail as a close target and the shared theme gives the preview the width of its rail with a one-pixel logical border. The indexed host routes Escape to an action rail's own pressed-B shortcut when the focused media has finished handling Back. Discord message reading groups, attachment galleries, the store trailer strip and templates do not use this capture strip.

At normal and 150% text the native preview measured 955 × 537 physical pixels over a 955-pixel rail. The fixture exercised thumbnail switching, B, Escape and reactivation. The large-text clip also advanced its playhead, entered fullscreen, returned to its row and closed by pointer. The SDK suite covered single-capture opening and closing. Screenshots, geometry and the default-profile launch receipt are in `artifacts/timeline-preview-width`.

### Game Details store thumbnails in 0.4.60

The initial trailer thumbnail retained focus after a right-stick scroll, but every subsequent directional press waited for the scroll to find a focus target. The shared WinUI gallery now supplies its native buttons to the presenter's existing visible-control selection. The Playnite store gallery is the shipping consumer of public gallery thumbnails. Timeline capture strips use SDK action surfaces, and Discord attachment galleries use their own previous and next controls. Neither requires a widget change.

The 16:9 tiles now measure 168 × 94.5 DIPs. The widget displays the game's library source as Platform after Last played in the action row. A Silksong fixture exercised small scrolls, both endpoints, direction reversal, D-pad movement and left-stick exit at normal and 150% text. The original candidate reproduced the blocked D-pad press. Screenshots, input results, the shared scroll-focus suite and the default-profile launch receipt are in `artifacts/game-details-thumbnails`.

### Store media carousel in 0.4.61

Playnite opts its store gallery into the protocol 142 inline carousel. It supplies Steam screenshots and posters, plus a higher-resolution poster URL for a publisher YouTube trailer. The SDK retains the existing gallery overload. WinUI owns placement, input, motion, cancellation and still-image decoding. Playnite and Discord attachment galleries retain their standard layout. The timeline capture strip and standalone players have their own presentation contracts. Templates require no migration.

The native Silksong preview aligned its 774–775-pixel selected frame to the leading edge, midpoint and trailing edge of a 1411-pixel rail. Verification covered click-to-select, scrolling, fullscreen return, reduced motion at 150% text, high contrast, an unavailable trailer, a larger-than-thumbnail neighbor decode, and signed-out embedded playback. A sampled direction reversal and endpoint screenshots are under `artifacts/game-details-carousel`. One early fullscreen run failed a compound assertion. Separate checks for element and decoder identity, geometry, focus and playback passed in 15 consecutive runs. The original failed condition could not be isolated.

**Thumbnail tooltips.** The shared WinUI media gallery keeps accessible labels on thumbnail buttons and omits their hover popups so artwork stays visible. Playnite Game Details uses this strip. Playnite and Discord attachment galleries use the same player for playback-control hints. Timeline capture strips use SDK action surfaces. Browser and shell chrome have separate tooltip call sites. Native gallery checks covered thumbnail labels, the absence of a tooltip, and playback-control colors at normal and high contrast. Release hover screenshots at 150% text and the default-profile launch receipt are in `artifacts/gallery-tooltip-theme`.

### Game launch foreground investigation

The default-profile 0.4.61 candidate and Playnite Desktop 10.60 launched the same installed Steam copy of Hollow Knight: Silksong through the same Playnite instance. Both reached Playnite’s game-started event. From Game Details the game process appeared at 1.6 seconds and its main window at 5.7 seconds. WidgetRail retained foreground for the entire 16-second observation. From the foreground Playnite desktop window, the game became foreground at 3.2 seconds. These single-launch timings establish the order of events. They do not measure comparative launch performance. Both games were closed normally. The traces are under `artifacts/playnite-launch-handoff`.

The production widget requests `CloseOnConfirmedSuccess`. Its community application service discards that argument and returns only `RequestAccepted` after the add-on calls `api.StartGame(id)`. The local response confirms admission. This path bypasses the built-in app library broker's close effect. The add-on already receives game-started and stopped callbacks for capture integration. The host also dismisses the overlay on a confirmed external foreground change, which never occurred in the widget trace.

Proposed first increment: correlate the accepted launch with a specific game start in the connected Playnite profile, including callbacks that arrive before the launch response. Carry the verified process instance through a host-owned, bounded foreground handoff. Resolve its actual visible game window, using child/helper evidence when necessary. Reuse the task switcher's native activation and foreground checks with launch-specific lifetime rules. Dismiss the overlay on a successful handoff. End the pending handoff if the user changes focus or reopens the overlay. Handle an already-running game, cancellation, provider failures, a delayed or missing window and launcher prompts without duplicate launches or delayed focus theft. Keep observation independent of capture permissions and capture history.

The proposed first UI state is “Launching [game]…” on the selected game's Play action. A confirmed game window ends that state. For a launcher prompt or an unconfirmed start, offer a relevant foreground action when available. Reserve any richer transition for a verified handoff.

**Delivered in 0.4.62.** Playnite Library shows “Launching…” and observes the exact launch through its connected add-on. The widget waits up to 25 seconds for a window. The broker binds its one-use ticket to the interactive action and lifecycle. It validates the reported window using the Windows task-window eligibility rules, PID and process creation time. The shell checks the original visible session, then uses the established controller-release, close-motion, permission-delegation and activation sequence. Window discovery inspects one handle. Looking up the whole application catalogue in an early candidate exceeded the task switcher's two-second freshness check. The final candidate retains that check.

| Consumer | Verification |
| --- | --- |
| Playnite external launches | Silksong reached foreground at 3.1 seconds in the final release trace. An explicit return to the running game reused its process with one Playnite launch total. Hiding and reopening during startup cancelled the pending switch. Granting and declining switch permission were tested on an isolated WidgetRail profile. |
| Host task switcher | Its original full-motion, reduced-motion, reopen and expiry fixtures passed all 51 checks. |
| Built-in app library launches, ordinary widget window switching and SDK templates | Existing operations and deadlines remain in use. The broker suite verified scoped window tokens alongside the new process-reuse and replay checks. An unrelated pre-existing capture capability-vocabulary assertion still fails. |

An unverified launch displays guidance to check Playnite or the launcher. The widget does not guess which third-party launcher prompt belongs to the game. Hardware and storefront coverage for the live handoff is limited to this Windows machine and Silksong through Steam. Evidence and deployment receipt are in `artifacts/playnite-launch-handoff/candidate`.

### Carousel image focus and idle playback controls

The shared WinUI player owns the timers, focus handoff and fullscreen placement. Playnite Library 0.4.62 and add-on 1.3.6 use their existing contracts.

| Consumer | Behavior and evidence |
| --- | --- |
| Playnite Game Details carousel | Inline screenshots use thumbnail navigation. Fullscreen entry from a thumbnail focuses Fit. Native fixtures covered zoom, controller pan, gallery arrows, interrupted entry and thumbnail return at normal and 150% high-contrast settings. |
| Steam and publisher YouTube trailers | Release keyboard and pointer checks exercised focused controls hiding and waking during playback. The fullscreen check exposed the resident browser being covered by its own gallery. The host now paints that browser above the fullscreen backdrop while its controls retain input. The signed-out trailer remained visible and playing through idle and wake, then returned to its invoking control. |
| Playnite and Discord attachment galleries | Shared attachment-gallery fixture covered ordinary inline image controls, delayed selection, fullscreen item changes, arrow availability after idle and return. |
| Game Help, Capture Library, Discord inline clips and SDK Gallery | Native player consumers inherit the playing-video idle policy. Ordinary inline image controls retain their existing behavior. These widgets were checked at their entry points and were not individually driven. |
| Standalone embedded fullscreen, other widgets and templates | The existing fullscreen player shares the idle policy for a stationary pointer. No SDK or protocol change. Standalone embedded fullscreen was not independently driven in this pass. |

Results and screenshots are under `artifacts/gallery-idle-controls`.

### Running games and the launch splash in 0.4.63

The Playnite add-on 1.3.7 seeds its active-game tracker from the Playnite database at integration startup. Playnite's start, cancel and stop callbacks maintain it thereafter. Each response on the existing active-widget event feed includes the current running IDs. Opening the widget, reconnecting and recovering a missed event therefore use the same snapshot. An immediate snapshot does not occupy a long-poll slot. The widget republishes indexed presentation only when the set changes. The data describes games tracked by this Playnite instance.

For a widget launch, the add-on checks the selected target and the other active games on the Playnite UI thread before calling StartGame. A conflict returns a name, count and revision without starting anything. Cancel is initially focused. Launch anyway revalidates the selected game and the revision. A changed set can require a fresh confirmation. Returning to a game Playnite already tracks bypasses the conflict. The original command registry deduplicates repeated requests.

After admission the widget uses its existing full-size background surface, the selected game's artwork and a loading indicator. The artwork has a theme-colored fallback. Guide can dismiss the overlay while the game starts. A tested widget-local Stop waiting action was rejected. Holding the originating action until the foreground handoff completes is necessary for the existing shell authority. Extending foreground authority solely for a splash button would weaken this boundary.

The implementation belongs to Playnite Library and its add-on. The SDK, protocol, host, broker and task switcher require no changes for this phase. Release checks and native screenshots are under `artifacts/playnite-launch-scene`.

### Independent launch entrance in 0.4.64

The 0.4.63 splash declared a page transition group only on the destination. WinUI requires a group in both committed views to animate navigation. The launch splash now declares a one-shot fade on its background surface. WRSS and the WinUI renderer own this optional effect. It uses the existing compositor opacity animation, includes foreground text and artwork, and keeps no outgoing game-details tree or decoder. Game launch, focus and foreground activation do not wait on playback.

| Consumer | Applicability |
| --- | --- |
| Playnite launch | The launch scene opts in. Native Silksong frames show the artwork fading into place with page animations disabled. Reduced motion displayed it immediately. |
| Other background surfaces, including the regular Playnite library, SDK samples and first-party widgets | Their base styles omit the property and keep the default. Ordinary updates and artwork replacements use their existing policies. |
| Modal, section and page navigation, other controls and templates | No navigation declaration, global motion preference, or template change. |

Recordings and build results are in `artifacts/playnite-launch-motion`.

### Currently Playing and the announcement reader

Playnite Library 0.4.65 composes the existing Home reveal grid with a second focus-reveal region. Up from the poster rail enters the running game and Down returns to the rail. A Playnite stop event removes the corresponding shelf. Multiple running games share one shelf with a selector. The add-on supplies names and known process start times. Return to game uses a dedicated return-only request. Playnite checks its current running flag on the UI thread before issuing the existing handoff receipt.

The widget resolves news using the same verified Steam app ID as store media. A non-Steam library entry can provide a single unambiguous Steam store link. Feed acquisition uses the bounded same-origin downloader. A worker caches up to eight public RSS feeds and atomically retains up to eight 1 MiB files on disk. Successful entries are fresh for six hours. A failed refresh can reuse the previous feed. No news is requested for an unmapped game. The reader groups by month in windows of five headlines and presents the focused article on the right. Original posts and the Steam archive use the regular web intent.

| Consumer | Ownership and behavior |
| --- | --- |
| Playnite Home | Game artwork, resume, news, capture, achievement and session destinations are widget decisions. Rendering and input reuse ordinary SDK controls. |
| Game Help 0.1.10 | Handles the standard game-help intent. Opens the composer with the game name, preserves the draft and waits for Send. |
| Other intent senders and providers | The bridge exposes registered handler signatures from the enabled catalog. WinUI checks only actions whose WRSS opts into conditional visibility. Routing still resolves and validates the request on activation. Ordinary intent buttons retain their existing behavior. |
| Other focus presentation surfaces, SDK examples and templates | The reading-pane layout is opt-in. The inline fragment becomes a scrollable right-hand article while the headline retains focus. Existing focus summaries retain their flow layout. |

Release checks and native screenshots are under `artifacts/now-playing`.

### Currently Playing opening and snap positions in 0.4.66

Home enters the Game details card while a game is running. Details, optional news and captures have independent resource snapshots. Small cards retain their element IDs and new destinations append to the current game's row. Game Help uses the existing installed-handler check. The focused game's summary occupies the same slot as the poster summary. The foreground uses the theme's acrylic panel brush with the native accessibility fallback.

The first stop reveals 36 DIPs of the poster stage. Down enters the poster collection at its ordinary Home stop. Recent activity keeps its full reading viewport. Initial placement, normal motion, interrupted movement and keyboard direction changes were checked in the Release candidate with Silksong. At 2560 × 1440 the visible poster artwork measured 28 pixels at entry. Its top after Down was at pixel 633, compared with 634 for ordinary Home.

Hiding Home retains the last running snapshot in worker memory. A zero-wait add-on read reconciles it on activation. The event client can recover from a saturated long poll using its original cursor. An unchanged process-qualified catalog revision skips the remaining game pages. The named category collection is validated separately. The shelf shares Recent Activity's capture read and only queries older matches when needed. Steam announcements reuse the existing bounded disk cache.

| Consumer | Effect of the shared layout change |
| --- | --- |
| Playnite Home, running game | Uses one multi-stop reveal grid and the measured top of its poster stage. |
| Playnite Home, no running game, and Game Details | Use the existing two-position reveal. |
| Other shipping widgets, first-party widgets, SDK samples and templates | No opt-in declarations. Their navigation and layout use their existing controls. |

Native screenshots and input timings are under `artifacts/now-playing/release-shelf-native*`. Times cover simulated keyboard input to an accessible window, control or focus. They do not measure the physical Guide button or display scanout.

### Currently Playing card layouts in 0.4.69

Game Updates presents the latest cached announcement as a headline, an excerpt and optional publisher artwork. Sessions uses the running snapshot's local start time and the previous session in Home's available history. The trophy case retains up to three earned badge references for the running game. Tracking moves the tracked achievement to the lead position. Ask Game Help uses the receiving widget's illustration and its existing installed-handler intent. These layouts share the existing game-scoped resources.

Home's expanded focus summary gives the title the remaining space between Play and the play statistics, with single-line ellipsis and the full accessible title. Pressing Up from a poster enters the shelf's remembered card group. Opening Home still targets the first card.

Native checks covered the news reader, three Silksong award images, Sessions, Game Help, and navigation across three titles including Ace Combat 8 at increased text scale. The default-profile check in 0.4.69 kept all three poster tops at pixel 631. Badge files remain available while Home is retained behind Game Details. Evidence is under `artifacts/playing-cards/release69*` and `artifacts/playing-cards/rc68-*`.

### Poster sheen and anchored navigation scope

The Steam recording shows a light sweep over the focused artwork and a collection scrolling under a relatively stable focus position. These are independent. The following changes are proposed.

**Element focus styling.** Settings → Focus animation controls how the host draws and animates the focus border (Settle, Fade, None). WRSS owns the widget's visual response to focus. Playnite already declares a focused poster scale of 1.10. Add a declarative one-shot sheen to the semantic poster/artwork target so authors can use scale, sheen, or both. The host clips and animates a non-interactive gradient with composition. Play it on actual focus entry. Snapshot updates, artwork arrival, focus inside the same item and recycled containers must not restart it. Retire the previous sweep on navigation, hide or unload. Respect global reduced motion, Windows animation preferences and high contrast. Prototype and verify the effect on light, dark and missing covers, including rapid reversals. Estimate 2–3 days with native verification.

**Anchor contract.** The widget author opts a collection into anchored directional navigation through the SDK. The WinUI host owns measurement, virtualization, focus and cancellation. Start with two independently specified positions along the collection's scroll axis: the desired point in the usable viewport (0% through 100%, measured after padding or pinned controls) and the point on the focused item or grid row (leading, center, trailing). Resolve leading/trailing in reading order. Center-to-50% gives the Steam-style rail. Top-to-25% gives a reading position with room for the content below it. Recompute from measured bounds on resize, text scaling and responsive column changes. Clamp to real scroll extents. Short lists stay still. Keep the focused control visible when a row is taller than the usable viewport. No artificial edge gutters by default.

| Playnite surface | Starting position for the prototype | Ownership and interaction |
| --- | --- | --- |
| Home poster rail | Poster center at 50% across | First SDK/WinUI indexed-collection consumer. Preserve vertical snap positions between Currently Playing, the poster rail and Recent Activity. Estimate 3–5 days for the generic rail implementation. |
| Library grid | Selected row center near 45–50% down | Preserve column on Up/Down. Left/Right within the same row moves focus horizontally without vertical scrolling. |
| Home Recent Activity | Selected row top around 25% down its feed viewport | The existing outer snap reveals Recent Activity. Its two-column grouped collection then owns movement within the feed. Keep same-row moves steady and date boundaries legible. Test the grid and feed together, estimated 3–5 additional days. |
| Game Details activity timeline and achievements | Focused entry top around 20–25% below the tab strip | The modal and header retain their existing reveal controls. The active inner list owns its scroll axis. Expanded or oversized entries need free reading scroll. Restore the same entry on return. |
| Game Details media and other tabs | Thumbnail center at 50% across. Other focusable row groups use a reading position where appropriate. | The media gallery owns its thumbnail strip, separately from indexed lists. Check selection, autoplay, fullscreen and return. Information, Links, Captures and Sessions can use ordinary scroll surfaces, so inspect each actual layout; plain text and individual media controls must remain freely readable. Allow roughly 2–4 additional days for Details adoption and native tests, subject to the gallery and ordinary-scroller findings. |

The percentages are widget-author starting values. A future user-facing preference could expose presets if testing supports it. Directional controller and keyboard input move real focus onto the selected item. Pointer and wheel scrolling stay free; directional movement resumes anchoring. The outer section reveal and inner collection must never issue competing scroll requests for the same axis and viewport. The newest input supersedes queued movement. Explicit entry, Back, focus restoration, gallery return and manual reading retain their existing authority. Test first/last items, ragged rows, group headers, resize, high text scale, interrupted motion and slow virtualized reads. Compare focus latency, frame times, realized items and artwork requests with the current candidate. Sheen must add zero image decodes or collection reads.

### Motion implementation and consumer checks (0.4.71)

| Surface | Adoption and evidence |
| --- | --- |
| Home poster rail and Library grid | Playnite opts into one clipped 380 ms focused poster sweep. Home centers at 50%. Library centers the selected row at 45%. Native movement covered both axes, reversal and Home/End. Outer Home snaps keep their own viewport. |
| Recent Activity and Game Details | The feed and timeline align the leading edge at 25%. Achievements uses 20%. The store gallery centers the focused thumbnail at 50% during horizontal movement. Native checks covered repeated moves, endpoints and returning from image fullscreen to the same thumbnail. |
| Other Playnite lists and tabs | Information, Links, Sessions, capture review and pickers retain free reading or their previous entry policy. The separate Achievement Hub retains its own rail and grid. |
| Games & Apps | A native three-column grid moved focus down the screen until reaching its visible edge. Reversing kept the column. |
| Capture Library, Settings, Discord, Spotify, YouTube, YouTube Music, Full Application and SDK Gallery | Existing collection declarations omit the anchor. Existing posters omit the sheen. All eight consumer suites passed. Shared native free-scroll checks cover controller settlement and interruption. |
| PoB | Source checked. Its lists retain ordinary reveal. No dedicated PoB interaction run. |
| Templates | No collection or poster declaration requires adoption. |

Native evidence is under `artifacts/directional-focus`. Home poster centers
settled at x=1280 on a 2560-pixel display at both 100% and 150% text scale.
The 150% profile used reduced motion. The 100% profile used full motion.
On the default profile, opening Game Details from the sixth Home poster and
pressing Back returned to that poster at the same measured coordinates.
The native focus-decoration suite verified suppression in contrast and
reduced-motion modes, cleanup after completion and interruption, and
independence from the border setting. The performance comparison used the
same instrumented WinUI host and fixture provider with 0.4.70 and 0.4.71
widget code. Across 40 down and 40 up moves in a 2,000-game grid, the
end-of-run samples were 56 versus 64 realized tiles, 14 versus 20 completed
collection loads, and 80 versus 75 artwork decodes. The rendering-event p95
was 102 ms versus 91 ms in this pair of debug runs. These are instrumented
host observations from one comparison run.

### Wallpaper and card lighting follow-up (0.4.74)

Home and Library opt their shared cinematic background into a 300 ms directional
fade and a nine-second one-shot zoom from 103% to 101% around 75% across and 50%
down. The host carries the actual static or virtualized focus move through the
existing artwork demand. A refresh of the same handle can upgrade its decoded
pixels without restarting the camera. Retained hidden pages and modal backgrounds
pause their ambient motion. A visible surface enclosing a separate navigation
scope remains active.

| Consumer | Behavior and validation |
| --- | --- |
| Playnite Home, Library | Native rail and grid navigation, reversal, artwork replacement and Game Details return. In the Home recording, background landmarks moved over five seconds while a toolbar landmark stayed stationary. |
| Playnite posters, Currently Playing, Home Recent Activity | One 570 ms feathered upper-left to lower-right sweep on focus entry. Recording covers static shelf cards and virtualized poster and activity rows. |
| Playnite Game Details and launch backdrop, SDK Gallery | These background declarations retain the default plain fade. The shared native fixture exercises resize, stale replacement, scope, reduced motion and disposal. |
| Other shipping widgets and templates | No other stylesheet declares artwork motion or sheen. Plain action bodies keep their existing layout and allocate no highlight until opted in and focused. |

Native evidence, test outputs and the screen recording are in
`artifacts/wallpaper-motion`. The public styling contract is in
`docs/reference/wrss.md`.

### Focus reflection tuning and repetition (0.4.76)

Playnite posters and the Currently Playing and Recent Activity cards use the
broad reflection. The pass lasts 1.9 seconds at normal speed. Peak opacity is
0.24 on cards with text and 0.30 on posters. The first pass has a 180 ms delay.
Subsequent passes start after six seconds of rest on the same visible focus.
Other widgets and templates omit the repeat property and keep one-shot behavior.

The native validation fixture exercises late callbacks after focus replacement
and unload, reduced motion, contrast changes and cancellation during the entry
delay. The shipping candidate recording includes two passes on a Recent
Activity session card. Rapid Library navigation across virtualized rows and
Back from Game Details returned to the original poster. Evidence is in
`artifacts/repeating-sheen`.

### Game Details Sessions redesign

Give Sessions a distinct purpose: explore when time went into this game,
what a typical sitting looks like, and which sessions made up a memorable
period. Game Details Home keeps its recent event timeline.

1. Lead with weekly playtime bars for the eight calendar weeks ending in the
   last recorded session's week. Show the actual date range and include quiet
   weeks. All history uses month buckets, coarsened to years for long spans.
   A selected bucket shows its dates, duration, active days and session count.
   Keep the gallery and play bar under the existing modal header behavior.
2. Pair the chart with a compact profile for the displayed period: median
   session length and a horizontal distribution of session lengths. For a
   single session, show its date and duration directly. Use the existing
   seven-day chart composition as a native precedent and check both text scales.
3. Replace manually paged rows with a virtualized session history, newest
   first, grouped by local date. Give each day a divider and total. Show
   start/end and duration. Chart navigation has one entry point, Left/Right
   inspects periods, A drills into a nonempty period, and Back restores it.
   Down enters history directly. LB/RB continues to switch sections.
4. Reuse the cached per-game GameActivity snapshot, which admits up to 10,000
   sessions. Aggregate on data or range changes and keep the last good view
   on refresh. Split dated playtime at local calendar boundaries; count session
   starts once. Keep undated records accessible and distinguish provider totals
   from dated chart coverage. Secondary capture and unlock evidence can enrich
   an expanded session when its timestamp belongs unambiguously to the session.
   Reuse current permissions, apply no implicit writes, and avoid per-row reads.
5. Verify with dense short launches, one session, years of history,
   cross-midnight/DST records, missing providers, overlapping sessions and
   asynchronous updates. Capture native opening, period changes and return
   focus at 100% and 150% text scale. Confirm that inspecting the chart generates
   no extra provider reads and the history realizes only the visible range.

#### Implemented and verified in Playnite Library 0.4.78

The widget owns the calendar calculations, period selection and session highlights.
It uses the existing SDK focus presentation, indexed collection and query resource.
The package uses the existing host, Playnite add-on and GameActivity installation.

Period activation keeps the chart visible, filters the history in place and
updates the session-length profile. Back clears the filter and restores the
selected period. The first prototype removed the chart and reset the query,
which briefly emptied the list. Keeping one collection identity per Game Details
opening preserved the visible matching rows in the native transition recording.
Quiet periods have readable focus captions. All history includes undated records
and labels the GameActivity total. A session can show up to three highlights from
known unlocks and the existing authorized read of up to 48 linked captures.

All 289 Playnite widget tests passed. Added coverage includes DST in both
directions, fractional starts spanning a week boundary, overlapping sessions,
delayed achievement evidence, exact focus return and a 10,000-session history.
The large-history test acquired the last three rows through the SDK, switched
range, drilled into a period and returned using one GameActivity read. History
with more than 256 date groups draws its dividers inside the virtualized rows.

Verified the release package in native Silksong Game Details at 100% and 150%
text scale. Keyboard navigation, quiet weeks, all history, a single-session
month, row entry and Back were exercised. UI Automation exposed dates,
durations and session counts on the period and row focus targets. Bumper
routing was exercised through the SDK's controller input path. Physical
controller input and spoken Narrator output were not separately exercised.
Screenshots, the sampled transition and verification logs are under
`artifacts/sessions-redesign/`. Installed and enabled the verified package on
the default profile, with matching installed DLL and stylesheet hashes.

#### Per-game achievement tracking and session highlights in 0.4.79

The achievement row's X menu separates **Track achievement** from **Pin progress**.
The widget saves one provider-qualified achievement ID per game and connected
Playnite profile in its existing revision-checked private state. Currently
Playing resolves that ID from its existing achievement read. Unpinning,
changing a pin or stopping tracking do not change the other selection.

The former three-highlight limit came from the widget's session row template.
The collapsed row now previews up to three icons or thumbnails. Expanding it
inserts every matched highlight in chronological order into the same SDK indexed
collection. Date-group counts include the inserted rows, and the session count
and duration continue to describe game sessions. Native disclosure buttons
control a declared content subtree. The indexed history uses separate leased
rows for its children so they can each receive focus and be realized on demand.
Back returns to the owning session. The existing coverage of the 48 most recent
linked captures per game still applies.

Verified the eight-highlight November 1 Phantom Blade Zero session at 150%
text scale, including the eighth unlock and return focus. A separate native
Silksong check surfaced the tracked achievement on Currently Playing without
opening a pin. Tests cover per-game persistence across worker restart, provider
identity, ambiguous IDs, replacement, independent pin selection and unpinning.
Evidence is in `artifacts/tracked-highlights/`.

#### Carousel navigation and loading in 0.4.80

The shared WinUI gallery keeps playable occurrences in declaration order. When
an autoplay trailer fails during navigation, selection continues toward the
user's requested edge. At an edge it chooses the nearest surviving item.
The thumbnail strip, carousel neighbors and fullscreen controls use the same
remaining items. A provider can also withdraw the selected trailer while its
request is pending. Surviving thumbnail controls retain their native identity
and the superseded request cannot restore the withdrawn item.

Thumbnail artwork and original media have independent outcomes. Missing
thumbnail artwork shows the item's label and keeps its original selectable.
When every original in the public store carousel fails, the host closes its
fullscreen presentation and collapses the gallery until the declaration or
owner changes. The initial Game Details loading surface reserves the media
area with a centered grid cell. Native checks also measure the player ring
inside the center carousel preview during a delayed selection.

| Consumer | Applicability and checks |
| --- | --- |
| Playnite Game Details store media | Uses public thumbnails and the inline carousel. Verified Phantom Blade Zero's Release Date → failed Pre-Order → Launch trailer sequence on Windows, then reversed direction. |
| Playnite captures and Discord message attachments | Use ordinary attachment galleries. The error message and navigation to another attachment remain available on failure. Exercised the shared ordinary and modal gallery player paths. |
| First-party widgets, templates and SDK examples | No other shipping caller declares this public store carousel. No SDK or protocol change. |

Native evidence in `artifacts/gallery-direction/` includes focus traces,
the all-fail and late-failure cases, modal and fullscreen tests, live Steam
playback, and a delayed Game Details opening with a 40 × 40 pixel indicator
centered in a 1413 × 566 pixel area. Tests used keyboard and native controller
routing. Physical gamepad input was not separately exercised.

### Shipped candidate behavior before motion work

Playnite Library 0.4.70 saves hidden Currently Playing card types in the existing revision-checked organization store. X or the native context menu offers Hide this card and Manage cards. The header More menu retains the manager when the shelf is empty. Its six switches persist across games and worker restarts. A restored card appears when its provider has content. Hidden news, achievement and capture cards skip their dedicated loads. The Home activity feed keeps its independent data requirements.

Game Help 0.1.11 opens review from the composer and right trigger, including from the game-specific Playnite intent. Review can start empty, retains edits on Back, and enables Send to Gemini for a nonblank question. An attached capture uses capture review. A follow-up that would reuse previous context displays that context in review. The final submission path was exercised with a fake provider. Native checks from Silksong stopped before external submission.

### Currently Playing achievements and settled outlines (0.4.83)

Home publishes available cards in priority order as their sources arrive. The game portrait opens Game Details, followed by capture, achievements, news and the other available cards. Tracking is per game and accepts a visible locked achievement with a unique provider ID even when it has no progress counter. The shelf shows the tracked achievement, one latest unlock and the five visible locked achievements with the highest known player unlock percentages. A qualifying achievement appears once across those groups. Activating its card opens its own row in the achievements tab.

| Consumer | Settled focus behavior |
| --- | --- |
| Playnite Home and Library posters, Currently Playing and Recent Activity cards with reflection | WRSS opts into edge glint or breathe. The host starts the effect after the focus transition and retains the base outline during the rest interval. |
| Other shipping widgets, first-party widgets, templates and SDK examples | No other reflection declarations found during the applicability check. The default settled effect is static. |

Verification in `artifacts/playing-outlines` includes 293 Playnite tests, 41 styling checks, 45 native focus checks and a native keyboard traversal in Silksong. The traversal found one recent unlock after the tracked achievement, then five ranked suggestions before news. Opening Granted focused its matching row and Back returned to Granted. Physical gamepad input was not independently exercised.

**Home polish (0.4.84).** The floating poster rail is the accepted layout. The redundant panel wrapper and unused Favorites shelf styles were removed. The invisible size reservation still holds the rail steady during lazy realization. The empty Home shelf still links to Library. Home's platform and status labels use the same line metrics and vertical padding. Native checks at 100% and 150% text measured the platform and title at the same left coordinate, including a truncated long title. Keyboard traversal covered Currently Playing, poster-to-poster movement and returning from Recent Activity. Evidence is in `artifacts/floating-home`.

**Theme-controlled settled highlights.** The shared WRSS compiler resolves the optional `--focus-highlight` color only on controls using breathe or edge glint. Explicit control colors take precedence. Playnite is the only shipping widget declaring the effects in the consumer search across samples, first-party widgets and templates. Other widgets gain the same behavior when they opt in. The steady outline, Reduced Motion and high-contrast policies retain their existing roles. The default-profile candidate uses Tidepool 1.0.1. All 13 installed palettes resolved successfully. The native Tidepool glint reached RGB (206, 229, 255), and breathing reached (199, 231, 251) at its authored 85% intensity. Verification used the Release host and bridge in `E:/WidgetRail/theme-highlight`.

### Conditional Currently Playing background (0.4.88, add-on 1.3.9)

Playnite includes the confirmed process-instance key in the running-game snapshot. The widget supplies the selected game's key on the Home background and Currently Playing cards. The host compares it with its existing pre-overlay foreground snapshot and checks display, window validity and effective surface appearance. A cold widget can supply the key after the first render. When the selected content matches the captured window, the host fades out the surface paint with the existing transition. There is no extra provider query or periodic scan. The process-scoped window observer retires on hide. The desktop dimmer, foreground acquisition, card input and ordinary poster transitions retain their existing owners.

| Consumer | Behavior |
| --- | --- |
| Playnite Home | The cards can reveal the dimmed game, including when a cold worker publishes the match after loading. The poster rail uses decoded artwork. A mismatch keeps ordinary paint. |
| Playnite Library, Game Details, other widget backgrounds, SDK examples and templates | No paired process declaration. Existing artwork and material paths apply. |
| Shell media and pinned widgets | A separate window cannot claim the main overlay's foreground snapshot. |

Release evidence in E:/WidgetRail/foreground-background includes desktop captures of Silksong before and after opening, poster focus and return, a cold opening with late game data, a minimized game, and sampled color traces during interrupted transitions. The isolated visual fixture supplied Silksong's live process key using session data from the installed 1.3.8 add-on. The 1.3.9 production feed was independently exercised by the Framework/API fixture. No physical controller or exclusive display-mode switch was tested.

The cold opening follow-up used the installed Playnite provider and a running
Silksong window. The first open revealed the game, native focus on a poster
brought back decoded artwork, and returning focus to Currently Playing revealed
the game again. A cold open above an unrelated window kept the surface painted.
Evidence is in `E:/WidgetRail/interaction-plan/late-game-background/final-match.json`
and `final-mismatch.json`.

**Default-profile rollout, 0.4.89 with add-on 1.3.9.** Silksong was closed normally and relaunched from Game Details after the widget updated Playnite. Home opts its navigation row into foreground-chrome auto-hide. Up focuses Home, mouse hover reveals the row, and Down returns to the card. All three states preserved the card bounds. The page still owns LB/RB. In-app acrylic on the shelf, focus summary and header uses the native brush's translucent fallback during live-game transparency, then resumes ordinary acrylic over artwork. Other backgrounds and themes continue using their existing material. No capture or per-frame refresh was added. Live input, desktop images and transition traces are under E:/WidgetRail/foreground-chrome. The tracked Woven achievement was reselected after the updater renewed the binding to the same Playnite profile.

### Resume playing and task switching (0.4.90)

The lead card switches to the selected running game. Home and Library poster play actions use the same return-only Playnite command for a known running game. The native host routes widget-issued window switches through the task list's confirmation and cancellation logic. The broker still enforces the widget's original permission and effect authority. Playnite is the shipping launch-handoff consumer; the existing shell task list uses the same confirmation, and the explicit Windows Task View entry remains direct. Verified on the default profile with Silksong already running. The controlled native widget denial requested one fallback after 250 ms. Late success, overlay reopening and a stale target suppressed the fallback. Evidence is in E:/WidgetRail/resume.

### Acrylic experiment on the live-game shelf (rejected for 0.4.90)

Tested the release WinUI host in an isolated profile with Silksong running. The experiment used bounded desktop acrylic on one card, then on the card faces across the row. Inverting a striped window under the overlay confirmed that the cards blurred current desktop pixels. Delaying attachment left sharp stripes visible. Crossfading the desktop link could also leave stripes in a card after poster artwork covered the game. Covering the link with a solid tint removed those stripes but left a flat card followed by a second change in appearance.

Restoring the in-app brush when poster artwork started returning improved that direction. The reverse still exposed the desktop early. In the broad green/magenta test, 236 ms after pressing Up, the Woven card's sampled paint was within 1.1 RGB levels on average of its settled desktop appearance. A fixed sample of the surrounding artwork had traversed only about 1% of the visual difference between poster and live game. These are screen-pixel measurements, not animation progress callbacks. Sharp game text was also visible through fading cards on overlay entrance. The ordinary whole-overlay fade contributes to the latter observation.

The performance comparison used the same copied profile and game. Across 20 poster/shelf round trips the host used 12.88 CPU seconds with the last prototype and 12.83 with the existing build. The median sampled Desktop Window Manager 3D-engine utilization over eight seconds on the settled shelf was 0.93% for each. These observations cover one machine and scene. The transition defect determined the decision.

Keep the existing theme tint over the live game and in-app acrylic over poster artwork. No production host, widget, SDK, theme, pin or notification changes were made for this trial. A future host approach would need to composite the material against the same visible artwork/game blend as the owning surface. Passing the destination and duration from `WidgetPresentationSurface.SetPaintHidden` to independent targets in `DesktopAcrylicSurface` did not provide that behavior. Evidence under `E:/WidgetRail/acrylic-trial` includes `guardstress-exit-detail.png`, `coveredstress1-exit-detail.png`, `material-handoff.png`, the raw frames, CPU samples, and GPU counters.

### Achievement sync and library recovery

**Implemented and verified in the workspace.** Game Details offers **Sync this
game** in Achievements when the provider supports it. Failed reads offer
**Try again** beside the affected content. The achievement overview updates from
the Playnite event feed. The same feed coalesces catalog updates for the visible
Home or Library view. An inactive view picks up catalog changes on return.
Installation copy directs users to configure achievement sources and start the
initial refresh in Playnite. Library-wide achievement sync is done in Playnite.

All 295 Playnite widget tests passed. Isolated WinUI fixtures verified
controller navigation in the overview header and native focus and invocation
for both new Game Details buttons. The installed profile was left alone, and
the sync operation was tested with a fixture provider.

### Discord card and activity panel in Currently Playing

**Status** Implemented and verified in an isolated release candidate.

| Step | Status | Verification |
| --- | --- | --- |
| Shelf entry and explicit Discord activation | Verified | A fresh isolated catalog displayed Start Discord beside Resume playing for Hollow Knight: Silksong. The lifetime log recorded no Discord worker until the card was invoked. The packaged Discord worker then opened its account setup. After an explicit **Stop widget**, the card opened the host’s Reload screen and the worker stayed stopped until Reload was pressed. Disabled and absent providers have no shelf destination. |
| Live summary, consent and scoped actions | Verified | Independent grants on the two widgets exposed the synthetic call roster through the production Bridge. Watch & pin opened Discord’s selected stream layout. Leave followed by Join voice from a friend row updated Playnite to the new channel. Revoking the reader grant while the panel was open dismissed it and removed the roster. |
| Compact and wide shelf, activity panel and navigation | Verified | The production-widget fixture exercised two → three → two, a six-person roster, an empty panel and all six avatar URLs. WinUI retained focus on Discord through expansion and on its right-hand neighbor through shrink and re-expansion. The neighbor stayed in view. Dismissal returned focus to Discord. |
| Native visual and transition review | Verified | The X hint and overflow indicator have separate space. The action label aligns at the trailing edge. Public avatars and initial fallbacks share the same geometry. Mouse, keyboard and simulated controller checks covered the shipped surface. The narrow panel at 150% text retained its action and footer in high contrast. Recordings covered A/B, Enter/Escape and interrupted dismissal against the running game and hero artwork. |

| Consumers of the new summary contract | Applicability |
| --- | --- |
| Discord and Playnite | Discord publishes a sequence-checked summary and owns the action tokens, current call and choice of pin slot. Playnite subscribes while its running-game shelf is active and owns layout, overflow and the dialog. |
| Existing open intents, including Game Help, Settings, capture links and file sharing | Keep their existing handler selection and action authority. Only the reserved summary-action contract can stage a pin during delivery. |
| Other shipping widgets, SDK Gallery and authoring templates | No addressed live-summary declarations. Ordinary widget pins still use the existing pinning API. Both entry points use the shell’s current-layout, capacity and replacement checks. |

The production Discord package was used for cold activation. All people, voice changes and streams used to verify live actions came from the synthetic Discord session. The test used the running Silksong game and the actual Playnite library. The two test packages, permissions, pins and browser profiles were isolated from the user's installed catalog. A release-mode frontend verified a native stream pin through both worker processes. A separate isolated candidate with both production packages passed its final launch check.

Place one Discord `UI.ActionSurface` beside Resume playing. Match the shelf’s card height, title style and theme focus outline. Give the whole card one focus stop and one primary action. Left and Right move between cards. X opens the existing Hide this card / Manage cards menu.

| Card state | Presentation and action |
| --- | --- |
| Discord installed and stopped | Use the compact width. **Connect Discord** asks Playnite for permission to read Discord activity. **Start Discord** starts the worker in the background. If the user previously stopped it, ask for confirmation over Playnite. If Discord asks for sign-in, the card directs the player to Discord on the rail. |
| Ready without a call | Keep zero, one or two active friends compact. Show an empty state or one row per friend with avatar, name and activity. At three friends, expand to two columns and show up to four friends. Activation opens **Friends active** with the displayed friends and their available actions. The full list is available from Discord on the rail. |
| In a call | Lead with the channel name and participant count. Keep up to two participants compact. At three, expand to two columns and show up to four participants, each with an avatar, name and current voice status. Mark streaming separately so a muted streamer shows both states. List streamers first and keep positions stable as people speak. Activation opens the call panel. Full call controls live in Discord. |
| Unavailable | Finish the rest of the shelf immediately. Hide the card when Discord is absent or disabled. Clear private rows on account change, revocation, or worker retirement. |

Keep the shelf card 194 DIPs tall in both layouts. Use the 398-DIP Game updates card as the wide reference. When more people are available than fit, show a small avatar stack in the header with the remaining count, such as **+2** for six people with four visible. Use an ellipsis when Discord reports a truncated list with an unknown total. The indicator belongs to the card’s single focus target.

Change width in place on the same card within the existing shelf and scroll container. Once the card is wide, speaking, muting, streaming, avatar arrival and overflow-count updates only update its contents. On warm reopen, retain the last authorized layout while awaiting a fresh summary from the same Discord session. On first load, allow one adjustment when the people count becomes known.

Use a compact native `WidgetModal` centered inside Playnite, following the Game updates reader. Keep the shelf and game visible behind the themed dialog. Each row has an avatar, a name, a second line of activity or voice context, and a trailing action label when available. For “Alex · Streaming 007 First Light,” the whole row activates **Watch & pin**. A joinable voice row offers **Join voice**. Informational rows remain readable and skip directional focus. The list scrolls within the panel, keeping **Close** accessible in the footer.

On entry, focus the first actionable row, falling back to **Close**. A, Enter or a click activates the row. B, Escape or Close dismisses the dialog and returns focus to the Discord shelf card.

Playnite owns placement, presentation and the card visibility preference. Discord owns who is visible, target validation and pin slot choice. The SDK exposes publish, read, change notification and reader verification operations. The Bridge binds each summary to a worker lifetime and each action to the authenticated reader and publisher. Discord queues publication separately from its existing state reads. Playnite subscribes before its first read, cancels on exit, and clears private content on revocation. Opening Playnite leaves Discord asleep until an explicit start. The separate **Stop widget** command takes effect until the player confirms a restart.

Native evidence is in `artifacts/discord-shelf-native/portraits-review`. The isolated cross-process recordings are `E:/WidgetRail/discord-shelf-verification-1124/synthetic/silksong-navigation.mp4` and `E:/WidgetRail/discord-shelf-verification-1124/qualified/hero-transitions.mp4`. The game recording captured 316 frames and the hero recording captured 240. The sampled shelf and background remained painted during modal entry, dismissal and rapid reversal. Physical controller hardware and a live Discord call with other people were outside this verification.

The final local suites passed 297 Playnite tests, 1,089 Discord checks, 115 platform broker tests, 263 Bridge tests and 17 SDK compatibility tests. The Bridge workflow checks the description on Game Details’ Information tab before returning Home to launch. The capability check includes the existing reviewed capture-destination operation. Both retain their original action and authority assertions.

Verify cold start, unavailable and signed-out states, arrival of a stream while focused, a friend leaving between display and activation, sign-out, and return focus after a pin or sign-in flow. Check empty and informational-only lists, long names at large text sizes, and footer access on a narrow surface. Exercise open, scroll and dismiss with controller, keyboard and mouse over both hero artwork and the transparent game background. Check for flashes, shelf movement and focus loss during transitions.

For both friends and calls, exercise two → three → two with focus on Discord and on its right-hand neighbor near the scrolled end of the shelf. Verify the focused card stays visible, the scroll position does not reset, and the shelf stays painted throughout. Check a four-person roster, overflow at five, and a warm reopen with a delayed summary.

### Capture tab and gallery navigation

**Status** Implemented in the working tree. The Playnite Captures tab uses responsive two-column tiles with capture thumbnails and timestamps. Tiles share a grid across session boundaries. The shared gallery player holds the displayed image during an authorized same-gallery switch and applies the section motion to the decoded replacement. Playnite saved captures and Discord message attachments use this path. Single players and inline store carousels retain their own presentation. Other gallery consumers inherit this behavior for same-gallery image replacement. No SDK or protocol change is required.

### Reader consent and actions that keep Playnite selected

| Step | Status | Evidence |
| --- | --- | --- |
| Trace existing routes | Verified in source | Tray input uses a validated controller offer. Widget intents use a source snapshot, ticket and receiver worker identity. Existing pin delivery requires a visible pin. |
| Reader-side connection | Verified | The isolated shelf showed Connect Discord, handled denial, granted access once, dismissed private rows on revocation and recovered after reconnect. Broker checks cover recipient scope, wrong-provider requests, worker replacement, revocation and legacy grants. |
| Background delivery and Discord actions | Verified | The production Discord package in a fresh browser profile reached the signed-out card while its worker stayed in Background. The card kept focus. Selecting Discord from the rail exposed the real sign-in page. The synthetic provider exercised explicit stop and restart, microphone consent and cancellation, join, pin creation, stale actions and repeated pin presses. |
| Regression and native verification | Verified in managed suites and isolated integration | Bridge 264/264, runtime 121/121, broker 115/115, Playnite 297/297, Discord widget 1,091 checks, backend adapter 159/159 and generated native contract parity. Native evidence records Playnite row focus after a pin and after a cancelled microphone prompt. The native tray passed mouse and keyboard activation, replay picker cancellation, and a radial retry after retiring the worker. |

Discord and Playnite are the shipping summary consumers. Existing media, file, navigation and settings intents keep their declared presentation. Tray, dashboard and pinned shortcuts retain their input contracts. Templates have no summary or background intent consumer.

The host reuses authenticated worker startup and snapshot preparation for tray presses and background intents. Tray assignments continue to revalidate their live offer, recorder revision or media session, and controller press. Background intents revalidate their source control, payload, handler and ticket. Moving existing tray commands to intent contracts would require replacing their persisted assignments, live fallback offers and capability admission. The overlap in this change is worker startup and preparation.

Native signed-out evidence is under `E:/WidgetRail/discord-shelf-verification-1124/signed-out-check`. Voice consent and cancellation are under `stay-source-verified`, and native pin ownership, row focus and revocation are under `stay-source-pin-final` in the same test directory. The voice and stream checks used fixture participants. A live authenticated Discord call and physical controller hardware were outside this run.
### Discord startup feedback

The Start Discord card publishes “Connecting to Discord” during the existing outgoing intent preparation callback. The action and its scope stay stable for host revalidation. Acceptance keeps the progress label until the subscription reports Discord's activity or sign-in state. Cancellation and delivery failure restore an actionable retry on the same card. A failed activity subscription retries the subscription. Discord marks connection errors explicitly in its summary and refreshes a failed connection on a retry press.

Playnite is the shipping sender of this start action. Other intent senders keep their existing feedback. The shell includes background worker startup in the existing Back-to-cancel window. This also applies to the existing background summary actions. The microphone and stopped-widget prompts retain their own input handling.

The focused Playnite tests exercise progress before the worker starts, unchanged intent authority, cancellation, retry, acceptance before readiness and a subsequent provider failure. Discord's harness passes 1,093 checks, including failure and recovery through the background open handler. In a fresh native profile, cancelling the stopped-widget prompt returned to Retry, and confirming on the next attempt reached sign-in. Evidence is under `E:/WidgetRail/discord-connect-1124`.

### Reconnecting a Discord stream pin

The reported live stream played in Watch and disconnected in the pin. The running frontend had a separate pin window on the secondary display and Discord's receiver-session termination notice. The earlier build did not retain which provider condition ended that watch.

Discord Controls 0.1.0.61 asks the existing video view to reattach its live track on explicit Reconnect before replacing the receiver. The action verifies the session, account, call, stream, lease and view. Repeating an already-connected Playnite Watch & pin reuses the selected receiver. The provider retries local playback once if no first frame appears by six seconds. It still retires a stalled connection at the original twelve-second deadline. A failure reason can travel back with the existing unwatch response for the matching owner, account and lease. Failure storage is limited to three slot records. No additional provider polling or automatic remote watch is required.

Discord is the only shipping caller of BackendVideo and the track binding API across samples, first-party widgets and templates. The extra restart parameter is optional. The native fixture creates the receiver offscreen, attaches it to a passive pin at 70% opacity, tests a real local WebRTC track and closes the original viewer. All 57 native fixture checks passed on this system. The 720p comparison counted 529 frames on both players and 13 drops on the pin. The widget harness passed 1,094 checks and the adapter/backend suites passed 183 tests. Evidence is under `E:/WidgetRail/pin1124`. The remote participant's live failure remains to be rechecked on the updated candidate.
