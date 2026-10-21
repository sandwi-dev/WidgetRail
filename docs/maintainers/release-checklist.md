# Prepare a public release

WidgetRail ships through per-user Inno Setup with an unpackaged WinUI frontend.
Application MSIX packaging and its signing/trust prerequisite have been removed.
See the [installer contract](../../eng/installer/README.md) for deployment details.

Use this checklist for the exact commit and artifacts being offered to users.
The project has accepted local installers, version stamping, and initial
branding. Publishing those artifacts is a separate step.

## Prepare the public repository

- Review the README, user guides, and product screenshots in `docs/images`.
- Check the MIT license and third-party license/branding notices.
- Review the Git history being published for secrets and personal information. Deleting old notes from the current tree does not erase history.
- Establish the reporting channel described in [SECURITY.md](../../SECURITY.md).
- Run CI on the exact proposed main commit after an authorized push.

## Check documentation for this build

- Run the focused [documentation check](documentation.md#run-the-check). It checks
  relative links/images, public learning paths and selected source contracts;
  it does not require running the application suite.
- Review changed user flows alongside their implementation: exact Settings labels,
  default opening shortcut, startup behavior, permission prompts and installation
  enablement. Check Bare/Production against `eng/release-content.json`.
- Reconcile app-update behavior with the initiating release as well as the target
  installer. An older installed updater does not acquire the new flow in advance.
- Check each listed integration's actual setup, connection verification and
  credential-removal action. A packaged add-on is not proof of service access.
- Review the screenshot/demo [inventory](demonstrations.md) against the candidate.
  Record exact app/widget versions, commit and tested configuration for new media;
  identify older illustrative captures rather than implying current validation.
- Keep one detailed explanation per topic, and update its summary links. Retire
  completed plans and superseded candidate logs to Git history after preserving
  current contracts, reusable fixture commands and relevant acceptance coverage.

The repository retains three operating and ownership documents:
`docs/delivery-plan.md`, `docs/review-planner-goal.md`, and
`docs/implementation-agent-goal.md`. They are not user guides or the public API
contract. Keep an active implementation plan and its verification ledger until
delivery is complete. Retire superseded plans after transferring current operational
guidance and unresolved acceptance requirements; use Git history for old discussion.

## Build the editions

Build Bare (Settings and Catalogue) and Production (the generally useful starting
selection declared in `eng/release-content.json`). Both include identical host
capabilities, shared runtimes and CLI. Developer edition is no longer shipped;
source samples remain usable with the SDK. Bundle choice must not limit later
widget installation.

From a clean committed checkout with the [development tools](building.md):

```powershell
pwsh -NoProfile -File .\scripts\Build-Release.ps1
pwsh -NoProfile -File .\scripts\Get-InstallerCompiler.ps1
pwsh -NoProfile -File .\scripts\Build-Installer.ps1 `
  -ReleaseRoot .\artifacts\releases\0.1.0-preview.38 `
  -CompilerPath .\artifacts\tools\installer-compiler\package\tools\ISCC.exe
```

Use the actual version from the build output when it changes. Existing release
destinations are not overwritten. The build records source and packaging revisions
and validates the catalogs and file inventory for both editions.
The builder compiles WinUI plus the shared native platform/preview boundaries;
it does not build the retired native renderer or require Rust/Taffy.

Read the [installer contract](../../eng/installer/README.md) for runtime provisioning,
startup ownership, uninstall behavior, and the supported GameInput minimum.

## Check the user journey

- Fresh installation on supported Windows without developer tools.
- Missing-runtime prompts, cancellation, failure messages, and retry after restart.
- Upgrade and edition switch with settings and add-ons preserved.
- Startup enabled/disabled, including a Windows Startup Apps override.
- Widget installation, permission review, manual update, and removal.
- Uninstall with data kept and with explicit data deletion.
- Representative controllers, display scales, media, window previews, and games.

Include browser page/pin transfers, permission cancellation, webapp removal and
clear-data/update preservation; notification suppression and stale entries;
capture with optional application audio; and representative performance-monitor
providers (unavailable readings must not look like zero). For service integrations,
verify each account/profile separately, including PoE1/PoE2 trade sign-in and
Playnite's installed/portable, running/stopped and missing-Bridge cases. Exercise
multiple display scales and actual controllers on the exact release candidate.
Historical implementation checklists do not qualify a new release automatically.

Full-access add-ons may save data outside the host's directories, including
credentials in Windows Credential Manager. The host uninstaller does not
generically erase arbitrary add-on-owned stores. Review and document those
cleanup boundaries before describing uninstall as removing every possible sign-in.

## Publish deliberately

Publish every Catalogue-listed widget as a package, including widgets preinstalled
by Production. SDK Gallery, Clock, Embedded Media Sample and Full Application
Reference remain buildable source examples; they do not need Catalogue listings
or release assets. Discord, Game Help, Display Controls, Twitch and MapGenie remain
separate downloads. Build their release assets separately:

```powershell
./samples/DiscordWidget/Build-CommunityPackage.ps1
./samples/GameHelpWidget/Build-CommunityPackage.ps1
./samples/DisplayProfilesWidget/Build-CommunityPackage.ps1
./samples/TwitchWidget/Build-CommunityPackage.ps1
./samples/MapGenieWidget/Build-CommunityPackage.ps1
```

The common builder also packages the first-party optional widgets and Browser:

```powershell
foreach ($widget in @('AudioMixer','CaptureLibrary','PerformanceMonitor','MediaSessions',
    'GamesApps','TaskSwitcher','NetworkControls','Power','RecentApps','Browser')) {
    ./scripts/Build-MarketplacePackage.ps1 -Widget $widget -OutputDirectory ./artifacts/catalogue-addons/release
}
```

PoB, Spotify, YouTube Music and Playnite retain their package builders because their
payloads have additional requirements. Check Catalogue inventory against actual
published assets; do not publish a new listing without its validated package.

Publish the resulting immutable `.wrwidget` archives alongside the release. GitHub's asset digests are supported; when publishing a checksum manifest, combine the generated `SHA256SUMS.txt` entries into one release-wide file with each asset listed once. A shared `-OutputDirectory` accumulates that file automatically. Individual `.sha256` sidecars are also generated for manual verification. The Catalogue selects compatible published assets; a catalog entry alone is not a downloadable package. Review installation/enablement and permission requests with a fresh widget catalog before release.

Choose the executable-signing and distribution policy. Application signing is
optional for this installer path. Test the download/install experience
on a normal user account, including the warnings an unsigned preview may show.
Review third-party redistribution terms for the actual bundle and retain notices.

Advance versions according to [Versioning](versioning.md), publish immutable
artifacts with checksums and release notes, and describe remaining compatibility
limits. Clean-machine coverage and signing are release work, not something a
documentation pass can certify.
