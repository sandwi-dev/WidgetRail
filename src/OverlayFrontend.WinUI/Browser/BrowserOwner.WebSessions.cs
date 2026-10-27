using System.Security.Cryptography;
using System.Text;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    private Task? hostedPump;
    private readonly HashSet<Task> hostedOperations = [];
    private readonly Dictionary<string, Task> hostedMessagePumps = [];
    private readonly BrowserSessionStartups hostedStartups = new();
    private IEnumerable<(string Widget, string Document)> BrowserResidentKeys => entries.Keys.Concat(retiringEntries).Concat(captureRetirements.Keys);
    private int BrowserResidencyCount => hostedStartups.CountResidents(BrowserResidentKeys);
#if ENABLE_WIDGET_VALIDATION
    internal Func<HostWebSessionWork, string, CancellationToken, Task<WidgetRail.PlatformSettings.BrowserProfileRecord?>>? RegisterHostedProfileForValidation { get; set; }
    internal Action<BrowserSurface>? BeforeHostedConfigureForValidation { get; set; }
#endif
    private Task<WidgetRail.PlatformSettings.BrowserProfileRecord?> RegisterHostedProfileAsync(HostWebSessionWork request, string name, CancellationToken token)
    {
#if ENABLE_WIDGET_VALIDATION
        if (RegisterHostedProfileForValidation is { } register) return register(request, name, token);
#endif
        return profileLifecycle.RegisterAsync(request.WidgetId, name, null, token);
    }
    internal void StartHostedSessions() => hostedPump ??= PumpHostedSessionsAsync();

    private async Task PumpHostedSessionsAsync()
    {
        try
        {
            while (!retired)
            {
                var batch = await session.TakeWebSessionsAsync(lifetime.Token);
                if(batch.LiveRequests is { } liveRequests)
                {
                    var current=liveRequests.ToHashSet(StringComparer.Ordinal);
                    foreach(var request in hostedDecisionRequests.Where(p=>!current.Contains(p.Key)).ToArray())request.Value.Cancel();
                }
                var live = batch.LiveSessions.ToHashSet(StringComparer.Ordinal);
                hostedStartups.RetireMissing(live);
                foreach (var pair in entries.Where(p => p.Value.Hosted is { } reference && !live.Contains(reference.Id)).ToArray())
                    await RetireAsync(pair.Key, pair.Value);
                foreach (var work in batch.Work)
                {
                    if(work.Request.Cancelled)
                    {
                        if(hostedDecisionRequests.TryGetValue(work.Request.RequestId,out var cancellation))cancellation.Cancel();
                        continue;
                    }
                    var operation = HandleHostedWorkAsync(work);
                    if (!operation.IsCompleted) hostedOperations.Add(operation);
                }
                hostedOperations.RemoveWhere(task => task.IsCompleted);
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!retired) Diagnostics.FrontendFailureLog.Current.Write("web-session-pump", error);
            foreach (var pair in entries.Where(p => p.Value.Hosted is not null).ToArray()) await RetireAsync(pair.Key, pair.Value);
        }
    }

    private async Task HandleHostedWorkAsync(BridgeWebSessionWork work)
    {
        var request = work.Request;
        var key = (Widget: request.WidgetId, Document: request.Session.Id);
        string? errorCode = null;
        WebSessionMicrophoneResult? microphone=null;
        WebCaptureStreamStatus? capture=null;
        Entry? openedEntry = null;
        BrowserSurface? unregisteredSurface = null;
        using var decisionCancellation=request.Command.Operation is WebSessionOperation.RequestMicrophone or WebSessionOperation.PrepareWebCapture or WebSessionOperation.PrepareWebCaptureConstrained
            ? CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token):null;
        if(decisionCancellation is not null)hostedDecisionRequests.Add(request.RequestId,decisionCancellation);
        try
        {
            if (request.Command.Operation == WebSessionOperation.Close)
            {
                hostedStartups.Retire(key);
                if (entries.TryGetValue(key, out var closed)) await RetireAsync(key, closed);
                return;
            }
            if (request.Command.Operation is WebSessionOperation.Open or WebSessionOperation.OpenAdapted or WebSessionOperation.OpenBackend)
            {
                var definition = request.Command.Definition!;
                if (!definition.IsWellFormed() || entries.ContainsKey(key)) throw new InvalidOperationException("Invalid hosted session request.");
                using var startup = hostedStartups.TryReserve(key, BrowserResidentKeys, lifetime.Token);
                if (startup is null)
                { errorCode = "session_limit"; return; }
                var originKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    request.Identity.PackageId + "\n" + request.Identity.PublisherId + "\n" + definition.Key))).ToLowerInvariant();
                var url = definition.Source.Kind == WebSessionSourceKind.Package
                    ? "https://app-" + originKey[..32] + ".widgetrail.internal/" + definition.Source.Location : definition.Source.Location;
                var options = definition.Browser.ProfileKey == "default"
                    ? definition.Browser with { ProfileKey = "session-" + (definition.Browser.Storage == BrowserStorage.Private ? request.Session.Id : originKey[..32]) }
                    : definition.Browser;
                var document = new WebBrowserDocument(request.Session.Id, 1, url, definition.AccessibleName)
                { Options = options, InteractionMode = BrowserInteractionMode.ActivateToInteract };
                var authority = session.GetState(request.WidgetId)?.LastGood?.Authority ??
                    new WidgetPresentationAuthority(request.WidgetId, "", "", 0, request.Identity.InstanceId, 0, "");
                var ownedProfile = await RegisterHostedProfileAsync(request, BrowserProfileIdentity.Name(request.WidgetId, document), startup.Token)
                    ?? throw new InvalidOperationException("This widget was removed.");
                BrowserSurface surface = null!;
                Entry entry = null!;
                if (!startup.TryCommit(() =>
                {
                    surface = CreateSurface(request.WidgetId, document, authority, profileName: ownedProfile.Name);
                    unregisteredSurface = surface;
#if ENABLE_WIDGET_VALIDATION
                    BeforeHostedConfigureForValidation?.Invoke(surface);
#endif
                    surface.ConfigureHostedSession(definition, work.Resources);
                    surface.HostedCaptureChanged += status => _ = PublishHostedCaptureAsync(request.Session, status);
                    entry = new Entry(authority, surface)
                    { Hosted = request.Session, Retained = definition.Browser.Background == BrowserBackgroundMode.KeepAlive, LastUse = Environment.TickCount64 };
                    entries.Add(key, entry); CreatedCount++;
                    openedEntry = entry;
                    unregisteredSurface = null;
                })) throw new OperationCanceledException("Hosted startup retired during profile registration.", startup.Token);
                var background = ParkingFor(surface);
                await sessionBackgroundHost!.Ready.WaitAsync(TimeSpan.FromSeconds(5),startup.Token);
                if (!startup.IsCurrent || !ReferenceEquals(entries.GetValueOrDefault(key), entry)) throw new InvalidOperationException("Hosted session retired while preparing its native host.");
                // Reconciliation may have parked this registered entry while
                // the new island was loading. Do not attach it a second time.
                if (!background.Children.Contains(surface)) background.Children.Add(surface);
                surface.PlacementHost = background;
                background.UpdateLayout();
                hostedMessagePumps[request.Session.Id] = ForwardHostedMessagesAsync(request.Session, surface);
                await surface.StartHostedSessionAsync(startup.Token);
                if (!startup.IsCurrent || !ReferenceEquals(entries.GetValueOrDefault(key), entry)) throw new InvalidOperationException("Hosted session retired while starting.");
                ActivityChanged?.Invoke();
                Refresh();
            }
            else
            {
                if (!entries.TryGetValue(key, out var entry) || entry.Hosted != request.Session)
                {
                    if (request.Command.Operation == WebSessionOperation.StopWebCapture &&
                        captureRetirements.TryGetValue(key, out var retiring) && retiring.Hosted == request.Session)
                    {
                        capture = await RetryRetiredCaptureStopAsync(key, retiring, request.Command.CaptureTicket!);
                        return;
                    }
                    // A stop may arrive after complete teardown (including a
                    // cancelled prepare that never created a surface). The
                    // broker still validates the exact original request/ticket.
                    if (request.Command.Operation == WebSessionOperation.StopWebCapture)
                    {
                        capture = new(WebCaptureStreamPhase.Stopped) { Ticket = request.Command.CaptureTicket, Reason = "session_closed" };
                        return;
                    }
                    throw new InvalidOperationException("Hosted session retired.");
                }
                if (request.Command.Operation == WebSessionOperation.PostMessage) await entry.Surface.PostHostedMessageAsync(request.Command.MessageBase64!);
                else if (request.Command.Operation == WebSessionOperation.AuthorizeResource) entry.Surface.AuthorizeHostedResource(request.Command.ResourceUrl!);
                else if (request.Command.Operation == WebSessionOperation.Navigate) await entry.Surface.NavigateHostedAsync(request.Command.Destination!, lifetime.Token);
                else if(request.Command.Operation==WebSessionOperation.RequestMicrophone)
                    microphone=RequestMicrophone is { } requestMicrophone
                        ? await requestMicrophone(entry.Surface,decisionCancellation!.Token)
                        : new(WebSessionMicrophoneState.Unavailable);
                else if (request.Command.Operation is WebSessionOperation.PrepareWebCapture or WebSessionOperation.PrepareWebCaptureConstrained)
                    capture = ChooseWebCaptureSource is { } chooser
                        ? await entry.Surface.PrepareHostedCaptureAsync(request.Command.CaptureTicket!, request.Command.CaptureOptions!,
                            token => chooser(entry.Surface, request.Command.CaptureOptions!, request.Command.CaptureQualities, token), decisionCancellation!.Token,
                            request.Command.CaptureQualities)
                        : new(WebCaptureStreamPhase.Unavailable) { Ticket = request.Command.CaptureTicket, Reason = "chooser_unavailable" };
                else if (request.Command.Operation == WebSessionOperation.StopWebCapture)
                    capture = await entry.Surface.StopHostedCaptureAsync(request.Command.CaptureTicket!);
                else throw new InvalidOperationException("Invalid hosted session operation.");
            }
        }
        catch (OperationCanceledException) when (retired) { return; }
        catch(OperationCanceledException) when(decisionCancellation is not null)
        {
            if (request.Command.Operation == WebSessionOperation.RequestMicrophone) microphone=new(WebSessionMicrophoneState.Cancelled);
            else capture=new(WebCaptureStreamPhase.Cancelled) { Ticket=request.Command.CaptureTicket };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            errorCode = "session_failed";
            // Page content and request payloads are deliberately absent from diagnostics.
            Diagnostics.FrontendFailureLog.Current.Write("web-session", null, $"operation={request.Command.Operation} code={errorCode}");
            // A failed duplicate or a late pre-registration result owns no other
            // entry. Only retire the exact browser this attempt registered.
            if (openedEntry is not null && ReferenceEquals(entries.GetValueOrDefault(key), openedEntry))
                await RetireAsync(key, openedEntry);
        }
        finally
        {
            if (unregisteredSurface is { } abandoned)
                try { await abandoned.DisposeAsync(); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { errorCode = "session_failed"; Diagnostics.FrontendFailureLog.Current.Write("web-session", null, "startup_cleanup_failed"); }
            if (!retired && request.Command.Operation != WebSessionOperation.Close)
                try { await session.CompleteWebSessionAsync(new(request.RequestId, errorCode,microphone,capture), lifetime.Token); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("web-session-complete", null, "transport_unavailable"); }
            if(decisionCancellation is not null)hostedDecisionRequests.Remove(request.RequestId);
        }
    }

    private async Task ForwardHostedMessagesAsync(WebSessionReference reference, BrowserSurface surface)
    {
        try
        {
            while (await surface.HostedMessages.WaitToReadAsync(lifetime.Token))
            {
                var messages = new List<string>();
                while (messages.Count < WebSessionContract.MaximumMessagesPerBatch && surface.HostedMessages.TryRead(out var message)) messages.Add(message);
                await session.PublishWebSessionMessagesAsync(new(reference, messages), lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!retired)
                try { await session.PublishWebSessionMessagesAsync(new(reference, [], "session_failed"), lifetime.Token); }
                catch (Exception failure) when (failure is not OutOfMemoryException) { }
        }
        finally { hostedMessagePumps.Remove(reference.Id); }
    }
}
