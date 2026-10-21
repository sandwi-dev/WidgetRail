# App updates

Open **Settings → About & troubleshooting → App updates**. Automatic checks only
notify; downloading and installing remain explicit user actions.

## Install an update

Routine updates can be completed with the controller when the target installer's
prerequisites are already satisfied and Windows permits it to run.

1. Choose **Download update**. WidgetRail downloads only the new installer for the
   installed edition and verifies its SHA256 against the GitHub release digest or
   checksum manifest. Download progress and cancellation remain available.
2. Choose **Install and restart**. The helper verifies and locks the installer,
   checks the installation identity, and uses the target installer's supported
   preflight to check prerequisites before closing WidgetRail.
3. When ready, WidgetRail closes gracefully, setup runs without a wizard, and the
   verified installed app reopens. Startup at sign-in, automatic-check preferences
   and installation location are preserved.

If prerequisites require setup, or the installer does not advertise support for
this unattended flow, WidgetRail stays open and offers **Open interactive setup**.
That second action explicitly confirms the fallback. Have a mouse and keyboard
available for the installer and any Windows approval prompts. Silent setup cannot
approve UAC or bypass Windows execution policies.

Cancelled or failed setup reopens a verified usable installation where available.
A recorded result appears in App updates and an update notification; restart-required
and startup-registration warnings explain the next action. If no usable payload can
be verified, the helper leaves diagnostics instead of launching unverified files.

Routine installation has no extra review dialog or manual quit step. There is no
profile-backup or rollback UI. Settings, sign-ins, widget data and browser profiles stay
in place. Existing recovery folders created by earlier releases are left alone;
the legacy recovery mutex is still respected while an older helper owns the profile.

Installation is available only from the registered Inno payload using the normal
profile. Development candidates can check releases but cannot replace the installed
app. Downloads are limited to 1 GiB and 15 minutes. The helper waits at most two
minutes for the host to exit; it never force-kills it. Checksum verification is not
an Authenticode signing claim; Windows can still show unsigned-installer warnings.

The initiating version owns the updater. Updating from an older release still uses
that release's flow. The unattended flow requires both a supporting updater and
a supporting target installer.

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

Service and handoff tests use controlled HTTP, temporary files and inert callbacks
for readiness/exit ordering, locks, checksums, changed installations, cancellation,
failure and timeouts. Six additional real-Inno upgrade cases cover Bare and
Production with startup enabled, disabled and foreign-owned. Those cases use the
actual payload and handoff, isolated registration/shortcut/data names, complete
installed-file verification and hidden relaunch. They preserve edition and saved
update-check choices without changing the normal installation.

This does not establish unmodified production-registration, clean-machine
prerequisite/UAC, Windows execution-policy, or real restart/failure-recovery
acceptance. Requalify those cases against the exact release using the
[release checklist](../maintainers/release-checklist.md). Historical run evidence
is retained in Git history. Temporary native candidates cannot install app updates.
