# CLI package operations

These commands continue the [generated project example](cli-projects.md).
All catalog commands below use `scratch/catalog`, a separate test catalog.
They do not change the widgets in your normal installation. To try the widget
in the ordinary overlay, install its package through Settings instead.

## Pack and install

Inspect an existing package before installation:

```powershell
& $wrail inspect .\example.wrwidget
```

This validates the archive using the installer's rules and shows its identity,
SHA-256, execution model, required and optional permissions, host API range,
architectures and size. It does not install the package or execute widget code.
Full-trust packages can be inspected without approving their installation.
Add `--json` for structured metadata. Inspection validates package structure;
it does not prove publisher identity or runtime behavior.

<!-- canonical-author-journey:pack-install -->
```powershell
& $wrail pack .\scratch\VolumeControl `
  --configuration Release `
  --output .\scratch\dev.example.volume-control-0.1.0.wrwidget
& $wrail install .\scratch\dev.example.volume-control-0.1.0.wrwidget --catalog .\scratch\catalog
```

Installation publishes a package version; enablement is separate. Review the
widget and requested permissions before enabling it in an ordinary user catalog.

## Select or roll back a version

First change the test project's manifest version to `0.2.0`, pack it again,
and install that second package into the same `scratch/catalog` with `--catalog`.
The following commands assume both `0.1.0` and `0.2.0` exist there.

<!-- canonical-author-journey:version-lifecycle -->
```powershell
& $wrail disable dev.example.volume-control --catalog .\scratch\catalog
& $wrail version list dev.example.volume-control --catalog .\scratch\catalog
& $wrail version select dev.example.volume-control 0.2.0 --catalog .\scratch\catalog
# Test catalog only; enable after reviewing the example package:
& $wrail enable dev.example.volume-control --catalog .\scratch\catalog
& $wrail disable dev.example.volume-control --catalog .\scratch\catalog
& $wrail version rollback dev.example.volume-control --catalog .\scratch\catalog
& $wrail version select dev.example.volume-control 0.2.0 --catalog .\scratch\catalog
```

The version in this example must exist before selection. Versions are immutable:
changed bytes require a new package version. A missing selected version must
not silently run an unreviewed alternative.

## Remove the test package

<!-- canonical-author-journey:remove -->
```powershell
& $wrail disable dev.example.volume-control --catalog .\scratch\catalog
& $wrail uninstall dev.example.volume-control --catalog .\scratch\catalog
```

Package removal preserves saved widget data. Clearing private state, deleting
credentials, and uninstalling the application are separate operations.

## Install a remote release

The CLI supports an absolute HTTPS URL or
`github:owner/repository@tag/asset.wrwidget`. An HTTPS URL requires an explicit
`--sha256` pin. With GitHub shorthand, you can omit it when GitHub publishes an
asset digest or the release includes a supported checksum manifest.
The shorthand names an exact asset, not the latest release and not a repository
to clone and execute.

Hashes verify bytes, not publishers. Remote installation does not create an
automatic update subscription. See [Publishing](../developers/publishing-and-installation.md)
and [Package format](widget-packaging.md).

## Discover packages on GitHub

```powershell
& $wrail releases example/widgets
& $wrail releases example/widgets --include-prerelease --page 2
& $wrail install github:example/widgets@v1.0.0/example.wrwidget
```

Discovery lists widget and theme assets from one page of up to 20 releases.
Use `--tag` for an exact release or `--json` for structured results. Prereleases
are omitted unless requested. Discovery supports public repositories; it does
not read GitHub credentials or clone source.

When `--sha256` is omitted, installation first uses GitHub's asset digest. If
there is none, it reads `SHA256SUMS.txt`, `SHA256SUMS`, or `checksums.txt` from
the same release, in that preference order. These use the standard
`<64-hex-hash>  <exact-filename>` format. Missing or duplicate matching entries
stop installation. An explicit hash always takes precedence. Checksums identify
the downloaded bytes; they are not publisher signatures.

## Review and apply an update

```powershell
& $wrail update dev.example.volume-control
& $wrail theme update dev.example.ocean-night
```

These commands check GitHub's latest stable release and download one candidate
for validation and review. Nothing is installed by a check. The widget report
shows the version, required/optional permission changes, execution model and
checksum. Use `--tag` to check a particular release, including a prerelease.

GitHub installations remember their source, matched to the installed version
and content. If no source was recorded, supply `--repo owner/repository`.
When an asset cannot be identified unambiguously, choose `--asset filename`
from `wrail releases`. Stable asset names or `<package-id>-<version>` names
are easiest to discover.

The report prints a PowerShell apply command containing the exact repository,
tag, asset and SHA-256. Review it before running it. A widget must be disabled
before apply; the update transaction checks identity, publisher, compatibility
and current selection again, then selects the new version disabled. Changed
execution models are rejected. Full-trust widgets still require explicit
`--accept-full-trust`. Previous versions remain available for rollback.

Theme updates add a version without changing the selected theme. By default,
the check compares against the newest installed version of that theme; use
`--version` to choose another installed version. Select the new version in
Settings > Appearance when ready.

Source records are bounded optional metadata under `.wrail-sources` in the
chosen catalog/settings root. A metadata write failure warns without undoing
an installation. CLI uninstall/removal clears the corresponding records.
There is no periodic checking, automatic retry loop or background updater.
