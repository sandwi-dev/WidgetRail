# gbar developer CLI

For an end-to-end walkthrough, see the repository
[widget quickstart](../../docs/widget-quickstart.md). For package and trust
limitations, see [publishing and installation](../../docs/publishing-and-installation.md).
Global-theme authors should use [theme packaging and
distribution](../../docs/theme-packaging.md).
Launcher Experience authors should use the dedicated
[data-only pack format](../../docs/launcher-experience-packs.md).

The prototype CLI makes the controller-widget development loop usable before
the graphical simulator exists. It has no third-party runtime dependencies.
The complete build output is one portable local artifact: copy `gbar.exe`, its
sibling runtime/assembly files, and `templates\ControllerWidget` together. A
copied output can scaffold and package an external widget without this checkout;
copying only the executable is unsupported.

```text
gbar new widget VolumeControl --id dev.example.volume-control --template basic
dotnet build .\\VolumeControl\\VolumeControl.csproj -c Release
dotnet run --project .\\VolumeControl\\tests\\VolumeControl.Tests.csproj -c Release
gbar validate .\\VolumeControl
gbar dev .\\VolumeControl
gbar preview .\\VolumeControl
gbar preview .\\VolumeControl --scenario ready --output .\\VolumeControl\\fixtures\\ready.scenario.json
gbar render .\\VolumeControl\\fixtures\\ready.snapshot.json --output snapshot.json
gbar replay .\\VolumeControl\\fixtures\\ready.snapshot.json .\\VolumeControl\\replays\\smoke.json
gbar pack .\\VolumeControl --configuration Release --output .\\VolumeControl-1.0.0.gbarwidget
gbar install .\\VolumeControl-1.0.0.gbarwidget
gbar install github:example/widgets@v1.0.0/volume-control.gbarwidget --sha256 <64-hex-digest>
gbar list
gbar disable dev.example.volume-control
gbar version list dev.example.volume-control
gbar version select dev.example.volume-control 1.0.0
gbar version rollback dev.example.volume-control
gbar enable dev.example.volume-control
gbar disable dev.example.volume-control
gbar uninstall dev.example.volume-control

gbar authority-recovery list
gbar authority-recovery retry <confirmation-token>

gbar theme new "Ocean Night" --id dev.example.ocean-night --publisher dev.example
gbar theme validate .\OceanNight
gbar theme preview .\OceanNight
gbar theme pack .\OceanNight --output .\dev.example.ocean-night-1.0.0.gbartheme
gbar theme inspect .\dev.example.ocean-night-1.0.0.gbartheme
gbar theme install .\dev.example.ocean-night-1.0.0.gbartheme
gbar theme list

gbar launcher-theme new "Deep Space" --id dev.example.deep-space --publisher dev.example
gbar launcher-theme validate .\DeepSpace
gbar launcher-theme preview .\DeepSpace --output .\deep-space.preview.json
gbar launcher-theme pack .\DeepSpace --output .\dev.example.deep-space-1.0.0.gbarlauncher
gbar launcher-theme inspect .\dev.example.deep-space-1.0.0.gbarlauncher
gbar launcher-theme install .\dev.example.deep-space-1.0.0.gbarlauncher
gbar launcher-theme list
gbar launcher-theme remove dev.example.deep-space 1.0.0
```

## Commands

