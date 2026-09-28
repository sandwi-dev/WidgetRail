using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>
/// One durable, host-owned native WebView2. The owner retains this object while its sealed
/// document remains current, including when no viewport is declared. Unloading an element
/// is not session retirement. Only Dispose closes its document and audio lifetime.
/// All methods and callbacks are UI-thread confined.
/// </summary>
internal sealed class EmbeddedMediaSurface : IDisposable
{
    private static Task<CoreWebView2Environment>? sharedEnvironment;
    // Bridge observations have host lifetime, independent of the page's event sequence.
    private static long nextPlaybackObservation;
    private readonly PresentationSession session;
    private readonly WidgetPresentationEmbeddedMediaDocument document;
    private readonly EmbeddedMediaRequestPolicy policy;
    private readonly EmbeddedMediaTransport transport;
    private readonly WebView2 browser = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer deadline = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly HashSet<CoreWebView2Frame> frames = [];
    private CoreWebView2? core;
    private CoreWebView2Environment? environment;
    private bool retired;
    private bool started;
    private bool documentNavigated;
    private bool visible;
    private bool inputEnabled;
    private int pendingSubmissions;
    private Task? initialization;

    public FrameworkElement Element => browser;
    public bool IsRetired => retired;
    public bool IsReady => !retired && transport.Ready;
    public string? FailureCode { get; private set; }
    public event Action<string>? Diagnostic;
    public event Action? BackRequested;
    public event Action? StateChanged;

    public EmbeddedMediaSurface(PresentationSession session, WidgetPresentationEmbeddedMediaDocument document)
    {
        this.session = session;
        this.document = document;
        var state = session.GetEmbeddedMediaState(document) ?? throw new InvalidOperationException("Retired media document.");
        string? applicationIdentity;
        try { applicationIdentity = Windows.ApplicationModel.Package.Current.Id.Name; }
        catch (InvalidOperationException) { applicationIdentity = null; }
        policy = new(document, state.Declaration, applicationIdentity);
        transport = new(document.SessionId);
        AutomationProperties.SetName(browser, state.Declaration.AccessibleName);
        AutomationProperties.SetAutomationId(browser, "EmbeddedMedia." + document.SessionId);
        browser.IsTabStop = false; // Controller focus stays with the host-owned MediaViewport.
        browser.IsHitTestVisible = false;
        browser.Loaded += OnLoaded;
        deadline.Tick += OnDeadline;
    }

    /// <summary>Visibility and input authority are separate; modal coverage gates input without destroying media.</summary>
    public void UpdatePresentation(bool isVisible, bool acceptsInput)
    {
        if (retired) return;
        visible = isVisible;
        inputEnabled = isVisible && acceptsInput;
        browser.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        browser.IsHitTestVisible = inputEnabled;
        Refresh();
    }

    public void Refresh()
    {
        if (retired) return;
        var state = session.GetEmbeddedMediaState(document);
        if (state is null) { Dispose(); return; }
        AutomationProperties.SetName(browser, state.Declaration.AccessibleName);
        Pump();
    }

