using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Explicit installation roots; never attaches to another host's bridge.</summary>
internal sealed record OverlayShellOptions(string InstallationRoot, string SettingsRoot,
    string InstalledCatalogRoot, string? InitialWidgetId = null, string? LayoutDiagnosticsPath = null, string? SwitchDiagnosticsPath = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public DevelopmentLaunchOptions? Development { get; init; }

    internal static OverlayShellOptions Load(string path)
    {
        var options = JsonSerializer.Deserialize(File.ReadAllText(path), ShellJsonContext.CaseInsensitive.OverlayShellOptions)
            ?? throw new InvalidDataException("Overlay shell options are missing.");
        foreach (var root in new[] { options.InstallationRoot, options.SettingsRoot, options.InstalledCatalogRoot })
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
                throw new InvalidDataException("Overlay shell roots must be absolute paths.");
        if (options.LayoutDiagnosticsPath is { } diagnostics && !Path.IsPathFullyQualified(diagnostics))
            throw new InvalidDataException("Layout diagnostic output must be an absolute path.");
        if (options.SwitchDiagnosticsPath is { } switches && !Path.IsPathFullyQualified(switches))
            throw new InvalidDataException("Switch diagnostic output must be an absolute path.");
        return options;
    }
}
