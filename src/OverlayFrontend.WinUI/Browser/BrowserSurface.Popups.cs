using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed record BrowserPopupRequest(BrowserPopupPlacement Placement, Func<BrowserSurface, Task<bool>>? Pin = null, Func<Task<bool>>? BeforeReplace = null);

internal sealed partial class BrowserSurface
{
    private readonly bool isPopup;
    private BrowserSurface? popup;
    private BrowserSurface? popupParent, preparingPopup;
    private bool popupDetached;
    private BrowserSurface? AttachedPopup => popupDetached ? null : popup;
    private Grid? popupLayer;
    private Task? popupClosing;
    private Task? popupOpening;
    private Task? queuedNewWindow;
    private CancellationTokenSource? queuedNewWindowCancellation;
    private bool newWindowQueued;
    private CancellationTokenSource? popupCancellation;
    private bool openingPopup;
    private bool popupPlacementReady;
    internal Func<BrowserSurface, bool>? ReservePopup { get; set; }
    internal Action<BrowserSurface>? ReleasePopup { get; set; }
    internal Action? CloseRequested { get; set; }
    internal Action<BrowserSurface>? PopupClosed { get; set; }
    internal Func<BrowserSurface, Task>? RequestPopupPin { get; set; }
    internal Func<BrowserSurface, bool>? PopupIsPinned { get; set; }
    internal Func<BrowserSurface, Grid, bool, CancellationToken, Task<CoreWebView2>>? PreparePopup { get; set; }
    internal Func<BrowserSurface, BrowserSurface?, string?, CancellationToken, Task<BrowserPopupRequest?>>? ChoosePopupPlacement { get; set; }
    internal Func<BrowserSurface, string, CancellationToken, Task<BrowserPopupPlacement>>? ReadPopupPlacement { get; set; }
    internal Func<BrowserSurface, string, BrowserPopupPlacement, CancellationToken, Task>? WritePopupPlacement { get; set; }
    internal bool HasPopup => popup is not null || openingPopup;
    internal bool HasAttachedPopup => AttachedPopup is not null;
    internal BrowserSurface ActiveSurface => AttachedPopup?.ActiveSurface ?? this;
    internal BrowserSurface? PinnablePopup => !retired && (!openingPopup || popupPlacementReady) && popupClosing is not { IsCompleted: false } && popup is { IsReady: true } child ? child : null;
    internal bool IsDetachedPopup(BrowserSurface child) => popupDetached && ReferenceEquals(popup, child);
    internal bool Owns(BrowserSurface source) => ReferenceEquals(this, source) || popup?.Owns(source) == true || ReferenceEquals(preparingPopup, source);
    internal IEnumerable<FrameworkElement> NativeCompositionRoots
    {
        get
        {
            yield return CompositionRoot;
            if (AttachedPopup is { } child)
                foreach (var root in child.NativeCompositionRoots) yield return root;
        }
    }

    internal Grid? DetachPopup(BrowserSurface child)
    {
        if (!ReferenceEquals(PinnablePopup, child) || popupDetached || popupLayer is null) return null;
        child.SetPresentation(false, false);
        popupDetached = true;
        popupLayer.Visibility = Visibility.Collapsed;
        SetPresentation(visible, presentationInputRequested);
        return popupLayer;
    }

    internal bool RestorePopup(BrowserSurface child)
    {
        if (retired || !IsDetachedPopup(child) || popupLayer is null || !child.IsReady) return false;
        popupDetached = false;
        popupLayer.Visibility = Visibility.Visible;
        child.IsInteractionFocused = () => IsInteractionFocused?.Invoke() == true;
        child.FocusInteraction = () => FocusInteraction?.Invoke();
        child.ExitInteraction = () => Run(ClosePopupAsync);
        child.InteractionChanged = () => InteractionChanged?.Invoke();
        SetPresentation(visible, presentationInputRequested);
        InteractionChanged?.Invoke();
        return true;
    }

