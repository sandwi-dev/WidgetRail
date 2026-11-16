using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetRail.Playnite;

/// <summary>One bounded, authenticated loopback listener. No remote binding or eval.</summary>
public sealed class LocalApiServer : IDisposable
{
    private readonly string token;
    private readonly ILocalApiDispatcher dispatcher;
    private readonly string instanceId;
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private readonly SemaphoreSlim admission = new SemaphoreSlim(IntegrationIdentity.MaximumConcurrentRequests);
    private readonly object gate = new object();
    private readonly HashSet<Task> active = new HashSet<Task>();
    private HttpListener? listener;
    private Task? acceptLoop;
    private Task? shutdown;
    public int Port { get; private set; }
    public int ActiveRequests { get { lock (gate) return active.Count; } }
    public Action<string>? Diagnostic { get; set; }

    public LocalApiServer(string token, string instanceId, ILocalApiDispatcher dispatcher)
    {
        if (!IntegrationCredentials.ValidToken(token)) throw new ArgumentException("Invalid integration token.", nameof(token));
        if (!Guid.TryParseExact(instanceId, "D", out _)) throw new ArgumentException("Invalid server identity.", nameof(instanceId));
        this.token = token; this.instanceId = instanceId; this.dispatcher = dispatcher;
    }

    public int Start(IReadOnlyList<int>? candidates = null)
    {
        lock (gate)
        {
            if (listener != null || shutdown != null) throw new InvalidOperationException("A server instance starts once.");
            var ports = candidates ?? IntegrationIdentity.PortCandidates();
            if (ports.Count == 0 || ports.Count > 12) throw new ArgumentException("Invalid port candidates.", nameof(candidates));
            foreach (var port in ports)
            {
                if (port < 1024 || port > 65535 || port == 19821) throw new ArgumentOutOfRangeException(nameof(candidates));
                var next = new HttpListener();
                next.Prefixes.Add("http://127.0.0.1:" + port + "/");
                try { next.Start(); }
                catch (HttpListenerException error) when (error.NativeErrorCode == 183 || error.NativeErrorCode == 32 || error.NativeErrorCode == 10048)
                { next.Close(); continue; }
                catch { next.Close(); throw; }
                listener = next; Port = port;
                acceptLoop = AcceptAsync(next);
                return port;
            }
            throw new IOException("All configured local integration ports are occupied.");
        }
    }

