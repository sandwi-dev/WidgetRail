using System.Buffers.Binary;
using GameBarAlternative.WidgetBridge;

internal static class BridgeEventWriteBoundaryScenarios
{
    private const int MaximumMessageBytes = 64 * 1024;

    internal static async Task CancellationPreservesFrameBoundary()
    {
        await QueuedCancellationWritesNothingAsync();
        await StartedFrameDeadlineEndsSessionAsync();
    }

    private static async Task QueuedCancellationWritesNothingAsync()
    {
        await using var adapter = new ManualEventWriteAdapter(
            initiallyAdmitWriter: false,
            blockFrameBody: false);
        var boundary = new BridgeEventWriteBoundary(adapter);
        using var cancellation = new CancellationTokenSource();

        var withdrawn = boundary.WriteAsync(
            Envelope(BridgeMessageTypes.Invalidation, "withdrawn"),
            cancellation.Token);
        await adapter.FirstWriterWaiting.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await withdrawn.WaitAsync(TimeSpan.FromSeconds(2));

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
        BoundaryAssert.Equal(0, adapter.AbortCount);
    }

    private static async Task StartedFrameDeadlineEndsSessionAsync()
    {
        await using var adapter = new ManualEventWriteAdapter(
            initiallyAdmitWriter: true,
            blockFrameBody: true);
        var boundary = new BridgeEventWriteBoundary(adapter);
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
        await Task.WhenAll(partial, excluded).WaitAsync(TimeSpan.FromSeconds(2));

        BoundaryAssert.Equal(1, adapter.AbortCount);
        BoundaryAssert.Equal(1, adapter.ReleaseCount);
        BoundaryAssert.Equal(sizeof(int), adapter.Stream.Bytes.Length);
        BoundaryAssert.True(
            adapter.SessionCancellation.IsCancellationRequested,
            "The partial-frame timeout did not terminate the session.");
        BoundaryAssert.Equal(
            BridgeEventWriteBoundary.WriteDeadline,
            adapter.ObservedDeadline);
    }

    private static BridgeEnvelope Envelope(string type, string value) => new()
    {
        Type = type,
        Payload = BridgeJson.ToElement(new { value }),
    };
}

internal sealed class ManualEventWriteAdapter : IBridgeEventWriteAdapter, IAsyncDisposable
{
    private readonly SemaphoreSlim _writerGate;
    private readonly CancellationTokenSource _session = new();
    private readonly CancellationTokenSource _deadline = new();
    private int _waitingWriters;

    internal ManualEventWriteAdapter(bool initiallyAdmitWriter, bool blockFrameBody)
    {
        _writerGate = new SemaphoreSlim(initiallyAdmitWriter ? 1 : 0, 1);
        Stream = new ManualFrameStream(blockFrameBody);
        Channel = new BridgeFrameChannel(Stream, 64 * 1024);
    }

    internal ManualFrameStream Stream { get; }
    internal Task FirstWriterWaiting => _firstWriterWaiting.Task;
    internal Task SecondWriterWaiting => _secondWriterWaiting.Task;
    internal int AbortCount { get; private set; }
    internal int ReleaseCount { get; private set; }
    internal TimeSpan ObservedDeadline { get; private set; }

    private readonly TaskCompletionSource _firstWriterWaiting = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _secondWriterWaiting = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public BridgeFrameChannel? Channel { get; }
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

internal sealed class ManualFrameStream(bool blockFrameBody) : Stream
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
            return new ValueTask(BlockBodyAsync(cancellationToken));
        _bytes.Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    private async Task BlockBodyAsync(CancellationToken cancellationToken)
    {
        _bodyWriteEntered.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
            .ConfigureAwait(false);
    }

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
}
