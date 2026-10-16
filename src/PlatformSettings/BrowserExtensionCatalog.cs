using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.PlatformSettings.BrowserExtensions;

public sealed record ExtensionManifest(string Name, string Version, int ManifestVersion, string? Key,
    string? OptionsPage, string[] Permissions, string[] Sites);
public sealed record ExtensionProfile(string Name, string WidgetId, string Label, bool Private);
public enum ExtensionOverride { Default, On, Off }
public sealed record ExtensionInstallation(string Slot, string Id, ExtensionManifest Manifest);
public sealed record ExtensionBinding(string Revision, string? Error = null);
public sealed record ExtensionEntry(string Slot, string Id, ExtensionManifest Manifest, string Revision,
    bool EnabledByDefault, Dictionary<string, ExtensionOverride> Overrides, Dictionary<string, ExtensionBinding> Bindings)
{
    public bool EnabledFor(string profile) => Overrides.GetValueOrDefault(profile) switch
    { ExtensionOverride.On => true, ExtensionOverride.Off => false, _ => EnabledByDefault };
    [JsonIgnore] public ExtensionInstallation Installation => new(Slot, Id, Manifest);
}
public sealed record ExtensionCatalog(ExtensionProfile[] Profiles, ExtensionEntry[] Library)
{
    public static ExtensionCatalog Empty => new([], []);
}
public sealed record ExtensionTransaction(ExtensionEntry? Previous, ExtensionEntry? Next, string[] Profiles);

[JsonSerializable(typeof(ExtensionCatalog))]
[JsonSerializable(typeof(ExtensionTransaction))]
public sealed partial class ExtensionJsonContext : JsonSerializerContext;

/// <summary>Shared, non-secret Settings read model. Native browser operations remain host-owned.</summary>
public static class BrowserExtensionCatalogFile
{
    public static string PathFor(string settingsRoot) => Path.Combine(settingsRoot, "BrowserExtensions", "catalog.json");
    public static async Task<ExtensionCatalog> ReadAsync(string settingsRoot, CancellationToken token = default)
    {
        var path = PathFor(settingsRoot);
        if (!File.Exists(path)) return ExtensionCatalog.Empty;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Extension catalog is too large.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        var catalog = await JsonSerializer.DeserializeAsync(file, ExtensionJsonContext.Default.ExtensionCatalog, token).ConfigureAwait(false);
        if (catalog?.Profiles is null || catalog.Library is null) throw new InvalidDataException("Extension catalog is invalid.");
        foreach (var entry in catalog.Library)
            if (!Guid.TryParseExact(entry.Slot, "N", out _) || entry.Overrides is null || entry.Bindings is null)
                throw new InvalidDataException("Extension catalog is invalid.");
        return catalog;
    }
}
