using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameBarAlternative.PlatformSettings;

/// <summary>
/// A bounded, package-and-publisher-scoped store for public widget configuration.
/// This store intentionally rejects secret values; credentials belong in the
/// platform credential vault rather than a readable JSON document.
/// </summary>
public sealed partial class WidgetConfigurationStore
{
    public const int MaximumEntries = 32;
    public const int MaximumKeyCharacters = 64;
    public const int MaximumValueCharacters = 4096;
    public const int MaximumDocumentBytes = 32 * 1024;
    public const int MaximumStoredDocuments = 256;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(40);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _root;
    private readonly string _lockFile;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WidgetConfigurationStore(PlatformSettingsPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _root = Path.GetFullPath(paths.WidgetConfigurationDirectory);
        _lockFile = Path.Combine(_root, ".widget-config.lock");
    }

    public async Task<WidgetConfigurationSnapshot> ReadAsync(
        string packageId,
        string publisherId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(packageId, publisherId);
        var path = PathFor(packageId, publisherId);
        if (!File.Exists(path)) return WidgetConfigurationSnapshot.Empty(packageId, publisherId);
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(_root);
        FileSystemGuard.RejectReparsePoint(path);
        var bytes = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
        try
        {
            StrictJson.RejectDuplicateProperties(bytes);
            var document = JsonSerializer.Deserialize<WidgetConfigurationDocument>(bytes, JsonOptions)
                ?? throw new JsonException("Widget configuration document was null.");
            ValidateDocument(document, packageId, publisherId);
            return new WidgetConfigurationSnapshot(
                document.PackageId,
                document.PublisherId,
                new Dictionary<string, string>(document.Values, StringComparer.Ordinal));
        }
        catch (JsonException exception)
        {
            throw new PlatformSettingsException(
                "invalid_widget_configuration",
                "Widget configuration JSON is invalid.",
                exception);
        }
    }

    /// <summary>
    /// Reads non-secret configuration for an authenticated runtime authority.
    /// Signed/trusted publishers use their exact authority. An unsigned
    /// content-digest authority may fall back to one unambiguous manifest-
    /// publisher document whose namespace owns the package ID. This keeps
    /// public user configuration across unsigned package rebuilds without
    /// weakening consent, private state, tokens, or credential isolation.
    /// </summary>
    public async Task<WidgetConfigurationSnapshot> ReadForRuntimeAuthorityAsync(
        string packageId,
        string runtimePublisherId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(packageId, runtimePublisherId);
        var exactPath = PathFor(packageId, runtimePublisherId);
        if (File.Exists(exactPath) ||
            !runtimePublisherId.StartsWith("unsigned.", StringComparison.Ordinal) ||
            !Directory.Exists(_root))
            return await ReadAsync(packageId, runtimePublisherId, cancellationToken)
                .ConfigureAwait(false);

        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(_root);
        var files = Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly)
            .Take(MaximumStoredDocuments + 1).ToArray();
        if (files.Length > MaximumStoredDocuments)
            throw new PlatformSettingsException(
                "too_many_widget_configurations",
                $"At most {MaximumStoredDocuments} widget configuration documents are supported.");

        WidgetConfigurationSnapshot? match = null;
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileSystemGuard.RejectReparsePoint(path);
            var bytes = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
            WidgetConfigurationDocument? document;
            try
            {
                StrictJson.RejectDuplicateProperties(bytes);
                document = JsonSerializer.Deserialize<WidgetConfigurationDocument>(bytes, JsonOptions);
                if (document is null) continue;
                ValidateIdentity(document.PackageId, document.PublisherId);
                ValidateDocument(document, document.PackageId, document.PublisherId);
            }
            catch (JsonException)
            {
                continue;
            }

