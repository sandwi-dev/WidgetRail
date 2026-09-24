# Versioning

The application, SDK, and widgets have separate versions because people update
them for different reasons. An audio-widget fix does not need to renumber every
community package.

## The version a user installs

The **application version** identifies a complete WidgetRail release. Its source
is `eng/WidgetRailRelease.props`; the current preview is `0.1.0-preview.12`.
Both Production and Developer use this version. Their contents differ, not
their stability channel.

The host, bridge, workers, built-in widgets, resources, and private runtime ship
together. Native and managed product metadata, artifact names, and release
manifests use the application release settings. Users update by running a newer
installer; there is no automatic updater.

The Windows numeric file version is `0.1.0.11` for this preview. A source commit
distinguishes local candidates with the same preview label. Never replace a
published release's bytes: advance its version before distributing a correction.

## The version a widget author builds against

The **SDK and CLI** are a coordinated developer release unit. Their source is
`eng/WidgetSdkRelease.props`; the current unit is `0.4.0-dev`. It includes matching
templates and the author-facing application bootstrap.

The scaffold bundles an exact SDK package in a local feed. Its version adds a
`.local.<content-hash>` suffix so different SDK contents are not confused. This
is an offline developer workflow, not evidence of a published NuGet feed.

During `0.x`, breaking SDK changes advance the minor version and need migration
notes. Compatible fixes or additions advance the patch/prerelease version.
After `1.0`, use the usual SemVer major/minor/patch rules. See
[SDK evolution](../developers/widget-sdk-evolution.md) for the detailed policy.

## Widget and theme versions

Each widget or theme owns its manifest version. Update it when distributing new
bytes, even for an artwork change. Installed versions are immutable so users can
review an update and keep a previous version for rollback.

A widget's manifest declares the host API range it expects. That range is a
compatibility contract; it is not the application's marketing version. Matching
version numbers alone do not establish compatibility.

## Protocol and internal versions

| Version | Meaning |
|---|---|
| Presentation protocol (currently 1–54) | The UI data features supported between a widget and host |
| Capability suffix, such as `.v1` | A particular Windows-service contract |
| Manifest version | The package schema |
| Template format (currently 2) | The format understood by the scaffold tooling |
| Native layout crate version | Internal build metadata |

These numbers change when their own contracts change. They do not all move
with an application patch.

## Record what shipped

`release.json` records the app and SDK versions, exact compiled-source commit,
packaging commit, edition, bundled widgets, and file inventory. Installer metadata
records its packaging revision too. This allows packaging-only fixes to be
distinguished from newly compiled application code.

Suggested public tag names are `v0.1.0-preview.1` for the application and
`sdk-v0.3.0-preview.1` for a separately published SDK preview. These are naming
examples, not a claim that those tags have been published. Follow the
[release checklist](release-checklist.md) before publishing either.
