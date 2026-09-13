# Package and share your widget

A `.wrwidget` is the file users install. Start with a working project from the
[quickstart](widget-quickstart.md), then prepare one version you want to share.

## Build a package

Run your generated tests, then:

```powershell
& $wrail validate .
& $wrail pack .
```

Use the output path reported by `pack`. It performs a bounded isolated Release build
for source packages and checks the manifest and assets. Keep the generated
`NuGet.Config` and local SDK feed with the source so builds do not depend on an
unrelated WidgetRail checkout.

See [Package format](../reference/widget-packaging.md) for layout and validation rules.

## Install locally

Open **Settings → Widgets**, choose the package file, and review its permissions.
Installation shows a toast; it does not automatically enable the widget.
Once enabled, choose it from the tray.

For scripted development, [CLI package operations](../reference/cli-packages.md)
show installation, version selection, rollback, and removal in an isolated catalog.

## Share a release

Include your source, license, setup instructions, and supported WidgetRail/SDK
versions. Attach the package to an immutable release in your repository and
publish its SHA-256 checksum through a channel users can trust.

The CLI can install a pinned HTTPS or GitHub Release asset. A checksum detects
different bytes; it does not authenticate the author. The current platform has
no publisher-signing/revocation service or automatic marketplace updater.

## Ship an update

Increase the widget manifest version before distributing changed bytes. Users
choose **Update from file** in the widget details, then review and enable the
new version. Keep a useful description of changes and any new permissions.

The old version remains installed until removed. Do not overwrite published
bytes or promise that a new package can silently inherit previous consent.
See [Versioning](../maintainers/versioning.md).

## Choose a license

Your widget can use the license you choose. WidgetRail's first-party widgets and
templates are MIT-licensed examples, but service SDKs, artwork, and other dependencies
retain their own terms. Include their required notices in your distribution.
