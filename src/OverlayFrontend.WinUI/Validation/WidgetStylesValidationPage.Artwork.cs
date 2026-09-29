using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetProtocol;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeArtworkAsync()
    {
        var checkStart = checks.Count;
        var bytes = await ArtworkPngAsync(2048, 1536);
        var before = NativeArtworkCounters.Snapshot();
        var image = new Image { Width = 160, Height = 96, Stretch = Stretch.UniformToFill };
        var card = new Button { Content = image, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top };
        var zoom = new OverlayScaleRoot { Width = 680, Height = 360 };
        zoom.Children.Add(new Grid { Children = { card } }); host.Children.Add(zoom);
        using var style = new NativeComputedStyleAdapter(card);
        style.Update(Compute("#art { transition-duration: 70ms; } #art:focused { scale: 1.12; } #art:pressed { scale: 0.9; }", "art", "button"));
        using var lifetime = new CancellationTokenSource();
        var tasks = new List<Task>();
        Exception? failure = null;
        var complete = false;
        var active = true;
        TaskCompletionSource? delay = null;
        TaskCompletionSource? entered = null;
        WidgetPresentationSurface? background = null;
        using var demand = new NativeArtworkDemand(image, ImageFit.Cover, () => active,
            async token =>
            {
                if (delay is { } pause) { entered!.TrySetResult(); await pause.Task; }
                return new(bytes);
            }, source => image.Source = source, value => complete = value, tasks.Add,
            error => failure = error, lifetime.Token);
        try
        {
            await Wait(() => complete || failure is not null);
            if (failure is not null) throw failure;
            Check(demand.LastDecode is { Source.Width: 2048, Source.Height: 1536 } &&
                demand.LastDecode.Decoded.Width < 512 && demand.LastDecode.Decoded.Height < 512,
                "large native artwork decodes for its card instead of its full source dimensions");
            Check(demand.LastDecode!.Decoded.Width >= image.ActualWidth * XamlRoot.RasterizationScale * 1.12,
                "unfocused artwork already covers its declared focused scale");
            Check(demand.LastDecode.Image.DecodePixelType == DecodePixelType.Physical &&
                demand.LastDecode.Image.DecodePixelWidth == demand.LastDecode.Decoded.Width,
                "native bitmap receives explicit physical decode dimensions before SetSourceAsync");
            // Image source publication can change its uniform-fill ink extent.
            // Establish that native layout before measuring focus-only motion.
            image.UpdateLayout();
            var stableTarget = NativeArtworkDemand.TargetPixels(image);
            var stableRequests = NativeArtworkCounters.Snapshot().Requests;
            for (var phase = 0; phase < 4; ++phase)
            {
                card.Focus(FocusState.Keyboard); style.SetControllerPressed(phase % 2 == 0);
                await Wait(() => style.ScaleMotion?.IsAnimating != true);
                Check(NativeArtworkDemand.TargetPixels(image) == stableTarget,
                    "focus/press facade scaling does not change the artwork decode target");
                demand.RefreshSize(); await Task.Delay(30);
            }
            style.SetControllerPressed(false); outside.Focus(FocusState.Keyboard);
            await Wait(() => style.ScaleMotion?.IsAnimating != true);
            Check(NativeArtworkCounters.Snapshot().Requests == stableRequests,
                "repeated focus and press transitions do not restart artwork acquisition");
            var initialWidth = demand.LastDecode.Decoded.Width;
            foreach (var scale in new[] { 1.125, 1.25 })
            {
                zoom.InterfaceScale = scale;
                Check(zoom.InterfaceScale == scale, "artwork fixture applies a supported interface scale without clamping");
                Check(NativeArtworkDemand.TargetPixels(image).Width >= image.ActualWidth * XamlRoot.RasterizationScale * scale * 1.12,
                    "artwork demand sees current interface scale before another layout pass");
                await Wait(() => complete && demand.LastDecode!.Decoded.Width >= image.ActualWidth * XamlRoot.RasterizationScale * scale * 1.12);
                Check(demand.LastDecode!.Decoded.Width >= initialWidth,
                    $"retained or upgraded native artwork covers {scale:P1} interface scale without a new declaration");
                initialWidth = demand.LastDecode.Decoded.Width;
            }
            var count = NativeArtworkCounters.Snapshot().Requests;
            for (var i = 0; i < 8; ++i) demand.Refresh(ImageFit.Cover);
            await Task.Delay(50);
            Check(NativeArtworkCounters.Snapshot().Requests == count,
                "unchanged repeated artwork demand does not reacquire or decode");

            var previous = image.Source;
            delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            image.Width = 224;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!complete && ReferenceEquals(image.Source, previous), "size replacement retains old pixels while resolution is pending");
            delay.TrySetResult(); delay = null;
            await Wait(() => complete && !ReferenceEquals(image.Source, previous));
            Check(demand.LastDecode!.Decoded.Width >= image.ActualWidth * XamlRoot.RasterizationScale * zoom.InterfaceScale * 1.12,
                "resized poster publishes sufficient resolution after native decode completes");

            var retained = image.Source;
            var retainedRequests = NativeArtworkCounters.Snapshot().Requests;
            var retainedWidth = image.ActualWidth;
            image.Width = 120;
            await Wait(() => image.ActualWidth < retainedWidth - 1); await Task.Delay(40);
            image.Width = 224;
            await Wait(() => Math.Abs(image.ActualWidth - retainedWidth) < 1); await Task.Delay(40);
            Check(ReferenceEquals(retained, image.Source) && NativeArtworkCounters.Snapshot().Requests == retainedRequests,
                "resize reversal reuses the sufficient retained decode instead of shrinking and regrowing it");

            previous = image.Source;
            delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            image.Width = 272;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            lifetime.Cancel(); delay.TrySetResult(); delay = null;
            await Task.WhenAll(tasks);
            Check(ReferenceEquals(image.Source, previous), "cancelled late artwork cannot overwrite the retained image");

            using var secondLifetime = new CancellationTokenSource();
            background = new WidgetPresentationSurface(ViewNodeKind.BackgroundSurface) { Width = 620, Height = 300 };
            host.Children.Add(background);
            var backgroundReady = false;
            using var backgroundDemand = new NativeArtworkDemand(background, ImageFit.Cover, () => true,
                _ => Task.FromResult<NativeArtworkPayload?>(new(bytes)), source => background.SetArtwork(source, ImageFit.Cover),
                value => backgroundReady = value, tasks.Add, error => failure = error, secondLifetime.Token);
            await Wait(() => backgroundReady || failure is not null);
            if (failure is not null) throw failure;
            Check(backgroundDemand.LastDecode!.Decoded.Width > demand.LastDecode?.Decoded.Width ||
                backgroundDemand.LastDecode.Decoded.Width > 620,
                "background surface owns a separate larger decode than the poster");
            host.Children.Remove(background); backgroundDemand.Dispose(); await background.DisposeAsync();

            var slice = new byte[bytes.Length + 12]; bytes.CopyTo(slice, 7);
            var sliced = await NativeArtworkDecoder.DecodeAsync(new(slice.AsMemory(7, bytes.Length)), new(80, 80), ImageFit.Contain, CancellationToken.None);
            Check(sliced is { Source.Width: 2048, Source.Height: 1536 } && sliced.Decoded.Width == 80,
                "read-only artwork stream honors a value-owned memory slice without copying its buffer");

            var after = NativeArtworkCounters.Snapshot();
            Check(after.CopiedBytes == before.CopiedBytes && after.ConfiguredPixelBytes - before.ConfiguredPixelBytes <
                (after.NaturalPixelBytes - before.NaturalPixelBytes) / 3,
                "array-backed artwork avoids encoded copies and configures substantially fewer decoded pixels");
            Check(after.Cancelled > before.Cancelled, "diagnostics distinguish cancelled requests from completed decoding");
            var rejected = false;
            var oversized = await ArtworkPngAsync(ProtocolConstants.MaximumEncodedArtworkDimension + 1, 1);
            var decodes = NativeArtworkCounters.Snapshot().Decodes;
            try { await NativeArtworkDecoder.DecodeAsync(new(oversized), new(32, 32), ImageFit.Contain, CancellationToken.None); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && NativeArtworkCounters.Snapshot().Decodes == decodes,
                "native metadata rejects oversized source dimensions before bitmap pixel decode");
            Check(NativeArtworkDecoder.SizeFor(new(400, 200), new(100, 100), ImageFit.Cover) == new ArtworkPixelSize(200, 100) &&
                NativeArtworkDecoder.SizeFor(new(400, 200), new(100, 100), ImageFit.Contain) == new ArtworkPixelSize(100, 50),
                "cover and contain retain source proportions at the required target resolution");
            await NativeArtworkStreamingAsync();
            var diagnostics = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
            Directory.CreateDirectory(diagnostics);
            using var output = File.Create(Path.Combine(diagnostics, "artwork-decode-result.json"));
            using var writer = new System.Text.Json.Utf8JsonWriter(output);
            writer.WriteStartObject(); writer.WriteNumber("pid", Environment.ProcessId); writer.WriteBoolean("passed", true);
            writer.WriteNumber("rasterizationScale", XamlRoot.RasterizationScale);
            writer.WritePropertyName("before"); System.Text.Json.JsonSerializer.Serialize(writer, before, NativeArtworkJsonContext.Default.NativeArtworkStatistics);
            writer.WritePropertyName("after"); NativeArtworkCounters.JsonSnapshot().WriteTo(writer);
            writer.WriteStartArray("checks"); foreach (var check in checks.Skip(checkStart)) writer.WriteStringValue(check); writer.WriteEndArray();
            writer.WriteEndObject(); writer.Flush();
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Artwork fixture: requested={image.Width}, actual={image.ActualWidth}x{image.ActualHeight}, zoom={zoom.InterfaceScale}, decoded={demand.LastDecode?.Decoded}, completed={complete}, backgroundLoaded={background?.IsLoaded}, backgroundSize={background?.ActualWidth}x{background?.ActualHeight}, backgroundVisibility={background?.Visibility}, backgroundRoot={background?.XamlRoot is not null}, requests={NativeArtworkCounters.Snapshot().Requests - before.Requests}.", error);
        }
        finally
        {
            active = false; lifetime.Cancel(); delay?.TrySetResult();
            await Task.WhenAll(tasks); host.Children.Remove(zoom);
        }
    }

    private async Task NativeArtworkStreamingAsync()
    {
        using var exact = new MemoryStream(new byte[ProtocolConstants.MaximumEncodedArtworkBytes], writable: false);
        using var exactInput = exact.AsInputStream();
        using (var read = await NativeArtworkStream.ReadBoundedAsync(exactInput, CancellationToken.None))
            Check(read.Size == ProtocolConstants.MaximumEncodedArtworkBytes, "exact-bound artwork stream is admitted completely");
        using var oversized = new MemoryStream(new byte[ProtocolConstants.MaximumEncodedArtworkBytes + 65536], writable: false);
        using var oversizedInput = oversized.AsInputStream();
        var rejected = false;
        try { using var read = await NativeArtworkStream.ReadBoundedAsync(oversizedInput, CancellationToken.None); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && oversized.Position == ProtocolConstants.MaximumEncodedArtworkBytes + 1,
            "unknown-length native response stops at the encoded bound plus one byte");
        using var paused = new PausedArtworkStream();
        using var pausedInput = paused.AsInputStream();
        using var cancellation = new CancellationTokenSource();
        var pending = NativeArtworkStream.ReadBoundedAsync(pausedInput, cancellation.Token);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!pending.IsCompleted, "native source request remains incomplete while bytes are pending");
        cancellation.Cancel();
        try { using var read = await pending.WaitAsync(TimeSpan.FromSeconds(5)); throw new InvalidOperationException("Cancelled artwork completed."); }
        catch (OperationCanceledException) { Check(true, "cancellation retires an unfinished native artwork read"); }
        finally { paused.Release.TrySetResult(); }
        Check(NativeArtworkCounters.JsonSnapshot().GetProperty("Requests").ValueKind == System.Text.Json.JsonValueKind.Number,
            "artwork diagnostics use generated trim-safe JSON metadata");
    }

    private sealed class PausedArtworkStream : Stream
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<byte[]> ArtworkPngAsync(int width, int height)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var pixels = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; ++y)
            for (var x = 0; x < width; ++x)
            {
                var offset = (y * width + x) * 4;
                pixels[offset] = (byte)(x % 256); pixels[offset + 1] = (byte)(y % 256);
                pixels[offset + 2] = (byte)(((x / 16 + y / 16) % 2) * 255); pixels[offset + 3] = 255;
            }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync(); stream.Seek(0);
        var bytes = new byte[checked((int)stream.Size)];
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }
}