    // Native website windows retain their opener contract. A host link action
    // has already captured and authorized its destination, and navigates normally.
    private sealed class NewWindowRequest
    {
        private readonly CoreWebView2NewWindowRequestedEventArgs? native;
        private Windows.Foundation.Deferral? deferral;
        internal NewWindowRequest(CoreWebView2NewWindowRequestedEventArgs args)
        { native = args; args.Handled = true; Url = args.Uri; IsUserInitiated = args.IsUserInitiated; }
        internal NewWindowRequest(string url, CancellationToken token)
        { Url = url; IsUserInitiated = true; HostLink = true; Token = token; }
        internal string Url { get; }
        internal bool IsUserInitiated { get; }
        internal bool HostLink { get; }
        internal CancellationToken Token { get; }
        internal void Defer() { if (native is not null) deferral ??= native.GetDeferral(); }
        internal void Bind(BrowserSurface child, CoreWebView2 childCore)
        {
            if (native is null) return;
            native.NewWindow = childCore;
            child.appliedNavigation = child.document.NavigationId;
        }
        internal void Complete()
        { var pending = deferral; deferral = null; pending?.Complete(); }
    }

    private void NewWindowRequested(CoreWebView2NewWindowRequestedEventArgs args) => (popupParent ?? this).AdmitNewWindow(this, new NewWindowRequest(args));

