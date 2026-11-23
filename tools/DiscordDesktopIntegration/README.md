# Discord Desktop integration setup tools

These tools prepare an isolated, reproducible-input **diagnostic Vencord build**
and inspect application-installation metadata. They do not install Vencord, patch
Discord, launch/restart clients, copy profiles or read authentication data.
They are not a production installer or approval to change an installed client.

## Pinned build

`vencord-source.json` pins upstream Vencord 1.15.13 at
`443688b5366a4d43e9868519f48a4544cd7520d4`, its dependency lockfile digest and
pnpm 11.9.0. Node 22 or newer and Corepack are required. The source-build stage
uses Corepack's exact package manager, a frozen lockfile and disabled dependency
lifecycle scripts. No `pnpm inject`, `uninject` or Installer command is executed.

```powershell
./tools/DiscordDesktopIntegration/Stage-Vencord.ps1 -PlanOnly
./tools/DiscordDesktopIntegration/Stage-Vencord.ps1
```

The default plugin source is `samples/DiscordDesktopAdapter/plugin`. Only build
source files are copied; `*.test.*` files are excluded. Every copied source is
hashed before and after staging. A fresh child directory under this checkout's
`artifacts/discord-desktop` is required; existing outputs and reparse-point paths
are rejected. `-PrepareOnly` checks out and hardens the source without restoring
dependencies or building. `-PlanOnly` only validates paths and prints the plan.

Every build retains source, lockfile, logs, the exact hardening patch and output
digests in `provenance.json`. Fixed source timestamp/hash build inputs are used;
this is not a claim of byte-identical outputs across arbitrary toolchains.
The pinned source's full TypeScript check also runs after the esbuild output.
Build success does not establish compatibility with Discord's running internals.

The isolated probe intentionally differs from normal Vencord:

- Build with `--disable-updater` so the custom build does not replace itself.
- Remove the single `persistAfterDiscordUpdates` import. Upstream otherwise
  rewrites a newer Discord installation's bootstrap on host updates or exit.
- Restrict renderer and native-helper discovery to `src/userplugins`, containing
  only our plugin. Upstream includes required plugins that cannot be switched
  off merely through settings. The probe does not include those unrelated hooks.
- Source-map inspection verifies exactly our plugin and native helper are
  registered, and no automatic-host-repatch module entered the result. Vencord's
  shared settings/API exports still statically import several other plugin
  definitions; these are recorded as support sources, not registered/started.
  Live validation must check initialization behavior rather than equating a
  registration allowlist with proof of no side effects anywhere in Vencord.
- Remove the bootstrap's forced Discord developer-tools setting. The generated
  patcher is checked not to contain that preference key.
- Prepare (but never install/run) `bootstrap-index.js`, selecting a fresh
  build-owned Vencord data directory before requiring our patcher. Its `DATA_DIR`
  agrees with `VENCORD_USER_DATA_DIR`; no existing Vencord preferences are reused.

After compilation, pinned `@electron/asar@4.0.1` packages `bootstrap.asar` from
that entrypoint and a two-field Discord package manifest. Its lockfile is checked
in here; dependencies restore only into the staged `build-tools` directory with
scripts disabled. The library lists/extracts the result to verify exactly two
entries and byte-identical payloads. The archive path/hash is in
`provenance.json` under `bootstrapProposal`; installation requires no download or
packaging dependency. `-PrepareOnly` does not restore this tool or create the archive.

The bootstrap checks the four required staged runtime files **before loading any
Vencord module**. If the build was moved/cleaned or a required path is inaccessible,
it loads the saved sibling `_app.asar` original exactly once, with Electron's app
path and main-module entrypoint set to that original. It does not set isolated
Vencord environment variables on this fallback path. This assumes the original
archive remains intact; missing/corrupt original files still require recovery.

