# Publishing and installation

Status: deterministic pack/install/catalog commands, bounded HTTPS release
downloads, immutable version pin/rollback CLI, live last-good bridge catalog
revisions, and controller package review/enablement are implemented; publisher
signing, automatic update discovery, and version removal/garbage collection are
not

## Recommended workflow today: share source and immutable releases on GitHub

Until publisher signing and a curated production distribution flow exist,
publish widget source in a dedicated GitHub repository:

1. Include source, `manifest.json`, GBSS, assets, replay files, and a license.
2. Document the required Game Bar Alternative commit or protocol/SDK version.
3. Run `dotnet build`, typed-fake tests, `gbar validate`, data-only
   `gbar render`/`gbar replay` fixtures, and `gbar pack` in CI. Do not load the
   built DLL into a full-trust CI helper.
4. Treat repository code and GitHub Actions as reviewable source, not proof of
   publisher identity.
5. Attach the deterministic `.gbarwidget` to a versioned GitHub Release and
   publish its SHA-256 digest through a channel users can authenticate.
6. Label the release **developer preview / unsigned** until publisher signing
   is implemented.

Recipients see **Unsigned · publisher unverified** in Settings together with
the package's sealed content-tree SHA-256 before enablement. That digest-bound
review prevents replacement bytes from inheriting authority, but it is not a
signature and does not prove that the manifest publisher or GitHub account is
the author. Share the expected digest through an independent authenticated
channel.

The SDK does not yet have a supported public NuGet release. External widget
repositories must currently reference a checked-out SDK project or vendor a
specific compatible SDK build. `gbar new widget --sdk-project
C:\path\to\GameBarAlternative\src\WidgetSdk\WidgetSdk.csproj` creates that
explicit local-project contract. Without a discovered or supplied project, the
command fails before writing; it no longer emits the unpublished placeholder
package reference.

## `.gbarwidget` packages

The implemented package contract is a ZIP-compatible archive with the
`.gbarwidget` extension and root `manifest.json`. Typical contents are:

```text
manifest.json
payload/Widget.dll
payload/supporting-library.dll
styles/default.gbss
assets/...
```

The `gbar` CLI and `WidgetCatalog` library can deterministically pack, validate,
extract, discover, pin, roll back, enable, and order these packages.
Installation is immutable by `<id>/<version>` and fails if the same version
already exists. See the
complete [packaging contract](widget-packaging.md).

`gbar pack` recursively includes the supplied directory, so package from a
clean staging directory rather than a project tree containing `obj`, source,
secrets, or unrelated files. The staging root must contain exact-case
`manifest.json` and the assembly path named by the manifest.

For the standard `payload/<Widget>.dll` layout, one possible staging flow is:

```powershell
$packageRoot = '.\artifacts\VolumeControl-package'
New-Item -ItemType Directory -Force -Path "$packageRoot\payload", "$packageRoot\styles"
dotnet publish .\VolumeControl\VolumeControl.csproj -c Release -o "$packageRoot\payload"
Copy-Item .\VolumeControl\manifest.json "$packageRoot\manifest.json"
Copy-Item .\VolumeControl\styles\default.gbss "$packageRoot\styles\default.gbss"

gbar validate $packageRoot
gbar pack $packageRoot --output .\artifacts\dev.example.volume-control-0.1.0.gbarwidget
Get-FileHash .\artifacts\dev.example.volume-control-0.1.0.gbarwidget -Algorithm SHA256
```

Packing uses ordinal entry order, fixed timestamps/metadata, bounded content,
and the same installer inspection used later by `gbar install`, so identical
staged content produces identical package bytes.

## Publish a GitHub Release asset

Publish an immutable asset rather than asking users to install a branch or a
repository archive:

1. Choose a versioned tag. Do not reuse a tag for different package bytes.
2. Run the Release build, validation, render/replay checks, and `gbar pack`
   from a clean checkout or CI job.
