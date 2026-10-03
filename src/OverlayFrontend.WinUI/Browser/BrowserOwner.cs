using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using Session = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>Owns browser/controller lifetime; presenters borrow slots without recreating pages.</summary>
internal sealed class BrowserOwner : IAsyncDisposable
{
    private sealed record Binding(WidgetPresentationAuthority Authority, WebBrowserDocument Document, BrowserSlot Slot);
    private sealed class Entry(WidgetPresentationAuthority authority, BrowserSurface surface)
    {
        internal WidgetPresentationAuthority Authority = authority;
        internal BrowserSurface Surface = surface;
        internal BrowserSlot? Slot;
        internal long LastUse;
    }
    private readonly Session session;
    private readonly Grid parking;
    private readonly string profileRoot;
    private readonly Dictionary<string, BrowserLibraryStore> libraries = new(StringComparer.Ordinal);
    private readonly Dictionary<BrowserSlot, Binding> slots = [];
    private readonly Dictionary<(string Widget, string Document), Entry> entries = [];
    private Task<CoreWebView2Environment>? environment;
    private Task? reconciliation;
    private bool dirty, retired;
    private readonly CancellationTokenSource lifetime = new();
    internal Func<Uri, CancellationToken, Task<bool>>? OpenExternal { get; set; }
    internal int ResidentCount => entries.Count;
    internal int CreatedCount { get; private set; }
    internal int PlacementMoveCount { get; private set; }

