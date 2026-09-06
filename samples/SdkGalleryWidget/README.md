# SDK Gallery community addon

This capability-free reference widget demonstrates the public controller-first
`WidgetSdk` exactly as an independent addon can use it. It is intentionally not
part of the built-in tray: install it only when developing or reviewing UI.

The five pages cover:

- icon buttons, cards, section headers, status badges, alerts, and empty states;
- switches, segmented tabs, `SettingsRow`, `Picker`, nested-B `ActionSheet`, and
  a controller-native `Scrubber`;
- responsive `Tile` action surfaces for media and application content;
- a visible embedded WebP and a portrait `PosterTile`, each with its own
  focus-associated detail presentation;
- deterministic package-local `BackgroundSurface` artwork with Cover, Contain,
  Fill, root/nested focus ownership, retained focus artwork, and missing-art
  fallback; and
- presentational, nonfocusable `CodeText`, `LoadingIndicator`, and
  non-focus-stealing `Toast` feedback. `CodeText` displays bounded monospace
  content; it does not imply clipboard behavior.

The sample is also the production-style reference for the public responsive
navigation and stable-ID coordination APIs. `UI.NavigationShell` renders one
five-destination model as compact tabs or an expanded rail around one shared
page subtree. Compact and rail controls receive distinct stable element IDs but
share one protocol-v13 focus-persistence identity per logical destination. The
host uses only that explicit identity to preserve focus across responsive
presentation changes; action IDs remain routing intent and may be shared. The
built-in theme owns the standard shell dimensions and focus/selected/pressed
treatment; the sample does not rebuild those rules in local WRSS.

The Backgrounds page keeps three visually distinct source PNGs under `assets/`,
embeds them in `SdkGalleryWidget.dll` with stable manifest-resource names, and
resolves only their opaque handles through `OnResolveArtworkAsync`. The archive
does not duplicate them as loose files, and resolution is independent of the
shared worker process base and working directory. They are sealed package
resources—not provider URLs, user paths, cache entries, or network
dependencies. The root owns the default Cover image and
focused-descendant replacement; nested surfaces separately demonstrate
all three fit policies: Cover fills and may crop, Contain preserves the whole
image with possible unused space, and Fill stretches to the authored bounds.
All three use the same 1536 by 1024 artwork in identical fixed 150-DIP-high,
bordered regions so their different treatment is directly comparable. The
replacement row is one remembered-child focus group, and shell entry targets
that group rather than bypassing it. Focusing the
unadorned **Retain artwork** button deliberately keeps the last accepted root
image. An unknown handle demonstrates that the same semantic foreground remains
usable when artwork cannot resolve.

The Tiles page embeds a visible 512 by 512 WebP resource inside the sample
assembly and keeps it independent of process working directories, just like the
PNG fixtures. WebP and PosterTile publish distinct `PresentOnFocus` fragments.
The Controls page applies Compact, Comfortable, and Spacious classes to a
two-row repeated-content preview, with explicit current gap and padding values,
without changing the Select focus identity. Overview includes
presentational controller hints for navigation, A Select, B Back, and
right-stick scrolling. Top-level pages are explicit view state under one stable
root input scope, so the same header element remains valid while its page is
selected. `WidgetNavigator` owns only nested Picker and ActionSheet routes,
their exact-scope B action, return focus, and cancellation.

`WidgetIds.Scope("gallery")` builds the validated navigation ID. One explicit
`GalleryPage` value selects the shared-shell content without replacing its root
scope. One `WidgetNavigator<GalleryModal>` owns nested Picker/ActionSheet routes,
exact-scope B, remembered return focus, and route-lifetime cancellation. The
focused suite proves one shared content subtree, distinct compact/expanded
controls, stable root scope across page selection, selected-state accessibility,
authoring bounds, and that opening or leaving a nested route cancels its token
before the replacement view is published.

The sample has no permissions, custom executable worker, native provider, or
host-only escape hatch. Its manifest selects `dotnet-worker`, so an installed
package is loaded by the host's generic Community AppContainer worker. The
WRSS uses semantic `wrail-*` hooks plus local `gallery-*` classes and no fixed
pixel window assumptions. Copy the relevant method and its related rules rather
than copying the entire gallery into a production widget.

## Build, test, and inspect a snapshot

From the repository root:

```powershell
dotnet build .\samples\SdkGalleryWidget\SdkGalleryWidget.csproj -c Release
dotnet run --project .\tests\SdkGalleryWidget.Tests\SdkGalleryWidget.Tests.csproj -c Release
```

The focused suite constructs and validates the sample's semantic snapshots in
an author-controlled test process. `wrail render <snapshot.json>` can inspect a
persisted data-only fixture but never loads the sample DLL; use `wrail dev` for
executable AppContainer integration. The separate `wrail preview` manifest
workflow currently validates and lists scenario declarations without loading
their provider assembly. Selected scenario execution fails closed until an
AppContainer preview worker exists. See the
[widget authoring guide](../../docs/widget-authoring-guide.md#validate-list-scenarios-render-replay-and-test).

Build a deterministic `.wrwidget` with the same public CLI available to
community authors:

```powershell
.\samples\SdkGalleryWidget\Build-CommunityPackage.ps1 -Configuration Release
```

Install it into the current user's Community catalog for local testing:

```powershell
.\samples\SdkGalleryWidget\Build-CommunityPackage.ps1 -Configuration Release -Install
```

Installed versions are immutable. Bump `manifest.json` before replacing a
version, or pass `-Catalog <directory>` to test against an isolated catalog.

## Input and state rules demonstrated

- D-pad, left stick, keyboard arrows, pointer, and `A`/Enter remain host-routed.
- `X` is a dashboard quick action published by this widget; no shell mapping is
  assumed.
- Picker and ActionSheet publish their own active input scope. `B` closes that
  nested scope; at the gallery root, `B` remains available to the shell's normal
  navigation stack.
- Every open-widget action resolved by the standard SDK router carries the
  active input-scope ID. The gallery's navigator accepts nested B only from the
  exact current scope, so a stale action from a prior modal fails closed.
- Stable IDs survive state updates, so toggling, selecting, scrubbing, and toast
  insertion do not discard unrelated focus.
- Toast is presentational and adds no focus stop. The widget removes it through
  lifecycle-aware local state and clears it when the widget becomes hidden.
- The responsive grid derives columns from available logical-DIP width. The
  host may clamp every surface hint for work area, DPI, or text scale.

The focused Gallery suite covers nine tests, including complete page
coverage, control state, route-owned nested scopes, non-focus-stealing Toast,
sealed provider-neutral artwork, root/nested background ownership, all current
fit modes, missing-art fallback, remembered focus groups, visible density,
header-focus stability, generic package isolation, and responsive/theme-safe
WRSS.

See the [widget authoring guide](../../docs/widget-authoring-guide.md),
[declarative UI reference](../../docs/declarative-ui.md), and
[controller component guide](../../docs/controller-ui-components.md) for the
full contracts and design rationale.
