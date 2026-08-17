namespace WidgetRail.PlatformSettings;

public sealed record ThemeVersionRetirementResult(
    string ThemeId,
    string Version,
    string Name,
    bool CleanupPending);

/// <summary>
/// Owns exact-version theme catalog mutations. The cross-process catalog lock
/// is shared with installation so selection and retirement revalidate the
/// immutable package identity immediately before committing their change.
/// </summary>
public sealed class ThemeCatalogMutationPolicy
{
    public const string LockFileName = "theme-catalog.lock";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(40);

    private readonly PlatformSettingsStore _settings;
    private readonly ThemeCatalog _catalog;
    private readonly PlatformSettingsPaths _paths;

    public ThemeCatalogMutationPolicy(PlatformSettingsStore settings, ThemeCatalog catalog)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _paths = settings.Paths;
    }

    public async Task<PlatformSettingsDocument> SelectAsync(
        string themeId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(themeId, version);
        await using var catalogLock = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
        var loaded = _catalog.Load(themeId, version);
        if (!loaded.IsValid)
            throw new PlatformSettingsException(
                loaded.Diagnostics.FirstOrDefault()?.Code ?? "theme_not_found",
                "The exact installed theme version is unavailable or invalid.");
        return await _settings.UpdateAsync(current => current with
        {
            Appearance = current.Appearance with
            {
                ThemeId = loaded.Descriptor.Id,
                ThemeVersion = loaded.Descriptor.Version.ToString(),
            },
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ThemeVersionRetirementResult> RetireAsync(
        string themeId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(themeId, version);
        if (ThemeIdentity.IsBuiltIn(themeId))
            throw new PlatformSettingsException(
                "builtin_theme_protected", "Built-in theme versions cannot be removed.");

        await using var catalogLock = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(settings.Appearance.ThemeId, themeId, StringComparison.Ordinal) &&
            string.Equals(settings.Appearance.ThemeVersion, version, StringComparison.Ordinal))
            throw new PlatformSettingsException(
                "selected_theme_protected", "The selected theme version cannot be removed.");

        var loaded = _catalog.Load(themeId, version);
        if (loaded.Descriptor.IsBuiltIn)
            throw new PlatformSettingsException(
                "builtin_theme_protected", "Built-in theme versions cannot be removed.");

        var themesRoot = _paths.ThemesDirectory;
        var idDirectory = Path.Combine(themesRoot, themeId);
        var versionDirectory = Path.Combine(idDirectory, version);
        if (!FileSystemGuard.IsWithin(themesRoot, versionDirectory))
            throw new PlatformSettingsException("theme_path_escape", "Theme version escapes the catalog root.");
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(themesRoot);
        FileSystemGuard.RejectReparsePoint(themesRoot);
        FileSystemGuard.RejectReparsePoint(idDirectory);
        FileSystemGuard.RejectReparsePoint(versionDirectory);
        if (!Directory.Exists(versionDirectory))
            throw new PlatformSettingsException("theme_not_found", "The exact theme version is no longer installed.");

        // Discover/load again after path validation so malformed packages remain
        // removable while a concurrently replaced identity cannot be retired.
        var current = _catalog.Discover().Themes.SingleOrDefault(item =>
            string.Equals(item.CatalogId, themeId, StringComparison.Ordinal) &&
            string.Equals(item.CatalogVersion, version, StringComparison.Ordinal));
        if (current is null)
            throw new PlatformSettingsException("theme_changed", "The theme catalog changed; review it again.");

        cancellationToken.ThrowIfCancellationRequested();
        var retired = Path.Combine(_paths.RootDirectory, $".theme-retired-{Guid.NewGuid():N}");
        try
        {
            Directory.Move(versionDirectory, retired);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PlatformSettingsException(
                "theme_remove_failed",
                "The exact theme version could not be retired; the catalog was left unchanged.",
                exception);
        }

        // The rename is the atomic catalog commit. Cleanup is bounded to the
        // exact retired tree and never follows a reparse point.
        var cleanupPending = false;
        try
        {
            DeleteTreeNoReparse(retired);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformSettingsException)
        {
            cleanupPending = true;
        }
        TryDeleteEmptyDirectory(idDirectory);
        return new ThemeVersionRetirementResult(
            themeId, version, SafeName(current.Descriptor.Name, themeId), cleanupPending);
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.RootDirectory);
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(_paths.RootDirectory);
        var lockPath = Path.Combine(_paths.RootDirectory, LockFileName);
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                FileSystemGuard.RejectReparsePoint(lockPath);
                return new FileStream(
                    lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    1, FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(LockRetry, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new PlatformSettingsException(
                    "theme_catalog_busy",
                    "The theme catalog is busy. Retry after the other operation finishes.",
                    exception);
            }
        }
    }

    private static void DeleteTreeNoReparse(string root)
    {
        FileSystemGuard.RejectReparsePoint(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            FileSystemGuard.RejectReparsePoint(entry);
            if (Directory.Exists(entry)) DeleteTreeNoReparse(entry);
            else File.Delete(entry);
        }
        Directory.Delete(root);
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void ValidateIdentity(string themeId, string version)
    {
        if (!ThemeIdentity.IsValid(themeId))
            throw new PlatformSettingsException("invalid_theme_id", "Theme ID is invalid.");
        if (!ThemeIdentity.TryParseCanonicalVersion(version, out _))
            throw new PlatformSettingsException("invalid_theme_version", "Theme version is invalid.");
    }

    private static string SafeName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl)
            ? fallback
            : name;
}
