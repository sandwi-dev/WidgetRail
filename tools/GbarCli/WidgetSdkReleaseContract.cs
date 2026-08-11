using System.Reflection;
using System.Text.RegularExpressions;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.GbarCli;

internal sealed partial record WidgetSdkReleaseContract(
    string Version,
    string PackageId,
    int TemplateVersion)
{
    internal static WidgetSdkReleaseContract Current { get; } = Load();

    internal string LocalPackageVersion(string contentSuffix) =>
        Version.Contains('-', StringComparison.Ordinal)
            ? $"{Version}.local.{contentSuffix}"
            : $"{Version}-local.{contentSuffix}";

    private static WidgetSdkReleaseContract Load()
    {
        var cli = Read(typeof(CliApplication).Assembly, "gbar");
        var sdk = Read(typeof(Widget).Assembly, "WidgetSdk");
        if (cli != sdk)
            throw new CliUsageException(
                "gbar and WidgetSdk were built from different release-unit contracts. " +
                "Rebuild or reinstall them together before scaffolding.");
        return cli;
    }

    private static WidgetSdkReleaseContract Read(Assembly assembly, string label)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(item => item.Key is not null)
            .ToDictionary(item => item.Key!, item => item.Value ?? string.Empty, StringComparer.Ordinal);
        var version = Required(metadata, "WidgetSdkReleaseVersion", label);
        if (!VersionRegex().IsMatch(version))
            throw new CliUsageException(
                $"The {label} WidgetSdk release version '{version}' is invalid. Repair eng/WidgetSdkRelease.props and rebuild.");
        var packageId = Required(metadata, "WidgetSdkPackageId", label);
        if (!PackageIdRegex().IsMatch(packageId))
            throw new CliUsageException(
                $"The {label} WidgetSdk package ID '{packageId}' is invalid. Repair eng/WidgetSdkRelease.props and rebuild.");
        var templateText = Required(metadata, "ControllerWidgetTemplateVersion", label);
        if (!int.TryParse(templateText, out var templateVersion) || templateVersion is <= 0 or > 1000)
            throw new CliUsageException(
                $"The {label} ControllerWidget template version '{templateText}' is invalid. Repair eng/WidgetSdkRelease.props and rebuild.");
        return new(version, packageId, templateVersion);
    }

    private static string Required(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        string label)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            throw new CliUsageException(
                $"The {label} assembly is missing release metadata '{key}'. Rebuild gbar and WidgetSdk from the same checkout.");
        return value;
    }

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*(?:\\.[A-Za-z][A-Za-z0-9]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdRegex();
}
