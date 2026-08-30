using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WidgetRail.Samples.PlayniteLibrary;

internal enum PlayniteBridgeConnectionKind
{
    NotConfigured,
    Connected,
    Unavailable,
    AuthenticationRequired,
    Incompatible,
    Malformed,
}

internal sealed record PlayniteBridgeConnectionResult(
    PlayniteBridgeConnectionKind Kind,
    string Code);

internal sealed record PlayniteBridgeResponse(int StatusCode, string JsonBody);

internal interface IPlayniteBridgeTransport : IDisposable
{
    ValueTask<PlayniteBridgeResponse> ProbeAsync(
        string bearerToken,
        CancellationToken cancellationToken);
}

internal interface IPlayniteCredentialStore
{
    ValueTask<string?> ReadAsync(CancellationToken cancellationToken);
    ValueTask SaveAsync(string token, CancellationToken cancellationToken);
    ValueTask DeleteAsync(CancellationToken cancellationToken);
}

internal interface IPlayniteBridgeClient : IDisposable
{
    ValueTask<PlayniteBridgeConnectionResult> ProbeAsync(CancellationToken cancellationToken);
    ValueTask SaveCredentialAsync(string token, CancellationToken cancellationToken);
    ValueTask DeleteCredentialAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Bounded client for the package-owned Playnite connection workflow. The
/// callable surface deliberately accepts no URI, path, method, headers, or body.
/// </summary>
internal sealed class PlayniteBridgeClient(
    IPlayniteBridgeTransport transport,
    IPlayniteCredentialStore credentials) : IPlayniteBridgeClient
{
    internal const int Port = 19821;
    internal const string CompatibilityPath = "/api/games?installed=true&limit=1&offset=0";
    internal const int MaximumTokenCharacters = 512;
    private readonly IPlayniteBridgeTransport _transport = transport ??
        throw new ArgumentNullException(nameof(transport));
    private readonly IPlayniteCredentialStore _credentials = credentials ??
        throw new ArgumentNullException(nameof(credentials));

    internal static PlayniteBridgeClient CreateDefault() => new(
        new PlayniteBridgeHttpTransport(),
        new WindowsCredentialPlayniteTokenStore());

    public async ValueTask<PlayniteBridgeConnectionResult> ProbeAsync(
        CancellationToken cancellationToken)
    {
        string? token = null;
        try
        {
            token = await _credentials.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (token is null)
                return new(PlayniteBridgeConnectionKind.NotConfigured, "credential_missing");
            var response = await _transport.ProbeAsync(token, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == 401)
            {
                await _credentials.DeleteAsync(cancellationToken).ConfigureAwait(false);
                return new(PlayniteBridgeConnectionKind.AuthenticationRequired,
                    "authentication_required");
            }
            if (response.StatusCode == 403)
                return new(PlayniteBridgeConnectionKind.AuthenticationRequired,
                    "authentication_required");
            if (response.StatusCode is 404 or 405)
                return new(PlayniteBridgeConnectionKind.Incompatible, "api_incompatible");
            if (response.StatusCode is < 200 or > 299)
                return new(PlayniteBridgeConnectionKind.Unavailable, "service_unavailable");
            return IsCompatible(response.JsonBody)
                ? new(PlayniteBridgeConnectionKind.Connected, "connected")
                : new(PlayniteBridgeConnectionKind.Malformed, "malformed_response");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PlayniteBridgeTransportException exception)
        {
            return new(
                exception.IsMalformed
                    ? PlayniteBridgeConnectionKind.Malformed
                    : PlayniteBridgeConnectionKind.Unavailable,
                exception.IsMalformed ? "malformed_response" : "service_unavailable");
        }
        catch (PlayniteCredentialException)
        {
            return new(PlayniteBridgeConnectionKind.Unavailable,
                "secret_store_unavailable");
        }
        finally
        {
            token = null;
        }
    }

    public ValueTask SaveCredentialAsync(string token, CancellationToken cancellationToken)
    {
        ValidateToken(token);
        return _credentials.SaveAsync(token, cancellationToken);
    }

    public ValueTask DeleteCredentialAsync(CancellationToken cancellationToken) =>
        _credentials.DeleteAsync(cancellationToken);

    public void Dispose() => _transport.Dispose();

    internal static void ValidateToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        int bytes;
        try { bytes = new UTF8Encoding(false, true).GetByteCount(token); }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("The Playnite Bridge token is invalid.",
                nameof(token), exception);
        }
        if (token.Length is < 16 or > MaximumTokenCharacters || bytes > MaximumTokenCharacters ||
            token.Any(character => character > 0x7e || character < 0x21 || char.IsWhiteSpace(character)))
            throw new ArgumentException("The Playnite Bridge token is invalid.", nameof(token));
    }

    private static bool IsCompatible(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                TryNonNegativeInteger(root, "total", out _) &&
                TryNonNegativeInteger(root, "offset", out var offset) && offset == 0 &&
                TryNonNegativeInteger(root, "limit", out var limit) && limit == 1 &&
                root.TryGetProperty("games", out var games) &&
                games.ValueKind == JsonValueKind.Array && games.GetArrayLength() <= 1;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryNonNegativeInteger(
        JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value) && value >= 0;
    }
}

