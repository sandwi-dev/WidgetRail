using System.Text.Json;

namespace WidgetRail.OverlayFrontend.WinUI.Browser.Extensions;

internal sealed class ExtensionStore(string settingsRoot)
{
    internal string Root { get; } = Path.Combine(settingsRoot, "BrowserExtensions");
    private readonly SemaphoreSlim gate = new(1, 1);
    internal string Files(ExtensionInstallation install) => Path.Combine(SlotRoot(install.Slot), "files");
    internal string Files(ExtensionEntry entry) => Path.Combine(SlotRoot(entry.Slot), "files");
    internal string SlotRoot(string slot)
    {
        if (!Guid.TryParseExact(slot, "N", out _)) throw new InvalidDataException("Invalid extension storage slot.");
        return Path.Combine(Root, "packages", slot);
    }
    internal Task<ExtensionCatalog> ReadAsync(CancellationToken token) => BrowserExtensionCatalogFile.ReadAsync(settingsRoot, token);
    internal async Task UpdateAsync(Func<ExtensionCatalog, ExtensionCatalog> update, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var prior = await ReadAsync(token);
            var next = update(prior);
            if (ReferenceEquals(prior, next)) return;
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Root);
                var path = BrowserExtensionCatalogFile.PathFor(settingsRoot);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(next, ExtensionJsonContext.Default.ExtensionCatalog));
                File.Move(path + ".tmp", path, true);
            }, token);
        }
        finally { gate.Release(); }
    }
    internal Task RegisterAsync(ExtensionProfile profile, CancellationToken token) => UpdateAsync(catalog =>
        catalog.Profiles.Contains(profile) ? catalog : catalog with { Profiles = catalog.Profiles.Where(p => p.Name != profile.Name).Append(profile).ToArray() }, token);
    internal Task RemoveProfileAsync(string name, CancellationToken token) => UpdateAsync(catalog => catalog with
    {
        Profiles = catalog.Profiles.Where(profile => profile.Name != name).ToArray(),
        Library = catalog.Library.Select(entry => entry with
        {
            Overrides = entry.Overrides.Where(pair => pair.Key != name).ToDictionary(pair => pair.Key, pair => pair.Value),
            Bindings = entry.Bindings.Where(pair => pair.Key != name).ToDictionary(pair => pair.Key, pair => pair.Value),
        }).ToArray(),
    }, token);
    internal Task SaveAsync(ExtensionEntry entry, CancellationToken token) => UpdateAsync(catalog =>
        catalog with { Library = catalog.Library.Where(i => i.Slot != entry.Slot).Append(entry).ToArray() }, token);
    internal Task ForgetAsync(string slot, CancellationToken token) => UpdateAsync(catalog =>
        catalog with { Library = catalog.Library.Where(i => i.Slot != slot).ToArray() }, token);
}
