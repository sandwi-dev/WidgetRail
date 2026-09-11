using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WidgetRail.WindowsAppLibraryProvider;

/// <summary>
/// Reads bounded Steam app manifests from registered library roots. It never
/// reads game files or exposes paths to widget code; launch is by revalidated
/// numeric Steam AppId only.
/// </summary>
internal sealed class WindowsSteamApplicationSource : ISteamApplicationSource
{
    internal const int MaximumLibraries = 32;
    internal const int MaximumManifests = 4096;
    internal const int MaximumManifestBytes = 1024 * 1024;
    internal const int MaximumQuotedTokens = 16_384;
    internal const int MaximumTokenCharacters = 4096;
    private readonly Func<IReadOnlyList<string>> _steamRoots;
    private readonly WindowsSteamArtworkSource _artwork;

    internal WindowsSteamApplicationSource() : this(
        DiscoverSteamRoots, new WindowsSteamArtworkSource()) { }

    internal WindowsSteamApplicationSource(IEnumerable<string> steamRoots) :
        this(() => steamRoots.ToArray(), new WindowsSteamArtworkSource()) { }

    internal WindowsSteamApplicationSource(
        IEnumerable<string> steamRoots,
        WindowsSteamArtworkSource artwork) :
        this(() => steamRoots.ToArray(), artwork) { }

    private WindowsSteamApplicationSource(
        Func<IReadOnlyList<string>> steamRoots,
        WindowsSteamArtworkSource artwork)
    {
        _steamRoots = steamRoots ?? throw new ArgumentNullException(nameof(steamRoots));
        _artwork = artwork ?? throw new ArgumentNullException(nameof(artwork));
    }

    public IReadOnlyList<SteamRegistration> Enumerate(CancellationToken cancellationToken)
    {
        var candidate = Stage(cancellationToken);
        candidate.Commit?.Commit();
        return candidate.Registrations;
    }

