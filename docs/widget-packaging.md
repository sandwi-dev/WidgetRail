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

`presentation.icon` is optional closed semantic metadata discovered from the
manifest (default `connection`). It cannot name an asset, font, SVG, or drawing
payload. YT Music uses `music` and is the first complete [Community-addon
package reference](../samples/YtMusicWidget/README.md).

The archive may contain regular files under `assets/`, but successful packing
does not make them renderable. General package-asset resolution is not wired to
the widget protocol yet. Images currently use the bounded HTTPS image node;
icons use the closed semantic glyph set.

## Archive safety rules

The prototype defaults are:

- At most 512 archive entries
- At most 1,024 distinct package-root/implicit directories needed to reach
  regular files under exact worker authority
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

After validation and extraction, installation computes a bounded SHA-256 digest
over the normalized relative path, length, and bytes of every package file, then
writes host-owned `.gbar-integrity.json`. That path is reserved and rejected if
the archive supplies it. Discovery recomputes the digest in fixed ordinal path
order and rejects missing/malformed metadata or any changed content. The
integrity file is excluded from the digest it records.

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
revision for the lifetime of that bridge process. Invalid trusted shell JSON
retains the complete last-good catalog. Invalid installed state or package
integrity publishes a trusted-only revision instead: Community registrations
are removed synchronously, their running workers are retired, and a stale ID
cannot relaunch. Invalid individual styles or unsupported capabilities omit
only that package with bounded diagnostics. Reload and list never start workers.
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

Games & Apps is packaged as the same ordinary manifest-backed shape available
to another author. Its required `system.apps.library.read.v1` and optional
`system.apps.library.launch.v1` declarations come from its package manifest;
bundling does not auto-grant either capability or give its worker paths/launch
authority. The public SDK exposes only paged opaque metadata and an
Interactive-only launch of one broker-issued ID. The trusted provider retains
and revalidates the exact Start Menu shortcut. See the [Games & Apps
reference](games-and-apps.md).

Settings permission review also treats package diagnostics as read-only. One
focusable Review row opens a bounded nested controller page for unsupported
requests and inactive saved decisions. It offers no cleanup action, shares one
16-row detail budget, and suppresses inactive classification entirely when the
catalog projection or consent document is invalid or incomplete.

## Installed-worker isolation

Every package joined from the current-user catalog is marked by trusted bridge
policy as requiring AppContainer isolation. For unsigned packages, the host
derives its authority ID from the host-verified content-tree digest, not the
manifest's self-asserted publisher label. Each distinct byte tree therefore
receives a distinct profile, consent identity, and private-secret namespace;
rollback to the exact previously verified bytes returns to that prior authority,
while changed bytes cannot silently inherit it even if package ID and version
text are reused. Future signing can replace this content identity with verified
signer authority that is stable across authenticated updates. Neither
`manifest.json`, catalog state, worker arguments, nor widget protocol messages
can select or weaken the isolation policy.

Before a package worker runs, the catalog reacquires the exact published
content-tree digest and complete relative-path/length/SHA-256 inventory. It
retains read-only handles that deny write/delete replacement for every verified
file through that worker session. Content revalidation has a five-second
cooperative deadline and fails before process creation if any byte, length,
path, or namespace entry has changed. Exact ACL application and the subsequent
worker handshake do not yet share one aggregate enforced start deadline.

The Windows runtime then opens or creates the digest-specific capability-free
profile, removes any legacy inheriting grant on the package root, and grants its
SID direct non-inheriting read/execute access only to the verified files and
their traversal directories. A dependency, native library, style, or asset
inserted after verification receives no worker authority. The profile identity
includes the verified content digest, so a later content generation cannot
inherit direct grants left on an older root. The runtime applies the content
DACLs transactionally before any worker pipe or process is
created. Under one cross-process authority lock with a five-second acquisition
limit, it captures every attempted root, directory, and file DACL, publishes and flushes a bounded pending
record in a protected host-only control-plane directory, and only then begins
mutation. Each applied DACL is verified. A reported failure restores and
verifies targets in reverse order including the failing target; a complete
restore clears the record and reports a stable retryable admission failure.

If the host terminates during mutation or a restore remains incomplete, the
write-ahead record survives. Before any later community worker starts, the next
host recovers and verifies that transaction or refuses launch with the record
still pending. Invalid, reparse-shaped, unwritable, or unflushable journal state
fails before mutation. A production AppContainer-token fixture proves the
sandboxed profile cannot read or overwrite this host journal. ACL application
is still pathname-based rather than bound to the verified object identity;
alternate inherited/group authority auditing and a user-facing privileged
repair surface remain open.

