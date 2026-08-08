using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.PlatformDiagnostics;

public sealed class PlatformDiagnosticsPipeServer : IAsyncDisposable
{
    private const int MaximumFrameBytes = 64 * 1024;
    internal static readonly TimeSpan MaximumOperationTimeout = TimeSpan.FromSeconds(10);
    private readonly string _pipeName;
    private readonly Func<CancellationToken, ValueTask<PlatformDiagnosticsSnapshot>> _snapshotProvider;
    private readonly TimeSpan _requestTimeout;
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _lifetime = new();
    private int _expectedProcessId;
    private int _disposed;

    public PlatformDiagnosticsPipeServer(
        string pipeName,
        Func<CancellationToken, ValueTask<PlatformDiagnosticsSnapshot>> snapshotProvider,
        TimeSpan? requestTimeout = null)
    {
        if (!IsToken(pipeName, 200))
            throw new ArgumentException("Diagnostics pipe name is invalid.", nameof(pipeName));
        _pipeName = pipeName;
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _requestTimeout = ValidateTimeout(
            requestTimeout ?? TimeSpan.FromSeconds(2), nameof(requestTimeout));
        ChannelNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        // Construction happens before the worker launch. Keeping this first
        // instance alive for the whole companion session prevents another
        // process owned by the same user from claiming the otherwise-secret
        // random name between launch and RunAsync.
        _pipe = new NamedPipeServerStream(
            _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly |
            (OperatingSystem.IsWindows() ? PipeOptions.FirstPipeInstance : PipeOptions.None),
            4096, 4096);
    }

    public string ChannelNonce { get; }

