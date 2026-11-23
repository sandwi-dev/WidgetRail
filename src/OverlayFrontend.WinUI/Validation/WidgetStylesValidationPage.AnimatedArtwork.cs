using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task AnimatedArtworkProbeAsync(string output)
    {
        var colors=new List<string>();
        Exception? failure=null;
        var artwork=new WidgetArtworkView{Width=96,Height=96};
        BitmapImage? bitmap=null;
        try
        {
            await AnimatedArtworkBoundsAsync();
            artwork.ApplyStyle(new(Stretch.Fill));
            host.Children.Add(artwork);
            var decoded=await NativeArtworkDecoder.DecodeAsync(new(TwoFrameGif()),new(96,96),ImageFit.Fill,CancellationToken.None);
            bitmap=decoded?.Image??throw new InvalidOperationException("GIF did not decode.");
            artwork.Source=bitmap;
            await Wait(()=>artwork.IsLoaded&&artwork.ActualWidth>0&&bitmap.IsAnimatedBitmap);
            Check(bitmap.IsAnimatedBitmap,"Native artwork preserves the GIF animation frames");
            bitmap.Play();
            for(var i=0;i<12;i++){await Task.Delay(110);colors.Add(await CenterColor(artwork));}
            Check(bitmap.IsPlaying,"WinUI reports active animation playback");
            Check(colors.Contains("0000FFFF")&&colors.Contains("FF0000FF"),"The production ImageBrush artwork surface displays both GIF frames");
            bitmap.Stop();
            await Task.Delay(250);
            var stopped=await CenterColor(artwork);
            await Task.Delay(450);
            Check(!bitmap.IsPlaying&&await CenterColor(artwork)==stopped,"Native Stop prevents further image-frame changes");
            // Subsequent lifetime probes own their own viewport. Keeping this
            // first specimen stacked above them can push them below the small
            // validation window, where correct visibility policy pauses them.
            host.Children.Remove(artwork);
            await AnimatedArtworkLifetimeAsync();
            await DelayedArtworkAnimationAsync();
            await AnimatedImageViewerAsync();
            await RetainedArtworkAuthorityAsync();
        }
        catch(Exception error){failure=error;}
        finally
        {
            bitmap?.Stop();artwork.Source=null;host.Children.Remove(artwork);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=failure is null,checks,colors,error=failure?.ToString()}));
        }
        if(failure is not null)throw failure;
    }

    private async Task AnimatedArtworkBoundsAsync()
    {
        var canvas = TwoFrameGif(); canvas[6] = 2; canvas[8] = 3;
        var bounded = await NativeArtworkDecoder.DecodeAsync(new(canvas), new(96, 96), ImageFit.Contain,
            CancellationToken.None, autoPlay: false);
        Check(bounded?.Source == new ArtworkPixelSize(2, 3) && bounded.Decoded == bounded.Source,
            "GIF sizing uses its logical canvas rather than its smaller first delta frame");
        foreach (var laterFrame in new[] { false, true })
        {
            var bytes = TwoFrameGif();
            var widthOffset = laterFrame ? Array.LastIndexOf(bytes, (byte)44) + 5 : 6;
            bytes[widthOffset] = 255; bytes[widthOffset + 1] = 255;
            var before = NativeArtworkCounters.Snapshot().Decodes;
            var rejected = false;
            try { await NativeArtworkDecoder.DecodeAsync(new(bytes), new(96, 96), ImageFit.Fill, CancellationToken.None); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && NativeArtworkCounters.Snapshot().Decodes == before,
                laterFrame ? "Oversized later GIF frames are rejected before native pixel decoding" :
                    "Oversized GIF composition canvases are rejected before native pixel decoding");
        }
    }

    private async Task AnimatedArtworkLifetimeAsync()
    {
        var artwork = new WidgetArtworkView { Width = 96, Height = 96 };
        artwork.ApplyStyle(new(Stretch.Fill));
        var stack = new StackPanel(); stack.Children.Add(artwork); stack.Children.Add(new Border { Height = 400 });
        var scroll = new ScrollViewer { Width = 120, Height = 100, Content = stack };
        host.Children.Add(scroll);
        var active = true; var animations = true; var resolutions = 0;
        Exception? failure = null;
        using var lifetime = new CancellationTokenSource();
        var work = new List<Task>();
        using var demand = new NativeArtworkDemand(artwork, ImageFit.Fill, () => active,
            _ => { resolutions++; return Task.FromResult<NativeArtworkPayload?>(new(TwoFrameGif())); },
            image => artwork.Source = image, _ => { }, work.Add, error => failure = error,
            lifetime.Token, animationAllowed: () => animations);
        try
        {
            await Wait(() => failure is not null || demand.LastDecode?.Image.IsPlaying == true);
            if (failure is not null) throw failure;
            var bitmap = demand.LastDecode!.Image;
            Check(bitmap.IsAnimatedBitmap && resolutions == 1, "Visible admitted GIF starts through its artwork demand");
            animations = false; demand.RefreshPlayback();
            Check(!bitmap.IsPlaying, "Reduced-motion policy stops an existing animated bitmap immediately");
            animations = true; demand.RefreshPlayback();
            Check(bitmap.IsPlaying, "Restoring motion resumes existing pixels without another decode");
            active = false; demand.RefreshPlayback();
            Check(!bitmap.IsPlaying, "Presentation suspension pauses retained GIF artwork");
            active = true; demand.RefreshPlayback();
            Check(bitmap.IsPlaying, "Presentation resume reuses the animated bitmap");
            demand.SetPlaybackSuspended(true);
            Check(!bitmap.IsPlaying, "A cached page retains pixels without retaining animation playback");
            demand.SetPlaybackSuspended(false);
            scroll.ChangeView(null, 180, null, true);
            await Wait(() => scroll.VerticalOffset >= 179 && !bitmap.IsPlaying);
            Check(true, "Native effective viewport stops GIF playback when scrolled offscreen");
            scroll.ChangeView(null, 0, null, true);
            await Wait(() => scroll.VerticalOffset <= 1 && bitmap.IsPlaying);
            Check(resolutions == 1, "Reentering the viewport resumes without resolving or decoding again");
            lifetime.Cancel();
            Check(!bitmap.IsPlaying && demand.LastDecode is null, "Demand retirement stops animation and releases its decoded reference");
        }
        finally
        {
            lifetime.Cancel(); host.Children.Remove(scroll); await Task.WhenAll(work);
        }
    }

    private async Task DelayedArtworkAnimationAsync()
    {
        var artwork = new WidgetArtworkView { Width = 96, Height = 96 };
        artwork.ApplyStyle(new(Stretch.Fill));
        var scroll = new ScrollViewer { Width = 120, Height = 120, Content = artwork };
        host.Children.Add(scroll);
        try
        {
            await Wait(() => artwork.IsLoaded && artwork.ActualWidth > 0);
            // Start after the viewport has settled. Each new source owner must
            // observe the same native viewport without a scroll/resize nudge.
            await Task.Delay(150);
            var paintedChildren = artwork.Children.ToArray();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var source = new TaskCompletionSource<NativeArtworkPayload?>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var lifetime = new CancellationTokenSource();
                var pending = new List<Task>();
                Exception? failure = null;
                var reads = 0;
                using var demand = new NativeArtworkDemand(artwork, ImageFit.Fill, () => true,
                    async token => { reads++; return await source.Task.WaitAsync(token); },
                    value => artwork.Source = value, _ => { }, pending.Add, error => failure = error, lifetime.Token);
                try
                {
                    await Wait(() => reads == 1);
                    await Task.Delay(200);
                    Check(demand.LastDecode is null, $"Delayed GIF cycle{cycle} waits for its source without playback");
                    source.SetResult(new(TwoFrameGif()));
                    try { await Wait(() => failure is not null || demand.LastDecode?.Image.IsPlaying == true); }
                    catch (TimeoutException error)
                    { throw new InvalidOperationException($"Delayed GIF cycle{cycle}: loaded={artwork.IsLoaded}, host={artwork.XamlRoot?.IsHostVisible}, size={artwork.ActualWidth}x{artwork.ActualHeight}", error); }
                    if (failure is not null) throw failure;
                    Check(true, $"Delayed GIF cycle{cycle} starts in an unchanged, already arranged viewport");
                    var bitmap = demand.LastDecode!.Image;
                    var observer = artwork.Children.Except(paintedChildren).OfType<Border>().Single();
                    Check(!observer.IsHitTestVisible && !observer.IsTabStop &&
                        AutomationProperties.GetAccessibilityView(observer) == AccessibilityView.Raw &&
                        observer.ActualWidth == artwork.ActualWidth && observer.ActualHeight == artwork.ActualHeight,
                        $"The viewport observer covers the image without adding an input target in cycle{cycle}");
                    lifetime.Cancel();
                    Check(!bitmap.IsPlaying, $"Retiring delayed GIF cycle{cycle} stops its own animation");
                    Check(artwork.Children.SequenceEqual(paintedChildren),
                        $"Retiring cycle{cycle} restores the original image tree");
                }
                finally { lifetime.Cancel(); artwork.Source = null; await Task.WhenAll(pending); }
            }
        }
        finally { host.Children.Remove(scroll); scroll.Content = null; }
    }

    // A generated 1x1 red/blue GIF89a, two 200ms frames, infinite loop. No
    // downloaded media, network source, personal capture or external codec tool.
    private static byte[] TwoFrameGif()=>[
        71,73,70,56,57,97,1,0,1,0,128,0,0,255,0,0,0,0,255,
        33,255,11,78,69,84,83,67,65,80,69,50,46,48,3,1,0,0,0,
        33,249,4,0,20,0,0,0,44,0,0,0,0,1,0,1,0,0,2,2,68,1,0,
        33,249,4,0,20,0,0,0,44,0,0,0,0,1,0,1,0,0,2,2,76,1,0,59];

    private static async Task<string> CenterColor(UIElement element)
    {
        var capture=new RenderTargetBitmap();await capture.RenderAsync(element);
        var pixels=(await capture.GetPixelsAsync()).ToArray();
        var index=(capture.PixelHeight/2*capture.PixelWidth+capture.PixelWidth/2)*4;
        return Convert.ToHexString(pixels.AsSpan(index,4));
    }
}
