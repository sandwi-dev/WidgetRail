using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.PlatformSettings;

public sealed class PlatformSettingsStore
{
    public const int MaximumSettingsBytes = 64 * 1024;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(40);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly PlatformSettingsPaths _paths;
    private readonly string _lockFile;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PlatformSettingsStore(PlatformSettingsPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _lockFile = Path.Combine(_paths.RootDirectory, ".platform-settings.lock");
    }

    public PlatformSettingsPaths Paths => _paths;

    public async Task<PlatformSettingsDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.SettingsFile)) return PlatformSettingsDocument.Default;
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(_paths.RootDirectory);
        FileSystemGuard.RejectReparsePoint(_paths.SettingsFile);
        var bytes = await ReadBoundedAsync(_paths.SettingsFile, MaximumSettingsBytes, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            StrictJson.RejectDuplicateProperties(bytes);
            var document = JsonSerializer.Deserialize<PlatformSettingsDocument>(bytes, JsonOptions)
                ?? throw new JsonException("Settings document was null.");
            Validate(document);
            return document;
        }
        catch (JsonException exception)
        {
            throw new PlatformSettingsException(
                "invalid_settings",
                $"Platform settings JSON is invalid: {SafeMessage(exception.Message)}",
                exception);
        }
    }

    public async Task<PlatformSettingsDocument> UpdateAsync(
        Func<PlatformSettingsDocument, PlatformSettingsDocument> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_paths.RootDirectory);
            FileSystemGuard.EnsureExistingPathHasNoReparsePoints(_paths.RootDirectory);
            await using var crossProcess = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
            var current = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var updated = mutation(current) ?? throw new PlatformSettingsException(
                "invalid_settings", "Settings mutation returned null.");
            Validate(updated);
            await SaveAtomicAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Atomically replaces the complete document without first reading the old
    /// one. This is the recovery path for a user-confirmed reset when the
    /// existing file is malformed.
    /// </summary>
    public async Task<PlatformSettingsDocument> ReplaceAsync(
        PlatformSettingsDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_paths.RootDirectory);
            FileSystemGuard.EnsureExistingPathHasNoReparsePoints(_paths.RootDirectory);
            await using var crossProcess = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
            await SaveAtomicAsync(document, cancellationToken).ConfigureAwait(false);
            return document;
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
                    "Platform settings are busy in another process. Retry after the other operation finishes.",
                    exception);
            }
        }
    }

    private async Task SaveAtomicAsync(
        PlatformSettingsDocument document,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > MaximumSettingsBytes)
            throw new PlatformSettingsException(
                "settings_too_large", $"Platform settings exceed {MaximumSettingsBytes} bytes.");
        var temporary = Path.Combine(
            _paths.RootDirectory,
            $".platform-settings.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _paths.SettingsFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(PlatformSettingsDocument document)
    {
        var errors = PlatformSettingsValidator.Validate(document);
        if (errors.Count != 0)
            throw new PlatformSettingsException(
                errors[0].Code, $"{errors[0].Path}: {errors[0].Message}");
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var output = new MemoryStream(Math.Min(maximumBytes, 16 * 1024));
        var buffer = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total = checked(total + read);
            if (total > maximumBytes)
                throw new PlatformSettingsException(
                    "settings_too_large", $"Platform settings exceed {maximumBytes} bytes.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static string SafeMessage(string value)
    {
        var oneLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= 512 ? oneLine : oneLine[..512];
    }
}
