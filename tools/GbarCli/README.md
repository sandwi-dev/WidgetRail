# gbar developer CLI

For an end-to-end walkthrough, see the repository
[widget quickstart](../../docs/widget-quickstart.md). For package and trust
limitations, see [publishing and installation](../../docs/publishing-and-installation.md).
Global-theme authors should use [theme packaging and
distribution](../../docs/theme-packaging.md).

The prototype CLI makes the controller-widget development loop usable before
the graphical simulator exists. It has no third-party runtime dependencies.

```text
gbar new widget VolumeControl --id dev.example.volume-control
gbar validate .\\VolumeControl
gbar dev .\\VolumeControl
gbar render .\\fixtures\\volume-control.snapshot.json --output snapshot.json
gbar replay snapshot.json .\\VolumeControl\\replays\\smoke.json
gbar pack .\\VolumeControl --output .\\VolumeControl-1.0.0.gbarwidget
gbar install .\\VolumeControl-1.0.0.gbarwidget
gbar install github:example/widgets@v1.0.0/volume-control.gbarwidget --sha256 <64-hex-digest>
gbar list
gbar disable dev.example.volume-control
gbar version list dev.example.volume-control
gbar version select dev.example.volume-control 1.0.0
gbar version rollback dev.example.volume-control
gbar enable dev.example.volume-control

gbar theme new "Ocean Night" --id dev.example.ocean-night --publisher dev.example
gbar theme validate .\OceanNight
gbar theme preview .\OceanNight
gbar theme pack .\OceanNight --output .\dev.example.ocean-night-1.0.0.gbartheme
gbar theme inspect .\dev.example.ocean-night-1.0.0.gbartheme
gbar theme install .\dev.example.ocean-night-1.0.0.gbartheme
gbar theme list
```

## Commands

- `new widget` instantiates the bundled controller-first C# template. It uses
  a discovered source `ProjectReference` inside this repository. Outside the
  checkout, pass `--sdk-project <path-to-WidgetSdk.csproj>`. Because no
  supported SDK package is published yet, unresolved SDK input fails before
  creating a partial scaffold; the CLI never emits a placeholder package.
- `validate` checks strict manifest JSON and every GBSS file in a widget
  directory. GBSS validation uses the shared `WidgetStyling` parser/compiler,
  enforces typed bounded properties, and blocks scripts, expressions, URLs,
  file paths, import escapes, and malformed rules. Safe clamping is reported as
  a warning; syntax, security, and type errors fail validation.
- `dev` is the local unsigned edit/build/run loop. It accepts a widget source
  directory, one `.csproj`, a prebuilt package directory, or one
  `.gbarwidget`. Source projects are validated, built in a child `dotnet`
  process with a 120-second default timeout, staged as a catalog-valid package,
  installed into a session-only catalog, and opened by the packaged overlay.
  The overlay routes the package through the same generic `WidgetWorkerHost`,
  package-specific AppContainer, capability broker, lifecycle, and renderer
  used by installed community widgets; `gbar` never loads the widget assembly.
  `--host` selects a packaged `OverlayHost.exe`, `--configuration` selects the
  project build configuration, `--build-timeout-seconds` is bounded to
  10–600 seconds, and `--debounce-ms` is bounded to 50–2000 milliseconds.
  Source projects use bounded non-recursive per-directory handles for the
  manifest, project file, C# source set, `Directory.Build.props/targets`, and
  GBSS sources; newly created source/style directories are adopted after a
  bounded safe recapture. Prebuilt package directories watch the same bounded,
  reparse-safe file tree consumed by `pack`, including payload dependencies,
  assets, and initially empty directories. Events are coalesced before one
  complete rebuild. Before replacing a last-good overlay, a hidden probe that
  owns no controller/hotkey input must connect to WidgetBridge, reconcile the
  exact installed widget instance in the unique catalog generation, transition
  that widget to `Visible`, launch its generic worker, obtain a protocol-valid
  snapshot for the same instance, and return it to `Background`. Only then may
  it atomically publish a nonce-authenticated readiness record. A valid
  permission-denied UI is still a successful snapshot; worker construction or
  rendering failure is not. The interactive host performs the same bounded
  handshake. Missing, stale, or forged records fail closed, and an active-host
  startup failure restarts the previous generation. Ctrl+C cancels the current build, verifies termination of the
  overlay/bridge/worker process tree, and retries temporary-catalog removal;
  unreclaimed processes or files are reported as cleanup failure. The workflow
  never modifies the user's installed-widget catalog.