internal sealed class PlayniteBridgeHttpTransport : IPlayniteBridgeTransport
{
    internal const int MaximumResponseBytes = 96 * 1024;
    private static readonly Uri CompatibilityUri = new(
        $"http://127.0.0.1:{PlayniteBridgeClient.Port}" +
        PlayniteBridgeClient.CompatibilityPath,
        UriKind.Absolute);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly HttpClient _http;

    internal PlayniteBridgeHttpTransport()
    {
        _http = new HttpClient(CreateHandler(), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    internal static HttpClientHandler CreateHandler() => new()
    {
            AllowAutoRedirect = false,
            UseProxy = false,
            Proxy = null,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            PreAuthenticate = false,
            Credentials = null,
            MaxConnectionsPerServer = 1,
            MaxResponseHeadersLength = 16,
    };

    public async ValueTask<PlayniteBridgeResponse> ProbeAsync(
        string bearerToken,
        CancellationToken cancellationToken)
    {
        PlayniteBridgeClient.ValidateToken(bearerToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, CompatibilityUri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await _http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            var body = await ReadBoundedAsync(response.Content, timeout.Token)
                .ConfigureAwait(false);
            return new((int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new PlayniteBridgeTransportException(isMalformed: false, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new PlayniteBridgeTransportException(isMalformed: false, exception);
        }
        catch (IOException exception)
        {
            throw new PlayniteBridgeTransportException(isMalformed: false, exception);
        }
    }

    public void Dispose() => _http.Dispose();

    private static async Task<string> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
            throw new PlayniteBridgeTransportException(isMalformed: true);
        await using var stream = await content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > MaximumResponseBytes)
                throw new PlayniteBridgeTransportException(isMalformed: true);
            buffer.Write(chunk, 0, read);
        }
        try { return StrictUtf8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length)); }
        catch (DecoderFallbackException exception)
        {
            throw new PlayniteBridgeTransportException(isMalformed: true, exception);
        }
    }
}

internal sealed class WindowsCredentialPlayniteTokenStore : IPlayniteCredentialStore
{
    private const int GenericCredential = 1;
    private const int LocalMachinePersistence = 2;
    private const int NotFound = 1168;
    internal const string Target =
        "WidgetRail/PlayniteLibrary/PlayniteBridge/v1/widgetrail.samples.playnite-library";

    public ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.CredRead(Target, GenericCredential, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return ValueTask.FromResult<string?>(null);
            throw new PlayniteCredentialException(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlobSize is < 1 or >
                    PlayniteBridgeClient.MaximumTokenCharacters ||
                credential.CredentialBlob == IntPtr.Zero)
                throw new PlayniteCredentialException();
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                var token = new UTF8Encoding(false, true).GetString(bytes);
                PlayniteBridgeClient.ValidateToken(token);
                return ValueTask.FromResult<string?>(token);
            }
            catch (DecoderFallbackException exception)
            {
                throw new PlayniteCredentialException(exception);
            }
            catch (ArgumentException exception)
            {
                throw new PlayniteCredentialException(exception);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { NativeMethods.CredFree(pointer); }
    }

    public ValueTask SaveAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlayniteBridgeClient.ValidateToken(token);
        var bytes = Encoding.UTF8.GetBytes(token);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = Target,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = LocalMachinePersistence,
                UserName = "WidgetRail Playnite Library",
            };
            if (!NativeMethods.CredWrite(ref credential, 0))
                throw new PlayniteCredentialException(Marshal.GetLastWin32Error());
            return ValueTask.CompletedTask;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            for (var index = 0; index < bytes.Length; index++) Marshal.WriteByte(blob, index, 0);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.CredDelete(Target, GenericCredential, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NotFound) throw new PlayniteCredentialException(error);
        }
        return ValueTask.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredRead(
            string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredWrite(ref NativeCredential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        internal static extern void CredFree(IntPtr credential);
    }
}

internal sealed class PlayniteBridgeTransportException : Exception
{
    internal PlayniteBridgeTransportException(bool isMalformed, Exception? inner = null)
        : base("The bounded Playnite Bridge transport failed.", inner) =>
        IsMalformed = isMalformed;

    internal bool IsMalformed { get; }
}

internal sealed class PlayniteCredentialException : Exception
{
    internal PlayniteCredentialException() : base("The Playnite credential store failed.") { }
    internal PlayniteCredentialException(int error)
        : base($"The Playnite credential store failed with Windows error {error}.") { }
    internal PlayniteCredentialException(Exception inner)
        : base("The Playnite credential store failed.", inner) { }
}
