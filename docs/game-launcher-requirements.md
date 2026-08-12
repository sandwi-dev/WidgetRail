# Game Launcher product and engineering requirements

Status: **proposed expansion of the implemented Game Launcher 0.6.0**

This document defines the next product boundary for Game Launcher. The current
widget already provides a bounded installed-game library, controller paging,
search, favorites, recent games, manual entries, exact variant organization,
lazy artwork, durable opaque identities, and fresh launch admission. Those
behaviors remain the compatibility baseline described in the
[current Game Launcher reference](game-launcher.md).

The expanded product is a console-like home for a Windows game collection that
opens inside Game Bar Alternative. It should make a large, mixed-store library
feel coherent without turning a sandboxed widget into a desktop store client or
giving themes code execution.

## Decision summary

- Build a controller-first **console home in the overlay**, not a smaller copy
  of a desktop launcher.
- Federate trusted source adapters behind the existing application-library
  broker. The widget receives normalized state and opaque authority, never raw
  paths, commands, store credentials, launcher databases, or helper-process
  handles.
- Keep launch as the primary operation. Installation, updating, repair, move,
  cloud-save sync, and uninstall are optional source capabilities introduced
  behind separately reviewed broker contracts.
- Add first-class, per-launcher **Launcher Experience Packs**. A pack may
  compose host-owned semantic launcher slots inside a validated responsive
  layout, supply typed GBSS, sealed local presentation assets, and bounded
  parameters. It cannot define actions or data access, nor contain HTML,
  JavaScript, native code, arbitrary shaders, or remote scripts.
- Treat One Game Launcher's MYUI as evidence that users value complete layout
  personalities, live backgrounds, animated cover art, and console metaphors;
  do not reproduce its localhost/WebView execution model.
- Use IGDB as the optional non-commercial metadata provider and SteamGridDB as
  the optional non-commercial artwork provider. Both are host-brokered,
  user-configured, replaceable, and never required for discovery or launch.
- Preserve the existing global theme. A launcher experience is an explicit
  launcher-only selection layered on top of global appearance, and host
  accessibility policy remains final and authoritative.

## Product outcomes

Game Launcher succeeds when a user can press Guide/Home, open the launcher,
recognize the focused game immediately, and start it without reaching for a
mouse or opening the originating store. A user with several thousand entries
must get the same predictable focus, startup time, and bounded resource usage
as a user with twenty games.

The product must support these outcomes:

1. **One playable library:** installed games from supported Windows sources,
   manually added games, and optionally owned-but-not-installed games appear in
   one normalized collection without losing their exact source identity.
2. **Console presentation:** the launcher can look and move like a purpose-built
   console home while remaining a native, responsive overlay surface.
3. **Controller completeness:** browsing, search, details, source recovery,
   downloads, confirmations, theme selection, and launch are reachable with a
   standard controller.
4. **Truthful operations:** every tile distinguishes installed, launchable,
   update-ready, queued, downloading, running, unavailable, and source-offline
   state. An acknowledgement is never presented as a completed launch or
   installation.
5. **Safe extensibility:** adding a store adapter or launcher experience does
   not expand community-widget authority or weaken the host security boundary.
6. **Offline usefulness:** the last validated installed library and local
   artwork remain browsable offline; unavailable online operations say why and
   do not erase last-good content.

## Non-goals

- Reimplementing a store's checkout, social graph, chat, DRM, anti-cheat,
  entitlement, or account-recovery UI.
- Scraping credentials, browser cookies, authentication tokens, or encrypted
  launcher secrets.
- Claiming authoritative running/ended state from a process name, window title,
  or same-title executable guess.
- Allowing a theme to launch a program, call a network endpoint, read the game
  catalog, bind controller actions, or add executable UI behavior.
- Shipping copied console logos, sounds, copyrighted artwork, or a pixel-for-
  pixel clone of a commercial console dashboard. Built-in experiences must be
  original even when they use familiar rails, carousels, cover walls, and hero
  artwork.
- Reusing Heroic source code. Heroic is GPL-3.0; this research informs product
  and interface requirements only.

## Research findings and product translation

The Heroic repository was reviewed at commit
[`37a9bef678a837240477e97b708069dab4517666`](https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/tree/37a9bef678a837240477e97b708069dab4517666).
Heroic is an Electron/React application whose backend normalizes several store
implementations behind a common manager shape. Epic, GOG, Amazon, sideloaded,
and Zoom sources are selected through a
[`libraryManagerMap`](https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/blob/37a9bef678a837240477e97b708069dab4517666/src/backend/storeManagers/index.ts).
Its shared interfaces cover library refresh and version discovery as well as
per-game import, install, launch, move, repair, save sync, update, and uninstall
operations ([`game_manager.ts`](https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/blob/37a9bef678a837240477e97b708069dab4517666/src/common/types/game_manager.ts)).

Heroic also demonstrates several operational behaviors worth carrying forward:

