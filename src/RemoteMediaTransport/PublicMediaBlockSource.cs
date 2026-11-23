namespace WidgetRail.RemoteMediaTransport;

public sealed record PublicMediaStreamLimits
{
    public int BlockBytes { get; init; } = 64 * 1024;
    public int MaximumCachedBytes { get; init; } = 4 * 1024 * 1024;
    public int MaximumFallbackBytes { get; init; } = 8 * 1024 * 1024;
    public long MaximumLength { get; init; } = 512L * 1024 * 1024;
    public long MaximumTransferredBytes { get; init; } = 1024L * 1024 * 1024;
    public int MaximumQueuedReads { get; init; } = 16;
    internal void Validate()
    {
        if (BlockBytes is < 1 or > 1024 * 1024 || MaximumCachedBytes < BlockBytes || MaximumCachedBytes > 16 * 1024 * 1024 ||
            MaximumFallbackBytes is < 1 or > 16 * 1024 * 1024 || MaximumLength is < 1 or > 512L * 1024 * 1024 ||
            MaximumTransferredBytes < BlockBytes || MaximumTransferredBytes > 2L * 1024 * 1024 * 1024 || MaximumQueuedReads is < 0 or > 64)
            throw new ArgumentException("Invalid media stream limits.");
    }
}

/// <summary>
/// Version-pinned random reads for a native stream adapter. Bounded LRU blocks,
/// not a full video download. This object never opens a native decoder itself.
/// </summary>
public sealed class PublicMediaBlockSource : IAsyncDisposable
{
    private readonly PublicMediaClient client;
    private readonly Uri source;
    private readonly PublicMediaPolicy policy;
    private readonly PublicMediaStreamLimits limits;
    private readonly string? version;
    private byte[]? whole;
    private readonly Dictionary<long, LinkedListNode<Block>> blocks = [];
    private readonly LinkedList<Block> recent = [];
    private readonly SemaphoreSlim readGate = new(1, 1);
    private readonly CancellationTokenSource shutdown;
    private readonly CancellationToken shutdownToken;
    private readonly object sync = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool retiring, invalidated;
    private int reads, cachedBytes;
    private long transferred;
    private sealed record Block(long Index, byte[] Bytes);
    public long Length { get; }
    public string? MediaType { get; }
    internal int CachedBytes => Volatile.Read(ref cachedBytes) + (whole?.Length ?? 0);
    internal long TransferredBytes => Interlocked.Read(ref transferred);

    // Never wait for in-flight HTTP on the presentation thread. Only immutable
    // checked bytes, the resource validator and the transfer budget survive.
    internal BufferedSnapshot? TryTakeBufferedSnapshot()
    {
        lock (sync)
        {
            if (retiring || shutdownToken.IsCancellationRequested || !readGate.Wait(0)) return null;
            try
            {
                if (invalidated) return null;
                var saved = new BufferedSnapshot(this);
                // The retiring cursors must not start another transfer after
                // its byte budget was handed over to a new reader lifetime.
                invalidated = true;
                return saved;
            }
            finally { readGate.Release(); }
        }
    }

