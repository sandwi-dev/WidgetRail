# Launcher Experience Pack format

Launcher Experience Packs are launcher-only, presentation-only data packages.
They are separate from global `.gbartheme` packages and never change the shell,
Settings, or another widget. The production schema/catalog validates expanded
package directories and deterministic `.gbarlauncher` archives, and the native
host maps validated recipes to host-owned semantic slots. The CLI owns the
complete local authoring/package/catalog workflow. Ordinary-overlay adoption of
an authored pack remains a separate private native-host integration; the data-
only preview does not claim that production path.

## Authoring workflow

```powershell
gbar launcher-theme new "Deep Space" `
  --id dev.example.deep-space `
  --publisher dev.example `
  --preset hero-rail
gbar launcher-theme validate .\DeepSpace
gbar launcher-theme preview .\DeepSpace --output .\deep-space.preview.json
gbar launcher-theme pack .\DeepSpace `
  --output .\dev.example.deep-space-1.0.0.gbarlauncher
gbar launcher-theme inspect .\dev.example.deep-space-1.0.0.gbarlauncher
gbar launcher-theme install .\dev.example.deep-space-1.0.0.gbarlauncher
gbar launcher-theme list
gbar launcher-theme remove dev.example.deep-space 1.0.0
```

Every command uses the same production directory validator. Archive inspection
materializes captured bounded bytes into a private directory and reuses that
validator rather than approximating the manifest, recipe, GBSS, image, or
digest contract. `new` and preview output publish by one rename and never
overwrite an existing path. Packing sorts entries ordinally and fixes ZIP
timestamps and metadata, so identical source bytes produce identical archives
and SHA-256 digests. Installed ID/version directories are immutable.

Preview emits a bounded deterministic offscreen fixture document, not widget
code or a browser page. It enumerates Hero Rail, Cover Wall, Carousel, and
Compact Grid over compact, standard, and wide surfaces for empty, 20-game,
2,000-game, offline, long-title, missing-art, active-operation, 150%-scale,
reduced-motion, reduced-transparency, and high-contrast states. The large case
records only a 64-row retained window. It carries semantic slot names and
presentation state, never actions, SavedIds, provider bindings, paths, or game
authority. The native offscreen fixture verifies the existing rendering
contract independently; the ordinary overlay does not apply authored packs
until its private adoption hook is accepted.

## Package boundary

An expanded package uses an immutable lowercase ID and canonical dotted version
directory and contains at most 64 files, 64 subdirectories, and 32 MiB. Each
file is at most 16 MiB;
static PNG, JPEG, and WebP dimensions are at most 4096 by 4096. Paths are
normalized, package-relative, case-exact, at most 240 characters, and contain no
reparse point. Executables, libraries, scripts, HTML, archives, remote content,
and unknown file types are rejected. Catalog discovery admits at most 128
installed ID/version directories and stops at the first excess entry before
sorting or validating package content.

The initial shape is:

```text
launcher.json
layouts/launcher-layout.json  # optional custom recipe
styles/launcher.gbss
assets/preview.png
assets/background.png         # optional sealed presentation asset
```

`launcher.json` uses schema version 1 and rejects duplicate or unknown fields:

```json
{
  "schemaVersion": 1,
  "id": "dev.example.deep-space",
  "publisher": "dev.example",
  "name": "Deep Space",
  "version": "1.0.0",
  "layoutPreset": "hero-rail",
  "compositionFile": "layouts/launcher-layout.json",
  "styleFile": "styles/launcher.gbss",
  "previewFile": "assets/preview.png",
  "parameters": {
    "backgroundMode": "selected-game-artwork",
    "accent": "#8f80ff",
    "tileSize": "large",
    "metadataDensity": "standard",
    "motionIntensity": "reduced",
    "showSystemStatus": true,
    "focusEffect": "lift"
  }
}
```

`layoutPreset` is one of `hero-rail`, `cover-wall`, `carousel`, or
`compact-grid`. Omitting `compositionFile` selects that built-in recipe. Every
parameter has a host-defined closed value set; unknown parameter names and
values fail validation independently of Game Launcher state.

## Responsive recipe

A custom recipe contains one or more `compact`, `standard`, and `wide` branches.
An absent branch uses the selected built-in recovery structure. A branch has one
`root` and uses only `region`, bounded `grid`, `stack`, `overlay`, and `inset`
nodes. Coordinates are normalized to the parent region; alignment is `start`,
`center`, `end`, or `stretch`. Details and controller hints may select a closed
`solid` or `glass` surface role; text/status slots may select `compact`,
`standard`, or `expanded` density. Grid rows and columns are each limited to 1–12,
nesting to 16 levels, a branch to 128 nodes, and a node to 32 children.

