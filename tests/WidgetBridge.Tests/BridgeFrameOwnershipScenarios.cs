using System.Buffers.Binary;
using System.Text.Json;
using WidgetRail.WidgetBridge;

internal static class BridgeFrameOwnershipScenarios
{
    private const int MaximumMessageBytes = 64 * 1024;

    internal static async Task TypedIndexedRepliesPreserveOwnership()
    {
        var payload = new BridgeIndexedArtworkResponse("widget", "instance", "runtime", "presentation",
            new("lease", "item"), "cover", "demand", "image/png", new byte[] { 1, 2, 3 });
        await using (var adapter = new ManualEventWriteAdapter(true, false))
        {
            var boundary = new BridgeFrameWriteBoundary(adapter);
            await boundary.WriteReplyAsync(BridgeMessageTypes.IndexedArtwork, 47, payload, CancellationToken.None);
            await using var bytes = new MemoryStream(adapter.Stream.Bytes);
            var frame = await new BridgeFrameChannel(bytes, MaximumMessageBytes).ReadAsync(CancellationToken.None);
            var read = BridgeJson.FromElement<BridgeIndexedArtworkResponse>(frame.Payload);
            BoundaryAssert.Equal(47L, frame.RequestId);
            BoundaryAssert.Equal(payload.Item, read.Item);
            BoundaryAssert.Equal(payload.DemandId, read.DemandId);
            BoundaryAssert.Equal(true, payload.ContentBase64.Span.SequenceEqual(read.ContentBase64.Span));
            BoundaryAssert.Equal(BridgeFrameWriteBoundary.WriteDeadline, adapter.ObservedDeadline);
            BoundaryAssert.Equal(1, adapter.ReleaseCount);
        }
        await using (var adapter = new ManualEventWriteAdapter(false, false))
        {
            var boundary = new BridgeFrameWriteBoundary(adapter);
            using var cancel = new CancellationTokenSource();
            var pending = boundary.WriteReplyAsync(BridgeMessageTypes.IndexedArtwork, 48, payload, cancel.Token);
            await adapter.FirstWriterWaiting;
            cancel.Cancel();
            await BoundaryAssert.ThrowsAsync<OperationCanceledException>(() => pending);
            BoundaryAssert.Equal(0, adapter.Stream.Bytes.Length);
        }
        var large = payload with { ContentBase64 = new byte[512 * 1024] };
        var channel = new BridgeFrameChannel(Stream.Null, 2 * 1024 * 1024);
        ValueTask Old() => channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.IndexedArtwork,
            RequestId = 47, Payload = BridgeJson.ToElement(large) }, CancellationToken.None);
        ValueTask Direct() => channel.WriteAsync(BridgeMessageTypes.IndexedArtwork, 47, large, CancellationToken.None);
        // Null-stream writes complete synchronously. Warm serializer metadata,
        // then measure allocation on this thread without timing or GC thresholds.
        Old().GetAwaiter().GetResult(); Direct().GetAwaiter().GetResult();
        long Measure(Func<ValueTask> write)
        {
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 8; ++i) write().GetAwaiter().GetResult();
            return GC.GetAllocatedBytesForCurrentThread() - start;
        }
        var oldBytes = Measure(Old); var directBytes = Measure(Direct);
        Console.WriteLine($"Indexed artwork reply allocations for 8 x 512 KiB: before={oldBytes}, direct={directBytes}");
        BoundaryAssert.Equal(true, directBytes < oldBytes * .8);

        await using var wire = new MemoryStream();
        var writer = new BridgeFrameChannel(wire, 2 * 1024 * 1024);
        await writer.WriteAsync(BridgeMessageTypes.IndexedArtwork, 1, large, CancellationToken.None);
        await writer.WriteAsync(BridgeMessageTypes.IndexedArtwork, 2, payload, CancellationToken.None);
        wire.Position = 0;
        var reader = new BridgeFrameChannel(wire, 2 * 1024 * 1024);
        var retained = await reader.ReadAsync(CancellationToken.None);
        var next = await reader.ReadAsync(CancellationToken.None);
        BoundaryAssert.Equal(2L, next.RequestId);
        BoundaryAssert.Equal(large.ContentBase64.Length,
            BridgeJson.FromElement<BridgeIndexedArtworkResponse>(retained.Payload).ContentBase64.Length);
        BoundaryAssert.Equal(true, BridgeJson.FromElement<BridgeIndexedArtworkResponse>(next.Payload)
            .ContentBase64.Span.SequenceEqual(payload.ContentBase64.Span));
        long ReadAllocations(bool reuse)
        {
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 8; ++i)
            {
                wire.Position = 0;
                var input = reuse ? reader : new BridgeFrameChannel(wire, 2 * 1024 * 1024);
                _ = input.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            return GC.GetAllocatedBytesForCurrentThread() - start;
        }
        var freshReads = ReadAllocations(false); var reusedReads = ReadAllocations(true);
        Console.WriteLine($"Indexed artwork frame read allocations for 8 x 512 KiB: fresh={freshReads}, reused={reusedReads}");
        BoundaryAssert.Equal(true, reusedReads < freshReads * .8);
    }

    internal static async Task TimeoutAndCancellationHaveExactOwners()
    {
        await AbandonedWaitReproducesRetainedBodyPrefixAsync();
        await TerminalReaderCancelsAndDrainsExactReadAsync();
        await RawReplyCancellationCanLeaveHeaderOnlyAsync();
    }

    internal static async Task TypedArtworkNotificationsPreserveWireContent()
    {
        foreach (var demandId in new string?[] { null, "demand-1", "demand-2" })
        foreach (var length in new[] { 0, 1, 2, 3 })
        {
            var content = Enumerable.Range(0, length)
                .Select(value => (byte)(value + 1)).ToArray();
            await using var stream = new MemoryStream();
            var channel = new BridgeFrameChannel(stream, MaximumMessageBytes);
            await channel.WriteAsync(
                BridgeMessageTypes.Artwork,
                0,
                new BridgeEncodedArtworkEvent(
                    "sample", "artwork", "runtime", "presentation",
                    length == 0 ? string.Empty : "image/png", content, demandId),
                CancellationToken.None);

            var frame = stream.ToArray();
            var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(frame);
            BoundaryAssert.Equal(frame.Length - sizeof(int), bodyLength);
            using var document = JsonDocument.Parse(
                frame.AsMemory(sizeof(int), bodyLength));
            var root = document.RootElement;
            BoundaryAssert.Equal(BridgeProtocol.CurrentVersion,
                root.GetProperty("protocolVersion").GetInt32());
            BoundaryAssert.Equal(BridgeMessageTypes.Artwork,
                root.GetProperty("type").GetString());
            BoundaryAssert.Equal(0L, root.GetProperty("requestId").GetInt64());
            var payload = root.GetProperty("payload");
            BoundaryAssert.Equal(Convert.ToBase64String(content),
                payload.GetProperty("contentBase64").GetString());
            BoundaryAssert.Equal("sample", payload.GetProperty("widgetId").GetString());
            BoundaryAssert.Equal("artwork", payload.GetProperty("artworkHandle").GetString());
            if (demandId is null)
                BoundaryAssert.Equal(false, payload.TryGetProperty("demandId", out _));
            else
                BoundaryAssert.Equal(demandId, payload.GetProperty("demandId").GetString());
        }

        await using var legacyStream = new MemoryStream();
        var legacyChannel = new BridgeFrameChannel(legacyStream, MaximumMessageBytes);
        await legacyChannel.WriteAsync(
            BridgeMessageTypes.Artwork,
            0,
            new BridgeLegacyArtworkEvent(
                "sample", "artwork", "runtime", "presentation", "image/png", "AQI="),
            CancellationToken.None);
        using var legacyDocument = JsonDocument.Parse(
            legacyStream.ToArray().AsMemory(sizeof(int)));
        BoundaryAssert.Equal("AQI=", legacyDocument.RootElement
            .GetProperty("payload").GetProperty("contentBase64").GetString());

        await using var boundedStream = new MemoryStream();
        var boundedChannel = new BridgeFrameChannel(boundedStream, 256);
        _ = await BoundaryAssert.ThrowsAsync<BridgeProtocolException>(() =>
            boundedChannel.WriteAsync(
                BridgeMessageTypes.Artwork,
                0,
                new BridgeEncodedArtworkEvent(
                    "sample", "artwork", "runtime", "presentation", "image/png",
                    new byte[256]),
                CancellationToken.None).AsTask());
        BoundaryAssert.Equal(0, boundedStream.ToArray().Length);
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
