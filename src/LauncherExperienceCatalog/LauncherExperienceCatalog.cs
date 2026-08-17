using System.Collections.ObjectModel;

namespace WidgetRail.LauncherExperienceCatalog;

public sealed class LauncherExperienceCatalog
{
    public const int MaximumInstalledVersions = 128;

    private readonly string _root;
    private readonly LauncherExperienceValidator _validator;

    public LauncherExperienceCatalog(string root, LauncherExperienceValidator? validator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _validator = validator ?? new LauncherExperienceValidator();
    }

    public IReadOnlyList<LauncherExperiencePackage> BuiltInRecoveryPackages =>
        LauncherExperienceBuiltIns.RecoveryPackages;

    public LauncherExperienceCatalogSnapshot Discover()
    {
        var entries = LauncherExperienceBuiltIns.RecoveryPackages
            .Select(package => new LauncherExperienceCatalogEntry(package.Descriptor, true, [], package))
            .ToList();
        if (!Directory.Exists(_root)) return new(entries);
        LauncherExperienceFileGuard.RejectReparsePoint(_root);
        var count = 0;
        foreach (var idDirectory in EnumerateBoundedDirectories(
                     _root, MaximumInstalledVersions, "too_many_experience_ids",
                     $"Launcher experience catalog may contain at most {MaximumInstalledVersions} ID directories."))
        {
            LauncherExperienceFileGuard.RejectReparsePoint(idDirectory);
            var id = Path.GetFileName(idDirectory);
            foreach (var versionDirectory in EnumerateBoundedDirectories(
                         idDirectory, MaximumInstalledVersions - count, "too_many_experiences",
                         $"Launcher experience catalog may contain at most {MaximumInstalledVersions} installed versions."))
            {
                LauncherExperienceFileGuard.RejectReparsePoint(versionDirectory);
                count++;
                var version = Path.GetFileName(versionDirectory);
                var validated = _validator.ValidateDirectory(versionDirectory, id, version);
                entries.Add(validated.Package is { } package
                    ? new LauncherExperienceCatalogEntry(package.Descriptor, true, [], package)
                    : new LauncherExperienceCatalogEntry(Fallback(id, version), false, validated.Diagnostics));
            }
        }
        return new LauncherExperienceCatalogSnapshot(new ReadOnlyCollection<LauncherExperienceCatalogEntry>(entries));
    }

    public LauncherExperienceCatalogEntry Load(string id, string version)
    {
        var builtIn = LauncherExperienceBuiltIns.RecoveryPackages.SingleOrDefault(item =>
            string.Equals(item.Descriptor.Id, id, StringComparison.Ordinal) &&
            string.Equals(item.Descriptor.Version.ToString(), version, StringComparison.Ordinal));
        if (builtIn is not null) return new(builtIn.Descriptor, true, [], builtIn);
        if (!LauncherExperienceIdentity.IsValidId(id) || !LauncherExperienceIdentity.TryParseCanonicalVersion(version, out _))
            return new(Fallback(id, version), false, [new("$", "invalid_identity", "Launcher experience identity or version is invalid.")]);
        var directory = Path.Combine(_root, id, version);
        if (!Directory.Exists(directory))
            return new(Fallback(id, version), false, [new("$", "experience_not_found", "Launcher experience is not installed.")]);
        if (!LauncherExperienceFileGuard.IsWithin(_root, directory))
            return new(Fallback(id, version), false, [new("$", "path_escape", "Launcher experience path escapes the catalog root.")]);
        var validated = _validator.ValidateDirectory(directory, id, version);
        return validated.Package is { } package
            ? new(package.Descriptor, true, [], package)
            : new(Fallback(id, version), false, validated.Diagnostics);
    }

    public LauncherExperiencePackage ResolveOrRecovery(string id, string version, LauncherLayoutPreset recoveryPreset)
    {
        var selected = Load(id, version);
        return selected.Package ?? LauncherExperienceBuiltIns.RecoveryFor(recoveryPreset);
    }

    private static LauncherExperienceDescriptor Fallback(string? id, string? version) =>
        new(
            LauncherExperienceIdentity.IsValidId(id) ? id! : "invalid.launcher-experience",
            "invalid.publisher",
            string.IsNullOrWhiteSpace(id) ? "Invalid launcher experience" : id,
            LauncherExperienceIdentity.TryParseCanonicalVersion(version, out var parsed) ? parsed! : new Version(0, 0, 0),
            LauncherLayoutPreset.CompactGrid,
            false,
            string.Empty);

    private static IReadOnlyList<string> EnumerateBoundedDirectories(
        string root,
        int maximum,
        string code,
        string message)
    {
        var directories = new List<string>(Math.Min(maximum, MaximumInstalledVersions));
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (directories.Count == maximum)
                throw new LauncherExperiencePackageException(code, message);
            LauncherExperienceFileGuard.RejectReparsePoint(directory);
            directories.Add(directory);
        }
        directories.Sort(StringComparer.Ordinal);
        return directories;
    }
}
