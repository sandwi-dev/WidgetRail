using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed record HostWebSessionWork(string RequestId, string WidgetId, BrokerWidgetIdentity Identity,
    WebSessionReference Session, WebSessionCommand Command);
public sealed record HostWebSessionBatch(IReadOnlyList<HostWebSessionWork> Work, IReadOnlyList<string> LiveSessions);
public sealed record HostWebSessionResult(string RequestId, string? ErrorCode = null);
public sealed record HostWebSessionMessages(WebSessionReference Session, IReadOnlyList<string> MessagesBase64, string? ErrorCode = null);

/// <summary>Bounded rendezvous between authenticated worker services and the frontend owner.</summary>
public sealed class WebSessionRegistry
{
    public static WebSessionRegistry Shared { get; } = new();
    private readonly object gate = new();
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Owner> owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Pending> pending = new(StringComparer.Ordinal);
    private readonly Channel<HostWebSessionWork> work = Channel.CreateBounded<HostWebSessionWork>(64);
    private sealed record Pending(Session Session, TaskCompletionSource Completion);
    private sealed class Session(Owner owner, WebSessionReference reference, WebSessionDefinition definition)
    {
        internal Owner Owner { get; } = owner;
        internal WebSessionReference Reference { get; } = reference;
        internal WebSessionDefinition Definition { get; } = definition;
        internal Channel<string> Messages { get; } = Channel.CreateBounded<string>(WebSessionContract.MaximumQueuedMessages);
        internal bool Reading;
    }

    public Owner CreateOwner(string widgetId, BrokerWidgetIdentity identity, bool allowsLocalResources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        identity.Validate();
        lock (gate)
        {
            if (owners.TryGetValue(widgetId, out var previous)) Retire(previous);
            var owner = new Owner(this, widgetId, identity, allowsLocalResources);
            owners[widgetId] = owner;
            return owner;
        }
    }

    public sealed class Owner : IDisposable
    {
        private readonly WebSessionRegistry registry;
        internal string WidgetId { get; }
        internal BrokerWidgetIdentity Identity { get; }
        internal bool AllowsLocalResources { get; }
        internal bool Retired;
        internal Owner(WebSessionRegistry registry, string widgetId, BrokerWidgetIdentity identity, bool local)
        { this.registry = registry; WidgetId = widgetId; Identity = identity; AllowsLocalResources = local; }
        public ValueTask<WebSessionReply> ExecuteAsync(WebSessionCommand command, CancellationToken token) => registry.ExecuteAsync(this, command, token);
        public void Dispose() => registry.Retire(this);
    }

