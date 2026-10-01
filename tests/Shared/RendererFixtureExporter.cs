using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.Tests;

internal static class RendererFixtureExporter
{
    internal static void WriteCollectionFixture(string name, ViewSnapshot snapshot)
    {
        if (Environment.GetEnvironmentVariable("WRAIL_COLLECTION_LAYOUT_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        Write(Path.Combine(directory, name + ".renderer.json"), snapshot,
            CompileTheme(Path.Combine(AppContext.BaseDirectory, "styles"), "default.wrss"));
    }

    internal static void Write(string path, ViewSnapshot snapshot, WrssTheme theme)
    {
        using var document = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            snapshot = document.RootElement.Clone(),
            renderStyles = BridgeRenderStyleResolver.Resolve(snapshot, theme),
        }, options));
    }

    internal static WrssTheme CompileTheme(string packageRoot, string entry)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found for renderer fixture.");
        var platform = WrssPackageLoader.Load("builtin-default.wrss",
            new WrssFileSourceProvider(Path.Combine(root.FullName, "src", "PlatformSettings", "Themes")));
        var package = WrssPackageLoader.Load(entry, new WrssFileSourceProvider(packageRoot));
        var result = WrssThemeCompiler.Compile([new WrssThemeLayer(0, platform.Documents), new WrssThemeLayer(200, package.Documents)]);
        if (!result.IsValid) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        return result.Theme!;
    }
}
