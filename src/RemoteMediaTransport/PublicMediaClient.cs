using System.Net;
using System.Net.Http.Headers;

namespace WidgetRail.RemoteMediaTransport;

public sealed record PublicMediaLimits
{
    public bool IdentifyProduct { get; init; }
    public int MaximumRedirects { get; init; } = 4;
    public int MaximumConcurrentRequests { get; init; } = 4;
    public int MaximumQueuedRequests { get; init; } = 64;
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan OverallTimeout { get; init; } = TimeSpan.FromSeconds(30);
    internal void Validate()
    {
        if (MaximumRedirects is < 0 or > 8 || MaximumConcurrentRequests is < 1 or > 16 ||
            MaximumQueuedRequests is < 0 or > 256 || ConnectTimeout <= TimeSpan.Zero || ConnectTimeout > TimeSpan.FromSeconds(30) ||
            OverallTimeout < ConnectTimeout || OverallTimeout > TimeSpan.FromMinutes(5)) throw new ArgumentException("Invalid media download limits.");
    }
}

/// <summary>Owned bytes, never a native URL that could bypass the checked transport.</summary>
public sealed class PublicMediaBytes
{
    public ReadOnlyMemory<byte> Bytes { get; }
    public string? MediaType { get; }
    internal Uri EffectiveUri { get; }
    internal PublicMediaBytes(byte[] bytes, string? mediaType, Uri effectiveUri)
    { Bytes = bytes; MediaType = mediaType; EffectiveUri = effectiveUri; }
}

/// <summary>
/// Anonymous bounded downloads. No default credentials, cookies, proxy, automatic
/// redirects, content decompression or raw network exceptions cross this boundary.
/// </summary>
public sealed partial class PublicMediaClient : IAsyncDisposable
{
    private readonly HttpClient client;
    private readonly PublicMediaLimits limits;
    private readonly SemaphoreSlim active;
    private readonly CancellationTokenSource shutdown = new();
    private readonly CancellationToken shutdownToken;
    private readonly object sync = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int admitted;
    private bool retiring;

    public PublicMediaClient(PublicMediaLimits? limits = null) : this(CreateHandler(limits ?? new()), limits ?? new()) { }

    // Internal injection is for deterministic transport tests. Production callers
    // cannot replace DNS/socket validation or enable browser credential sharing.
    internal PublicMediaClient(HttpMessageHandler handler, PublicMediaLimits limits)
    {
        limits.Validate(); this.limits = limits;
        active = new(limits.MaximumConcurrentRequests, limits.MaximumConcurrentRequests);
        shutdownToken = shutdown.Token;
        client = new(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact };
        if (limits.IdentifyProduct) client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WidgetRail", "1.0"));
    }

    internal static SocketsHttpHandler CreateHandler(PublicMediaLimits limits)
    {
        limits.Validate();
        return new()
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            Credentials = null, PreAuthenticate = false, AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = limits.ConnectTimeout, MaxConnectionsPerServer = limits.MaximumConcurrentRequests,
            MaxResponseHeadersLength = 16, MaxResponseDrainSize = 0, PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30), ConnectCallback = PublicMediaConnector.ConnectAsync
        };
    }

    public Task<PublicMediaBytes> DownloadAsync(Uri source, PublicMediaPolicy policy, int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(policy);
        if (maximumBytes is < 1 or > 512 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        policy.Demand(source);
        return ExecuteAsync(async token =>
        {
            var download = await SendAsync(source, policy, token).ConfigureAwait(false);
            using var response = download.Response;
            var bytes = await ReadBodyAsync(response, maximumBytes, token).ConfigureAwait(false);
            return new PublicMediaBytes(bytes, response.Content.Headers.ContentType?.MediaType, download.EffectiveUri);
        }, cancellationToken);
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(retiring, this);
            if (admitted >= limits.MaximumConcurrentRequests + limits.MaximumQueuedRequests)
                throw new PublicMediaException(PublicMediaFailure.Busy);
            ++admitted;
        }
        var entered = false;
        try
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownToken);
            lifetime.CancelAfter(limits.OverallTimeout);
            var token = lifetime.Token;
            await active.WaitAsync(token).ConfigureAwait(false); entered = true;
            return await operation(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !shutdownToken.IsCancellationRequested)
        { throw new PublicMediaException(PublicMediaFailure.Timeout); }
        catch (PublicMediaException) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or System.Net.Sockets.SocketException)
        { throw new PublicMediaException(PublicMediaFailure.Network); }
        finally
        {
            if (entered) active.Release();
            lock (sync) { if (--admitted == 0 && retiring) drained.TrySetResult(); }
        }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, int maximumBytes, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength is { } length && length > maximumBytes)
            throw new PublicMediaException(PublicMediaFailure.TooLarge);
        if (response.Content.Headers.ContentEncoding.Any(value => !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new PublicMediaException(PublicMediaFailure.ResponseRejected);
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream((int)Math.Min(maximumBytes, response.Content.Headers.ContentLength ?? 64 * 1024));
        var buffer = new byte[Math.Min(maximumBytes + 1, 64 * 1024)];
        while (true)
        {
            var count = (int)Math.Min(buffer.Length, maximumBytes - output.Length + 1);
            var read = await input.ReadAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > maximumBytes) throw new PublicMediaException(PublicMediaFailure.TooLarge);
            output.Write(buffer, 0, read);
        }
        token.ThrowIfCancellationRequested();
        if (output.Length == 0 || response.Content.Headers.ContentLength is { } expected && output.Length != expected)
            throw new PublicMediaException(PublicMediaFailure.ResponseRejected);
        return output.ToArray();
    }

    private async Task<(HttpResponseMessage Response, Uri EffectiveUri)> SendAsync(Uri source, PublicMediaPolicy policy, CancellationToken token,
        RangeHeaderValue? range = null, string? version = null)
    {
        var current = source;
        var visited = new HashSet<Uri>();
        for (var redirects = 0; ; ++redirects)
        {
            policy.Demand(current);
            if (!visited.Add(current)) throw new PublicMediaException(PublicMediaFailure.ResponseRejected);
            using var request = new HttpRequestMessage(HttpMethod.Get, current) { Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            request.Headers.Range = range;
            if (version is not null) request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(version));
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK || range is not null && response.StatusCode == HttpStatusCode.PartialContent)
                return (response, current);
            using (response)
            {
                // A strong ETag belongs to the resource that supplied the first
                // block. A redirect from that resource requires a fresh open.
                if (version is not null || response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
                    HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect) ||
                    redirects >= limits.MaximumRedirects || response.Headers.Location is not { } location)
                    throw new PublicMediaException(PublicMediaFailure.ResponseRejected);
                if (!Uri.TryCreate(current, location, out var next)) throw new PublicMediaException(PublicMediaFailure.DestinationRejected);
                current = next;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        bool first;
        lock (sync) { first = !retiring; retiring = true; if (admitted == 0) drained.TrySetResult(); }
        if (!first) { await disposed.Task.ConfigureAwait(false); return; }
        try
        {
            shutdown.Cancel();
            await drained.Task.ConfigureAwait(false);
            client.Dispose(); active.Dispose(); shutdown.Dispose();
        }
        finally { disposed.TrySetResult(); }
    }
}
