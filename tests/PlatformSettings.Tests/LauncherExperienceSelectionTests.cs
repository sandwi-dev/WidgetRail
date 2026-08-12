using System.Buffers.Binary;
using GameBarAlternative.LauncherExperienceCatalog;
using GameBarAlternative.PlatformSettings;

internal static class LauncherExperienceSelectionTests
{
    public static async Task Run()
    {
        using var temp = new SelectionTempDirectory();
        var paths = new PlatformSettingsPaths(temp.Path);
        var store = new PlatformSettingsStore(paths);
        var policy = new LauncherExperienceSelectionPolicy(store);
        WritePackage(paths.LauncherExperiencesDirectory, "dev.example.launcher", "1.0.0", "First");
        WritePackage(paths.LauncherExperiencesDirectory, "dev.example.launcher", "2.0.0", "Second");

        var defaults = await store.LoadAsync();
        Equal(true, defaults.LauncherExperience.UseGlobalAppearance);
        Equal(null, defaults.LauncherExperience.SelectedId);

        var selected = await policy.SelectAsync("dev.example.launcher", "2.0.0");
        Equal(false, selected.LauncherExperience.UseGlobalAppearance);
        Equal("dev.example.launcher", selected.LauncherExperience.SelectedId);
        Equal("2.0.0", selected.LauncherExperience.SelectedVersion);
        Equal("dev.example.launcher", selected.LauncherExperience.LastGoodId);
        Equal("2.0.0", selected.LauncherExperience.LastGoodVersion);

        var global = await policy.UseGlobalAppearanceAsync();
        Equal(true, global.LauncherExperience.UseGlobalAppearance);
        Equal("dev.example.launcher", global.LauncherExperience.SelectedId);
        Equal("2.0.0", global.LauncherExperience.LastGoodVersion);
        await ThrowsCode(
            () => policy.RetireAsync("dev.example.launcher", "2.0.0"),
            "selected_launcher_experience_protected");

        var retired = await policy.RetireAsync("dev.example.launcher", "1.0.0");
        Equal("dev.example.launcher", retired.Id);
        True(!Directory.Exists(Path.Combine(paths.LauncherExperiencesDirectory,
            "dev.example.launcher", "1.0.0")), "Inactive exact version remained installed.");

        var prior = await store.LoadAsync();
        await ThrowsCode(
            () => policy.SelectAsync("dev.example.missing", "1.0.0"),
            "experience_not_found");
        Equal(prior, await store.LoadAsync());

        var recovered = await policy.RecoverBuiltInAsync();
        Equal(false, recovered.LauncherExperience.UseGlobalAppearance);
        Equal("org.gbar.builtin.hero-rail", recovered.LauncherExperience.SelectedId);
        Equal("1.0.0", recovered.LauncherExperience.SelectedVersion);
        Equal(recovered.LauncherExperience.SelectedId, recovered.LauncherExperience.LastGoodId);
        Equal(recovered.LauncherExperience.SelectedVersion, recovered.LauncherExperience.LastGoodVersion);

        await ThrowsCode(
            () => store.UpdateAsync(current => current with
            {
                LauncherExperience = current.LauncherExperience with
                {
                    UseGlobalAppearance = false,
                    SelectedId = "dev.example.launcher",
                    SelectedVersion = null,
                },
            }),
            "required");
    }

    private static void WritePackage(string root, string id, string version, string name)
    {
        var directory = Path.Combine(root, id, version);
        Directory.CreateDirectory(Path.Combine(directory, "layouts"));
        Directory.CreateDirectory(Path.Combine(directory, "styles"));
        Directory.CreateDirectory(Path.Combine(directory, "assets"));
        File.WriteAllText(Path.Combine(directory, "launcher.json"),
            "{\"schemaVersion\":1,\"id\":\"" + id +
            "\",\"publisher\":\"dev.example\",\"name\":\"" + name +
            "\",\"version\":\"" + version +
            "\",\"layoutPreset\":\"hero-rail\",\"compositionFile\":\"layouts/launcher-layout.json\"," +
            "\"styleFile\":\"styles/launcher.gbss\",\"previewFile\":\"assets/preview.png\",\"parameters\":{}}");
        File.WriteAllText(Path.Combine(directory, "layouts", "launcher-layout.json"),
            "{\"schemaVersion\":1,\"branches\":{\"compact\":{\"root\":" + Root() +
            "},\"standard\":{\"root\":" + Root() + "},\"wide\":{\"root\":" + Root() + "}}}");
        File.WriteAllText(Path.Combine(directory, "styles", "launcher.gbss"),
            "launcher-game-rail { color: #ffffff; } launcher-details-panel { background: rgba(0, 0, 0, 0.5); }");
        var png = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        "IHDR"u8.CopyTo(png.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), 64);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), 36);
        File.WriteAllBytes(Path.Combine(directory, "assets", "preview.png"), png);
    }

    private static string Root() =>
        "{\"type\":\"overlay\",\"children\":[" +
        "{\"type\":\"region\",\"slot\":\"hero-background\",\"region\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1}}," +
        "{\"type\":\"region\",\"slot\":\"game-rail\",\"orientation\":\"horizontal\",\"region\":{\"x\":0.08,\"y\":0.62,\"width\":0.84,\"height\":0.24}}," +
        "{\"type\":\"region\",\"slot\":\"source-status\",\"region\":{\"x\":0.68,\"y\":0.08,\"width\":0.24,\"height\":0.1}}," +
        "{\"type\":\"region\",\"slot\":\"details-panel\",\"region\":{\"x\":0.08,\"y\":0.08,\"width\":0.5,\"height\":0.4}}," +
        "{\"type\":\"region\",\"slot\":\"controller-hints\",\"region\":{\"x\":0.52,\"y\":0.91,\"width\":0.4,\"height\":0.06}}]}";

    private static async Task ThrowsCode(Func<Task> action, string code)
    {
        try { await action(); }
        catch (PlatformSettingsException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException($"Expected PlatformSettingsException '{code}'.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class SelectionTempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "gbar-launcher-selection-" + Guid.NewGuid().ToString("N"));
        public SelectionTempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }
}
