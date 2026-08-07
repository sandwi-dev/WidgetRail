# Widget packaging and catalog

Status: implemented prototype package and catalog contract

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
a new version beside the old version. Without an explicit version pin,
discovery selects the greatest `System.Version` as active. Once selected, an
installed version remains pinned until another explicit selection changes it.
A failed install deletes its staging directory and cannot damage an installed
version.

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

Schema-version-2 `catalog-state.json` stores enablement, presentation order,
and an optional canonical `activeVersion` pin. New IDs are disabled by default
and appended in ordinal ID order. A schema-version-1 state document remains
readable: it has no pin, so discovery initially uses the greatest installed
version, and the next successful state mutation atomically writes schema 2.
State updates serialize through an in-process lock and a bounded cross-process
catalog lock, flush a uniquely named sibling temporary file, and atomically
replace the state file. Package contents remain immutable.

Legacy schema 1:

```json
{
  "version": 1,
  "widgets": [
    { "id": "dev.example.clock", "enabled": false, "order": 0 }
  ]
}
```

Current schema 2 with an exact pin:

```json
{
  "version": 2,
  "widgets": [
    {
      "id": "dev.example.clock",
      "enabled": false,
      "order": 0,
      "activeVersion": "1.1.0"
    }
  ]
}
```

These files are strict host state, not a user-editing API. Use the CLI or
`WidgetCatalog` methods so validation, locking, and atomic replacement remain
in force.

A pin is authoritative. If its exact immutable version directory is absent,
discovery reports `active_version_missing`; malformed installed content fails
with its structural catalog diagnostic. Neither case falls forward or backward
to different code. Reinstall the exact pinned version first; after discovery
succeeds, another installed version can be selected explicitly. Version
selection is accepted only while the widget is disabled and does not enable it.

## API sketch

```csharp
var catalog = new WidgetCatalog(userCatalogRoot);
var installer = catalog.CreateInstaller();

var inspection = await installer.ValidateAsync("Clock.gbarwidget");
var installed = await catalog.InstallAsync("Clock.gbarwidget");

await catalog.SetActiveVersionAsync(installed.Id, new Version(1, 0, 0));
await catalog.SetOrderAsync(["dev.example.clock", "dev.example.audio"]);
await catalog.SetEnabledAsync(installed.Id, enabled: true);

WidgetCatalogSnapshot snapshot = await catalog.DiscoverAsync();
```

`SetOrderAsync` moves the supplied installed IDs to the front and preserves
the relative order and active-version pins of remaining widgets.
Only `WidgetCatalog.InstallAsync` publishes package bytes. The lower-level
installer is a validation surface, preventing library consumers from bypassing
the disabled-update and reviewed-version latch.

## Live bridge consumption

The packaged bridge watches only the trusted `widget-catalog.json`,
`catalog-state.json`, and the installed `packages` subtree. Staging, lock, and
atomic temporary files are ignored. File-system notifications enter a
capacity-one coalescing channel, wait for a 175 ms write burst to settle, and
then trigger a complete bounded discovery/validation. The watcher is a change
hint, never catalog authority.

A semantically different complete catalog publishes a monotonically increasing
revision for the lifetime of that bridge process. Invalid trusted JSON or
invalid installed catalog/state retains the complete last-good catalog and
revision. Invalid individual styles or unsupported capabilities omit only that
package with bounded diagnostics. Reload and list never start workers.
Watcher startup schedules one catch-up reload after both watchers are active,
closing the load-to-watch mutation window.

Descriptor, style, quick-action, or order-only changes preserve a compatible
running worker while atomically replacing its validated presentation and
quick-action authority. A fixed worker identity/process policy change—package,
publisher, instance, executable, arguments, declared capabilities, or memory
ceiling—retires the old client. Its replacement starts lazily with a fresh
authenticated broker session.
The native host coalesces revision events, atomically reloads descriptors,
invalidates affected presentation caches, clears runtime focus/lifecycle when
required, and safely leaves a disabled/removed active widget.

The native client retains an announced revision as in-flight until an atomic
descriptor list parses and reconciles successfully. A transient failure retries
the same genuine change event at bounded 250, 500, and 1000 ms delays. Hiding
the overlay cancels and abandons the sequence, after which the same revision can
be announced again and a later open/list can catch up. An ordinary open-time
failure does not start a retry loop. A new bridge session resets revision
tracking, and stale lists cannot replace newer state. Bridge-side revision
numbering also resets when the bridge process restarts. The current watcher and
semantic comparison do not provide package signing or code-integrity proof.

## Remote acquisition

The `gbar install` command can acquire an exact package from an absolute HTTPS
URL or a deterministic GitHub Release shorthand before calling this same
installer. The downloader adds transport, redirect, compressed-size, timeout,
temporary-file, and required remote SHA-256 controls; it does not weaken or replace
any archive rule on this page. See [publishing and
installation](publishing-and-installation.md#remote-acquisition-safety-boundary)
for the command grammar and limits.

CLI installation passes a pre-publish policy callback to the stream overload
of the package installer. The installer validates the package identity from
that stream, runs the enabled-ID policy against the validated identity, and
only then extracts and atomically publishes those same package bytes. Local
installs keep one read-only file stream open across optional hashing,
validation, policy, and extraction; remote installs use the downloader's
locked, digest-verified temporary-file stream. A rejected policy never
publishes the staged version.

## Version selection and rollback

The CLI exposes the same catalog contract without deleting package bytes:

```powershell
gbar version list dev.example.clock
gbar disable dev.example.clock
gbar version select dev.example.clock 1.1.0
gbar version rollback dev.example.clock
gbar version rollback dev.example.clock --to 1.0.0
```

`list` reports every installed path and marks the active version. `select`
accepts any installed canonical dotted numeric version. `rollback` without
`--to` chooses the greatest installed version strictly older than the active
one; an explicit target must also be installed and older. Selection and
rollback require a disabled widget, preserve that disabled state, and require a
separate review and `gbar enable` action afterward. There is no history stack:
use `version select` to move forward to a newer installed version.

## Deliberately deferred

- Publisher signature and certificate-chain verification
- Online catalog metadata, release discovery, automatic updates, and revocation
- Version removal and garbage collection of old versions (Settings and CLI
  exact-version selection/rollback are implemented)
- A graphical/file-picker installer (controller review/enablement exists after
  CLI installation)

Until signing is implemented, successful structural validation proves package
shape and containment—not publisher authenticity or code safety. Executable
widgets must still run through the isolated worker and capability model.