    public void BindExpectedClientProcess(int processId)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        if (Interlocked.CompareExchange(ref _expectedProcessId, processId, 0) != 0)
            throw new InvalidOperationException("Diagnostics client process was already bound.");
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        while (!linked.IsCancellationRequested)
        {
            try
            {
                await _pipe.WaitForConnectionAsync(linked.Token).ConfigureAwait(false);
                VerifyExpectedClient(_pipe);
                using var request = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                request.CancelAfter(_requestTimeout);
                await ServeConnectionAsync(_pipe, request.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // A peer may not hold the only server instance by stalling its
                // hello, request, provider, or response write.
            }
            catch (Exception exception) when (exception is IOException or
                                                   EndOfStreamException or
                                                   JsonException or
                                                   PlatformDiagnosticsException or
                                                   ObjectDisposedException)
            {
                // A malformed or disconnected peer loses this one connection.
                // The private endpoint remains available to the bound worker.
            }
            finally
            {
                if (!linked.IsCancellationRequested) ResetConnection();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _lifetime.Cancel();
            _pipe.Dispose();
        }
        _lifetime.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task ServeConnectionAsync(Stream pipe, CancellationToken cancellationToken)
    {
        var hello = await ReadAsync<DiagnosticsHello>(pipe, cancellationToken).ConfigureAwait(false);
        var authenticated = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(ChannelNonce), ParseNonce(hello.Nonce));
        await WriteAsync(pipe, new DiagnosticsAcknowledgement(authenticated), cancellationToken)
            .ConfigureAwait(false);
        if (!authenticated)
        {
            await ReadReceiptAsync(pipe, "authentication", cancellationToken).ConfigureAwait(false);
            return;
        }

        var request = await ReadAsync<DiagnosticsRequest>(pipe, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(request.Operation, "snapshot", StringComparison.Ordinal))
            throw new PlatformDiagnosticsException("unsupported_operation");
        var snapshot = await _snapshotProvider(cancellationToken).AsTask()
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        ValidateSnapshot(snapshot);
        await WriteAsync(pipe, snapshot, cancellationToken).ConfigureAwait(false);
        // FlushAsync only transfers the frame to the Windows pipe buffer.
        // Disconnecting immediately can discard it before the client reads it.
        // An async, bounded receipt proves delivery without WaitForPipeDrain(),
        // which is synchronous and can be held forever by a stalled peer.
        await ReadReceiptAsync(pipe, "snapshot", cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ReadReceiptAsync(
        Stream pipe, string expectedOperation, CancellationToken cancellationToken)
    {
        var receipt = await ReadAsync<DiagnosticsReceipt>(pipe, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(receipt.Operation, expectedOperation, StringComparison.Ordinal))
            throw new PlatformDiagnosticsException("invalid_receipt");
    }

    private void ResetConnection()
    {
        try
        {
            // NamedPipeServerStream must be explicitly disconnected before it
            // can accept again. IsConnected is only a momentary observation:
            // a peer can close between that check and this reset, leaving the
            // reusable first instance in a state where the next accept throws
            // forever. Disconnect unconditionally and treat "already reset"
            // as the benign race.
            _pipe.Disconnect();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
                                               ObjectDisposedException)
        {
            // A disconnect racing a peer close already leaves the reusable
            // instance ready for the next bounded accept.
        }
    }

    private void VerifyExpectedClient(NamedPipeServerStream pipe)
    {
        var expected = Volatile.Read(ref _expectedProcessId);
        if (expected <= 0) throw new PlatformDiagnosticsException("client_not_bound");
        if (!OperatingSystem.IsWindows()) return;
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var observed) ||
            observed != (uint)expected)
            throw new PlatformDiagnosticsException("client_identity_mismatch");
    }

    internal static void ValidateSnapshot(PlatformDiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != PlatformDiagnosticsSnapshot.CurrentSchemaVersion ||
            snapshot.Revision < 0 || snapshot.Workers is null ||
            snapshot.Workers.Count > PlatformDiagnosticsSnapshot.MaximumWorkers)
            throw new PlatformDiagnosticsException("invalid_snapshot");
        ValidateArea(snapshot.Bridge);
        ValidateArea(snapshot.Catalog);
        ValidateArea(snapshot.Appearance);
        ValidateArea(snapshot.Providers);
        ValidateArea(snapshot.Consent);
        ValidateArea(snapshot.Overlay);
        ValidateArea(snapshot.Guide);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var worker in snapshot.Workers)
        {
            if (worker is null || !IsToken(worker.WidgetId, 128) || !ids.Add(worker.WidgetId) ||
                string.IsNullOrWhiteSpace(worker.WidgetName) || worker.WidgetName.Length > 160 ||
                worker.WidgetName.IndexOfAny(['\r', '\n']) >= 0 || worker.Starts < 0 ||
                worker.LastFailureCode is { } failure && !IsToken(failure, 64))
                throw new PlatformDiagnosticsException("invalid_snapshot");
        }
    }

    private static void ValidateArea(PlatformDiagnosticArea area)
    {
        if (area is null || !Enum.IsDefined(area.State))
            throw new PlatformDiagnosticsException("invalid_snapshot");
        if (!IsToken(area.Id, 64) ||
            string.IsNullOrWhiteSpace(area.Label) || area.Label.Length > 80 ||
            area.Label.IndexOfAny(['\r', '\n']) >= 0 ||
            string.IsNullOrWhiteSpace(area.Summary) || area.Summary.Length > 256 ||
            area.Summary.IndexOfAny(['\r', '\n']) >= 0)
            throw new PlatformDiagnosticsException("invalid_snapshot");
    }

    private static byte[] ParseNonce(string nonce)
    {
        if (nonce is null || nonce.Length != 64)
            throw new PlatformDiagnosticsException("authentication_failed");
        try { return Convert.FromHexString(nonce); }
        catch (FormatException exception)
        {
            throw new PlatformDiagnosticsException("authentication_failed", exception);
        }
    }

    private static bool IsToken(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    internal static TimeSpan ValidateTimeout(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero || value > MaximumOperationTimeout ||
            value == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(parameterName,
                $"Timeout must be finite, positive, and no greater than {MaximumOperationTimeout}.");
        return value;
    }

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 16,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
        return options;
    }

    internal static async ValueTask WriteAsync<T>(
        Stream stream, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (payload.Length is <= 0 or > MaximumFrameBytes)
            throw new PlatformDiagnosticsException("frame_too_large");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumFrameBytes)
            throw new PlatformDiagnosticsException("frame_too_large");
        var payload = GC.AllocateUninitializedArray<byte>(length);
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        RejectDuplicateProperties(payload);
        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
            ?? throw new PlatformDiagnosticsException("malformed_frame");
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> payload)
    {
        var reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = 16 });
        var scopes = new Stack<HashSet<string>?>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                scopes.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.StartArray)
                scopes.Push(null);
            else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
                scopes.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName &&
                     scopes.TryPeek(out var names) && names is not null &&
                     !names.Add(reader.GetString()!))
                throw new PlatformDiagnosticsException("malformed_frame");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);

    private sealed record DiagnosticsHello(string Nonce);
    private sealed record DiagnosticsRequest(string Operation);
    private sealed record DiagnosticsAcknowledgement(bool Accepted);
    private sealed record DiagnosticsReceipt(string Operation);
}