    /// <summary>Only authored commands on a visible, input-enabled viewport may consume controller input.</summary>
    public bool Dispatch(EmbeddedMediaCommand command)
    {
        if (retired || !visible || !inputEnabled) return false;
        var state = session.GetEmbeddedMediaState(document);
        if (state is null) { Dispose(); return false; }
        if (!state.Declaration.Commands.Contains(command)) return false;
        // The media surface claims its declared command even while its adapter is busy.
        // Do not leak a second A/Back into parent navigation, or queue stale commands.
        var message = transport.Dispatch(command);
        if (message is not null) Send(message);
        return true;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!started && !retired) { started = true; initialization = InitializeAsync(); }
    }

    private async Task InitializeAsync()
    {
        try
        {
            sharedEnvironment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "EmbeddedMedia"), null).AsTask();
            environment = await sharedEnvironment;
            if (!Current()) return;
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = "media-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.Authority.WidgetId)))[..32];
            options.IsInPrivateModeEnabled = true;
            await browser.EnsureCoreWebView2Async(environment, options);
            if (!Current()) return;
            core = browser.CoreWebView2;
            var settings = core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDefaultScriptDialogsEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsPinchZoomEnabled = false;
            settings.IsSwipeNavigationEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsWebMessageEnabled = true;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.FrameNavigationStarting += OnFrameNavigationStarting;
            core.FrameCreated += OnFrameCreated;
            core.WebResourceRequested += OnResourceRequested;
            core.WebMessageReceived += OnMessage;
            core.ProcessFailed += OnProcessFailed;
            core.NewWindowRequested += OnNewWindow;
            core.PermissionRequested += OnPermission;
            core.DownloadStarting += OnDownload;
            core.BasicAuthenticationRequested += OnAuthentication;
            core.ServerCertificateErrorDetected += OnCertificateError;
            core.LaunchingExternalUriScheme += OnExternalScheme;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            deadline.Start();
            core.Navigate(policy.EntryUri);
        }
        catch (Exception error) when (!IsFatal(error))
        {
            if (sharedEnvironment is { IsFaulted: true } or { IsCanceled: true }) sharedEnvironment = null;
            if (!retired) Fault("media-initialization-failed");
        }
    }

    private bool Current()
    {
        if (retired) return false;
        if (session.GetEmbeddedMediaState(document) is not null) return true;
        Dispose();
        return false;
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // A second navigation/reload cannot inherit this document's command authority.
        if (!Current() || documentNavigated || !policy.IsDocument(args.Uri)) { args.Cancel = true; return; }
        documentNavigated = true;
    }
    private void OnFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args) => args.Cancel = !Current() || !policy.AllowsFrame(args.Uri);
    private void OnFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs args)
    {
        if (!Current()) return;
        if (frames.Contains(args.Frame)) return;
        if (frames.Count >= 16) { Fault("media-frame-limit"); return; }
        frames.Add(args.Frame);
        args.Frame.NavigationStarting += OnFrameNavigationStarting;
        args.Frame.FrameCreated += OnFrameCreated;
        args.Frame.Destroyed += OnFrameDestroyed;
    }
    private void OnFrameDestroyed(CoreWebView2Frame sender, object args) => RemoveFrame(sender);
    private void RemoveFrame(CoreWebView2Frame frame)
    {
        if (!frames.Remove(frame)) return;
        frame.NavigationStarting -= OnFrameNavigationStarting;
        frame.FrameCreated -= OnFrameCreated;
        frame.Destroyed -= OnFrameDestroyed;
    }
    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!Current()) return;
        if (!args.IsSuccess || !policy.IsDocument(sender.Source)) { Fault("media-navigation-failed"); return; }
        if (!transport.Ready && !transport.Busy) Send(transport.Initialize());
    }
    private void OnResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            // Workers are outside the SDK adapter contract. All surfaces deny them;
            // this also avoids ambiguous owner selection for shared-environment worker events.
            if (!Current() || args.RequestedSourceKind != CoreWebView2WebResourceRequestSourceKinds.Document)
            { Deny(sender, args); return; }
            var request = args.Request;
            var range = request.Headers.Contains("Range") ? request.Headers.GetHeader("Range") : null;
            var response = policy.Resolve(request.Uri, request.Method, range);
            if (response is not null)
            {
                args.Response = environment!.CreateWebResourceResponse(response.Content?.AsRandomAccessStream(), response.Status, response.Reason, response.Headers);
                Diagnostic?.Invoke("media-resource-" + response.Status);
            }
            else if (policy.AllowsRemoteResource(request.Uri)) request.Headers.SetHeader("Referer", policy.ApplicationReferer!);
            else Deny(sender, args);
        }
        catch (Exception error) when (!IsFatal(error)) { Deny(sender, args); Fault("media-resource-failed"); }
    }
    private void Deny(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        // A queued request can outlive Close. Keep environment response authority
        // separate from the retired controller, and never let denial crash teardown.
        try { if (environment is { } current) args.Response = current.CreateWebResourceResponse(null, 404, "Not Found", "Cache-Control: no-store\r\nContent-Length: 0\r\n"); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
    }

    private async void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!Current()) return;
        try
        {
            if (!policy.IsDocument(args.Source) || !transport.TryAccept(args.WebMessageAsJson, out var observed))
            { Fault("media-invalid-message"); return; }
            if (observed!.Type == "armed")
            {
                await ActivateAsync(observed);
                return;
            }
            if (!transport.Busy) deadline.Stop();
            if (observed.Playback is { } playback)
            {
                var state = session.GetEmbeddedMediaState(document);
                // A newer widget command supersedes older completion authority; never
                // attribute an old acknowledgement to the new command or replay it.
                if (state is not null && (playback.CommandSequence == 0 ||
                    state.Declaration.PendingCommand is { } pending && pending.Sequence == playback.CommandSequence && pending.MediaKey == playback.MediaKey))
                {
                    ++pendingSubmissions;
                    try
                    {
                        var published = playback with { Sequence = Interlocked.Increment(ref nextPlaybackObservation) };
                        await session.SendEmbeddedMediaPlaybackEventAsync(document, published, lifetime.Token);
                    }
                    finally { --pendingSubmissions; }
                }
            }
            if (!Current()) return;
            if (observed.Type == "back" && visible && inputEnabled) BackRequested?.Invoke();
            StateChanged?.Invoke();
            Pump();
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) when (!IsFatal(error)) { if (!retired) Fault("media-observation-failed"); }
    }

    private async Task ActivateAsync(EmbeddedMediaObservation observed)
    {
        if (!Current() || !visible || !inputEnabled || core is null ||
            observed.X + observed.Width > browser.ActualWidth || observed.Y + observed.Height > browser.ActualHeight)
        { Fault("media-activation-unavailable"); return; }
        // WinUI owns the WebView2 controller and exposes no SendMouseInput. This fixed
        // browser input operation is host-only; widgets cannot choose methods or payloads.
        var currentCore = core;
        var x = observed.X + observed.Width / 2; var y = observed.Y + observed.Height / 2;
        await currentCore.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type = "mousePressed", x, y, button = "left", buttons = 1, clickCount = 1 }));
        if (!Current()) return; // Closing the controller also releases browser input.
        if (!visible || !inputEnabled || transport.PendingCommandId != observed.CommandId)
        {
            // Release outside the action if host authority changed during the asynchronous press.
            await currentCore.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", "{\"type\":\"mouseReleased\",\"x\":-1,\"y\":-1,\"button\":\"left\",\"buttons\":0,\"clickCount\":0}");
            return;
        }
        await currentCore.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type = "mouseReleased", x, y, button = "left", buttons = 0, clickCount = 1 }));
    }

    private void Pump()
    {
        if (!Current() || core is null || pendingSubmissions != 0 || transport.Busy || !transport.Ready) return;
        var command = session.GetEmbeddedMediaState(document)?.Declaration.PendingCommand;
        // Gesture-requiring playback waits for a visible, uncovered viewport.
        if (command is null || command.Kind == EmbeddedMediaPlaybackCommandKind.Play && (!visible || !inputEnabled)) return;
        var message = transport.Dispatch(command);
        if (message is not null) Send(message);
    }
    private void Send(string message)
    {
        if (!Current() || core is null) return;
        try
        {
            deadline.Stop(); deadline.Start();
            core.PostWebMessageAsJson(message);
        }
        catch (Exception error) when (!IsFatal(error)) { Fault("media-command-transport-failed"); }
    }
    private void OnDeadline(object? sender, object args) => Fault("media-adapter-timeout");
    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args) => Fault("media-browser-process-failed");
    private static void OnNewWindow(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args) => args.Handled = true;
    private static void OnPermission(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args) { args.State = CoreWebView2PermissionState.Deny; args.Handled = true; }
    private static void OnDownload(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args) { args.Cancel = true; args.Handled = true; }
    private static void OnAuthentication(CoreWebView2 sender, CoreWebView2BasicAuthenticationRequestedEventArgs args) => args.Cancel = true;
    private static void OnCertificateError(CoreWebView2 sender, CoreWebView2ServerCertificateErrorDetectedEventArgs args) => args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
    private static void OnExternalScheme(CoreWebView2 sender, CoreWebView2LaunchingExternalUriSchemeEventArgs args) => args.Cancel = true;
    private static bool IsFatal(Exception error) => error is OutOfMemoryException or StackOverflowException or AccessViolationException;
    private void Fault(string code)
    {
        if (retired) return;
        FailureCode = code;
        Dispose();
        Diagnostic?.Invoke(code);
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (retired) return;
        retired = true;
        inputEnabled = visible = false;
        lifetime.Cancel();
        deadline.Stop(); deadline.Tick -= OnDeadline;
        browser.Loaded -= OnLoaded;
        browser.IsHitTestVisible = false;
        browser.Visibility = Visibility.Collapsed;
        try
        {
            foreach (var frame in frames.ToArray()) RemoveFrame(frame);
            if (core is { } current)
            {
                current.NavigationStarting -= OnNavigationStarting;
                current.NavigationCompleted -= OnNavigationCompleted;
                current.FrameNavigationStarting -= OnFrameNavigationStarting;
                current.FrameCreated -= OnFrameCreated;
                current.WebResourceRequested -= OnResourceRequested;
                current.WebMessageReceived -= OnMessage;
                current.ProcessFailed -= OnProcessFailed;
                current.NewWindowRequested -= OnNewWindow;
                current.PermissionRequested -= OnPermission;
                current.DownloadStarting -= OnDownload;
                current.BasicAuthenticationRequested -= OnAuthentication;
                current.ServerCertificateErrorDetected -= OnCertificateError;
                current.LaunchingExternalUriScheme -= OnExternalScheme;
                current.RemoveWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { Diagnostic?.Invoke("media-detach-after-process-exit"); }
        finally
        {
            core = null;
            frames.Clear();
            try { browser.Close(); }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
            { Diagnostic?.Invoke("media-close-after-process-exit"); }
            finally { lifetime.Dispose(); }
        }
    }
}
