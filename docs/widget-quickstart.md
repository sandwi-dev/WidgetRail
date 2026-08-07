# Widget quickstart

Status: implemented developer workflow inside this repository

This guide creates a controller-first C# widget, validates its manifest and
GBSS, renders a protocol snapshot, and replays input without opening the native
overlay.

## Prerequisites

- Windows and PowerShell
- .NET 8 SDK
- A checkout of this repository

The SDK is not currently published as a supported public NuGet package. Run
the quickstart inside this checkout so the generated project can use the local
`WidgetSdk.csproj` reference.

## Build the developer CLI

From the repository root:

```powershell
dotnet build .\tools\GbarCli\GbarCli.csproj -c Release
$gbar = '.\tools\GbarCli\bin\Release\net8.0\gbar.exe'
& $gbar help
```

The available commands are `new`, `validate`, `render`, `replay`, `pack`,
`install`, `list`, `enable`, and `disable`. There is no URL downloader,
GitHub-specific publisher, signing command, or marketplace client.

## Create and build a widget

```powershell
& $gbar new widget VolumeControl `
  --output .\scratch\VolumeControl `
  --id dev.example.volume-control `
  --publisher dev.example

dotnet build .\scratch\VolumeControl\VolumeControl.csproj
```

The starter contains:

- a strict `manifest.json`;
- a typed `Widget` subclass;
- stable element IDs and focus neighbors;
- controller shortcuts and bounded disabled states;
- optional closed semantic button icons through `.Icon(WidgetGlyph.X)`;
- a safe `styles/default.gbss` file; and
- a deterministic controller replay.

The generated widget uses the root input scope. For a nested modal or component
surface, call `.InputScope("scope-id")` on its Stack/Row, bind focus-independent
actions with `.Shortcut(button, actionId)`, and return that ID through
`WidgetView.ActiveInputScopeId`. Keep root and nested bindings independent; the
runtime never bubbles between scopes.

Publisher names must be lowercase reverse-DNS identifiers that are also valid
C# namespaces. Widget IDs use lowercase reverse-DNS components and may also
contain `_` or `-`.

## Validate

```powershell
& $gbar validate .\scratch\VolumeControl
```

Directory validation checks the strict manifest and every `.gbss` file. You
can also validate a single `manifest.json` or `.gbss` file. Warnings such as
safe numeric clamping do not fail validation; malformed or unsafe input does.

## Render a snapshot

```powershell
& $gbar render `
  .\scratch\VolumeControl\bin\Debug\net8.0\VolumeControl.dll `
  --type dev.example.VolumeControl.VolumeControl `
  --instance development.preview `
  --output .\scratch\VolumeControl\snapshot.json
```

`gbar render` loads the DLL in a collectible development-only load context and
calls `Render()`. It is not the production worker boundary and must not be used
to inspect untrusted binaries. The command can also preview an existing
snapshot with `gbar render <snapshot.json>`.

## Replay controller input

```powershell
& $gbar replay `
  .\scratch\VolumeControl\snapshot.json `
  .\scratch\VolumeControl\replays\smoke.json
```

Replay follows declared focus edges, activates a focused button with A, and
resolves declared non-Guide shortcuts. It catches navigation/action contract
errors before host integration.

## Authoring loop

1. Keep `Render()` deterministic for current widget state.
2. Build the tree from `UI.Stack`, `UI.Row`, and leaf elements.
3. Give every node a stable ID; never derive IDs from list position.
4. Add explicit focus neighbors where spatial navigation is ambiguous.
5. For a nested surface, publish `ActiveInputScopeId` and keep
   `InitialFocusId` inside it. The host owns/restores current focus.
6. Handle semantic action IDs in `OnActionAsync`.
7. Call `Invalidate()` only when visible state changes.
8. Rebuild, validate, render, and replay.

The MVP accepts only Pressed shortcuts. A and D-pad are reserved for focused
activation and navigation. Dashboard quick actions are separate: the host owns
A, Y, D-pad/analog, and Guide, while cards may expose B, X, bumpers, triggers,
stick clicks, Menu, or View.

Continue with the [declarative UI reference](declarative-ui.md), [controller
input model](controller-input.md), and [GBSS reference](gbss.md). For sharing,
read [publishing and installation](publishing-and-installation.md).
