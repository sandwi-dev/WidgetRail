using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Composition;
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
internal sealed class NativePackageIconTintCache : IDisposable
{
    private static readonly ConditionalWeakTable<XamlRoot, NativePackageIconTintCache> Caches = new();
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
    private int pending;
    private long reserved;
    private long useSequence;
    private bool disposed;
    internal int PreparationCount { get; private set; }
    internal Action<ReadOnlyMemory<byte>, int, int>? RasterizedForTesting { get; set; }
    internal long ReservedBytes => reserved;
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
        AutomationProperties.SetAccessibilityView(staging, AccessibilityView.Raw);
        owner.Children.Add(staging);
        owner.Unloaded += OwnerUnloaded;
    }
    private void OwnerUnloaded(object sender, RoutedEventArgs args) { if (!owner.IsLoaded) Dispose(); }
    internal static NativePackageIconTintCache For(XamlRoot root)
    {
        if (Caches.TryGetValue(root, out var previous) && previous.disposed) Caches.Remove(root);
        return Caches.GetValue(root, value => new(value));
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
            entry.Ready = PrepareAsync(payload, pixels, entry);
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
            await svgStream.WriteAsync(payload.NormalizedSvg.ToArray().AsBuffer()).AsTask(token); svgStream.Seek(0);
            var svg = new SvgImageSource { RasterizePixelWidth = extent, RasterizePixelHeight = extent };
            if (await svg.SetSourceAsync(svgStream).AsTask(token) != SvgImageSourceLoadStatus.Success) throw new InvalidDataException("Native SVG decode failed.");
            image = new() { Source = svg, Width = extent, Height = extent, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            var elementReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void ElementLoaded(object sender, RoutedEventArgs args) => elementReady.TrySetResult();
            image.Loaded += ElementLoaded;
            staging.Children.Add(image);
            try { if (!image.IsLoaded) await elementReady.Task.WaitAsync(token); }
            finally { image.Loaded -= ElementLoaded; }
            image.Measure(new Size(extent, extent)); image.Arrange(new Rect(0, 0, extent, extent));
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(image, extent, extent).AsTask(token);
            ++PreparationCount;
            if (bitmap.PixelWidth is < 1 or > 512 || bitmap.PixelHeight is < 1 or > 512 ||
                (long)bitmap.PixelWidth * bitmap.PixelHeight * 4 > entry.Bytes)
                throw new InvalidDataException("Native SVG raster exceeds the reserved dimensions.");
            var pixelsBuffer = (await bitmap.GetPixelsAsync().AsTask(token)).ToArray();
            RasterizedForTesting?.Invoke(pixelsBuffer, bitmap.PixelWidth, bitmap.PixelHeight);
            using var png = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png).AsTask(token);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixelsBuffer);
            await encoder.FlushAsync().AsTask(token); png.Seek(0);
            surface = LoadedImageSurface.StartLoadFromStream(png, new Size(extent, extent));
            var ready = new TaskCompletionSource<LoadedImageSourceLoadStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(LoadedImageSurface sender, LoadedImageSourceLoadCompletedEventArgs args) => ready.TrySetResult(args.Status);
            surface.LoadCompleted += Completed;
            try { if (await ready.Task.WaitAsync(token) != LoadedImageSourceLoadStatus.Success) throw new InvalidDataException("Native icon surface decode failed."); }
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
    private void Remove((string Hash, int Pixels) key, Entry entry)
    {
        if (entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
        { entries.Remove(key); reserved -= entry.Bytes; entry.Surface?.Dispose(); entry.Surface = null; }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; lifetime.Cancel();
        owner.Unloaded -= OwnerUnloaded; owner.Children.Remove(staging);
        foreach (var entry in entries.Values) { entry.Surface?.Dispose(); entry.Surface = null; }
        entries.Clear(); reserved = 0;
    }
}
