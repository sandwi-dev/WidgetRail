using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

internal sealed class WidgetFileReadStream(IWidgetCapabilityClient client, WidgetFileReference file) : Stream
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private long position;
    private int disposed;
    public override bool CanRead => disposed == 0;
    public override bool CanSeek => disposed == 0;
    public override bool CanWrite => false;
    public override long Length { get { DemandOpen(); return file.ByteLength; } }
    public override long Position { get { DemandOpen(); return Interlocked.Read(ref position); } set => Seek(value, SeekOrigin.Begin); }
    public override void Flush() => DemandOpen();
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        DemandOpen();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await gate.WaitAsync(cancellation.Token).ConfigureAwait(false);
        try
        {
            DemandOpen();
            cancellation.Token.ThrowIfCancellationRequested();
            if (buffer.IsEmpty || position == file.ByteLength) return 0;
            var count = (int)Math.Min(Math.Min(buffer.Length, WidgetFileContract.ChunkBytes), file.ByteLength - position);
            var result = await client.InvokeAsync(WidgetFileCapabilities.Read, new(file.Id, position, count), cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            DemandOpen();
            // Each broker read is exact except EOF. A short/non-progressing response
            // must fail before any bytes or position become visible to the caller.
            if (result?.Bytes is null || result.Offset != position || result.Bytes.Length != count || result.EndOfFile != (position + count == file.ByteLength)) throw WidgetFileService.Malformed();
            result.Bytes.AsMemory().CopyTo(buffer);
            position += count;
            return count;
        }
        finally { gate.Release(); }
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        DemandOpen();
        gate.Wait();
        try
        {
            DemandOpen();
            long target;
            try { target = checked((origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => position, SeekOrigin.End => file.ByteLength, _ => throw new ArgumentOutOfRangeException(nameof(origin)) }) + offset); }
            catch (OverflowException error) { throw new IOException("The seek is outside the file.", error); }
            if (target < 0 || target > file.ByteLength) throw new IOException("The seek is outside the file.");
            return position = target;
        }
        finally { gate.Release(); }
    }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref disposed, 1) == 0) lifetime.Cancel();
        // Concurrent readers still own gate and cancellation registrations. These
        // managed primitives are collected after the last pending read completes.
        base.Dispose(disposing);
    }
    private void DemandOpen() => ObjectDisposedException.ThrowIf(disposed != 0, this);
}
