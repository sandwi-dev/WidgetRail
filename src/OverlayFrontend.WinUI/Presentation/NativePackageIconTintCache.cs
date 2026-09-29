using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.WidgetPresentationSession;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// Native SVG rasterization occurs once per content hash and size bucket. Only
/// WinUI draws SVG; PNG encoding transports its unchanged alpha to composition.
/// Tint changes update a color brush and never repaint or rewrite SVG content.
/// </summary>
internal sealed class NativePackageIconTintCache : IDisposable, IAsyncDisposable
{
    private static readonly ConditionalWeakTable<XamlRoot, NativePackageIconTintCache> Caches = new();
    private static readonly ConditionalWeakTable<DispatcherQueue, DispatcherLifetime> Dispatchers = new();
    private sealed class DispatcherLifetime
    {
        internal HashSet<NativePackageIconTintCache> Caches { get; } = [];
        internal bool Closing;
        internal Task? Drain;
    }
    private const int MaximumEntries = 64;
    private const long MaximumBytes = 8 * 1024 * 1024;
    private readonly Panel owner;
    private readonly Canvas staging = new() { Width = 1, Height = 1, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        RenderTransform = new TranslateTransform { X = -4096, Y = -4096 },
        Clip = new RectangleGeometry { Rect = new Rect(0, 0, 0, 0) } };
    private readonly Dictionary<(string Hash, int Pixels), Entry> entries = [];
    private readonly SemaphoreSlim preparation = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherLifetime dispatcherLifetime;
    private readonly HashSet<Task> preparations = [];
    private Task? disposal;
    private int nativeOperations;
    private int pending;
    private long reserved;
    private long useSequence;
    private bool disposed;
    internal int PreparationCount { get; private set; }
    internal Action<ReadOnlyMemory<byte>, int, int>? RasterizedForTesting { get; set; }
    internal long ReservedBytes => reserved;
    internal int PendingPreparations => preparations.Count;
    internal int PendingNativeOperations => nativeOperations;
    internal bool StagingAttached => staging.Parent is not null;
    internal Action<string, bool>? NativeOperationStartedForTesting { get; set; }
    internal sealed class Entry
    {
        internal Task<LoadedImageSurface> Ready = null!;
        internal required long Bytes;
        internal int References;
        internal long LastUse;
        internal LoadedImageSurface? Surface;
    }
    internal sealed class Lease(NativePackageIconTintCache owner, Entry entry) : IDisposable
    {
        private NativePackageIconTintCache? cache = owner;
        internal LoadedImageSurface Surface => entry.Surface ?? throw new ObjectDisposedException(nameof(Lease));
        public void Dispose() { var current = Interlocked.Exchange(ref cache, null); if (current is not null) { --entry.References; entry.LastUse = ++current.useSequence; } }
    }
    private NativePackageIconTintCache(XamlRoot root)
    {
        owner = root.Content as Panel ?? throw new NotSupportedException("Native icon preparation requires a panel-owned XAML root.");
        dispatcherLifetime = Dispatchers.GetValue(owner.DispatcherQueue, _ => new());
        ObjectDisposedException.ThrowIf(dispatcherLifetime.Closing, this);
        dispatcherLifetime.Caches.Add(this);
        AutomationProperties.SetAccessibilityView(staging, AccessibilityView.Raw);
        owner.Children.Add(staging);
        owner.Unloaded += OwnerUnloaded;
    }
    private void OwnerUnloaded(object sender, RoutedEventArgs args) { if (!owner.IsLoaded) Dispose(); }
    internal static NativePackageIconTintCache For(XamlRoot root)
    {
        var dispatcher = root.Content?.DispatcherQueue ?? throw new InvalidOperationException("Icon root is unavailable.");
        if (!dispatcher.HasThreadAccess) throw new InvalidOperationException("Access icon caches on their native dispatcher.");
        ObjectDisposedException.ThrowIf(Dispatchers.GetValue(dispatcher, _ => new()).Closing, typeof(NativePackageIconTintCache));
        if (Caches.TryGetValue(root, out var previous) && previous.disposed)
        {
            if (previous.disposal?.IsCompleted != true) throw new ObjectDisposedException(nameof(NativePackageIconTintCache));
            Caches.Remove(root);
        }
        return Caches.GetValue(root, value => new(value));
    }
    /// <summary>Call on the UI dispatcher before closing its final XAML window.</summary>
    internal static Task ShutdownAsync(DispatcherQueue dispatcher)
    {
        if (!dispatcher.HasThreadAccess) throw new InvalidOperationException("Drain icon caches on their native dispatcher.");
        var state = Dispatchers.GetValue(dispatcher, _ => new());
        state.Closing = true; // Even a late request for a previously unused root must be rejected.
        return state.Drain ??= DrainDispatcherAsync(state, dispatcher);
    }
    private static async Task DrainDispatcherAsync(DispatcherLifetime state, DispatcherQueue dispatcher)
    {
        await Task.WhenAll(state.Caches.ToArray().Select(cache => cache.DisposeAsync().AsTask()));
        await DispatcherTurnAsync(dispatcher);
    }
    private static Task DispatcherTurnAsync(DispatcherQueue dispatcher)
    {
        var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => turn.TrySetResult()))
            throw new InvalidOperationException("Icon shutdown must precede dispatcher shutdown.");
        return turn.Task;
    }
    internal async Task<Lease> AcquireAsync(WidgetPresentationPackageIcon payload, int pixels, CancellationToken cancellation)
    {
        if (!owner.DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Prepare package icons on their native dispatcher.");
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellation.ThrowIfCancellationRequested();
        pixels = Math.Clamp(pixels, 32, 512);
        var key = (payload.NormalizedSha256, pixels);
        if (!entries.TryGetValue(key, out var entry))
        {
            var bytes = (long)pixels * pixels * 4;
            while (entries.Count >= MaximumEntries || reserved + bytes > MaximumBytes)
            {
                var idle = entries.Where(pair => pair.Value.References == 0 && pair.Value.Ready.IsCompleted)
                    .OrderBy(pair => pair.Value.LastUse).FirstOrDefault();
                if (idle.Value is null) throw new InvalidOperationException("Native package icon surface budget is full.");
                Remove(idle.Key, idle.Value);
            }
            if (pending >= 16) throw new InvalidOperationException("Native package icon preparation capacity is full.");
            ++pending; reserved += bytes;
            entry = new() { Bytes = bytes };
            entries.Add(key, entry);
            // Register ownership before native work can start or reenter shutdown.
            // A caller may abandon/evict its entry; the preparation still drains.
            var completion = new TaskCompletionSource<LoadedImageSurface>(TaskCreationOptions.RunContinuationsAsynchronously);
            entry.Ready = completion.Task;
            preparations.Add(entry.Ready);
            _ = RunPreparationAsync(payload, pixels, entry, completion);
            _ = entry.Ready.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        ++entry.References;
        try
        {
            await entry.Ready.WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(disposed, this);
            return new(this, entry);
        }
        catch
        {
            --entry.References;
            if (entry.Ready.IsFaulted || entry.Ready.IsCanceled) Remove(key, entry);
            throw;
        }
    }
    private async Task RunPreparationAsync(WidgetPresentationPackageIcon payload, int pixels, Entry entry,
        TaskCompletionSource<LoadedImageSurface> completion)
    {
        try { completion.TrySetResult(await PrepareAsync(payload, pixels, entry)); }
        catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { completion.TrySetException(error); }
        finally { preparations.Remove(completion.Task); }
    }
    private async Task<LoadedImageSurface> PrepareAsync(WidgetPresentationPackageIcon payload, int pixels, Entry entry)
    {
        var token = lifetime.Token;
        var admitted = false;
        Image? image = null;
        LoadedImageSurface? surface = null;
        try
        {
            await preparation.WaitAsync(token); admitted = true;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5)); token = deadline.Token;
            var scale = owner.XamlRoot.RasterizationScale;
            var extent = Math.Max(1, (int)Math.Floor(pixels / Math.Max(1, scale)));
            using var svgStream = new InMemoryRandomAccessStream();
            await svgStream.WriteAsync(payload.NormalizedSvg.ToArray().AsBuffer()).AsTask(); svgStream.Seek(0);
            token.ThrowIfCancellationRequested();
            var svg = new SvgImageSource { RasterizePixelWidth = extent, RasterizePixelHeight = extent };
            if (await NativeAsync(svg.SetSourceAsync(svgStream), "svg") != SvgImageSourceLoadStatus.Success) throw new InvalidDataException("Native SVG decode failed.");
            token.ThrowIfCancellationRequested();
            image = new() { Source = svg, Width = extent, Height = extent, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            var elementReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void ElementLoaded(object sender, RoutedEventArgs args) => elementReady.TrySetResult();
            image.Loaded += ElementLoaded;
            staging.Children.Add(image);
            try { if (!image.IsLoaded) await elementReady.Task.WaitAsync(token); }
            finally { image.Loaded -= ElementLoaded; }
            image.Measure(new Size(extent, extent)); image.Arrange(new Rect(0, 0, extent, extent));
            var bitmap = new RenderTargetBitmap();
            await NativeAsync(bitmap.RenderAsync(image, extent, extent), "render");
            token.ThrowIfCancellationRequested();
            ++PreparationCount;
            if (bitmap.PixelWidth is < 1 or > 512 || bitmap.PixelHeight is < 1 or > 512 ||
                (long)bitmap.PixelWidth * bitmap.PixelHeight * 4 > entry.Bytes)
                throw new InvalidDataException("Native SVG raster exceeds the reserved dimensions.");
            var pixelsBuffer = (await NativeAsync(bitmap.GetPixelsAsync(), "pixels")).ToArray();
            token.ThrowIfCancellationRequested();
            RasterizedForTesting?.Invoke(pixelsBuffer, bitmap.PixelWidth, bitmap.PixelHeight);
            using var png = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png).AsTask();
            token.ThrowIfCancellationRequested();
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixelsBuffer);
            await encoder.FlushAsync().AsTask(); png.Seek(0);
            token.ThrowIfCancellationRequested();
            surface = LoadedImageSurface.StartLoadFromStream(png, new Size(extent, extent));
            var ready = new TaskCompletionSource<LoadedImageSourceLoadStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(LoadedImageSurface sender, LoadedImageSourceLoadCompletedEventArgs args) => ready.TrySetResult(args.Status);
            surface.LoadCompleted += Completed;
            try { if (await NativeAsync(ready.Task, "surface") != LoadedImageSourceLoadStatus.Success) throw new InvalidDataException("Native icon surface decode failed."); }
            finally { surface.LoadCompleted -= Completed; }
            token.ThrowIfCancellationRequested();
            entry.Surface = surface;
            return surface;
        }
        catch { surface?.Dispose(); throw; }
        finally
        {
            if (image is not null) staging.Children.Remove(image);
            if (admitted) preparation.Release();
            --pending;
        }
    }
    private Task<T> NativeAsync<T>(Windows.Foundation.IAsyncOperation<T> operation, string stage) => NativeAsync(operation.AsTask(), stage);
    private async Task NativeAsync(Windows.Foundation.IAsyncAction operation, string stage)
    {
        var task = operation.AsTask();
        ++nativeOperations;
        try
        {
            try { NativeOperationStartedForTesting?.Invoke(stage, !task.IsCompleted); }
            catch { await task; throw; }
            await task;
        }
        finally { --nativeOperations; }
    }
    private async Task<T> NativeAsync<T>(Task<T> task, string stage)
    {
        ++nativeOperations;
        try
        {
            try { NativeOperationStartedForTesting?.Invoke(stage, !task.IsCompleted); }
            catch { await task; throw; }
            return await task;
        }
        finally { --nativeOperations; }
    }
    private void Remove((string Hash, int Pixels) key, Entry entry)
    {
        if (entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
        { entries.Remove(key); reserved -= entry.Bytes; entry.Surface?.Dispose(); entry.Surface = null; }
    }
    public void Dispose()
    {
        _ = DisposeAsync();
    }
    public ValueTask DisposeAsync()
    {
        if (!owner.DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Retire icon caches on their native dispatcher.");
        return new(disposal ??= DrainAsync());
    }
    private async Task DrainAsync()
    {
        disposed = true;
        owner.Unloaded -= OwnerUnloaded;
        lifetime.Cancel(); // Stop queued admission; started native operations retain their real completion.
        var pendingPreparations = preparations.ToArray();
        try
        {
            try { await Task.WhenAll(pendingPreparations); }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            { /* Per-icon cancellation/decode failure has already selected its semantic fallback. */ }
            // A native completion can resume an await inside its UI callback.
            // Unwind that callback before releasing the XAML staging tree/closing.
            if (pendingPreparations.Length != 0) await DispatcherTurnAsync(owner.DispatcherQueue);
        }
        finally
        {
            owner.Children.Remove(staging);
            foreach (var entry in entries.Values) { entry.Surface?.Dispose(); entry.Surface = null; }
            entries.Clear(); reserved = 0;
            dispatcherLifetime.Caches.Remove(this);
            preparation.Dispose(); lifetime.Dispose();
        }
    }
}
