# Publishing and installation

Status: deterministic pack/install/catalog commands and bounded HTTPS release
downloads implemented; publisher signing and native-host catalog discovery are
not production-ready

## Recommended workflow today: share source and immutable releases on GitHub

Until publisher signing and a graphical review flow exist, publish widget
source in a dedicated GitHub repository:

1. Include source, `manifest.json`, GBSS, assets, replay files, and a license.
2. Document the required Game Bar Alternative commit or protocol/SDK version.
3. Run `dotnet build`, `gbar validate`, `gbar render`, `gbar replay`, and
   `gbar pack` in CI.
4. Treat repository code and GitHub Actions as reviewable source, not proof of
   publisher identity.
5. Attach the deterministic `.gbarwidget` to a versioned GitHub Release and
   publish its SHA-256 digest through a channel users can authenticate.
6. Label the release **developer preview / unsigned** until publisher signing
   is implemented.

The SDK does not yet have a supported public NuGet release. External widget
repositories must currently reference a checked-out SDK project or vendor a
specific compatible SDK build. Do not advertise the placeholder preview
package reference emitted outside this repository as an available package.

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
extract, discover, enable, and order these packages. Installation is immutable
by `<id>/<version>` and fails if the same version already exists. See the
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
installed and enabled, the command refuses the update without changing the
working version; disable it explicitly before retrying. This prevents a failed
download or extraction from changing the active widget's state. Review the
source, manifest permissions, publisher, and reported digest before opting in:

```powershell
gbar list
gbar disable dev.example.volume-control
# Run the remote install command again when updating an enabled widget.
gbar enable dev.example.volume-control
```

If installation used `--catalog`, pass that same value to both commands.

## Install from a local package

Local files use the same command and package validator:

```powershell
gbar install .\dev.example.volume-control-0.1.0.gbarwidget
gbar list
gbar disable dev.example.volume-control
gbar enable dev.example.volume-control
```

A newly discovered widget ID is disabled by default. Explicitly enable it after
review. Installing another local version of an ID preserves that ID's existing
catalog state. A remote update requires that ID to be disabled before the
downloaded version can be installed.

The default catalog is
`%LOCALAPPDATA%\GameBarAlternative\widgets`. Every catalog command accepts the
same explicit override:

```powershell
gbar install .\widget.gbarwidget --catalog .\artifacts\test-catalog
gbar list --catalog .\artifacts\test-catalog
gbar disable dev.example.widget --catalog .\artifacts\test-catalog
```

There is no graphical installer, automatic updater, signature verification, or
marketplace client. The current native host does not automatically discover
this user catalog; install/list/enablement currently exercise the safe catalog
and future host-facing state.

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

These controls limit common downloader and archive attacks. They do not make
remote widget code trustworthy or sandbox it. Review [security and
trust](security-and-trust.md) before installing a third-party package.

A direct URL is an explicit user-authorized network destination, not a
host-curated allowlist. Literal-address checks reduce obvious local-network
mistakes; they do not resolve hostnames up front and do not claim to prevent DNS
rebinding. Use GitHub shorthand or a public HTTPS origin you intentionally
trust, and never put credentials or tokens in an install URL.

Installing a package only writes validated files and catalog state. It does
not launch a worker, send lifecycle transitions, or change the lifecycle
contract. When host catalog discovery is implemented and the widget is later
launched, the normal `Created`, `Background`, `Visible`, `Interactive`, and
`Destroying` rules apply unchanged.

## Programmatic catalog API

Applications and tests can also use the same implemented library API:

```csharp
var catalog = new WidgetCatalog(currentUserCatalogRoot);
var installer = catalog.CreateInstaller();

var inspection = await installer.ValidateAsync("Example.gbarwidget");
var installed = await installer.InstallAsync("Example.gbarwidget");
var snapshot = await catalog.DiscoverAsync();
```

CLI or library installation validates archive containment and manifest shape.
It does not establish who published the code. The current native host uses a
trusted deployment catalog for its reference worker; it does not automatically
discover the user catalog yet.

## Planned production flow

The intended flow is explicitly **planned**:

1. A publisher builds and signs an immutable `.gbarwidget`.
2. GitHub Releases or another HTTPS source hosts the exact bytes plus signed
   metadata; the current bounded downloader acquires the package.
3. A future trust layer verifies publisher identity, host compatibility,
   permissions, and revocation before the existing atomic installation.
4. The user reviews permissions and enables the widget.
5. The host resolves the installed version into trusted bridge configuration
   and launches it under production isolation.

Rollback/pinning UI, update checks, garbage collection, cross-process install
locking, signature chains, and revocation are also planned.

Read [security and trust](security-and-trust.md) before running a package you
did not build yourself.
