using WidgetRail.WidgetRuntime;

internal static class BundledRuntimeScenarios
{
    public static Task RootsAreExact()
    {
        var root = Path.Combine(Path.GetTempPath(), "WidgetRail runtime fixture");
        var app = Path.Combine(root, "runtime", "Bridge");
        var dotnet = Path.Combine(root, "dotnet");
        var runtime = Path.Combine(dotnet, "shared", "Microsoft.NETCore.App", "8.0.31");
        if (BundledDotNetRuntime.Resolve(app, runtime) != dotnet) throw new Exception("Private runtime was not admitted.");
        foreach (var other in new[] { root, dotnet + "-other", Path.Combine(root, "system-dotnet", "shared", "Microsoft.NETCore.App", "8.0.31") })
            if (BundledDotNetRuntime.Resolve(app, other) is not null) throw new Exception("Unrelated runtime authority was granted.");
        return Task.CompletedTask;
    }
}
