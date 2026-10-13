using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation
{
    internal sealed partial class CaptureDiscoveryValidationPage : Page
    {
        internal CaptureDiscoveryValidationPage(string result, bool full)
        {
            Content = new TextBlock { Text = "Checking recording encoder discovery; no screen recording." };
            Loaded += async (_, _) =>
            {
                try { await File.WriteAllTextAsync(result, JsonSerializer.Serialize(await NativeWindowCapture.MeasureDiscoveryAsync(full))); }
                catch (Exception error) { await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); }
            };
        }
    }
}
namespace WidgetRail.OverlayFrontend.WinUI.Capture
{
    internal static partial class NativeWindowCapture
    {
        internal static async Task<object> MeasureDiscoveryAsync(bool full)
        {
            var probes = 0; var audioProbes = 0;
            var detector = new CaptureEncoderDiscovery(() => Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported(),
                (profile, bitrate, audio) => { Interlocked.Increment(ref probes); return ProbeProfile(profile, bitrate, audio); },
                () => { Interlocked.Increment(ref audioProbes); return ProbeApplicationAudio(); });
            var watch = Stopwatch.StartNew();
            var result = await detector.DiscoverAsync(full ? null : new CaptureRecordingQuery());
            var coldMs = watch.Elapsed.TotalMilliseconds; var coldProbes = probes; var coldAudioProbes = audioProbes;
            watch.Restart();
            await detector.DiscoverAsync(full ? null : new CaptureRecordingQuery());
            var warmMs = watch.Elapsed.TotalMilliseconds;
            return new { passed = result.IsWellFormed() && probes == coldProbes && audioProbes == coldAudioProbes,
                full, coldMs, warmMs, coldProbes, coldAudioProbes, profiles = result.RecordingProfiles.Select(p => p.Id).ToArray() };
        }
    }
}
