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

The sample has no permissions, custom executable worker, native provider, or
host-only escape hatch. Its manifest selects `dotnet-worker`, so an installed
package is loaded by the host's generic Community AppContainer worker. The
GBSS uses semantic `gbar-*` hooks plus local `gallery-*` classes and no fixed
pixel window assumptions. Copy the relevant method and its related rules rather
than copying the entire gallery into a production widget.

## Build, test, and preview

From the repository root:

```powershell
dotnet build .\samples\SdkGalleryWidget\SdkGalleryWidget.csproj -c Release
dotnet run --project .\tests\SdkGalleryWidget.Tests\SdkGalleryWidget.Tests.csproj -c Release

dotnet run --project .\tools\GbarCli\GbarCli.csproj -c Release -- render `
  .\samples\SdkGalleryWidget\bin\Release\net8.0\SdkGalleryWidget.dll `
  --type GameBarAlternative.Samples.SdkGalleryWidget.SdkGalleryWidget `
  --instance sample.sdk-gallery
```

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
- Stable IDs survive state updates, so toggling, selecting, scrubbing, and toast
  insertion do not discard unrelated focus.
- Toast is presentational and adds no focus stop. The widget removes it through
  lifecycle-aware local state and clears it when the widget becomes hidden.
- The responsive grid derives columns from available logical-DIP width. The
  host may clamp every surface hint for work area, DPI, or text scale.

See the [widget authoring guide](../../docs/widget-authoring-guide.md),
[declarative UI reference](../../docs/declarative-ui.md), and
[controller component guide](../../docs/controller-ui-components.md) for the
full contracts and design rationale.