The only semantic slots are:

- `hero-background`
- `game-rail`
- `details-panel`
- `collection-tabs`
- `source-status`
- `operation-status`
- `system-status`
- `controller-hints`

Each slot appears at most once per branch. `game-rail`, `details-panel`,
`source-status`, and `controller-hints` are required in an authored branch;
operation and attribution content remains host-injected when applicable. The
validator rejects out-of-bounds regions, interactive-slot overlap, clipped game
focus extents, and a controller-hint region too small to expose Back.

Example bottom-rail root:

```json
{
  "schemaVersion": 1,
  "branches": {
    "wide": {
      "root": {
        "type": "overlay",
        "children": [
          { "type": "region", "slot": "hero-background", "region": { "x": 0, "y": 0, "width": 1, "height": 1 } },
          { "type": "region", "slot": "details-panel", "surface": "glass", "region": { "x": 0.08, "y": 0.08, "width": 0.5, "height": 0.4 } },
          { "type": "region", "slot": "source-status", "region": { "x": 0.68, "y": 0.08, "width": 0.24, "height": 0.1 } },
          { "type": "region", "slot": "game-rail", "orientation": "horizontal", "region": { "x": 0.08, "y": 0.62, "width": 0.84, "height": 0.24 } },
          { "type": "region", "slot": "controller-hints", "region": { "x": 0.52, "y": 0.91, "width": 0.4, "height": 0.06 } }
        ]
      }
    }
  }
}
```

## Authority and recovery

Recipes never contain widget content, game IDs, SavedIds, actions, provider
bindings, expressions, URLs, scripts, shaders, or custom elements. GBSS can
target only launcher semantic roles and cannot use ID or cross-widget selectors.
The catalog is a distinct `LauncherExperienceCatalog`; it does not register with
or alter global `ThemeCatalog` behavior.

Every accepted package receives a deterministic SHA-256 content digest over
case-exact sorted paths and bytes. Discovery requires the manifest ID/version to
match its catalog directories. Invalid, deleted, or incompatible selections can
resolve to one of four code-owned recovery descriptors (`hero-rail`,
`cover-wall`, `carousel`, or `compact-grid`) without trusting rejected package
content. Packs do not receive action or provider authority through recovery.

The default installed catalog is
`%LOCALAPPDATA%\GameBarAlternative\launcher-experiences`; `--catalog <root>`
creates isolated author/test state. `list` includes the four code-owned recovery
presets and installed versions. `remove` accepts only an exact canonical
package ID/version, revalidates it under the catalog mutation lock, and refuses
to remove a built-in. There is no URL install, automatic update, signing,
gallery, action binding, or provider/content capability in this workflow.

## Native host contract

The host resolves each recipe against the live work area and chooses its
compact, standard, or wide branch before paint. The same resolved slot bounds
drive pointer targets, controller and keyboard focus, accessibility bounds, and
canonical UIA order. Paint z-order cannot reorder semantics. A vertical or
horizontal game-rail declaration changes only host layout orientation; stable
game identities and action routes remain host-owned.

The four built-in recovery presets and the reference left-rail/glass recipe are
covered across compact, 720p, 1080p, taskbar-reserved, and 150%-scale profiles.
An incompatible recipe is replaced atomically by the matching built-in preset;
rejected content never supplies fallback actions or semantics.

The native `LauncherExperiencePresentation` owner applies only launcher-slot
pack and user overrides after the already-computed global appearance. Choosing
`Use global appearance` omits both launcher layers. Reduced motion,
transparency, and high contrast are applied last and can remove blur,
translation, scale, background art, and transition duration.

Static package artwork crosses the catalog/media boundary as immutable encoded
bytes plus an opaque asset and revision identity—never as a path, URL, or
worker value. The host bounds encoded bytes and decoded pixels, validates the
declared PNG/JPEG/WebP container, rejects multi-frame content, and publishes a
new revision only after successful decode. Selected-game artwork must match the
current artwork revision. A failed decode retains the previous professional
background or the code-owned fallback; three failures disable only that pack
revision and select the matching built-in recovery experience. The documented
safe-start route always bypasses the selected pack for that activation.