    internal sealed class BufferedSnapshot
    {
        private readonly PublicMediaClient client;
        private readonly PublicMediaPolicy policy;
        private readonly PublicMediaStreamLimits limits;
        private readonly PublicMediaRange header;
        private readonly long transferred;
        private readonly Block[] cached;
        private int consumed;
        internal BufferedSnapshot(PublicMediaBlockSource owner)
        {
            client = owner.client; policy = owner.policy; limits = owner.limits; transferred = owner.transferred;
            header = new(owner.whole ?? [], owner.source, owner.Length, owner.version, owner.MediaType, owner.whole is not null);
            cached = owner.recent.ToArray();
        }
        internal PublicMediaBlockSource Open(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref consumed,1)!=0) throw new InvalidOperationException("Saved media bytes were already reopened.");
            var result = new PublicMediaBlockSource(client, policy, limits, header, transferred, token);
            foreach (var block in cached) result.AddBlock(block.Index, block.Bytes);
            return result;
        }
    }

    private PublicMediaBlockSource(PublicMediaClient client, PublicMediaPolicy policy, PublicMediaStreamLimits limits,
        PublicMediaRange initial, long transferred, CancellationToken token)
    {
        this.client = client; source = initial.EffectiveUri; this.policy = policy; this.limits = limits;
        Length = initial.Length; MediaType = initial.MediaType; version = initial.Version; this.transferred = transferred;
        shutdown = CancellationTokenSource.CreateLinkedTokenSource(token); shutdownToken = shutdown.Token;
        if (initial.Complete) whole = initial.Bytes;
        else if(initial.Bytes.Length != 0) AddBlock(0, initial.Bytes);
    }

    public static async Task<PublicMediaBlockSource> OpenAsync(PublicMediaClient client, Uri source, PublicMediaPolicy policy,
        PublicMediaStreamLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(policy);
        limits ??= new(); limits.Validate();
        var first = await client.ReadRangeAsync(source, policy, 0, limits.BlockBytes, limits.MaximumLength,
            (int)Math.Min(limits.MaximumFallbackBytes, limits.MaximumTransferredBytes), null, null, cancellationToken).ConfigureAwait(false);
        long transferred = first.Bytes.Length;
        if (!first.Complete && first.Version is null)
        {
            // Without a strong validator, joining separate reads can mix two
            // different files. Small resources may instead become one snapshot.
            if (first.Length > limits.MaximumFallbackBytes || first.Length > limits.MaximumTransferredBytes - transferred)
                throw new PublicMediaException(PublicMediaFailure.ResponseRejected);
            var snapshot = await client.DownloadAsync(first.EffectiveUri, policy,
                (int)Math.Min(limits.MaximumTransferredBytes - transferred, Math.Min(limits.MaximumLength, limits.MaximumFallbackBytes)), cancellationToken).ConfigureAwait(false);
            var bytes = snapshot.Bytes.ToArray(); transferred += bytes.Length;
            first = first with { Bytes = bytes, Length = bytes.Length, MediaType = snapshot.MediaType, Complete = true };
        }
        if (transferred > limits.MaximumTransferredBytes) throw new PublicMediaException(PublicMediaFailure.TooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return new(client, policy, limits, first, transferred, cancellationToken);
    }

    public async ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (offset < 0 || destination.Length > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(offset));
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(retiring, this);
            if (reads >= limits.MaximumQueuedReads + 1) throw new PublicMediaException(PublicMediaFailure.Busy);
            ++reads;
        }
        var entered = false;
        try
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownToken);
            var token = lifetime.Token;
            await readGate.WaitAsync(token).ConfigureAwait(false); entered = true;
            if (invalidated) throw new PublicMediaException(PublicMediaFailure.ResponseRejected);
            token.ThrowIfCancellationRequested();
            if (offset >= Length || destination.Length == 0) return 0;
            var count = (int)Math.Min(destination.Length, Length - offset);
            if (whole is not null) { whole.AsMemory((int)offset, count).CopyTo(destination); return count; }
            var copied = 0;
            while (copied < count)
            {
                var position = offset + copied;
                var index = position / limits.BlockBytes;
                if (!blocks.TryGetValue(index, out var block))
                {
                    var start = index * limits.BlockBytes;
                    var expected = (int)Math.Min(limits.BlockBytes, Length - start);
                    if (expected > limits.MaximumTransferredBytes - transferred) throw new PublicMediaException(PublicMediaFailure.TooLarge);
                    Interlocked.Add(ref transferred, expected); // Failed/retried transfers also consume the finite work budget.
                    PublicMediaRange range;
                    try
                    {
                        range = await client.ReadRangeAsync(source, policy, start, limits.BlockBytes, limits.MaximumLength,
                            limits.MaximumFallbackBytes, Length, version, token).ConfigureAwait(false);
                    }
                    catch (PublicMediaException error) when (error.Failure is PublicMediaFailure.ResponseRejected or PublicMediaFailure.TooLarge)
                    { invalidated = true; throw; }
                    token.ThrowIfCancellationRequested();
                    block = AddBlock(index, range.Bytes);
                }
                recent.Remove(block); recent.AddLast(block);
                var within = (int)(position - index * limits.BlockBytes);
                var take = Math.Min(count - copied, block.Value.Bytes.Length - within);
                block.Value.Bytes.AsMemory(within, take).CopyTo(destination[copied..]);
                copied += take;
            }
            return copied;
        }
        finally
        {
            if (entered) readGate.Release();
            lock (sync) { if (--reads == 0 && retiring) drained.TrySetResult(); }
        }
    }

    private LinkedListNode<Block> AddBlock(long index, byte[] bytes)
    {
        while (cachedBytes + bytes.Length > limits.MaximumCachedBytes && recent.First is { } old)
        { recent.RemoveFirst(); blocks.Remove(old.Value.Index); cachedBytes -= old.Value.Bytes.Length; }
        var node = recent.AddLast(new Block(index, bytes)); blocks.Add(index, node); cachedBytes += bytes.Length;
        return node;
    }

    public async ValueTask DisposeAsync()
    {
        bool first;
        lock (sync) { first = !retiring; retiring = true; if (reads == 0) drained.TrySetResult(); }
        if (!first) { await disposed.Task.ConfigureAwait(false); return; }
        try
        {
            shutdown.Cancel(); await drained.Task.ConfigureAwait(false);
            blocks.Clear(); recent.Clear(); whole = null; cachedBytes = 0; readGate.Dispose(); shutdown.Dispose();
        }
        finally { disposed.TrySetResult(); }
    }
}
