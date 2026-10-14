using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using Session = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>Owns browser/controller lifetime; presenters borrow slots without recreating pages.</summary>
internal sealed partial class BrowserOwner : IAsyncDisposable
{
    private sealed record Binding(WidgetPresentationAuthority Authority, WebBrowserDocument Document, BrowserSlot Slot);
    private sealed class Entry(WidgetPresentationAuthority authority, BrowserSurface surface)
    {
        internal WidgetPresentationAuthority Authority = authority;
        internal BrowserSurface Surface = surface;
        internal BrowserSlot? Slot;
        internal long LastUse;
        internal bool Retained;
        internal bool RetentionAttempted;
        internal readonly SemaphoreSlim SessionGate = new(1, 1);
    }
    private readonly Session session;
    private readonly Grid parking;
    private readonly string profileRoot;
    private readonly Dictionary<string, BrowserLibraryStore> libraries = new(StringComparer.Ordinal);
    private readonly Dictionary<BrowserSlot, Binding> slots = [];
    private readonly Dictionary<(string Widget, string Document), Entry> entries = [];
    private readonly HashSet<(string Widget, string Document)> retiringEntries = [];
    private readonly HashSet<Task> retirements = [];
    private Task<CoreWebView2Environment>? environment;
    private Task? reconciliation;
    private bool dirty, retired;
    private readonly CancellationTokenSource lifetime = new();
    internal Func<Uri, CancellationToken, Task<bool>>? OpenExternal { get; set; }
    internal Func<BrowserSurface, string, string, IReadOnlyList<Shell.HostDialogChoice>, CancellationToken, Task<string?>>? Choose { get; set; }
    internal Func<BrowserSurface, BrowserFileRequest, CancellationToken, Task<string[]>>? ChooseFiles { get; set; }
    internal Func<BrowserSurface, CancellationToken, Task<bool>>? PrepareScreenPicker { get; set; }
    internal Func<BrowserSurface, Task>? RequestPopupPin { get; set; }
    internal Func<BrowserSurface, string?, CancellationToken, Task<BrowserPopupRequest?>>? ChoosePopupPlacement { get; set; }
    internal Func<BrowserSurface, string, CancellationToken, Task>? ManagePopupPlacement { get; set; }
    internal int ResidentCount => entries.Count;
    internal int CreatedCount { get; private set; }
    internal int PlacementMoveCount { get; private set; }
    internal bool OwnsNativeUiProcess(uint process) => !retired && entries.Values.Any(entry => entry.Surface.OwnsNativeUiProcess(process));

    internal BrowserOwner(Session session, Grid parking, string settingsRoot)
    {
        this.session = session; this.parking = parking; profileRoot = Path.Combine(settingsRoot, "WebBrowser");
        NotificationPreferences = new(Path.Combine(settingsRoot, "webapp-notifications.json"));
        notificationPreferencesLoading = LoadNotificationPreferencesAsync();
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
        if (popupReconciliation is { } popupTransfer)
        {
            try { await popupTransfer; }
            finally { if (ReferenceEquals(popupReconciliation, popupTransfer)) popupReconciliation = null; }
        }
        while (reconciliation is { } pending)
        {
            await pending;
            if (ReferenceEquals(pending, reconciliation)) break;
        }
        if (retirements.Count != 0) await Task.WhenAll(retirements.ToArray());
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
        if (reconciliation is null || reconciliation.IsCompleted) reconciliation = ReconcileAfterLayoutAsync();
    }
    private async Task ReconcileAfterLayoutAsync()
    {
        // Loaded/Unloaded can fire inside a XAML child-collection mutation.
        // Never reparent WebView2 reentrantly from those callbacks.
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!parking.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => ready.TrySetResult())) return;
        await ready.Task;
        if (!retired) await ReconcileAsync();
    }
    private void Changed(object? sender, WidgetPresentationChangedEventArgs args) => parking.DispatcherQueue.TryEnqueue(Refresh);
    private void CatalogChanged(object? sender, EventArgs args) => parking.DispatcherQueue.TryEnqueue(Refresh);
    private async Task<CoreWebView2Environment> EnvironmentAsync()
    {
        CoreWebView2EnvironmentOptions? options = null;
#if ENABLE_WIDGET_VALIDATION
        if (FixtureBrowserArguments is { } arguments) options = new() { AdditionalBrowserArguments = arguments };
#endif
        environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profileRoot, options).AsTask();
        try { return await environment; }
        catch { environment = null; throw; }
    }