    internal BrowserOwner(Session session, Grid parking, string settingsRoot)
    {
        this.session = session; this.parking = parking; profileRoot = Path.Combine(settingsRoot, "WebBrowser");
        session.PresentationChanged += Changed; session.CatalogChanged += CatalogChanged;
    }
    internal void Bind(WidgetPresentationFrame frame, string elementId, BrowserSlot slot,
        WidgetPinnedSelection? selection, WidgetPinnedProjection? projection)
    {
        if (retired || slot.Retired) return;
        try
        {
            var document = session.ResolveWebBrowserDocument(frame, elementId, selection, projection);
            slots[slot] = new(frame.Authority, document, slot);
            slot.Pinned = selection is not null; slot.PlacementChanged = Refresh;
            Refresh();
        }
        catch (WidgetPresentationSessionException)
        { Unbind(slot); slot.SetMessage("Waiting for the current browser page…"); }
    }
    internal void Unbind(BrowserSlot slot)
    { slot.Surface?.SetPresentation(false, false); slots.Remove(slot); slot.PlacementChanged = null; slot.SetSurface(null); Refresh(); }
    // A presenter must not destroy its XamlRoot while a borrowed WebView is
    // still detaching from it. Unbind schedules placement; disposal joins it.
    internal async Task ReleaseSlotsAsync(IReadOnlyList<BrowserSlot> released)
    {
        if (released.Count == 0 || retired) return;
        while (reconciliation is { } pending)
        {
            await pending;
            if (ReferenceEquals(pending, reconciliation)) break;
        }
        // If a native transfer failed before detachment, close that controller
        // before the old window goes away rather than retain a poisoned handle.
        foreach (var pair in entries.ToArray())
            if (released.Any(slot => ReferenceEquals(pair.Value.Slot, slot) || ReferenceEquals(pair.Value.Surface.Parent, slot.Host)))
                await RetireAsync(pair.Key, pair.Value);
    }
#if ENABLE_WIDGET_VALIDATION
    internal Func<Task>? BeforeAttachForValidation { get; set; }
#endif
    internal void Refresh()
    {
        if (retired) return;
        dirty = true;
        if (reconciliation is null || reconciliation.IsCompleted) reconciliation = ReconcileAsync();
    }
    private void Changed(object? sender, WidgetPresentationChangedEventArgs args) => parking.DispatcherQueue.TryEnqueue(Refresh);
    private void CatalogChanged(object? sender, EventArgs args) => parking.DispatcherQueue.TryEnqueue(Refresh);
    private async Task<CoreWebView2Environment> EnvironmentAsync()
    {
        environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profileRoot, null).AsTask();
        try { return await environment; }
        catch { environment = null; throw; }
    }
    private async Task ReconcileAsync()
    {
        try
        {
            while (dirty && !retired)
            {
                dirty = false;
                foreach (var pair in entries.ToArray())
                    if (!session.IsWebBrowserSessionCurrent(pair.Value.Authority, pair.Key.Document))
                        await RetireAsync(pair.Key, pair.Value);
                foreach (var group in slots.Values.Where(binding => binding.Slot.CanDisplay &&
                    session.IsWebBrowserSessionCurrent(binding.Authority, binding.Document.Id)).ToArray()
                    .GroupBy(binding => (Widget: binding.Authority.WidgetId, Document: binding.Document.Id)))
                {
                    // A pin owns the durable page until it is removed/hidden.
                    // Input readiness changes while entering a widget and must
                    // never shuttle that page between XAML roots.
                    var chosen = group.OrderByDescending(binding => binding.Slot.Pinned).ThenByDescending(binding => binding.Slot.AcceptsInput).First();
                    if (entries.TryGetValue(group.Key, out var existing) && existing.Surface.ProviderReference != chosen.Document.ProviderDocument)
                        await RetireAsync(group.Key, existing);
                    if (!entries.TryGetValue(group.Key, out var entry))
                    {
                        if (entries.Count >= 4)
                        {
                            var oldest = entries.Where(pair => pair.Value.Slot?.CanDisplay != true).OrderBy(pair => pair.Value.LastUse).FirstOrDefault();
                            if (oldest.Value is null) { chosen.Slot.SetMessage("Close another browser page before opening this one."); continue; }
                            await RetireAsync(oldest.Key, oldest.Value);
                        }
                        IReadOnlyList<string>? providerHtml = null;
                        if (chosen.Document.ProviderDocument is not null)
                        {
                            var resolveStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                            try { providerHtml = (await session.ResolveProviderDocumentAsync(chosen.Authority, chosen.Document, lifetime.Token)).Html; }
                            catch (Exception error) when (error is not OutOfMemoryException)
                            { chosen.Slot.SetMessage("This document is unavailable. Refresh the widget to try again."); continue; }
                            Diagnostics.FrontendFailureLog.Current.Write("provider-resolve", null,
                                FormattableString.Invariant($"widget={group.Key.Widget} elapsedMs={System.Diagnostics.Stopwatch.GetElapsedTime(resolveStarted).TotalMilliseconds:F1}"));
                            if (retired || slots.GetValueOrDefault(chosen.Slot)?.Document != chosen.Document ||
                                !session.IsWebBrowserSessionCurrent(chosen.Authority, chosen.Document.Id)) { dirty = true; continue; }
                        }
                        BrowserLibraryStore? library = null;
                        if (chosen.Document.ProviderDocument is null && !libraries.TryGetValue(group.Key.Widget, out library))
                        {
                            var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(group.Key.Widget)));
                            library = await BrowserLibraryStore.LoadAsync(Path.Combine(profileRoot, "Library", key + ".json"), lifetime.Token);
                            if (retired) break;
                            libraries[group.Key.Widget] = library;
                            if (slots.GetValueOrDefault(chosen.Slot)?.Document != chosen.Document ||
                                !session.IsWebBrowserSessionCurrent(chosen.Authority, chosen.Document.Id)) { dirty = true; continue; }
                        }
                        var surface = new BrowserSurface(group.Key.Widget, chosen.Document, EnvironmentAsync, providerHtml, library)
                        {
                            OpenExternal = (uri, token) => OpenExternal?.Invoke(uri, token) ?? Task.FromResult(false),
                            CheckProviderAuthority = chosen.Document.ProviderDocument is null ? null : async token =>
                                { _ = await session.ResolveProviderDocumentAsync(chosen.Authority, chosen.Document, token); },
                        };
                        entry = new(chosen.Authority, surface); entries.Add(group.Key, entry); CreatedCount++;
                    }
                    entry.LastUse = Environment.TickCount64;
                    entry.Surface.Update(chosen.Document);
                    if (!ReferenceEquals(entry.Slot, chosen.Slot) || !ReferenceEquals(entry.Surface.Parent, chosen.Slot.Host))
                    {
                        entry.Slot?.SetSurface(null);
                        entry.Surface.SetPresentation(false, false);
                        await MoveAsync(entry.Surface, chosen.Slot.Host);
                        if (retired) break;
                        entry.Slot = chosen.Slot;
                    }
                    var current = slots.GetValueOrDefault(chosen.Slot);
                    if (current is not null && current.Slot.CanDisplay && session.IsWebBrowserSessionCurrent(current.Authority, current.Document.Id))
                    {
                        chosen.Slot.SetSurface(entry.Surface);
                        entry.Surface.SetPresentation(true, chosen.Slot.AcceptsInput);
                    }
                    else { entry.Surface.SetPresentation(false, false); dirty = true; }
                    foreach (var other in group.Where(binding => !ReferenceEquals(binding.Slot, chosen.Slot)))
                    {
                        other.Slot.SetSurface(null);
                        other.Slot.SetBorrowed(chosen.Slot.Pinned);
                    }
                }
                foreach (var entry in entries.Values)
                    if (entry.Slot is not { } slot || !slots.ContainsKey(slot) || !slot.CanDisplay)
                    {
                        entry.Surface.SetPresentation(false, false); entry.Slot?.SetSurface(null); entry.Slot = null;
                        await MoveAsync(entry.Surface, parking);
                    }
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("browser-placement", error); }
    }
    private async Task MoveAsync(BrowserSurface surface, Grid target)
    {
        if (ReferenceEquals(surface.Parent, target)) return;
        PlacementMoveCount++;
        var native = surface.CompositionRoot;
        var acrossRoots = surface.XamlRoot is { } root && target.XamlRoot is { } next && !ReferenceEquals(root, next);
        var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Unloaded(object sender, RoutedEventArgs args) { if (!native.IsLoaded) unloaded.TrySetResult(); }
        native.Unloaded += Unloaded;
        try
        {
            var wasLoaded = native.IsLoaded;
            if (surface.Parent is Panel previous) previous.Children.Remove(surface);
            // As with the media surface, allow WebView2's Unloaded to disconnect
            // its composition target before binding it to a different XAML root.
            if (acrossRoots && wasLoaded) await unloaded.Task.WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token);
        }
        finally { native.Unloaded -= Unloaded; }
        if (retired) return;
