using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetWebSessionCapabilities
{
    public const string CapabilityId = "web.sessions.v1";
    public static WidgetCapabilityOperation<WebSessionCommand, WebSessionReply> Command { get; } =
        new(CapabilityId, "web-session.command");
}

public sealed class WidgetWebSessionService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetWebSessionService(IWidgetCapabilityClient client) => this.client = client;
    public bool IsAvailable => client.IsAvailable;
    public async ValueTask<WidgetWebSession> OpenAsync(WebSessionDefinition definition, CancellationToken cancellationToken = default)
    {
        if (definition?.IsWellFormed() != true) throw new ArgumentException("Invalid web session definition.", nameof(definition));
        var reply = await client.InvokeAsync(WidgetWebSessionCapabilities.Command,
            new(WebSessionOperation.Open) { Definition = definition }, cancellationToken).ConfigureAwait(false);
        if (reply?.Session?.IsWellFormed() != true) throw new WidgetCapabilityException("malformed_response", "Invalid web session reference.");
        return new(client, reply.Session);
    }
}

/// <summary>A host-owned page. Dispose to close it; use Reference only for optional visual presentation.</summary>
public sealed class WidgetWebSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IWidgetCapabilityClient client;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<JsonElement> messages = Channel.CreateBounded<JsonElement>(new BoundedChannelOptions(WebSessionContract.MaximumQueuedMessages)
        { SingleWriter = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim requestCapacity = new(16, 16);
    private readonly Task receiver;
    private int disposed;
    internal WidgetWebSession(IWidgetCapabilityClient client, WebSessionReference reference)
    { this.client = client; Reference = reference; receiver = ReceiveAsync(); }
    public WebSessionReference Reference { get; }
    public Task Completion => receiver;
    /// <summary>Navigate a remote session to HTTP(S), or a packaged session to one of its declared HTML assets.</summary>
    public async ValueTask NavigateAsync(string destination, CancellationToken cancellationToken = default)
    {
        DemandActive();
        if (!WebBrowserDocument.IsWebUrl(destination) && !WebSessionContract.IsAssetPath(destination)) throw new ArgumentException("Invalid navigation destination.", nameof(destination));
        await client.InvokeAsync(WidgetWebSessionCapabilities.Command, new(WebSessionOperation.Navigate)
            { Session = Reference, Destination = destination }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PostMessageAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        DemandActive();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        if (bytes.Length > WebSessionContract.MaximumMessageBytes) throw new ArgumentException("Web session message exceeds its bound.", nameof(message));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await client.InvokeAsync(WidgetWebSessionCapabilities.Command, new(WebSessionOperation.PostMessage)
            { Session = Reference, MessageBase64 = Convert.ToBase64String(bytes) }, linked.Token).ConfigureAwait(false);
    }

    public async ValueTask<TResponse> RequestAsync<TRequest, TResponse>(string method, TRequest payload,
        CancellationToken cancellationToken = default)
    {
        if (!WebSessionContract.IsKey(method)) throw new ArgumentException("Invalid web session method.", nameof(method));
        DemandActive();
        if (!await requestCapacity.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("Too many web session requests.");
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var posted = false;
        try
        {
            await PostMessageAsync(new { widgetRail = 1, kind = "request", id, method, payload }, timeout.Token).ConfigureAwait(false);
            posted = true;
            var value = await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            return value.Deserialize<TResponse>(Json)!;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (posted && !lifetime.IsCancellationRequested)
            {
                // Cooperative cancellation cannot undo effects a handler already performed.
                using var cancelDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await PostMessageAsync(new { widgetRail = 1, kind = "cancel", id }, cancelDeadline.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is OperationCanceledException or WidgetCapabilityException or ObjectDisposedException) { }
            }
            throw;
        }
        finally { pending.TryRemove(id, out _); requestCapacity.Release(); }
    }

    public async ValueTask AuthorizeLocalResourceAsync(Uri resource, CancellationToken cancellationToken = default)
    {
        DemandActive();
        if (resource is null || !WebSessionContract.IsLocalResource(resource.AbsoluteUri))
            throw new ArgumentException("An exact loopback resource is required.", nameof(resource));
        await client.InvokeAsync(WidgetWebSessionCapabilities.Command, new(WebSessionOperation.AuthorizeResource)
            { Session = Reference, ResourceUrl = resource.AbsoluteUri }, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<JsonElement> ReadMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in messages.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return message;
    }

    private async Task ReceiveAsync()
    {
        Exception? failure = null;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var reply = await client.InvokeAsync(WidgetWebSessionCapabilities.Command,
                    new(WebSessionOperation.ReadMessages) { Session = Reference }, lifetime.Token).ConfigureAwait(false);
                if (reply?.MessagesBase64 is not { Count: <= 8 }) throw new WidgetCapabilityException("malformed_response", "Invalid web session messages.");
                foreach (var encoded in reply.MessagesBase64)
                {
                    using var document = JsonDocument.Parse(WebSessionContract.DecodeMessage(encoded));
                    var value = document.RootElement.Clone();
                    if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("widgetRail", out var navigationVersion) &&
                        navigationVersion.ValueKind == JsonValueKind.Number && navigationVersion.GetInt32() == 1 &&
                        value.TryGetProperty("kind", out var navigationKind) && navigationKind.ValueKind == JsonValueKind.String && navigationKind.GetString() == "navigation")
                    {
                        foreach (var pair in pending.ToArray())
                            if (pending.TryRemove(pair.Key, out var request)) request.TrySetException(new WidgetCapabilityException("page_navigated", "The page navigated before completing the request."));
                        continue;
                    }
                    if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("widgetRail", out var version) && version.ValueKind == JsonValueKind.Number && version.GetInt32() == 1 &&
                        value.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "response" && value.TryGetProperty("id", out var identifier) &&
                        identifier.ValueKind == JsonValueKind.String)
                    {
                        // Responses to canceled/expired requests never become unsolicited app events.
                        if (!pending.TryRemove(identifier.GetString()!, out var request)) continue;
                        if (value.TryGetProperty("error", out _)) request.TrySetException(new WidgetCapabilityException("page_request_failed", "The page could not complete the request."));
                        else if (value.TryGetProperty("payload", out var payload)) request.TrySetResult(payload.Clone());
                        else request.TrySetException(new WidgetCapabilityException("malformed_response", "The page returned an invalid response."));
                    }
                    else if (!messages.Writer.TryWrite(value)) throw new WidgetCapabilityException("message_overflow", "Web session messages were not consumed in time.");
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            foreach (var request in pending.Values) request.TrySetException(failure ?? new ObjectDisposedException(nameof(WidgetWebSession)));
            messages.Writer.TryComplete(failure);
            lifetime.Cancel();
        }
    }

    private void DemandActive()
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (lifetime.IsCancellationRequested) throw new WidgetCapabilityException("session_closed", "The web session has ended. Open a new session to continue.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        try { await receiver.ConfigureAwait(false); } catch (Exception) { /* Completion exposes the failure to the owner. */ }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.InvokeAsync(WidgetWebSessionCapabilities.Command,
                new(WebSessionOperation.Close) { Session = Reference }, timeout.Token).ConfigureAwait(false);
        }
        catch (WidgetCapabilityException error) when (error.ErrorCode is "session_closed" or "lifecycle_denied")
        { /* The host owns retirement once the worker is destroying or has been replaced. */ }
        finally { lifetime.Dispose(); }
    }
}
