using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>One durable browser. Widget code receives neither the CoreWebView2 nor page data.</summary>
internal sealed partial class BrowserSurface : Grid, IAsyncDisposable
{
    private readonly WebView2 web = new() { IsTabStop = false, XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Disabled };
    private readonly Grid viewport = new();
    private readonly TextBlock welcome = new() { Text = "Search Google or open a URL, bookmark, or recent page.\nPress LS for the toolbar, or Y to search or enter an address.",
        TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Margin = new(32), MaxWidth = 460, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(12, 8, 12, 8), Visibility = Visibility.Collapsed };
    private readonly TextBlock address = new() { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly Border cursor = new() { Width = 16, Height = 16, CornerRadius = new(8), BorderThickness = new(2),
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    private readonly List<Button> buttons = [];
    private readonly Func<Task<CoreWebView2Environment>> environment;
    private readonly string profile;
    private readonly string widgetId;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim pointer = new(1, 1);
    private Task? initializing;
    private CoreWebView2? core;
    private WebBrowserDocument document;
    private long appliedNavigation;
    private bool retired, visible, input, browsing, interacting, pointerPump, faulted;
    private double cursorX = .5, cursorY = .5, wheelX, wheelY;
    private bool cursorDirty;
    internal bool IsInteracting => interacting;
    internal bool IsBrowsing => browsing;
    internal bool IsReady => core is not null && !retired && !faulted;
    internal bool IsNavigationInProgress => loading;
    internal bool IsToolbarVisible => header.Visibility == Visibility.Visible;
    internal double EnvironmentInitializationMilliseconds { get; private set; }
    internal double ControllerInitializationMilliseconds { get; private set; }
    internal double DocumentNavigationMilliseconds { get; private set; }
    private long navigationStarted;
    internal object? CaptureZoomGesture(ControllerButton button) => input && browsing && !HasDialog && !loading &&
        button is ControllerButton.LeftTrigger or ControllerButton.RightTrigger ? (this, pageEpoch, editGeneration, button) : null;
    internal string? CurrentUrl => core?.Source;
    internal string? CurrentTitle => core?.DocumentTitle;
    internal FrameworkElement CompositionRoot => web;
    internal int NavigationCount { get; private set; }
    internal Func<Uri, CancellationToken, Task<bool>>? OpenExternal { get; set; }
    internal Action? ExitInteraction { get; set; }
    internal Action? FocusInteraction { get; set; }
    internal Func<bool>? IsInteractionFocused { get; set; }
    internal bool RequiresActivation => document.InteractionMode == BrowserInteractionMode.ActivateToInteract;
    private bool backReleaseOwned;
    internal Action? InteractionChanged { get; set; }
#if ENABLE_WIDGET_VALIDATION || ENABLE_TRIMMED_CONTROL_VALIDATION
    internal static Func<string, string?>? FixturePage { get; set; }
    internal Presentation.WidgetTextEntryDialog? FixtureEditor => editDialog;
    internal bool FixtureZoomIdle => !zoomPumping;
    internal void NavigateForValidation(string url) => core!.Navigate(url);
    internal BrowserLibraryDialog? FixtureLibrary => libraryDialog;
    internal BrowserLibraryStore? FixtureLibraryStore => library;
    internal void CloseControllerForValidation() => web.Close();
    internal List<string> FixturePointerTrace { get; } = [];
    internal async Task<string> EvaluateFixtureAsync(string script) => await core!.ExecuteScriptAsync(script);
#endif
    [System.Diagnostics.Conditional("ENABLE_WIDGET_VALIDATION")]
    [System.Diagnostics.Conditional("ENABLE_TRIMMED_CONTROL_VALIDATION")]
    private void TracePointer(string value)
    {
#if ENABLE_WIDGET_VALIDATION || ENABLE_TRIMMED_CONTROL_VALIDATION
        if (FixturePointerTrace.Count < 200) FixturePointerTrace.Add(value);
#endif
    }

    internal BrowserSurface(string widgetId, WebBrowserDocument document, Func<Task<CoreWebView2Environment>> environment, IReadOnlyList<string>? providerHtml = null, BrowserLibraryStore? library = null)
    {
        this.library = library;
        this.widgetId = widgetId;
        this.document = document; this.environment = environment; this.providerHtml = providerHtml;
        profile = "browser-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(widgetId)))[..32];
        CreateChrome();
        AutomationProperties.SetName(web, document.AccessibleName);
        AutomationProperties.SetAutomationId(web, "Browser.WebView");
        Loaded += (_, _) => { AttachTheme(); if (visible && initializing is null) initializing = InitializeAsync(); };
        Unloaded += (_, _) => { chromeTheme?.Dispose(); chromeTheme = null; };
        viewport.SizeChanged += (_, args) =>
        { DrawCursor(); if (Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > 1) Run(RefreshProviderHeightAsync); };
        web.GotFocus += (_, _) => { if (input && !HasDialog) { toolbarFocused = false; interacting = true; browsing = true; UseDesktopPointer(); InteractionChanged?.Invoke(); } };
        web.PointerMoved += (_, args) =>
        {
            if (input && args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
                UseDesktopPointer();
        };
        web.PointerPressed += (_, _) =>
        {
            if (!input) return;
            UseDesktopPointer();
        };
        SetPresentation(false, false);
    }

    internal void Update(WebBrowserDocument next)
    {
        if (retired || next.ProviderDocument != document.ProviderDocument || next.Id != document.Id || !next.IsWellFormed() || next.NavigationId < document.NavigationId) return;
        if (next.NavigationId == document.NavigationId && next.Url != document.Url)
        { ShowStatus("The browser navigation changed without a new request."); return; }
        if (document.InteractionMode != next.InteractionMode)
        { CancelPointer(); interacting = browsing = false; }
        document = next;
        AutomationProperties.SetName(web, document.AccessibleName);
        NavigatePending();
        RefreshInteractionFocus();
    }

    internal void SetPresentation(bool isVisible, bool acceptsInput)
    {
        if (retired) return;
        var wasInteracting = interacting;
        visible = isVisible && !retired; input = visible && acceptsInput && !faulted;
        web.IsTabStop = input;
        foreach (var button in buttons) { button.IsTabStop = input; button.IsHitTestVisible = input; }
        if (!input) { toolbarFocused = false; libraryDialog?.Hide(); backReleaseOwned = false; CancelPointer(); CancelEdit(); interacting = false; browsing = false; wheelX = wheelY = 0; cursorDirty = false; }
        DrawCursor();
        try
        {
            if (!faulted)
            {
                web.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                web.IsHitTestVisible = input;
                if (visible && IsLoaded && initializing is null) initializing = InitializeAsync();
                if (visible) { core?.Resume(); NavigatePending(); }
            }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            faulted = true; input = false; interacting = browsing = false;
            ++pointerEpoch; pointerPressed = nativePointerDown = false; pointerCore = null; dragData = null;
            core = null;
            try { web.Close(); }
            catch (Exception closeError) when (closeError is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
            CancelEdit(); DrawCursor(); UpdateChrome();
            ShowStatus("The browser could not resume. Use Reload widget to reopen it.");
            Diagnostics.FrontendFailureLog.Current.Write("browser-presentation", error);
        }
        RefreshInteractionFocus();
        if (wasInteracting != interacting) InteractionChanged?.Invoke();
    }

    internal void RefreshInteractionFocus()
    {
        if (retired || HasDialog) return;
        var before = interacting;
        if (!input || IsInteractionFocused?.Invoke() != true)
        {
            if (interacting) CancelPointer();
            interacting = browsing = false;
        }
        else if (!RequiresActivation && IsReady) { interacting = true; browsing = !toolbarFocused; }
        DrawCursor();
        if (before != interacting) InteractionChanged?.Invoke();
    }

    internal void Enter()
    {
        if (!input) return;
        if (core is null || faulted || HasDialog) return;
        toolbarFocused = false; interacting = browsing = true;
        controllerPointerVisible = true;
        FocusInteraction?.Invoke();
        DrawCursor();
        InteractionChanged?.Invoke();
    }
    internal bool HandleButton(ControllerButton button, ControllerEventPhase phase)
    {
        if (button == ControllerButton.A && chromeOwnedA)
        { if (phase == ControllerEventPhase.Released) chromeOwnedA = false; return true; }
        if (button == ControllerButton.B && backReleaseOwned)
        { if (phase == ControllerEventPhase.Released) backReleaseOwned = false; return true; }
        if (!input || !interacting) return false;
        if (!HasDialog && phase == ControllerEventPhase.Pressed) UseControllerInput();
        if (editPending && button == ControllerButton.B && phase == ControllerEventPhase.Pressed)
        { CancelEdit(); DrawCursor(); InteractionChanged?.Invoke(); return true; }
        if (libraryDialog is { } libraryChoice)
        {
            if (button == ControllerButton.B && phase == ControllerEventPhase.Pressed) backReleaseOwned = true;
            libraryChoice.Handle(button, phase); return true;
        }
        if (editDialog is { } dialog) { dialog.Handle(button, phase); return true; }
        if (HasDialog) return true;
        if (button == ControllerButton.LeftStick && !IsProviderContent)
        { if (phase == ControllerEventPhase.Pressed) ToggleToolbar(); return true; }
        if (toolbarFocused && button == ControllerButton.A)
        {
            if (phase == ControllerEventPhase.Pressed)
            { chromeOwnedA = true; if (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is Button focused && focused.IsEnabled && chromeCommands.TryGetValue(focused, out var command)) command(); }
            return true;
        }
        if (button == ControllerButton.A)
        {
            if (phase == ControllerEventPhase.Pressed) PressPointer();
            else if (phase == ControllerEventPhase.Released) ReleasePointer();
            return true;
        }
        if (button == ControllerButton.B && !RequiresActivation) return false;
        if (phase == ControllerEventPhase.Released || phase == ControllerEventPhase.Repeated && button is not (ControllerButton.LeftTrigger or ControllerButton.RightTrigger)) return true;
        if (IsProviderContent && button is not (ControllerButton.B or ControllerButton.LeftTrigger or ControllerButton.RightTrigger)) return true;
        if (button == ControllerButton.B)
        {
            backReleaseOwned = true;
            CancelPointer(); interacting = browsing = false; DrawCursor(); ExitInteraction?.Invoke();
            InteractionChanged?.Invoke();
        }
        else if (button == ControllerButton.LeftBumper && core?.CanGoBack == true) core.GoBack();
        else if (button == ControllerButton.RightBumper && core?.CanGoForward == true) core.GoForward();
        else if (button == ControllerButton.X) Reload();
        else if (button == ControllerButton.Y) Run(() => EditAsync(addressEntry: true));
        else if (button == ControllerButton.RightStick) Run(() => EditAsync(addressEntry: false));
        else if (button == ControllerButton.Menu) Run(OpenExternallyAsync);
        else if (button == ControllerButton.LeftTrigger) ChangeZoom(-.1);
        else if (button == ControllerButton.RightTrigger) ChangeZoom(.1);
        return true;
    }
    internal bool MoveFocus(FocusNavigationDirection direction)
    {
        if (!input || !interacting) return false;
        if (libraryDialog is { } libraryChoice) { libraryChoice.MoveFocus(direction); return true; }
        if (editDialog is { } dialog) { dialog.MoveFocus(direction); return true; }
        if (HasDialog) return true;
        if (toolbarFocused) { MoveToolbar(direction); return true; }
        MovePointer(direction == FocusNavigationDirection.Left ? -.04 : direction == FocusNavigationDirection.Right ? .04 : 0,
                direction == FocusNavigationDirection.Up ? -.04 : direction == FocusNavigationDirection.Down ? .04 : 0);
        return true;
    }
    internal bool Scroll(double x, double y)
    {
        if (!input || !interacting || core is null) return false;
        if (HasDialog || toolbarFocused) return true;
        if (x == 0 && y == 0) return true;
        UseControllerInput();
        wheelX = Math.Clamp(wheelX + x, -1600, 1600); wheelY = Math.Clamp(wheelY + y, -1600, 1600);
        PumpPointer(); return true;
    }
    internal void MovePointer(double x, double y)
    {
        if (!input || !browsing || HasDialog || x == 0 && y == 0) return;
        UseControllerInput();
        cursorX = Math.Clamp(cursorX + x, .01, .99); cursorY = Math.Clamp(cursorY + y, .01, .99);
        TrackPointerMovement();
        cursorDirty = true; DrawCursor(); PumpPointer();
    }
    private void DrawCursor()
    {
        cursor.Visibility = input && browsing && controllerPointerVisible && !HasDialog ? Visibility.Visible : Visibility.Collapsed;
        cursor.Margin = new(Math.Max(0, viewport.ActualWidth * cursorX - 8), Math.Max(0, viewport.ActualHeight * cursorY - 8), 0, 0);
    }
    private bool loading;
    private async Task InitializeAsync()
    {
        try
        {
            var initializationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var env = await environment().WaitAsync(lifetime.Token);
            EnvironmentInitializationMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(initializationStarted).TotalMilliseconds;
            if (retired) return;
            var options = env.CreateCoreWebView2ControllerOptions(); options.ProfileName = profile; options.IsInPrivateModeEnabled = true;
            var controllerStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            await web.EnsureCoreWebView2Async(env, options);
            ControllerInitializationMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(controllerStarted).TotalMilliseconds;
            if (retired) return;
            core = web.CoreWebView2;
            InitializeDrag();
            var settings = core.Settings;
            settings.AreHostObjectsAllowed = false; settings.IsWebMessageEnabled = false;
            settings.AreDevToolsEnabled = false; settings.AreDefaultContextMenusEnabled = false;
            settings.AreDefaultScriptDialogsEnabled = false; settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsPasswordAutosaveEnabled = false; settings.IsGeneralAutofillEnabled = false;
            settings.IsStatusBarEnabled = false; settings.IsBuiltInErrorPageEnabled = false;
            settings.IsPinchZoomEnabled = true;
            core.NavigationStarting += (_, args) =>
            {
                if (IsProviderContent && args.Uri != document.Url)
                { args.Cancel = true; if (args.IsUserInitiated && input) Run(() => OpenProviderLinkAsync(args.Uri)); return; }
                if (args.Uri != WebBrowserDocument.StartPage && !WebBrowserDocument.IsWebUrl(args.Uri)) { args.Cancel = true; ShowStatus("This link cannot open inside WidgetRail."); return; }
                navigationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                pageEpoch++; CancelPointer(); CancelEdit(); loading = true; UpdateChrome(); ShowStatus(null);
            };
            core.NavigationCompleted += (_, args) =>
            { if (retired || faulted) return;
                if (navigationStarted != 0) DocumentNavigationMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(navigationStarted).TotalMilliseconds;
                if (IsProviderContent)
                    Diagnostics.FrontendFailureLog.Current.Write("provider-startup", null,
                        FormattableString.Invariant($"widget={widgetId} success={args.IsSuccess} environmentMs={EnvironmentInitializationMilliseconds:F1} controllerMs={ControllerInitializationMilliseconds:F1} navigationMs={DocumentNavigationMilliseconds:F1}"));
                loading = false; ShowStatus(args.IsSuccess || args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled ? null : "The page could not be loaded. Reload to try again."); UpdateAddress(); UpdateChrome(); RefreshZoom(); if (args.IsSuccess) { RecordVisit(); Run(RefreshProviderHeightAsync); } };
            core.HistoryChanged += (_, _) => UpdateChrome();
            core.DocumentTitleChanged += (_, _) => UpdateAddress();
            core.SourceChanged += (_, _) => { UpdateAddress(); if (!loading) RecordVisit(); };
            core.PermissionRequested += (_, args) => { args.State = CoreWebView2PermissionState.Deny; args.Handled = true; };
            core.DownloadStarting += (_, args) => { args.Cancel = true; args.Handled = true; ShowStatus("Use Open externally to download files."); };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (input && args.IsUserInitiated && WebBrowserDocument.IsWebUrl(args.Uri))
                { if (IsProviderContent) Run(() => OpenProviderLinkAsync(args.Uri)); else core.Navigate(args.Uri); }
            };
            core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
            core.BasicAuthenticationRequested += (_, args) => args.Cancel = true;
            core.ServerCertificateErrorDetected += (_, args) => args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            core.ProcessFailed += (_, _) => { faulted = true; CancelEdit(); browsing = false; DrawCursor(); ShowStatus("The browser stopped. Use Reload widget to restart it."); UpdateChrome(); };
            ConfigureProviderDocument(env);
#if ENABLE_WIDGET_VALIDATION || ENABLE_TRIMMED_CONTROL_VALIDATION
            if (!IsProviderContent && FixturePage is not null)
            {
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, args) =>
                {
                    var html = FixturePage?.Invoke(args.Request.Uri) ?? "<!doctype html><title>Blocked test request</title>";
                    args.Response = env.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)).AsRandomAccessStream(), 200, "OK", "Content-Type: text/html");
                };
            }
#endif
            SetPresentation(visible, input);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error)
        { if (!retired) { faulted = true; ShowStatus("The browser could not start. Use Reload widget to retry."); UpdateChrome(); Diagnostics.FrontendFailureLog.Current.Write("browser-start", error); } }
    }
    private void NavigatePending()
    {
        if (!visible || retired || faulted || core is null || appliedNavigation >= document.NavigationId) return;
        appliedNavigation = document.NavigationId; NavigationCount++;
        core.Navigate(document.Url);
    }
    private void UpdateAddress()
    {
        if (retired || faulted || core is null || !Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)) return;
        var title = core.DocumentTitle ?? "";
        welcome.Visibility = core.Source == WebBrowserDocument.StartPage ? Visibility.Visible : Visibility.Collapsed;
        address.Text = core.Source == WebBrowserDocument.StartPage ? "Search Google or enter a URL" : uri.IdnHost + (title.Length == 0 ? "" : " · " + title[..Math.Min(180, title.Length)]);
        AutomationProperties.SetName(address, address.Text);
        UpdateBookmark();
    }
    private async Task OpenExternallyAsync()
    {
        if (!IsProviderContent && input && core is not null && WebBrowserDocument.IsWebUrl(core.Source) && OpenExternal is { } open)
            if (!await open(new(core.Source), lifetime.Token) && input) ShowStatus("The page could not be opened externally.");
    }
    private void PumpPointer()
    {
        if (pointerPump) return;
        pointerPump = true;
        Run(async () =>
        {
            try
            {
                while (input && core is not null && (cursorDirty || wheelX != 0 || wheelY != 0))
                {
                    var x = wheelX; var y = wheelY; wheelX = wheelY = 0; cursorDirty = false;
                    await SendPointerAsync(x != 0 || y != 0 ? "mouseWheel" : "mouseMoved", x, y);
                }
            }
            finally { pointerPump = false; }
        });
    }
    private async Task SendPointerAsync(string type, double deltaX = 0, double deltaY = 0)
    {
        await pointer.WaitAsync(lifetime.Token);
        try
        {
            if (!input || !controllerPointerVisible || HasDialog || core is null) return;
            var current = core;
            using var metrics = JsonDocument.Parse(await current.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
            if (!input || !controllerPointerVisible || HasDialog || retired || !ReferenceEquals(current, core)) return;
            var size = metrics.RootElement.GetProperty("cssVisualViewport");
            if (dragData is not null && nativePointerDown)
            {
                await DispatchDragAsync(current, "dragOver", cursorX * size.GetProperty("clientWidth").GetDouble(), cursorY * size.GetProperty("clientHeight").GetDouble());
                return;
            }
            await current.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new BrowserPointerEvent(
                type, cursorX * size.GetProperty("clientWidth").GetDouble(),
                cursorY * size.GetProperty("clientHeight").GetDouble(), deltaX, deltaY,
                button: nativePointerDown ? "left" : "none", buttons: nativePointerDown ? 1 : 0),
                BrowserJsonContext.Default.BrowserPointerEvent));
        }
        finally { pointer.Release(); }
    }
    private void Run(Func<Task> action) => _ = ObserveAsync(action);
    private async Task ObserveAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error)
        {
            if (!retired)
            {
                if (input) ShowStatus("This browser action could not finish. Try again.");
                Diagnostics.FrontendFailureLog.Current.Write("browser-operation", error);
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (retired) return; SetPresentation(false, false); retired = true; lifetime.Cancel();
        chromeTheme?.Dispose(); chromeTheme = null;
        if (dragReceiver is not null)
        {
            try { dragReceiver.DevToolsProtocolEventReceived -= DragIntercepted; }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
            dragReceiver = null;
        }
        if (initializing is not null) await initializing;
        if (providerAuthorityPump is not null) await providerAuthorityPump;
        CheckProviderAuthority = null;
        try { web.Close(); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
        core = null; IsInteractionFocused = null; ExitInteraction = null; FocusInteraction = null; InteractionChanged = null; lifetime.Dispose();
    }
}
