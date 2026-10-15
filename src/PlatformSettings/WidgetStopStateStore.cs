using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.PlatformSettings;

/// <summary>Host lifecycle state, kept separate from the rollback-compatible settings document.</summary>
public sealed class WidgetStopStateStore(PlatformSettingsPaths paths)
{
    private readonly string root = paths.RootDirectory;
    private readonly string path = Path.Combine(paths.RootDirectory, "stopped-widgets.json");
    private const int MaximumBytes = 48 * 1024;

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken token = default)
    {
        if (!File.Exists(path)) return Array.Empty<string>();
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(root);
        FileSystemGuard.RejectReparsePoint(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
        if (stream.Length > MaximumBytes) throw Invalid();
        var bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        try
        {
            StrictJson.RejectDuplicateProperties(bytes);
            var state = JsonSerializer.Deserialize(bytes, WidgetStopJsonContext.Default.WidgetStopDocument);
            if (state?.SchemaVersion != 1 || state.PackageIds is not { } ids || ids.Count > 256 ||
                ids.Any(id => !ThemeIdentity.IsValid(id)) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) throw Invalid();
            return ids.ToArray();
        }
        catch (JsonException) { throw Invalid(); }
    }

    public async Task<IReadOnlyList<string>> SetStoppedAsync(string packageId, bool stopped, CancellationToken token = default)
    {
        if (!ThemeIdentity.IsValid(packageId)) throw new ArgumentException("Invalid widget package ID.", nameof(packageId));
        Directory.CreateDirectory(root);
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(root);
        await using var held = await LockAsync(token).ConfigureAwait(false);
        var current = await LoadAsync(token).ConfigureAwait(false);
        var next = stopped ? current.Append(packageId).Distinct(StringComparer.Ordinal).ToArray() : current.Where(id => id != packageId).ToArray();
        if (next.SequenceEqual(current)) return current;
        if (next.Length > 256) throw Invalid();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new WidgetStopDocument(1, next), WidgetStopJsonContext.Default.WidgetStopDocument);
        if (bytes.Length > MaximumBytes) throw Invalid();
        var temporary = Path.Combine(root, $".stopped-widgets.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(bytes, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            FileSystemGuard.RejectReparsePoint(path);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return next;
    }

    private async Task<FileStream> LockAsync(CancellationToken token)
    {
        var lockPath = Path.Combine(root, ".stopped-widgets.lock");
        var deadline = Environment.TickCount64 + 10_000;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                FileSystemGuard.RejectReparsePoint(lockPath);
                return new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);
            }
            catch (IOException) when (Environment.TickCount64 < deadline) { await Task.Delay(40, token).ConfigureAwait(false); }
        }
    }

    private static PlatformSettingsException Invalid() => new("invalid_widget_stop_state", "Saved widget stop state is invalid.");
}

internal sealed record WidgetStopDocument(int SchemaVersion, IReadOnlyList<string> PackageIds);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(WidgetStopDocument))]
internal sealed partial class WidgetStopJsonContext : JsonSerializerContext;
