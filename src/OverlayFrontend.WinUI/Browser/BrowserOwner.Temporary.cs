using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    private const string SharedBrowserWidgetId = "widgetrail.samples.browser";
    private BrowserSurface? temporaryPage;
    private BrowserSlot? temporarySlot;
    private Task? temporaryClosing;

    internal bool IsTemporary(BrowserSurface source) => temporaryPage?.Owns(source) == true;
    private IEnumerable<BrowserSurface> ProfileRoots => entries.Values.Select(e => e.Surface)
        .Concat(temporaryPage is { } page ? new[] { page } : []);

    internal async Task<BrowserSurface> OpenTemporaryAsync(string url, BrowserStorage storage, WidgetPresentationAuthority source, CancellationToken token)
    {
        if (!WebBrowserDocument.IsWebUrl(url)) throw new ArgumentException("Invalid browser destination.", nameof(url));
        if (!Enum.IsDefined(storage)) throw new ArgumentException("Invalid browser storage.", nameof(storage));
        if (temporaryClosing is { } closing) await closing;
        if (retired || temporaryPage is not null) throw new InvalidOperationException("An overlay browser page is already open.");
        if (entries.Count + retiringEntries.Count >= 4) throw new InvalidOperationException("Stop another webapp before opening this page.");
        var document = new WebBrowserDocument("host.intent-page", 1, url, storage == BrowserStorage.Private ? "Private browser" : "Browser")
        {
            InteractionMode = BrowserInteractionMode.InteractOnFocus,
            Options = new() { Storage = storage, ProfileKey = storage == BrowserStorage.Persistent ? "default" : "temporary." + Guid.NewGuid().ToString("N"), Popups = BrowserPopupMode.Hosted },
        };
        var profileName = BrowserProfileIdentity.Name(SharedBrowserWidgetId, document);
        BrowserLibraryStore? library = null;
        if (storage == BrowserStorage.Persistent && !libraries.TryGetValue(profileName, out library))
        {
            library = await BrowserLibraryStore.LoadAsync(Path.Combine(profileRoot, "Library", BrowserProfileIdentity.LibraryFileName(SharedBrowserWidgetId, document)), token);
            libraries[profileName] = library;
        }
        token.ThrowIfCancellationRequested();
        if (retired) throw new OperationCanceledException();
        var page = CreateSurface(SharedBrowserWidgetId, document, source, library: library);
        // A temporary page is owned by the invoking surface, not by a package or background lease.
        page.RequestPopupPin = null;
        if (storage == BrowserStorage.Private) page.RegisterExtensionProfile = null;
        page.CloseRequested = () => ExpansionInvalidated?.Invoke();
        page.AddTemporaryPageChrome(storage);
        page.ChangeRunning = (_, _) => Task.FromResult(false);
        page.Recover = async _ => { ExpansionInvalidated?.Invoke(); await CloseTemporaryAsync(); };
        temporaryPage = page;
        parking.Children.Add(page); page.PlacementHost = parking;
        try { await page.PrepareCoreAsync(token); return page; }
        catch { await CloseTemporaryAsync(); throw; }
    }

    private async Task<bool> ExpandTemporaryAsync(BrowserSurface page, BrowserSlot slot)
    {
        if (retired || !ReferenceEquals(temporaryPage, page) || temporarySlot is not null || slot.Retired) return false;
        temporarySlot = slot;
        await MoveAsync(page, slot.Host, () => ReferenceEquals(temporaryPage, page) && !slot.Retired);
        if (retired || !ReferenceEquals(temporaryPage, page) || slot.Retired) return false;
        slot.SetSurface(page);
        return true;
    }

    internal Task CloseTemporaryAsync() => temporaryClosing is { IsCompleted: false } closing ? closing : temporaryClosing = CloseTemporaryCoreAsync();
    private async Task CloseTemporaryCoreAsync()
    {
        var page = temporaryPage; temporaryPage = null;
        temporarySlot?.SetSurface(null); temporarySlot = null;
        if (page is null) return;
        page.SetPresentation(false, false);
        await page.DisposeAsync();
        if (page.Parent is Panel parent) parent.Children.Remove(page);
        page.PlacementHost = null;
    }
}
