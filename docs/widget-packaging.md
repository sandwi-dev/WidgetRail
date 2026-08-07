# Widget packaging and local catalog

Status: implemented prototype contract

For the current GitHub sharing workflow and missing user-facing commands, see
[publishing and installation](publishing-and-installation.md). For the trust
boundary, see [security and trust](security-and-trust.md).

Executable community widgets are distributed as immutable `.gbarwidget`
packages. The format is ZIP-compatible for tooling convenience, but the
installer treats every archive and every extracted path as untrusted.

## Package layout

```text
manifest.json
payload/
  Example.Widget.dll
  supporting-library.dll
styles/
  default.gbss
assets/
  metadata.png           # packaged content; general asset brokering is planned
signature.json          # reserved for the planned signing phase
```

`manifest.json` must be at the archive root with exact casing. Its
`entrypoint.assembly` must identify an actual file with identical casing.
The ID must equal the publisher namespace or begin with
`<publisher>.`. Versions use the existing dotted numeric manifest format;
their textual form must already be canonical.

The package filename is informational. Installation identity always comes from
the validated manifest.

The archive may contain regular files under `assets/`, but successful packing
does not make them renderable. General package-asset resolution is not wired to
the widget protocol yet. Images currently use the bounded HTTPS image node;
icons use the closed semantic glyph set.

## Archive safety rules

The prototype defaults are:

- At most 512 archive entries
- At most 16 MiB expanded per entry
- At most 64 MiB expanded across the package
- At most 240 UTF-16 characters per relative path

Limits are checked against archive metadata and again while streaming bytes to
disk. Overflow and length mismatches fail installation.

Paths must use forward slashes, Unicode NFC, and portable Windows names. The
installer rejects:

- Absolute paths, drive paths, `.` and `..` segments
- Backslashes, control characters, Windows-invalid characters, trailing dots
  or spaces, and reserved device names
- Duplicate paths, case-only collisions, and file/directory prefix collisions
- Unix symbolic links, Windows reparse entries, and other special file types
- Catalog or staging directories that resolve through a reparse point

The catalog root is caller-supplied and intended to be a private,
current-user-owned application-data directory. ACL provisioning and production
package signature verification remain host responsibilities.

## Atomic immutable installation

The installer validates archive metadata and the manifest before extraction.
It then:

1. Creates a random staging directory on the catalog volume.
2. Streams regular files with `CreateNew` while rechecking limits.
3. Revalidates the staged manifest and identity.
4. Creates the ID parent and atomically moves the complete staged directory to
   `packages/<id>/<version>`.

An existing destination is never replaced or merged. Installing an update adds
a new version beside the old version; discovery selects the greatest
`System.Version` as active. A failed install deletes its staging directory
and cannot damage an installed version.

## Discovery and user state

`WidgetCatalog` discovers only this layout:

```text
<current-user-root>/
  catalog-state.json
  packages/
    dev.example.clock/
      1.0.0/
        manifest.json
      1.1.0/
        manifest.json
  staging/
```

Directory ID and version must exactly match the validated manifest. Discovery
fails closed when installed content is missing, malformed, reparse-backed, or
identity-mismatched. Widget IDs use ordinal ordering; versions use descending
`System.Version` ordering.

`catalog-state.json` stores only enablement and presentation order. New IDs
are enabled by default and appended in ordinal ID order. State updates serialize
through an instance lock, flush a uniquely named sibling temporary file, and
atomically replace the state file. Package contents remain immutable.

## API sketch

```csharp
var catalog = new WidgetCatalog(userCatalogRoot);
var installer = catalog.CreateInstaller();

var inspection = await installer.ValidateAsync("Clock.gbarwidget");
var installed = await installer.InstallAsync("Clock.gbarwidget");

await catalog.SetEnabledAsync(installed.Id, enabled: true);
await catalog.SetOrderAsync(["dev.example.clock", "dev.example.audio"]);

WidgetCatalogSnapshot snapshot = await catalog.DiscoverAsync();
```

`SetOrderAsync` moves the supplied installed IDs to the front and preserves
the relative order of remaining widgets.

## Deliberately deferred

- Publisher signature and certificate-chain verification
- Online catalog metadata, downloads, updates, and revocation
- Rollback/pinning UI and garbage collection of old versions
- Cross-process locking for concurrent host and installer processes

Until signing is implemented, successful structural validation proves package
shape and containment—not publisher authenticity or code safety. Executable
widgets must still run through the isolated worker and capability model.