| Heroic pattern | Requirement for Game Launcher | Deliberate difference |
| --- | --- | --- |
| One store-manager interface with source-specific implementations | Define one normalized source adapter contract with explicit per-source capabilities and health. | Adapters stay in trusted broker processes; a widget never receives a store manager or helper-binary interface. |
| Legendary, gogdl, and Nile helper tools for store operations | Permit separately shipped trusted adapters when a store has no suitable in-process API. Pin exact tool versions and verify their bytes before activation. | No arbitrary executable path, user-selected helper binary, or helper output crosses into widget/theme state. |
| Import, install, update, repair, move, uninstall, cloud sync, and per-game settings | Model these as typed, independently supported operations with progress and cancellation. | Launch ships first; destructive and credential-bearing operations require separate capability/security gates. |
| Persistent sequential download queue with pause/resume/cancel and automatic offline pause ([`downloadqueue.ts`](https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/blob/37a9bef678a837240477e97b708069dab4517666/src/backend/downloadmanager/downloadqueue.ts)) | Provide one host-owned operations queue that survives overlay close/restart and reconciles source truth. | The widget is a projection of queue state, not its durable owner. |
| Console Mode and install overlay | Make controller navigation the primary UI contract for every launcher route. | Use the existing native declarative renderer, input scopes, UI Automation, and host focus ownership instead of browser focus/gamepad emulation. |
| `heroic://launch` protocol and shortcuts ([`protocol.ts`](https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/blob/37a9bef678a837240477e97b708069dab4517666/src/backend/protocol.ts)) | Allow host-owned dashboard shortcuts and future deep links to resolve one exact SavedId. | Never accept a theme-authored URL or launch based on title-only lookup. External protocol activation is a separate security review. |
| Built-in theme variables plus user-selected CSS files ([`ThemeSelector`](https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/blob/37a9bef678a837240477e97b708069dab4517666/src/frontend/components/UI/ThemeSelector/index.tsx)) | Expose semantic launcher roles, variables, layout parameters, and deterministic preview tooling. | Retain typed GBSS and sealed packages; do not load arbitrary CSS or browser content. |
| Windows disk-space and writable-location checks ([`windows.ts`](https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/blob/37a9bef678a837240477e97b708069dab4517666/src/backend/utils/filesystem/windows.ts)) | Validate space, target support, and permissions before admitting an install or move. | The trusted operation owner performs checks and returns a sanitized result; PowerShell text is not a public provider contract. |

