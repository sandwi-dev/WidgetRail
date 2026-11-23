using System.Runtime.InteropServices.WindowsRuntime;
using WidgetRail.RemoteMediaTransport;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>
/// Host-owned, read-only WinRT cursors over checked public media blocks. Native
/// clones share a bounded byte source, never a mutable shared stream position.
/// </summary>
internal sealed partial class NativePublicMediaStream : IAsyncDisposable
{
    private readonly PublicMediaBlockSource blocks;
    private readonly string contentType;
    private readonly object sync = new();
    private readonly HashSet<ReadView> views = [];
    private Task? retirement;
    internal long Length => blocks.Length;
    internal PublicMediaBlockSource.BufferedSnapshot? TryTakeBufferedSnapshot() => blocks.TryTakeBufferedSnapshot();

    internal NativePublicMediaStream(PublicMediaBlockSource blocks, string contentType)
    { this.blocks = blocks; this.contentType = contentType; }

    internal IRandomAccessStream CreateView(ulong position = 0)
    {
        if (position > long.MaxValue) throw new ArgumentOutOfRangeException(nameof(position));
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(retirement is not null, this);
            if (views.Count >= 32) throw new IOException("The media reader limit is in use.");
            var view = new ReadView(this, (long)position); views.Add(view); return view;
        }
    }

    private void Release(ReadView view) { lock (sync) views.Remove(view); }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (retirement is not null) return new(retirement);
            // Retire admission before canceling native cursors. Their Dispose
            // callbacks may reenter Release on this same thread.
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            retirement = done.Task;
            foreach (var view in views.ToArray()) view.Dispose();
            views.Clear();
            _ = FinishAsync(done);
            return new(retirement);
        }
    }
    private async Task FinishAsync(TaskCompletionSource done)
    {
        try { await blocks.DisposeAsync().ConfigureAwait(false); done.TrySetResult(); }
        catch (Exception error) { done.TrySetException(error); }
    }

    internal sealed partial class ReadView : IRandomAccessStreamWithContentType
    {
        private readonly NativePublicMediaStream owner;
        private readonly CancellationTokenSource shutdown = new();
        private readonly CancellationToken shutdownToken;
        private long position;
        private int closed, reading;
        internal ReadView(NativePublicMediaStream owner, long position)
        { this.owner = owner; this.position = position; shutdownToken = shutdown.Token; }
        public bool CanRead => Volatile.Read(ref closed) == 0;
        public bool CanWrite => false;
        public string ContentType => owner.contentType;
        public ulong Position => (ulong)Interlocked.Read(ref position);
        public ulong Size { get => (ulong)owner.blocks.Length; set => throw new NotSupportedException("Media is read-only."); }
        public IInputStream GetInputStreamAt(ulong value) { DemandOpen(); return owner.CreateView(value); }
        public IOutputStream GetOutputStreamAt(ulong value) => throw new NotSupportedException("Media is read-only.");
        public IRandomAccessStream CloneStream() { DemandOpen(); return owner.CreateView(Position); }
        public void Seek(ulong value)
        {
            DemandOpen();
            if (value > long.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
            if (Volatile.Read(ref reading) != 0) throw new InvalidOperationException("A media read is in progress.");
            Interlocked.Exchange(ref position, (long)value);
        }
        public IAsyncOperationWithProgress<IBuffer, uint> ReadAsync(IBuffer buffer, uint count, InputStreamOptions options)
        {
            DemandOpen(); ArgumentNullException.ThrowIfNull(buffer);
            if (count > buffer.Capacity || count > 1024 * 1024 || !Enum.IsDefined(options)) throw new ArgumentOutOfRangeException(nameof(count));
            if (Interlocked.CompareExchange(ref reading, 1, 0) != 0) throw new InvalidOperationException("A media read is in progress.");
            return AsyncInfo.Run<IBuffer, uint>(async (token, progress) =>
            {
                try
                {
                    using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, shutdownToken);
                    var bytes = new byte[count];
                    var read = await owner.blocks.ReadAtAsync(Interlocked.Read(ref position), bytes, lifetime.Token).ConfigureAwait(false);
                    lifetime.Token.ThrowIfCancellationRequested();
                    Interlocked.Add(ref position, read); progress.Report((uint)read);
                    if (read == 0) { buffer.Length = 0; return buffer; }
                    // WinRT permits returning a different buffer; callers must use
                    // the returned buffer. This avoids an additional native copy.
                    return bytes.AsBuffer(0, read);
                }
                finally { Interlocked.Exchange(ref reading, 0); }
            });
        }
        public IAsyncOperationWithProgress<uint, uint> WriteAsync(IBuffer buffer) =>
            AsyncInfo.Run<uint, uint>((_, _) => Task.FromException<uint>(new NotSupportedException("Media is read-only.")));
        public IAsyncOperation<bool> FlushAsync() => AsyncInfo.Run(_ => Task.FromResult(true));
        private void DemandOpen() => ObjectDisposedException.ThrowIf(Volatile.Read(ref closed) != 0, this);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0) return;
            shutdown.Cancel(); shutdown.Dispose(); owner.Release(this);
        }
    }
}
