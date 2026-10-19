using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.PlatformSettings.BrowserExtensions;

namespace WidgetRail.PlatformSettings;

public sealed record BrowserProfileRecord(string Name, string? LibraryFile);
public sealed record BrowserOwnerData(string WidgetId, string Generation, bool Removed,
    string? LastReset, BrowserProfileRecord[] Profiles, BrowserProfileRecord[] Pending);
[JsonSerializable(typeof(BrowserOwnerData))]
internal sealed partial class BrowserOwnerDataJsonContext : JsonSerializerContext;

/// <summary>Durable, host-owned profile identities and exact deletion receipts. Updates never rotate identities.</summary>
public sealed class BrowserProfileLifecycleStore
{
    private readonly string settingsRoot;
    public string DirectoryPath { get; }
    public BrowserProfileLifecycleStore(string settingsRoot)
    {
        this.settingsRoot = Path.GetFullPath(settingsRoot);
        DirectoryPath = Path.Combine(this.settingsRoot, "browser-profile-lifecycle");
    }
    public static string OwnerKey(string widgetId)
    {
        if (string.IsNullOrEmpty(widgetId) || widgetId.Length > 128 || widgetId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))
            throw new ArgumentException("Invalid browser profile owner.", nameof(widgetId));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(widgetId)));
    }
    private string PathFor(string widgetId) => Path.Combine(DirectoryPath, OwnerKey(widgetId) + ".json");
    public async Task<BrowserOwnerData> ReadAsync(string widgetId, CancellationToken token = default)
    {
        var path = PathFor(widgetId);
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(path);
        if (!File.Exists(path)) return new(widgetId, "", false, null, [], []);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        if (stream.Length > 512 * 1024) throw new InvalidDataException("Browser profile ownership exceeds its bound.");
        var value = await JsonSerializer.DeserializeAsync(stream, BrowserOwnerDataJsonContext.Default.BrowserOwnerData, token).ConfigureAwait(false);
        if (value is null || value.WidgetId != widgetId || value.Generation is null ||
            value.Generation.Length != 0 && !Guid.TryParseExact(value.Generation, "N", out _) || value.Profiles is null || value.Pending is null ||
            value.Profiles.Length > 256 || value.Pending.Length > 1024)
            throw new InvalidDataException("Invalid browser profile ownership.");
        foreach (var profile in value.Profiles.Concat(value.Pending)) Validate(profile);
        return value;
    }
    public async Task<IReadOnlyList<BrowserOwnerData>> ReadAllAsync(CancellationToken token = default)
    {
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(DirectoryPath);
        if (!Directory.Exists(DirectoryPath)) return [];
        var files = Directory.EnumerateFiles(DirectoryPath, "*.json").Take(2049).ToArray();
        if (files.Length > 2048) throw new InvalidDataException("Too many browser profile owners.");
        var result = new List<BrowserOwnerData>();
        foreach (var path in files)
        {
            FileSystemGuard.RejectReparsePoint(path);
            if (new FileInfo(path).Length > 512 * 1024) throw new InvalidDataException("Browser profile ownership exceeds its bound.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
            var value = await JsonSerializer.DeserializeAsync(stream, BrowserOwnerDataJsonContext.Default.BrowserOwnerData, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Invalid browser profile ownership.");
            if (path != PathFor(value.WidgetId)) throw new InvalidDataException("Browser profile owner mismatch.");
            result.Add(await ReadAsync(value.WidgetId, token).ConfigureAwait(false));
        }
        return result;
    }
    public async Task<BrowserProfileRecord?> RegisterAsync(string widgetId, string baseName, string? legacyLibrary, CancellationToken token = default)
    {
        Validate(new(baseName, legacyLibrary));
        await using var lease = await LockAsync(widgetId, token).ConfigureAwait(false);
        var owner = await ReadAsync(widgetId, token).ConfigureAwait(false);
        if (owner.Removed) return null;
        if (baseName.StartsWith("private-", StringComparison.Ordinal)) return new(baseName, legacyLibrary);
        var name = owner.Generation.Length == 0 ? baseName : baseName[..baseName.IndexOf('-')] + "-" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(baseName + "\n" + owner.Generation)))[..48];
        var profile = new BrowserProfileRecord(name, legacyLibrary is null ? null : owner.Generation.Length == 0 ? legacyLibrary : name + ".json");
        if (!owner.Profiles.Contains(profile))
        {
            if (owner.Profiles.Length >= 256) throw new InvalidDataException("Too many browser profiles for one widget.");
            await WriteAsync(owner with { Profiles = [.. owner.Profiles, profile] }, token).ConfigureAwait(false);
        }
        return profile;
    }
    public async Task ResetAsync(string widgetId, string operationId, bool removed, CancellationToken token = default)
    {
        if (!Guid.TryParseExact(operationId, "N", out _)) throw new ArgumentException("Invalid data reset operation.");
        await using var lease = await LockAsync(widgetId, token).ConfigureAwait(false);
        var owner = await ReadAsync(widgetId, token).ConfigureAwait(false);
        if (owner.LastReset == operationId) return;
        // Existing profiles predate the ownership registry. Import their exact identities, never paths.
        var legacy = (await BrowserExtensionCatalogFile.ReadAsync(settingsRoot, token).ConfigureAwait(false)).Profiles
            .Where(p => p.WidgetId == widgetId && !p.Private).Select(p => new BrowserProfileRecord(p.Name, p.Name + ".json"));
        var pending = owner.Pending.Concat(owner.Profiles).Concat(legacy).Distinct().ToList();
        if (widgetId is "browser" or "widgetrail.samples.browser")
        {
            var profile = pending.FirstOrDefault();
            if (profile is not null) pending.Add(profile with { LibraryFile = OwnerKey(widgetId) + ".json" });
        }
        foreach (var profile in pending) Validate(profile);
        if (pending.Count > 1024) throw new InvalidDataException("Browser cleanup must finish before another reset.");
        await WriteAsync(owner with { Generation = Guid.NewGuid().ToString("N"), Removed = removed, LastReset = operationId,
            Profiles = [], Pending = pending.Distinct().ToArray() }, token).ConfigureAwait(false);
    }
    public async Task ActivateAsync(string widgetId, CancellationToken token = default)
    {
        if (!File.Exists(PathFor(widgetId))) return;
        await using var lease = await LockAsync(widgetId, token).ConfigureAwait(false);
        var owner = await ReadAsync(widgetId, token).ConfigureAwait(false);
        if (owner.Removed) await WriteAsync(owner with { Removed = false }, token).ConfigureAwait(false);
    }
    public async Task CompleteAsync(string widgetId, BrowserProfileRecord profile, CancellationToken token = default)
    {
        await using var lease = await LockAsync(widgetId, token).ConfigureAwait(false);
        var owner = await ReadAsync(widgetId, token).ConfigureAwait(false);
        if (owner.Pending.Contains(profile)) await WriteAsync(owner with { Pending = owner.Pending.Where(p => p != profile).ToArray() }, token).ConfigureAwait(false);
    }
    public async Task<bool> HasDataAsync(string widgetId, CancellationToken token = default) =>
        (await ReadAsync(widgetId, token).ConfigureAwait(false)).Profiles.Length > 0 ||
        (await BrowserExtensionCatalogFile.ReadAsync(settingsRoot, token).ConfigureAwait(false)).Profiles.Any(p => p.WidgetId == widgetId && !p.Private);
    private static void Validate(BrowserProfileRecord profile)
    {
        if (profile is null || string.IsNullOrEmpty(profile.Name) || profile.Name.Length > 128 ||
            !new[] { "persistent-", "provider-", "private-" }.Any(prefix => profile.Name.StartsWith(prefix, StringComparison.Ordinal)) ||
            profile.Name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
            profile.LibraryFile is { } library && (library.Length > 140 || !library.EndsWith(".json", StringComparison.Ordinal) ||
                library[..^5].Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new InvalidDataException("Invalid browser profile identity.");
    }
    private async Task<FileStream> LockAsync(string widgetId, CancellationToken token)
    {
        FileSystemGuard.EnsureExistingPathHasNoReparsePoints(DirectoryPath);
        Directory.CreateDirectory(DirectoryPath);
        var path = PathFor(widgetId) + ".lock";
        FileSystemGuard.RejectReparsePoint(path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(40, timeout.Token).ConfigureAwait(false); }
        }
    }
    private async Task WriteAsync(BrowserOwnerData owner, CancellationToken token)
    {
        var path = PathFor(owner.WidgetId); var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        FileSystemGuard.RejectReparsePoint(path);
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, owner, BrowserOwnerDataJsonContext.Default.BrowserOwnerData, token).ConfigureAwait(false);
                await file.FlushAsync(token).ConfigureAwait(false); file.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
