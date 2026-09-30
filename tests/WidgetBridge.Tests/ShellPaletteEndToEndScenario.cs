using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;

internal static class ShellPaletteEndToEndScenario
{
    internal static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WidgetRail-shell-palette-" + Guid.NewGuid().ToString("N"));
        var paths = new PlatformSettingsPaths(directory);
        var store = new PlatformSettingsStore(paths);
        try
        {
            await store.UpdateAsync(value => value with { Appearance = value.Appearance with
                { ThemeId = ThemeIdentity.BuiltInNeonCircuit, ThemeVersion = ThemeIdentity.BuiltInNeonCircuitVersion } });
            await using var appearance = new PlatformAppearanceService(paths, new ThemeManager(store, new ThemeCatalog(paths)));
            await appearance.StartAsync();
            var pipe = "shell-palette-" + Guid.NewGuid().ToString("N");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var server = new WidgetBridgeServer(pipe, new([]), appearance: appearance);
            var serving = server.RunAsync(TimeSpan.FromSeconds(5), deadline.Token);
            await using (var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token))
            {
                var palette = await session.ReadShellStylesAsync(deadline.Token);
                var expected = new[] { "canvas", "backdrop", "panel", "tray", "tray-item", "tray-item:selected", "tray-item:focused",
                    "tray-item:selected:focused", "title", "body", "hint", "controller-glyph", "status", "tray-clock", "tray-date", "tray-status-icon" };
                if (!palette.Keys.OrderBy(value => value).SequenceEqual(expected.OrderBy(value => value)))
                    throw new InvalidOperationException("The complete real platform palette was not admitted.");
                if (palette["controller-glyph"].Base["font-size"].Number != 24 || palette["tray-clock"].Base["font-size"].Number != 22 ||
                    palette["tray-date"].Base["font-size"].Number != 11 || palette["tray-status-icon"].Base["font-size"].Number != 16)
                    throw new InvalidOperationException("Class-based shell typography did not survive real theme resolution and transport.");
                if (palette["tray-item:focused"].Base["outline-color"].Text != "#3fe0ff")
                    throw new InvalidOperationException("The selected built-in theme accent was lost.");
            }
            await serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
