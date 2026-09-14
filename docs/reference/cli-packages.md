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
`github:owner/repository@tag/asset.wrwidget`, with an explicit SHA-256 pin.
The shorthand names an exact asset, not the latest release and not a repository
to clone and execute.

Hashes verify bytes, not publishers. Remote installation does not create an
automatic update subscription. See [Publishing](../developers/publishing-and-installation.md)
and [Package format](widget-packaging.md).
