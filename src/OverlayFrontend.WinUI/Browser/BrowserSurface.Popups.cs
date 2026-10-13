using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private readonly bool isPopup;
    private BrowserSurface? popup;
    private bool popupDetached;
    private BrowserSurface? AttachedPopup => popupDetached ? null : popup;
    private Grid? popupLayer;
    private Task? popupClosing;
    private Task? popupOpening;
    private CancellationTokenSource? popupCancellation;
    private bool openingPopup;
    internal Func<BrowserSurface, bool>? ReservePopup { get; set; }
    internal Action<BrowserSurface>? ReleasePopup { get; set; }
    internal Action? CloseRequested { get; set; }
    internal Action<BrowserSurface>? PopupClosed { get; set; }
    internal Func<BrowserSurface, Task>? RequestPopupPin { get; set; }
    internal Func<BrowserSurface, bool>? PopupIsPinned { get; set; }
    internal Func<BrowserSurface, Grid, CancellationToken, Task<CoreWebView2>>? PreparePopup { get; set; }
    internal bool HasPopup => popup is not null || openingPopup;
    internal bool HasAttachedPopup => AttachedPopup is not null;
    internal BrowserSurface ActiveSurface => AttachedPopup?.ActiveSurface ?? this;
    internal BrowserSurface? PinnablePopup => !retired && !openingPopup && popupClosing is not { IsCompleted: false } && popup is { IsReady: true } child ? child : null;
    internal bool IsDetachedPopup(BrowserSurface child) => popupDetached && ReferenceEquals(popup, child);
    internal bool Owns(BrowserSurface source) => ReferenceEquals(this, source) || popup?.Owns(source) == true;
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

    private void NewWindowRequested(CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (retired || !input || !args.IsUserInitiated || HasDialog) return;
        var url = args.Uri;
        if (IsProviderContent)
        { if (WebBrowserDocument.IsWebUrl(url)) Run(() => OpenProviderLinkAsync(url)); return; }
        if (url != WebBrowserDocument.StartPage && !WebBrowserDocument.IsWebUrl(url))
        { ShowStatus("This popup cannot open inside WidgetRail."); return; }
        if (isPopup) { ShowStatus("Close this popup before opening another. You can also open the page externally."); return; }
        if (document.Options.Popups != BrowserPopupMode.Hosted)
        {
            if (!WebBrowserDocument.IsWebUrl(url)) return;
            if (document.Options.Popups == BrowserPopupMode.SystemBrowser)
            { if (OpenExternal is { } open) Run(async () => { await open(new Uri(url), lifetime.Token); }); }
            else Run(() => NavigateRequestedAsync(url));
            return;
        }
        if (HasPopup || ReservePopup?.Invoke(this) == false)
        { ShowStatus("Close an existing popup before opening another."); return; }
        openingPopup = true;
        var deferral = args.GetDeferral();
        popupCancellation?.Dispose();
        popupCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        popupOpening = ObserveAsync(OpenPopupAsync);
        async Task OpenPopupAsync()
        {
            var failed = false;
            try
            {
                var name = document.AccessibleName[..Math.Min(document.AccessibleName.Length, 250)] + " popup";
                var child = new BrowserSurface(widgetId, new("popup." + Guid.NewGuid().ToString("N"), 1, url, name)
                {
                    InteractionMode = BrowserInteractionMode.InteractOnFocus,
                    Options = document.Options with { Chrome = BrowserChrome.Toolbar, ExternalLinks = BrowserExternalLinkMode.InWidget, Background = BrowserBackgroundMode.Automatic },
                }, environment, library: library, popup: true)
                {
                    OpenExternal = OpenExternal, Choose = Choose, ChooseFiles = ChooseFiles, ChangeProfile = ChangeProfile, PrepareScreenPicker = PrepareScreenPicker,
                    ReceiveNotification = ReceiveNotification, InvalidateNotifications = InvalidateNotifications,
                    RequestPopupPin = RequestPopupPin, PopupIsPinned = PopupIsPinned,
                    IsInteractionFocused = () => IsInteractionFocused?.Invoke() == true,
                    FocusInteraction = () => FocusInteraction?.Invoke(),
                    ExitInteraction = () => Run(ClosePopupAsync),
                    InteractionChanged = () => InteractionChanged?.Invoke(),
                    CloseRequested = () => DispatcherQueue.TryEnqueue(() => Run(ClosePopupAsync)),
                    Recover = async _ => { await ClosePopupAsync(); ShowStatus("The popup closed. Retry the action on the page to open it again."); },
                };
                popup = child;
                popupLayer = new Grid();
                SetRowSpan(popupLayer, RowDefinitions.Count); Children.Add(popupLayer);
                CoreWebView2 childCore;
                if (PreparePopup is { } prepare) childCore = await prepare(child, popupLayer, popupCancellation.Token);
                else
                {
                    popupLayer.Children.Add(child);
                    childCore = await child.PrepareCoreAsync(popupCancellation.Token);
                }
                if (retired || !ReferenceEquals(popup, child)) return;
                args.NewWindow = childCore;
                child.appliedNavigation = child.document.NavigationId;
                SetPresentation(visible, presentationInputRequested);
                child.Enter();
                ActivityChanged?.Invoke();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failed = true;
                if (!retired && error is not OperationCanceledException) { ShowStatus("The popup could not open. Try again or open the page externally."); Diagnostics.FrontendFailureLog.Current.Write("browser-popup", error); }
            }
            finally
            {
                openingPopup = false;
                try { deferral.Complete(); }
                catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException)
                { failed = true; if (!retired) Diagnostics.FrontendFailureLog.Current.Write("browser-popup-complete", error); }
            }
            if (failed) await ClosePopupAsync();
        }
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
        popupCancellation?.Cancel();
        if (popupOpening is { } pending) await pending;
        await ClosePopupAsync();
        popupCancellation?.Dispose(); popupCancellation = null;
    }
}
