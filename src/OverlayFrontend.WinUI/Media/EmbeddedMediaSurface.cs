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
internal sealed class EmbeddedMediaSurface : IDisposable, IAsyncDisposable
{
    private static Task<CoreWebView2Environment>? sharedEnvironment;
    private readonly MediaStartupTrace startupTrace;
    // Bridge observations have host lifetime, independent of the page's event sequence.
    private static long nextPlaybackObservation;
    private readonly PresentationSession session;
    private readonly WidgetPresentationEmbeddedMediaDocument document;
    private readonly EmbeddedMediaRequestPolicy policy;
    private readonly EmbeddedMediaTransport transport;
    private readonly WebView2 browser = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer deadline = new() { Interval = TimeSpan.FromSeconds(15) };
    private CoreWebView2? core;
    private CoreWebView2Environment? environment;
    private bool retired;
    private bool started;
    private bool documentNavigated;
    private bool reportedResourceDenial;
    private bool visible;
    private bool inputEnabled;
    private int pendingSubmissions;
    private Task? initialization;
    private Task? retirement;
    private TaskCompletionSource? placement;
    private TaskCompletionSource? placementChanged;
    private Grid? placementTarget;
    private bool placementVisible, placementInput;
    internal Task PlacementCompletion => placement?.Task ?? Task.CompletedTask;
    private readonly HashSet<Task> observations = [];
    private EmbeddedMediaPlaybackEvent? lastPlayback;
    private long lastPlaybackSequence;

    public FrameworkElement Element => browser;
    public bool IsRetired => retired;
    public bool IsReady => !retired && transport.Ready;
    internal bool IsCommandPending => transport.Busy || pendingSubmissions != 0;
    internal EmbeddedMediaPlaybackEvent? Playback => lastPlayback;
    public string? FailureCode { get; private set; }
    internal Func<bool>? InputAuthority { get; set; }
    private bool AcceptsInput => !retired && visible && inputEnabled && (InputAuthority?.Invoke() ?? true);
    internal Func<bool>? PlaybackActivationAuthority { get; set; }
    private bool AcceptsPlaybackActivation => !retired && visible &&
        (AcceptsInput || PlaybackActivationAuthority?.Invoke() == true);
    public event Action<string>? Diagnostic;
    public event Action? BackRequested;
    public event Action? StateChanged;
    internal event Action<Exception>? ObservationFailed;

    public EmbeddedMediaSurface(PresentationSession session, WidgetPresentationEmbeddedMediaDocument document, MediaStartupTrace? startupTrace = null)
    {
        this.startupTrace = startupTrace ?? new(document.Authority.WidgetId);
        this.session = session;
        this.document = document;
        var state = session.GetEmbeddedMediaState(document) ?? throw new InvalidOperationException("Retired media document.");
        policy = new(document, state.Declaration);
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
        // SDK viewports expose pixels; authored native controls retain pointer/focus
        // ownership. Host-only trusted activation uses the fixed browser input path.
        browser.IsHitTestVisible = false;
        if (!AcceptsPlaybackActivation) CancelActivation();
        Refresh();
    }

    public void Refresh()
    {
        if (retired) return;
        var state = session.GetEmbeddedMediaState(document);
        if (state is null) { Dispose(); return; }
        AutomationProperties.SetName(browser, state.Declaration.AccessibleName);
        if (!AcceptsPlaybackActivation) CancelActivation();
        Pump();
    }