public sealed class PlatformDiagnosticsPipeClient(
    string pipeName,
    string nonce,
    int expectedServerProcessId,
    TimeSpan? timeout = null) : IPlatformDiagnosticsService
{
    private readonly string _pipeName = ValidatePipeName(pipeName);
    private readonly string _nonce = ValidateClientNonce(nonce);
    private readonly int _expectedServerProcessId = expectedServerProcessId > 0
        ? expectedServerProcessId
        : throw new ArgumentOutOfRangeException(nameof(expectedServerProcessId));
    private readonly TimeSpan _timeout = PlatformDiagnosticsPipeServer.ValidateTimeout(
        timeout ?? TimeSpan.FromSeconds(2), nameof(timeout));

    public async ValueTask<PlatformDiagnosticsSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(bounded.Token).ConfigureAwait(false);
            VerifyExpectedServer(pipe);
            await PlatformDiagnosticsPipeServer.WriteAsync(
                pipe, new DiagnosticsHello(_nonce), bounded.Token).ConfigureAwait(false);
            var accepted = await PlatformDiagnosticsPipeServer
                .ReadAsync<DiagnosticsAcknowledgement>(pipe, bounded.Token)
                .ConfigureAwait(false);
            if (!accepted.Accepted)
            {
                await PlatformDiagnosticsPipeServer.WriteAsync(
                    pipe, new DiagnosticsReceipt("authentication"), bounded.Token)
                    .ConfigureAwait(false);
                throw new PlatformDiagnosticsException("authentication_failed");
            }
            await PlatformDiagnosticsPipeServer.WriteAsync(
                pipe, new DiagnosticsRequest("snapshot"), bounded.Token)
                .ConfigureAwait(false);
            var snapshot = await PlatformDiagnosticsPipeServer
                .ReadAsync<PlatformDiagnosticsSnapshot>(pipe, bounded.Token)
                .ConfigureAwait(false);
            PlatformDiagnosticsPipeServer.ValidateSnapshot(snapshot);
            await PlatformDiagnosticsPipeServer.WriteAsync(
                pipe, new DiagnosticsReceipt("snapshot"), bounded.Token)
                .ConfigureAwait(false);
            return snapshot;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PlatformDiagnosticsException("diagnostics_timeout");
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or JsonException)
        {
            throw new PlatformDiagnosticsException("diagnostics_unavailable", exception);
        }
    }

    private sealed record DiagnosticsHello(string Nonce);
    private sealed record DiagnosticsRequest(string Operation);
    private sealed record DiagnosticsAcknowledgement(bool Accepted);
    private sealed record DiagnosticsReceipt(string Operation);

    private void VerifyExpectedServer(NamedPipeClientStream pipe)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var observed) ||
            observed != (uint)_expectedServerProcessId)
            throw new PlatformDiagnosticsException("server_identity_mismatch");
    }

    private static string ValidatePipeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 ||
            !value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            throw new ArgumentException("Diagnostics pipe name is invalid.", nameof(value));
        return value;
    }

    private static string ValidateClientNonce(string value)
    {
        if (value is null || value.Length != 64 || !value.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Diagnostics nonce is invalid.", nameof(value));
        return value;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(
        IntPtr pipe, out uint serverProcessId);
}

public sealed class PlatformDiagnosticsException : Exception
{
    public PlatformDiagnosticsException(string code, Exception? innerException = null)
        : base("Platform diagnostics request failed.", innerException) => Code = code;

    public string Code { get; }
}
