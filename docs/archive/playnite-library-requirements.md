# Playnite Library product and engineering requirements

Status: **proposed expansion of the implemented Playnite Library 0.6.0**

This document defines the next product boundary for Playnite Library. The current
widget already provides a bounded installed-game library, controller paging,
search, favorites, recent games, manual entries, exact variant organization,
lazy artwork, durable opaque identities, and fresh launch admission. Those
behaviors remain the compatibility baseline described in the
[current Playnite Library reference](../reference/playnite-library.md).

The expanded product is a console-like home for a Windows game collection that
opens inside WidgetRail. It should make a large, mixed-store library
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
- Treat One Game Launcher's MYUI as evidence that users value complete layout
  personalities, live backgrounds, animated cover art, and console metaphors;
  do not reproduce its localhost/WebView execution model.
- Use IGDB as the optional non-commercial metadata provider and SteamGridDB as
  the optional non-commercial artwork provider. Both are host-brokered,
  user-configured, replaceable, and never required for discovery or launch.
- Preserve the existing global theme and keep host accessibility policy final
  and authoritative.

## Product outcomes

Playnite Library succeeds when a user can press Guide/Home, open the launcher,
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
5. **Safe extensibility:** adding a store adapter does not expand
   community-widget authority or weaken the host security boundary.
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

| Heroic pattern | Requirement for Playnite Library | Deliberate difference |
| --- | --- | --- |
| One store-manager interface with source-specific implementations | Define one normalized source adapter contract with explicit per-source capabilities and health. | Adapters stay in trusted broker processes; a widget never receives a store manager or helper-binary interface. |
| Legendary, gogdl, and Nile helper tools for store operations | Permit separately shipped trusted adapters when a store has no suitable in-process API. Pin exact tool versions and verify their bytes before activation. | No arbitrary executable path, user-selected helper binary, or helper output crosses into widget/theme state. |
| Import, install, update, repair, move, uninstall, cloud sync, and per-game settings | Model these as typed, independently supported operations with progress and cancellation. | Launch ships first; destructive and credential-bearing operations require separate capability/security gates. |
| Persistent sequential download queue with pause/resume/cancel and automatic offline pause ([`downloadqueue.ts`](https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/blob/37a9bef678a837240477e97b708069dab4517666/src/backend/downloadmanager/downloadqueue.ts)) | Provide one host-owned operations queue that survives overlay close/restart and reconciles source truth. | The widget is a projection of queue state, not its durable owner. |
| Console Mode and install overlay | Make controller navigation the primary UI contract for every launcher route. | Use the existing native declarative renderer, input scopes, UI Automation, and host focus ownership instead of browser focus/gamepad emulation. |
| `heroic://launch` protocol and shortcuts ([`protocol.ts`](https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/blob/37a9bef678a837240477e97b708069dab4517666/src/backend/protocol.ts)) | Allow host-owned dashboard shortcuts and future deep links to resolve one exact SavedId. | Never accept a theme-authored URL or launch based on title-only lookup. External protocol activation is a separate security review. |
| Built-in theme variables plus user-selected CSS files ([`ThemeSelector`](https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/blob/37a9bef678a837240477e97b708069dab4517666/src/frontend/components/UI/ThemeSelector/index.tsx)) | Expose semantic launcher roles, variables, layout parameters, and deterministic preview tooling. | Retain typed WRSS and sealed packages; do not load arbitrary CSS or browser content. |
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
package-owned source adapters ---- package operation queue / credential vault
        |
        v
normalized package service (opaque IDs, capabilities, revisions)
        |
        v
Playnite Library Community application (view state, organization intent, exact launch)
        |
        v
native host renderer + accessibility policy
```

The implemented Community package runs through the generic full-trust application
bootstrap. The product host neither brokers Playnite Library discovery/launch nor
ships a Playnite Library-specific capability. Windows/Xbox and opt-in installed-store
adapters, caches, source health, organization persistence, SavedId issuance, and
fresh launch revalidation are package-owned. This ordinary current-user authority
is disclosed at install and enable time; it is not AppContainer isolation and does
not grant authority to retained display rows. Package-owned artwork registrations
are opaque, bounded to 128 current entries, and resolved lazily through exact
provider revalidation. Successful demands become bounded inline PNG content;
missing or offline artwork becomes the closed game glyph and never exposes a path,
store identity, or unusable package-local handle to the host.

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

Playnite Library does **not require a public game database API** to launch games or
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
