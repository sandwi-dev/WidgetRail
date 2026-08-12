using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameBarAlternative.WindowsAppLibraryProvider;

/// <summary>
/// Converts GOG's fixed machine registration plus its bounded per-install info
/// file into best-effort installed evidence. Disabled discovery exits before
/// any registry or filesystem access. This source grants no launch authority.
/// </summary>
internal sealed class GogInstalledGameApplicationSource : IGogApplicationSource
{
    internal const int MaximumInfoBytes = 256 * 1024;
    private readonly IGogRegistryReader _registry;
    private readonly Func<CancellationToken, bool> _isEnabled;
    private readonly Action<string>? _afterInfoRead;

    internal GogInstalledGameApplicationSource(
        IGogRegistryReader registry,
        Func<CancellationToken, bool> isEnabled,
        Action<string>? afterInfoRead = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _afterInfoRead = afterInfoRead;
    }

    public GogApplicationSourceCandidate Enumerate(CancellationToken cancellationToken)
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

        var snapshot = _registry.Enumerate(cancellationToken);
        if (!snapshot.IsAvailable ||
            snapshot.Records.Count > WindowsGogRegistryReader.MaximumRegistrations)
            return new(GameLibrarySourceHealth.Unavailable, []);

        var registrations = new List<GogGameRegistration>(snapshot.Records.Count);
        var corrupt = false;
        foreach (var record in snapshot.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var registration = TryCreate(record, cancellationToken);
            if (registration is null) corrupt = true;
            else registrations.Add(registration);
        }

        if (registrations.GroupBy(item => item.ProductId, StringComparer.Ordinal)
            .Any(group => group.Count() != 1))
            return new(GameLibrarySourceHealth.Degraded, []);

        return new(
            corrupt ? GameLibrarySourceHealth.Degraded : GameLibrarySourceHealth.Healthy,
            registrations.OrderBy(item => item.ProductId, StringComparer.Ordinal).ToArray());
    }

    private GogGameRegistration? TryCreate(
        GogRegistryRecord record,
        CancellationToken cancellationToken)
    {
        if (record.RegistryView is not (WindowsGogRegistryReader.Registry32 or
                WindowsGogRegistryReader.Registry64) ||
            !TryProductId(record.KeyName, record.GameId, out var productId) ||
            record.GameName is not string rawName ||
            WindowsAppLibraryProvider.SanitizeDisplayName(rawName) is not { } displayName ||
            record.InstallPath is not string rawInstall ||
            !TrySafeDirectory(rawInstall, out var installLocation)) return null;

        var infoPath = Path.Combine(installLocation, $"goggame-{productId}.info");
        if (!TryReadInfo(infoPath, productId, displayName, cancellationToken,
                out var infoHash, out var infoLength, out var infoWrite)) return null;

        var identity = "gog-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(productId))).ToLowerInvariant();
        var revision = "gog-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(record.RegistryView + "\0" + record.KeyName +
                "\0" + productId + "\0" + displayName + "\0" + installLocation +
                "\0" + infoHash + "\0" + infoLength.ToString(CultureInfo.InvariantCulture) +
                "\0" + infoWrite.Ticks.ToString(CultureInfo.InvariantCulture))));
        return new(identity, displayName, productId, revision);
    }

    private bool TryReadInfo(
        string path,
        string productId,
        string displayName,
        CancellationToken cancellationToken,
        out string contentHash,
        out long length,
        out DateTime write)
    {
        contentHash = string.Empty;
        length = 0;
        write = default;
        try
        {
            if (!IsSafeExistingFile(path) || !string.Equals(
                    Path.GetFileName(path), $"goggame-{productId}.info",
                    StringComparison.OrdinalIgnoreCase)) return false;
            var before = new FileInfo(path);
            if (before.Length is <= 0 or > MaximumInfoBytes) return false;
            length = before.Length;
            write = before.LastWriteTimeUtc;
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
                    if (read == 0) return false;
                    offset += read;
                }
                if (stream.ReadByte() != -1) return false;
            }
            _afterInfoRead?.Invoke(path);
            var after = new FileInfo(path);
            if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != write)
                return false;

            RejectDuplicateProperties(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryJsonString(root, "gameId", out var infoId) ||
                !string.Equals(infoId, productId, StringComparison.Ordinal) ||
                !TryJsonString(root, "rootGameId", out var rootId) ||
                !string.Equals(rootId, productId, StringComparison.Ordinal) ||
                !TryJsonString(root, "name", out var infoName) ||
                !string.Equals(
                    WindowsAppLibraryProvider.SanitizeDisplayName(infoName),
                    displayName, StringComparison.Ordinal)) return false;
            contentHash = Convert.ToHexString(SHA256.HashData(bytes));
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or
            UnauthorizedAccessException or InvalidOperationException or ArgumentException or
            NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool TryProductId(string keyName, object? value, out string productId)
    {
        productId = value switch
        {
            string text => text,
            int number when number >= 0 => number.ToString(CultureInfo.InvariantCulture),
            long number when number >= 0 => number.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty,
        };
        return ValidProductId(keyName) && ValidProductId(productId) &&
            string.Equals(keyName, productId, StringComparison.Ordinal);
    }

    private static bool ValidProductId(string value) =>
        value.Length is > 0 and <= 20 && value.All(char.IsAsciiDigit) &&
        (value.Length == 1 || value[0] != '0');

    private static bool TrySafeDirectory(string path, out string exact)
    {
        exact = string.Empty;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return false;
            var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (!Directory.Exists(candidate) || HasReparseAncestor(candidate)) return false;
            exact = candidate;
            return true;
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            return false;
        }
    }

    private static bool IsSafeExistingFile(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path) && File.Exists(path) &&
                !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) &&
                !HasReparseAncestor(Path.GetDirectoryName(Path.GetFullPath(path))!);
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            return false;
        }
    }

    private static bool HasReparseAncestor(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null)
        {
            if (current.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
            current = current.Parent;
        }
        return false;
    }

    private static bool TryJsonString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            property.GetString() is { Length: > 0 and <= 120 } candidate &&
            (value = candidate) is not null;
    }

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
                throw new JsonException("Duplicate info property.");
        }
    }

    private static bool IsSourceFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or InvalidOperationException or
        System.Security.SecurityException or ArgumentException or NotSupportedException;
}