    internal Task MoveTo(Grid destination, bool isVisible, bool acceptsInput)
    {
        var changed = !ReferenceEquals(placementTarget, destination) || placementVisible != isVisible || placementInput != acceptsInput;
        placementTarget = destination; placementVisible = isVisible; placementInput = acceptsInput;
        if (placement is { Task.IsCompleted: false })
        {
            if (changed) placementChanged?.TrySetResult();
            return placement.Task;
        }
        if (browser.XamlRoot is { } root && destination.XamlRoot is { } nextRoot && !ReferenceEquals(root, nextRoot))
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            placement = completion;
            _ = TransferAcrossRootsAsync(completion);
            return completion.Task;
        }
        if (!ReferenceEquals(browser.Parent, destination))
        {
            // Publish detachment before changing XamlRoot so WebView2 rebuilds
            // its composition/host visibility binding for the new native site.
            UpdatePresentation(false, false);
            if (browser.Parent is Panel previous) previous.Children.Remove(browser);
            destination.Children.Add(browser);
        }
        UpdatePresentation(isVisible, acceptsInput);
        return Task.CompletedTask;
    }

    private async Task TransferAcrossRootsAsync(TaskCompletionSource completion)
    {
        try
        {
            while (!retired)
            {
                var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Detached(object sender, RoutedEventArgs args) { if (!browser.IsLoaded) unloaded.TrySetResult(); }
                browser.Unloaded += Detached;
                try
                {
                    var wasLoaded = browser.IsLoaded;
                    UpdatePresentation(false, false);
                    if (browser.Parent is Panel previous) previous.Children.Remove(browser);
                    // WebView2 disconnects RootVisualTarget in Unloaded. Do not
                    // suppress that event by attaching to the peer immediately.
                    if (wasLoaded) await unloaded.Task.WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token);
                    else await Task.Yield();
                }
                finally { browser.Unloaded -= Detached; }
                if (retired || placementTarget is not { } target) break;
                var attached = await AttachToRootAsync(target);
                // A newer placement can arrive while native Loaded is pending.
                // Complete only the newest destination, retaining one transaction.
                if (attached && ReferenceEquals(browser.Parent, placementTarget))
                {
                    UpdatePresentation(placementVisible, placementInput);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) when (!IsFatal(error))
        { if (!retired) { ObservationFailed?.Invoke(error); Fault("media-placement-failed"); } }
        finally
        {
            completion.TrySetResult();
            StateChanged?.Invoke();
        }
    }

    private async Task<bool> AttachToRootAsync(Grid target)
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool Attached() => browser.IsLoaded && ReferenceEquals(browser.Parent, target) &&
            target.XamlRoot is not null && ReferenceEquals(browser.XamlRoot, target.XamlRoot);
        void Loaded(object sender, RoutedEventArgs args) { if (Attached()) loaded.TrySetResult(); }
        browser.Loaded += Loaded;
        try
        {
            target.Children.Add(browser);
            // Keep the browser hidden until Loaded has attached its composition
            // target. Publishing visibility first can reach the native browser
            // while it still has the detached target from the previous root.
            // WinUI rebinds WebView2's HWND, XamlRoot and composition target in
            // Loaded. Adding a child alone is not completion of a visible move.
            // Hidden parking has no visible-attachment requirement.
            while (placementVisible && target.IsLoaded && !loaded.Task.IsCompleted)
            {
                var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                placementChanged = changed;
                try { await Task.WhenAny(loaded.Task, changed.Task).WaitAsync(TimeSpan.FromSeconds(2), lifetime.Token); }
                finally { if (ReferenceEquals(placementChanged, changed)) placementChanged = null; }
                if (!ReferenceEquals(target, placementTarget)) return false;
            }
            Diagnostic?.Invoke($"media-root-attached loaded={browser.IsLoaded} rootMatches={ReferenceEquals(browser.XamlRoot, target.XamlRoot)} hostVisible={target.XamlRoot?.IsHostVisible} visible={placementVisible} input={placementInput} width={browser.ActualWidth:F1} height={browser.ActualHeight:F1}");
            return true;
        }
        finally { browser.Loaded -= Loaded; }
    }

    /// <summary>Only authored commands on a visible, input-enabled viewport may consume controller input.</summary>
    public bool Dispatch(EmbeddedMediaCommand command)
    {
        if (!AcceptsInput) return false;
        var state = session.GetEmbeddedMediaState(document);
        if (state is null) { Dispose(); return false; }
        if (!state.Declaration.Commands.Contains(command)) return false;
        // The media surface claims its declared command even while its adapter is busy.
        // Do not leak a second A/Back into parent navigation, or queue stale commands.
        var message = transport.Dispatch(command);
        if (message is not null) Send(message);
        return true;
    }

    internal bool DispatchPresentation(EmbeddedMediaHostCommand command, MediaPresentationKind presentation)
    {
        if (!AcceptsInput || lastPlayback is not { } playback || session.GetEmbeddedMediaState(document) is not { } state ||
            !state.Declaration.SupportedPresentations.Contains(presentation)) return false;
        var message = transport.DispatchHost(command, playback, state.Declaration.MediaSeekStepSeconds ?? ProtocolConstants.DefaultMediaSeekStepSeconds);
        if (message is not null) Send(message);
        return true; // Busy commands are consumed once, never queued or replayed.
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!started && !retired) { started = true; initialization = InitializeAsync(); }
    }

    private async Task InitializeAsync()
    {
        startupTrace.Mark("initialization-start");
        try
        {
            sharedEnvironment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "EmbeddedMedia"), null).AsTask();
            environment = await sharedEnvironment.WaitAsync(lifetime.Token);
            startupTrace.Mark("environment-ready");
            if (!Current()) return;
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = "media-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.Authority.WidgetId)))[..32];
            options.IsInPrivateModeEnabled = true;
            // Initialization owns native work beyond a canceled managed wait.
            // Retirement revokes authority now and waits for this operation before Close.
            await browser.EnsureCoreWebView2Async(environment, options);
            startupTrace.Mark("controller-ready");
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
    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!Current()) return;
        if (!args.IsSuccess || !policy.IsDocument(sender.Source)) { Fault("media-navigation-failed"); return; }
        startupTrace.Mark("document-loaded");
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
            // The core navigation event covers direct child frames. Document
            // requests cover nested frames too, without per-frame COM subscriptions
            // whose removal after frame destruction crashes WebView2 154.
            // A media/CDN resource allowlist never grants document-navigation rights.
            if (args.ResourceContext == CoreWebView2WebResourceContext.Document &&
                !policy.IsDocument(request.Uri) && !policy.AllowsFrame(request.Uri))
            { Deny(sender, args); Diagnostic?.Invoke("media-frame-document-denied"); return; }
            var range = request.Headers.Contains("Range") ? request.Headers.GetHeader("Range") : null;
            var response = policy.Resolve(request.Uri, request.Method, range);
            if (response is not null)
            {
                args.Response = environment!.CreateWebResourceResponse(response.Content?.AsRandomAccessStream(), response.Status, response.Reason, response.Headers);
                Diagnostic?.Invoke("media-resource-" + response.Status);
            }
            else if (policy.AllowsRemoteResource(request.Uri)) request.Headers.SetHeader("Referer", policy.ApplicationReferer!);
            else
            {
                Deny(sender, args);
                if (!reportedResourceDenial)
                {
                    reportedResourceDenial = true;
                    Diagnostic?.Invoke(policy.ApplicationReferer is null
                        ? "media-resource-host-identity-missing" : "media-resource-origin-denied");
                }
            }
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

    private void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var operation = HandleMessageAsync(args);
        observations.Add(operation);
        _ = ObserveAsync();
        async Task ObserveAsync()
        {
            try { await operation; }
            finally { observations.Remove(operation); }
        }
    }

    private async Task HandleMessageAsync(CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!Current()) return;
        try
        {
            var json = args.WebMessageAsJson;
            if (policy.IsDocument(args.Source) && transport.IsRetiredAuthority(json)) return;
            if (!policy.IsDocument(args.Source) || !transport.TryAccept(json, out var observed))
            { Fault("media-invalid-message"); return; }
            if (observed!.Type == "armed")
            {
                await ActivateAsync(observed);
                return;
            }
            if (!transport.Busy) deadline.Stop();
            if (transport.Ready) startupTrace.Mark("adapter-ready");
            if (observed.Playback is { } playback)
            {
                if (playback.State == EmbeddedMediaPlaybackState.Ready) startupTrace.Mark("media-ready");
                if (playback.State == EmbeddedMediaPlaybackState.Playing) startupTrace.Mark("playing");
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
                        var current = session.GetEmbeddedMediaState(document);
                        // The broker may reject an observation after a command or
                        // document changes. Only accepted observations may change
                        // the host controls, and an older async continuation must
                        // not overwrite a newer accepted playback state.
                        if (current is not null && published.Sequence > lastPlaybackSequence &&
                            (current.Declaration.PendingCommand is null ||
                             current.Declaration.PendingCommand == state.Declaration.PendingCommand))
                        {
                            lastPlayback = playback;
                            lastPlaybackSequence = published.Sequence;
                        }
                    }
                    finally { --pendingSubmissions; }
                }
            }
            if (!Current()) return;
            if (observed.Type == "back" && AcceptsInput) BackRequested?.Invoke();
            StateChanged?.Invoke();
            Pump();
        }
        catch (OperationCanceledException) when (retired || session.GetEmbeddedMediaState(document) is null) { }
        catch (WidgetPresentationSessionException error) when (IsSupersededMedia(error))
        {
            // A completed browser operation may race a newer widget command or a
            // document retirement. Drop its terminal; never replay it or stop a
            // healthy browser because it no longer owns completion authority.
            Diagnostic?.Invoke(error.Code);
            if (Current()) { StateChanged?.Invoke(); Pump(); }
        }
        catch (Exception error) when (!IsFatal(error))
        { if (!retired) { ObservationFailed?.Invoke(error); Fault("media-observation-failed"); } }
    }

    private async Task ActivateAsync(EmbeddedMediaObservation observed)
    {
        if (!Current()) return;
        if (!AcceptsPlaybackActivation) { CancelActivation(); return; }
        if (core is null)
        { Fault("media-activation-unavailable"); return; }
        // WinUI owns the WebView2 controller and exposes no SendMouseInput. This fixed
        // browser input operation is host-only; widgets cannot choose methods or payloads.
        var currentCore = core;
        // Adapter bounds and CDP pointer coordinates are CSS pixels. WinUI's
        // ActualWidth/Height are DIPs and can differ after interface/DPI scaling
        // or fractional layout. Validate against the browser's own CSS viewport.
        using var metrics = JsonDocument.Parse(await currentCore.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
        if (!Current() || !AcceptsPlaybackActivation || transport.PendingCommandId != observed.CommandId) { CancelActivation(); return; }
        var viewport = metrics.RootElement.GetProperty("cssLayoutViewport");
        var width = Math.Min(observed.Width, viewport.GetProperty("clientWidth").GetDouble() - observed.X);
        var height = Math.Min(observed.Height, viewport.GetProperty("clientHeight").GetDouble() - observed.Y);
        // A resize can clip the reported action between armed and input delivery.
        // Activate only inside the intersection; no intersection cancels that
        // gesture rather than destroying the otherwise healthy media document.
        if (width <= 0 || height <= 0) { CancelActivation(); return; }
        var x = observed.X + width / 2; var y = observed.Y + height / 2;
        await currentCore.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", PointerMessage("mousePressed", x, y, 1));
        if (!Current()) return; // Closing the controller also releases browser input.
        if (!AcceptsPlaybackActivation || transport.PendingCommandId != observed.CommandId)
        {
            // Release outside the action if host authority changed during the asynchronous press.
            await currentCore.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", "{\"type\":\"mouseReleased\",\"x\":-1,\"y\":-1,\"button\":\"left\",\"buttons\":0,\"clickCount\":0}");
            CancelActivation();
            return;
        }
        await currentCore.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", PointerMessage("mouseReleased", x, y, 0));
    }

    private static string PointerMessage(string type, double x, double y, int buttons) =>
        new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = type, ["x"] = x, ["y"] = y, ["button"] = "left", ["buttons"] = buttons, ["clickCount"] = 1,
        }.ToJsonString();

    private void CancelActivation()
    {
        if (retired || core is null || transport.CancelActivation(out var canceled) is not { } reset) return;
        // Abort the adapter operation without closing its browser or existing audio.
        // The reset is handled by the already shipped SDK runtime; no script injection.
        Send(reset);
        Diagnostic?.Invoke("media-activation-canceled");
        if (canceled is null) return;
        var task = PublishCancellationAsync(canceled);
        observations.Add(task);
        _ = ObserveAsync();
        async Task ObserveAsync() { try { await task; } finally { observations.Remove(task); } }
    }

    private async Task PublishCancellationAsync(EmbeddedMediaPlaybackCommand command)
    {
        ++pendingSubmissions;
        try
        {
            var state = session.GetEmbeddedMediaState(document);
            if (state?.Declaration.PendingCommand is not { } pending || pending.Sequence != command.Sequence || pending.MediaKey != command.MediaKey) return;
            var value = new EmbeddedMediaPlaybackEvent
            {
                SessionId = document.SessionId, Sequence = Interlocked.Increment(ref nextPlaybackObservation),
                CommandSequence = command.Sequence, MediaKey = command.MediaKey, State = EmbeddedMediaPlaybackState.Error,
                PositionSeconds = lastPlayback?.PositionSeconds ?? 0, DurationSeconds = lastPlayback?.DurationSeconds ?? 0,
                Volume = lastPlayback?.Volume ?? 1, PlaybackRate = lastPlayback?.PlaybackRate ?? 1,
                Muted = lastPlayback?.Muted ?? false, Loop = lastPlayback?.Loop ?? false, ErrorCode = "activation-canceled",
            };
            await session.SendEmbeddedMediaPlaybackEventAsync(document, value, lifetime.Token);
        }
        catch (OperationCanceledException) when (retired || session.GetEmbeddedMediaState(document) is null) { }
        catch (WidgetPresentationSessionException error) when (IsSupersededMedia(error)) { Diagnostic?.Invoke(error.Code); }
        catch (Exception error) when (!IsFatal(error))
        { if (!retired) { ObservationFailed?.Invoke(error); Fault("media-cancellation-report-failed"); } }
        finally { --pendingSubmissions; if (!retired) Pump(); }
    }

    private static bool IsSupersededMedia(WidgetPresentationSessionException error) =>
        error.Code is "embedded_media_stale" or "embedded_media_command_stale";

    private void Pump()
    {
        if (!Current() || core is null || pendingSubmissions != 0 || transport.Busy || !transport.Ready) return;
        var command = session.GetEmbeddedMediaState(document)?.Declaration.PendingCommand;
        // Gesture-requiring playback waits for a visible, uncovered viewport.
        if (command is null || command.Kind == EmbeddedMediaPlaybackCommandKind.Play && !AcceptsPlaybackActivation) return;
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
            if (transport.AwaitingActivation) Diagnostic?.Invoke("media-activation-requested");
        }
        catch (Exception error) when (!IsFatal(error)) { Fault("media-command-transport-failed"); }
    }
    private void OnDeadline(object? sender, object args) => Fault("media-adapter-timeout");
    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        if (retired) return;
        // WebView2 recovers these subprocesses itself. Closing the controller
        // here would turn a recoverable GPU/utility failure into lost playback.
        // Main/frame renderer failures still retire the sealed media document's
        // native surface; an essential provider frame cannot be silently ignored.
        var kind = args.ProcessFailedKind;
        var automaticRecovery = kind is CoreWebView2ProcessFailedKind.GpuProcessExited or
            CoreWebView2ProcessFailedKind.UtilityProcessExited or CoreWebView2ProcessFailedKind.SandboxHelperProcessExited or
            CoreWebView2ProcessFailedKind.PpapiPluginProcessExited or CoreWebView2ProcessFailedKind.PpapiBrokerProcessExited;
        Diagnostic?.Invoke($"media-process kind={kind} reason={args.Reason} exitCode={args.ExitCode} automaticRecovery={automaticRecovery}");
        if (!automaticRecovery) Fault("media-browser-process-failed");
    }
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
        InputAuthority = null;
        inputEnabled = visible = false;
        lifetime.Cancel();
        deadline.Stop(); deadline.Tick -= OnDeadline;
        browser.Loaded -= OnLoaded;
        browser.IsHitTestVisible = false;
        browser.Visibility = Visibility.Collapsed;
        retirement = FinishRetirementAsync();
    }

    private async Task FinishRetirementAsync()
    {
        // WebView2 callbacks must unwind before closing their native controller.
        await Task.Yield();
        if (initialization is { } pending) await pending;
        await PlacementCompletion;
        try
        {
            if (core is { } current)
            {
                current.NavigationStarting -= OnNavigationStarting;
                current.NavigationCompleted -= OnNavigationCompleted;
                current.FrameNavigationStarting -= OnFrameNavigationStarting;
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
            try { browser.Close(); }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
            { Diagnostic?.Invoke("media-close-after-process-exit"); }
        }
        try { await Task.WhenAll(observations.ToArray()); }
        finally { lifetime.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (retirement is { } pending) await pending;
    }
}