    private void AdmitNewWindow(BrowserSurface source, NewWindowRequest window)
    {
        var transferred = false;
        try
        {
            if (retired || source.retired || !window.IsUserInitiated || window.Token.IsCancellationRequested || !source.visible || source.faulted || source.profileChanging ||
                !window.HostLink && (!source.input || source.HasDialog) ||
                !ReferenceEquals(source, this) && !ReferenceEquals(source, popup))
            { TraceNewWindow("rejected-input-or-owner"); return; }
            var url = window.Url;
            if (source.IsProviderContent)
            { if (WebBrowserDocument.IsWebUrl(url)) source.Run(() => source.OpenProviderLinkAsync(url)); return; }
            if (url != WebBrowserDocument.StartPage && !WebBrowserDocument.IsWebUrl(url))
            { source.ShowStatus("This new window cannot open inside WidgetRail."); return; }
            if (document.Options.Popups != BrowserPopupMode.Hosted)
            {
                if (!WebBrowserDocument.IsWebUrl(url)) return;
                if (document.Options.Popups == BrowserPopupMode.SystemBrowser)
                { if (OpenExternal is { } open) Run(async () => { await open(new Uri(url), lifetime.Token); }); }
                else Run(() => NavigateRequestedAsync(url));
                return;
            }
            if (openingPopup || popupClosing is { IsCompleted: false })
            {
                if (!newWindowQueued && (popupPlacementReady || !openingPopup) && (openingPopup ? popupOpening : popupClosing) is { } previousOperation)
                {
                    window.Defer();
                    transferred = true;
                    QueueNewWindow(source, window, previousOperation);
                }
                else TraceNewWindow("rejected-busy");
                return;
            }
            var previous = popup;
            if (previous is null && ReservePopup?.Invoke(this) == false)
            { source.ShowStatus("Too many additional pages are open. Close another page and try again."); return; }
            ShowStatus(null);
            if (!ReferenceEquals(source, this)) source.ShowStatus(null);
            openingPopup = true;
            window.Defer();
            transferred = true;
            popupCancellation?.Dispose();
            popupCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var cancellation = popupCancellation;
            var expectedPage = pageEpoch;
            var sourcePage = source.pageEpoch;
            var origin = BrowserSiteIdentity.Origin(source.core?.Source ?? "", requireHttps: false);
            popupOpening = ObserveAsync(OpenPopupAsync);
            async Task OpenPopupAsync()
            {
                Grid? layer = null;
                Grid? previousLayer = null;
                BrowserSurface? child = null;
                var committed = false;
                try
                {
                    using var promptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token,
                        (source.siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(source.lifetime.Token)).Token);
                    window.Token.ThrowIfCancellationRequested();
                    if (!source.CanCompleteSiteDecision || sourcePage != source.pageEpoch) return;
                    BrowserPopupRequest? request = new(BrowserPopupPlacement.InWidget);
                    TraceNewWindow("placement-requested");
                    if (ChoosePopupPlacement is { } choose) request = await choose(source, previous, origin, promptCancellation.Token);
                    else if (previous is not null)
                    {
                        var answer = source.Choose is { } confirm ? await confirm(source, "Replace existing page?",
                            "This closes the existing additional page and ends its activity.", (Shell.HostDialogChoice[])[new("replace", "Replace page")], promptCancellation.Token) : null;
                        if (answer != "replace") return;
                    }
                    if (request is null || !Current()) { TraceNewWindow("placement-declined-or-stale"); return; }
                    var name = document.AccessibleName[..Math.Min(document.AccessibleName.Length, 240)] + " additional page";
                    child = new BrowserSurface(widgetId, new("popup." + Guid.NewGuid().ToString("N"), 1, url, name)
                    {
                        InteractionMode = BrowserInteractionMode.InteractOnFocus,
                        Options = document.Options with { Chrome = BrowserChrome.OnDemand, ExternalLinks = BrowserExternalLinkMode.InWidget, Background = BrowserBackgroundMode.Automatic },
                    }, environment, library: library, popup: true)
                    {
                        popupParent = this,
                        OpenExternal = OpenExternal, Choose = Choose, ChooseFiles = ChooseFiles, ChangeProfile = ChangeProfile, PrepareScreenPicker = PrepareScreenPicker,
                        ReceiveNotification = ReceiveNotification, InvalidateNotifications = InvalidateNotifications,
                        ReadPopupPlacement = ReadPopupPlacement, WritePopupPlacement = WritePopupPlacement,
                        RequestPopupPin = RequestPopupPin, PopupIsPinned = PopupIsPinned,
                        IsInteractionFocused = () => IsInteractionFocused?.Invoke() == true,
                        FocusInteraction = () => FocusInteraction?.Invoke(),
                        ExitInteraction = () => Run(ClosePopupAsync),
                        InteractionChanged = () => InteractionChanged?.Invoke(),
                        CloseRequested = () => DispatcherQueue.TryEnqueue(() =>
                        { if (ReferenceEquals(popup, child)) Run(ClosePopupAsync); else if (ReferenceEquals(preparingPopup, child)) cancellation.Cancel(); }),
                        Recover = async _ => { await ClosePopupAsync(); ShowStatus("The additional page closed. Retry the action to open it again."); },
                    };
                    preparingPopup = child;
                    layer = new Grid { Visibility = Visibility.Collapsed };
                    SetRowSpan(layer, RowDefinitions.Count); Children.Add(layer);
                    CoreWebView2 childCore;
                    if (PreparePopup is { } prepare) childCore = await prepare(child, layer, true, cancellation.Token);
                    else { layer.Children.Add(child); childCore = await child.PrepareCoreAsync(cancellation.Token); }
                    if (!Current()) return;
                    // No existing page is closed until its replacement controller is ready.
                    if (request.BeforeReplace is { } before && !await before()) return;
                    cancellation.Token.ThrowIfCancellationRequested();
                    window.Bind(child, childCore);
                    previousLayer = popupLayer;
                    previous?.SetPresentation(false, false);
                    popup = child; popupLayer = layer; preparingPopup = null; committed = true; popupPlacementReady = true;
                    var pinned = request.Placement == BrowserPopupPlacement.Pinned && request.Pin is not null && await request.Pin(child);
                    if (!pinned)
                    {
                        if (PreparePopup is { } place) await place(child, layer, false, cancellation.Token);
                        layer.Visibility = Visibility.Visible;
                        if (request.Placement == BrowserPopupPlacement.Pinned) ShowStatus("The page opened in the widget because pin placement was unavailable.");
                    }
                    TraceNewWindow(previous is null ? "opened" : "replaced");
                }
                catch (OperationCanceledException)
                {
                    if (committed) await ClosePopupAsync();
                    TraceNewWindow("cancelled");
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    if (committed && !retired && !popupDetached && child is { IsReady: true } && layer is not null)
                    {
                        try
                        {
                            if (PreparePopup is { } place) await place(child, layer, false, lifetime.Token);
                            layer.Visibility = Visibility.Visible;
                        }
                        catch (Exception fallback) when (fallback is not OutOfMemoryException) { await ClosePopupAsync(); }
                    }
                    if (!retired) { ShowStatus("The new window could not open. Try again or open the page externally."); Diagnostics.FrontendFailureLog.Current.Write("browser-new-window", error); }
                }
                finally
                {
                    // Place first, release native navigation second. Otherwise a
                    // newly loaded page can request permission while still hidden.
                    if (committed && !retired) SetPresentation(visible, presentationInputRequested);
                    try
                    {
                        if (committed && !retired && window.HostLink && child?.core is { } target)
                        {
                            child.appliedNavigation = child.document.NavigationId;
                            target.Navigate(url);
                        }
                        window.Complete();
                    }
                    catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException)
                    { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("browser-new-window-complete", error); }
                    if (committed && previous is not null)
                    {
                        // The requesting child must survive until its native
                        // deferral is completed. Its retirement cannot restore old
                        // focus, release the new reservation, or remove the new pin.
                        await previous.DisposeAsync();
                        if (previous.Parent is Panel oldHost) oldHost.Children.Remove(previous);
                        if (previousLayer is not null) { previousLayer.Children.Clear(); Children.Remove(previousLayer); }
                        PopupClosed?.Invoke(previous);
                    }
                    if (!committed)
                    {
                        if (child is not null) { await child.DisposeAsync(); if (child.Parent is Panel host) host.Children.Remove(child); }
                        if (layer is not null) Children.Remove(layer);
                    }
                    preparingPopup = null; openingPopup = false; popupPlacementReady = false;
                    if (popup is null) ReleasePopup?.Invoke(this);
                    if (!retired)
                    {
                        SetPresentation(visible, presentationInputRequested);
                        if (!popupDetached) popup?.Enter();
                        ActivityChanged?.Invoke();
                    }
                }
                bool Current() => !retired && expectedPage == pageEpoch && sourcePage == source.pageEpoch &&
                    !source.retired && ReferenceEquals(popup, previous) && !cancellation.IsCancellationRequested;
            }
        }
        finally { if (!transferred) window.Complete(); }
    }

    private void QueueNewWindow(BrowserSurface source, NewWindowRequest window, Task previousOperation)
    {
        var page = source.pageEpoch;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, source.lifetime.Token,
            (source.siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(source.lifetime.Token)).Token);
        queuedNewWindowCancellation = cancellation;
        newWindowQueued = true;
        queuedNewWindow = ObserveAsync(DeliverAsync);
        async Task DeliverAsync()
        {
            var transferred = false;
            try
            {
                await previousOperation.WaitAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (retired || source.retired || source.pageEpoch != page) return;
                // The original request already passed user/input admission. The
                // normal entry rechecks the current source and presentation.
                transferred = true;
                AdmitNewWindow(source, window);
            }
            catch (OperationCanceledException) { }
            finally
            {
                try { if (!transferred) window.Complete(); }
                finally
                {
                    newWindowQueued = false;
                    if (ReferenceEquals(queuedNewWindowCancellation, cancellation)) queuedNewWindowCancellation = null;
                    cancellation.Dispose();
                }
            }
        }
    }

    private long lastNewWindowRejection;
    private void TraceNewWindow(string stage)
    {
        if (stage.StartsWith("rejected", StringComparison.Ordinal))
        {
            var now = Environment.TickCount64;
            if (now - lastNewWindowRejection < 2000) return;
            lastNewWindowRejection = now;
        }
        Diagnostics.FrontendFailureLog.Current.Write("browser-new-window", null,
            $"widget={widgetId} stage={stage} input={input} dialog={HasDialog} opening={openingPopup}");
    }

    internal async Task<CoreWebView2> PrepareCoreAsync(CancellationToken token)
    {
        if (!IsLoaded)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Ready(object sender, RoutedEventArgs args) => loaded.TrySetResult();
            Loaded += Ready;
            try { if (!IsLoaded) await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10), token); }
            finally { Loaded -= Ready; }
        }
        initializing ??= InitializeAsync();
        await initializing.WaitAsync(TimeSpan.FromSeconds(15), token);
        return IsReady ? core! : throw new InvalidOperationException("Browser controller is unavailable.");
    }

    internal Task ClosePopupAsync()
    {
        queuedNewWindowCancellation?.Cancel();
        popupCancellation?.Cancel();
        if (popupClosing is { IsCompleted: false }) return popupClosing;
        if (popup is not { } child) { ReleasePopup?.Invoke(this); return Task.CompletedTask; }
        popupClosing = CloseAsync(child);
        return popupClosing;
    }

    private async Task CloseAsync(BrowserSurface child)
    {
        try { await child.DisposeAsync(); }
        finally
        {
            if (ReferenceEquals(popup, child))
            {
                popup = null;
                popupDetached = false;
                PopupClosed?.Invoke(child);
                if (child.Parent is Panel host) host.Children.Remove(child);
                if (popupLayer is { } layer) { layer.Children.Clear(); Children.Remove(layer); }
                popupLayer = null;
                ReleasePopup?.Invoke(this);
                SetPresentation(visible, presentationInputRequested);
                if (input) FocusInteraction?.Invoke();
                InteractionChanged?.Invoke(); ActivityChanged?.Invoke();
            }
        }
    }

    internal async Task DrainPopupAsync()
    {
        queuedNewWindowCancellation?.Cancel();
        if (queuedNewWindow is { } queued) await queued;
        popupCancellation?.Cancel();
        if (popupOpening is { } pending) await pending;
        await ClosePopupAsync();
        popupCancellation?.Dispose(); popupCancellation = null;
    }
}