There is deliberately no catch-and-restart fallback after Vencord initialization
begins: hooks or part of Discord may already have started. Such failures require
closing the selected client and using the hash-conditional reviewed restore path,
then starting ordinary Discord. Retrying the original in the same process could
double-start it. `bootstrap.test.mjs` validates preflight absence/non-file cases,
exactly-once loading, app/main paths, untouched fallback environment and propagation
of partial-initialization/original-client failures using stubbed modules only.

This is a minimal diagnostic build, not the standard Vencord distribution. It
must be validated live after explicit installation approval. Any later need for
an upstream core/API plugin requires a reviewed inclusion, not silent expansion.
The plugin must not depend on excluded API plugins. The output remains subject
to Vencord's GPL-3.0-or-later terms; preserve the exact upstream source and patch
and review source-distribution obligations before distributing a build.

## Read-only installed-client inventory

```powershell
./tools/DiscordDesktopIntegration/Read-DiscordInstallation.ps1
./tools/DiscordDesktopIntegration/Read-DiscordInstallation.ps1 -IncludeFileHashes
```

The default roots are the per-user Discord, DiscordPTB and DiscordCanary
application installations. `-InstallationRoot` permits explicitly selected
locations. The tool reads only version-directory, executable and resource-file
metadata. It deliberately does not enumerate account storage or Discord modules.
It lists all candidate app versions and separately identifies exact executable
paths observed in running processes. It never chooses the highest directory as
the running client. `_app.asar` or an `app` directory means “requires review,”
not “known Vencord.” An `app.asar` alone is not proof of a vanilla installation.

## Installation and rollback proposal — not implemented/executed

The upstream Installer was reviewed at
`595991ca27520868af4f9c48adf0c53ee971f1f2`:

- `app_asar.go:WriteAppAsar` creates a small bootstrap whose entrypoint requires
  the selected Vencord `dist/patcher.js`.
- `patcher.go` renames original `app.asar` to `_app.asar`, installs the bootstrap,
  and restores that original during uninstall. Partial rename rollback exists.
- `find_discord_windows.go:ParseDiscord` selects the lexicographically highest
  `app-*` resource directory. `PreparePatch` kills a process by executable name.
- CLI `--location` selects an install root, not an exact running app version.
  `VENCORD_DEV_INSTALL=1` uses local custom build files. Vencord's `pnpm inject`
  downloads/runs the latest Installer; it is not a pinned, non-disruptive probe.

Consequently, do not invoke `pnpm inject` against the user's installation as the
next step. It can target a newer on-disk version than the one running, and can
interrupt a call. The bootstrap design is reusable, but the exact-path admission
and rollback transaction need a guarded setup operation.

Prepare these reviewable items before requesting installation approval:

1. Exact selected executable/resource directory, running PID/creation identities,
   current loader classification, and hashes of every application file to change.
2. Staged custom-build path/digests, source provenance, a separate Vencord data
   directory with no copied user settings, and the exact bootstrap payload.
3. Explicit close/restart scope, with any active call impact explained. Recheck
   all identities and require the selected client to be closed before file edits.
4. Backup/journal paths outside account storage; pre-write hashes; atomic staging
   of the bootstrap; verification that original Discord entrypoints are preserved.
5. A tested restore operation that acts only while our installed bootstrap's bytes
   still match the journal. External changes/upgrades are a conflict, not permission
   to restore an obsolete backup. Preserve other loaders and Vencord settings.
6. An isolated launch selecting that adapter's data directory without a global
   environment change; restore ordinary startup at removal. Authentication stays
   in the existing Discord profile. Do not silently copy it into a test profile.

If the installation is already modified, stop at classification and prepare a
coexistence-specific proposal. Replacing an existing Vencord distribution requires
separate explicit approval. The managed build's disabled auto-repatch hook means
a Discord update can remove the integration; expose repair rather than rewriting
other versions silently.

### Observed update transition during approved launch

