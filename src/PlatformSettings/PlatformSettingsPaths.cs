namespace GameBarAlternative.PlatformSettings;

public sealed class PlatformSettingsPaths
{
    public PlatformSettingsPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        SettingsFile = Path.Combine(RootDirectory, "platform-settings.json");
        ThemesDirectory = Path.Combine(RootDirectory, "themes");
    }

    public string RootDirectory { get; }
    public string SettingsFile { get; }
    public string ThemesDirectory { get; }

    public static PlatformSettingsPaths CreateDefault()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            throw new PlatformSettingsException(
                "local_app_data_unavailable",
                "The current user's Local Application Data directory is unavailable.");
        return new PlatformSettingsPaths(Path.Combine(local, "GameBarAlternative"));
    }
}