#if ENABLE_WIDGET_VALIDATION
    internal static string? FixtureBrowserArguments { get; set; }
#endif
    private async Task ReconcileAsync()
    {
        try
        {
            while (dirty && !retired)
            {
                dirty = false;
                SynchronizeExpandedBinding();
                foreach (var pair in entries.ToArray())
                {
                    if (!session.IsWebBrowserSessionCurrent(pair.Value.Authority, pair.Key.Document))
                        await RetireAsync(pair.Key, pair.Value);
                    else if (pair.Value.Retained && !pair.Value.Surface.KeepsBackgroundSession)
                        await SynchronizeRetentionAsync(pair.Key, pair.Value);
                }
                foreach (var group in slots.Values.Where(binding => binding.Slot.CanDisplay &&
                    session.IsWebBrowserSessionCurrent(binding.Authority, binding.Document.Id)).ToArray()
                    .GroupBy(binding => (Widget: binding.Authority.WidgetId, Document: binding.Document.Id)))
                {
                    // A pin owns the durable page until it is removed/hidden.
                    // Input readiness changes while entering a widget and must
                    // never shuttle that page between XAML roots.
                    var chosen = group.OrderByDescending(binding => binding.Slot.Expanded).ThenByDescending(binding => binding.Slot.Pinned).ThenByDescending(binding => binding.Slot.AcceptsInput).First();
                    if (entries.TryGetValue(group.Key, out var existing) &&
                        (existing.Surface.ProviderReference != chosen.Document.ProviderDocument || !existing.Surface.UsesProfile(chosen.Document)))
                        await RetireAsync(group.Key, existing);
                    if (!entries.TryGetValue(group.Key, out var entry))
                    {
                        if (retiringEntries.Contains(group.Key)) continue;
                        if (changingProfiles.Contains(BrowserProfileIdentity.Name(group.Key.Widget, chosen.Document))) continue;
                        if (entries.Count + retiringEntries.Count >= 4)
                        {
                            var oldest = entries.Where(pair => !pair.Value.Retained && !pair.Value.Surface.BackgroundFaulted && pair.Value.Slot?.CanDisplay != true &&
                                !(popupPresentation is { IsCurrent: true } popup && ReferenceEquals(popup.Parent, pair.Value.Surface))).OrderBy(pair => pair.Value.LastUse).FirstOrDefault();
                            if (oldest.Value is null) { chosen.Slot.SetMessage(retiringEntries.Count > 0 ? "Closing the previous browser session…" : "Stop another webapp session before opening this one."); continue; }
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
                        var profileName = BrowserProfileIdentity.Name(group.Key.Widget, chosen.Document);
                        if (chosen.Document.ProviderDocument is null && !libraries.TryGetValue(profileName, out library))
                        {
                            library = await BrowserLibraryStore.LoadAsync(Path.Combine(profileRoot, "Library", BrowserProfileIdentity.LibraryFileName(group.Key.Widget, chosen.Document)), lifetime.Token);
                            if (retired) break;
                            libraries[profileName] = library;
                            if (slots.GetValueOrDefault(chosen.Slot)?.Document != chosen.Document ||
                                !session.IsWebBrowserSessionCurrent(chosen.Authority, chosen.Document.Id)) { dirty = true; continue; }
                        }
                        var surface = new BrowserSurface(group.Key.Widget, chosen.Document, EnvironmentAsync, providerHtml, library)
                        {
                            OpenExternal = (uri, token) => OpenExternal?.Invoke(uri, token) ?? Task.FromResult(false),
                            Choose = (source, title, message, choices, token) => Choose?.Invoke(source, title, message, choices, token) ?? Task.FromResult<string?>(null),
                            ChangeProfile = ChangeProfileAsync,
                            ReceiveNotification = ReceiveNotification,
                            InvalidateNotifications = source => Notifications.InvalidateOwner(source),
                            ChooseFiles = (source, request, token) => ChooseFiles?.Invoke(source, request, token) ?? Task.FromResult(Array.Empty<string>()),
                            ReservePopup = ReservePopup,
                            ReleasePopup = surface => popupOwners.Remove(surface),
                            PopupClosed = PopupClosed,
                            PreparePopup = PreparePopupAsync,
                            ChoosePopupPlacement = ChoosePopupPlacement,
                            ManagePopupPlacement = ManagePopupPlacement,
                            RequestPopupPin = session.GetState(group.Key.Widget)?.LastGood?.Descriptor.FullWidgetPinningSupported == true && RequestPopupPin is not null
                                ? source => RequestPopupPin?.Invoke(source) ?? Task.CompletedTask : null,
                            PopupIsPinned = source => popupPresentation is { IsCurrent: true } popup && ReferenceEquals(popup.Child, source),
                            ChangeRunning = ChangeRunningAsync,
                            Recover = RecoverAsync,
                            PrepareScreenPicker = (source, token) => PrepareScreenPicker?.Invoke(source, token) ?? Task.FromResult(false),
                            ActivityChanged = () => { Refresh(); ActivityChanged?.Invoke(); },
                            CheckProviderAuthority = chosen.Document.ProviderDocument is null ? null : async token =>
                                { _ = await session.ResolveProviderDocumentAsync(chosen.Authority, chosen.Document, token); },
                        };
                        entry = new(chosen.Authority, surface); entries.Add(group.Key, entry); CreatedCount++;
                        // WinUI can move composition across XamlRoots while the
                        // native WebView retains its original creation HWND.
                        // Create on the durable host, never on a disposable pin.
                        parking.Children.Add(surface);
                        try { await surface.PrepareCoreAsync(lifetime.Token); }
                        catch (InvalidOperationException) when (surface.NeedsRecovery) { /* The initialized error surface still needs placement. */ }
                        if (retired) break;
                    }
                    entry.LastUse = Environment.TickCount64;
                    entry.Surface.Update(chosen.Document);
                    if (!await SynchronizeRetentionAsync(group.Key, entry))
                    {
                        chosen.Slot.SetMessage("A background webapp cannot start right now. Stop another webapp session and try again.");
                        await RetireAsync(group.Key, entry);
                        continue;
                    }
                    if (!ReferenceEquals(entries.GetValueOrDefault(group.Key), entry)) continue;
                    if (!ReferenceEquals(entry.Slot, chosen.Slot) || !ReferenceEquals(entry.Surface.Parent, chosen.Slot.Host))
                    {
                        var expanding = chosen.Slot.Expanded || entry.Slot?.Expanded == true;
                        entry.Slot?.SetSurface(null);
                        // A visible placement transfer must not cancel the
                        // screen-sharing consent that requested expansion.
                        entry.Surface.SetPresentation(expanding && entry.Surface.CanCompleteSiteDecision, false);
                        await MoveAsync(entry.Surface, chosen.Slot.Host);
                        if (retired) break;
                        entry.Slot = chosen.Slot;
                    }
                    var current = slots.GetValueOrDefault(chosen.Slot);
                    if (ReferenceEquals(entries.GetValueOrDefault(group.Key), entry) && current is not null && current.Slot.CanDisplay && session.IsWebBrowserSessionCurrent(current.Authority, current.Document.Id))
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
    private async Task MoveAsync(BrowserSurface surface, Grid target, Func<bool>? stillCurrent = null)
    {
        if (stillCurrent?.Invoke() == false) return;
        if (ReferenceEquals(surface.Parent, target)) return;
        PlacementMoveCount++;
        var native = surface.NativeCompositionRoots.ToArray();
        var unloading = native.Where(root => root.IsLoaded).ToDictionary(root => root,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        void Unloaded(object sender, RoutedEventArgs args)
        {
            foreach (var (root, completion) in unloading)
                if (!root.IsLoaded) completion.TrySetResult();
        }
        foreach (var root in unloading.Keys) root.Unloaded += Unloaded;
        try
        {
            if (surface.Parent is Panel previous) previous.Children.Remove(surface);
            // A detached ancestor may already report a null XamlRoot while its
            // WebView is still unloading. Drain that native detach even within
            // one root; otherwise collapsing an expanded subtree can reattach
            // the controller while its previous composition target is retiring.
            // Attached pop-outs carry their own controller. All controllers in
            // this subtree must detach before any old peer can be destroyed.
            await Task.WhenAll(unloading.Values.Select(completion => completion.Task)).WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token);
        }
        finally { foreach (var root in unloading.Keys) root.Unloaded -= Unloaded; }
        if (retired || stillCurrent?.Invoke() == false) return;
#if ENABLE_WIDGET_VALIDATION
        if (BeforeAttachForValidation is { } wait) await wait();
        if (retired || stillCurrent?.Invoke() == false) return;
#endif
        var loading = native.ToDictionary(root => root, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        void Loaded(object sender, RoutedEventArgs args)
        {
            foreach (var (root, completion) in loading)
                if (root.IsLoaded && ReferenceEquals(root.XamlRoot, target.XamlRoot)) completion.TrySetResult();
        }
        foreach (var root in native) root.Loaded += Loaded;
        try
        {
            target.Children.Add(surface);
            Loaded(target, null!);
            if (target.IsLoaded && !ReferenceEquals(target, parking))
                await Task.WhenAll(loading.Values.Select(completion => completion.Task)).WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token);
            if (target.IsLoaded && !ReferenceEquals(target, parking)) target.UpdateLayout();
        }
        finally { foreach (var root in native) root.Loaded -= Loaded; }
    }
    private Task RetireAsync((string Widget, string Document) key, Entry entry)
    {
        if (!ReferenceEquals(entries.GetValueOrDefault(key), entry) || !retiringEntries.Add(key)) return Task.CompletedTask;
        entries.Remove(key); entry.Slot?.SetSurface(null);
        var task = RetireCoreAsync(key, entry);
        retirements.RemoveWhere(retirement => retirement.IsCompleted);
        retirements.Add(task);
        return task;
    }

    private async Task RetireCoreAsync((string Widget, string Document) key, Entry entry)
    {
        try
        {
            await entry.SessionGate.WaitAsync();
            try
            {
                await entry.Surface.DisposeAsync();
                if (entry.Retained || entry.RetentionAttempted)
                {
                    try
                    {
                        using var release = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await session.SetWebBrowserRetentionAsync(entry.Authority, key.Document, false, release.Token);
                    }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("browser-retention-release", error); }
                }
            }
            finally { entry.SessionGate.Release(); }
            ActivityChanged?.Invoke();
            if (!entries.Values.Any(other => other.Surface.ProfileName == entry.Surface.ProfileName) && libraries.Remove(entry.Surface.ProfileName, out var library))
                await library.DrainAsync();
            if (entry.Surface.Parent is Panel parent) parent.Children.Remove(entry.Surface);
        }
        finally { retiringEntries.Remove(key); if (!retired) parking.DispatcherQueue.TryEnqueue(Refresh); }
    }
    private Task? disposal;
    private readonly Task notificationPreferencesLoading;
    internal void BeginShutdown()
    {
        if (retired) return; retired = true; lifetime.Cancel();
        session.PresentationChanged -= Changed; session.CatalogChanged -= CatalogChanged;
    }
    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());
    private async Task DisposeCoreAsync()
    {
        BeginShutdown();
        try { await notificationPreferencesLoading; } catch (OperationCanceledException) { }
        Notifications.Dispose(); NotificationBannerRequested = null;
        foreach (var slot in slots.Keys) { slot.PlacementChanged = null; slot.SetSurface(null); } slots.Clear();
        if (reconciliation is not null) await reconciliation;
        if (popupReconciliation is { } popupTransfer)
            try { await popupTransfer; } catch (OperationCanceledException) { }
        await popupPlacement.WaitAsync();
        try { foreach (var pair in entries.ToArray()) await RetireAsync(pair.Key, pair.Value); }
        finally { popupPlacement.Release(); }
        if (retirements.Count != 0) await Task.WhenAll(retirements.ToArray());
        lifetime.Dispose();
    }
}