In the parent-controlled live attempt on 2026-10-27, Computer Use's app-launch
operation resolved the supplied executable to Discord's registered Squirrel
launcher. That launched version 1.0.9266 and the normal updater removed 1.0.9265,
instead of launching the just-patched 1.0.9265 executable. An executable argument
to an application-launch abstraction is therefore not proof of the process image
that will run. Verify the actual image/version before attaching or attributing a
missing adapter to the plugin itself.

The 9265 external original backup/journal remains evidence; it must never be
restored over 9266. Stop the old transaction, revalidate the new target and
authorization scope, and prepare a separate version-specific journal. Do not
enable Vencord's automatic future-version patcher to work around this transition.
The read-only 9266 revalidation is retained at
`artifacts/discord-desktop/reviewed-target-9266.json` and
`runtime-source-review-9266.json`. Its ASAR package retains the `discord` /
`bundle.js` entrypoint. Voice-wrapper and desktop-core hashes changed; the
required native method names remain present in source, which does not establish
runtime behavior. No installed files were changed by that revalidation.

### Exact diagnostic launch

`Start-DesktopProbe.ps1` is plan-only by default. It takes the installed
transaction journal and its separately reviewed digest, rather than an arbitrary
application name or shortcut:

```powershell
./tools/DiscordDesktopIntegration/Start-DesktopProbe.ps1 `
    -JournalDirectory <reviewed-journal-directory> `
    -ExpectedJournalSha256 <reviewed-journal-sha256>
```

Only an explicit `-Launch` performs one process creation. It rechecks the exact
executable, installed bootstrap, original backup and pinned runtime artifacts,
then uses an absolute executable path with `ProcessStartInfo.UseShellExecute=true`.
It does not resolve a registered application ID, invoke Update.exe, kill an app,
restart an existing instance or retry an uncertain launch. Already-running target
processes produce an `already-running` result; another running version/updater
of the same installation blocks launch. The returned process ID must resolve to
the exact expected image. Image verification is not adapter-readiness proof.

The raw `UseShellExecute=false` launch in the parent's approved live attempt
inherited tool output pipes and Discord's logger raised `EPIPE`. The successful
corrected launch used ShellExecute without redirected stdin/stdout/stderr. The
reusable launcher follows that path; hidden window style is advisory and does
not promise Discord will remain hidden. User call state is not changed by the
launcher itself, although starting Discord naturally runs its ordinary startup.

Installer and launcher share exact-file admission in `DesktopProbeFiles.ps1`.
Both reject a relocated/copied stage: the bootstrap contains absolute references,
so validating identical files at a new path would not validate the files actually
loaded from its old path. The declared output directory and isolated Vencord data
directory must match the selected stage. Regenerate/review the bootstrap when
moving a build; never edit only its provenance to bypass this check.

## Checks

`Test-SetupTools.ps1` uses generated non-executable files under artifacts. It checks
read-only inventory, multiple version preservation, honest loader classification,
optional hashes, absent installations, staging boundaries, no overwrite, build/test
source separation and no writes in plan-only mode. It never opens a real profile
or invokes Discord, an installer, or media services.
Pass `-StagedBuild <output-directory>` to additionally validate the recorded
artifact hashes, registration allowlist, absent automatic repatcher/forced
developer-tools setting and absence of initialized Vencord data.
Run `node --test tools/DiscordDesktopIntegration/bootstrap.test.mjs` for the
deterministic bootstrap fallback checks; staging also runs these before packaging.
`Test-DesktopProbeLaunch.ps1` covers plan-only behavior, one exact ShellExecute
launch, no redirected streams, changed-file/journal rejection, running/other-version
clients, process-state changes during preparation, unexpected image/exit outcomes
and relocated stages. Process creation is intercepted only in a generated script
copy, and every target is a generated non-executable fixture. No installed client
is launched by that suite. Installer fixtures also cover copied-stage and wrong
Vencord-data-directory rejection before any transaction writes.
