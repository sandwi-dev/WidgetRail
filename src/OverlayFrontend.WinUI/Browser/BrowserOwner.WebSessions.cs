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
    internal void StartHostedSessions() => hostedPump ??= PumpHostedSessionsAsync();

    private async Task PumpHostedSessionsAsync()
    {
        try
        {
            while (!retired)
            {
                var batch = await session.TakeWebSessionsAsync(lifetime.Token);
                var live = batch.LiveSessions.ToHashSet(StringComparer.Ordinal);
                foreach (var pair in entries.Where(p => p.Value.Hosted is { } reference && !live.Contains(reference.Id)).ToArray())
                    await RetireAsync(pair.Key, pair.Value);
                foreach (var work in batch.Work)
                {
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
        try
        {
            if (request.Command.Operation == WebSessionOperation.Close)
            {
                if (entries.TryGetValue(key, out var closed)) await RetireAsync(key, closed);
                return;
            }
            if (request.Command.Operation == WebSessionOperation.Open)
            {
                var definition = request.Command.Definition!;
                if (!definition.IsWellFormed() || entries.ContainsKey(key)) throw new InvalidOperationException("Invalid hosted session request.");
                if (entries.Count + retiringEntries.Count >= 4) { errorCode = "session_limit"; return; }
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
                var surface = CreateSurface(request.WidgetId, document, authority);
                surface.ConfigureHostedSession(definition, work.Resources);
                var entry = new Entry(authority, surface)
                { Hosted = request.Session, Retained = definition.Browser.Background == BrowserBackgroundMode.KeepAlive, LastUse = Environment.TickCount64 };
                entries.Add(key, entry); CreatedCount++;
                var background = ParkingFor(surface);
                await sessionBackgroundHost!.Ready.WaitAsync(TimeSpan.FromSeconds(5),lifetime.Token);
                if (!ReferenceEquals(entries.GetValueOrDefault(key), entry)) throw new InvalidOperationException("Hosted session retired while preparing its native host.");
                // Reconciliation may have parked this registered entry while
                // the new island was loading. Do not attach it a second time.
                if (!background.Children.Contains(surface)) background.Children.Add(surface);
                surface.PlacementHost = background;
                background.UpdateLayout();
                hostedMessagePumps[request.Session.Id] = ForwardHostedMessagesAsync(request.Session, surface);
                await surface.StartHostedSessionAsync(lifetime.Token);
                if (!ReferenceEquals(entries.GetValueOrDefault(key), entry)) throw new InvalidOperationException("Hosted session retired while starting.");
                ActivityChanged?.Invoke();
                Refresh();
            }
            else
            {
                if (!entries.TryGetValue(key, out var entry) || entry.Hosted != request.Session) throw new InvalidOperationException("Hosted session retired.");
                if (request.Command.Operation == WebSessionOperation.PostMessage) entry.Surface.PostHostedMessage(request.Command.MessageBase64!);
                else if (request.Command.Operation == WebSessionOperation.AuthorizeResource) entry.Surface.AuthorizeHostedResource(request.Command.ResourceUrl!);
                else if (request.Command.Operation == WebSessionOperation.Navigate) await entry.Surface.NavigateHostedAsync(request.Command.Destination!, lifetime.Token);
                else throw new InvalidOperationException("Invalid hosted session operation.");
            }
        }
        catch (OperationCanceledException) when (retired) { return; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            errorCode = "session_failed";
            // Page content and request payloads are deliberately absent from diagnostics.
            Diagnostics.FrontendFailureLog.Current.Write("web-session", null, $"operation={request.Command.Operation} code={errorCode}");
            if (request.Command.Operation == WebSessionOperation.Open && entries.TryGetValue(key, out var failed)) await RetireAsync(key, failed);
        }
        finally
        {
            if (!retired && request.Command.Operation != WebSessionOperation.Close)
                try { await session.CompleteWebSessionAsync(new(request.RequestId, errorCode), lifetime.Token); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("web-session-complete", null, "transport_unavailable"); }
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
