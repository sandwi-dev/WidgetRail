# Widget package format

A `.wrwidget` is a ZIP package with a manifest, code, and declared assets. Users
install a specific immutable version, then review and enable it.

## Typical layout

```text
manifest.json
payload/
    MyWidget.dll
styles/
    default.wrss
assets/
    ...
```

For an ordinary `dotnet-worker`, `entrypoint.assembly` points to the packaged
assembly and the type names a concrete SDK widget. Full-access applications
use their own runtime/entrypoint contract. Do not mix those two bootstrap models.

The manifest also declares identity, publisher label, version, host compatibility,
permissions, supported architecture, and optional presentation/residency settings.
Use a generated template and `wrail validate` instead of creating fields by guesswork.

## Validation and installation

The catalog checks archive paths, duplicates/collisions, sizes, manifest shape,
declared content, and compatibility. Archive traversal or a path escaping the
package is rejected. Package files are inspected before publication.

Installation publishes a version without overwriting an existing version's bytes.
A file lock and verified content identities protect the launch handoff; this is
not a claim that a user-owned directory is physically immutable forever.

A digest proves which bytes were reviewed. It does not prove the publisher's
identity. Changed unsigned content requires its own review and cannot inherit
authority simply by reusing a name or version string.

## Selection and updates

The catalog records the selected version separately from the versions on disk.
An invalid or missing selected version should not silently execute some other
version. A successful manual update selects the new version disabled for review,
while preserving the previous installed version.

Users can remove unused versions and uninstall a community widget through Settings.
These operations protect the current selection and preserve saved widget data.
See [Package and share](../developers/publishing-and-installation.md).

## Runtime boundary

An ordinary package uses the generic worker host in its mandatory sandbox. A
manifest cannot request the trusted Settings worker's launch privileges or a
desktop-token fallback. Full-access applications are a separately reviewed model.

See [Security boundaries](../maintainers/security-and-trust.md) for details.
The format and installation implementation live in
[`WidgetProtocol`](../../src/WidgetProtocol/) and [`WidgetCatalog`](../../src/WidgetCatalog/).
