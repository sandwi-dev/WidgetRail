# WidgetRail for Playnite — independent local API plan

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
