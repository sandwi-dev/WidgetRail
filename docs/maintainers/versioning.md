# Versioning

WidgetRail has several version numbers because they identify different things.
They should not all increment together.

## What the repository does today

| Component / contract | Current authority | Meaning |
|---|---|---|
| Desktop application | No canonical application release version or public tag yet | A local Git commit identifies a candidate; default assembly or crate versions are not an app release |
| SDK and `wrail` | `eng/WidgetSdkRelease.props`: `0.3.0-dev` | One reviewed developer release unit, including its author-facing application bootstrap |
| Local scaffold SDK package | Developer version plus `.local.<content hash>` | Exact local content, not a published NuGet release |
| ControllerWidget template format | `ControllerWidgetTemplateVersion`: `2` | Template contract format; bundled with the matching CLI |
| Widget packages | Each widget's `manifest.json` version | Independently versioned immutable packages |
| Theme packages | Each theme's manifest version | Independently versioned data packages |
| Presentation protocol | `ProtocolConstants`: supported range `1–52` | Wire format and feature requirements, not SemVer |
| Manifest / broker capabilities | Manifest schema and IDs such as `system.power.control.v1` | Separate schema and operation contracts |
| Native layout crate | `Cargo.toml`: `0.1.0`, `publish = false` | Internal build metadata, not a separately released product |

The SDK/CLI contract already has compatibility tests and a reviewed public API
baseline. The desktop application still needs release-version plumbing.
No product version or public tag was created by this documentation change.

## Recommended public release policy

### One desktop application release

Use SemVer with prereleases, for example **`v0.1.0-preview.1`** for a first app
preview. This is a proposed tag, not an existing release.

Release the native host, bridge, broker, generic workers, bundled widgets,
resources and dependency notices together. Users should never assemble these
from different releases. Internal libraries do not need independently marketed
version numbers.

Before shipping, add one canonical application version source and stamp it into
the native version resource, managed product metadata, release manifest and
artifact filename. Include the exact Git commit. Do not use the SDK version or
the native layout crate version as a substitute.

### One developer release unit

Keep **SDK, CLI and bundled templates synchronized**, as they are today.
Publish a distinct immutable version for every externally distributed artifact;
for example `sdk-v0.3.0-preview.1` for the first developer preview.

- During `0.x`, breaking SDK changes advance the minor version and include
  migration notes.
- Compatible fixes/additions advance the patch or prerelease sequence within
  the current minor line.
- After `1.0`, use normal SemVer: major for breaking changes, minor for compatible
  additions, patch for compatible fixes.
- Keep template format changes explicit. A new CLI release does not require a
  new template format number when the format itself is unchanged.
- Local content-hash package versions remain useful for development, but are not
  a replacement for public release identities.

Follow the existing [SDK evolution policy](../developers/widget-sdk-evolution.md)
for deprecation and migration obligations.

### Independent widget and theme versions

Each separately distributed widget or theme keeps its own SemVer version.
A Spotify widget fix should not force a Playnite package version change.
Bundled widget versions can also remain independent internally; the app
release manifest records which exact versions and digests it contains.

Never replace published bytes under an existing package version. Build a new
version even for an asset-only correction. Do not infer widget compatibility
from a matching app version; check manifest requirements, SDK compatibility,
capabilities and the supported presentation protocol.

### Protocol and schema versions

Keep protocol feature versions and capability `.v1` identifiers separate from
release SemVer. An app patch can ship the same protocol, and a new protocol
feature does not require renumbering every widget.

The manifest's `hostApi` range is a compatibility contract, not the desktop
application's marketing version. Define its published support policy alongside
the first release rather than treating existing `1.0` declarations as proof
of universal cross-version compatibility.

## Every release records its contents

Record the app version, source commit, SDK/CLI unit, protocol range, bundled
widget/theme versions, dependency versions, checksums and required runtimes.
Publish matching source and license notices. Keep old release artifacts immutable.
Automating that manifest, signing, packaging and update validation remains a
[release checklist](release-checklist.md) task.
