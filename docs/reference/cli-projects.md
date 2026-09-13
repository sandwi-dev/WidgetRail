# Create a project with the CLI

This advanced example is the generated-project workflow exercised by the CLI
tests. New authors can use the shorter [quickstart](../developers/widget-quickstart.md).

Set `$wrail` to the installed `wrail.cmd` or the source-built CLI executable.
Use a directory you can dedicate to the example.

## Generate, build, and run the test

<!-- canonical-author-journey:create-build-test -->
```powershell
& $wrail new widget VolumeControl `
  --output .\scratch\VolumeControl `
  --id dev.example.volume-control `
  --publisher dev.example `
  --template basic

dotnet build .\scratch\VolumeControl\VolumeControl.csproj -c Release
dotnet run --project .\scratch\VolumeControl\tests\VolumeControl.Tests.csproj `
  -c Release
& $wrail preview .\scratch\VolumeControl --scenario ready `
  --output .\scratch\VolumeControl\fixtures\ready.scenario.json
(Get-Content .\scratch\VolumeControl\fixtures\ready.scenario.json | ConvertFrom-Json).snapshot |
  ConvertTo-Json -Depth 100 |
  Set-Content .\scratch\VolumeControl\fixtures\ready.snapshot.json
```

The project gets a matching SDK package in `.widgetrail\packages`, a local
`NuGet.Config`, styles, and generated tests. Keep that local feed with the project.
The commands above do not need the WidgetRail source tree after generation.

The `ready` scenario's snapshot is written separately so it can be used by the
[render and replay examples](cli-scenarios.md).

## Generated widget source

This is the exact `src/VolumeControl.cs` produced by the basic template. It shows
the view, action handler, activation hook, and test scenario together.

<!-- canonical-author-journey:widget-source -->
```csharp
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace dev.example.VolumeControl;

public sealed class VolumeControl : Widget
{
    private int _level = 2;
    private bool _active;

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Text("VolumeControl", "title"),
            UI.Text($"{(_active ? "Active" : "Paused")} · Level {_level}", "status"),
            UI.Button("Raise", "primary", "primary")
                .Shortcut(ControllerButton.RightBumper)),
        InitialFocusId: "primary");

    public override ValueTask OnActionAsync(WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (action.ActionId == "primary")
        {
            _level = Math.Min(4, _level + 1);
            Invalidate();
        }
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        _active = true;
        Invalidate();
        return ValueTask.CompletedTask;
    }
}

public static class Scenarios
{
    public const string ExpectedText = "Active · Level 3";
    public static WidgetScenarioDefinition Ready() => new(
        new VolumeControl(), new WidgetTestHostServicesBuilder().Build());
}
```

The value is simulated. Raising it does not control Windows audio. To add a
real integration, declare a supported [capability](capabilities.md) and use its
typed host service.

## Validate before packaging

<!-- canonical-author-journey:validate -->
```powershell
& $wrail validate .\scratch\VolumeControl
```

Validation checks the manifest and supported styles. The manifest's
`entrypoint.assembly` must refer to the compiled assembly inside the package.
Continue with [Package operations](cli-packages.md).
