using System.Runtime.InteropServices;

namespace WidgetRail.WidgetRuntime;

internal static class BundledDotNetRuntime
{
    internal static IEnumerable<string> ReadOnlyRoots() => Resolve(
        AppContext.BaseDirectory, RuntimeEnvironment.GetRuntimeDirectory()) is { } root ? [root] : [];

    internal static string? Resolve(string applicationDirectory, string runtimeDirectory)
    {
        // Trust the runtime actually hosting this process, not an arbitrary
        // DOTNET_ROOT inherited from the environment. Never mutate global .NET ACLs.
        var expected = Path.GetFullPath(Path.Combine(applicationDirectory, "..", "..", "dotnet"));
        var runtime = Path.GetFullPath(runtimeDirectory);
        var shared = Path.Combine(expected, "shared", "Microsoft.NETCore.App") + Path.DirectorySeparatorChar;
        return runtime.StartsWith(shared, StringComparison.OrdinalIgnoreCase) ? expected : null;
    }
}
