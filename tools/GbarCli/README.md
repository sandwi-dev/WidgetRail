# gbar developer CLI

For an end-to-end walkthrough, see the repository
[widget quickstart](../../docs/widget-quickstart.md). For package and trust
limitations, see [publishing and installation](../../docs/publishing-and-installation.md).

The prototype CLI makes the controller-widget development loop usable before
the graphical simulator exists. It has no third-party runtime dependencies.

```text
gbar new widget VolumeControl --id dev.example.volume-control
gbar validate .\\VolumeControl
gbar render .\\VolumeControl\\bin\\Debug\\net8.0\\VolumeControl.dll --type dev.example.VolumeControl.VolumeControl --output snapshot.json
gbar replay snapshot.json .\\VolumeControl\\replays\\smoke.json
gbar pack .\\VolumeControl --output .\\VolumeControl-1.0.0.gbarwidget
gbar install .\\VolumeControl-1.0.0.gbarwidget
gbar list
gbar disable dev.example.volume-control
gbar enable dev.example.volume-control
```

## Commands

- `new widget` instantiates the bundled controller-first C# template. It uses
  a source `ProjectReference` inside this repository and the future SDK
  package reference when installed elsewhere.
- `validate` checks strict manifest JSON and every GBSS file in a widget
  directory. GBSS validation uses the shared `WidgetStyling` parser/compiler,
  enforces typed bounded properties, and blocks scripts, expressions, URLs,
  file paths, import escapes, and malformed rules. Safe clamping is reported as
  a warning; syntax, security, and type errors fail validation.
- `render` validates and prints a semantic snapshot tree. Given a widget DLL
  and type, it runs `Render()` in a collectible development-only load context
  and can persist the deterministic protocol snapshot.
- `replay` walks explicit D-pad focus edges and resolves `A` actions and
  declared non-Guide shortcuts from a versioned JSON event stream.
- `pack` validates the root manifest and entrypoint, rejects reparse points,
  path escapes, ambiguous Windows names, case collisions, and oversized trees,
  then writes a deterministic `.gbarwidget` ZIP. Files use ordinal path order,
  fixed timestamps and fixed metadata, so identical content produces identical
  package bytes. The default output is `<id>-<version>.gbarwidget` beside the
  widget directory.
- `install` validates and extracts a local `.gbarwidget` through the shared
  `WidgetCatalog` safety boundary. Installed `<id>/<version>` directories are
  immutable; installing the same version again is an error.
- `list`, `enable`, and `disable` inspect or update persistent catalog state.
  `--catalog <root>` overrides the default
  `%LOCALAPPDATA%\\GameBarAlternative\\widgets` location for every catalog
  command.

Packaging and catalog commands only inspect bytes and metadata; they never load
or execute a widget assembly. Packages are not signed in this local-development
slice, so install only files you trust. URL and GitHub download support is
intentionally absent—download a release asset separately, then pass its local
path to `gbar install`.

The DLL renderer is development tooling, not the production worker host or
security boundary. Do not use it to inspect untrusted widget binaries.

Exit code 0 means success, 1 means validation/runtime failure, and 2 means the
command was used incorrectly.
