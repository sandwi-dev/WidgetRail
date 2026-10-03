using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.Samples.SdkGalleryWidget;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using WidgetRail.PlatformSettings;

internal static class GalleryScrollFixture
{
    internal static async Task ExportAsync(string directory, string stylePath)
    {
        Directory.CreateDirectory(directory);
        var widget = new SdkGalleryWidget();
        await widget.OnActionAsync(new("gallery.tab.text", "gallery.tab.text"));
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(Path.GetFullPath(directory)));
        var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, WrssPackageLoader.LoadFile(Path.GetFullPath(stylePath)),
            catalog.Load(ThemeIdentity.BuiltInNeonCircuit, ThemeIdentity.BuiltInNeonCircuitVersion).Package).Theme!;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        for (var i = 0; i < 3; i++)
        {
            if (i > 0) await widget.OnActionAsync(new("gallery.message", "gallery.message"));
            var snapshot = widget.Render().CreateSnapshot("gallery.scroll.fixture", i + 1);
            await File.WriteAllTextAsync(Path.Combine(directory, i + ".json"),
                JsonSerializer.Serialize(new { snapshot, renderStyles = BridgeRenderStyleResolver.Resolve(snapshot, theme) }, options));
        }
    }
}
