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

Open **Settings → Widgets → Install local widget** and choose the package file.
Full-access packages require explicit approval. Fresh installations start enabled;
choose the widget from the tray and grant access when a feature requests it.

For scripted development, [CLI package operations](../reference/cli-packages.md)
show installation, version selection, rollback, and removal in an isolated catalog.

## Share a release

Include your source, license, setup instructions, and supported WidgetRail/SDK
versions. Attach the package to an immutable release in your repository and
publish its SHA-256 checksum through a channel users can trust.

The CLI can install a pinned HTTPS or GitHub Release asset. A checksum detects
different bytes; it does not authenticate the author. The current platform has
no publisher-signing/revocation service or automatic marketplace updater.
`wrail releases owner/repository` lists packages on public GitHub releases.
GitHub asset digests and standard `SHA256SUMS.txt` files let users install
without copying a hash manually. Use stable asset names or
`<package-id>-<version>.wrwidget` to make later update discovery straightforward.

## Ship an update

Increase the widget manifest version before distributing changed bytes. Users
choose **Update from file** in the widget details, or use **Marketplace → Updates**
for curated packages. Updates preserve enabled/stopped state and carry ordinary widget settings after
review. Security permissions and credentials are not transferred to changed
unsigned package identities. Full-access updates require approval. Keep a useful description of changes and
any new permissions, which should be requested when a feature needs them.
CLI users can run `wrail update <widget-id>` to review a GitHub update, then
run the exact apply command printed by the report. For a local package, use
`wrail update <widget-id> --file <package.wrwidget>` instead of raw install. Checking never installs or
activates a package. See [GitHub package updates](../reference/cli-packages.md#review-and-apply-an-update).

The old version remains installed until removed. Do not overwrite published
bytes or promise that a new package can silently inherit previous consent.
See [Versioning](../maintainers/versioning.md).

## Choose a license

Your widget can use the license you choose. WidgetRail's first-party widgets and
templates are MIT-licensed examples, but service SDKs, artwork, and other dependencies
retain their own terms. Include their required notices in your distribution.