3. Compute SHA-256 over the final `.gbarwidget` asset, not its staging folder:

   ```powershell
   $package = '.\artifacts\dev.example.volume-control-0.1.0.gbarwidget'
   $digest = (Get-FileHash $package -Algorithm SHA256).Hash.ToLowerInvariant()
   $digest
   ```

4. Create the GitHub Release for that exact tag and attach the package. Publish
   the 64-character digest in release notes and, where possible, through a
   separate authenticated project channel users already trust.
5. Copy the exact owner, repository, tag, and asset filename into the install
   example. Test that example against an empty catalog before announcing it.

GitHub hosting and a matching hash do not make an unsigned widget safe. Keep
source and build instructions available so users can review or reproduce the
artifact.

## Install from a GitHub Release

The shortest supported source is a deterministic GitHub Release reference:

```powershell
gbar install `
  github:example/volume-control@v0.1.0/dev.example.volume-control-0.1.0.gbarwidget `
  --sha256 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
```

Its grammar is:

```text
github:<owner>/<repository>@<tag>/<asset.gbarwidget>
```

All four components are required. The CLI converts the reference directly to
`https://github.com/<owner>/<repository>/releases/download/<tag>/<asset>`.
It does not query the GitHub API, infer a latest release, clone the repository,
build source, or execute repository scripts. Owner, repository, tag, and asset
must be conservative single path segments; path separators and unsafe segment
characters are rejected. Name the asset with a `.gbarwidget` suffix.

The same package can be installed from its complete HTTPS URL:

```powershell
gbar install `
  https://github.com/example/volume-control/releases/download/v0.1.0/dev.example.volume-control-0.1.0.gbarwidget `
  --sha256 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
