using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLibraryApplicationPaths(
    string Root,
    string StateFile,
    string SavedIdKeyFile,
    string SourceConfigurationFile,
    string LegacyPlatformSettingsFile)
{
    private const string RootOverride = "WRAIL_PLAYNITE_LIBRARY_DATA_ROOT";

    internal static PlayniteLibraryApplicationPaths CreateDefault()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(RootOverride);
        var local = string.IsNullOrWhiteSpace(overrideRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : overrideRoot;
        if (string.IsNullOrWhiteSpace(local) || !Path.IsPathFullyQualified(local))
            throw new InvalidOperationException("Playnite Library local state is unavailable.");
        var productRoot = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(local, "WidgetRail")
            : Path.GetFullPath(local);
        var root = Path.Combine(productRoot, "community-apps",
            "widgetrail.samples.playnite-library");
        return new(
            root,
            Path.Combine(root, "organization.json"),
            Path.Combine(root, "saved-id.key"),
            Path.Combine(root, "sources.json"),
            Path.Combine(productRoot, "platform-settings.json"));
    }
}

internal sealed record PlayniteLibrarySourceConfiguration(
    int SchemaVersion,
    bool EpicInstalledGamesEnabled,
    bool GogInstalledGamesEnabled)
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumBytes = 4 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    internal static async Task<PlayniteLibrarySourceConfiguration> LoadAsync(
        PlayniteLibraryApplicationPaths paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (File.Exists(paths.SourceConfigurationFile))
            return await ReadAsync(paths.SourceConfigurationFile, cancellationToken)
                .ConfigureAwait(false);

        var imported = await ImportLegacyAsync(
                paths.LegacyPlatformSettingsFile, cancellationToken)
            .ConfigureAwait(false);
        await WriteAtomicAsync(paths.SourceConfigurationFile, imported, cancellationToken)
            .ConfigureAwait(false);
        return imported;
    }

    private static async Task<PlayniteLibrarySourceConfiguration> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
        var value = JsonSerializer.Deserialize<PlayniteLibrarySourceConfiguration>(
            bytes, JsonOptions) ?? throw new InvalidDataException(
            "Playnite Library source configuration is invalid.");
        return value.SchemaVersion == CurrentSchemaVersion
            ? value
            : throw new InvalidDataException(
                "Playnite Library source configuration version is unsupported.");
    }

    private static async Task<PlayniteLibrarySourceConfiguration> ImportLegacyAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new(CurrentSchemaVersion, false, false);
        var bytes = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });
        if (!document.RootElement.TryGetProperty("appLibrary", out var library) ||
            library.ValueKind != JsonValueKind.Object)
            return new(CurrentSchemaVersion, false, false);
        return new(
            CurrentSchemaVersion,
            ReadBoolean(library, "epicInstalledGamesEnabled"),
            ReadBoolean(library, "gogInstalledGamesEnabled"));
    }

    private static bool ReadBoolean(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.True;

    private static async Task<byte[]> ReadBoundedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumBytes)
            throw new InvalidDataException("Playnite Library source configuration is invalid.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    private static async Task WriteAtomicAsync(
        string path,
        PlayniteLibrarySourceConfiguration value,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".sources.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another exact application generation completed the same first-run import.
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
