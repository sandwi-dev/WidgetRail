using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal Func<CancellationToken, Task<bool>>? HideForCaptureRequested { get; set; }
    internal Func<NativeWindowPreviewTarget?>? ForegroundBeforeOverlay { get; set; }
    private Task? capturePump, capturing;
    internal Func<NativeWindowPreviewTarget?>? CurrentExternalCaptureTarget { get; set; }
    private sealed record TrayCaptureAdmission(string Widget, long VisibleSince, long Selection, bool Keyboard, bool Hidden, NativeWindowPreviewTarget? Target);
    private readonly Dictionary<long, TrayCaptureAdmission> pendingTrayCaptures = [];
    private string? nativeCaptureActivityWidget;
    private readonly HashSet<string> captureDiscoveries = [];
    internal Func<string,string?,Func<Task<bool>>,CancellationToken,Task<bool>>? OpenCaptureFolderRequested { get; set; }
    private Task? openingCaptureFolder, choosingCaptureCopy, choosingCaptureSource;
    private HostCaptureDestinationReview? reviewingDestination;
    private CancellationTokenSource? destinationReviewLifetime;
    private string? activeApprovalPicker;
    private CancellationTokenSource? approvalPickerLifetime;
    private async Task PumpCaptureAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(350));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                if (capturing is { IsCompleted: false }) continue;
                try
                {
                    var next = await owner!.Session.TakeCaptureAsync(lifetime.Token);
                    if (reviewingDestination is { } shown && next.DestinationReview?.Id != shown.Id)
                        destinationReviewLifetime?.Cancel();
                    if (next.DestinationReview is { } review && reviewingDestination is null)
                        _ = ReviewCaptureDestinationAsync(review, next.DestinationWidgetId);
                    if (activeApprovalPicker is not null && next.ApprovalPicker?.Id != activeApprovalPicker)
                        approvalPickerLifetime?.Cancel();
                    if (next.ApprovalPicker is { } picker && activeApprovalPicker is null)
                        _ = ChooseReplayApprovalAsync(picker, next.ApprovalWidgetId);
                    if (next.NeedsCapabilities)
                        await owner.Session.PublishCaptureCapabilitiesAsync(NativeWindowCapture.ImageCapabilities(), lifetime.Token, encodingProbed: false);
                    if (next.NeedsEncodingCapabilities)
                        BeginCaptureDiscovery(null);
                    foreach (var discovery in next.RecordingDiscoveries) BeginCaptureDiscovery(discovery);
                    if (next.SourceFolder is { } source && choosingCaptureSource is not { IsCompleted: false })
                        choosingCaptureSource = ChooseCaptureSourceAsync(source, next.SourceWidgetId);
                    if (next.CopyDestination is { } copy && choosingCaptureCopy is not { IsCompleted: false })
                        choosingCaptureCopy = ChooseCaptureCopyAsync(copy, next.CopyWidgetId);
                    if(next.OpenFolder is { } folder && openingCaptureFolder is not { IsCompleted:false })
                        openingCaptureFolder=OpenCaptureFolderAsync(folder,next.FolderWidgetId);
                    if (next.Edit is { } edit && editingCapture is not { IsCompleted: false })
                        editingCapture = EditCaptureAsync(edit, next.EditWidgetId);
                    if (next.Request is not null) capturing = RunCaptureAsync(next);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception error) when (BridgeDisconnected && error is not OutOfMemoryException) { break; }
                catch (Exception error) when (error is not OutOfMemoryException)
                { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-pump", error); }
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-pump", error); }
    }
    private async Task ReviewCaptureDestinationAsync(HostCaptureDestinationReview review, string? widgetId)
    {
        reviewingDestination = review;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        destinationReviewLifetime = cancellation;
        var selection = selectionVersion;
        var accepted = false;
        bool Current() => !retired && visible && foreground && activeWidget == widgetId && selectionVersion == selection;
        try
        {
            if (!Current() || !interactive || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen) return;
            var choice = await ShowHostChoiceAsync("Unlink capture?", $"Remove the link between “{review.Capture}” and “{review.Destination}”? You can undo this in the widget.",
                (HostDialogChoice[])[new("unlink", "Unlink capture")], cancellation.Token, directDecisionButtons: true);
            accepted = choice == "unlink" && Current() && !cancellation.IsCancellationRequested;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-destination-review", error); }
        finally
        {
            try { if (!retired) await owner!.Session.DecideCaptureDestinationAsync(review.Id, accepted, lifetime.Token); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-destination-decision", error); }
            destinationReviewLifetime = null;
            reviewingDestination = null;
        }
    }

    private async Task OpenCaptureFolderAsync(HostCaptureFolderRequest folder,string? widgetId)
    {
        var opened=false;
        try
        {
            if(!retired && visible && foreground && interactive && activeWidget==widgetId && !HostChoiceActive && !LocalInstallActive && !SystemFilePickerOpen && OpenCaptureFolderRequested is { } open)
                opened=await open(folder.Path,folder.FileName,()=>owner!.Session.IsCaptureFolderCurrentAsync(folder.Id,lifetime.Token),lifetime.Token);
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        { if(!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-folder",error); }
        finally
        {
            try { if(!retired) await owner!.Session.CompleteCaptureFolderAsync(folder.Id,opened,lifetime.Token); }
            catch(Exception error) when(error is not OutOfMemoryException)
            { if(!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-folder-completion",error); }
        }
    }
    private void BeginCaptureDiscovery(HostCaptureDiscovery? discovery)
    {
        var key = discovery?.Id ?? "catalog";
        if (!captureDiscoveries.Add(key)) return;
        _ = PublishAsync();
        async Task PublishAsync()
        {
            try
            {
                var capabilities = await (discovery is null ? NativeWindowCapture.GetCapabilitiesAsync() :
                    NativeWindowCapture.GetRecordingCapabilitiesAsync(discovery.Query)).WaitAsync(lifetime.Token);
                await owner!.Session.PublishCaptureCapabilitiesAsync(capabilities, lifetime.Token, discoveryId: discovery?.Id);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-discovery", error);
            }
            finally { captureDiscoveries.Remove(key); }
        }
    }
    private async Task RunCaptureAsync(BridgeCaptureTake next)
    {
        var request = next.Request!;
        var session = owner!.Session;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        using var stop = new CancellationTokenSource();
        var preparation = request.Options?.Preparation ?? new CapturePreparation();
        var duration = request.Options?.Recording?.MaximumDurationSeconds ?? CaptureLimits.VideoSeconds;
        cancellation.CancelAfter(TimeSpan.FromSeconds(120 + preparation.DelaySeconds + duration));
        var token = cancellation.Token;
        using var renewing = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task? renewal = null;
        var immediate = request.Kind == WindowCaptureKind.Screenshot && preparation.DelaySeconds == 0;
        var captureSelection = selectionVersion;
        var visibleSession = visibleSince;
        bool SameOverlay() => !retired && visible && foreground && visibleSession == visibleSince && captureSelection == selectionVersion;
        bool SameHiddenSession() => !retired && !visible && visibleSession == visibleSince && captureSelection == selectionVersion;
        var hidden = false;
        var committed = false;
        var recording = false;
        try
        {
            var widgetId = next.WidgetId ?? throw new OperationCanceledException();
            var admission = request.TraySequence is { } sequence ? pendingTrayCaptures.GetValueOrDefault(sequence) : null;
            var fromTray = request.SaveToLibrary && admission?.Widget == widgetId &&
                admission.VisibleSince == visibleSession && admission.Selection == captureSelection;
            if (request.TraySequence.HasValue && !fromTray) throw new OperationCanceledException();
            hidden = fromTray && admission!.Hidden;
            bool Current() => hidden ? SameHiddenSession() : SameOverlay() &&
                (fromTray ? (!interactive || admission!.Keyboard) && !PinnedInputActive : interactive && activeWidget == widgetId);
            if (!Current() || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen) throw new OperationCanceledException();
            var video = request.Kind == WindowCaptureKind.Video;
            var delay = preparation.DelaySeconds == 0 ? "after WidgetRail hides" : $"after a {preparation.DelaySeconds}-second countdown";
            var imageSize = request.Options?.Screenshot is { } image ? image.MaximumWidth is null ? "full-resolution" : $"up to {image.MaximumWidth} × {image.MaximumHeight}" :
                request.Resolution == CaptureImageResolution.Original ? "full-resolution" : "up to 1920 × 1080";
            var encoding = request.Options?.Recording is { } settings ? "\n"+CaptureRecordingSummary.Video(settings) : "";
            var applicationAudio = request.Options?.Recording?.Audio == CaptureAudioMode.Application;
            var audioDescription = applicationAudio ? "Application audio will be recorded. The microphone will not be recorded." : "No audio will be recorded.";
            NativeWindowCapture.FixedCaptureTarget? targetOverride = null;
            string? confirmedApplication = null;
            string description;
            if (hidden)
            {
                description = "";
            }
            else if (immediate)
            {
                var target = ForegroundBeforeOverlay?.Invoke();
                var application = target is null ? null : await TaskWindows.GetCaptureApplicationNameAsync(target, token);
                if (target is null || application is null)
                    throw new BrokerException("capture_target_unavailable", "Open the application you want to capture, then reopen WidgetRail.");
                targetOverride = new(target, (nint)hostWindow);
                confirmedApplication = application;
                description = $"Take a {imageSize} screenshot of {application} while WidgetRail stays open." +
                    (request.SaveToLibrary ? " The screenshot saves to Capture Library." : "");
            }
            else description = "WidgetRail will hide. Bring the application you want to capture to the foreground.\n\n" +
                (video ? $"Recording starts {delay} and lasts up to {duration} seconds. {audioDescription}{encoding}" :
                    $"A {imageSize} screenshot will be taken {delay}.") +
                " The application in front when capture begins will be captured." +
                (request.SaveToLibrary
                        ? video ? "\n\nReopen WidgetRail to finish early. The clip saves to Capture Library." : "\n\nThe screenshot saves to Capture Library."
                        : video && request.Options is not null ? "\n\nReopen WidgetRail to finish recording and review it." : "\n\nReopen WidgetRail when finished to review your capture.");
            if (!Current() || !await ConfirmCaptureAsync(widgetId, video, description,
                    async () => Current() && await session.IsCaptureCurrentAsync(request.RequestId, token), token, hidden) ||
                !await session.IsCaptureCurrentAsync(request.RequestId, token) || !Current()) throw new OperationCanceledException();
            if (hidden && preparation.DelaySeconds == 0)
            {
                if (admission!.Target is not { } target || target != CurrentExternalCaptureTarget?.Invoke())
                    throw new BrokerException("capture_target_unavailable", "The foreground capture target changed.");
                targetOverride = new(target, 0);
            }
            if (!hidden && !immediate && (HideForCaptureRequested is null || !await HideForCaptureRequested(token)))
                throw new OperationCanceledException();
            renewal = RenewAsync();
            CaptureIndicator? indicator = null;
            try
            {
                try { if (!immediate) indicator = new(ShellPalette, Appearance, systemUi.AnimationsEnabled); }
                catch (InvalidOperationException) { /* Countdown availability is platform-dependent; confirmation states the delay. */ }
                for (var seconds = preparation.DelaySeconds; seconds > 0; --seconds)
                {
                    token.ThrowIfCancellationRequested();
                    if (visible || hidden && !SameHiddenSession()) throw new OperationCanceledException();
                    if (preparation.ShowCountdown) indicator?.Show((video ? "Recording starts in " : "Screenshot in ") + seconds + "…");
                    await Task.Delay(1000, token);
                }
                await session.SetCaptureRecordingAsync(request.RequestId, token);
                recording = true;
                nativeCaptureActivityWidget=next.WidgetId;
                QueueWidgetStatusRefresh();
                indicator?.Show(video ? $"Recording · up to {duration}s · {(applicationAudio ? "application audio" : "no audio")}" : "Taking screenshot…");
                var result = await NativeWindowCapture.RunAsync(request, token, stop.Token, targetOverride);
                if ((immediate || hidden && !video) && !Current()) throw new OperationCanceledException();
                await renewing.CancelAsync();
                await renewal;
                var receipt = await session.CompleteCaptureWithResultAsync(new(request.RequestId, (int)result.Width, (int)result.Height, result.DurationSeconds, Target: result.Target)
                    { Properties = result.Properties, ResolvedRecording = result.ResolvedRecording }, token);
                committed = receipt.Current;
                if (request.SaveToLibrary)
                {
                    var kind = video ? "Clip" : "Screenshot";
                    var name = receipt.ApplicationName ?? confirmedApplication;
                    var message = name is null ? $"{kind} saved." : $"{kind} saved from {name}.";
                    await ShowCaptureResultAsync(widgetId, request.RequestId, committed ? message : "Capture could not be saved.");
                }
            }
            finally { indicator?.Dispose(); }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!retired)
            {
                if (error is not OperationCanceledException) Diagnostics.FrontendFailureLog.Current.Write("capture-run", error);
                try { await session.CompleteCaptureAsync(new(request.RequestId, 0, 0, 0,
                    error is OperationCanceledException ? "cancelled" : error is BrokerException configuration ? configuration.Code :
                    error.HResult == unchecked((int)0x800700DF) ? "capture_too_large" : "capture_target_unavailable"), lifetime.Token); }
                catch (Exception report) when (report is not OutOfMemoryException) { Diagnostics.FrontendFailureLog.Current.Write("capture-report", report); }
                if (request.SaveToLibrary && error is not OperationCanceledException && next.WidgetId is { } widget)
                    await ShowCaptureResultAsync(widget, request.RequestId, error is BrokerException { Code: "capture_target_unavailable" } ||
                        immediate && error is UnauthorizedAccessException ? "Bring the application you want to capture to the foreground and try again." : "Capture could not be saved.");
            }
        }
        finally
        {
            nativeCaptureActivityWidget=null;
            QueueWidgetStatusRefresh();
            cancellation.Cancel();
            if (renewal is not null) await renewal;
            if (!committed) { try { File.Delete(request.OutputPath); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        async Task RenewAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(300, renewing.Token);
                    var state = await session.GetCaptureStateAsync(request.RequestId, renewing.Token);
                    if (retired || !state.Current) { cancellation.Cancel(); return; }
                    if (state.StopRequested || visible && recording && request.Options?.Recording is not null) stop.Cancel();
                    else if (hidden ? !SameHiddenSession() : immediate ? !SameOverlay() : visible) { cancellation.Cancel(); return; }
                }
            }
            catch (OperationCanceledException) when (renewing.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException) { cancellation.Cancel(); }
        }
    }

    private async Task ShowCaptureResultAsync(string widget, string requestId, string message)
    {
        if (retired || notificationCenter is not { } center) return;
        await center.Ready;
        if (retired || !ReferenceEquals(notificationCenter, center)) return;
        var key = "capture-result." + requestId;
        center.Publish(widget, replayNotificationOwner, new(key, new WidgetNotification(key, "Capture Library")
        {
            Body = message, Visibility = WidgetNotificationVisibility.Always,
            Interaction = WidgetNotificationInteraction.Informational, Retention = WidgetNotificationRetention.BannerOnly
        }), () => !retired && ReferenceEquals(notificationCenter, center));
    }
}
