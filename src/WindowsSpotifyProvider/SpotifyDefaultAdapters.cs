using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace GameBarAlternative.WindowsSpotifyProvider;

internal sealed class SpotifyHttpTransport : ISpotifyHttpTransport
{
    internal const int MaximumResponseBytes = 512 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly HttpClient _client;

    internal SpotifyHttpTransport()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = 4,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        };
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public async Task<SpotifyHttpResponse> SendAsync(
        SpotifyHttpRequest request, CancellationToken cancellationToken)
    {
        ValidateUri(request.Uri);
        using var message = new HttpRequestMessage(request.Method, request.Uri)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        if (request.FormBody is not null)
            message.Content = new StringContent(
                request.FormBody, StrictUtf8, "application/x-www-form-urlencoded");
        if (request.Headers is not null)
        {
            foreach (var (name, value) in request.Headers)
            {
                if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    message.Headers.Authorization = AuthenticationHeaderValue.Parse(value);
                else if (!message.Headers.TryAddWithoutValidation(name, value))
                    throw new SpotifyProviderException(
                        "invalid_request", "Spotify request header is invalid.");
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var response = await _client.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            var body = await ReadBodyAsync(response, timeout.Token).ConfigureAwait(false);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (response.Headers.RetryAfter is { } retryAfter)
                headers["Retry-After"] = retryAfter.ToString();
            return new SpotifyHttpResponse((int)response.StatusCode, body, headers);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new SpotifyProviderException(
                "spotify_timeout", "Spotify did not respond in time.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new SpotifyProviderException(
                "spotify_unavailable", "Spotify is unavailable.", exception);
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            throw new SpotifyProviderException("invalid_request", "Spotify URI is invalid.");
        var allowed = uri.Host.Equals("api.spotify.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith("/v1/", StringComparison.Ordinal) ||
            uri.Host.Equals("accounts.spotify.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.Equals("/api/token", StringComparison.Ordinal);
        if (!allowed || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
            throw new SpotifyProviderException("invalid_request", "Spotify URI is not trusted.");
    }

    private static async Task<string> ReadBodyAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new SpotifyProviderException(
                "response_too_large", "Spotify response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > MaximumResponseBytes)
                throw new SpotifyProviderException(
                    "response_too_large", "Spotify response is too large.");
            memory.Write(buffer, 0, read);
        }
        try { return StrictUtf8.GetString(memory.GetBuffer(), 0, (int)memory.Length); }
        catch (DecoderFallbackException exception)
        {
            throw new SpotifyProviderException(
                "invalid_response", "Spotify returned invalid text.", exception);
        }
    }
}

internal sealed class SpotifyBrowserLauncher : ISpotifyBrowserLauncher
{
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("accounts.spotify.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Equals("/authorize", StringComparison.Ordinal))
            throw new SpotifyProviderException(
                "invalid_request", "Spotify authorization URI is invalid.");
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            throw new SpotifyProviderException(
                "browser_unavailable", "The Spotify sign-in page could not be opened.", exception);
        }
    }
}

internal sealed class LoopbackSpotifyAuthorizationCallbackReceiver :
    ISpotifyAuthorizationCallbackReceiver
{
    private const int CallbackPort = 43827;
    private const int MaximumRequestHeaderBytes = 16 * 1024;
    private const int MaximumAcceptedConnections = 16;
    private const int MaximumConcurrentClients = 4;
    private static readonly TimeSpan ClientHeaderTimeout = TimeSpan.FromSeconds(2);

    public async Task<SpotifyAuthorizationCallback> ReceiveAsync(
        Uri exactRedirectUri, string expectedState, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (exactRedirectUri.AbsoluteUri != WindowsSpotifyPlatformBackend.ExactRedirectUri)
            throw new SpotifyProviderException(
                "invalid_redirect_uri", "Spotify redirect URI is not the host redirect URI.");
        if (string.IsNullOrWhiteSpace(expectedState) || expectedState.Length > 256)
            throw new SpotifyProviderException(
                "invalid_callback", "Spotify sign-in callback state was invalid.");
        var listener = new TcpListener(IPAddress.Loopback, CallbackPort)
        {
            ExclusiveAddressUse = true,
        };
        try { listener.Start(MaximumAcceptedConnections); }
        catch (SocketException exception)
        {
            throw new SpotifyProviderException(
                "callback_unavailable", "Spotify sign-in callback could not be started.", exception);
        }

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout);
        var clients = Channel.CreateBounded<TcpClient>(new BoundedChannelOptions(
            MaximumConcurrentClients)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false,
        });
        var completion = new TaskCompletionSource<SpotifyAuthorizationCallback>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptTask = AcceptClientsAsync(listener, clients.Writer, bounded.Token);
        var clientTasks = Enumerable.Range(0, MaximumConcurrentClients)
            .Select(_ => ProcessClientsAsync(
                clients.Reader, exactRedirectUri, expectedState, completion, bounded.Token))
            .ToArray();
        try
        {
            // Browsers, endpoint-security tools, and proxy helpers can open a
            // speculative loopback connection before the actual navigation.
            // A bounded set of readers prevents a silent probe from occupying
            // the only accept slot. Invalid paths, malformed requests, and
            // wrong-state callbacks cannot consume the authorization listener.
            var processing = Task.WhenAll(clientTasks.Prepend(acceptTask));
            var completed = await Task.WhenAny(completion.Task, processing)
                .ConfigureAwait(false);
            if (completed == completion.Task)
                return await completion.Task.ConfigureAwait(false);
            await processing.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (bounded.IsCancellationRequested)
                throw new OperationCanceledException(bounded.Token);
            throw new SpotifyProviderException(
                "invalid_callback", "Spotify sign-in callback was invalid.");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SpotifyProviderException(
                "authorization_timeout", "Spotify sign-in timed out.", exception);
        }
        finally
        {
            bounded.Cancel();
            listener.Stop();
            clients.Writer.TryComplete();
            while (clients.Reader.TryRead(out var pending)) pending.Dispose();
            try { await Task.WhenAll(clientTasks.Prepend(acceptTask)).ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or
                SocketException or ObjectDisposedException) { }
        }
    }

    private static async Task AcceptClientsAsync(
        TcpListener listener,
        ChannelWriter<TcpClient> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; attempt < MaximumAcceptedConnections; attempt++)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    await writer.WriteAsync(client, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }
        }
        finally { writer.TryComplete(); }
    }

    private static async Task ProcessClientsAsync(
        ChannelReader<TcpClient> reader,
        Uri exactRedirectUri,
        string expectedState,
        TaskCompletionSource<SpotifyAuthorizationCallback> completion,
        CancellationToken cancellationToken)
    {
        await foreach (var client in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            using (client)
            using (var clientHeaderLifetime =
                   CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                clientHeaderLifetime.CancelAfter(ClientHeaderTimeout);
                try
                {
                    var callback = await ReceiveClientAsync(
                        client, exactRedirectUri, expectedState, clientHeaderLifetime.Token)
                        .ConfigureAwait(false);
                    completion.TrySetResult(callback);
                    return;
                }
                catch (SpotifyProviderException exception) when (
                    exception.Code == "invalid_callback") { }
                catch (IOException) { }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // One silent local client cannot occupy a receiver for the
                    // complete authorization window.
                }
            }
        }
    }

    private static async Task<SpotifyAuthorizationCallback> ReceiveClientAsync(
        TcpClient client,
        Uri exactRedirectUri,
        string expectedState,
        CancellationToken cancellationToken)
    {
        if (client.Client.RemoteEndPoint is not IPEndPoint remote ||
            !IPAddress.IsLoopback(remote.Address))
            throw new SpotifyProviderException(
                "invalid_callback", "Spotify sign-in callback was invalid.");
        using var stream = client.GetStream();
        var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
        var lines = request.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3 || requestLine[0] != "GET" ||
            requestLine[2] != "HTTP/1.1")
            throw new SpotifyProviderException(
                "invalid_callback", "Spotify sign-in callback was invalid.");
        var host = lines.Skip(1).FirstOrDefault(line =>
            line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
        if (host is null || !host[5..].Trim().Equals(
                "127.0.0.1:43827", StringComparison.OrdinalIgnoreCase))
            throw new SpotifyProviderException(
                "invalid_callback", "Spotify sign-in callback was invalid.");
        if (!Uri.TryCreate("http://127.0.0.1:43827" + requestLine[1],
                UriKind.Absolute, out var callbackUri) ||
            callbackUri.AbsolutePath != exactRedirectUri.AbsolutePath)
            throw new SpotifyProviderException(
                "invalid_callback", "Spotify sign-in callback was invalid.");
        var query = ParseQuery(callbackUri.Query);
        var code = query.GetValueOrDefault("code");
        var state = query.GetValueOrDefault("state");
        var error = query.GetValueOrDefault("error");
        if (!string.Equals(state, expectedState, StringComparison.Ordinal) ||
            (string.IsNullOrWhiteSpace(code) == string.IsNullOrWhiteSpace(error)))
            throw new SpotifyProviderException(
                "invalid_callback", "Spotify sign-in callback was invalid.");
        try
        {
            await WriteResponseAsync(stream,
                error is not null ? "Spotify sign-in was not completed." :
                    "Spotify is connected. You can close this window.", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The verified authorization response is authoritative even when
            // the browser closes before reading the friendly completion page.
        }
        return new SpotifyAuthorizationCallback(
            code, state, error, query.GetValueOrDefault("error_description"));
    }

    private static async Task<string> ReadRequestAsync(
        NetworkStream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(1024);
        var buffer = new byte[1024];
        while (bytes.Count < MaximumRequestHeaderBytes)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            bytes.AddRange(buffer.AsSpan(0, read).ToArray());
            if (bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' &&
                bytes[^2] == '\r' && bytes[^1] == '\n')
                break;
        }
        if (bytes.Count < 4 || bytes.Count >= MaximumRequestHeaderBytes ||
            bytes[^4] != '\r' || bytes[^3] != '\n' || bytes[^2] != '\r' || bytes[^1] != '\n')
            throw new SpotifyProviderException(
                "invalid_callback", "Spotify sign-in callback was invalid.");
        try { return new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
        catch (DecoderFallbackException exception)
        {
            throw new SpotifyProviderException(
                "invalid_callback", "Spotify sign-in callback was invalid.", exception);
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        if (query.Length > 8192)
            throw new SpotifyProviderException("invalid_callback", "Spotify sign-in callback was invalid.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var name = Decode(parts[0]);
            var value = parts.Length == 2 ? Decode(parts[1]) : string.Empty;
            if (name.Length is 0 or > 64 || value.Length > 4096 || !result.TryAdd(name, value))
                throw new SpotifyProviderException("invalid_callback", "Spotify sign-in callback was invalid.");
        }
        return result;
    }

    private static string Decode(string value)
    {
        try { return Uri.UnescapeDataString(value.Replace('+', ' ')); }
        catch (UriFormatException exception)
        {
            throw new SpotifyProviderException(
                "invalid_callback", "Spotify sign-in callback was invalid.", exception);
        }
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream, string message, CancellationToken cancellationToken)
    {
        var escaped = WebUtility.HtmlEncode(message);
        var bytes = Encoding.UTF8.GetBytes(
            "<!doctype html><html><head><meta charset=utf-8><title>Game Bar Alternative</title>" +
            "</head><body><p>" + escaped + "</p></body></html>");
        var headers = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bytes.Length}\r\nConnection: close\r\n" +
            "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class SpotifyDelay : ISpotifyDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
