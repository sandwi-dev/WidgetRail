using WidgetRail.WidgetBridge;

internal static class BridgeFrameOwnershipScenarios
{
    private const int MaximumMessageBytes = 64 * 1024;

    internal static async Task TimeoutAndCancellationHaveExactOwners()
    {
        await AbandonedWaitReproducesRetainedBodyPrefixAsync();
        await TerminalReaderCancelsAndDrainsExactReadAsync();
        await RawReplyCancellationCanLeaveHeaderOnlyAsync();
    }

    private static async Task AbandonedWaitReproducesRetainedBodyPrefixAsync()
    {
        await using var stream = new ManualSequencedReadStream();
        var channel = new BridgeFrameChannel(stream, MaximumMessageBytes);
        var abandonedRead = channel.ReadAsync(CancellationToken.None).AsTask();
        await stream.FirstReadStarted.WaitAsync(TimeSpan.FromSeconds(2));

        using var waitCancellation = new CancellationTokenSource();
        var timedWait = abandonedRead.WaitAsync(waitCancellation.Token);
        waitCancellation.Cancel();
        _ = await BoundaryAssert.ThrowsAsync<OperationCanceledException>(() => timedWait);
        BoundaryAssert.Equal(1, stream.PendingReadCount);

        var successorRead = channel.ReadAsync(CancellationToken.None).AsTask();
        await stream.SecondReadStarted.WaitAsync(TimeSpan.FromSeconds(2));
        stream.Supply(await SerializeAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Acknowledged,
            RequestId = 77,
            Payload = BridgeJson.ToElement(new { }),
        }));

        var failure = await BoundaryAssert.ThrowsAsync<BridgeProtocolException>(
            () => successorRead);
        BoundaryAssert.True(
            failure.Message.Contains("1919951483", StringComparison.Ordinal),
            "The abandoned read did not reproduce the retained JSON-body prefix.");
        BoundaryAssert.True(
            !abandonedRead.IsCompleted,
            "The timed-out wrapper unexpectedly canceled its underlying frame read.");

        stream.Abort();
        _ = await BoundaryAssert.ThrowsAsync<IOException>(() => abandonedRead);
    }

    private static async Task TerminalReaderCancelsAndDrainsExactReadAsync()
    {
        await using var stream = new ManualSequencedReadStream();
        var reader = new BridgeTestFrameReader(
            new BridgeFrameChannel(stream, MaximumMessageBytes),
            stream.Abort);
        using var timeout = new CancellationTokenSource();

        var read = reader.ReadAsync(timeout.Token);
        await stream.FirstReadStarted.WaitAsync(TimeSpan.FromSeconds(2));
        timeout.Cancel();
        _ = await BoundaryAssert.ThrowsAsync<OperationCanceledException>(() => read);

        BoundaryAssert.True(reader.IsTerminal, "A timed-out test read stayed reusable.");
        BoundaryAssert.Equal(0, stream.PendingReadCount);
        BoundaryAssert.Equal(1, stream.MaximumPendingReads);
        BoundaryAssert.Equal(1, stream.AbortCount);
        _ = await BoundaryAssert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync(CancellationToken.None));
        BoundaryAssert.Equal(1, stream.ReadRequestCount);
    }

    private static async Task RawReplyCancellationCanLeaveHeaderOnlyAsync()
    {
        await using var stream = new ManualFrameStream(
            blockFrameBody: true,
            releaseBlockedBody: false);
        var channel = new BridgeFrameChannel(stream, MaximumMessageBytes);
        using var requestCancellation = new CancellationTokenSource();

        var write = channel.WriteAsync(new BridgeEnvelope
        {
            Type = BridgeMessageTypes.Acknowledged,
            RequestId = 88,
            Payload = BridgeJson.ToElement(new { }),
        }, requestCancellation.Token).AsTask();
        await stream.BodyWriteEntered.WaitAsync(TimeSpan.FromSeconds(2));
        requestCancellation.Cancel();
        _ = await BoundaryAssert.ThrowsAsync<OperationCanceledException>(() => write);

        BoundaryAssert.Equal(sizeof(int), stream.Bytes.Length);
    }

    private static async Task<byte[]> SerializeAsync(BridgeEnvelope envelope)
    {
        await using var stream = new MemoryStream();
        var channel = new BridgeFrameChannel(stream, MaximumMessageBytes);
        await channel.WriteAsync(envelope, CancellationToken.None);
        return stream.ToArray();
    }
}

internal sealed class ManualSequencedReadStream : Stream
{
    private readonly object _gate = new();
    private readonly Queue<ReadRequest> _requests = new();
    private readonly Queue<byte> _bytes = new();
    private bool _aborted;

    internal Task FirstReadStarted => _firstReadStarted.Task;
    internal Task SecondReadStarted => _secondReadStarted.Task;
    internal int PendingReadCount { get; private set; }
    internal int MaximumPendingReads { get; private set; }
    internal int ReadRequestCount { get; private set; }
    internal int AbortCount { get; private set; }

    private readonly TaskCompletionSource _firstReadStarted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _secondReadStarted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var request = new ReadRequest(buffer, cancellationToken);
        lock (_gate)
        {
            if (_aborted)
                return ValueTask.FromException<int>(new IOException("Stream aborted."));
            request.Active = true;
            _requests.Enqueue(request);
            PendingReadCount++;
            MaximumPendingReads = Math.Max(MaximumPendingReads, PendingReadCount);
            ReadRequestCount++;
            if (ReadRequestCount == 1) _firstReadStarted.TrySetResult();
            if (ReadRequestCount == 2) _secondReadStarted.TrySetResult();
            request.Registration = cancellationToken.UnsafeRegister(
                static state =>
                {
                    var pair = ((ManualSequencedReadStream Stream, ReadRequest Request))state!;
                    pair.Stream.Cancel(pair.Request);
                }, (this, request));
            PumpLocked();
        }
        return new ValueTask<int>(ObserveAsync(request));
    }

    internal void Supply(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        lock (_gate)
        {
            foreach (var value in bytes) _bytes.Enqueue(value);
            PumpLocked();
        }
    }

    internal void Abort()
    {
        lock (_gate)
        {
            if (_aborted) return;
            _aborted = true;
            AbortCount++;
            while (_requests.Count > 0)
            {
                var request = _requests.Dequeue();
                if (!request.Active) continue;
                request.Active = false;
                PendingReadCount--;
                request.Completion.TrySetException(new IOException("Stream aborted."));
            }
            _bytes.Clear();
        }
    }

    private async Task<int> ObserveAsync(ReadRequest request)
    {
        try
        {
            return await request.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            request.Registration.Dispose();
        }
    }

    private void Cancel(ReadRequest request)
    {
        lock (_gate)
        {
            if (!request.Active) return;
            request.Active = false;
            PendingReadCount--;
            request.Completion.TrySetCanceled(request.CancellationToken);
        }
    }

    private void PumpLocked()
    {
        while (_bytes.Count > 0 && _requests.Count > 0)
        {
            var request = _requests.Dequeue();
            if (!request.Active) continue;
            var count = Math.Min(request.Buffer.Length, _bytes.Count);
            for (var index = 0; index < count; index++)
                request.Buffer.Span[index] = _bytes.Dequeue();
            request.Active = false;
            PendingReadCount--;
            request.Completion.TrySetResult(count);
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override ValueTask DisposeAsync()
    {
        Abort();
        Dispose(false);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private sealed class ReadRequest(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        internal Memory<byte> Buffer { get; } = buffer;
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal TaskCompletionSource<int> Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenRegistration Registration { get; set; }
        internal bool Active { get; set; }
    }
}