```

Replace the example digest with the release asset's actual 64-hex-character
SHA-256 value. Hexadecimal casing is ignored. `--sha256` is required for every
remote install. A digest confirms exact bytes only when users obtain the
expected value through an independently trusted channel; a hash posted beside
a compromised asset does not establish publisher identity.

Every successful remote install reports the downloaded asset's actual SHA-256
digest. A mismatch fails before catalog installation. Retain the reported value
for audit or comparison, but do not mistake a value learned from the download
itself for an independent trust decision.

Remote installation leaves the widget disabled. If that widget ID is already
installed and enabled, the command refuses the update without publishing the
new version or changing the working version; disable it explicitly before
retrying. This prevents unreviewed code or a failed download/extraction from
changing the active widget's state. Review the
source, manifest permissions, publisher, and reported digest before opting in.
The preferred controller flow is Settings → Installed widgets; the CLI remains
available for scripted/test catalogs:

```powershell
gbar list
gbar disable dev.example.volume-control
# Run the remote install command again when updating an enabled widget.
# Inspect every installed version and make the intended version explicit.
gbar version list dev.example.volume-control
gbar version select dev.example.volume-control 0.2.0
# Prefer reviewing/enabling the result in Settings. CLI equivalent:
gbar enable dev.example.volume-control
# Removal is also an explicit disabled-only operation:
gbar disable dev.example.volume-control
gbar uninstall dev.example.volume-control
```

The complete update review sequence is therefore:

1. Disable the widget ID.
2. Install the exact new GitHub Release asset with its independently obtained
   SHA-256 pin. The old immutable version remains installed.
3. In Settings → Installed widgets, open the package and choose **Manage
   versions**. Select the exact new version. For scripted catalogs, use `gbar
   version list <widget-id>` followed by `gbar version select <widget-id>
   <new-version>`.
4. Review the selected identity, version, compatibility, and capabilities on
   the package details page.
5. Enable the widget as a separate action.

An ID without an explicit active-version pin uses the greatest installed
`System.Version`; explicitly selecting the reviewed release removes that
implicit choice from later discovery. If an older version was already pinned,
installing a new release preserves that pin and prints the exact `version
select` command needed to choose the new package. If the update fails after
enablement, disable the widget and run `gbar version rollback <widget-id>` to
select the greatest installed version older than the active one, or add `--to
<version>` for a specific installed older version. Rollback leaves the widget
disabled so the selected package can be reviewed before re-enabling it. Use
`version select`, not `rollback`, to move forward again.

If installation used `--catalog`, pass that same value to both commands.
The packaged bridge and Settings widget read the default current-user catalog;
an override is an alternate test/script catalog and is managed with matching
CLI commands rather than appearing in the packaged overlay.

`gbar uninstall <widget-id>` removes every installed immutable version and the
ID's catalog-state entry only after the widget is disabled. Package discovery
is retired with an atomic directory move before bounded deletion, so no
partially deleted version becomes executable. A file lock after that commit
point is reported as pending cleanup; the next install or uninstall retries a
bounded staging sweep. Provider-owned private secrets
are a separate authority and are not enumerable by the catalog; widgets that
offer private-data removal must clear their known slots through the declared
host service before uninstall.

## Install from a local package

Local files use the same command and package validator:

```powershell
gbar install .\dev.example.volume-control-0.1.0.gbarwidget
gbar list
gbar disable dev.example.volume-control
gbar version list dev.example.volume-control
gbar version select dev.example.volume-control 0.1.0
gbar enable dev.example.volume-control
```

A newly discovered widget ID is disabled by default. Explicitly enable it after
review in Settings → Installed widgets (or with the CLI for automation).
Installing another local version of an ID preserves that ID's existing catalog
state and the old version-addressed package. Follow the same disable, install, explicit
version selection, review, and enable sequence for local updates. Both local
and remote updates reject an enabled ID before publishing the new immutable
version into the catalog.

For a local install, the CLI first rejects missing files, wrong extensions,
and reparse-point sources, then opens the package once for read-only access
with read sharing. Optional SHA-256 verification, archive inspection, the
enabled-ID policy decision, extraction, and publication all operate from that
same open stream. The installer invokes the policy after validating the exact
archive identity and before publishing any version directory, so changing the
source path during the command cannot substitute different package bytes. A
policy rejection removes staging and leaves the installed catalog unchanged.
Remote installation applies the same pre-publish policy to its already locked,
digest-verified temporary-file stream.

The default catalog is
`%LOCALAPPDATA%\GameBarAlternative\widgets`. Every catalog command accepts the
same explicit override:

```powershell
gbar install .\widget.gbarwidget --catalog .\artifacts\test-catalog
gbar list --catalog .\artifacts\test-catalog
gbar disable dev.example.widget --catalog .\artifacts\test-catalog
gbar version list dev.example.widget --catalog .\artifacts\test-catalog
```

There is no file-picker/graphical installer, automatic updater, signature
verification, or marketplace client. The bridge watches the default
current-user catalog without polling, validates a complete replacement, and
publishes semantic changes live. Listing/reload never launches a worker.
Invalid trusted shell state retains the last-good catalog. Invalid installed
state or package integrity publishes a trusted-only revision, removes Community
registrations, and retires their workers; unsupported styles/capabilities on an
individual package fail soft with bounded diagnostics.

The default host policy bounds the complete installed catalog, not only each
archive: 256 widget IDs, eight versions per ID, 512 versions total, 32,768
content-file/integrity-metadata entries, 2 GiB of content plus a conservative
4 KiB integrity-metadata reservation per version, and a 30-second elapsed-work
budget. Cancellation/deadline checkpoints run during recursive entry inspection
and before each bounded file read (at most 64 KiB), as well as between versions.
A synchronous Windows filesystem read already blocked in the kernel cannot be
preempted by that token. Installation
reserves the incoming package against those same ceilings before publication;
a refusal removes staging and cannot leave the new version discoverable.
Unexpected files at the ID/version directory layers fail discovery closed.
These are supervisor policy defaults, not manifest-controlled resource
requests. Disable and uninstall unused versions/IDs before retrying a refused
install; a Settings remediation flow and measured maximum-catalog baseline
remain product work.
Recursive reparse inspection stops at the catalog entry ceiling, and each
version's filename collector stops at its archive limit plus one before sorting
or hashing, so a tampered tree cannot force an unbounded preflight allocation.

Installed manifest and host-owned integrity metadata are each read from one
restrictively shared handle with a consumed-byte ceiling. Content-tree hashing
uses one captured length per file and rejects early EOF, extra bytes, or a
changed seekable length. These checks prevent a size preflight from authorizing
more bytes than the catalog consumes. Integrity verification parses an owned
copy of the exact manifest bytes included in the tree hash and returns that
model with the verified digest, so policy and digest cannot come from different
reads. On each worker start or restart, the catalog reacquires the full verified
inventory and retains delete-denying directory/file handles for that session.
It reads Windows volume/file identities from those same handles; the bridge
carries path, authority role, and identity together, and runtime refuses a
different object before journal publication, DACL mutation, pipe creation, or
process launch. The identities are intentionally session-local rather than
catalog metadata. Ancestor path traversal is still checked lexically rather
than opened component-by-component relative to a pinned catalog-root handle.

For installed widget styles, the catalog also carries an exact relative-path/
SHA-256 inventory computed in the same tree-hash pass. The bridge reads each
GBSS entry/import through one consumed-byte-bounded handle, decodes strict
UTF-8, and compares its bytes with that inventory before parsing. Modified and
late-added style sources fail closed. Executable assemblies, lazy dependencies,
and general package assets use the same verified-content lease described above.

After CLI installation, open Settings → Installed widgets. The paginated
controller surface shows package ID, publisher, active/installed versions,
runtime, host-API range, architectures, compatibility result/reason, and
required versus optional capability declarations before enable or disable.
**Manage versions** opens a five-item-per-page nested scope: LB/RB change pages,
B returns to package details, and each exact version is labeled Active,
Rollback, or Select newer plus Compatible/Incompatible. Version rows are
actionable only while the widget is disabled. Selecting one keeps it disabled
and requires separate review and enablement. An incompatible package cannot be
enabled, though an already enabled incompatible entry can be disabled for
recovery/update. Enabling only joins a compatible widget to the overlay; it
does **not** grant a capability. On that same widget management page, open
**Permissions & configuration** to confirm a grant or immediately deny/revoke
it. Required means the feature is core, not that it is automatically granted.
Optional means the widget must degrade without it.

An accepted catalog change invalidates native descriptors/caches. Compatible
presentation-only changes preserve a running worker while atomically replacing
its validated style/quick-action metadata; identity, executable, arguments,
declared capabilities, instance, or memory-policy changes retire it and start a
new authenticated worker lazily on next use. Disable/removal safely returns an
active widget to an available dashboard selection. For a genuine in-flight
change event, a transient native list/parse failure gets only the bounded 250,
500, and 1000 ms retry sequence; hiding abandons it, and ordinary open-time
failures never create polling. The same revision can be announced again and a
later open/list catches up. No overlay restart is required. See [widget
capabilities](capabilities.md).

## Remote acquisition safety boundary

Remote acquisition is deliberately smaller than a package manager:

- Only absolute HTTPS URLs on port 443 are accepted. Embedded credentials and
  URL fragments are rejected. `localhost`, `.localhost` subdomains, and
  loopback, unspecified, private, or link-local IP address literals are also
  rejected for both initial requests and redirects.
- Redirects are followed at most five times, and every redirect target must
  also use HTTPS. This permits ordinary GitHub Release asset redirects without
  allowing an HTTPS-to-HTTP downgrade.
- Connection establishment is bounded to 10 seconds, response headers to 20
  seconds, and the complete operation to 120 seconds.
- A declared `Content-Length` over 72 MiB is rejected before download. The
  streaming path enforces the same 72 MiB compressed-byte ceiling when the
  header is absent or inaccurate.
- Requests require identity content encoding; encoded responses are rejected
  so the limit and digest apply to the exact transferred package bytes.
- Downloads use a unique temporary file. Success, validation failure, hash
  mismatch, timeout, cancellation, and network failure all delete it.
- After acquisition and required digest comparison, the existing package
  installer applies the archive, manifest, path, expansion, and immutable
  catalog checks documented in the [packaging contract](widget-packaging.md).

These controls limit common downloader and archive attacks. They do not prove a
remote package's publisher or intent. Runtime AppContainer isolation is a
separate mandatory execution boundary applied only if an installed/community
worker is later launched. Review [security and trust](security-and-trust.md)
before installing a third-party package.

A direct URL is an explicit user-authorized network destination, not a
host-curated allowlist. Literal-address checks reduce obvious local-network
mistakes; they do not resolve hostnames up front and do not claim to prevent DNS
rebinding. Use GitHub shorthand or a public HTTPS origin you intentionally
trust, and never put credentials or tokens in an install URL.

Installing a package only writes validated files and catalog state. It does
not launch a worker, send lifecycle transitions, or change the lifecycle
contract. After explicit enablement, a compatible package joins the live
dashboard catalog but remains unlaunched until first use.
The normal `Created`, `Background`, `Visible`, `Interactive`, and `Destroying`
rules then apply unchanged.

## Programmatic catalog API

Applications and tests can also use the same implemented library API:

```csharp
var catalog = new WidgetCatalog(currentUserCatalogRoot);
var installer = catalog.CreateInstaller();