Heroic's current README summarizes the mature feature envelope—login, install,
update, repair, move, import, cloud saves, a download queue, custom games,
categories, console use, and custom theming
([source](https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/blob/37a9bef678a837240477e97b708069dab4517666/README.md#features-available-right-now)).
That is a useful completeness checklist, not a demand to put every store-client
operation in the first overlay milestone.

One Game Launcher demonstrates the presentation target more directly: a Game
Bar widget, LB/RB controller paging, a unified multi-store library, and MYUI
themes with different rail/carousel layouts, custom or live backgrounds, and
animated cover art ([product overview](https://ogl.app/),
[MYUI gallery](https://ogl.app/MYUI/)). Its public MYUI example is an arbitrary
HTML page that reads games and requests launch through localhost HTTP, then uses
WebView messages for host actions
([MYUI README](https://github.com/ShatsDev/MYUI/blob/main/README.md)). Game Bar
Alternative must deliver comparable creative range through bounded native data,
not through an unauthenticated local launch endpoint or theme-authored script.

The supplied One Game Launcher references establish two concrete visual
acceptance targets rather than a request for cosmetic recoloring:

- a full-bleed selected-game hero with a bottom cover rail, legible game/source
  labels, strong focused-card scale, and safe cropping around the rail; and
- a full-bleed selected-game hero with a vertical cover rail, a movable
  translucent details panel, title/source/primary action, play-length and score
  cards, and background art whose focal subject remains visible.

Launcher packs therefore need bounded **layout composition**, not just a choice
among rigid templates. They may arrange host-owned slots and select which
optional metadata slots are present. They cannot invent a field, fabricate a
score, replace launch semantics, or hide Back, focus, the current primary
action, source/availability truth, active-operation state, or required
attribution.

## Experience architecture

The product has five ownership layers:

```text
Store/Windows state
        |
        v
trusted source adapters ---- trusted operation queue / credential vault
        |
        v
normalized app-library broker (opaque IDs, capabilities, revisions)
        |
        v
Game Launcher worker (view state, organization intent, no raw authority)
        |
        v
native host renderer + Launcher Experience Pack + accessibility policy
```

### Source adapter contract

Each source publishes a revisioned descriptor and a closed set of supported
operations. Unsupported actions are absent, not present-but-optimistically-
attempted. A source adapter must expose only the operations it can prove on the
current machine and account state.

The normalized source contract must be capable of representing:

- source ID, display name, health, account state, catalog revision, and last
  successful refresh time;
- installed, owned, unavailable, update-ready, queued, active-operation, and
  launch-evidence state;
- exact source-private record identity mapped by the host to an authority-
  scoped SavedId;
- title, optional sort title, source attribution, normalized kind, install
  size, version, last played, playtime, categories/tags, and bounded details;
- separate tile, cover, background/hero, logo, and optional animated-artwork
  handles with revisions and semantic fallbacks; and
- supported actions selected from `launch`, `install`, `pause`, `resume`,
  `cancel`, `update`, `repair`, `move`, `import`, `uninstall`, `cloud-sync`,
  `open-source-client`, and `manage-add-ons`.

The first adapter expansion should prioritize installed-game truth on Windows:

1. existing Start Menu, AppsFolder, and Steam sources;
2. Xbox/Microsoft Store installed games through supported Windows registration
   APIs;
3. opt-in Epic, GOG, and Amazon adapters;
4. official-launcher registration adapters for EA, Ubisoft, and Battle.net
   where an exact supported launch/import contract exists; and
5. manual games and applications.

Owned-but-not-installed enumeration, account sign-in, and content management
are separate milestones. A source that cannot safely manage content may still
contribute installed entries and an **Open source launcher** recovery action.

### Adapter isolation and supply chain

- An adapter that handles credentials or invokes a helper binary runs outside
  `OverlayHost` and outside every widget worker.
- Every shipped helper has an exact version, expected SHA-256, provenance,
  license record, update policy, and rollback version. A changed digest is a
  new reviewed release, never an in-place replacement.
- Secrets remain in a host-owned vault or the source's own supported credential
  store. The widget sees only `SignedOut`, `SigningIn`, `Ready`, `Expired`,
  `Denied`, or `Unavailable` plus sanitized recovery copy.
- Adapter logs exclude paths, commands, tokens, email/account IDs, store record
  IDs, entitlement payloads, and helper stdout that may contain them.
- A failing adapter cannot block, clear, or mutate another source's last-good
  catalog.

## External metadata and artwork APIs

### Decision

Game Launcher does **not require a public game database API** to launch games or
ship M1. Installed state, exact launch authority, source, title, local play
history, and provider-native artwork must come from local Windows/store
adapters. User-supplied title and artwork overrides close the remaining offline
presentation gaps.

An API becomes useful for optional enrichment: better covers/heroes/logos,
release year, genres, developer/publisher, age ratings, descriptions, aggregate
ratings, and estimated completion time. The recommended first spike is a
host-brokered IGDB adapter. This project will use IGDB only for its documented
non-commercial use, so commercial partnership is not a current release blocker.
Credential delivery and attribution still have to satisfy the IGDB/Twitch
terms. Any future monetization, paid distribution, advertising, commercial
service, or other change in use pauses the integration until the terms are
reviewed again. No external provider is a launch authority or a hard
dependency.

SteamGridDB is the selected complementary artwork provider for this
non-commercial open-source project. It supplies community grids, heroes, logos,
and icons while IGDB remains the general metadata source. SteamGridDB access is
user-enabled with the user's own API key and is limited to documented API
functionality and personal, non-commercial presentation. Assets are cached only
for that user's local launcher and are never bundled into the application,
experience packs, fixture libraries, release screenshots, or exported caches.

If “GameDB” means **TheGamesDB**, it is not the recommended baseline today: its
official site exposes an authenticated API, but its current public pages do not
give us enough durable pricing, commercial-use, redistribution, cache, quota,
and attribution detail to make it a product dependency. Obtain written terms
before implementing it.

### Provider evaluation as of 2026-08-12

| Provider | Relevant value | Current constraints | Product decision |
| --- | --- | --- | --- |
| [IGDB](https://api-docs.igdb.com/) | Covers, artwork, screenshots, external store mappings, companies, genres, ratings, age ratings, release data, and a Game Time To Beat endpoint. | Requires a Twitch confidential client ID/secret and OAuth; 4 requests/second and 8 concurrent requests. Public docs allow free non-commercial use and direct commercial users to a partnership. Browser calls are unsupported. | **Selected M2 enrichment provider for this non-commercial project**, broker-only. Record required attribution and use user-supplied credentials unless IGDB approves another desktop flow. Never compile the confidential secret into this open-source client. |
| [RAWG](https://rawg.io/apidocs) | Broad catalog, screenshots, ratings, Metacritic value, and average playtime with a relatively simple key. | Free-plan and API terms impose key, quota, attribution/link, usage, and redistribution conditions; current commercial wording must be reconciled before release. | **Fallback candidate**, not a default dependency. Legal/UX review must prove that overlay attribution and intended scale comply. |
| [SteamGridDB](https://www.steamgriddb.com/api/v2) | Particularly strong community grids, heroes, logos, and icons. Its organization publishes open-source API wrappers and integrations. | Requires a personal API key. Its [terms](https://www.steamgriddb.com/terms) limit service use to personal, non-commercial use and prohibit scraping/unreasonable automation. Artwork is user-submitted content with independent rights and takedown risk. | **Selected optional M2 artwork provider** for this non-commercial project. Use the documented API, each user's key, bounded local caching, provenance, and no redistribution. |
| [TheGamesDB](https://api.thegamesdb.net/) | General game records and artwork. | Login/key required; current public commercial, cache, attribution, quota, and redistribution terms are insufficiently explicit for this decision. | **Defer** pending written provider terms. |
| [MobyGames](https://www.mobygames.com/info/api/) | Curated historical metadata and credits. | The free allowance is non-commercial; current commercial subscriptions are paid and require attribution/use restrictions. | **Do not use as the free default.** Re-evaluate only for a funded licensed tier. |
| [PCGamingWiki](https://www.pcgamingwiki.com/wiki/PCGamingWiki:API) | Compatibility notes, fixes, save/config locations, and exact store-ID redirects. | MediaWiki/Cargo rate limits and content attribution/license obligations; not a general hero-art service. | Prefer an **Open PCGamingWiki** details action using an exact provider ID. Structured import is a later, separately reviewed feature. |
| [Giant Bomb](https://www.giantbomb.com/api/) | Historically broad editorial game data. | Its official page currently says the game-data APIs are unavailable after the platform rebuild. | **Not a candidate.** |
| OpenCritic / HowLongToBeat | Review aggregates and completion estimates match the supplied details-card concept. | No supported public product API with acceptable current terms was found. Unofficial libraries generally scrape sites and can break or violate terms. | **Do not scrape.** Use an approved aggregate field from a licensed provider (IGDB exposes time-to-beat data) or omit the card. |

The Microsoft Store submission/analytics APIs are for a publisher's own
Partner Center products, not a public installed-game catalog. Steam's official
Web API likewise includes publisher/authenticated surfaces and is not a generic
license to republish store metadata. Provider-native local registrations and
cached client assets remain the primary source for those libraries.

### Enrichment broker requirements

Because an installed open-source desktop executable cannot protect a bundled
provider secret from the machine owner, production authentication must choose
one reviewed model: user-supplied developer credentials stored by Windows and
never exported; a product-operated privacy-minimizing proxy with its own cost,
abuse, retention, and availability plan; or a provider-approved desktop/public
client grant. A secret embedded in source, binaries, package resources, public
configuration, or a theme is forbidden. A host credential vault isolates a
user-supplied secret from widgets; it does not magically make a shipped shared
secret confidential. The initial non-commercial implementation uses
user-supplied Twitch developer credentials so the launcher does not require a
hosted service or distribute a shared secret.

- **GL-META-001 (M2):** Enrichment is a separate user consent and capability.
  Disabling it stops requests and leaves the installed library fully usable.
- **GL-META-002 (M2):** Only a trusted host broker or explicitly approved
  product proxy makes provider requests. API keys, client secrets, access
  tokens, provider record IDs, query payloads, cache paths, and raw responses
  never enter widget state, public config, diagnostics, or an experience pack.
- **GL-META-002A (M2):** No shared provider secret is compiled into or shipped
  with the desktop product. User-supplied credentials use Windows-protected
  storage and revocation; a proxy sends only the minimum exact IDs/fields and
  has disclosed retention, deletion, abuse prevention, and service budgets.
- **GL-META-003 (M2):** Matching prefers exact provider/store crosswalk IDs.
  Title/year/platform matching produces candidates with confidence and requires
  user confirmation below a reviewed threshold. It never merges SavedIds or
  changes launch authority.
- **GL-META-004 (M2):** Every normalized field carries provider, retrieval
  time, source record revision, match method/confidence, and required
  attribution. Conflicting providers are resolved per field, not by replacing
  the entire game record.
- **GL-META-005 (M2):** Requests are deduplicated and batch/coalesced where the
  provider allows, with provider-specific concurrency/rate limits, exponential
  backoff, a global monthly budget, negative caching, and a user-visible
  quota/degraded state.
- **GL-META-006 (M2):** Cache TTL, storage, deletion, export, and attribution
  follow the selected provider's current terms. The cache is bounded and
  revisioned; pack archives and support bundles cannot redistribute it.
- **GL-META-007 (M2):** Missing, expired, rate-limited, revoked, or ambiguous
  data collapses cleanly. A theme may request an optional semantic field but
  cannot require one for navigation, resize around an untrusted string without
  bounds, or show zero as a substitute for unknown.
- **GL-META-008 (M2):** Ratings identify their scale and source; completion
  estimates identify the categories the provider actually supplies. Values
  from different providers are never silently combined or relabeled.
- **GL-META-009 (M2):** Provider terms, quotas, response schema, attribution,
  credential rotation, revocation behavior, and representative match quality
  have contract fixtures before the adapter can ship.
- **GL-META-010 (M2):** The shipped IGDB integration is explicitly classified
  as non-commercial. Release review must confirm that distribution and current
  product behavior remain within that classification; a commercial scope
  change disables new IGDB requests until a new agreement is recorded.
- **GL-META-011 (M2):** SteamGridDB is an optional user-enabled artwork source
  using the user's personal API key. Requests use only its documented API and
  exact store mappings where available; title-search matches require the same
  confidence/confirmation rules as other enrichment.
- **GL-META-012 (M2):** SteamGridDB assets retain provider asset ID, type,
  author/provenance when returned, retrieval time, and source revision. The
  cache is bounded, local, purgeable, and excluded from experience packs,
  fixture data, support bundles, cache export, installers, and release media.
  Removed or unavailable assets fall back without affecting game identity.
- **GL-META-013 (M2):** SteamGridDB use remains personal and non-commercial. A
  monetization or distribution-model change, provider-terms change, key
  revocation, or takedown disables new requests until reviewed; already cached
  content is removed when the applicable terms or takedown require it.

## Functional requirements

Requirements labeled **M1** define the next playable milestone. **M2** expands
source coverage and polish. **M3** adds store-management operations after the
required security gates close.

### Library and organization

- **GL-CAT-001 (M1):** The launcher must merge every healthy source into one
  normalized collection while retaining exact source attribution and SavedId.
- **GL-CAT-002 (M1):** The launcher must never merge or redirect two records by
  title alone. Existing explicit variant groups remain presentation metadata;
  activation always resolves the focused exact SavedId.
- **GL-CAT-003 (M1):** Search, source, install state, favorites, category, and
  sort criteria must execute through bounded provider queries or fixed retained
  slices. The worker must not cache an unbounded complete library.
- **GL-CAT-004 (M1):** The default collection must include Continue/Recent,
  Favorites, All installed, Updates, Manual, and per-source views when they are
  non-empty. Users may create up to 32 named collections with bounded rules.
- **GL-CAT-005 (M1):** Hide, restore, favorite, category, preferred variant,
  recent order, and manual membership must remain exact-SavedId operations with
  conflict-safe persistence and independent replacement behavior.
- **GL-CAT-006 (M1):** Refresh must be independently cancelable per source.
  Healthy source results publish even if another source fails; the failed
  source retains last-good entries with visible stale/degraded state.
- **GL-CAT-007 (M1):** Installed library browsing and launch of locally
  revalidated entries must remain available without network connectivity.
- **GL-CAT-008 (M2):** Owned-but-not-installed records must be visually and
  semantically distinct from installed records and cannot enter launch
  admission.
- **GL-CAT-009 (M2):** Users must be able to override title, artwork, background,
  category, hidden state, and preferred source variant without changing the
  provider's launch authority. Reset restores provider presentation.
- **GL-CAT-010 (M2):** Optional metadata enrichment must be separately
  consented, cache bounded, source attributed, replaceable, and unable to
  authorize launch or content operations.

### Console launcher surface

- **GL-UX-001 (M1):** The built-in experience must open on a hero-and-rail
  surface: focused-game background, bounded title/status/metadata, one
  horizontal game rail, collection tabs, and controller hints.
- **GL-UX-002 (M1):** A launches or installs the exact focused game according to
  its current primary action. X toggles Favorite. Y opens a scoped action sheet.
  View opens details. B closes the action sheet/details first, then returns to
  the launcher root, then to the tray according to the host Back contract.
- **GL-UX-003 (M1):** LB/RB retain collection-page traversal. LT/RT change
  top-level collections only when the active experience exposes them and may
  not leak through an action sheet, picker, dialog, or details scope.
- **GL-UX-004 (M1):** Search uses host-owned text entry, preserves the committed
  query on cancel, and restores focus to the search action.
- **GL-UX-005 (M1):** Opening details and returning must restore the originating
  collection, tile, page, scroll offset, and exact focus when the identity still
  exists. A removed identity chooses the nearest deterministic neighbor.
- **GL-UX-006 (M1):** Disabled and busy tiles remain navigable and expose a
  visible plus accessible reason. Loading, empty, signed-out, offline,
  permission-denied, degraded, stale, and retry states must not collapse into
  one generic error.
- **GL-UX-007 (M1):** At 420 by 340 logical DIPs the experience may reduce hero
  metadata and use one compact rail, but launch, Back, search, source health,
  downloads, and recovery remain reachable.
- **GL-UX-008 (M2):** Wide and full-screen overlay sizes may add a second rail,
  richer details, clock, and battery status. These additions are host data and
  never theme-provided device access.
- **GL-UX-009 (M2):** The launcher must expose a controller-reachable **Switch
  experience** action, preview the candidate against fixture data, and provide
  one-action recovery to the built-in safe experience.

Nested action sheets, dialogs, and pickers must be separate input scopes outside
the root navigation shell. Inactive responsive/layout branches must be absent
from focus, actions, hit testing, and accessibility rather than merely hidden.

### Game details and launch

- **GL-PLAY-001 (M1):** Details must show title, source, installed/availability
  state, current primary action, favorite/category/variant state, version when
  known, last played, playtime, and current operation status without exposing
  provider-private identifiers.
- **GL-PLAY-002 (M1):** Launch must re-resolve the exact SavedId, verify the
  current source revision and availability, issue a short-lived operation ID,
  and reject stale or replaced authority.
- **GL-PLAY-003 (M1):** Visible launch evidence uses the closed progression
  `Pending`, `Request accepted`, `Launcher started`, `Running`, `Ended`, or
  `Failed`. The overlay auto-closes only at `Launcher started` or stronger.
- **GL-PLAY-004 (M1):** Late launch completion after source replacement,
  deactivation, or a newer launch generation cannot modify the current tile or
  recent order.
- **GL-PLAY-005 (M2):** A source may declare launch options such as edition,
  branch, language, executable variant, or offline mode. Options are bounded
  typed choices rendered by the host; the widget never accepts a free-form
  command line.
- **GL-PLAY-006 (M2):** A user may choose a default option for one exact
  source record. Holding or bypassing a store-required launcher is not an
  available option unless the source contract explicitly supports it.
- **GL-PLAY-007 (M2):** Pre-launch cloud-save or update warnings must retain the
  focused game and require an explicit choice. Cancel returns to the same tile.

### Downloads and content management

- **GL-OPS-001 (M3):** The host must own one durable operations queue shared by
  all sources. Overlay close, widget restart, source refresh, and host restart
  must not duplicate an admitted operation.
- **GL-OPS-002 (M3):** Queue entries expose source, game, operation type,
  queued/running/paused/completed/failed state, bytes and item progress when
  known, speed/ETA when trustworthy, and a sanitized error/recovery action.
- **GL-OPS-003 (M3):** The queue supports pause, resume, cancel, and retry only
  when the source advertises those actions. Connectivity loss automatically
  pauses network operations and reconnect resumes only operations the source
  proves resumable.
- **GL-OPS-004 (M3):** Install and move must perform source-owned path,
  free-space, filesystem, permission, and existing-install checks before queue
  admission. A stale check is repeated immediately before mutation.
- **GL-OPS-005 (M3):** Import reconciles an existing installation through the
  source adapter and does not manufacture an installed state from a user-picked
  executable alone.
- **GL-OPS-006 (M3):** Repair/verify, move, and uninstall require a details
  action sheet. Uninstall and delete-downloaded-data paths require a distinct
  destructive confirmation naming the exact game and source.
- **GL-OPS-007 (M3):** The widget never elevates, writes a store database, or
  kills a store client directly. When a supported operation needs store-owned
  ceremony, the provider returns **Continue in source launcher**.
- **GL-OPS-008 (M3):** At most two content operations may transfer concurrently
  by default, and a source may impose a lower bound. Queue ordering is durable
  and controller-editable without exposing raw operation payloads.

### Cloud saves, achievements, and play history

- **GL-DATA-001 (M3):** Cloud-save sync is a separately supported source action
  with `Not supported`, `Up to date`, `Syncing`, `Conflict`, `Offline`, and
  `Failed` states.
- **GL-DATA-002 (M3):** A conflict requires a host-owned choice that identifies
  local/cloud timestamps and device labels only when the source can prove them.
  No choice is auto-selected and neither side is deleted before confirmation.
- **GL-DATA-003 (M3):** Automatic pre/post-launch sync is opt-in per source or
  game, bounded, cancelable, and must never delay launch indefinitely.
- **GL-DATA-004 (M2):** Achievements and store playtime are optional read-only
  details. Missing support is omitted rather than shown as zero progress.
- **GL-DATA-005 (M1):** Local recent order changes only from exact current
  `Launcher started`, `Running`, or `Ended` evidence, preserving the existing
  authority contract.

## Launcher Experience Packs

A Launcher Experience Pack is a new, launcher-specific data package. It is not
the existing global `.gbartheme`, and installing one must not change the shell,
Settings, or another widget. The proposed extension is `.gbarlauncher` with an
exact ID/version selection stored under Game Launcher's public configuration.

### Pack composition

An initial pack contains:

```text
launcher.json
layouts/launcher-layout.json
styles/launcher.gbss
assets/preview.png
assets/background.png        # optional
assets/background-poster.png # required if motion media is added later
```

`launcher.json` must be strict, versioned, duplicate/unknown-member rejecting,
and equivalent to this closed shape:

```json
{
  "schemaVersion": 1,
  "id": "dev.example.deep-space",
  "publisher": "dev.example",
  "name": "Deep Space",
  "version": "1.0.0",
  "layoutPreset": "hero-rail",
  "compositionFile": "layouts/launcher-layout.json",
  "styleFile": "styles/launcher.gbss",
  "previewFile": "assets/preview.png",
  "parameters": {
    "tileSize": "large",
    "metadataDensity": "standard",
    "backgroundMode": "selected-game-artwork",
    "focusEffect": "lift"
  }
}
```

### Host-validated layout composition

`compositionFile` is optional; omitting it selects the named built-in preset.
When present, it is a strict data recipe over a versioned allowlist of
host-owned slots and layout primitives. It is not the widget declarative tree,
cannot name actions or bindings, and cannot contain expressions, script,
provider queries, URLs, or custom element types.

The initial semantic slot allowlist is:

| Slot | Host-owned contents | Pack-controlled presentation |
| --- | --- | --- |
| `hero-background` | Selected game's revision-bound hero/fallback art, loading and failure behavior. | Region, crop/focal alignment, fit, scrim/blur intensity, and transition preset. |
| `game-rail` | Bounded page of exact game tiles and focus semantics. | Horizontal/vertical orientation, edge/center alignment, cover/tile aspect, size, gap, focused-card emphasis, and bounded peeking. |
| `details-panel` | Title, source/state, primary action, optional approved metadata, operation state, and required attribution. | Region, width, density, glass/surface role, text alignment, and optional-field order. |
| `collection-tabs` | Current collection and controller-reachable collection navigation. | Region, horizontal/vertical presentation, label/icon density. |
| `source-status` | Offline/stale/signed-out/degraded source truth and recovery. | Region and compact/standard presentation. |
| `operation-status` | Current launch/download/content-operation truth. | Region and compact/standard presentation. |
| `system-status` | Host-provided clock/battery/network indicators when allowed. | Region, approved fields, and compact/standard presentation. |
| `controller-hints` | Current host action map, including Back. | Region, compact/expanded density, and surface role. |

Layouts use only `region`, bounded `grid`, `stack`, `overlay`, `inset`, and
alignment records. Each slot appears at most once per responsive branch. A
recipe supplies separate `compact`, `standard`, and `wide` branches or inherits
the nearest built-in fallback. The validator rejects out-of-bounds geometry,
unbounded rows/columns, invalid overlap, clipped focus extents, and branches
that cannot reach all required actions at their target size.

The following slots are invariant and cannot be omitted when their state is
applicable: `game-rail`, current primary action, source/availability state,
active `operation-status`, required metadata attribution, visible focus, and a
Back route. Optional details fields, collection tabs, system status, and
controller-hint density may change by responsive branch, but their actions
remain reachable through a host-owned overflow/action route.

This model directly permits the two supplied references: a bottom horizontal
cover rail over a full-bleed hero, or a left vertical cover rail with an
independently positioned translucent details panel. The art's declared focal
point and safe zones inform cropping; the host still moves or scrims text when
contrast/focus tests fail.

Example composition excerpt:

```json
{
  "schemaVersion": 1,
  "branches": {
    "wide": {
      "root": { "type": "overlay", "children": [
        { "slot": "hero-background", "region": "full" },
        { "slot": "game-rail", "region": "left", "orientation": "vertical" },
        { "slot": "details-panel", "region": "upper-center", "surface": "glass" },
        { "slot": "controller-hints", "region": "bottom-right", "density": "compact" }
      ] }
    }
  }
}
```

### Built-in layout presets

The host must implement at least four original profiles:

| Profile | Intent | Required controller behavior |
| --- | --- | --- |
| `hero-rail` | Large selected-game backdrop and one horizontal cover rail. | Left/Right moves games; LB/RB pages; Up reaches collections/actions. |
| `cover-wall` | Responsive multi-row library with optional compact details. | Spatial D-pad movement and focus-follow vertical scrolling. |
| `carousel` | Center-weighted horizontal carousel with bounded depth/scale cues. | One logical focus target per game; off-axis transforms never alter focus geometry. |
| `compact-grid` | Low-motion, high-density fallback for small overlay surfaces. | Row-major focus and complete action reachability at the minimum size. |

These are safe starter presets and recovery fallbacks, not the limit of pack
composition. Presets and custom recipes use the same semantic IDs, host-owned
contents/actions, responsive validation, focus graph, and UI Automation model.
Consequently, composition may radically change presentation but cannot add or
remove product authority.

### Theme cascade and user control

The required launcher cascade, from lowest to highest, is:

1. built-in platform baseline;
2. Game Launcher authored GBSS;
3. selected global user theme;
4. selected Launcher Experience Pack GBSS;
5. launcher user adjustments such as accent/background override; and
6. host accessibility policy.

This is an intentional new per-launcher layer. Selecting **Use global
appearance** omits layers 4 and 5 and reproduces current behavior. A launcher
pack overrides only documented launcher semantic roles/classes. It cannot use
an ID selector to reach another widget or host shell role.

User-adjustable pack parameters must include background choice, accent, tile
size, metadata density, motion intensity, and whether optional clock/battery
status is shown. Every parameter has host-defined type, range, fallback, and
accessibility behavior. A malformed or no-longer-supported value falls back
independently and produces a bounded diagnostic; it never changes launch state.

### Artwork and motion

- **GL-THEME-001 (M1):** Static packs may contain bounded PNG, JPEG, or WebP
  presentation assets. Files are decoded by a trusted media owner into bounded
  pixels and never exposed as native paths to the widget.
- **GL-THEME-002 (M1):** `selected-game-artwork` uses only the current record's
  revision-bound background handle, crossfades after successful decode, and
  preserves the previous/fallback background on failure.
- **GL-THEME-003 (M1):** Theme focus animation is limited to host-owned opacity,
  scale, translation, outline, and background crossfade targets. Reduced motion
  removes translation/scale and makes transitions immediate or a short opacity
  dissolve.
- **GL-THEME-004 (M2):** Animated cover art and local looped video backgrounds
  may be added only after a hardware-decoded native media broker exists. Media
  pauses when the overlay is hidden, the launcher is not selected, the surface
  is occluded, Remote Desktop policy disables it, battery saver is active, or
  reduced motion is enabled. The poster image is always sufficient.
- **GL-THEME-005 (M2):** Packs may include a bounded semantic navigation sound
  set only after host-owned mixing, mute/volume control, and accessibility
  policy exist. Themes cannot select an output device or play arbitrary audio.
- **GL-THEME-006 (M1):** Remote image/video/YouTube URLs are not valid package
  assets. Optional provider artwork uses the existing credential-free brokered
  image path and cache policy.
- **GL-THEME-006A (M1):** A layout recipe can arrange only the documented
  semantic slots. Responsive branch validation, critical-slot invariants,
  focus geometry, UI Automation, contrast/scrim correction, and fallback to a
  built-in preset are enforced by the host after parsing.

Initial static packages are limited to 64 files, 32 MiB expanded, 16 MiB per
asset, and images no larger than 4096 by 4096 after metadata validation.
Animated-media limits must be specified and fuzz-tested before the format is
accepted; raising package limits is not implicit permission to add media.

### Authoring and recovery

- **GL-THEME-007 (M1):** `gbar launcher-theme new`, `validate`, `preview`,
  `pack`, `inspect`, `install`, `list`, and `remove` must share the production
  manifest, GBSS, asset, digest, and catalog validators.
- **GL-THEME-008 (M1):** Packing is deterministic; installed ID/version content
  is immutable; remote install requires an exact release asset and independently
  obtained SHA-256 as the current theme workflow does.
- **GL-THEME-009 (M1):** Preview renders fixture libraries for empty, 20-game,
  2,000-game, offline, signed-out, download-active, long-title, and missing-art
  states at compact/standard/wide sizes and accessibility settings.
- **GL-THEME-010 (M1):** Invalid selection/reload retains the last-good
  experience. Holding the documented safe-start controller gesture while
  opening Game Launcher bypasses the selected pack for that activation.
- **GL-THEME-011 (M2):** Settings must display exact pack ID/version, publisher
  claim, digest, asset/media summary, compatibility, and unsigned status before
  selection. A selected or built-in version cannot be removed.
- **GL-THEME-012 (M3):** A curated gallery and automatic updates require signed
  publisher identity, revocation, compatibility ranges, rollback, and a content
  review policy. They are not part of local package support.

## Accessibility requirements

- **GL-A11Y-001:** Every focused game is one UI Automation ListItem or Button-
  equivalent target with title, source, install/availability state, favorite
  state, primary action, and operation status in its semantic projection.
- **GL-A11Y-002:** Selected-game backgrounds, logos, video, depth, glow, scale,
  and color cannot be the only indication of focus or state. The host preserves
  a geometric focus ring and visible status text.
- **GL-A11Y-003:** At 150% text scale, long localized copy must reflow or clamp
  without covering the focused tile, primary action, controller hints, or Back.
- **GL-A11Y-004:** High contrast, bold text, reduced transparency, and reduced
  motion apply after every experience-pack rule. A pack cannot disable them.
- **GL-A11Y-005:** Animated artwork and background media must have no semantic
  content unavailable elsewhere. Reduced motion uses the static poster without
  degrading navigation or information.
- **GL-A11Y-006:** Loading and operation progress expose bounded live-region
  changes; per-frame byte, speed, and animation updates must not flood UIA.
- **GL-A11Y-007:** All routes remain complete with controller, keyboard, mouse,
  touch where supported, and UI Automation. Hover is never required.

## Security and privacy requirements

- **GL-SEC-001:** The widget retains only bounded SavedIds and sanitized
  presentation/organization state. Store IDs, AUMIDs, AppIds, paths, commands,
  PIDs, HWNDs, account IDs, tokens, and helper output remain host-private.
- **GL-SEC-002:** Every read, launch, install, update, repair, move, import,
  uninstall, and cloud action revalidates authenticated caller identity,
  declaration, consent, lifecycle, source revision, and exact opaque target.
- **GL-SEC-003:** Read, launch, content management, credentials, cloud saves,
  metadata enrichment, and destructive operations are separate capabilities.
  Granting installed-library read/launch cannot imply any other authority.
- **GL-SEC-004:** Themes and experience packs are presentation-only data. A
  pack can arrange allowlisted host slots but cannot define semantic contents,
  actions, bindings, or authority; access the catalog; name an external
  executable; open a URL; make a network request; start a process; or invoke
  localhost.
- **GL-SEC-005:** Free-form executable, argument, environment, script, and
  pre/post-launch configuration is not available to community widgets or
  themes. Any future advanced local-game editor is a trusted Settings surface
  with explicit risk copy and separate storage.
- **GL-SEC-006:** Destructive operations include exact target/source copy and a
  host-owned confirmation. Stale confirmations fail closed without affecting a
  replacement or same-title neighbor.
- **GL-SEC-007:** Account sign-in opens only a provider-owned, system-browser,
  or host-owned supported flow. Passwords and MFA codes never pass through the
  Game Launcher worker.
- **GL-SEC-008:** Artwork/media decoders run against size, dimension, duration,
  codec, frame-count, decompression, path, and reparse-point limits. Invalid
  media falls back without taking down the launcher surface.
- **GL-SEC-009:** Diagnostics use stable codes and opaque correlation IDs.
  Export requires explicit user action and redaction tests.

## Performance and reliability budgets

- **GL-PERF-001:** With a valid cached catalog, Game Launcher must publish a
  usable root snapshot within 250 ms at p95 on the minimum supported hardware.
  Source refresh continues asynchronously and cannot blank the last-good view.
- **GL-PERF-002:** Controller input to visible focus feedback must complete
  within 50 ms at p95 while cached artwork is decoding or an operation is
  progressing.
- **GL-PERF-003:** Catalog paging keeps the existing 64-record page and bounded
  retained-window semantics. A 10,000-record source cannot produce an
  unbounded widget snapshot, accessibility tree, artwork request burst, or
  private-state document.
- **GL-PERF-004:** Tile and background artwork are demand-loaded. Prefetch is
  limited to the current visible window plus one adjacent page and is canceled
  on query, collection, source revision, experience, or lifecycle change.
- **GL-PERF-005:** The widget performs no periodic source polling while
  Background. Providers use change events where supported and bounded explicit
  refresh otherwise.
- **GL-PERF-006:** Animation and media must preserve host input/render budgets.
  If the budget is exceeded for a bounded sampling window, the host reduces
  effects in closed steps and ultimately uses the static poster; it does not
  sacrifice focus or input responsiveness.
- **GL-PERF-007:** Source, operation, theme, and artwork failures are isolated.
  A crash loop disables only the offending adapter/pack revision and retains a
  safe built-in launcher with diagnostics.

## Telemetry and diagnostics

Diagnostics are local and privacy-preserving by default. The launcher must
report, in bounded form:

- active experience ID/version and last-good revision;
- source health, last refresh outcome/time, and catalog revision without
  account or game identity;
- operation queue counts by state and sanitized terminal error codes;
- artwork/media cache size, decode failures, and fallback count;
- snapshot node count, retained rows/cursors, publication latency, focus-input
  latency, and dropped/degraded visual-effect count; and
- capability/consent availability without secret values.

No game titles, account labels, paths, store IDs, play history, or library
contents leave the machine unless the user explicitly exports a redacted
support bundle and reviews its manifest.

## Verification and acceptance

The feature is not complete on documentation or unit tests alone. Each
milestone requires deterministic managed contracts, native renderer/focus/UIA
coverage, process/broker integration, packaged hidden-overlay smoke, and a
hands-on controller/display pass.

### M1: Console home and safe experiences

M1 is accepted when:

- all current Game Launcher behavior remains green, including 10,000-item
  traversal, exact SavedId launch, stale/canceled results, warm display-only
  state, variant organization, and source failure isolation;
- `hero-rail`, `cover-wall`, `carousel`, and `compact-grid` render the same
  semantic actions and pass compact/standard/wide focus and Back tests;
- a custom recipe can reproduce both supplied reference structures—a bottom
  cover rail and a left cover rail with a glass details panel—while validator
  fixtures reject missing critical slots, clipped focus, illegal overlap,
  provider/action bindings, and inaccessible responsive branches;
- a data-only launcher pack can be scaffolded, validated, deterministically
  packed, installed, previewed, selected, reloaded, rejected to last-good, and
  removed under the exact-version policy;
- no pack archive containing HTML, JavaScript, executable content, remote URLs,
  unsafe paths, oversized/decompression-bomb images, unknown fields, or
  cross-widget selectors can be selected;
- static custom background, selected-game background, focus effects, and user
  accent/tile/density settings work with 150% text, high contrast, reduced
  motion, and reduced transparency; and
- a physical Xbox-compatible controller can open the overlay, switch
  collections, search, open/close details and an action sheet, select/recover a
  theme, and launch a game without mouse or keyboard.

### M2: Broader installed library and richer presentation

M2 is accepted when each new Windows/store source passes missing-client,
signed-out, empty, large-library, duplicate-title, source-update, stale-record,
offline, corrupt-cache, helper-crash, and revocation fixtures. Xbox/Microsoft
Store, Epic, GOG, Amazon, EA, Ubisoft, and Battle.net are listed as supported
only for the exact operations proven by their adapter tests.

IGDB enrichment is accepted after its non-commercial product-use basis and
required attribution are recorded, secrets remain broker-private, exact-ID and
ambiguous matching fixtures pass, quota/offline/revocation states degrade
cleanly, cache and deletion behavior comply with its terms, and missing
metadata leaves every experience navigable. The provider can be disabled
without changing discovery or launch acceptance.

SteamGridDB artwork is accepted after user-key setup, exact-ID and confirmed
search matching, grid/hero/logo/icon selection, bounded cache eviction, asset
removal/takedown, corrupt-media fallback, provider outage, key revocation, and
terms-change fixtures pass. Disabling it restores IGDB, provider-native, manual,
or built-in fallback artwork without changing the library or selected theme.

Animated artwork or background media is accepted only with hardware-decoded
native playback, static fallback, lifecycle/battery/reduced-motion suspension,
malformed-media fuzzing, bounded caches, and measured controller/render
responsiveness on minimum hardware.

### M3: Trusted content operations

M3 is accepted source by source. A source cannot advertise an operation until
install/update/import/pause/resume/cancel/retry, disk-full, permission-denied,
offline, stale revision, helper crash/restart, host restart, duplicate
admission, and destructive-confirmation recovery tests pass for that operation.
Cloud conflict handling and credential revocation have separate acceptance
fixtures. Passing for one source does not authorize the operation for another.

## Delivery sequence

1. Freeze public launcher semantic roles, normalized source states, and the
   experience-pack manifest, slot, responsive-layout, and parameter schemas.
2. Implement the four built-in layout profiles over the current installed-only
   provider so the console experience is independently playable.
3. Add validated slot composition, launcher-specific selection, static asset
   broker, CLI tooling, native fixture preview, last-good reload, and safe-start
   recovery.
4. Implement brokered IGDB enrichment for the documented non-commercial use,
   with user-supplied Twitch developer credentials, exact-ID matching,
   attribution, and fixture data; keep manual and provider-native artwork
   complete without it.
5. Add optional SteamGridDB artwork selection with a user API key, exact store
   mappings, bounded local cache, provenance, and no asset redistribution.
6. Expand installed-library adapters one source at a time, starting with
   supported Windows/Xbox registration and opt-in Epic/GOG/Amazon discovery.
7. Add the durable operations queue and prove install/update for one source
   before generalizing the contract.
8. Gate animated media, account integration, cloud saves, and each additional
   destructive operation behind their own measured security/reliability work.
9. Add signing, gallery, and automatic update only after local immutable packs
   and recovery have production evidence.

The first shippable target is therefore intentionally ambitious in feel but
bounded in authority: a fast, visually transformable, controller-complete
console home for every safely discoverable installed game. Store management
becomes an additive trusted platform capability, not a prerequisite for making
the launcher excellent.
