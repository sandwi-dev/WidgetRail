using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
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
                var originalFailureOwner = CapturePresentationFailureGuard();
                Check(originalFailureOwner(), "Current presentation can report its own operation failure");
                CompositionTarget.Rendering += Observe;
                // Acknowledgment of unrelated outgoing cleanup must not keep an
                // already-published incoming widget inert. The worker barrier is
                // bounded below its transport deadline and always released here.
                var barrier = Path.Combine(options.InstallationRoot, "audio-mixer.deactivation");
                var actionPath = Path.Combine(options.InstallationRoot, "games-apps.actions");
                File.WriteAllText(barrier + ".arm", "interactive-switch");
                var draining = SelectAsync("games-apps", true);
                bool admittedWhileDraining;
                bool actionWhileDraining;
                bool navigatedWhileDraining;
                try
                {
                    await Until(() => File.Exists(barrier + ".entered"));
                    var incomingFrame = retainedSurfaces["games-apps"].Frame!;
                    admittedWhileDraining = activeWidget == "games-apps" && !switching &&
                        surface!.IsInteractionCurrent(incomingFrame.Authority) && !draining.IsCompleted;
                    var action = InvokeAsync(new(incomingFrame,
                        new("fixture.ready", "games-ready", InputScopeId: incomingFrame.Authority.ActiveInputScopeId)));
                    await Task.WhenAny(action, Task.Delay(650));
                    actionWhileDraining = action.IsCompletedSuccessfully && File.Exists(actionPath) &&
                        !File.Exists(barrier + ".completed") && !File.Exists(barrier + ".timed-out");
                    surface!.MoveFocus(FocusNavigationDirection.Down);
                    await Task.Delay(30);
                    navigatedWhileDraining = FocusId() == "Widget.games-more";
                    observations.Add(new { phase = "outgoing-deactivation", activeWidget, switching, visible,
                        foreground, interactive, admittedWhileDraining, actionWhileDraining, focus = FocusId() });
                }
                finally { File.WriteAllText(barrier + ".release", "interactive-switch"); await draining; }
                Check(admittedWhileDraining, "Committed incoming widget admits input while outgoing cleanup is pending");
                Check(!originalFailureOwner(), "A late failure from the previous widget cannot replace the incoming presentation");
                Check(actionWhileDraining, "Incoming action reaches its worker before unrelated outgoing cleanup completes");
                Check(File.Exists(barrier + ".completed") && !File.Exists(barrier + ".timed-out"),
                    "Outgoing cleanup completes under the existing bounded lifecycle serializer");
                Check(navigatedWhileDraining && FocusId() == "Widget.games-more",
                    "Incoming navigation survives outgoing cleanup without restoring earlier focus");
                var beforeHideFailureOwner = CapturePresentationFailureGuard();
                SetVisible(false);
                Check(!beforeHideFailureOwner(), "Hidden presentation cannot publish late operation failure");
                await Until(() => surface is { IsPresentationActive: false });
                Check(interactive, "Hiding a focused widget does not interpret native tray fallback as a user domain transfer");
                SetVisible(true);
                await Until(() => visible && !switching && FocusId() == "Widget.games-more");
                Check(!beforeHideFailureOwner(), "Reopen cannot revive a prior visible session's operation failure");
                Check(interactive, "Reopening restores the widget's remembered item rather than entering the tray");
                var down = WidgetRail.OverlayPlatformClient.ControllerFrame.Create();
                down.Connected = 1;
                down.DpadNavigation = new()
                {
                    Direction = WidgetRail.OverlayPlatformClient.NavigationDirection.Down,
                    Phase = WidgetRail.OverlayPlatformClient.NavigationPhase.Pressed,
                };
                foreach (var switcher in new[] { WidgetRail.PlatformSettings.WidgetSwitcherLayout.Rail, WidgetRail.PlatformSettings.WidgetSwitcherLayout.Radial })
                {
                    Appearance = Appearance with { WidgetSwitcher = switcher };
                    Receive(down);
                    await Until(() => !interactive && FocusedTrayWidget()?.Id == "games-apps");
                    Check(!RadialOpen && Tray.IsEnabled, switcher + " Controller Down at root bottom enters rail");
                    await SelectAsync("games-apps", true);
                    await RouteButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.B, WidgetRail.WidgetProtocol.ControllerEventPhase.Pressed);
                    await RouteButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.B, WidgetRail.WidgetProtocol.ControllerEventPhase.Released);
                    await Until(() => !interactive && FocusedTrayWidget()?.Id == "games-apps");
                    Check(RadialOpen == (switcher == WidgetRail.PlatformSettings.WidgetSwitcherLayout.Radial),
                        switcher + " root Back honors the configured switcher independently of directional entry");
                    var beforeAction = File.ReadAllText(actionPath);
                    Check(trayGuide.DisplayedHints.Any(hint => hint.Button == WidgetRail.WidgetProtocol.ControllerButton.X),
                        switcher + " guide includes current-view dashboard shortcut");
                    await RouteButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.X, WidgetRail.WidgetProtocol.ControllerEventPhase.Pressed,
                        WidgetRail.WidgetSdk.ControllerInputOrigin.AccessibilityAutomation);
                    await RouteButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.X, WidgetRail.WidgetProtocol.ControllerEventPhase.Released,
                        WidgetRail.WidgetSdk.ControllerInputOrigin.AccessibilityAutomation);
                    await Until(() => File.ReadAllText(actionPath) != beforeAction);
                    Check(!interactive, switcher + " dispatches dashboard input once without entering the widget");
                    await SelectAsync("games-apps", true);
                }
                Appearance = Appearance with { WidgetSwitcher = WidgetRail.PlatformSettings.WidgetSwitcherLayout.Rail };
                await SelectAsync("audio-mixer", true);
                File.WriteAllText(barrier + ".arm", "superseded-switch");
                var supersededCleanup = SelectAsync("games-apps", true);
                Task? supersedingSelection = null;
                bool revokedOnSupersession;
                bool revokedOnHide;
                try
                {
                    await Until(() => File.ReadAllText(barrier + ".entered") == "superseded-switch");
                    // Keep wide-peer cold for the delayed-publication checks below.
                    supersedingSelection = SelectAsync("settings", false);
                    revokedOnSupersession = !surface!.IsInteractionCurrent(retainedSurfaces["games-apps"].Frame!.Authority);
                    SetVisible(false);
                    revokedOnHide = !surface.IsHitTestVisible;
                }
                finally
                {
                    File.WriteAllText(barrier + ".release", "superseded-switch");
                    await supersededCleanup;
                    if (supersedingSelection is not null) await supersedingSelection;
                }
                Check(revokedOnSupersession && revokedOnHide && !visible && !switching,
                    "Supersession and hide revoke the committed page while old cleanup drains");
                SetVisible(true);
                await Until(() => !switching && activeWidget == "settings");
                Check(surface!.IsHitTestVisible, "Reopen prepares the latest intent after interrupted cleanup");
                await SelectAsync("audio-mixer", true);
                var original = surface!;
                if (original.WidgetResizeCompletion is { } reveal)
                    await reveal.WaitAsync(TimeSpan.FromSeconds(3));
                var extent = new SurfaceExtent(WidgetSurface.Width, WidgetSurface.Height);
                var before = await CornerPixels(original);
                Check(before[3] > 240, "Committed widget raster is opaque, not two matching blank captures");
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
                Check(surface!.WidgetResizeCompletion is not null, "Different widget extents create a resize transition");
                Check(await surface.WidgetResizeCompletion!.WaitAsync(TimeSpan.FromSeconds(3)) == Motion.WidgetMotionOutcome.Completed &&
                    Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(surface).Scale == System.Numerics.Vector3.One &&
                    Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(WidgetSurface).Scale == System.Numerics.Vector3.One,
                    "Production publication completes widget and shell-fill resizing without layout cancellation");
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
                int.TryParse(FrontendArguments.Value(Environment.GetCommandLineArgs(), "--switch-soak-cycles"), out var cycles);
                int.TryParse(FrontendArguments.Value(Environment.GetCommandLineArgs(), "--switch-soak-seconds"), out var seconds);
                if (cycles > 0 || seconds > 0)
                {
                    if (cycles is < 0 or > 1000 || seconds is < 0 or > 1800) throw new ArgumentOutOfRangeException(nameof(cycles));
                    var samples = new List<object>();
                    var targets = new[] { "audio-mixer", "games-apps", "wide-peer", "now-playing" };
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    // More widgets than the native cache can retain exercises
                    // retirement/recreation, not only warmed visibility toggles.
                    var completedCycles = 0;
                    for (var cycle = 0; cycle < cycles || elapsed.Elapsed.TotalSeconds < seconds; ++cycle)
                    {
                        var target = targets[cycle % targets.Length];
                        await SelectAsync(target, true);
                        await Until(() => !switching && activeWidget == target && surface is { IsPresentationActive: true });
                        if (cycle % 4 == 3)
                        {
                            SetVisible(false);
                            await Until(() => surface is { IsPresentationActive: false });
                            SetVisible(true);
                            await Until(() => surface is { IsPresentationActive: true } && !switching);
                        }
                        await Task.Delay(100);
                        if (invariantFailure is not null) throw new InvalidOperationException(invariantFailure);
                        if (retainedSurfaces.Count > RetainedSurfaceLimit || preparingSurface is not null || RecoveryVisible)
                            throw new InvalidOperationException("Switch soak retained preparation, exceeded the cache bound, or entered recovery.");
                        completedCycles = cycle + 1;
                        if (cycle % 20 == 19 || cycle + 1 >= cycles && elapsed.Elapsed.TotalSeconds >= seconds)
                        {
                            using var process = System.Diagnostics.Process.GetCurrentProcess();
                            samples.Add(new { cycle = cycle + 1, elapsedSeconds = elapsed.Elapsed.TotalSeconds,
                                process.PrivateMemorySize64, process.WorkingSet64, process.HandleCount,
                                managedBytes = GC.GetTotalMemory(false), nativeSurfaces = WidgetSurfaces.Children.Count,
                                retained = retainedSurfaces.Count });
                        }
                    }
                    Check(true, $"{completedCycles} synthetic switches and {completedCycles / 4} shell hide/reopen cycles retain bounded native surfaces without recovery");
                    observations.Add(new { scenario = "synthetic-switch-soak", completedCycles, elapsedSeconds = elapsed.Elapsed.TotalSeconds, samples,
                        scope = "frontend process only; no real provider, artwork grid, WebView, controller or GPU-memory qualification" });
                }
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
            string? FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused
                ? AutomationProperties.GetAutomationId(focused) : null;
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
