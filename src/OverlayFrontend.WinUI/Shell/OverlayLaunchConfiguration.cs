namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>
/// Resolves one explicit installation and profile. Default launch uses the app's
/// own payload, never the current directory or another version's registry entry.
/// Resolution and preflight do not create directories or start a bridge.
/// </summary>
internal static class OverlayLaunchConfiguration
{
    internal static OverlayShellOptions Resolve(IReadOnlyList<string> arguments, string applicationDirectory,
        string defaultSettingsRoot)
    {
        var configuration = Value(arguments, "--shell-config");
        var installation = Value(arguments, "--installation-root");
        var settings = Value(arguments, "--settings-root");
        var catalog = Value(arguments, "--installed-catalog-root");
        var widget = Value(arguments, "--widget");
        if (configuration is not null)
        {
            if (installation is not null || settings is not null || catalog is not null || widget is not null)
                throw new InvalidDataException("Shell configuration cannot be combined with launch-root overrides.");
            return OverlayShellOptions.Load(configuration);
        }
        var root = Absolute(installation ?? applicationDirectory);
        var profile = Absolute(settings ?? defaultSettingsRoot);
        if (widget is not null && !ShellPreferences.ValidId(widget))
            throw new InvalidDataException("The initial widget identifier is invalid.");
        return new(root, profile, Absolute(catalog ?? Path.Combine(profile, "widgets")), widget);
    }

    internal static bool HasInstallationFiles(OverlayShellOptions options, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        return fileExists(Path.Combine(options.InstallationRoot, "widget-catalog.json")) &&
            fileExists(Path.Combine(options.InstallationRoot, "runtime", "Bridge", "WidgetBridge.exe"));
    }

    private static string Absolute(string path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
        : throw new InvalidDataException("Launch roots must be absolute paths.");

    private static string? Value(IReadOnlyList<string> arguments, string name)
    {
        var values = arguments.Where(value => value == name || value.StartsWith(name + "=", StringComparison.Ordinal)).ToArray();
        if (values.Length == 0) return null;
        if (values.Length != 1 || values[0].Length <= name.Length + 1 || string.IsNullOrWhiteSpace(values[0][(name.Length + 1)..]))
            throw new InvalidDataException($"Invalid or conflicting value for {name}.");
        return values[0][(name.Length + 1)..];
    }
}
