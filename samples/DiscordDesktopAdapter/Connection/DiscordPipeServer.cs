using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.DiscordDesktop.Connection;

/// <summary>One owner, one peer, one authentication attempt. Dispose closes all pending I/O.</summary>
[SupportedOSPlatform("windows")]
public sealed class DiscordPipeServer : IAsyncDisposable
{
    public const string PipePrefix = "wrail-discord-desktop-";
    private readonly object sync = new();
    private readonly byte[] secret;
    private readonly NamedPipeServerStream pipe;
    private readonly DiscordProcessIdentity expectedPeer;
    private readonly CancellationTokenSource lifetime = new();
    private SafeProcessHandle? peerProcess;
    private bool disposed;
    private int accepting;
    public string PipeName { get; }
    public static string CreatePipeName() => PipePrefix + Guid.NewGuid().ToString("N");

    public DiscordPipeServer(string pipeName, DiscordProcessIdentity expectedPeer, ReadOnlySpan<byte> secret)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Discord desktop pipes require Windows.");
        ArgumentNullException.ThrowIfNull(expectedPeer);
        ArgumentNullException.ThrowIfNull(pipeName);
        if (!pipeName.StartsWith(PipePrefix, StringComparison.Ordinal) || !DiscordAuthentication.IsLowerHex(pipeName[PipePrefix.Length..], 32))
            throw new ArgumentException("Use a fresh bounded Discord pipe name.", nameof(pipeName));
        if (secret.Length != 32) throw new ArgumentException("Pairing secret must contain exactly 32 bytes.", nameof(secret));
        this.secret = secret.ToArray(); this.expectedPeer = expectedPeer; PipeName = pipeName;
        try { pipe = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance, 4096, 4096); }
        catch { CryptographicOperations.ZeroMemory(this.secret); lifetime.Dispose(); throw; }
    }

    public async Task<DiscordAuthenticatedConnection> AcceptAsync(CancellationToken token = default)
    {
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref accepting, 1, 0) != 0) throw new InvalidOperationException("This Discord endpoint already accepted an attempt.");
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        try
        {
            await pipe.WaitForConnectionAsync(waiting.Token).ConfigureAwait(false);
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(waiting.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            var handshakeToken = handshake.Token;
            var process = WindowsDiscordIdentity.Verify(pipe, expectedPeer);
            lock (sync)
            {
                if (disposed) { process.Dispose(); throw new ObjectDisposedException(nameof(DiscordPipeServer)); }
                peerProcess = process;
            }
            var hello = DiscordAuthentication.ClientHello(await DiscordJsonFrame.ReadAsync(pipe, handshakeToken).ConfigureAwait(false));
            var connectionId = DiscordAuthentication.RandomHex(16);
            var serverNonce = DiscordAuthentication.RandomHex(32);
            var serverProof = ComputeProof("server", hello.Nonce, serverNonce, connectionId, hello.Instance);
            var response = DiscordJsonFrame.Object(writer =>
            {
                writer.WriteString("type", "serverHello"); writer.WriteNumber("version", 1);
                writer.WriteString("connectionId", connectionId); writer.WriteString("serverNonce", serverNonce);
                writer.WriteString("serverProof", Convert.ToHexString(serverProof).ToLowerInvariant());
            });
            CryptographicOperations.ZeroMemory(serverProof);
            await DiscordJsonFrame.WriteAsync(pipe, response, handshakeToken).ConfigureAwait(false);
            var supplied = DiscordAuthentication.ClientProof(await DiscordJsonFrame.ReadAsync(pipe, handshakeToken).ConfigureAwait(false));
            var expected = ComputeProof("client", hello.Nonce, serverNonce, connectionId, hello.Instance);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(supplied, expected)) throw new UnauthorizedAccessException("Discord authentication failed.");
            }
            finally { CryptographicOperations.ZeroMemory(supplied); CryptographicOperations.ZeroMemory(expected); }
            WindowsDiscordIdentity.EnsureAlive(process);
            await DiscordJsonFrame.WriteAsync(pipe, DiscordJsonFrame.Object(writer =>
            { writer.WriteString("type", "ready"); writer.WriteString("connectionId", connectionId); }), handshakeToken).ConfigureAwait(false);
            ThrowIfDisposed();
            return new(this, connectionId, hello.Instance);
        }
        catch { await DisposeAsync().ConfigureAwait(false); throw; }
    }
    private byte[] ComputeProof(string role, string clientNonce, string serverNonce, string connectionId, string instance)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return DiscordAuthentication.Proof(secret, role, clientNonce, serverNonce, connectionId, instance);
        }
    }
    internal void ThrowIfDisposed() { lock (sync) ObjectDisposedException.ThrowIf(disposed, this); }
    internal Stream Stream => pipe;
    internal CancellationToken Lifetime => lifetime.Token;
    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposed) return ValueTask.CompletedTask;
            disposed = true;
            CryptographicOperations.ZeroMemory(secret);
            try { lifetime.Cancel(); }
            finally { pipe.Dispose(); peerProcess?.Dispose(); lifetime.Dispose(); }
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>Authenticated bounded objects; domain messages still require typed validation by their owner.</summary>
[SupportedOSPlatform("windows")]
public sealed class DiscordAuthenticatedConnection : IAsyncDisposable
{
    private readonly DiscordPipeServer owner;
    private int reading, writing;
    public string ConnectionId { get; }
    public string ClientInstance { get; }
    internal DiscordAuthenticatedConnection(DiscordPipeServer owner, string connectionId, string clientInstance)
    { this.owner = owner; ConnectionId = connectionId; ClientInstance = clientInstance; }
    public async ValueTask<JsonElement> ReadObjectAsync(CancellationToken token = default)
    {
        owner.ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref reading, 1, 0) != 0) throw new InvalidOperationException("One Discord read may be outstanding.");
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, owner.Lifetime);
            return await DiscordJsonFrame.ReadAsync(owner.Stream, linked.Token).ConfigureAwait(false);
        }
        catch { await owner.DisposeAsync().ConfigureAwait(false); throw; }
        finally { Volatile.Write(ref reading, 0); }
    }
    public async ValueTask WriteObjectAsync(JsonElement message, CancellationToken token = default)
    {
        owner.ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref writing, 1, 0) != 0) throw new InvalidOperationException("One Discord write may be outstanding.");
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, owner.Lifetime);
            await DiscordJsonFrame.WriteAsync(owner.Stream, message, linked.Token).ConfigureAwait(false);
        }
        catch { await owner.DisposeAsync().ConfigureAwait(false); throw; }
        finally { Volatile.Write(ref writing, 0); }
    }
    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
