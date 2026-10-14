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
    private const string EmptyBrowserMessage = "Search Google or open a URL, bookmark, or recent page.\nPress LS for the toolbar, or Y to search or enter an address.";
    private readonly TextBlock welcome = new() { Text = EmptyBrowserMessage,
        TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Margin = new(32), MaxWidth = 460, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(12, 8, 12, 8), Visibility = Visibility.Collapsed };
    private readonly TextBlock address = new() { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly Border cursor = new() { Width = Input.ControllerPointerAppearance.Diameter, Height = Input.ControllerPointerAppearance.Diameter, CornerRadius = new(Input.ControllerPointerAppearance.Diameter / 2), BorderThickness = new(Input.ControllerPointerAppearance.Border),
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    private readonly List<Button> buttons = [];
    private readonly Func<Task<CoreWebView2Environment>> environment;
    private readonly string profile;
    private readonly string widgetId;
    internal string WidgetId => widgetId;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim pointer = new(1, 1);
    private Task? initializing;
    private CoreWebView2? core;
    private uint browserProcessId;
    private WebBrowserDocument document;
    private long appliedNavigation;
    private bool retired, visible, input, browsing, interacting, pointerPump, faulted;
    private double cursorX = .5, cursorY = .5, wheelX, wheelY;
    private bool cursorDirty;
    internal bool IsInteracting => AttachedPopup?.IsInteracting ?? interacting;
    internal bool IsBrowsing => AttachedPopup?.IsBrowsing ?? browsing;
    internal bool IsReady => core is not null && !retired && !faulted;
    internal bool OwnsNativeUiProcess(uint process) => process != 0 && (visible && IsReady && browserProcessId == process || popup?.OwnsNativeUiProcess(process) == true);
    internal uint NativeBrowserProcessId => IsReady ? browserProcessId : 0;
    internal bool IsNavigationInProgress => AttachedPopup?.IsNavigationInProgress ?? loading;
    internal bool IsToolbarVisible => AttachedPopup?.IsToolbarVisible ?? (header.Visibility == Visibility.Visible);
    internal bool UsesProfile(WebBrowserDocument next) => BrowserProfileIdentity.SameProfile(document, next);
    internal string ProfileName => profile;
    internal double EnvironmentInitializationMilliseconds { get; private set; }
    internal double ControllerInitializationMilliseconds { get; private set; }
    internal double DocumentNavigationMilliseconds { get; private set; }
    private long navigationStarted;
    internal object? CaptureZoomGesture(ControllerButton button) => AttachedPopup is { } child ? child.CaptureZoomGesture(button) : input && browsing && !HasDialog && !loading &&
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
    internal async Task<string> EvaluateFixtureAsync(string script) => await core!.ExecuteScriptAsync(script).AsTask().WaitAsync(TimeSpan.FromSeconds(8));
    internal async Task CrashRendererFixtureAsync()
    {
        try { await core!.CallDevToolsProtocolMethodAsync("Page.crash", "{}").AsTask().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (Exception) { /* A crashed renderer cannot complete its command reply. */ }
    }
    internal async Task EvaluateGestureFixtureAsync(string script)
    {
        using var result = JsonDocument.Parse(await core!.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            "{\"expression\":" + JsonSerializer.Serialize(script) + ",\"userGesture\":true}"));
        if (result.RootElement.TryGetProperty("exceptionDetails", out var error)) throw new InvalidOperationException("Fixture script failed: " + error);
    }
#endif
    [System.Diagnostics.Conditional("ENABLE_WIDGET_VALIDATION")]
    [System.Diagnostics.Conditional("ENABLE_TRIMMED_CONTROL_VALIDATION")]
    private void TracePointer(string value)
    {
#if ENABLE_WIDGET_VALIDATION || ENABLE_TRIMMED_CONTROL_VALIDATION
        if (FixturePointerTrace.Count < 200) FixturePointerTrace.Add(value);
#endif
    }

    internal BrowserSurface(string widgetId, WebBrowserDocument document, Func<Task<CoreWebView2Environment>> environment, IReadOnlyList<string>? providerHtml = null, BrowserLibraryStore? library = null, bool popup = false)
    {
        isPopup = popup;
        this.library = library;
        this.widgetId = widgetId;
        this.document = document; this.environment = environment; this.providerHtml = providerHtml;
        profile = BrowserProfileIdentity.Name(widgetId, document);
        CreateChrome();
        AutomationProperties.SetName(web, document.AccessibleName);
        AutomationProperties.SetAutomationId(web, "Browser.WebView");
        Loaded += (_, _) => { AttachTheme(); if (visible && initializing is null) initializing = InitializeAsync(); };
        Unloaded += (_, _) => { chromeTheme?.Dispose(); chromeTheme = null; };
        viewport.SizeChanged += (_, args) =>
        { CancelSpatialNavigation(); DrawCursor(); if (Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > 1) Run(RefreshProviderHeightAsync); };
        // Focus alone does not identify the input source. A controller's CDP
        // mouse-down can focus WebView too; cancelling here loses its mouse-up.
        // Real pointer events below own the mouse/controller handoff.
        web.GotFocus += (_, _) => { if (input && !HasDialog) { toolbarFocused = false; UpdateChromeVisibility(); interacting = true; browsing = true; InteractionChanged?.Invoke(); } };
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
        var newNavigation = next.NavigationId > document.NavigationId;
        document = next;
        UpdateChrome();
        AutomationProperties.SetName(web, document.AccessibleName);
        if (sessionStopped && newNavigation) Run(() => NavigateRequestedAsync(next.Url));
        else NavigatePending();
        RefreshInteractionFocus();
    }

    internal void SetPresentation(bool isVisible, bool acceptsInput)
    {
        if (retired) return;
        var wasInteracting = interacting;
        presentationInputRequested = acceptsInput;
        visible = isVisible && !retired; input = visible && acceptsInput && !faulted && !profileChanging && AttachedPopup is null;
        AttachedPopup?.SetPresentation(isVisible, acceptsInput);
        if (!visible) CancelSiteDecisions();
        if (!input) { editorOwnedButtons.Clear(); chromeOwnedA = false; }
        web.IsTabStop = input;
        foreach (var button in buttons) { button.IsTabStop = input; button.IsHitTestVisible = input; }
        if (!input) { toolbarFocused = false; libraryDialog?.Hide(); backReleaseOwned = false; CancelPointer(); CancelEdit(); interacting = false; browsing = false; wheelX = wheelY = 0; cursorDirty = false; }
        UpdateChromeVisibility();
        DrawCursor();
        try
        {
            if (!faulted)
            {
                web.Visibility = visible && AttachedPopup is null ? Visibility.Visible : Visibility.Collapsed;
                web.IsHitTestVisible = input || visible && nativeDialogPointerAccess;
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
            ActivityChanged?.Invoke();
        }
        RefreshInteractionFocus();
        if (wasInteracting != interacting) InteractionChanged?.Invoke();
    }

    internal void RefreshInteractionFocus()
    {
        if (AttachedPopup is { } child) { child.RefreshInteractionFocus(); return; }
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
        if (AttachedPopup is { } child) { child.Enter(); return; }
        if (visible && presentationInputRequested && faulted && Recover is { } recover) { Run(() => recover(this)); return; }
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
        if (button == ControllerButton.B && backReleaseOwned)
        { if (phase == ControllerEventPhase.Released) backReleaseOwned = false; return true; }
        if (AttachedPopup is { } child)
        {
            if (button == ControllerButton.B && !child.HasDialog && !child.IsToolbarFocused)
            { if (phase == ControllerEventPhase.Pressed) { backReleaseOwned = true; Run(ClosePopupAsync); } return true; }
            return child.HandleButton(button, phase);
        }
        if (phase == ControllerEventPhase.Released && editorOwnedButtons.Remove(button)) return true;
        if (editDialog is null && editorOwnedButtons.Contains(button)) return true;
        if (button == ControllerButton.A && chromeOwnedA)
        { if (phase == ControllerEventPhase.Released) chromeOwnedA = false; return true; }
        if (!input || !interacting) return false;
        if (!HasDialog && phase == ControllerEventPhase.Pressed) UseControllerInput();
        if (editPending && button == ControllerButton.B && phase == ControllerEventPhase.Pressed)
        { CancelEdit(); DrawCursor(); InteractionChanged?.Invoke(); return true; }
        if (libraryDialog is { } libraryChoice)
        {
            if (button == ControllerButton.B && phase == ControllerEventPhase.Pressed) backReleaseOwned = true;
            libraryChoice.Handle(button, phase); return true;
        }
        if (editDialog is { } dialog)
        {
            if (phase == ControllerEventPhase.Pressed) editorOwnedButtons.Add(button);
            dialog.Handle(button, phase); return true;
        }
        if (HasDialog) return true;
        if (button == ControllerButton.B && toolbarFocused && (isPopup || document.Options.Chrome == BrowserChrome.OnDemand))
        {
            if (phase == ControllerEventPhase.Pressed) { backReleaseOwned = true; ToggleToolbar(); }
            return true;
        }
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
        else if (button == ControllerButton.LeftBumper && !sessionStopped && core?.CanGoBack == true) core.GoBack();
        else if (button == ControllerButton.RightBumper && !sessionStopped && core?.CanGoForward == true) core.GoForward();
        else if (button == ControllerButton.X) Reload();
        else if (button == ControllerButton.Y) Run(EditAddressAsync);
        else if (button == ControllerButton.Menu) Run(OpenContextMenuAsync);
        else if (button == ControllerButton.LeftTrigger) ChangeZoom(-.1);
        else if (button == ControllerButton.RightTrigger) ChangeZoom(.1);
        return true;
    }
    internal bool MoveFocus(FocusNavigationDirection direction)
    {
        if (AttachedPopup is { } child) return child.MoveFocus(direction);
        if (!input || !interacting) return false;
        if (libraryDialog is { } libraryChoice) { libraryChoice.MoveFocus(direction); return true; }
        if (editDialog is { } dialog) { dialog.MoveFocus(direction); return true; }
        if (HasDialog) return true;
        if (toolbarFocused) { MoveToolbar(direction); return true; }
        JumpPointer(direction);
        return true;
    }
    internal bool Scroll(double x, double y)
    {
        if (AttachedPopup is { } child) return child.Scroll(x, y);
        if (!input || !interacting || core is null) return false;
        if (HasDialog || toolbarFocused) return true;
        if (x == 0 && y == 0) return true;
        CancelSpatialNavigation();
        UseControllerInput();
        wheelX = Math.Clamp(wheelX + x, -1600, 1600); wheelY = Math.Clamp(wheelY + y, -1600, 1600);
        PumpPointer(); return true;
    }
    internal void MovePointer(double x, double y)
    {
        if (AttachedPopup is { } child) { child.MovePointer(x, y); return; }
        if (!input || !browsing || HasDialog || x == 0 && y == 0) return;
        CancelSpatialNavigation();
        UseControllerInput();
        cursorX = Math.Clamp(cursorX + x, .01, .99); cursorY = Math.Clamp(cursorY + y, .01, .99);
        TrackPointerMovement();
        cursorDirty = true; DrawCursor(freeMovement: true); PumpPointer();
    }
    private void DrawCursor(bool animateJump = false, bool freeMovement = false)
    {
        cursor.Visibility = input && browsing && controllerPointerVisible && !HasDialog ? Visibility.Visible : Visibility.Collapsed;
        DrawPointerVisual(animateJump, freeMovement);
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
            var options = env.CreateCoreWebView2ControllerOptions();
            options.ProfileName = profile;
            options.IsInPrivateModeEnabled = document.Options.Storage == BrowserStorage.Private;
            var controllerStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            await web.EnsureCoreWebView2Async(env, options);
            ControllerInitializationMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(controllerStarted).TotalMilliseconds;
            if (retired) return;
            core = web.CoreWebView2;
            browserProcessId = core.BrowserProcessId;
            InitializeDrag();
            var settings = core.Settings;
            settings.AreHostObjectsAllowed = false; settings.IsWebMessageEnabled = false;
            settings.AreDevToolsEnabled = false; settings.AreDefaultContextMenusEnabled = !IsProviderContent;
            settings.AreDefaultScriptDialogsEnabled = false; settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsPasswordAutosaveEnabled = false; settings.IsGeneralAutofillEnabled = false;
            settings.IsStatusBarEnabled = false; settings.IsBuiltInErrorPageEnabled = false;
            settings.IsPinchZoomEnabled = true;
            core.NavigationStarting += (_, args) =>
            {
                if (IsProviderContent && args.Uri != document.Url)
                { args.Cancel = true; if (args.IsUserInitiated && input) Run(() => OpenProviderLinkAsync(args.Uri)); return; }
                if (!IsProviderContent && args.IsUserInitiated && !args.IsRedirected && BrowserNavigationPolicy.OpenExternally(document, args.Uri))
                {
                    args.Cancel = true;
                    var destination = new Uri(args.Uri);
                    if (input && OpenExternal is { } open) Run(async () => { await open(destination, lifetime.Token); });
                    return;
                }
                if (args.Uri != WebBrowserDocument.StartPage && !WebBrowserDocument.IsWebUrl(args.Uri)) { args.Cancel = true; ShowStatus("This link cannot open inside WidgetRail."); return; }
                CancelSiteDecisions();
                InvalidateNotifications?.Invoke(this);
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
            // WinRT event senders can be another managed projection of the same
            // COM object. Capture our controller identity for stale-event checks.
            var permissionOwner = core;
            core.PermissionRequested += (_, args) => PermissionRequested(permissionOwner, args);
            InitializeNotifications(permissionOwner);
            core.ScreenCaptureStarting += (_, args) => ScreenCaptureStarting(permissionOwner, args);
            core.DownloadStarting += (_, args) => DownloadStarting(permissionOwner, args);
            core.NewWindowRequested += (_, args) => NewWindowRequested(args);
            core.WindowCloseRequested += (_, _) => CloseRequested?.Invoke();
            core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
            core.BasicAuthenticationRequested += (_, args) => args.Cancel = true;
            core.ServerCertificateErrorDetected += (_, args) => args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            core.ScriptDialogOpening += (_, args) => ScriptDialogOpening(permissionOwner, args);
            core.ContextMenuRequested += (_, args) => ContextMenuRequested(permissionOwner, args);
            core.ProcessFailed += (_, args) =>
            {
                Diagnostics.FrontendFailureLog.Current.Write("browser-process-failed", null, $"widget={widgetId} kind={args.ProcessFailedKind}");
                if (!RequiresPageRecovery(args.ProcessFailedKind))
                {
                    if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                        ShowStatus("The page is not responding. Reload it or stop its session from the tray menu.");
                    return;
                }
                faulted = true; InvalidateNotifications?.Invoke(this); CancelEdit(); CancelSiteDecisions(); browsing = interacting = false; DrawCursor();
                ShowStatus("The page disconnected. Press A to reopen it."); UpdateChrome();
                ActivityChanged?.Invoke();
            };
            await InitializeFilesAsync();
            if (retired) return;
            ConfigureProviderDocument(env);
#if ENABLE_WIDGET_VALIDATION || ENABLE_TRIMMED_CONTROL_VALIDATION
            if (!IsProviderContent && FixturePage is not null)
            {
                // Include requests initiated by out-of-process iframe reloads.
                // The deprecated two-argument filter misses those documents.
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
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
        { if (!retired) { faulted = true; ShowStatus("The browser could not start. Use Restart widget to retry."); UpdateChrome(); Diagnostics.FrontendFailureLog.Current.Write("browser-start", error); ActivityChanged?.Invoke(); } }
    }
    private void NavigatePending()
    {
        if (isPopup || !visible || retired || faulted || profileChanging || sessionStopped || core is null || appliedNavigation >= document.NavigationId) return;
        appliedNavigation = document.NavigationId; NavigationCount++;
        core.Navigate(document.Url);
    }
    private void UpdateAddress()
    {
        if (retired || faulted || core is null || !Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)) return;
        var title = core.DocumentTitle ?? "";
        welcome.Visibility = core.Source == WebBrowserDocument.StartPage ? Visibility.Visible : Visibility.Collapsed;
        address.Text = isPopup ? (BrowserSiteIdentity.Origin(core.Source) ?? (core.Source == WebBrowserDocument.StartPage ? "New popup" : "Untrusted origin")) : core.Source == WebBrowserDocument.StartPage ? "Search Google or enter a URL" : uri.IdnHost + (title.Length == 0 ? "" : " · " + title[..Math.Min(180, title.Length)]);
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
                while (input && core is not null && (!spatialHoverDeferred && cursorDirty || wheelX != 0 || wheelY != 0))
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
            if (!input || !controllerPointerVisible || HasDialog || core is null || type == "mouseMoved" && spatialHoverDeferred) return;
            var current = core;
            using var metrics = JsonDocument.Parse(await current.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
            if (!input || !controllerPointerVisible || HasDialog || retired || !ReferenceEquals(current, core) || type == "mouseMoved" && spatialHoverDeferred) return;
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
        if (retired) return; SetPresentation(false, false); retired = true; ResetPointerVisual(); lifetime.Cancel();
        InvalidateNotifications?.Invoke(this); ReceiveNotification = null; InvalidateNotifications = null;
        nativeNotifications?.Dispose(); nativeNotifications = null;
        if (pendingSiteDecisions.Count != 0) await Task.WhenAll(pendingSiteDecisions.ToArray());
        await DrainPopupAsync();
        CloseFiles();
        Choose = null;
        ChangeRunning = null; ActivityChanged = null; Recover = null;
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
