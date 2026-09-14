using WidgetRail.PlatformSettings;

internal static class DisplayScaleTests
{
    internal static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "WidgetRail-scale-tests-" + Guid.NewGuid().ToString("N"));
        var store = new PlatformSettingsStore(new PlatformSettingsPaths(root));
        try
        {
            var fallback = AppearanceSettings.Default with { InterfaceScale = 1.1, TextScale = 1.2 };
            await store.ReplaceAsync(PlatformSettingsDocument.Default with { Appearance = fallback });
            var legacy = await File.ReadAllTextAsync(store.Paths.SettingsFile);
            // The new property is additive; simulate an older writer.
            var json = System.Text.Json.Nodes.JsonNode.Parse(legacy)!;
            json["appearance"]!.AsObject().Remove("displayScales");
            await File.WriteAllTextAsync(store.Paths.SettingsFile, json.ToJsonString());
            var loaded = await store.LoadAsync();
            Check(DisplayScalePolicy.Resolve(loaded.Appearance, "unseen") == new DisplayScaleSettings(1.1, 1.2));
            await store.UpdateAsync(current => current with
            {
                Appearance = DisplayScalePolicy.Set(current.Appearance, "monitor-a", new(0.85, 1.4)),
            });
            await store.UpdateAsync(current => current with
            {
                Appearance = DisplayScalePolicy.Set(current.Appearance, "monitor-b", new(1.25, 0.9)),
            });
            var reopened = new PlatformSettingsStore(new PlatformSettingsPaths(root));
            await reopened.UpdateAsync(current => current with
            {
                Appearance = current.Appearance with { BoldText = !current.Appearance.BoldText },
            });
            loaded = await store.LoadAsync();
            Check(DisplayScalePolicy.Resolve(loaded.Appearance, "monitor-a") == new DisplayScaleSettings(0.85, 1.4));
            Check(DisplayScalePolicy.Resolve(loaded.Appearance, "monitor-b") == new DisplayScaleSettings(1.25, 0.9));
            Check(DisplayScalePolicy.Resolve(loaded.Appearance, null) == new DisplayScaleSettings(1.1, 1.2));
            var before = await File.ReadAllTextAsync(store.Paths.SettingsFile);
            try
            {
                await store.UpdateAsync(current => current with
                {
                    Appearance = DisplayScalePolicy.Set(current.Appearance, "monitor-a", new(double.NaN, 1)),
                });
                throw new Exception("Invalid scale accepted");
            }
            catch (PlatformSettingsException) { }
            Check(before == await File.ReadAllTextAsync(store.Paths.SettingsFile));
            var bounded = fallback;
            for (var i = 0; i < DisplayScalePolicy.MaximumDisplays; ++i)
                bounded = DisplayScalePolicy.Set(bounded, "display-" + i, new(1, 1));
            bounded = DisplayScalePolicy.Set(bounded, "display-0", new(1.1, 1.1));
            try { DisplayScalePolicy.Set(bounded, "overflow", new(1, 1)); throw new Exception("Unbounded displays"); }
            catch (PlatformSettingsException) { }
            Check(PlatformSettingsValidator.Validate(PlatformSettingsDocument.Default with
            {
                Appearance = fallback with { DisplayScales = new Dictionary<string, DisplayScaleSettings> { ["bad\nkey"] = new(1, 1) } },
            }).Count > 0);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void Check(bool value) { if (!value) throw new Exception("Display sizing invariant failed"); }
}