            if (!string.Equals(document.PackageId, packageId, StringComparison.Ordinal) ||
                !packageId.StartsWith(document.PublisherId + ".", StringComparison.Ordinal))
                continue;
            if (match is not null)
                return WidgetConfigurationSnapshot.Empty(packageId, runtimePublisherId);
            match = new WidgetConfigurationSnapshot(
                document.PackageId,
                document.PublisherId,
                new Dictionary<string, string>(document.Values, StringComparer.Ordinal));
        }
        return match ?? WidgetConfigurationSnapshot.Empty(packageId, runtimePublisherId);
    }

    public Task<WidgetConfigurationSnapshot> SetAsync(
        string packageId,
        string publisherId,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateValue(value);
        return MutateAsync(packageId, publisherId, values =>
        {
            values[key] = value;
            return values;
        }, cancellationToken);
    }

    public Task<WidgetConfigurationSnapshot> RemoveAsync(
        string packageId,
        string publisherId,
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        return MutateAsync(packageId, publisherId, values =>
        {
            values.Remove(key);
            return values;
        }, cancellationToken);
    }

    public Task<WidgetConfigurationSnapshot> ClearAsync(
        string packageId,
        string publisherId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(packageId, publisherId, values =>
        {
            values.Clear();
            return values;
        }, cancellationToken);

    private async Task<WidgetConfigurationSnapshot> MutateAsync(
        string packageId,
        string publisherId,
        Func<Dictionary<string, string>, Dictionary<string, string>> mutation,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(packageId, publisherId);
        ArgumentNullException.ThrowIfNull(mutation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root);
            FileSystemGuard.EnsureExistingPathHasNoReparsePoints(_root);
            await using var crossProcess = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
            var current = await ReadAsync(packageId, publisherId, cancellationToken).ConfigureAwait(false);
            var values = mutation(new Dictionary<string, string>(current.Values, StringComparer.Ordinal))
                ?? throw new PlatformSettingsException(
                    "invalid_widget_configuration", "Widget configuration mutation returned null.");
            ValidateValues(values);
            var document = new WidgetConfigurationDocument(
                WidgetConfigurationDocument.CurrentSchemaVersion,
                packageId,
                publisherId,
                values);
            await SaveAtomicAsync(PathFor(packageId, publisherId), document, cancellationToken)
                .ConfigureAwait(false);
            return new WidgetConfigurationSnapshot(
                packageId,
                publisherId,
                new Dictionary<string, string>(values, StringComparer.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                FileSystemGuard.RejectReparsePoint(_lockFile);
                return new FileStream(
                    _lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    1, FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(LockRetry, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new PlatformSettingsException(
                    "settings_busy",
                    "Widget configuration is busy in another process. Retry shortly.",
                    exception);
            }
        }
    }

    private static async Task SaveAtomicAsync(
        string path,
        WidgetConfigurationDocument document,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > MaximumDocumentBytes)
            throw new PlatformSettingsException(
                "widget_configuration_too_large",
                $"Widget configuration exceeds {MaximumDocumentBytes} bytes.");
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                8 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string PathFor(string packageId, string publisherId)
    {
        var identityBytes = Encoding.UTF8.GetBytes($"{publisherId}\n{packageId}");
        var name = Convert.ToHexString(SHA256.HashData(identityBytes)).ToLowerInvariant();
        var path = Path.Combine(_root, name + ".json");
        if (!FileSystemGuard.IsWithin(_root, path))
            throw new PlatformSettingsException(
                "invalid_widget_configuration", "Widget configuration path is invalid.");
        return path;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            8 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumDocumentBytes)
            throw new PlatformSettingsException(
                "widget_configuration_too_large",
                $"Widget configuration exceeds {MaximumDocumentBytes} bytes.");
        using var output = new MemoryStream((int)stream.Length);
        await stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return output.ToArray();
    }

    private static void ValidateDocument(
        WidgetConfigurationDocument document,
        string packageId,
        string publisherId)
    {
        if (document.SchemaVersion != WidgetConfigurationDocument.CurrentSchemaVersion ||
            !string.Equals(document.PackageId, packageId, StringComparison.Ordinal) ||
            !string.Equals(document.PublisherId, publisherId, StringComparison.Ordinal))
            throw new JsonException("Widget configuration identity or schema is invalid.");
        ValidateValues(document.Values);
    }

    private static void ValidateValues(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count > MaximumEntries)
            throw new PlatformSettingsException(
                "invalid_widget_configuration",
                $"Widget configuration may contain at most {MaximumEntries} entries.");
        foreach (var (key, value) in values)
        {
            ValidateKey(key);
            ValidateValue(value);
        }
    }

    private static void ValidateIdentity(string packageId, string publisherId)
    {
        if (!PackageIdentity().IsMatch(packageId ?? string.Empty) ||
            !PackageIdentity().IsMatch(publisherId ?? string.Empty))
            throw new PlatformSettingsException(
                "invalid_widget_identity", "Widget package identity is invalid.");
    }

    private static void ValidateKey(string key)
    {
        if (key is null || key.Length is < 1 or > MaximumKeyCharacters || !ConfigurationKey().IsMatch(key))
            throw new PlatformSettingsException(
                "invalid_widget_configuration_key",
                "Widget configuration key is invalid.");
    }

    private static void ValidateValue(string value)
    {
        if (value is null || value.Length > MaximumValueCharacters ||
            value.Any(character => char.IsControl(character) && character is not '\t'))
            throw new PlatformSettingsException(
                "invalid_widget_configuration_value",
                "Widget configuration value is invalid.");
    }

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdentity();

    [GeneratedRegex("^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ConfigurationKey();
}

public sealed record WidgetConfigurationSnapshot(
    string PackageId,
    string PublisherId,
    IReadOnlyDictionary<string, string> Values)
{
    internal static WidgetConfigurationSnapshot Empty(string packageId, string publisherId) =>
        new(packageId, publisherId, new Dictionary<string, string>(StringComparer.Ordinal));
}

internal sealed record WidgetConfigurationDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string PackageId,
    [property: JsonRequired] string PublisherId,
    [property: JsonRequired] IReadOnlyDictionary<string, string> Values)
{
    public const int CurrentSchemaVersion = 1;
}
