# Your first widget

Create a working C# widget, try its button, and package it for the overlay.
The basic template starts with a simulated level control. It does not change
your system volume or require an account.

## Before you start

Install WidgetRail and a .NET SDK that supports `net8.0`. Both WidgetRail editions
include the CLI and templates; Developer also includes SDK Gallery. You do not
need Visual Studio C++ tools or the host source to write this widget.

Open PowerShell in a folder where you keep your projects. Find the installed CLI:

```powershell
$appRoot = (Get-ItemProperty 'HKCU:\Software\WidgetRail\Installation').ApplicationRoot
$wrail = Join-Path $appRoot 'wrail.cmd'
& $wrail help
```

If you use a source build instead, follow [Building from source](../maintainers/building.md).
The installed CLI is not automatically added to your system PATH.

## 1. Create the project

```powershell
& $wrail new widget VolumeControl `
  --output .\VolumeControl `
  --id dev.example.volume-control `
  --publisher dev.example `
  --template basic
Set-Location .\VolumeControl
```

Choose an output folder that does not already exist. Use your own publisher and
widget IDs when you start a project you intend to share.

The generated folder includes:

| File or folder | Purpose |
|---|---|
| `src/VolumeControl.cs` | The widget's view and action handler |
| `manifest.json` | Name, version, runtime, and permissions |
| `styles/default.wrss` | Widget styles |
| `tests/` | A generated scenario test |
| `.widgetrail/packages/` | The matching local SDK package |

The local SDK package means this project does not depend on a checkout of
WidgetRail. Keep the generated `NuGet.Config` with it.

## 2. Build and preview

```powershell
dotnet build .\VolumeControl.csproj -c Release "-bl:build-$([guid]::NewGuid().ToString('N')).binlog"
& $wrail preview . --scenario ready
```

The `ready` scenario runs the widget with test host services and reports its
presentation. This is a data preview, not a screenshot or a desktop window.
You should find the level text and the Raise action in its output.

## 3. Understand the button

Open `src/VolumeControl.cs`. The view contains this button:

```csharp
UI.Button("Raise", "primary", "primary")
    .Shortcut(ControllerButton.RightBumper)
```

The first string is the visible label. The second is the action ID. The third
is the control's stable ID. The shortcut also lets RB invoke this action.

Find the `primary` branch in `OnActionAsync`. It increases the simulated level
and calls `Invalidate()`, so the host can show the updated text. Try changing
the label to **Raise level**, then build and preview again.

## 4. Try it in the overlay

```powershell
& $wrail validate .
& $wrail pack .
```

Use the package path printed by `pack`. In **Settings → Widgets**, install the
`.wrwidget` file, review its details, and enable it. Choose it from the tray.
Press A on Raise level, or use RB, and watch the level change.

For a faster iteration loop, [CLI workflows](../reference/cli-workflows.md#authoring-loop)
explain `wrail dev`, scenario tests, rendering, and input replay.

## Next steps

Read [Core concepts](concepts.md), then [State and updates](widget-model.md).
The [authoring guide](widget-authoring-guide.md) helps you choose a path from
this first button to data, navigation, styling, and media.
