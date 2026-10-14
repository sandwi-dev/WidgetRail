# Community distribution and permission adoption

User scope: Discord, Game Help and Display Profiles must be marketplace-only, absent from both Production and Developer installers. Widgets should request missing access when the user invokes a feature, not upon activation. Work in the existing checkout on `codex/community-widget-distribution`; preserve main's unrelated documentation edit and the installed user's profile. No release/push/installation was requested.

## Implementation

- Removed the three widgets from release membership; Display Profiles also removed from the base bundled catalog and moved to `samples/DisplayProfilesWidget` with community identity `widgetrail.samples.display-profiles`.
- Added the three curated marketplace listings and package builders with archive validation and SHA-256 sidecars. Download availability requires publishing the package assets later.
- Reused `HostServices.Permissions` and active-lifetime `Operations` through a linked widget-authoring helper. A granted action proceeds normally; missing access starts one asynchronous consent operation. Denial/cancel does not run the action. The broker still enforces current permissions on every operation. No host-specific widget exception was added.
- Adoption: Audio Mixer (output/input/session/device/spatial actions and read retries); Network Controls (network details, saved networks, Wi-Fi/Bluetooth actions and read retries); Games & Apps (library/running views, launch and registration); Now Playing (transport/read retry); Task Switcher (switch/close/refresh); Power (confirmed commands/read retry); Performance Monitor (FPS/read retry); Display Profiles (save/edit/apply/read retry); Recent Apps (read retry).
- Game Help already requests capture and library read; added provider-document consent for grounded answers. Capture Library now asks only for library read when opening the library, and capture/save when creating saved media. Existing delete/folder consent is retained. Discord/Browser website permissions remain separate. Spotify/YouTube/Playnite provider sign-ins do not declare these host capabilities.

## Validation and remaining work

Implementation and focused validation are complete. Evidence is under `artifacts/community-distribution/`.

- 347 checks pass across the affected widget suites (Audio Mixer 48, Network Controls 31, Performance Monitor 11, Power 8, Task Switcher 8, Now Playing 31, Games & Apps 94, Recent Apps 7, Display Profiles 18, Game Help 47), permission-action tests (6), marketplace service/UI (7/4), and Capture Library (27).
- Permission tests cover pending/grant/deny/cancel/unavailable, repeated input, current grant rechecking, lifecycle cancellation, inactive rejection and local navigation. Performance Monitor additionally verifies that a denied request never starts the collector and an approved request resumes once. Capture Library verifies its capture/save permission pair. Tests use simulated providers; no real display, power, network or FPS changes were performed.
- Games & Apps indexed callbacks use the same gate while retaining captured-item freshness checks after consent. Running-app removal requests registration access only for registered items. Network Controls restarts denied optional subscriptions after permission approval.
- `scripts/Test-ReleasePackaging.ps1 -SelfTest` passes: Production has ten bundled widgets plus Settings; Developer has fourteen plus Settings. Discord, Game Help and Display Profiles are absent from both catalogs and runtime folders.
- Three archives built and CLI-validated in `artifacts/community-distribution/packages`: `widgetrail.samples.discord-0.1.0.wrwidget`, `widgetrail.samples.game-help-0.1.2.wrwidget`, `widgetrail.samples.display-profiles-0.1.0.wrwidget`. A combined `SHA256SUMS.txt` and individual checksum files are present.
- Manual review covered declared capability mappings, cancellation/denial, source-item revalidation, timed display rollback, older-host fallback, package identities, publisher boundaries, immutable package output and installer membership. No host privilege bypass or new public SDK contract was introduced.
- `Test-VerificationRunner.ps1 -SelfTest` remains blocked by a pre-existing omission of `tests/AppUpdates.Tests/AppUpdates.Tests.csproj` from the verification manifest. This is not reported as a passing repository-wide gate; the new permission test entries are registered. Initial widget fixture failures from missing permission handlers, old copy assertions and the old capture-only expectation were corrected with explicit fixture contracts, then rerun successfully.

Still external to this task: publishing the package assets, building new installers, merging/pushing, and physical acceptance of the new permission flows. The running installed overlay and user profile were not replaced or modified. No real provider accounts, credentials or personal captures were accessed.
