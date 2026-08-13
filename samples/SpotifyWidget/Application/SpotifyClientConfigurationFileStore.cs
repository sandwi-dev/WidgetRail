using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameBarAlternative.WindowsSpotifyProvider;

namespace GameBarAlternative.Samples.SpotifyWidget;

/// <summary>
/// Package-owned reader/writer for the existing package-scoped public client-ID
/// document. The format and identity stay unchanged so the full-trust cutover
/// preserves the user's configured OAuth application without moving secrets.
/// </summary>
internal sealed class SpotifyClientConfigurationFileStore(string root) :
    ISpotifyClientConfigurationStore
{
    internal const string ConfigurationRootEnvironmentVariable =
        "GBA_SPOTIFY_CONFIGURATION_ROOT";
    private const int MaximumDocumentBytes = 32 * 1024;
    private const string ClientIdKey = "client-id";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    private readonly string _root = Path.GetFullPath(root);
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal static SpotifyClientConfigurationFileStore CreateDefault()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(
            ConfigurationRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            if (!Path.IsPathFullyQualified(configuredRoot))
                throw new SpotifyApplicationException(
                    "invalid_configuration",
                    "Spotify configuration root must be an absolute path.");
            return new(configuredRoot);
        }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            throw new SpotifyApplicationException(
                "configuration_unavailable",
                "The current user's Local Application Data directory is unavailable.");
        return new(Path.Combine(local, "GameBarAlternative", "widget-config"));
    }

    public async Task<SpotifyClientConfiguration?> ReadAsync(
        SpotifyIntegrationIdentity identity,
        CancellationToken cancellationToken)
    {
        var path = PathFor(identity);
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            8 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumDocumentBytes)
            throw InvalidConfiguration("Spotify public configuration is too large.");
        var document = await JsonSerializer.DeserializeAsync<ConfigurationDocument>(
            stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw InvalidConfiguration("Spotify public configuration is empty.");
        ValidateDocument(document, identity);
        return document.Values.TryGetValue(ClientIdKey, out var clientId)
            ? new SpotifyClientConfiguration(clientId)
            : null;
    }

    public async Task WriteAsync(
        SpotifyIntegrationIdentity identity,
        SpotifyClientConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root);
            await using var crossProcess = await AcquireLockAsync(cancellationToken)
                .ConfigureAwait(false);
            var path = PathFor(identity);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (File.Exists(path))
            {
                await using var read = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    8 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (read.Length > MaximumDocumentBytes)
                    throw InvalidConfiguration("Spotify public configuration is too large.");
                var current = await JsonSerializer.DeserializeAsync<ConfigurationDocument>(
                    read, JsonOptions, cancellationToken).ConfigureAwait(false)
                    ?? throw InvalidConfiguration("Spotify public configuration is empty.");
                ValidateDocument(current, identity);
                foreach (var item in current.Values) values[item.Key] = item.Value;
            }
            values[ClientIdKey] = configuration.ClientId;
            var document = new ConfigurationDocument(
                1, identity.PackageId, identity.PublisherId, values);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            if (bytes.Length > MaximumDocumentBytes)
                throw InvalidConfiguration("Spotify public configuration is too large.");
            var temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken)
                    .ConfigureAwait(false);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(_root, ".widget-config.lock");
        var deadline = DateTimeOffset.UtcNow + LockTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new SpotifyApplicationException(
                    "configuration_busy",
                    "Spotify public configuration is busy. Retry shortly.", exception);
            }
        }
    }

    private string PathFor(SpotifyIntegrationIdentity identity)
    {
        var bytes = Encoding.UTF8.GetBytes($"{identity.PublisherId}\n{identity.PackageId}");
        try
        {
            var name = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return Path.Combine(_root, name + ".json");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void ValidateDocument(
        ConfigurationDocument document,
        SpotifyIntegrationIdentity identity)
    {
        if (document.SchemaVersion != 1 ||
            !string.Equals(document.PackageId, identity.PackageId, StringComparison.Ordinal) ||
            !string.Equals(document.PublisherId, identity.PublisherId, StringComparison.Ordinal) ||
            document.Values.Count > 32 ||
            document.Values.Any(item => item.Key.Length is < 1 or > 64 ||
                item.Value.Length > 4096 || item.Value.Any(char.IsControl)))
            throw InvalidConfiguration("Spotify public configuration is invalid.");
    }

    private static SpotifyApplicationException InvalidConfiguration(string message) =>
        new("invalid_configuration", message);

    private sealed record ConfigurationDocument(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string PackageId,
        [property: JsonRequired] string PublisherId,
        [property: JsonRequired] IReadOnlyDictionary<string, string> Values);
}