- `render` is data-only: it validates, bounds, and prints an existing semantic
  snapshot tree and can write a canonical copy. DLL input fails closed before
  type resolution or output handling. Use `dev` when author code must execute;
  it retains the production AppContainer/worker boundary.
- `replay` walks explicit D-pad focus edges and resolves `A` actions and
  declared non-Guide shortcuts from a versioned JSON event stream.
- `pack` validates the root manifest and entrypoint, rejects reparse points,
  path escapes, ambiguous Windows names, case collisions, and oversized trees,
  then writes a deterministic `.gbarwidget` ZIP. Files use ordinal path order,
  fixed timestamps and fixed metadata, so identical content produces identical
  package bytes. The default output is `<id>-<version>.gbarwidget` beside the
  widget directory.
- `install` accepts a local `.gbarwidget`, an absolute HTTPS URL, or the
  deterministic GitHub shorthand
  `github:owner/repository@tag/asset.gbarwidget`. The shorthand maps directly
  to that named GitHub Release asset; it does not query "latest," clone a
  repository, build source, or execute downloaded code. Remote installs require
  `--sha256` followed by the expected 64-character hexadecimal digest. Local
  installs may also use the option. Every package is validated and extracted
  through the shared `WidgetCatalog` safety boundary. Installed
  `<id>/<version>` directories are immutable; installing the same version again
  is an error.
- `list`, `enable`, and `disable` inspect or update persistent catalog state.
  `--catalog <root>` overrides the default
  `%LOCALAPPDATA%\\GameBarAlternative\\widgets` location for every catalog
  command.
- `uninstall <widget-id>` requires the widget to be disabled, atomically
  retires the package ID from discovery, removes all of its immutable versions,
  and removes/reindexes its catalog-state entry. If a retiring worker still
  locks files, the command reports cleanup pending and a later install or
  uninstall retries the bounded staging cleanup. It does not enumerate or
  purge provider-owned secrets; a widget must clear known secret slots through
  its public host service before uninstall when that behavior is desired.
- `repair list` is the bounded, data-only recovery view for a catalog that
  normal validation cannot load. It reads only canonical package/version
  directory names and validated catalog state; it never parses a candidate
  manifest or loads package code. Entries are labeled `removable`,
  `selected-protected`, or `enabled-selected-protected`.
- `repair remove <widget-id> <version>` atomically retires exactly one inactive,
  non-selected canonical version, even when another selected version is enabled.
  There is no `--force` path and no recursive
  path supplied by the caller. Repeat until the reported quota is satisfied,
  then use normal `list`/Settings validation again.
- `version list <widget-id>` lists every immutable installed version and marks
  the active one. `version select <widget-id> <version>` pins any installed
  canonical dotted numeric version. `version rollback <widget-id>` selects the
  greatest installed version older than the active version; `--to <version>`
  selects a specific older version. Selection and rollback require the widget
  to be disabled and leave it disabled. Review the selected package, then run
  `enable` as a separate decision. Use `version select`, not `rollback`, to move
  forward again.

### Theme authoring and distribution

`gbar theme` is the supported global-theme workflow. It uses the production
`PlatformSettings` catalog and `WidgetStyling` compiler, so validation and
preview do not approximate GBSS with a browser or a second parser.

- `theme new` scaffolds a schema-version-2 `theme.json` and safe starter GBSS.
  A theme ID must belong to its declared publisher namespace and its version is
  canonical dotted numeric notation.
- `theme validate` validates either a source directory or `.gbartheme`. It
  follows package-relative imports, compiles typed values and variables, and
  rejects unreachable GBSS files rather than silently shipping dead content.
