using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Explicit fixture-only entry point. Uses the production bridge, selection,
    // retention and lifecycle methods; it does not change readiness policy.
    internal void EnableSwitchValidation(string resultPath)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            var observations = new List<object>();
            var frames = 0;
            string? invariantFailure = null;
            void Observe(object? sender, object args)
            {
                ++frames;
                if (visible && activeWidget is not null && (surface is null || surface.Visibility != Visibility.Visible || surface.Opacity != 1))
                    invariantFailure ??= "Committed content became hidden before replacement.";
                if (WidgetSurfaces.Children.Count > RetainedSurfaceLimit) invariantFailure ??= "Native surface budget exceeded.";
            }
            try
            {
                if (startup is not null) await startup;
                Check(activeWidget == "audio-mixer" && !switching, "Initial fixture commits");
                CompositionTarget.Rendering += Observe;
                var original = surface!;
                var extent = new SurfaceExtent(WidgetSurface.Width, WidgetSurface.Height);
                var before = await CornerPixels(original);
                var slow = SelectAsync("wide-peer", false);
                await Task.Delay(250);
                Check(switching && activeWidget == "audio-mixer" && requestedWidget == "wide-peer", "Delayed request retains displayed identity");
                Check(ReferenceEquals(original, surface) && original.Visibility == Visibility.Visible && original.Opacity == 1,
                    "Delayed request retains outgoing native content");
                Check(extent == new SurfaceExtent(WidgetSurface.Width, WidgetSurface.Height), "Delayed request retains committed extent");
                Check(!original.IsInteractionCurrent(retainedSurfaces["audio-mixer"].Frame!.Authority), "Outgoing input authority is denied during preparation");
                var during = await CornerPixels(original);
                Check(before.SequenceEqual(during), "Outgoing raster pixels survive delayed preparation");
                await slow;
                Check(activeWidget == "wide-peer" && !switching && surface!.ActualWidth > original.ActualWidth,
                    "Cold incoming content and extent commit together");
                Check(preparingSurface is null && surface!.Opacity == 1, "Preparation slot retires after commit");
                await SelectAsync("audio-mixer", false);
                Check(ReferenceEquals(original, surface), "Cached selection reuses native content");
                var superseded = SelectAsync("now-playing", false);
                await Task.Delay(80);
                var reversed = SelectAsync("wide-peer", false);
                var latest = SelectAsync("audio-mixer", false);
                await Task.WhenAll(superseded, reversed, latest);
                Check(activeWidget == "audio-mixer" && !switching && preparingSurface is null,
                    "Rapid reversal commits only latest selection and drains preparation");
                await SelectAsync("games-apps", false);
                await SelectAsync("wide-peer", false);
                await SelectAsync("now-playing", false);
                Check(!retainedSurfaces.ContainsKey("audio-mixer"), "Cache pressure evicts old native surface");
                await SelectAsync("audio-mixer", false);
                Check(activeWidget == "audio-mixer" && !ReferenceEquals(original, surface), "Evicted widget recreates and commits");
                await SelectAsync("settings", false);
                Check(activeWidget == "settings" && !switching, "Intentionally empty valid SDK tree is ready");
                await SelectAsync("network-controls", false);
                Check(activeWidget == "network-controls" && !switching, "Loading content is ready without remote-data wait");
                var retained = surface;
                await SelectAsync("spotify", false);
                Check(activeWidget == "network-controls" && ReferenceEquals(retained, surface) && RecoveryVisible,
                    "Failed incoming widget preserves displayed content and exposes recovery");
                await SelectAsync("wide-peer", false);
                Check(activeWidget == "wide-peer" && !RecoveryVisible, "Selection recovers after failed preparation");
                var hidden = SelectAsync("audio-mixer", false);
                SetVisible(false);
                await hidden;
                Check(!switching && preparingSurface is null, "Hide cancels pending preparation");
                SetVisible(true);
                await Until(() => visible && !switching && activeWidget == "audio-mixer");
                Check(surface!.Visibility == Visibility.Visible && surface.Opacity == 1, "Reopen establishes the latest requested widget");
                await SelectAsync("audio-mixer", true);
                var livePresenter = surface;
                var liveFrame = retainedSurfaces["audio-mixer"].Frame!;
                await InvokeAsync(new(liveFrame, new("fixture.ready", "audio-ready", InputScopeId: liveFrame.Authority.ActiveInputScopeId)));
                await Until(() => retainedSurfaces["audio-mixer"].Frame!.Authority.SnapshotSequence > liveFrame.Authority.SnapshotSequence);
                Check(ReferenceEquals(livePresenter, surface) && !switching, "Dynamic publication preserves the committed presenter");
                var catalogPath = Path.Combine(options.InstallationRoot, "widget-catalog.json");
                var catalog = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(catalogPath))!;
                var entry = catalog["widgets"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "audio-mixer")!;
                entry["instanceId"] = "audio-mixer.replacement";
                File.WriteAllText(catalogPath, catalog.ToJsonString());
                await Until(() => !switching && retainedSurfaces.TryGetValue("audio-mixer", out var value) &&
                    value.Descriptor.InstanceId == "audio-mixer.replacement");
                Check(activeWidget == "audio-mixer" && !ReferenceEquals(livePresenter, surface), "Same-ID incarnation stages a replacement before retiring old content");
                Check(invariantFailure is null, invariantFailure ?? "Every sampled native frame retained a committed surface within budget");
                observations.Add(new { frames, nativeSurfaces = WidgetSurfaces.Children.Count, activeWidget });
                Write(new { passed = true, checks, observations });
            }
            catch (Exception error) { Write(new { passed = false, checks, observations, error = error.ToString() }); }
            finally { CompositionTarget.Rendering -= Observe; }

            void Check(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException(name);
                checks.Add(name);
            }
            void Write(object result)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
                File.WriteAllText(resultPath, JsonSerializer.Serialize(new { pid = Environment.ProcessId, result }, new JsonSerializerOptions { WriteIndented = true }));
            }
        };
    }

    private static async Task<byte[]> CornerPixels(WidgetViewPresenter presenter)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(presenter);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        if (bitmap.PixelWidth < 16 || bitmap.PixelHeight < 16) throw new InvalidOperationException("No native raster was produced.");
        var offset = ((bitmap.PixelHeight - 12) * bitmap.PixelWidth + bitmap.PixelWidth - 12) * 4;
        return pixels.AsSpan(offset, 16).ToArray();
    }

    private static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition()) await Task.Delay(20, deadline.Token);
    }
}
