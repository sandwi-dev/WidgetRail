# Widget package format

A `.wrwidget` is a ZIP package with a manifest, code, and declared assets. Users
install a specific immutable version, then review and enable it.

Startup verifies enabled, selected versions rather than reading every retained
payload. The requested initial widget is validated first, and verified widgets
become available progressively. An incomplete catalog does not mean that other
widgets were uninstalled. Finishing discovery does not select a widget, reopen
the overlay, or restore pins.

The lightweight inventory used by marketplace and CLI lists contains display
information and version directories only; it grants no execution or permission
authority. Enabling, selecting a different version, permission review, and
launch admission still use verified package content. Launch holds verified file
handles, so inventory data or file timestamps never substitute for validation.

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
version. Updates automatically keep the current version and one previous version.
Older package versions are removed after publication and selection succeed;
locked files are retried on a later catalog change. Existing longer histories
are trimmed when that widget is next installed or updated, not during startup.
The larger internal discovery ceiling permits legacy catalogs and cleanup retries.

Marketplace and Settings updates preserve enabled state; CLI updates require the
widget to be disabled first. Settings exposes **Revert to previous version** in
its existing **Technical details** section. Disable the widget before reverting,
then review permissions and enable it again. After reverting, use the normal Marketplace, file or CLI update flow
to return to a newer version. A retained package is verified against the reviewed
archive and reused without overwriting its immutable files. Rollback changes package selection;
it does not restore a snapshot of saved widget data.

Raw CLI installation keeps an existing explicit selection and retains it with
the incoming version until selection. Package versions remain immutable.
`wrail dev` uses isolated generations and is unaffected by normal retention.
Users can uninstall a community widget through Settings. Saved widget data is
preserved; cleanup never grants permissions or copies credentials.
See [Package and share](../developers/publishing-and-installation.md).

## Runtime boundary

An ordinary package uses the generic worker host in its mandatory sandbox. A
manifest cannot request the trusted Settings worker's launch privileges or a
desktop-token fallback. Full-access applications are a separately reviewed model.

See [Security boundaries](../maintainers/security-and-trust.md) for details.
The format and installation implementation live in
[`WidgetProtocol`](../../src/WidgetProtocol/) and [`WidgetCatalog`](../../src/WidgetCatalog/).
