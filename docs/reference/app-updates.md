# App updates

Open **Settings → About & troubleshooting → App updates**. Automatic checks only
notify; downloading and installing remain explicit user actions.

## Install an update

1. Choose **Download update**. WidgetRail downloads only the new installer for the
   installed edition and verifies its SHA256 against the GitHub release digest or
   checksum manifest. The page shows download progress and allows cancellation.
2. Choose **Install and restart**. A small helper verifies and locks the installer,
   checks the installed version and acknowledges readiness. Only then does the
   host close gracefully. If preparation fails, the app remains open for retry.
3. The normal interactive Windows installer opens after WidgetRail exits. On
   successful setup, the helper verifies the newly registered version and reopens
   WidgetRail. Cancelling setup leaves the current installation available from Start.

There is no extra review dialog, manual quit step, profile backup, old-installer
download, or rollback UI. Settings, sign-ins, widget data and browser profiles stay
in place. Existing recovery folders created by earlier releases are left alone;
the legacy recovery mutex is still respected while an older helper owns the profile.

Installation is available only from the registered Inno payload using the normal
profile. Development candidates can check releases but cannot replace the installed
app. Downloads are limited to 1 GiB and 15 minutes. The helper waits at most two
minutes for the host to exit; it never force-kills it. Checksum verification is not
an Authenticode signing claim; Windows can still show unsigned-installer warnings.

The initiating version owns the updater. Updating from an older release still uses
that release's flow; the simplified behavior starts with preview.34.

## Automatic checks and notifications

**Automatically check for updates** defaults on for new installations. Settings and
setup use `automatic-app-updates.txt`; upgrades preserve an existing saved choice.
Windows sign-in retains deferred initialization: workers and checks start when the
overlay is first opened. After startup settles, one check runs after ten seconds.
Manual Check works regardless of this preference. Network failures appear on the
update page rather than interrupting play.

Available updates produce one pending entry per running session, subject to
Settings' notification preferences and quiet-banner policy. Opening it from the
bell navigates to App updates. Notifications are not persisted across restart;
each startup may notify again. Checks do not download or install anything.

Stable installs consider stable releases; previews also consider prereleases.
The newest 20 public releases are checked for the matching edition, with numeric
preview-version ordering. Release notes are plain text, up to 8,000 characters,
split into protocol-bounded wrapping blocks in the scrollable update page.

## Validation

The service tests use fake HTTP and a controlled helper acknowledgement. The
Windows PowerShell handoff tests use inert installer callbacks and temporary files,
covering readiness/exit ordering, file locks, checksums, changed installations,
installer cancellation/failure and host-exit timeouts. They do not run an installer
or alter a real profile. Full installed-version upgrade acceptance remains physical.

Preview.34 validation: service scenarios 10/10; Settings 92/92; isolated Windows
PowerShell handoff checks passed. Production Settings snapshots passed 21 native
renderer/focus checks at 520/880 DIP and normal/large text. Long notes and the ready
page were visually reviewed; scrolling reaches the install action. No real installer
was executed during these checks. Existing profile/recovery data was not modified.