    private async Task AcceptAsync(HttpListener owner)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var context = await owner.GetContextAsync().ConfigureAwait(false);
                if (!admission.Wait(0))
                { context.Response.StatusCode = 503; context.Response.Headers["Retry-After"] = "1"; context.Response.Close(); continue; }
                lock (gate)
                {
                    var work = Task.Run(() => ProcessAsync(context));
                    active.Add(work);
                    _ = work.ContinueWith(completed =>
                    {
                        // Observe even faults from unexpected cleanup failures.
                        if (completed.IsFaulted) Diagnostic?.Invoke("request_cleanup_failed");
                        _ = completed.Exception;
                        lock (gate) active.Remove(completed);
                        admission.Release();
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
        }
        catch (Exception error) when (error is HttpListenerException || error is ObjectDisposedException || error is OperationCanceledException)
        { if (!lifetime.IsCancellationRequested) Diagnostic?.Invoke("listener_failed"); }
    }

    private bool Authorized(string? header)
    {
        var expected = "Bearer " + token;
        if (header == null || header.Length != expected.Length) return false;
        var difference = 0;
        for (var index = 0; index < expected.Length; index++) difference |= header[index] ^ expected[index];
        return difference == 0;
    }

    private async Task ProcessAsync(HttpListenerContext context)
    {
        var responseStarted = false;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(35));
            using (deadline.Token.Register(() => { try { context.Response.Abort(); } catch { } }))
            try
            {
                var request = context.Request;
                LocalApiResponse result;
                if (request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address) ||
                    request.Url?.Host != "127.0.0.1" || !string.IsNullOrEmpty(request.Headers["Origin"]))
                    result = LocalApiResponse.Error(403, "local_client_required");
                else if (!Authorized(request.Headers["Authorization"])) result = LocalApiResponse.Error(401, "unauthorized");
                else if (request.Headers["X-WidgetRail-Instance"] != instanceId) result = LocalApiResponse.Error(409, "server_instance_changed");
                else if (request.RawUrl == null || request.RawUrl.Length > 8192) result = LocalApiResponse.Error(414, "uri_too_long");
                else if (request.ContentLength64 > IntegrationIdentity.MaximumRequestBytes) result = LocalApiResponse.Error(413, "body_too_large");
                else if (request.HasEntityBody && !string.Equals(request.ContentType?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
                    result = LocalApiResponse.Error(415, "json_required");
                else if (request.HttpMethod == "GET" && request.HasEntityBody) result = LocalApiResponse.Error(400, "unexpected_body");
                else if (request.HttpMethod != "GET" && request.HttpMethod != "POST" && request.HttpMethod != "PATCH" && request.HttpMethod != "PUT")
                    result = LocalApiResponse.Error(405, "method_not_allowed");
                else
                {
                    var body = await ReadBodyAsync(request.InputStream, deadline.Token).ConfigureAwait(false);
                    result = await dispatcher.DispatchAsync(new LocalApiRequest(request.HttpMethod, request.Url!.AbsolutePath,
                        request.QueryString, body, request.Headers["X-WidgetRail-Command"]), deadline.Token).ConfigureAwait(false);
                }
                deadline.Token.ThrowIfCancellationRequested();
                context.Response.StatusCode = result.Status;
                context.Response.ContentType = result.ContentType;
                context.Response.Headers["Cache-Control"] = "no-store";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                if (result.ETag != null) context.Response.Headers["ETag"] = result.ETag;
                responseStarted = true;
                context.Response.ContentLength64 = result.Body.Length;
                await context.Response.OutputStream.WriteAsync(result.Body, 0, result.Body.Length, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                // Once headers/body start, a disconnected client must never cause
                // a second response write (the original Bridge crash regression).
                if (!responseStarted && !deadline.IsCancellationRequested)
                {
                    try
                    {
                        var oversized = error is RequestTooLargeException;
                        var result = LocalApiResponse.Error(oversized ? 413 : 500, oversized ? "body_too_large" : "request_failed");
                        context.Response.StatusCode = result.Status;
                        context.Response.ContentType = result.ContentType;
                        responseStarted = true;
                        context.Response.ContentLength64 = result.Body.Length;
                        await context.Response.OutputStream.WriteAsync(result.Body, 0, result.Body.Length).ConfigureAwait(false);
                    }
                    catch { /* peer already closed; do not retry */ }
                }
                if (!(error is HttpListenerException || error is IOException || error is OperationCanceledException || error is ObjectDisposedException))
                    Diagnostic?.Invoke("request_failed");
            }
            finally { try { context.Response.Close(); } catch { } }
        }
    }

    private static async Task<byte[]> ReadBodyAsync(Stream stream, CancellationToken token)
    {
        using (var output = new MemoryStream())
        {
            var buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
            {
                if (output.Length + count > IntegrationIdentity.MaximumRequestBytes) throw new RequestTooLargeException();
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
    }
    private sealed class RequestTooLargeException : IOException { }

    public Task StopAsync()
    {
        lock (gate) return shutdown ?? (shutdown = StopCoreAsync());
    }
    private async Task StopCoreAsync()
    {
        lifetime.Cancel(); listener?.Close();
        if (acceptLoop != null) await acceptLoop.ConfigureAwait(false);
        Task[] pending; lock (gate) pending = active.ToArray();
        var drained = Task.WhenAll(pending);
        if (await Task.WhenAny(drained, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false) == drained)
        { try { await drained.ConfigureAwait(false); } catch { /* request failures already observed */ } }
        else
        {
            Diagnostic?.Invoke("request_drain_timed_out");
            // A provider may ignore cancellation. Keep its existing admission
            // ownership until it finishes, observe faults, and never await it
            // forever on Playnite's UI shutdown thread.
            _ = drained.ContinueWith(done => { _ = done.Exception; }, TaskScheduler.Default);
        }
        // The continuation releases each admission slot. Keep the small semaphore
        // alive until its owning server is collected rather than race that release.
    }
    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
