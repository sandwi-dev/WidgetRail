# SDK Gallery community addon

This capability-free reference widget demonstrates the public controller-first
`WidgetSdk` exactly as an independent addon can use it. It is intentionally not
part of the built-in tray: install it only when developing or reviewing UI.

The four pages cover:

- icon buttons, cards, section headers, status badges, alerts, and empty states;
- switches, segmented tabs, `SettingsRow`, `Picker`, nested-B `ActionSheet`, and
  a controller-native `Scrubber`;
- responsive `MediaTile` and `AppTile` action surfaces; and
- `CodeText`, `LoadingIndicator`, and non-focus-stealing `Toast` feedback.

The sample is also the production-style reference for the public responsive
navigation and stable-ID coordination APIs. `UI.NavigationShell` renders one
four-destination model as compact tabs or an expanded rail around one shared
page subtree. Compact and rail controls receive distinct stable element IDs but
share one protocol-v13 focus-persistence identity per logical destination. The
host uses only that explicit identity to preserve focus across responsive
presentation changes; action IDs remain routing intent and may be shared. The
built-in theme owns the standard shell dimensions and focus/selected/pressed
treatment; the sample does not rebuild those rules in local GBSS.

`WidgetIds.Scope("gallery")` builds the validated navigation ID. One
`WidgetNavigator<GalleryRoute>` owns root destinations, nested
Picker/ActionSheet routes, stable input scopes, exact-scope B, remembered return
focus, and route-lifetime cancellation. It does not keep parallel page/modal,
focus-return, responsive-destination, or scope-string fields. The focused suite
proves one shared content subtree, distinct compact/expanded controls,
controller traversal, selected-state accessibility, authoring bounds, and that
leaving a route cancels its token before the replacement view is published.

The sample has no permissions, custom executable worker, native provider, or
host-only escape hatch. Its manifest selects `dotnet-worker`, so an installed
package is loaded by the host's generic Community AppContainer worker. The
GBSS uses semantic `gbar-*` hooks plus local `gallery-*` classes and no fixed
pixel window assumptions. Copy the relevant method and its related rules rather
than copying the entire gallery into a production widget.

## Build, test, and inspect a snapshot

From the repository root:

```powershell
dotnet build .\samples\SdkGalleryWidget\SdkGalleryWidget.csproj -c Release
dotnet run --project .\tests\SdkGalleryWidget.Tests\SdkGalleryWidget.Tests.csproj -c Release
```

The focused suite constructs and validates the sample's semantic snapshots in
an author-controlled test process. `gbar render <snapshot.json>` can inspect a
persisted data-only fixture but never loads the sample DLL; use `gbar dev` for
executable AppContainer integration. The separate `gbar preview` manifest
workflow currently validates and lists scenario declarations without loading
their provider assembly. Selected scenario execution fails closed until an
AppContainer preview worker exists. See the
[widget authoring guide](../../docs/widget-authoring-guide.md#validate-list-scenarios-render-replay-and-test).

Build a deterministic `.gbarwidget` with the same public CLI available to
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

The focused Gallery suite currently passes 6/6 tests, including complete page
coverage, control state, route-owned nested scopes, non-focus-stealing Toast,
generic package isolation, and responsive/theme-safe GBSS.

See the [widget authoring guide](../../docs/widget-authoring-guide.md),
[declarative UI reference](../../docs/declarative-ui.md), and
[controller component guide](../../docs/controller-ui-components.md) for the
full contracts and design rationale.
