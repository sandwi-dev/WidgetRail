using WidgetRail.PlatformSettings;

namespace WidgetRail.FirstPartyWidgets.Settings.Worker;

/// <summary>The trusted host's profile applies to settings, themes and consent together.</summary>
internal static class SettingsWorkerProfile
{
    internal static PlatformSettingsStore CreateStore(IReadOnlyList<string> arguments)
    {
        var indexes = Enumerable.Range(0, arguments.Count)
            .Where(index => arguments[index] == "--settings-root").ToArray();
        if (indexes.Length == 0) return new(PlatformSettingsPaths.CreateDefault());
        if (indexes.Length != 1 || indexes[0] + 1 >= arguments.Count)
            throw new ArgumentException("The Settings worker requires exactly one value for --settings-root.", nameof(arguments));
        var root = arguments[indexes[0] + 1];
        if (string.IsNullOrWhiteSpace(root) || root.Length > 1024 || root.StartsWith("--", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(root))
            throw new ArgumentException("The Settings worker profile must be an explicit absolute path.", nameof(arguments));
        return new(new PlatformSettingsPaths(root));
    }
}
