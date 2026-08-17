using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WidgetRail.WindowsAppLibraryProvider;

/// <summary>
/// Reads Epic's installed-item manifests from one fixed provider-owned root.
/// Manifest paths, catalog identifiers, and executable evidence never leave
/// this assembly.
/// </summary>
internal sealed class EpicInstalledGameApplicationSource : IEpicApplicationSource
{
    internal const int MaximumManifests = 4_096;
    internal const int MaximumManifestBytes = 256 * 1024;
    internal static string DefaultManifestRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");

    private readonly string _manifestRoot;
    private readonly Func<CancellationToken, bool> _isEnabled;
    private readonly Action<string>? _afterRead;

    internal EpicInstalledGameApplicationSource(
        string manifestRoot,
        Func<CancellationToken, bool> isEnabled,
        Action<string>? afterRead = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestRoot);
        _manifestRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(manifestRoot));
        _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _afterRead = afterRead;
    }

    public EpicApplicationSourceCandidate Enumerate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!_isEnabled(cancellationToken))
                return new(GameLibrarySourceHealth.Disabled, []);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            return new(GameLibrarySourceHealth.Unavailable, []);
        }
        if (!IsSafeExistingDirectory(_manifestRoot))
            return new(GameLibrarySourceHealth.Unavailable, []);

        try
        {
            var paths = Directory.EnumerateFiles(
                    _manifestRoot, "*.item", SearchOption.TopDirectoryOnly)
                .Take(MaximumManifests + 1)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (paths.Length > MaximumManifests)
                return new(GameLibrarySourceHealth.Degraded, []);

            var registrations = new List<EpicGameRegistration>(paths.Length);
            var corrupt = false;
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var registration = TryReadManifest(path, cancellationToken);
                if (registration is null) corrupt = true;
                else registrations.Add(registration);
            }

            var duplicate = registrations
                .GroupBy(item => item.IdentityKey, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() != 1);
            if (duplicate) return new(GameLibrarySourceHealth.Degraded, []);
            return new(corrupt ? GameLibrarySourceHealth.Degraded :
                GameLibrarySourceHealth.Healthy,
                registrations.OrderBy(item => item.IdentityKey,
                    StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            return new(GameLibrarySourceHealth.Unavailable, []);
        }
    }

    public EpicGameRegistration? ReadExact(
        string manifestPath,
        string appName,
        CancellationToken cancellationToken)
    {
        if (!IsIdentifier(appName) || !IsDirectManifest(manifestPath)) return null;
        try
        {
            var current = TryReadManifest(manifestPath, cancellationToken);
            return current is not null && string.Equals(
                current.AppName, appName, StringComparison.Ordinal)
                ? current : null;
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            return null;
        }
    }

    private EpicGameRegistration? TryReadManifest(
        string path,
        CancellationToken cancellationToken)
    {
        if (!IsDirectManifest(path) || File.GetAttributes(path).HasFlag(
                FileAttributes.ReparsePoint)) return null;
        var before = new FileInfo(path);
        if (!before.Exists || before.Length is <= 0 or > MaximumManifestBytes)
            return null;
        var length = before.Length;
        var write = before.LastWriteTimeUtc;
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete, 4096,
                   FileOptions.SequentialScan))
        {
            bytes = new byte[length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) return null;
                offset += read;
            }
            if (stream.ReadByte() != -1) return null;
        }
        _afterRead?.Invoke(path);
        var after = new FileInfo(path);
        if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != write)
            return null;

        try
        {
            RejectDuplicateProperties(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryInt(root, "FormatVersion", out var format) || format != 0 ||
                !TryString(root, "AppName", out var appName, IsIdentifier) ||
                !TryString(root, "DisplayName", out var rawName,
                    value => value.Length <= 120) ||
                WindowsAppLibraryProvider.SanitizeDisplayName(rawName) is not
                    { } displayName ||
                !TryString(root, "CatalogNamespace", out var catalogNamespace,
                    IsIdentifier) ||
                !TryString(root, "CatalogItemId", out var catalogItemId,
                    IsIdentifier) ||
                !TryString(root, "InstallLocation", out var installLocation,
                    value => value.Length <= 32_767) ||
                !TryString(root, "LaunchExecutable", out var launchExecutable,
                    value => value.Length <= 512))
                return null;

            if (!root.TryGetProperty("bIsApplication", out var application) ||
                application.ValueKind != JsonValueKind.False) return null;
            if (!TryValidateInstall(
                    installLocation, launchExecutable,
                    out var exactInstall, out var exactExecutable)) return null;

            var identity = "epic-" + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(catalogNamespace + "\0" + catalogItemId +
                    "\0" + appName))).ToLowerInvariant();
            var executable = new FileInfo(exactExecutable);
            var revision = "epic-" + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(Convert.ToHexString(SHA256.HashData(bytes)) +
                    "\0" + executable.Length + "\0" +
                    executable.LastWriteTimeUtc.Ticks)));
            return new(identity, displayName, appName, catalogNamespace,
                catalogItemId, Path.GetFullPath(path), exactInstall,
                launchExecutable.Replace('\\', '/'), revision);
        }
        catch (Exception exception) when (exception is JsonException or
            InvalidOperationException or ArgumentException or IOException or
            UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryValidateInstall(
        string installLocation,
        string launchExecutable,
        out string exactInstall,
        out string exactExecutable)
    {
        exactInstall = string.Empty;
        exactExecutable = string.Empty;
        if (!Path.IsPathFullyQualified(installLocation) ||
            Path.IsPathFullyQualified(launchExecutable)) return false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installLocation));
        var executable = Path.GetFullPath(Path.Combine(root, launchExecutable));
        var relative = Path.GetRelativePath(root, executable);
        if (relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            string.Equals(relative, "..", StringComparison.Ordinal) ||
            Path.GetExtension(executable).Equals(".exe",
                StringComparison.OrdinalIgnoreCase) is false ||
            !IsSafeExistingDirectory(root) || !File.Exists(executable) ||
            File.GetAttributes(executable).HasFlag(FileAttributes.ReparsePoint))
            return false;
        exactInstall = root;
        exactExecutable = executable;
        return true;
    }

    private bool IsDirectManifest(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return false;
        var exact = Path.GetFullPath(path);
        return Path.GetExtension(exact).Equals(".item", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetDirectoryName(exact), _manifestRoot,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeExistingDirectory(string path)
    {
        if (!Directory.Exists(path)) return false;
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if (current.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            current = current.Parent;
        }
        return true;
    }

    private static bool TryString(
        JsonElement root,
        string name,
        out string value,
        Func<string, bool> validator)
    {
        value = string.Empty;
        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            property.GetString() is { } candidate && validator(candidate) &&
            (value = candidate) is not null;
    }

    private static bool TryInt(JsonElement root, string name, out int value)
    {
        value = default;
        return root.TryGetProperty(name, out var property) &&
            property.TryGetInt32(out value);
    }

    private static bool IsIdentifier(string value) =>
        value.Length is > 0 and <= 128 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16,
        });
        var scopes = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                scopes.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject)
                scopes.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName &&
                !scopes.Peek().Add(reader.GetString()!))
                throw new JsonException("Duplicate manifest property.");
        }
    }

    private static bool IsSourceFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or InvalidOperationException or
        System.Security.SecurityException or ArgumentException or NotSupportedException;
}
