namespace WidgetRail.PlatformSettings;

public sealed class PlatformSettingsPaths
{
    public PlatformSettingsPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        SettingsFile = Path.Combine(RootDirectory, "platform-settings.json");
        ThemesDirectory = Path.Combine(RootDirectory, "themes");
        WidgetConfigurationDirectory = Path.Combine(RootDirectory, "widget-config");
    }

    public string RootDirectory { get; }
    public string SettingsFile { get; }
    public string ThemesDirectory { get; }
    /// <summary>
    /// Host-owned, package-scoped non-secret widget configuration. Authentication
    /// tokens and other secrets must never be stored here.
    /// </summary>
    public string WidgetConfigurationDirectory { get; }

    public static PlatformSettingsPaths CreateDefault()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            throw new PlatformSettingsException(
                "local_app_data_unavailable",
                "The current user's Local Application Data directory is unavailable.");
        return new PlatformSettingsPaths(Path.Combine(local, "WidgetRail"));
    }
}
