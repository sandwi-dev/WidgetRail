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
        internal WebSessionReference? Hosted;
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
    private readonly Dictionary<Task, string> retirements = [];
    internal Web.HostedWebViewEnvironment Runtime { get; }
    private Task? reconciliation;
    private bool dirty, retired;
    private readonly CancellationTokenSource lifetime = new();
    internal Func<Uri, CancellationToken, Task<bool>>? OpenExternal { get; set; }
    internal BrowserChoiceHandler? Choose { get; set; }
    internal Func<BrowserSurface, BrowserFileRequest, CancellationToken, Task<string[]>>? ChooseFiles { get; set; }
    internal Func<BrowserSurface, CancellationToken, Task<bool>>? PrepareScreenPicker { get; set; }
    internal Func<BrowserSurface, Task>? RequestPopupPin { get; set; }
    internal Func<BrowserSurface, BrowserSurface?, string?, CancellationToken, Task<BrowserPopupRequest?>>? ChoosePopupPlacement { get; set; }
    internal Func<BrowserSurface, string, CancellationToken, Task<BrowserPopupPlacement>>? ReadPopupPlacement { get; set; }
    internal Func<BrowserSurface, string, BrowserPopupPlacement, CancellationToken, Task>? WritePopupPlacement { get; set; }
    internal int ResidentCount => entries.Count;
    internal int CreatedCount { get; private set; }
    internal int PlacementMoveCount { get; private set; }
    internal bool OwnsNativeUiProcess(uint process) => !retired && entries.Values.Any(entry => entry.Surface.OwnsNativeUiProcess(process));

    internal BrowserOwner(Session session, Grid parking, string settingsRoot, NotificationCenter? notifications = null)
    {
        this.session = session; this.parking = parking; profileRoot = Path.Combine(settingsRoot, "WebBrowser");
        CoreWebView2EnvironmentOptions? environmentOptions = null;
#if ENABLE_WIDGET_VALIDATION
        if (FixtureBrowserArguments is { } arguments) environmentOptions = new() { AdditionalBrowserArguments = arguments };
#endif
        Runtime = new(profileRoot, environmentOptions);
        ownsNotifications = notifications is null;
        NotificationCenter = notifications ?? new(settingsRoot);
        notificationPreferencesLoading = NotificationCenter.Ready;
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
        if (retirements.Count != 0) await Task.WhenAll(retirements.Keys.ToArray());
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
    private Task<CoreWebView2Environment> EnvironmentAsync() => Runtime.GetAsync();
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
                    if (pair.Value.Hosted is null && !session.IsWebBrowserSessionCurrent(pair.Value.Authority, pair.Key.Document))
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
                    if (entries.TryGetValue(group.Key, out var hosted) && hosted.Hosted is { } reference)
                    {
                        if (chosen.Document.HostedSession != reference ||
                            (hosted.Authority.WorkerRun != chosen.Authority.WorkerRun &&
                             !await session.CheckWebSessionViewAsync(chosen.Authority, reference, lifetime.Token)))
                        { chosen.Slot.SetSurface(null); chosen.Slot.SetMessage("This web session belongs to a retired widget run."); continue; }
                        if (!session.IsWebBrowserSessionCurrent(chosen.Authority, chosen.Document.Id)) { dirty = true; continue; }
                    }
                    if (entries.TryGetValue(group.Key, out var existing) && existing.Hosted is null &&
                        (existing.Surface.ProviderReference != chosen.Document.ProviderDocument || !existing.Surface.UsesProfile(chosen.Document)))
                        await RetireAsync(group.Key, existing);
                    if (!entries.TryGetValue(group.Key, out var entry))
                    {
                        if (chosen.Document.HostedSession is not null) { chosen.Slot.SetMessage("This web session is unavailable. Reopen it from the widget."); continue; }
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
                        var surface = CreateSurface(group.Key.Widget, chosen.Document, chosen.Authority, providerHtml, library);
                        entry = new(chosen.Authority, surface); entries.Add(group.Key, entry); CreatedCount++;
                        // WinUI can move composition across XamlRoots while the
                        // native WebView retains its original creation HWND.
                        // Create on the durable host, never on a disposable pin.
                        parking.Children.Add(surface); surface.PlacementHost = parking;
                        try { await surface.PrepareCoreAsync(lifetime.Token); }
                        catch (InvalidOperationException) when (surface.NeedsRecovery) { /* The initialized error surface still needs placement. */ }
                        if (retired) break;
                    }
                    entry.LastUse = Environment.TickCount64;
                    if (entry.Hosted is not null) { entry.Authority = chosen.Authority; entry.Surface.ApplyHostedView(chosen.Document); }
                    else entry.Surface.Update(chosen.Document);
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
                        entry.Surface.SetPresentation(true, chosen.Slot.AcceptsInput,
                            passivePin: chosen.Slot.Pinned && !chosen.Slot.Expanded && !chosen.Slot.AcceptsInput);
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
    private BrowserSurface CreateSurface(string widgetId, WebBrowserDocument document,
        WidgetPresentationAuthority authority, IReadOnlyList<string>? providerHtml = null, BrowserLibraryStore? library = null)
    {
        return new BrowserSurface(widgetId, document, EnvironmentAsync, providerHtml, library)
        {
            OpenExternal = (uri, token) => OpenExternal?.Invoke(uri, token) ?? Task.FromResult(false),
            Choose = (source, title, message, choices, token, focusFirstChoice, preference) => Choose?.Invoke(source, title, message, choices, token, focusFirstChoice, preference) ?? Task.FromResult<string?>(null),
            ChangeProfile = ChangeProfileAsync,
            ReceiveNotification = ReceiveNotification,
            InvalidateNotifications = source => Notifications.InvalidateOwner(source),
            ChooseFiles = (source, request, token) => ChooseFiles?.Invoke(source, request, token) ?? Task.FromResult(Array.Empty<string>()),
            ReservePopup = ReservePopup,
            ReleasePopup = surface => popupOwners.Remove(surface),
            PopupClosed = PopupClosed,
            PreparePopup = PreparePopupAsync,
            ChoosePopupPlacement = ChoosePopupPlacement,
            ReadPopupPlacement = ReadPopupPlacement, WritePopupPlacement = WritePopupPlacement,
            RequestPopupPin = session.GetState(widgetId)?.LastGood?.Descriptor.FullWidgetPinningSupported == true && RequestPopupPin is not null
                ? source => RequestPopupPin?.Invoke(source) ?? Task.CompletedTask : null,
            PopupIsPinned = source => popupPresentation is { IsCurrent: true } popup && ReferenceEquals(popup.Child, source),
            ChangeRunning = ChangeRunningAsync,
            Recover = RecoverAsync,
            PrepareScreenPicker = (source, token) => PrepareScreenPicker?.Invoke(source, token) ?? Task.FromResult(false),
            ActivityChanged = () => { Refresh(); ActivityChanged?.Invoke(); },
            CheckProviderAuthority = document.ProviderDocument is null ? null : async token =>
                { _ = await session.ResolveProviderDocumentAsync(authority, document, token); },
        };
    }

    private async Task MoveAsync(BrowserSurface surface, Grid target, Func<bool>? stillCurrent = null)
    {
        if (stillCurrent?.Invoke() == false) return;
        if (target.Children.Contains(surface)) { surface.PlacementHost = target; return; }
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
            var previous = surface.Parent as Panel ?? Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(surface) as Panel ?? surface.PlacementHost;
            if (previous?.Children.Contains(surface) == true) previous.Children.Remove(surface);
            surface.PlacementHost = null;
            // A detached ancestor may already report a null XamlRoot while its
            // WebView is still unloading. Drain that native detach even within
            // one root; otherwise collapsing an expanded subtree can reattach
            // the controller while its previous composition target is retiring.
            // Attached pop-outs carry their own controller. All controllers in
            // this subtree must detach before any old peer can be destroyed.
            await Task.WhenAll(unloading.Values.Select(completion => completion.Task)).WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token);
            // Unloaded acknowledges XAML detachment, not submission of the old
            // composition tree. Commit that detach before the same native
            // visual target is attached to another root. Otherwise a passive
            // peer can remain transparent until an unrelated visual update.
            if (unloading.Count != 0)
                await Microsoft.UI.Xaml.Media.CompositionTarget.GetCompositorForCurrentThread().RequestCommitAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token);
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
            surface.PlacementHost = target;
            Loaded(target, null!);
            if (target.IsLoaded && !ReferenceEquals(target, parking))
                await Task.WhenAll(loading.Values.Select(completion => completion.Task)).WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token);
            if (target.IsLoaded && !ReferenceEquals(target, parking)) target.UpdateLayout();
        }
        finally { foreach (var root in native) root.Loaded -= Loaded; }
    }
    internal async Task RetireWidgetAsync(string widgetId)
    {
        foreach (var pair in entries.Where(pair => pair.Key.Widget == widgetId).ToArray()) _ = RetireAsync(pair.Key, pair.Value);
        await Task.WhenAll(retirements.Where(pair => pair.Value == widgetId).Select(pair => pair.Key).ToArray());
    }

    private Task RetireAsync((string Widget, string Document) key, Entry entry)
    {
        if (!ReferenceEquals(entries.GetValueOrDefault(key), entry) || !retiringEntries.Add(key)) return Task.CompletedTask;
        entries.Remove(key); entry.Slot?.SetSurface(null);
        var task = RetireCoreAsync(key, entry);
        foreach (var completed in retirements.Keys.Where(task => task.IsCompleted).ToArray()) retirements.Remove(completed);
        retirements.Add(task, key.Widget);
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
                if (entry.Hosted is { } reference && !retired)
                    await session.PublishWebSessionMessagesAsync(new(reference, [], "session_closed"), lifetime.Token);
                if (entry.Hosted is null && (entry.Retained || entry.RetentionAttempted) && session.IsWebBrowserSessionCurrent(entry.Authority, key.Document))
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
            var parent = entry.Surface.Parent as Panel ?? entry.Surface.PlacementHost;
            if (parent?.Children.Contains(entry.Surface) == true) parent.Children.Remove(entry.Surface);
            entry.Surface.PlacementHost = null;
        }
        finally { retiringEntries.Remove(key); if (!retired) parking.DispatcherQueue.TryEnqueue(Refresh); }
    }
    private Task? disposal;
    private readonly Task notificationPreferencesLoading;
    private readonly bool ownsNotifications;
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
        if (ownsNotifications) NotificationCenter.Dispose();
        foreach (var slot in slots.Keys) { slot.PlacementChanged = null; slot.SetSurface(null); } slots.Clear();
        if (hostedPump is not null) await hostedPump;
        if (hostedOperations.Count != 0) await Task.WhenAll(hostedOperations.ToArray());
        if (reconciliation is not null) await reconciliation;
        if (popupReconciliation is { } popupTransfer)
            try { await popupTransfer; } catch (OperationCanceledException) { }
        await popupPlacement.WaitAsync();
        try { foreach (var pair in entries.ToArray()) await RetireAsync(pair.Key, pair.Value); }
        finally { popupPlacement.Release(); }
        if (retirements.Count != 0) await Task.WhenAll(retirements.Keys.ToArray());
        if (hostedMessagePumps.Count != 0) await Task.WhenAll(hostedMessagePumps.Values.ToArray());
        lifetime.Dispose();
    }
}