- `new widget` selects the closed version-2 `basic`, `data`, `media`, or
  `multipage` controller-first C# template inventory and a
  matching `GameBarAlternative.WidgetSdk` package in `.gbar/packages`. Its
  `template.json` is a closed inventory of bounded text templates and
  byte-preserved binary assets; undeclared, missing, duplicate, traversing,
  reparse, oversized, or unsupported inputs are rejected with the affected
  file or manifest rule. Generation occurs in a private sibling staging
  directory, validates the complete scaffold, and publishes it with one
  directory rename. A failed command removes its staging directory and never
  creates, deletes, or overwrites the requested output path. Consequently the
  output path must not already exist, even as an empty directory. Its
  generated `NuGet.Config` clears external feeds and resolves that dependency
  only from the relative project-local feed, so the scaffold builds offline in
  a clean directory without a platform checkout, machine-wide package-cache
  dependency, or machine-specific project reference. The external-consumer
  contract verifies this with a fresh temporary `NUGET_PACKAGES` root and the
  generated cleared `NuGet.Config`. Each sibling `MSTest.Sdk` 4.3.2 project
  executes the same semantic scenario declared for isolated `gbar preview`; no
  repository project reference or credential is generated.
  `eng/WidgetSdkRelease.props` supplies the shared pre-release version and
  supported template version stamped into the CLI and SDK. The generated
  package adds a deterministic content suffix and the project references that
  exact package version; mismatched CLI/SDK/template release units fail before
  publication. The local SDK package contains only `WidgetSdk.dll` and
  `WidgetProtocol.dll`; CLI, broker, runtime, catalog, and Settings assemblies
  remain tool implementation and are not bundled into the generated widget.
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
- `pack` accepts a source widget directory or `.csproj`, or an already-staged
  package directory. Source mode validates manifest and GBSS, runs a bounded
  isolated build with private intermediates, omits compiler symbols, stages the
  declared payload, and then applies the normal packer. Directory mode remains
  the low-level contract and includes its complete bounded tree. Both modes
  reject reparse points, path escapes, ambiguous Windows names, case
  collisions, and oversized trees, then write an ordinal, fixed-metadata,
  deterministic `.gbarwidget` ZIP. Use `--configuration` and the bounded
  `--build-timeout-seconds` only for source mode.
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
- `authority-recovery list` is a local host-control view of interrupted
  AppContainer content-authority transactions. It prints only a bounded
  confirmation token, validated AppContainer profile name, target count, and
  current/legacy format. It never prints journal paths, content paths, file
  identities, security descriptors, or nested exception details.
- `authority-recovery retry <confirmation-token>` re-reads and compares the
  exact pending transaction before restoring and verifying every recorded DACL.
  Obtain the token from a fresh `authority-recovery list`. Stale or malformed
  tokens fail closed. The command has no journal-root option, raw clear, force,
  path argument, or way to supply replacement authority data.
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
- `theme remove <exact-id> <exact-version>` uses the same exact-version mutation
  policy as Settings. It removes only an inactive user-installed version after
  revalidation under the catalog lock. Built-in and selected versions are
  protected; sibling versions and the appearance record are not rewritten.

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

### Launcher Experience authoring and distribution

`gbar launcher-theme` is the data-only Game Launcher presentation workflow. It
uses `LauncherExperienceValidator` for source directories and materialized
archives, so commands do not carry a second manifest, recipe, GBSS, asset, or
digest schema.

- `launcher-theme new` atomically publishes a minimal valid project with strict
  `launcher.json`, compact/standard/wide recipe branches, launcher-scoped GBSS,
  and a sealed static preview asset. The requested output must not exist.
- `validate` accepts a directory or `.gbarlauncher`; `pack` writes a
  byte-reproducible ZIP with ordinal entries and fixed metadata; and `inspect`
  reports identity, publisher claim, version, preset, sizes, content digest,
  and archive digest without executing anything.
- `preview` writes a deterministic schema-version-1 offscreen fixture document.
  It covers all four presets across compact, standard, and wide surfaces for
  empty, 20-game, 2,000-game, offline, long-title, missing-art,
  active-operation, 150%-scale, reduced-motion, reduced-transparency, and
  high-contrast states. The 2,000-game case retains only a bounded 64-row
  semantic window. The document contains no actions, SavedIds, provider
  bindings, paths, or game/content authority.
- `install`, `list`, and `remove` use the production catalog under
  `%LOCALAPPDATA%\GameBarAlternative\launcher-experiences`, or an isolated
  `--catalog <root>`. Installed `<id>/<version>` content is immutable; removal
  requires an exact canonical ID/version and cannot remove built-in recovery
  presets.

The archive admits only strict JSON, launcher-scoped GBSS, and bounded static
PNG/JPEG/WebP. URLs, HTML/JavaScript, executables, links/reparse points, path
escape/collisions, animated or multi-frame images, oversized content,
pack-authored actions/provider bindings, and inaccessible recipe branches fail
closed. Installation grants presentation data only. Preview is deterministic
authoring evidence, not an ordinary-overlay preview: production overlay
adoption remains owned by the private native Launcher Experience hook.

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
`gbar preview <directory> --scenario <name>` loads one declared public static
factory only in a capability-free AppContainer/Job worker, drives the normal
Visible, Interactive, and Background lifecycle, validates two deterministic
snapshots, and writes a bounded versioned semantic result. The CLI process never
loads the scenario assembly. The exact build-output tree is pinned for the
worker session; neighboring files are not granted. This is credential-free
semantic preview, not native pixels, controller replay, or a general desktop
sandbox. Use only author-controlled scenario code.

Exit code 0 means success, 1 means validation/runtime failure, and 2 means the
command was used incorrectly.
