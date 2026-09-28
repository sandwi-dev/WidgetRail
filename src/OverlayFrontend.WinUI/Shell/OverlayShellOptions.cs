using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Explicit installation roots; never attaches to another host's bridge.</summary>
internal sealed record OverlayShellOptions(string InstallationRoot, string SettingsRoot,
    string InstalledCatalogRoot, string? InitialWidgetId = null)
{
    internal static OverlayShellOptions Load(string path)
    {
        var options = JsonSerializer.Deserialize<OverlayShellOptions>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Overlay shell options are missing.");
        foreach (var root in new[] { options.InstallationRoot, options.SettingsRoot, options.InstalledCatalogRoot })
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
                throw new InvalidDataException("Overlay shell roots must be absolute paths.");
        return options;
    }
}