    private async ValueTask<WebSessionReply> ExecuteAsync(Owner owner, WebSessionCommand command, CancellationToken token)
    {
        if (command is null || !Enum.IsDefined(command.Operation)) throw Failure("invalid_payload");
        Session session;
        lock (gate)
        {
            if (owner.Retired) throw Failure("session_closed");
            if (command.Operation is WebSessionOperation.Open or WebSessionOperation.OpenAdapted)
            {
                if (command.Definition?.IsWellFormed() != true || command.Session is not null || command.MessageBase64 is not null || command.ResourceUrl is not null || command.Destination is not null)
                    throw Failure("invalid_payload");
                if ((command.Operation == WebSessionOperation.OpenAdapted) != (command.Definition.PageAdapter is not null))
                    throw Failure("invalid_payload");
                if (sessions.Values.Any(s => ReferenceEquals(s.Owner, owner) && s.Definition.Key == command.Definition.Key)) throw Failure("session_exists");
                if (sessions.Count >= 8 || sessions.Values.Count(s => ReferenceEquals(s.Owner, owner)) >= 4) throw Failure("session_limit");
                var reference = new WebSessionReference(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
                session = new(owner, reference, command.Definition);
                sessions.Add(reference.Id, session);
            }
            else
            {
                if (command.Session?.IsWellFormed() != true || command.Definition is not null ||
                    !sessions.TryGetValue(command.Session.Id, out session!) || !ReferenceEquals(session.Owner, owner)) throw Failure("session_closed");
                if (command.Operation == WebSessionOperation.PostMessage)
                {
                    if (session.Definition.Source.Kind != WebSessionSourceKind.Package && session.Definition.PageAdapter is null || command.ResourceUrl is not null) throw Failure("messaging_unavailable");
                    try { _ = WebSessionContract.DecodeMessage(command.MessageBase64!); }
                    catch (ArgumentException) { throw Failure("invalid_payload"); }
                }
                else if (command.MessageBase64 is not null) throw Failure("invalid_payload");
                if (command.Operation == WebSessionOperation.AuthorizeResource)
                {
                    if (!owner.AllowsLocalResources || session.Definition.Source.Kind != WebSessionSourceKind.Package) throw Failure("permission_denied");
                    if (!WebSessionContract.IsLocalResource(command.ResourceUrl)) throw Failure("invalid_payload");
                }
                else if (command.ResourceUrl is not null) throw Failure("invalid_payload");
                if (command.Operation == WebSessionOperation.Navigate)
                {
                    var valid = session.Definition.Source.Kind == WebSessionSourceKind.Remote ? WebBrowserDocument.IsWebUrl(command.Destination) :
                        WebSessionContract.IsAssetPath(command.Destination) && session.Definition.Source.Resources.Any(r => r.Path == command.Destination && r.ContentType == "text/html");
                    if (!valid) throw Failure("invalid_payload");
                }
                else if (command.Destination is not null) throw Failure("invalid_payload");
                if (command.Operation == WebSessionOperation.ReadMessages)
                {
                    if (session.Reading) throw Failure("reader_exists");
                    session.Reading = true;
                }
                if (command.Operation == WebSessionOperation.Close)
                { CloseLocked(session, "session_closed"); return new(); }
            }
        }
        if (command.Operation == WebSessionOperation.ReadMessages)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                try { await session.Messages.Reader.WaitToReadAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(); }
                var result = new List<string>();
                while (result.Count < WebSessionContract.MaximumMessagesPerBatch && session.Messages.Reader.TryRead(out var message)) result.Add(message);
                lock (gate) if (!sessions.ContainsKey(session.Reference.Id)) throw Failure("session_closed");
                return new() { MessagesBase64 = result };
            }
            finally { lock (gate) session.Reading = false; }
        }
        try
        {
            await EnqueueAsync(session, command, token).ConfigureAwait(false);
            return new() { Session = session.Reference };
        }
        catch
        {
            if (command.Operation is WebSessionOperation.Open or WebSessionOperation.OpenAdapted) lock (gate) CloseLocked(session, "session_failed");
            throw;
        }
    }

    private async Task EnqueueAsync(Session session, WebSessionCommand command, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (!sessions.ContainsKey(session.Reference.Id)) throw Failure("session_closed");
            if (pending.Count >= 32 || pending.Values.Count(p => ReferenceEquals(p.Session, session)) >= 8) throw Failure("request_limit");
            pending.Add(id, new(session, completion));
            if (!work.Writer.TryWrite(new(id, session.Owner.WidgetId, session.Owner.Identity, session.Reference, command)))
            { pending.Remove(id); throw Failure("request_limit"); }
        }
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false); }
        catch (TimeoutException) { throw Failure("session_timeout"); }
        finally { lock (gate) pending.Remove(id); }
    }

    public async Task<HostWebSessionBatch> TakeAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await work.Reader.WaitToReadAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        lock (gate)
        {
            var batch = new List<HostWebSessionWork>();
            while (batch.Count < 1 && work.Reader.TryRead(out var item))
                if (item.Command.Operation == WebSessionOperation.Close || pending.ContainsKey(item.RequestId)) batch.Add(item);
            return new(batch, sessions.Keys.ToArray());
        }
    }
    public bool IsCurrent(HostWebSessionWork item)
    { lock (gate) return pending.TryGetValue(item.RequestId, out var request) && request.Session.Reference == item.Session; }
    public bool HasBackgroundSession(string instanceId)
    { lock (gate) return sessions.Values.Any(s => s.Owner.Identity.InstanceId == instanceId && s.Definition.Browser.Background == BrowserBackgroundMode.KeepAlive); }
    public bool IsOwned(string widgetId, WebSessionReference reference)
    { lock (gate) return reference?.IsWellFormed() == true && sessions.TryGetValue(reference.Id, out var session) &&
        session.Owner.WidgetId == widgetId && !session.Owner.Retired && ReferenceEquals(owners.GetValueOrDefault(widgetId), session.Owner); }
    public void Complete(HostWebSessionResult result)
    {
        lock (gate)
        {
            if (!pending.Remove(result.RequestId, out var request)) return;
            if (result.ErrorCode is null) request.Completion.TrySetResult();
            else request.Completion.TrySetException(Failure(WebSessionContract.IsKey(result.ErrorCode) ? result.ErrorCode : "session_failed"));
        }
    }
    public void Publish(HostWebSessionMessages message)
    {
        if (message.Session?.IsWellFormed() != true || message.MessagesBase64 is not { Count: <= WebSessionContract.MaximumMessagesPerBatch }) throw Failure("invalid_payload");
        foreach (var value in message.MessagesBase64)
            try { _ = WebSessionContract.DecodeMessage(value); } catch (ArgumentException) { throw Failure("invalid_payload"); }
        lock (gate)
        {
            if (!sessions.TryGetValue(message.Session.Id, out var session)) return;
            if (message.ErrorCode is not null) { CloseLocked(session, "session_failed"); return; }
            foreach (var value in message.MessagesBase64)
                if (!session.Messages.Writer.TryWrite(value)) { CloseLocked(session, "message_overflow"); break; }
        }
    }
    private void Retire(Owner owner)
    {
        lock (gate)
        {
            owner.Retired = true;
            if (ReferenceEquals(owners.GetValueOrDefault(owner.WidgetId), owner)) owners.Remove(owner.WidgetId);
            foreach (var session in sessions.Values.Where(s => ReferenceEquals(s.Owner, owner)).ToArray()) CloseLocked(session, "session_closed");
        }
    }
    private void CloseLocked(Session session, string reason)
    {
        if (!sessions.Remove(session.Reference.Id)) return;
        session.Messages.Writer.TryComplete();
        while (session.Messages.Reader.TryRead(out _)) { }
        foreach (var pair in pending.Where(p => ReferenceEquals(p.Value.Session, session)).ToArray())
        { pending.Remove(pair.Key); pair.Value.Completion.TrySetException(Failure(reason)); }
        work.Writer.TryWrite(new(Guid.NewGuid().ToString("N"), session.Owner.WidgetId, session.Owner.Identity, session.Reference,
            new(WebSessionOperation.Close) { Session = session.Reference }));
    }
    private static BrokerException Failure(string code) => new(code, "The hosted web session could not complete the operation.");
}
