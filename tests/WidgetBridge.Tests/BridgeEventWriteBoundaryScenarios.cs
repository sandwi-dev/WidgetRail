using System.Buffers.Binary;
using WidgetRail.WidgetBridge;

internal static class BridgeEventWriteBoundaryScenarios
{
    private const int MaximumMessageBytes = 64 * 1024;

    internal static async Task CancellationPreservesFrameBoundary()
    {
        await QueuedCancellationWritesNothingAsync();
        await StartedFrameDeadlineEndsSessionAsync();
        await OrdinaryReplyCancellationFinishesExactFrameAsync();
    }

    private static async Task QueuedCancellationWritesNothingAsync()
    {
        await using var adapter = new ManualEventWriteAdapter(
            initiallyAdmitWriter: false,
            blockFrameBody: false);
        var boundary = new BridgeFrameWriteBoundary(adapter);
        using var cancellation = new CancellationTokenSource();

        var withdrawn = boundary.WriteAsync(
            Envelope(BridgeMessageTypes.Invalidation, "withdrawn"),
            cancellation.Token);
        await adapter.FirstWriterWaiting.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        _ = await BoundaryAssert.ThrowsAsync<OperationCanceledException>(
            () => withdrawn.WaitAsync(TimeSpan.FromSeconds(2)));

        BoundaryAssert.Equal(0, adapter.Stream.Bytes.Length);
        BoundaryAssert.Equal(0, adapter.ReleaseCount);
        BoundaryAssert.Equal(0, adapter.AbortCount);

        adapter.AdmitWriter();
        await boundary.WriteAsync(
            Envelope(BridgeMessageTypes.Failure, "intact"),
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        var bytes = adapter.Stream.Bytes;
        await using var stream = new MemoryStream(bytes, writable: false);
        var channel = new BridgeFrameChannel(stream, MaximumMessageBytes);
        var frame = await channel.ReadAsync(CancellationToken.None);
        BoundaryAssert.Equal(BridgeMessageTypes.Failure, frame.Type);
        BoundaryAssert.Equal("intact", frame.Payload.GetProperty("value").GetString());
        BoundaryAssert.Equal(stream.Length, stream.Position);
        BoundaryAssert.Equal(1, adapter.ReleaseCount);
        BoundaryAssert.Equal(1, adapter.ChannelAccessCount);
        BoundaryAssert.Equal(0, adapter.AbortCount);
    }

    private static async Task StartedFrameDeadlineEndsSessionAsync()
    {
        await using var adapter = new ManualEventWriteAdapter(
            initiallyAdmitWriter: true,
            blockFrameBody: true);
        var boundary = new BridgeFrameWriteBoundary(adapter);
        using var publicationCancellation = new CancellationTokenSource();

        var partial = boundary.WriteAsync(
            Envelope(BridgeMessageTypes.Invalidation, "partial"),
            publicationCancellation.Token);
        await adapter.Stream.BodyWriteEntered.WaitAsync(TimeSpan.FromSeconds(2));
        var bytesAfterHeader = adapter.Stream.Bytes;
        BoundaryAssert.Equal(sizeof(int), bytesAfterHeader.Length);
        var announcedLength = BinaryPrimitives.ReadInt32LittleEndian(bytesAfterHeader);
        BoundaryAssert.True(
            announcedLength is > 0 and <= MaximumMessageBytes,
            "The event header did not announce a bounded frame.");

        publicationCancellation.Cancel();
        BoundaryAssert.True(
            !partial.IsCompleted,
            "Publication cancellation interrupted a frame after writing began.");

        var excluded = boundary.WriteAsync(
            Envelope(BridgeMessageTypes.Failure, "must-not-follow"),
            CancellationToken.None);
        await adapter.SecondWriterWaiting.WaitAsync(TimeSpan.FromSeconds(2));

        adapter.TriggerDeadline();
        _ = await BoundaryAssert.ThrowsAsync<OperationCanceledException>(
            () => partial.WaitAsync(TimeSpan.FromSeconds(2)));
        _ = await BoundaryAssert.ThrowsAsync<OperationCanceledException>(
            () => excluded.WaitAsync(TimeSpan.FromSeconds(2)));

        BoundaryAssert.Equal(1, adapter.AbortCount);
        BoundaryAssert.Equal(1, adapter.ChannelAccessCount);
        BoundaryAssert.Equal(sizeof(int), adapter.Stream.Bytes.Length);
        BoundaryAssert.True(
            adapter.SessionCancellation.IsCancellationRequested,
            "The partial-frame timeout did not terminate the session.");
        BoundaryAssert.Equal(
            BridgeFrameWriteBoundary.WriteDeadline,
            adapter.ObservedDeadline);
    }

    private static async Task OrdinaryReplyCancellationFinishesExactFrameAsync()
    {
        await using var adapter = new ManualEventWriteAdapter(
            initiallyAdmitWriter: true,
            blockFrameBody: true,
            releaseBlockedBody: true);
        var boundary = new BridgeFrameWriteBoundary(adapter);
        using var requestCancellation = new CancellationTokenSource();

        var reply = boundary.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Acknowledged,
            RequestId = 41,
            Payload = BridgeJson.ToElement(new { value = "reply" }),
        }, requestCancellation.Token);
        await adapter.Stream.BodyWriteEntered.WaitAsync(TimeSpan.FromSeconds(2));
        requestCancellation.Cancel();
        BoundaryAssert.True(
            !reply.IsCompleted,
            "Request cancellation interrupted an ordinary reply after its header.");