#if ENABLE_WIDGET_VALIDATION
        if (BeforeAttachForValidation is { } wait) await wait();
        if (retired) return;
#endif
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded(object sender, RoutedEventArgs args) { if (native.IsLoaded && ReferenceEquals(native.XamlRoot, target.XamlRoot)) loaded.TrySetResult(); }
        native.Loaded += Loaded;
        try
        {
            target.Children.Add(surface);
            if (acrossRoots && target.IsLoaded && !ReferenceEquals(target, parking) && !native.IsLoaded)
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token);
        }
        finally { native.Loaded -= Loaded; }
    }
    private async Task RetireAsync((string Widget, string Document) key, Entry entry)
    {
        entries.Remove(key); entry.Slot?.SetSurface(null);
        await entry.Surface.DisposeAsync();
        if (!entries.Keys.Any(other => other.Widget == key.Widget) && libraries.Remove(key.Widget, out var library))
            await library.DrainAsync();
        if (entry.Surface.Parent is Panel parent) parent.Children.Remove(entry.Surface);
    }
    public async ValueTask DisposeAsync()
    {
        if (retired) return; retired = true; lifetime.Cancel();
        session.PresentationChanged -= Changed; session.CatalogChanged -= CatalogChanged;
        foreach (var slot in slots.Keys) { slot.PlacementChanged = null; slot.SetSurface(null); } slots.Clear();
        if (reconciliation is not null) await reconciliation;
        foreach (var pair in entries.ToArray()) await RetireAsync(pair.Key, pair.Value);
        lifetime.Dispose();
    }
}
