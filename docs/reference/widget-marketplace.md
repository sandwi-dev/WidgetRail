# Widget marketplace and updates

Open **Marketplace** from the widget tray, or use **Settings → Widgets → Browse marketplace**.
It is included in both app editions. The initial curated catalog
contains Discord, Twitch, Game Help, Display Profiles, Playnite Library, Spotify, YouTube Video, and YouTube Music. Discord, Twitch, Game Help and Display Profiles are marketplace-only and are not bundled in either installer. Built-in
widgets are distributed with the app; the marketplace does not replace them.

## Browse, review, install

The catalog describes setup requirements and shows available and installed
versions. Its first opening checks public WidgetRail releases; subsequent checks
are explicit through **Check for updates**. Reopening refreshes local version
selection without another network check. Preview releases are labelled. The
current feed inspects the latest 20 non-draft releases and chooses the highest
matching package version for each curated item.

Open a listing to view its description, download size and screenshots. LB/RB or
the screenshot buttons change the preview. YouTube Music has no screenshots in
the initial catalog. Screenshot URLs are pinned to a repository commit so they
do not change underneath a published app.

**Y** checks for updates; **X** switches between all listings and available updates.
**B** returns from details to the previously selected card. From the catalog, B
keeps its ordinary tray behavior. Navigation remains available while a download
runs, and its progress/cancel action remains visible even when viewing another listing.

**Download for review** (or **Download update for review**) verifies SHA-256 and
validates the archive before offering installation. Downloading changes no
installed widget. The review shows host/architecture compatibility, required and
optional permissions, new permissions relative to the installed version, and the
execution model. Full-access widgets explicitly warn that they run with the
user's Windows-account access. The controller-native dialog starts on **Cancel**.

Installing requires this exact review. The new version remains **disabled**.
Use **Manage in Settings** to open that exact widget's details, review permissions and enable it. Updates retain
older versions and widget data. Existing **Versions** controls select a previous
version while the widget is disabled. Up-to-date listings and versions already
saved after a rollback open management rather than downloading the same version
again.

Curated placement and checksums do not constitute a publisher signature. There
are no accounts, payments, ratings or public submissions in this version.

## Jobs and recovery

Downloads and installs run as Bridge-owned jobs; short management requests only
start, inspect, approve or cancel them. Navigation stays available during a
download. The primary action remains focusable while busy. Returning to a listing
shows its existing job. Explicit cancellation stops pending network work; a
publication that has already committed remains installed and disabled.

One job and one retained review archive are admitted at a time. Reviews expire
after ten minutes and expire on the next status/action check. Finish or discard
the current review before starting another download or release check. A review token cannot approve another
package or be replayed after installation. The verified stream stays write-locked
through review and installation.

Downloads use the CLI's 72 MiB archive bound, HTTPS/redirect/timeout rules, and
checksum resolution. Archive expansion and installation use the existing catalog
limits. The private `.marketplace-downloads` directory permits only a bounded
number of leftovers; cleanup touches recognized temporary package files without
following cache reparse points. No permanent download library is created.

After installation, the Bridge reloads its catalog and notifies Settings to
refresh installed versions and permissions. GitHub source records use the same
`.wrail-sources` format as the CLI, bound to the installed version and content
digest. Failure to save optional CLI source metadata does not undo installation.

## Maintainer and framework boundaries

- `src/WidgetBridge/Marketplace/catalog.json` is the curated inventory, embedded
  in the app. Additions require a reviewed app build. The initial source is
  `sandwi-dev/WidgetRail` releases; arbitrary source entry is still available via
  the CLI and local file installation, not through this trusted catalog.
- Publish immutable `<package-id>-<version>.wrwidget` assets with GitHub asset
  digests or a supported checksum manifest. ID, publisher and version inside the
  archive must match the curated listing and advertised asset.
- Discord, Twitch, Game Help and Display Profiles each provide `samples/<Widget>Widget/Build-CommunityPackage.ps1`, backed by `scripts/Build-MarketplacePackage.ps1`. Build their archives and SHA-256 sidecars, then attach them to the release when publishing. Adding a listing alone does not make an unpublished package downloadable. These builders do not install, enable, grant access or publish.
- `PackageDistribution` owns shared remote download, release and source-record
  code. CLI command parsing and exit-code mapping remain in the CLI.
- `BridgeWidgetMarketplace` owns jobs and archive handles. Its only mutations
  call the existing catalog install/update transactions. It does not enable
  widgets or grant permissions.
- Marketplace commands and validation are shared protocol contracts; Settings'
  private diagnostics channel and the dedicated marketplace call the same Bridge
  service. The review UI is shared too. There is no second installation pipeline.
- `HostServices.Marketplace` is a reserved, host-granted service attached only to
  the sealed, bundled `widgetrail.firstparty.marketplace` running in AppContainer.
  Installed packages cannot acquire it by claiming the same name/publisher or
  declaring `widgets.marketplace.v1`. It is not requestable through consent.
  Read/status operations require a visible widget; download/install/cancel require
  interaction. The operation and command action must agree, preventing a read call
  from carrying an installation command. URLs and paths are never accepted.
- Other widgets should open the marketplace using `WidgetIntentContracts.Marketplace`
  (`widgetrail.marketplace.open`, version 1, empty object payload). Declare the
  empty-object schema with `additionalProperties: false` and `properties: {}`.
  Settings and SDK Gallery demonstrate this route. Marketplace uses Settings'
  widget-details intent for installed management.
- Additional GitHub repositories and private-source authentication are deferred.

Automated tests cover shared CLI behavior, consent/review/replay/expiry, staged
update and rollback, cancellation, identity mismatches, private transport and
controller view contracts. A live public-release probe reviews and discards a
real archive without installation. Physical screenshot loading, long reviews,
downloads under poor connectivity and installation/rollback remain separate
acceptance checks.