- `theme preview` prints deterministic computed properties for the semantic
  canvas, backdrop, panel, tray, tray-item states, text, button states, and
  progress roles. It includes the built-in platform layer. It is intentionally
  non-GUI: it is useful in terminals and CI, but does not replace physical
  resolution, contrast, text-scale, or mixed-DPI visual review.
- `theme pack` creates a deterministic `.gbartheme` ZIP with ordinal paths,
  fixed timestamps, fixed metadata, and a printed SHA-256 digest.
- `theme inspect` reports the exact identity, publisher claim, version, entry,
  expanded size, and archive SHA-256 without executing anything.
- `theme install` accepts a local package, an absolute HTTPS URL, or
  `github:owner/repository@tag/asset.gbartheme`. Remote installation requires
  `--sha256`. Installation validates through the same compiler, stages on the
  settings volume under a random directory, holds a bounded cross-process
  lock, and atomically publishes a new immutable `<id>/<version>` directory.
  Existing versions are never overwritten, and installation stops at the
  platform catalog limit of 128 user-theme versions.
- `theme list` reports built-in and installed versions, publisher claims,
  validity, and the first safe diagnostic. `--settings-root <root>` gives all
  theme catalog commands an isolated root for testing; otherwise they use
  `%LOCALAPPDATA%\GameBarAlternative`.

The package format is data-only. A `.gbartheme` contains exact-case root
`theme.json` plus UTF-8 `.gbss` files; assemblies, scripts, executables, images,
fonts, browser content, and arbitrary assets are rejected. Validation also
rejects archive traversal, explicit directory and symbolic-link entries,
reparse points in source/install paths, Unicode/path ambiguity, case
collisions, unsafe Windows names, malformed or duplicate JSON, unsupported
manifest members, invalid imports, and bounded-size/count violations. Current
limits are 65 files, 240 characters per archive path, 4 MiB per entry, 4 MiB
expanded total, and 4 MiB compressed archive size.

Schema-version-2 manifests have this strict shape:

```json
{
  "schemaVersion": 2,
  "id": "dev.example.ocean-night",
  "publisher": "dev.example",
  "name": "Ocean Night",
  "version": "1.0.0",
  "entryFile": "theme.gbss"
}
```

Publisher is a claim, not proof. A digest proves exact bytes, not publisher
identity; obtain it through an independent trusted channel. Packages are not
signed and there is no revocation service yet. Legacy schema-version-1 local
theme directories remain readable by the runtime, but `theme pack` accepts
only the publisher-bearing public schema.

Packaging and catalog commands only inspect bytes and metadata; they never load
or execute a widget assembly. Packages are not signed, and a digest proves
integrity rather than publisher identity, so compare the required remote digest
through an independent trusted channel. Newly installed widget IDs remain
disabled until explicitly enabled after review. Installed versions are
immutable and coexist under `<id>/<version>`; changing the active version never
rewrites either package. Update or rollback is a disabled-only review flow:
disable the ID, install if necessary, explicitly select or roll back, review,
and then enable it again. A local or remote update of an enabled widget is
rejected before package publication without changing its active version.
Installing a newer version does not override an existing explicit pin; the CLI
prints the still-selected version and the exact `version select` command needed
to review the new one.

The shared remote downloader permits HTTPS on port 443 only, rejects credentials,
fragments, localhost, and obvious private or link-local IP literals, and checks
every redirect against the same policy. It accepts at most five redirects and
72 MiB for widgets or 4 MiB for themes, with the selected byte limit enforced
both from `Content-Length` and while streaming. The default connect, response,
and overall limits are 10, 20, and 120 seconds. Responses must use identity
content encoding. A random temporary file remains exclusively locked through
validation and is removed on success or failure. These controls do not make an
untrusted widget safe and do not claim to prevent DNS rebinding; install only
from publishers you trust.

`gbar render` and scenario listing never load widget/provider assemblies.
`gbar dev` is the executable integration path because it retains the production
community worker boundary. Snapshot fixtures should come from author-controlled
typed-fake tests or an isolated worker capture, not by executing downloaded code
inside a test or CLI process.

Exit code 0 means success, 1 means validation/runtime failure, and 2 means the
command was used incorrectly.
