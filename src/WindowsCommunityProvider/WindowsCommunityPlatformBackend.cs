using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsCommunityProvider;

/// <summary>
/// Trusted host provider for constrained loopback JSON and package-scoped
/// write-only secrets. No native handle, credential value, URI authority or
/// socket is exposed through the widget contract.
/// </summary>
public sealed class WindowsCommunityPlatformBackend :
    ILoopbackHttpPlatformBrokerBackend,
    IPrivateSecretPlatformBrokerBackend,
    IPrivateStatePlatformBrokerBackend,
    IAsyncDisposable
{
    internal const int MaximumGlobalRequests = 8;
    private static readonly SemaphoreSlim GlobalRequestGate =
        new(MaximumGlobalRequests, MaximumGlobalRequests);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ConcurrentDictionary<int, HttpClient> _clients = new();
    private readonly IPrivateSecretStore _secrets;
    private readonly IPrivateStateStore _privateState;
    private int _disposed;

    public WindowsCommunityPlatformBackend() : this(DefaultPrivateStateRoot()) { }

    public WindowsCommunityPlatformBackend(string privateStateRoot) : this(
        new WindowsCredentialPrivateSecretStore(),
        new WindowsPrivateStateStore(privateStateRoot)) { }

    internal WindowsCommunityPlatformBackend(
        IPrivateSecretStore secrets,
        IPrivateStateStore? privateState = null)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _privateState = privateState ?? new WindowsPrivateStateStore(DefaultPrivateStateRoot());
    }

    public Task<PrivateStateSnapshotSummary> ReadPrivateStateAsync(
        BrokerWidgetIdentity identity, CancellationToken cancellationToken) =>
        _privateState.ReadAsync(identity, cancellationToken);

    public Task<PrivateStateMutationSummary> WritePrivateStateAsync(
        BrokerWidgetIdentity identity, WritePrivateStateRequest request,
        CancellationToken cancellationToken) =>
        _privateState.WriteAsync(identity, request, cancellationToken);

    public Task<PrivateStateMutationSummary> ClearPrivateStateAsync(
        BrokerWidgetIdentity identity, ClearPrivateStateRequest request,
        CancellationToken cancellationToken) =>
        _privateState.ClearAsync(identity, request, cancellationToken);

    public Task<PrivateSecretMetadataSummary> GetPrivateSecretMetadataAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken) =>
        _secrets.GetMetadataAsync(identity, slot, cancellationToken);

    public Task SavePrivateSecretAsync(
        BrokerWidgetIdentity identity, string slot, string secret,
        CancellationToken cancellationToken) =>
        _secrets.SaveAsync(identity, slot, secret, cancellationToken);

    public Task DeletePrivateSecretAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken) =>
        _secrets.DeleteAsync(identity, slot, cancellationToken);

    public async Task<LoopbackJsonResponse> SendLoopbackJsonAsync(
        BrokerWidgetIdentity identity,
        int port,
        bool isPost,
        LoopbackJsonRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ValidateRequest(identity, port, isPost, request);
        await GlobalRequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(request.TimeoutMilliseconds));
            using var message = new HttpRequestMessage(
                isPost ? HttpMethod.Post : HttpMethod.Get,
                new Uri($"http://127.0.0.1:{port}{request.Path}", UriKind.Absolute))
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };
            if (isPost)
                message.Content = new StringContent(
                    request.JsonBody!, StrictUtf8, "application/json");
            foreach (var header in request.Headers)
            {
                if (!message.Headers.TryAddWithoutValidation(header.Name, header.Value))
                    throw new BrokerException("invalid_payload", "Loopback HTTP header is unsupported.");
            }

            if (request.BearerSecretSlot is { } secretSlot)
            {
                var secret = await _secrets.ReadForHostUseAsync(
                    identity, secretSlot, timeout.Token).ConfigureAwait(false);
                if (secret.Any(character => character is < '!' or > '~'))
                    throw new BrokerException(
                        "invalid_secret", "Stored secret cannot be used as an HTTP Bearer value.");
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            }

            try
            {
                using var response = await GetClient(port).SendAsync(
                    message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
                var result = await ReadResponseAsync(response, timeout.Token).ConfigureAwait(false);
                if (result.StatusCode == (int)HttpStatusCode.Unauthorized &&
                    request is
                    {
                        InvalidateBearerSecretOnUnauthorized: true,
                        BearerSecretSlot: { } rejectedSlot,
                    })
                {
                    // This is part of the authenticated request, while the
                    // broker's dependent private-secret lease is still held.
                    // Failure is surfaced rather than returning a 401 while a
                    // rejected durable credential remains available.
                    await _secrets.DeleteAsync(identity, rejectedSlot, cancellationToken)
                        .ConfigureAwait(false);
                }
                return result;
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new BrokerException(
                    "loopback_timeout", "Loopback service did not respond in time.", exception);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new BrokerException(
                    "loopback_unavailable", "Loopback service is unavailable.", exception);
            }
            catch (HttpRequestException exception)
            {
                throw new BrokerException(
                    "loopback_unavailable", "Loopback service is unavailable.", exception);
            }
        }
        finally
        {
            GlobalRequestGate.Release();
        }
    }

    private HttpClient GetClient(int port) => _clients.GetOrAdd(port, static exactPort =>
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            MaxConnectionsPerServer = MaximumGlobalRequests,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                };
                try
                {
                    await socket.ConnectAsync(
                        new IPEndPoint(IPAddress.Loopback, exactPort), cancellationToken)
                        .ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    });

    private static async Task<LoopbackJsonResponse> ReadResponseAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength > CommunityPlatformLimits.MaximumLoopbackResponseBodyUtf8Bytes)
            throw new BrokerException("response_too_large", "Loopback response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var writer = new ArrayBufferWriter<byte>();
        var buffer = ArrayPool<byte>.Shared.Rent(8 * 1024);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (writer.WrittenCount + read >
                    CommunityPlatformLimits.MaximumLoopbackResponseBodyUtf8Bytes)
                    throw new BrokerException("response_too_large", "Loopback response is too large.");
                writer.Write(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }

        string body;
        try { body = StrictUtf8.GetString(writer.WrittenSpan); }
        catch (DecoderFallbackException exception)
        {
            throw new BrokerException(
                "invalid_response", "Loopback response is not valid UTF-8 JSON.", exception);
        }

        var statusCode = (int)response.StatusCode;
        if (!IsJson(body, allowEmpty: statusCode is 204 or 205))
        {
            if (statusCode is >= 200 and <= 299)
                throw new BrokerException(
                    "invalid_response", "Loopback service returned a non-JSON success response.");
            body = "{\"error\":\"non_json_response\"}";
        }

        return new LoopbackJsonResponse(statusCode, body, ProjectResponseHeaders(response));
    }

    private static IReadOnlyList<LoopbackHttpHeader> ProjectResponseHeaders(
        HttpResponseMessage response)
    {
        var projected = new List<LoopbackHttpHeader>();
        var projectedCharacters = 0;
        Add("Content-Type", response.Content.Headers.ContentType?.ToString());
        Add("ETag", response.Headers.ETag?.ToString());
        Add("Last-Modified", response.Content.Headers.LastModified?.ToString("R"));
        Add("Retry-After", response.Headers.RetryAfter?.ToString());
        foreach (var header in response.Headers)
            if (header.Key.StartsWith("X-RateLimit-", StringComparison.OrdinalIgnoreCase))
                Add(header.Key, string.Join(",", header.Value));
        return projected.AsReadOnly();

        void Add(string name, string? value)
        {
            if (string.IsNullOrEmpty(value) ||
                projected.Count >= CommunityPlatformLimits.MaximumLoopbackHeaderCount ||
                value.Length > CommunityPlatformLimits.MaximumLoopbackHeaderValueCharacters ||
                projectedCharacters + name.Length + value.Length >
                    CommunityPlatformLimits.MaximumLoopbackHeaderCharacters)
                return;
            projected.Add(new LoopbackHttpHeader(name, value));
            projectedCharacters += name.Length + value.Length;
        }
    }

    private static bool IsJson(string body, bool allowEmpty)
    {
        if (body.Length == 0) return allowEmpty;
        try
        {
            using var _ = JsonDocument.Parse(body, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = BrokerJson.MaximumDepth,
            });
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static void ValidateRequest(
        BrokerWidgetIdentity identity, int port, bool isPost, LoopbackJsonRequest request)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
        ArgumentNullException.ThrowIfNull(request);
        if (port is < CommunityPlatformLimitsForProvider.MinimumLoopbackPort or > 65_535 ||
            request.Path is null ||
            request.Path.Length is < 1 or > CommunityPlatformLimits.MaximumLoopbackPathCharacters ||
            request.Path[0] != '/' || request.Path.StartsWith("//", StringComparison.Ordinal) ||
            request.Path.Contains('\\') || request.Path.Contains('#') ||
            request.Path.Any(character => character is '\r' or '\n' || char.IsControl(character)) ||
            !Uri.TryCreate(request.Path, UriKind.Relative, out _) ||
            request.Headers is null ||
            request.Headers.Count > CommunityPlatformLimits.MaximumLoopbackHeaderCount ||
            request.TimeoutMilliseconds is < 1 or
                > CommunityPlatformLimits.MaximumLoopbackTimeoutMilliseconds ||
            request.InvalidateBearerSecretOnUnauthorized && request.BearerSecretSlot is null ||
            isPost != (request.JsonBody is not null))
            throw new BrokerException("invalid_payload", "Loopback HTTP request is invalid.");
        if (request.BearerSecretSlot is { } slot)
            WindowsCredentialPrivateSecretStore.ValidateSlot(slot);
        if (isPost && (Encoding.UTF8.GetByteCount(request.JsonBody!) >
                CommunityPlatformLimits.MaximumLoopbackRequestBodyUtf8Bytes ||
            !IsJson(request.JsonBody!, allowEmpty: false)))
            throw new BrokerException("invalid_payload", "Loopback request body is invalid.");
        var headerCharacters = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            if (header is null || string.IsNullOrEmpty(header.Name) ||
                header.Name.Length > CommunityPlatformLimits.MaximumLoopbackHeaderNameCharacters ||
                header.Value is null ||
                header.Value.Length > CommunityPlatformLimits.MaximumLoopbackHeaderValueCharacters ||
                !header.Name.All(IsHttpTokenCharacter) ||
                header.Value.Any(character => character is '\r' or '\n' || char.IsControl(character)) ||
                !names.Add(header.Name) || IsRestrictedHeader(header.Name))
                throw new BrokerException("invalid_payload", "Loopback HTTP header is invalid.");
            headerCharacters += header.Name.Length + header.Value.Length;
        }
        if (headerCharacters > CommunityPlatformLimits.MaximumLoopbackHeaderCharacters)
            throw new BrokerException("invalid_payload", "Loopback HTTP headers are too large.");
    }

    private static bool IsRestrictedHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Expect", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);

    private static bool IsHttpTokenCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) ||
        character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or
            '^' or '_' or '`' or '|' or '~';

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        foreach (var client in _clients.Values) client.Dispose();
        _clients.Clear();
        return ValueTask.CompletedTask;
    }

    private static string DefaultPrivateStateRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameBarAlternative", "widget-state");

    private static class CommunityPlatformLimitsForProvider
    {
        internal const int MinimumLoopbackPort = 1_024;
    }
}