        var successor = boundary.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Acknowledged,
            RequestId = 42,
            Payload = BridgeJson.ToElement(new { value = "successor" }),
        }, CancellationToken.None);
        await adapter.SecondWriterWaiting.WaitAsync(TimeSpan.FromSeconds(2));
        adapter.Stream.ReleaseBody();
        await Task.WhenAll(reply, successor).WaitAsync(TimeSpan.FromSeconds(2));

        await using var stream = new MemoryStream(adapter.Stream.Bytes, writable: false);
        var channel = new BridgeFrameChannel(stream, MaximumMessageBytes);
        var first = await channel.ReadAsync(CancellationToken.None);
        var second = await channel.ReadAsync(CancellationToken.None);
        BoundaryAssert.Equal(41L, first.RequestId);
        BoundaryAssert.Equal(42L, second.RequestId);
        BoundaryAssert.Equal(stream.Length, stream.Position);
        BoundaryAssert.Equal(2, adapter.ReleaseCount);
        BoundaryAssert.Equal(0, adapter.AbortCount);
    }

    private static BridgeEnvelope Envelope(string type, string value) => new()
    {
        Type = type,
        Payload = BridgeJson.ToElement(new { value }),
    };
}

internal sealed class ManualEventWriteAdapter : IBridgeFrameWriteAdapter, IAsyncDisposable
{
    private readonly SemaphoreSlim _writerGate;
    private readonly CancellationTokenSource _session = new();
    private readonly CancellationTokenSource _deadline = new();
    private readonly BridgeFrameChannel _channel;
    private int _channelAccessCount;
    private int _waitingWriters;

    internal ManualEventWriteAdapter(
        bool initiallyAdmitWriter,
        bool blockFrameBody,
        bool releaseBlockedBody = false)
    {
        _writerGate = new SemaphoreSlim(initiallyAdmitWriter ? 1 : 0, 1);
        Stream = new ManualFrameStream(blockFrameBody, releaseBlockedBody);
        _channel = new BridgeFrameChannel(Stream, 64 * 1024);
    }

    internal ManualFrameStream Stream { get; }
    internal Task FirstWriterWaiting => _firstWriterWaiting.Task;
    internal Task SecondWriterWaiting => _secondWriterWaiting.Task;
    internal int AbortCount { get; private set; }
    internal int ChannelAccessCount => Volatile.Read(ref _channelAccessCount);
    internal int ReleaseCount { get; private set; }
    internal TimeSpan ObservedDeadline { get; private set; }

    private readonly TaskCompletionSource _firstWriterWaiting = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _secondWriterWaiting = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public BridgeFrameChannel? Channel
    {
        get
        {
            Interlocked.Increment(ref _channelAccessCount);
            return _channel;
        }
    }
    public CancellationToken SessionCancellation => _session.Token;

    public async Task AcquireWriterAsync(CancellationToken cancellationToken)
    {
        var waiting = Interlocked.Increment(ref _waitingWriters);
        if (waiting == 1) _firstWriterWaiting.TrySetResult();
        if (waiting == 2) _secondWriterWaiting.TrySetResult();
        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void ReleaseWriter()
    {
        ReleaseCount++;
        _writerGate.Release();
    }

    public CancellationTokenSource CreateDeadline(TimeSpan timeout)
    {
        ObservedDeadline = timeout;
        return CancellationTokenSource.CreateLinkedTokenSource(
            _session.Token, _deadline.Token);
    }

    public void AbortSession()
    {
        AbortCount++;
        _session.Cancel();
    }

    internal void AdmitWriter() => _writerGate.Release();
    internal void TriggerDeadline() => _deadline.Cancel();

    public async ValueTask DisposeAsync()
    {
        _session.Cancel();
        _deadline.Cancel();
        await Stream.DisposeAsync();
        _deadline.Dispose();
        _session.Dispose();
        _writerGate.Dispose();
    }
}

internal sealed class ManualFrameStream(
    bool blockFrameBody,
    bool releaseBlockedBody) : Stream
{
    private readonly MemoryStream _bytes = new();
    private int _writeCount;

    internal Task BodyWriteEntered => _bodyWriteEntered.Task;
    internal byte[] Bytes => _bytes.ToArray();

    private readonly TaskCompletionSource _bodyWriteEntered = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _bytes.Length;
    public override long Position
    {
        get => _bytes.Position;
        set => throw new NotSupportedException();
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var write = Interlocked.Increment(ref _writeCount);
        if (blockFrameBody && write == 2)
            return new ValueTask(BlockBodyAsync(buffer.ToArray(), cancellationToken));
        _bytes.Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    private async Task BlockBodyAsync(
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        _bodyWriteEntered.TrySetResult();
        await _bodyRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (releaseBlockedBody) _bytes.Write(buffer);
    }

    private readonly TaskCompletionSource _bodyRelease = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal void ReleaseBody() => _bodyRelease.TrySetResult();

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        _bytes.Write(buffer, offset, count);
}

internal static class BoundaryAssert
{
    internal static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    internal static async Task<T> ThrowsAsync<T>(Func<Task> action)
        where T : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
