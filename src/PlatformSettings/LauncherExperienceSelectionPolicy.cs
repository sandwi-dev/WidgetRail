using GameBarAlternative.LauncherExperienceCatalog;

namespace GameBarAlternative.PlatformSettings;

public sealed record LauncherExperienceRetirementResult(
    string Id,
    string Version,
    string Name);

/// <summary>
/// Owns exact Launcher Experience selection and Settings-managed retirement.
/// Package parsing and immutable catalog mutation stay in
/// LauncherExperienceCatalog; the settings document remains the sole durable
/// selection and last-good owner.
/// </summary>
public sealed class LauncherExperienceSelectionPolicy
{
    private readonly PlatformSettingsStore _settings;
    private readonly string _catalogRoot;

    public LauncherExperienceSelectionPolicy(PlatformSettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _catalogRoot = settings.Paths.LauncherExperiencesDirectory;
    }

    public Task<PlatformSettingsDocument> SelectAsync(
        string id,
        string version,
        CancellationToken cancellationToken = default) =>
        LauncherExperienceArchive.ExecuteMutationAsync(
            _catalogRoot,
            async (root, token) =>
            {
                var entry = RequireValid(
                    new GameBarAlternative.LauncherExperienceCatalog.LauncherExperienceCatalog(root)
                        .Load(id, version));
                return await _settings.UpdateAsync(current => current with
                {
                    LauncherExperience = current.LauncherExperience with
                    {
                        UseGlobalAppearance = false,
                        SelectedId = entry.Descriptor.Id,
                        SelectedVersion = entry.Descriptor.Version.ToString(),
                        LastGoodId = entry.Descriptor.Id,
                        LastGoodVersion = entry.Descriptor.Version.ToString(),
                    },
                }, token).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<PlatformSettingsDocument> UseGlobalAppearanceAsync(
        CancellationToken cancellationToken = default) =>
        _settings.UpdateAsync(current => current with
        {
            LauncherExperience = current.LauncherExperience with
            {
                UseGlobalAppearance = true,
            },
        }, cancellationToken);

    public Task<PlatformSettingsDocument> RecoverBuiltInAsync(
        CancellationToken cancellationToken = default)
    {
        var recovery = LauncherExperienceBuiltIns.RecoveryFor(
            LauncherLayoutPreset.HeroRail).Descriptor;
        return SelectAsync(
            recovery.Id, recovery.Version.ToString(), cancellationToken);
    }

    public Task<LauncherExperienceRetirementResult> RetireAsync(
        string id,
        string version,
        CancellationToken cancellationToken = default) =>
        LauncherExperienceArchive.ExecuteMutationAsync(
            _catalogRoot,
            async (root, token) =>
            {
                var settings = await _settings.LoadAsync(token).ConfigureAwait(false);
                var selection = settings.LauncherExperience;
                if (string.Equals(selection.SelectedId, id, StringComparison.Ordinal) &&
                    string.Equals(selection.SelectedVersion, version, StringComparison.Ordinal))
                    throw new PlatformSettingsException(
                        "selected_launcher_experience_protected",
                        "The selected Launcher Experience version cannot be removed.");
                var entry = RequireValid(
                    new GameBarAlternative.LauncherExperienceCatalog.LauncherExperienceCatalog(root)
                        .Load(id, version));
                if (entry.Descriptor.IsBuiltIn)
                    throw new PlatformSettingsException(
                        "builtin_launcher_experience_protected",
                        "Built-in recovery experiences cannot be removed.");
                try
                {
                    LauncherExperienceArchive.RemoveUnderMutationLock(root, id, version);
                }
                catch (LauncherExperiencePackageException exception)
                {
                    throw Map(exception);
                }
                return new LauncherExperienceRetirementResult(
                    entry.Descriptor.Id,
                    entry.Descriptor.Version.ToString(),
                    SafeName(entry.Descriptor.Name, entry.Descriptor.Id));
            },
            cancellationToken);

    private static LauncherExperienceCatalogEntry RequireValid(
        LauncherExperienceCatalogEntry entry)
    {
        if (entry.IsValid && entry.Package is not null) return entry;
        var diagnostic = entry.Diagnostics.FirstOrDefault();
        throw new PlatformSettingsException(
            diagnostic?.Code ?? "launcher_experience_not_found",
            "The exact installed Launcher Experience version is unavailable or invalid.");
    }

    private static PlatformSettingsException Map(
        LauncherExperiencePackageException exception) => new(
        exception.Code,
        "The exact Launcher Experience version could not be removed.",
        exception);

    private static string SafeName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(char.IsControl)
            ? fallback
            : name;
}
