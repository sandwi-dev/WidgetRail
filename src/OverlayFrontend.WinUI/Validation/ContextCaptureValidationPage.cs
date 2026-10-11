using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Previews;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Captures a disposable external red/green fixture supplied by the test runner.</summary>
internal sealed partial class ContextCaptureValidationPage : Page
{
    internal ContextCaptureValidationPage(string resultPath)
    {
        Content = new TextBlock { Text = "Validating owned screenshot and silent video capture…" };
        Loaded += async (_, _) =>
        {
            // Let the validation window finish its own initial activation before
            // transferring foreground to the independently rendered source.
            await Task.Delay(250);
            var checks = new List<string>(); string? error = null;
            var directory = Path.GetDirectoryName(Path.GetFullPath(resultPath))!;
            Directory.CreateDirectory(directory);
            try
            {
                var argument = Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--capture-fixture-window");
                if (!long.TryParse(argument, out var value) || value == 0)
                    throw new InvalidOperationException("Provide an external red/green capture fixture window.");
                var handle = (nint)value;
                var identity = new NativePreviewTarget { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativePreviewTarget>(), Version = 1 };
                System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(PreviewNative.ReadIdentity((ulong)handle, ref identity));
                if (identity.ProcessId == (uint)Environment.ProcessId) throw new InvalidOperationException("Fixture must be outside the overlay process.");
                var request = new HostWindowCapture(Guid.NewGuid().ToString("N"), new("fixture.capture", "fixture", "fixture"),
                    WindowCaptureKind.Screenshot, Path.Combine(directory, "synthetic.png"));
                using var indicator = new CaptureIndicator(null, AppearanceSettings.Default, false);
                if (!Environment.GetCommandLineArgs().Contains("--capture-no-indicator")) indicator.Show("Synthetic capture test");
                WinUIEx.HwndExtensions.SetForegroundWindow(handle);
                await Task.Delay(60);
                await File.WriteAllTextAsync(resultPath + ".ready", handle.ToString());
                var foregroundDeadline = Environment.TickCount64 + 20000;
                while (GetForegroundWindow() != handle)
                {
                    if (Environment.TickCount64 >= foregroundDeadline) throw new TimeoutException("Owned capture source needs actual foreground.");
                    await Task.Delay(20);
                }
                var affinityRead = GetWindowDisplayAffinity(handle, out var affinity);
                checks.Add($"Target={handle}, foreground={GetForegroundWindow()}, affinityRead={affinityRead}, affinity={affinity}, error={System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var image = await NativeWindowCapture.RunAsync(request, deadline.Token);
                Check(image.Width > 0 && image.Height > 0 && image.Frames == 1, "Owned WGC screenshot produces bounded dimensions and one frame");
                using (var stream = await (await StorageFile.GetFileFromPathAsync(request.OutputPath)).OpenReadAsync())
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    var data = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
                        ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
                    var left = (int)((decoder.PixelHeight / 2 * decoder.PixelWidth + decoder.PixelWidth / 4) * 4);
                    var right = (int)((decoder.PixelHeight / 2 * decoder.PixelWidth + decoder.PixelWidth * 3 / 4) * 4);
                    Check(data[left + 2] > 180 && data[left + 1] < 80 && data[right + 1] > 180 && data[right + 2] < 80,
                        "PNG contains the foreground window's red and green content with correct orientation");
                }
                indicator.Show("Recording synthetic window · no audio");
                var videoRequest = request with { Kind = WindowCaptureKind.Video, OutputPath = Path.Combine(directory, "synthetic.mp4") };
                var video = await NativeWindowCapture.RunAsync(videoRequest, deadline.Token);
                var properties = await (await StorageFile.GetFileFromPathAsync(videoRequest.OutputPath)).Properties.GetVideoPropertiesAsync();
                Check(video.Frames is > 0 and <= 75 && Math.Abs(properties.Duration.TotalSeconds - video.DurationSeconds) < .15 && video.DurationSeconds <= 5 && video.DurationSeconds > 4.5,
                    "Legacy MP4 retains its five-second duration with bounded frame delivery");
                Check(properties.Width == video.Width && properties.Height == video.Height, "MP4 dimensions match the bounded capture");
                var original = await NativeWindowCapture.RunAsync(request with { OutputPath = Path.Combine(directory, "original.png"),
                    Options = new() { Screenshot = new(), Preparation = new(0) } }, deadline.Token);
                Check(original.Properties is { } originalProperties && original.Width == originalProperties.SourceWidth && original.Height == originalProperties.SourceHeight,
                    "Configured original screenshot keeps exact source pixel dimensions");
                var small = await NativeWindowCapture.RunAsync(request with { OutputPath = Path.Combine(directory, "small.png"),
                    Options = new() { Screenshot = new(320, 240), IncludeCursor = true } }, deadline.Token);
                checks.Add($"Source dimensions: original {original.Properties!.SourceWidth}x{original.Properties.SourceHeight}, small {small.Properties!.SourceWidth}x{small.Properties.SourceHeight}; output {small.Width}x{small.Height}.");
                Check(small.Width <= 320 && small.Height <= 240 && small.Properties?.SourceWidth == original.Properties!.SourceWidth,
                    "Configured bounded screenshot fits without upscaling and retains source metadata");
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                var cpuBefore = process.TotalProcessorTime; var elapsed = System.Diagnostics.Stopwatch.StartNew();
                var configuredRequest = videoRequest with { OutputPath = Path.Combine(directory, "configured.mp4"),
                    Options = new() { Recording = new(640, 360, 15, 1_000_000, 2), Preparation = new(0) } };
                var configured = await NativeWindowCapture.RunAsync(configuredRequest, deadline.Token);
                process.Refresh(); elapsed.Stop();
                checks.Add($"Synthetic 640x360/15 software capture: {elapsed.Elapsed.TotalMilliseconds:0} ms elapsed, {(process.TotalProcessorTime-cpuBefore).TotalMilliseconds:0} ms process CPU; not game-load overhead.");
                Check(configured.Properties is { Encoder: CaptureEncoderMode.Software, RequestedFramesPerSecond: 15, Audio: CaptureAudioMode.None } &&
                    configured.Width <= 640 && configured.Height <= 360 && configured.DurationSeconds is > 1.5 and <= 2,
                    "Configured video reports actual software encoding, dimensions and requested duration");
                using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(1200));
                var stoppedRequest = configuredRequest with { OutputPath = Path.Combine(directory, "stopped.mp4"),
                    Options = new() { Recording = new(640, 360, 15, 1_000_000, 10, StopManually: true) } };
                var stopped = await NativeWindowCapture.RunAsync(stoppedRequest, deadline.Token, stop.Token);
                var stoppedFile = await StorageFile.GetFileFromPathAsync(stoppedRequest.OutputPath);
                var stoppedProperties = await stoppedFile.Properties.GetVideoPropertiesAsync();
                Check(stopped.Frames > 0 && stopped.DurationSeconds < 3 && Math.Abs(stoppedProperties.Duration.TotalSeconds - stopped.DurationSeconds) < .2,
                    "Stop-and-keep finalizes a playable short MP4 instead of discarding it");
                using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                var cancelled = false;
                try { await NativeWindowCapture.RunAsync(videoRequest with { OutputPath = Path.Combine(directory, "cancelled.mp4") }, cancel.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "Cancellation stops an in-progress native recording");
                var again = await NativeWindowCapture.RunAsync(request with { OutputPath = Path.Combine(directory, "after-cancel.png") }, deadline.Token);
                Check(again.Frames == 1, "Native capture releases its owner and remains usable after cancellation");
                indicator.Dispose();
                using var preview = new MediaPlayerView();
                Content = preview;
                await preview.ValidateCapturePlaybackAsync(request.OutputPath, videoRequest.OutputPath, checks);

            }
            catch (Exception failure) { error = failure.ToString(); }
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = error is null, checks, error }));
            void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); checks.Add(message); }
        };
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [System.Runtime.InteropServices.LibraryImport("user32.dll", SetLastError = true)] private static partial int GetWindowDisplayAffinity(nint window, out uint affinity);


}
