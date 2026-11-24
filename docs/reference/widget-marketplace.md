# WidgetRail Catalogue and updates

Open **WidgetRail Catalogue** from the widget tray, or use **Settings → Widgets → Browse catalogue**.
Discover widgets, inspect their setup requirements and screenshots, then download
and review their permissions before installing. The catalogue also brings updates
for installed listings together in one place. The **Themes** tab offers free
WidgetRail themes. Widget availability depends on published packages.

## Browse, review, install

Browse opens with Featured, followed by Essentials for in-game controls and
Performance Monitor. More widgets contains the rest of the catalogue. Search
matches widgets across all three groups.

The catalog describes setup requirements and shows available and installed
versions. **Browse**, **Installed** and **Updates** separate discovery from
management. Opening Catalogue checks releases without hiding existing content
or blocking navigation, while preserving an active download or review. Manual
**Check for updates** remains available. Preview releases are labelled. Availability
is cached in the host for 15 minutes, so reopening the widget normally makes no
release request. Manual checks respect a 30-second minimum spacing and any GitHub
cooldown. Expired/manual checks use ETag or Last-Modified validators and inspect up
to five sequential pages of 100 releases, choosing the highest matching package
version for each curated item. A truncated scan retains previously known curated
releases outside that window and labels them **Previously checked release**; it
does not claim their availability is current. Complete scans remove missing assets.
Failures leave cached information visible with retry guidance. A batch shares its
release checksum manifest without weakening individual archive verification; a
new batch checks the manifest again.

Listings with several screenshots use the inline gallery. Focus a thumbnail and
move Left or Right to change the preview, or click a thumbnail. Open the selected
image for a fullscreen view. A listing with one screenshot displays it directly.
**Integrations** describes what the listed widgets can do together. Search also
matches the partner name and the integration description.

Widget listings in `src/WidgetBridge/Marketplace/catalog.json` and theme listings
in `themes/catalogue.json` accept an optional `details` array. Rows with the same
`section` appear together in author order. For example,

```json
"details": [
  { "section": "Integrations", "label": "Game Help", "value": "Open a help request for the current game." }
]
```

Each entry supports up to eight plain-text rows. Section titles allow 48
characters, labels 80, and values 240. Adding a section or field uses the same
format. Omitting the array leaves the listing usable. Setup requirements and
permission review retain their dedicated fields and controls.

Focusing or hovering a listing with setup requirements opens a read-only rich
preview of those requirements. Listings with no setup requirements have no
redundant preview; descriptions remain on the card/details page.

Setup requirements appear before download, separately from package permissions.
For example, Playnite needs its separate Bridge integration (guided widget setup
installs/connects it and saves the token); Spotify needs your own
developer app and Premium for playback; Game Help needs a Gemini API key; YouTube
Video search needs a YouTube Data API key. Conditional account requirements are
labelled for the features that need them. Credentials are entered in each widget's
setup flow, never in Catalogue.

**LB/RB** switches Browse, Installed, Updates and Themes. **L3** returns focus to search.
Search applies when its text is committed; **Clear** resets it. **Check for updates**
is beside the widget search field. On the widget tabs, **Y** checks for updates
and **X** opens Updates. Tab navigation remains available while a job runs.
**B** returns from details to the previously selected card. From the catalog, B
keeps its ordinary tray behavior. Navigation remains available while a download
runs, and its progress/cancel action remains visible even when viewing another listing.

**Download for review** (or **Download update for review**) verifies SHA-256 and
validates the archive before offering installation. Downloading changes no
installed widget. The review shows host/architecture compatibility, required and
optional permissions, new permissions relative to the installed version, and the
execution model. Full-access widgets explicitly warn that they run with the
user's Windows-account access. The controller-native dialog starts on **Cancel**.

Installing requires this exact review. Fresh interactive installs start **enabled**
and request permissions when a feature needs them. **Manage in Settings** opens
the exact widget details directly through a destination loading state. Updates
preserve enabled/disabled and stopped state. The review explains that ordinary
widget settings will be kept. Approval copies only the bounded private-settings
document into the exact replacement package namespace. Allow and Deny decisions
for permissions declared by both versions are kept; new permissions need approval.
Saved app references remain stable. Credentials are not transferred. Existing destination settings and cleared state are respected.
Ordinary updates retain the active version and one backup, cleaning older
versions automatically. Locked files can remain until a later cleanup retry. To revert, disable the widget and open
its **Technical details** in Settings, then choose **Revert to previous version**.
After reverting, use the ordinary update action in Catalogue again to restore the
newer version, even when that version is still retained locally. Up-to-date
listings offer **Manage in Settings**.

Curated placement and checksums do not constitute a publisher signature. There
are no accounts, payments, ratings or public submissions in this version.

## Browse and try themes

Open **Themes** to see the featured theme and new releases. Search by theme,
creator, inspiration, or game tag. For example, **GTA 6** finds Afterglow FM.
The bundled store thumbnails show an Audio Mixer sample
with the same levels in each theme. A listing shows the selected theme on the radial tray,
a poster rail and a notification. **Try theme** opens a controller-ready sample
with a comparison to your current theme. Back returns to the listing.
Next and Previous bring the first card on the new page into view and focus it.

Previewing leaves your appearance setting and installed themes as they were.
**Install** saves the listed version. Choose **Install and apply** to use it
immediately, or **Apply** after installation. At the 128-version limit,
**Manage installed themes** opens Settings Appearance. Preview remains available.
Cached listings remain available when the catalogue is offline. The initial
catalogue uses curated placement and release dates.