    public SteamApplicationSourceCandidate Stage(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new([]);
        var result = new List<SteamRegistration>();
        var libraries = DiscoverSteamAppsDirectories(cancellationToken);
        var trustedRoots = libraries
            .Select(library => library.TrustedSteamRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var library in libraries)
        {
            IReadOnlyList<string> manifests;
            try
            {
                manifests = Directory.EnumerateFiles(
                        library.SteamAppsDirectory, "appmanifest_*.acf",
                        SearchOption.TopDirectoryOnly)
                    .Take(MaximumManifests - result.Count)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException or DirectoryNotFoundException or
                System.Security.SecurityException)
            {
                continue;
            }

            foreach (var manifest in manifests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryReadManifest(manifest, cancellationToken) is { } registration)
                    result.Add(registration);
                if (result.Count >= MaximumManifests)
                    return StageArtwork(result, trustedRoots);
            }
        }
        return StageArtwork(result, trustedRoots);
    }

    public SteamRegistration? ReadExact(
        string steamAppId,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !IsValidAppId(steamAppId) ||
            string.IsNullOrWhiteSpace(manifestPath)) return null;
        try
        {
            var exact = Path.GetFullPath(manifestPath);
            var libraries = DiscoverSteamAppsDirectories(cancellationToken);
            var library = libraries
                .SingleOrDefault(candidate =>
                    IsDirectChild(candidate.SteamAppsDirectory, exact));
            if (library is null) return null;
            var current = TryReadManifest(exact, cancellationToken);
            return current is not null &&
                string.Equals(current.SteamAppId, steamAppId, StringComparison.Ordinal)
                    ? current with
                    {
                        Artwork = _artwork.Register(
                            libraries.Select(candidate => candidate.TrustedSteamRoot),
                            steamAppId),
                    }
                    : null;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or ArgumentException or NotSupportedException or
            System.Security.SecurityException)
        {
            return null;
        }
    }

    public string? LoadArtwork(
        SteamRegistration exactRegistration,
        CancellationToken cancellationToken) =>
        exactRegistration.Artwork is { } artwork
            ? _artwork.Load(artwork, cancellationToken)
            : null;

    internal static bool IsValidAppId(string? value) =>
        value is { Length: > 0 and <= 10 } &&
        value.All(character => character is >= '0' and <= '9') &&
        uint.TryParse(value, out var parsed) && parsed > 0;

    private IReadOnlyList<SteamLibraryLocation> DiscoverSteamAppsDirectories(
        CancellationToken cancellationToken)
    {
        var locations = new Dictionary<string, SteamLibraryLocation>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var rootValue in _steamRoots()
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryNormalizeDirectory(rootValue, out var root)) continue;
            AddSteamAppsDirectory(Path.Combine(root, "steamapps"), root, locations);
            if (locations.Count >= MaximumLibraries) break;

            var librariesFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            foreach (var path in ReadLibraryPaths(librariesFile))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddSteamAppsDirectory(Path.Combine(path, "steamapps"), root, locations);
                if (locations.Count >= MaximumLibraries) break;
            }
        }
        return locations.Values
            .OrderBy(location => location.SteamAppsDirectory,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddSteamAppsDirectory(
        string path,
        string trustedSteamRoot,
        Dictionary<string, SteamLibraryLocation> locations)
    {
        if (locations.Count >= MaximumLibraries ||
            !TryNormalizeDirectory(path, out var normalized) ||
            !Directory.Exists(normalized)) return;
        locations.TryAdd(normalized, new SteamLibraryLocation(
            normalized, trustedSteamRoot));
    }

    private static bool TryNormalizeDirectory(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32_767) return false;
        try
        {
            normalized = Path.GetFullPath(value.Trim());
            return Path.IsPathFullyQualified(normalized);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static IEnumerable<string> ReadLibraryPaths(string file)
    {
        var text = ReadBoundedText(file);
        if (text is null) yield break;
        var tokens = TokenizeQuotedValues(text);
        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            if (!tokens[index].Equals("path", StringComparison.OrdinalIgnoreCase)) continue;
            yield return tokens[index + 1];
        }
    }

    private SteamRegistration? TryReadManifest(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var nameFromFile = Path.GetFileNameWithoutExtension(manifestPath);
        const string prefix = "appmanifest_";
        if (!nameFromFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var fileAppId = nameFromFile[prefix.Length..];
        if (!IsValidAppId(fileAppId)) return null;
        var snapshot = ReadBoundedFile(manifestPath);
        if (snapshot is null) return null;
        var tokens = TokenizeQuotedValues(snapshot.Text);
        string? appId = null;
        string? displayName = null;
        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            if (tokens[index].Equals("appid", StringComparison.OrdinalIgnoreCase))
                appId = tokens[index + 1];
            else if (tokens[index].Equals("name", StringComparison.OrdinalIgnoreCase))
                displayName = tokens[index + 1];
        }
        if (!string.Equals(appId, fileAppId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(displayName)) return null;
        return new SteamRegistration(
            "steam-" + appId,
            displayName,
            appId!,
            Path.GetFullPath(manifestPath),
            // Launch authority is the registered manifest location and numeric
            // AppId, not mutable Steam bookkeeping such as LastPlayed.
            "acf-" + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(appId + "\0" + displayName))).ToLowerInvariant());
    }

    private SteamApplicationSourceCandidate StageArtwork(
        IReadOnlyList<SteamRegistration> registrations,
        IReadOnlyList<string> trustedRoots)
    {
        var artwork = _artwork.StageCatalog(
            trustedRoots, registrations.Select(registration => registration.SteamAppId));
        var staged = registrations
            .Select(registration => registration with
            {
                Artwork = artwork.Registrations.GetValueOrDefault(
                    registration.SteamAppId),
            })
            .ToArray();
        return new(staged, artwork);
    }

    private static string? ReadBoundedText(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumManifestBytes) return null;
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or NotSupportedException)
        {
            return null;
        }
    }

    private static FileSnapshot? ReadBoundedFile(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumManifestBytes) return null;
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            using var reader = new StreamReader(
                new MemoryStream(bytes, writable: false), Encoding.UTF8, true);
            var text = reader.ReadToEnd();
            return new FileSnapshot(
                text,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or NotSupportedException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> TokenizeQuotedValues(string text)
    {
        var values = new List<string>();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '"') continue;
            if (values.Count >= MaximumQuotedTokens) break;
            var value = new StringBuilder();
            for (index++; index < text.Length; index++)
            {
                var character = text[index];
                if (character == '"') break;
                if (character == '\\' && index + 1 < text.Length &&
                    text[index + 1] is '\\' or '"')
                    character = text[++index];
                if (value.Length < MaximumTokenCharacters) value.Append(character);
            }
            values.Add(value.ToString());
        }
        return values;
    }

    private static bool IsDirectChild(string directory, string file) =>
        string.Equals(Path.GetDirectoryName(file), directory, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> DiscoverSteamRoots()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRegistryRoot(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath", roots);
        AddRegistryRoot(Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam",
            "InstallPath", roots);
        AddRegistryRoot(Registry.LocalMachine, @"Software\Valve\Steam", "InstallPath", roots);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFiles)) roots.Add(Path.Combine(programFiles, "Steam"));
        return roots.ToArray();
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryRoot(
        RegistryKey hive, string subKey, string valueName, HashSet<string> roots)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey, writable: false);
            if (key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
                roots.Add(value);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or
            System.Security.SecurityException or IOException)
        {
            // Registry access is optional; the conventional path remains a fallback.
        }
    }

    private sealed record FileSnapshot(string Text, string Sha256);
    private sealed record SteamLibraryLocation(
        string SteamAppsDirectory,
        string TrustedSteamRoot);
}