internal interface IPrivateSecretStore
{
    Task<PrivateSecretMetadataSummary> GetMetadataAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken);
    Task SaveAsync(
        BrokerWidgetIdentity identity, string slot, string secret,
        CancellationToken cancellationToken);
    Task DeleteAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken);
    Task<string> ReadForHostUseAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken);
}

internal sealed class WindowsCredentialPrivateSecretStore : IPrivateSecretStore
{
    private const int GenericCredential = 1;
    private const int PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const string TargetPrefix = "WidgetRail/private-secret/v1/";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public Task<PrivateSecretMetadataSummary> GetMetadataAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = BuildTarget(identity, slot);
        if (!NativeMethods.CredRead(target, GenericCredential, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == ErrorNotFound)
                return Task.FromResult(new PrivateSecretMetadataSummary(false, null));
            throw CredentialFailure("secret_store_unavailable");
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            var lastWritten = DateTime.FromFileTimeUtc(
                ((long)credential.LastWritten.dwHighDateTime << 32) |
                (uint)credential.LastWritten.dwLowDateTime);
            return Task.FromResult(new PrivateSecretMetadataSummary(
                true, new DateTimeOffset(lastWritten).ToUnixTimeMilliseconds()));
        }
        finally { NativeMethods.CredFree(pointer); }
    }

    public Task SaveAsync(
        BrokerWidgetIdentity identity, string slot, string secret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIdentity(identity);
        ValidateSlot(slot);
        ArgumentNullException.ThrowIfNull(secret);
        byte[] bytes;
        try { bytes = StrictUtf8.GetBytes(secret); }
        catch (EncoderFallbackException exception)
        {
            throw new BrokerException("invalid_payload", "Private secret is invalid.", exception);
        }
        if (bytes.Length is < 1 or > CommunityPlatformLimits.MaximumPrivateSecretUtf8Bytes)
            throw new BrokerException("invalid_payload", "Private secret is invalid.");
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = BuildTarget(identity, slot),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = "WidgetRail",
            };
            cancellationToken.ThrowIfCancellationRequested();
            if (!NativeMethods.CredWrite(ref credential, 0))
                throw CredentialFailure("secret_store_unavailable");
            return Task.CompletedTask;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
        }
    }

    public Task DeleteAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = BuildTarget(identity, slot);
        if (!NativeMethods.CredDelete(target, GenericCredential, 0) &&
            Marshal.GetLastWin32Error() != ErrorNotFound)
            throw CredentialFailure("secret_store_unavailable");
        return Task.CompletedTask;
    }

    public Task<string> ReadForHostUseAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = BuildTarget(identity, slot);
        if (!NativeMethods.CredRead(target, GenericCredential, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == ErrorNotFound)
                throw new BrokerException("secret_not_found", "Private secret slot is empty.");
            throw CredentialFailure("secret_store_unavailable");
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlobSize is < 1 or
                > CommunityPlatformLimits.MaximumPrivateSecretUtf8Bytes ||
                credential.CredentialBlob == IntPtr.Zero)
                throw new BrokerException("invalid_secret", "Stored private secret is invalid.");
            var bytes = new byte[credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Task.FromResult(StrictUtf8.GetString(bytes));
            }
            catch (DecoderFallbackException exception)
            {
                throw new BrokerException("invalid_secret", "Stored private secret is invalid.", exception);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { NativeMethods.CredFree(pointer); }
    }

    internal static string BuildTarget(BrokerWidgetIdentity identity, string slot)
    {
        ValidateIdentity(identity);
        ValidateSlot(slot);
        // Package authority is intentionally stable across version-bearing
        // instance IDs. Unsigned installations receive a content-derived
        // publisher authority, so replacement bytes cannot inherit a secret.
        var material = StrictUtf8.GetBytes(
            $"gba-private-secret-v1\0{identity.PublisherId}\0{identity.PackageId}\0{slot}");
        try { return TargetPrefix + Convert.ToHexString(SHA256.HashData(material)); }
        finally { CryptographicOperations.ZeroMemory(material); }
    }

    internal static void ValidateSlot(string? slot)
    {
        if (string.IsNullOrEmpty(slot) ||
            slot.Length > CommunityPlatformLimits.MaximumPrivateSecretSlotCharacters ||
            !slot.All(character => char.IsAsciiLetterOrDigit(character) ||
                character is '.' or '_' or '-'))
            throw new BrokerException("invalid_payload", "Private secret slot is invalid.");
    }

    private static void ValidateIdentity(BrokerWidgetIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
    }

    private static BrokerException CredentialFailure(string code) =>
        new(code, "Windows private secret storage is unavailable.");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", EntryPoint = "CredReadW",
            SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredRead(
            string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredWrite(ref NativeCredential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW",
            SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        internal static extern void CredFree(IntPtr credential);
    }
}