var inspection = await installer.ValidateAsync("Example.gbarwidget");
var installed = await catalog.InstallAsync("Example.gbarwidget");
await catalog.SetActiveVersionAsync(installed.Id, installed.Version);
var snapshot = await catalog.DiscoverAsync();
```

`SetActiveVersionAsync` accepts only an installed version while the widget is
disabled. Catalog-state schema 1 remains readable and has no explicit pin; the
next successful mutation writes schema 2. A schema-2 pin to a missing version
fails discovery closed with `active_version_missing` rather than silently
executing different code. Reinstalling that exact pinned package through
`WidgetCatalog.InstallAsync` repairs the catalog in a forced-disabled state.
`WidgetPackageInstaller` exposes validation only to callers; publication goes
through the catalog so install, enablement, and active-version decisions share
one cross-process operation lock.

CLI or library installation validates archive containment and manifest shape.
It does not establish who published the code. The bridge combines its trusted
deployment catalog with enabled compatible entries from the default user
catalog, then maintains complete validated live revisions. This discovery/
reload path is not publisher proof; do not enable code you do not already trust.

## Planned production flow

The intended flow is explicitly **planned**:

1. A publisher builds and signs an immutable `.gbarwidget`.
2. GitHub Releases or another HTTPS source hosts the exact bytes plus signed
   metadata; the current bounded downloader acquires the package.
3. A future trust layer verifies publisher identity, host compatibility,
   permissions, and revocation before the existing atomic installation.
4. The user reviews permissions and enables the widget.
5. The bridge resolves the installed version through the generic worker host;
   the current lazy path requires the package-specific capability-free
   AppContainer, Job Object restrictions, and PID/nonce/identity-authenticated
   main and broker pipes with no desktop-token fallback.

Until publisher signing exists, the host seals every installed unsigned content
tree and derives authority from its verified SHA-256 digest rather than its
self-asserted publisher label. Changed bytes receive a new AppContainer profile,
capability-consent identity, and private-secret namespace—even if package ID and
version text are unchanged—so Settings must review grants separately and an
integration may require re-pairing. Rolling back to the exact previously
verified bytes restores only that content identity and its prior decisions.
This blocks silent unsigned-update authority inheritance but does not prove who
published either byte tree.

Update checks, version removal/garbage collection, signature chains,
revocation, CPU quotas, disk/profile quotas and cleanup, and an audit UI are
also planned. The implemented Settings and CLI pin/rollback flows deliberately
do not remove immutable versions or discover updates automatically.

Read [security and trust](security-and-trust.md) before running a package you
did not build yourself.
