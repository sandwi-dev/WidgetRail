# Publishing and installation

Status: deterministic local pack/install/catalog commands implemented; signed
public distribution and native-host catalog discovery are not production-ready

## Recommended workflow today: share source on GitHub

Until publisher signing and a graphical review flow exist, publish widget
source in a dedicated GitHub repository:

1. Include source, `manifest.json`, GBSS, assets, replay files, and a license.
2. Document the required Game Bar Alternative commit or protocol/SDK version.
3. Run `dotnet build`, `gbar validate`, `gbar render`, `gbar replay`, and
   `gbar pack` in CI.
4. Treat repository code and GitHub Actions as reviewable source, not proof of
   publisher identity.
5. Attach the deterministic `.gbarwidget` and its hash to a GitHub Release,
   labeled **developer preview / unsigned**.

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

## Install and manage a local package

Download a GitHub Release asset yourself, verify the hash through a channel you
trust, then pass the local file to the CLI:

```powershell
gbar install .\dev.example.volume-control-0.1.0.gbarwidget
gbar list
gbar disable dev.example.volume-control
gbar enable dev.example.volume-control
```

The default catalog is
`%LOCALAPPDATA%\GameBarAlternative\widgets`. Every catalog command accepts the
same explicit override:

```powershell
gbar install .\widget.gbarwidget --catalog .\artifacts\test-catalog
gbar list --catalog .\artifacts\test-catalog
gbar disable dev.example.widget --catalog .\artifacts\test-catalog
```

These commands operate on local files only. There is no graphical installer,
automatic updater, URL/GitHub downloader, signature verification, or
marketplace client. The current native host does not automatically discover
this user catalog; install/list/enablement currently exercise the safe local
catalog and future host-facing state.

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
   metadata.
3. The installer verifies publisher identity, integrity, host compatibility,
   permissions, and revocation before atomic installation.
4. The user reviews permissions and enables the widget.
5. The host resolves the installed version into trusted bridge configuration
   and launches it under production isolation.

Rollback/pinning UI, update checks, garbage collection, cross-process install
locking, signature chains, and revocation are also planned.

Read [security and trust](security-and-trust.md) before running a package you
did not build yourself.