The runtime separately
grants the generic worker executable, supplies a stripped environment, and
launches at Low integrity. Token SID, integrity, and zero-capability state are
verified before resume. The Job Object
also enforces the trusted memory ceiling, one active process, kill-on-close,
die-on-unhandled-exception, and basic UI restrictions.

The content lease follows the exact process session and is reacquired after a
crash, intentional unload, or restart. Disable/removal, failed connection, and
shutdown release it only after process/pipe/Job teardown. This can temporarily
block a same-user write or uninstall while a worker is alive; that is deliberate
byte authority, not a claim that the current-user package directory is generally
OS-immutable.

The bounded maximum-inventory fixtures use 512 verified files. Current local
Clean retained Release run `20260809T152831Z-67b77c73` records 325.283 ms to
reacquire/hash/pin that inventory and 360.426 ms to apply exact AppContainer
grants and complete lazy activation. The
focused tests reject content acquisition above five seconds and the maximum-
grant fixture above ten seconds, but those separate ceilings are not one
production start-admission budget. These are one-machine regression
measurements, not cross-hardware startup targets.
Journal-enabled dirty all-lane run `20260810T002639Z-3dcb61c2` records 485.499
ms for the same 512-file exact-grant path and passes Runtime 55/55; the earlier
clean number remains the release-eligible baseline until an exact clean commit
is retained.

A focused exact-shape fixture uses the public `gbar pack`, `install`, and
`enable` workflow for 258 verified files reached through exactly 1,024 authority
directories, then renders a real first-party widget in its AppContainer. On the
current machine packing takes 376.140 ms and activation through first validated
render takes 2,528.883 ms. Clean retained selected run
`20260809T155221Z-ae6e5d8d` passed CLI 50/50, Documentation 1/1, and First-Party
Conformance 6/6 for documentation commit `b2956ab` over implementation
`4f903b0`. A paired 1,025-directory case proves `gbar pack` leaves no output and
`gbar install` publishes no package bytes. The measured edge remains under a
ten-second regression ceiling; this selected run is neither a complete
all-manifest gate nor the missing aggregate production start deadline.

The random global main and optional broker pipes allow only the desktop host
and exact AppContainer SID, carry a Low mandatory label, accept one local
client, and verify the expected worker PID before the runtime hello or broker
nonce/package/publisher/instance handshake. Any profile, ACL, token, launch,
PID, or protocol-authentication failure aborts startup; there is no trusted-
token fallback for an installed package.

This execution boundary is independent of archive validation and publisher
trust. A well-contained unsigned package is still unsigned. Public distribution
still requires signing/revocation, CPU quotas, disk/profile quotas and cleanup,
and a security audit/history surface. Trusted bundled Settings temporarily
remains Job-only for desktop-user resources; packages cannot request that
exception.

YT Music is the first Community-addon integration case. Its trusted catalog and
custom worker were removed; `Build-CommunityPackage.ps1` stages its payload and
uses the same public validate/pack/install/enable commands documented below.
The installed addon runs in the generic package AppContainer and reaches only
its separately declared/consented exact loopback port and private-secret
service. Package conformance proves the public path without a fallback; clean
packaged user testing remains open.
Its exact-port and vault declarations remain ordinary permission review items;
see [local companion HTTP and private secrets](community-companion-services.md).
Win32k system-call disable is not enabled because the tested mitigation caused
CoreCLR DLL initialization failure (`0xC0000142`); Job Object UI restrictions
remain part of the enforced boundary.

The native client retains an announced revision as in-flight until an atomic
descriptor list parses and reconciles successfully. A transient failure retries
the same genuine change event at bounded 250, 500, and 1000 ms delays. Hiding
the overlay cancels and abandons the sequence, after which the same revision can
be announced again and a later open/list can catch up. An ordinary open-time
failure does not start a retry loop. A new bridge session resets revision
tracking, and stale lists cannot replace newer state. Bridge-side revision
numbering also resets when the bridge process restarts. Host-sealed content-tree
verification plus the per-session launch lease binds the bytes exposed to a
running worker. The watcher, digest, and lease still do not prove publisher
identity or benign behavior.

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
shape and archive containment—not publisher authenticity or benign behavior.
Executable installed/community widgets still run through the mandatory
AppContainer worker and brokered-capability model.