## Jobs and recovery

Downloads and installs run as Bridge-owned jobs; short management requests only
start, inspect, approve or cancel them. Navigation stays available during a
download. The primary action remains focusable while busy. Returning to a listing
shows its existing job. Explicit cancellation stops pending network work; a
publication that has already committed remains installed with its resulting enable state.

One job is admitted at a time. An individual job retains one review archive; a
batch retains only its bounded verified selection. Reviews expire
after ten minutes and expire on the next status/action check. Finish or discard
the current review before starting another download or release check. A review token cannot approve another
package or be replayed after installation. The verified stream stays write-locked
through review and installation.

Downloads use the CLI's 272 MiB widget archive bound, HTTPS/redirect/timeout rules, and
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
- Optional `setupRequirements` metadata contains at most eight plain-text entries
  of 320 characters each. Empty or control-character entries are rejected. The
  host snapshots this metadata and retains it through release and inventory
  refreshes. Update it when service prerequisites change; it does not grant
  permissions or establish that the user already meets a requirement.
- Publish immutable `<package-id>-<version>.wrwidget` assets with GitHub asset
  digests or a supported checksum manifest. ID, publisher and version inside the
  archive must match the curated listing and advertised asset.
- Theme sources and curation live under `themes/`. `scripts/Publish-ThemeCatalogue.ps1`
  stages data-only `.wrtheme` archives, native preview PNGs and the versioned
  index using the CLI and a validation-enabled WinUI renderer. Publish the
  referenced immutable assets to `gh-pages` under `themes/v1/` before replacing
  the index, and retain assets referenced by earlier indexes. Increase the
  version when package bytes change. The publisher checks the current index
  for an existing version with different bytes. The bridge checks
  the exact archive digest and theme metadata before preview or installation.
  Routine updates can pass `-ReuseVerifiedPackages` with the previous index and
  its staged archives in the output directory. The publisher reuses an archive
  after checking its digest and comparing every source path and file. Run
  without the switch when changing the packer or revalidating all package sources.
- Discord, Twitch, Game Help and Display Controls provide `Build-CommunityPackage.ps1` in `samples/DiscordWebViewWidget`, `samples/TwitchWidget`, `samples/GameHelpWidget` and `samples/DisplayProfilesWidget`, respectively, backed by `scripts/Build-MarketplacePackage.ps1`. Discord first composes its sealed backend. Build their archives and SHA-256 sidecars, then attach them to the release when publishing. Adding a listing alone does not make an unpublished package downloadable; local testing uses the archive through Settings. These builders do not install, enable, grant access or publish. The current Discord package is `widgetrail.samples.discord-controls`; the old `samples/DiscordWidget` webapp remains a source-only example with a distinct trust model.
- `PackageDistribution` owns shared remote download, release and source-record
  code. CLI command parsing and exit-code mapping remain in the CLI.
- `BridgeWidgetMarketplace` owns jobs and archive handles. Mutations use the
  existing verified catalog transactions. Reviewed fresh installs start enabled;
  updates preserve existing enable and stop choices. Permissions are never granted
  by installation. Low-level CLI installation remains disabled staging.
- Catalogue commands and validation are shared protocol contracts; Settings'
  private diagnostics channel and the dedicated Catalogue call the same Bridge
  service. The review UI is shared too. There is no second installation pipeline.
- `HostServices.Marketplace` is a reserved, host-granted service attached only to
  the sealed, bundled `widgetrail.firstparty.marketplace` running in AppContainer.
  Installed packages cannot acquire it by claiming the same name/publisher or
  declaring `widgets.marketplace.v1`. It is not requestable through consent.
  Read/status operations require a visible widget; download/install/cancel require
  interaction. The operation and command action must agree, preventing a read call
  from carrying an installation command. URLs and paths are never accepted.
- Other widgets should open the catalogue using `WidgetIntentContracts.Marketplace`
  (`widgetrail.marketplace.open`, version 1, empty object payload). Declare the
  empty-object schema with `additionalProperties: false` and `properties: {}`.
  Settings and SDK Gallery demonstrate this route. Catalogue uses Settings'
  widget-details intent for installed management.
- Additional GitHub repositories and private-source authentication are deferred.

Automated tests cover shared CLI behavior, consent/review/replay/expiry, staged
update and rollback, cancellation, identity mismatches, private transport and
controller view contracts. A live public-release probe reviews and discards a
real archive without installation. Physical screenshot loading, long reviews,
downloads under poor connectivity and installation/rollback remain separate
acceptance checks.

## Centralized updates

The Updates tab offers individual updates and **Update all** without visiting each
widget's details. The host sequentially downloads and verifies the exact selected
versions, then presents one review with per-package compatibility, permission and
full-access information. Cancel is initially focused. Approval applies only to
those retained bytes and the installed baselines recorded in the review.

A batch admits up to 16 packages, 544 MiB of archives and 128 KiB of review metadata.
Larger reviews remain available as individual updates rather than truncating
permission details. Cancellation keeps completed installations and cancels queued
work. Per-item results explain failures; Retry remaining updates excludes already
completed items. Reviews expire and reject changed installed baselines.

Installed covers widgets listed in the catalogue. Other locally imported widgets
remain in Settings → Widgets. Additional repository support is deferred.

Widget package limits are shared by the CLI, installer and runtime content lease: 256 MiB expanded per package, with a 32 MiB per-file limit. Large immutable datasets may use bounded archive parts. Path, entry-count, seal and extraction checks still apply. Theme package limits and generic non-widget downloads are unchanged.
