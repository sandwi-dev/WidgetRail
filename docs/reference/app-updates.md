# App updates and recovery

Open **Settings → About & troubleshooting → App updates & recovery**. Checks can
run after startup when enabled; WidgetRail never downloads, closes itself, or installs on a timer.
Stable installations consider stable releases; preview installations also consider
previews. The check considers the newest 20 public non-draft GitHub releases and
matches the installed production/developer edition. Notes are displayed as text
(up to 8,000 characters), with numeric preview-version ordering.

## Install an update

1. Review release notes, then choose **Download update & recovery**. Both the new
   installer and the exact currently installed version's installer are downloaded
   from WidgetRail's public release repository. SHA256 values come from the GitHub
   asset digest or published checksum manifest. This is integrity verification,
   not an Authenticode-signing claim.
2. Choose **Review installation**, then **Continue**. Quit WidgetRail from Settings; a Windows confirmation opens afterward. The app is never force-closed.
3. The helper makes a verified copy of the entire local WidgetRail profile, then
   starts normal interactive Inno Setup. Cancelling Setup retains recovery data.

Installing is available only from the registered Inno payload with the normal
profile. Development candidates can inspect releases but cannot replace your app.
If the exact previous installer is unavailable, downloading for installation fails
safely; do not infer that an arbitrary older version can read the current profile.
Each installer has a 1 GiB bound and a 15-minute download limit. Cancel stops network
work; no installer runs as a consequence of downloading.

## Recovery from Settings

Open **Settings → About & troubleshooting → App updates → Recovery**.
When a matching recovery set is available, choose **Restore version …**, review
what will be restored, then continue and quit WidgetRail. The helper runs the
retained previous Inno installer and restores its matching pre-update profile.
The newer profile is preserved as `profile-after`; completed recovery is
idempotent. Recovery is offered only for the current installed version and
requires a fresh exact-metadata confirmation. Development candidates cannot
replace the installed app.

No desktop or Start-menu recovery shortcut is created. Recovery through an app
that cannot launch is outside this feature's scope. Existing backups and any
previously created shortcuts are not deleted by this change.

Private recovery folders live in `%LOCALAPPDATA%\WidgetRail-Recovery`, outside the
active `%LOCALAPPDATA%\WidgetRail` profile. Keep these until the new version is
accepted. They contain private widget data and must not be shared as support
bundles. Nothing is uploaded. Up to three folders are retained; remove obsolete
folders deliberately before preparing more updates.
Incomplete/cancelled preparation folders may also be removed after checking that
no active operation uses them. There is no automatic deletion of recovery data.

A backup includes settings, widget packages, configuration and data inside the
profile, with their ACLs. It is limited to 10 GiB and 100,000 filesystem entries.
Linked paths, unreadable/in-use files, insufficient space or incomplete copies stop
installation. SHA256 manifests detect damaged backup files before restore. Phase
markers preserve the recovery decision across interrupted installation/profile swaps.
The updated frontend refuses startup while the recovery helper owns the profile.
Do not start old versions or CLI profile mutations while recovery runs.

Windows Credential Manager, OS driver/permission registrations and data that
full-trust widgets store outside WidgetRail's profile are not restored. Accounts
may require signing in again. Recovery does not claim automatic backward schema
compatibility: it restores the old app **and** the corresponding profile. A manual
new-profile copy can be inspected later to recover post-update changes.

## Validation and remaining physical checks

Isolated service tests use fake HTTP and never launch executables. Windows
PowerShell recovery tests exercise real temporary filesystem copies/ACLs with
inert installer callbacks. Checks cover exact confirmation, cancellation, preview
ordering, checksums, missing recovery assets, backup fidelity, failed setup,
interrupted restore, locked files and junction refusal.

The full Inno upgrade/downgrade, Windows dialog flow, credential reauthentication,
and in-app recovery still need physical acceptance. No application update
has been installed during automated validation. Windows may show unsigned-installer
warnings, and organizational PowerShell restrictions can prevent recovery launch.

The standalone helper shows a Windows progress window while preparing and verifying the profile. Preparation can be cancelled before Setup starts; existing data is preserved. Once Setup starts, use the installer's own Cancel control.

Uninstalling normally preserves recovery copies. Choosing the uninstaller's explicit **delete local data** option also removes WidgetRail-Recovery, including profile backups and retained installers. Exported files remain. Any recovery shortcuts created by an older version can be removed separately.

The in-app helper waits silently for up to two minutes for the app to exit, preserving controller focus until then. The app does not promise that a prepared operation remains resumable after that helper exits; return to App updates to review its current status. No confirmation or installer opens over an active overlay as part of this handoff.

## Automatic checks and notifications

**Automatically check for updates** defaults on for new installations and is
available in both Settings and setup. Both use the profile's
`automatic-app-updates.txt` (`enabled` or `disabled`); upgrades preserve the saved
choice unless changed in setup. Silent upgrades retain an existing value.

Windows sign-in keeps WidgetRail's existing deferred initialization: the Bridge,
widgets and update-check task do not start until the overlay is first opened
(for example, with Guide). After that startup settles, the frontend waits ten
seconds before asking the Bridge to check once. This does not require opening
Settings or starting a WebView. Checks never
download or install. Manual Check remains available when automatic checks are
off. Network errors remain in App updates status, without a blocking dialog.

An available version produces one pending notification per running session,
subject to Settings' notification preferences and normal quiet-banner policy.
Open from the bell navigates to App updates using `widgetrail.settings.updates.open`.
Pending notifications and dismissal state are not saved across restarts. Every
new startup may notify again. Repeated checks in a session do not create duplicate
entries or repeated banners for the same version.
