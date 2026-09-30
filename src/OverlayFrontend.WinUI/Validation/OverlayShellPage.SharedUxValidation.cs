using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private Exception? validationFailure;
    partial void RecordValidationFailure(Exception error) => validationFailure ??= error;
    // Opt-in real-package regression; section navigation only, no media/game launch.
    internal void EnableSharedUxValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            try
            {
                if (startup is not null) await startup;
                await SelectAsync("widgetrail.samples.playnite-library", true);
                await Until(() => Background()?.ArtworkSource is not null);
                var background = Background()!;
                var unloaded = 0; var blank = false; var samples = 0;
                void Observe(object? sender, object args) { ++samples; blank |= background.ArtworkSource is null || !ReferenceEquals(background, Background()); }
                background.Unloaded += (_, _) => ++unloaded;
                CompositionTarget.Rendering += Observe;
                try
                {
                    foreach (var action in new[] { "playnite-library.browse.open", "playnite-library.home.open", "playnite-library.browse.open" })
                    {
                        var previous = surface!.CurrentBinding!.Scope;
                        Activate(action);
                        await Until(() => surface!.CurrentBinding!.Scope != previous);
                        await Task.Delay(400);
                        Check(ReferenceEquals(background, Background()) && !blank && unloaded == 0,
                            "Production Playnite retains the same painted background across " + action);
                    }
                    Check(samples > 0, "Background retention sampled actual composition frames");
                }
                finally { CompositionTarget.Rendering -= Observe; }

                await CheckCollectionReopen("playnite-library.library.scroll", "Playnite Library");
                await CheckPlayniteDetails();
                Activate("playnite-library.home.open");
                await Until(() => Element("playnite-library.library.grid") is WidgetIndexedCollectionView { IsLoaded: true });
                await CheckCollectionReopen("playnite-library.library.grid", "Playnite Home rail");

                await SelectAsync("widgetrail.samples.sdk-gallery", true);
                Activate("gallery.tab.tiles");
                await Until(() => Element("gallery.media") is Control { IsLoaded: true });
                await CheckReopen("gallery.media", "SDK Gallery Night Drive");

                await SelectAsync("widgetrail.samples.ytmusic", true);
                Check(activeWidget == "widgetrail.samples.ytmusic", "Production YouTube Music is committed before focus checks");
                await Until(() => Nodes(surface!.CurrentBinding!.View.Root).Any(node => node.Kind == ViewNodeKind.IndexedCollection));
                var musicCollection = Nodes(surface!.CurrentBinding!.View.Root).First(node => node.Kind == ViewNodeKind.IndexedCollection).Id;
                await CheckCollectionReopen(musicCollection, "YouTube Music collection");
                await CheckMusicResize();

                await SelectAsync("media-sessions", true);
                Check(activeWidget == "media-sessions", "Production Now Playing is committed before focus checks");
                await Until(() => Nodes(surface!.CurrentBinding!.View.Root).Any(node => node.IsFocusable && node.IsDisabled != true));
                // No transport action is invoked: exercise the real provider's
                // currently available transport/session (or its empty state).
                var mediaFocus = Nodes(surface!.CurrentBinding!.View.Root)
                    .Where(node => node.IsFocusable && node.IsDisabled != true && Element(node.Id) is Control { IsEnabled: true })
                    .OrderByDescending(node => node.Id == "media.play-toggle")
                    .First().Id;
                await CheckReopen(mediaFocus, "Now Playing " + mediaFocus);

                await SelectAsync("settings", true);
                Check(activeWidget == "settings", "Production Settings is committed before focus checks");
                await Until(() => Element("category.accessibility") is Button { IsEnabled: true });
                Activate("open.accessibility");
                await Until(() => Element("motion.reduced") is Control { IsEnabled: true });
                // Focus only; do not change the user's motion preference.
                await CheckReopen("motion.reduced", "Settings noninitial accessibility option");

                // Explicit controller transfer to the tray still persists. The
                // fallback guard must not turn every reopen into widget entry.
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                // Page Back is asynchronous. Observe its published root before
                // issuing the distinct root-to-switcher Back operation.
                await Until(() => !interactive || Element("category.accessibility") is { IsLoaded: true });
                if (interactive)
                {
                    await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                    await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                }
                await Until(() => !interactive && FocusedTrayWidget() is not null);
                SetVisible(false);
                await Until(() => surface is { IsPresentationActive: false });
                SetVisible(true);
                await Until(() => !switching && !interactive && FocusedTrayWidget() is not null);
                Check(true, "Explicit tray ownership survives hide/reopen");
                Check(validationFailure is null, "Actual widget workflows do not transiently enter host recovery");
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }

            async Task CheckPlayniteDetails()
            {
                var focus = FocusId();
                Check(focus.StartsWith("Widget.playnite-library.library.scroll.Item.", StringComparison.Ordinal),
                    "Details workflow starts on the remembered library item, never a Play control");
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed, ControllerInputOrigin.AccessibilityAutomation);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released, ControllerInputOrigin.AccessibilityAutomation);
                await Until(() => Element("playnite-library.details.content") is { IsLoaded: true });
                await Until(() => FocusId().StartsWith("Widget.playnite-library.details.", StringComparison.Ordinal));
                Check(!surface!.CanLeaveRootScope, "Actual Playnite details owns a modal input scope");
                foreach (var tab in new[] { "Achievements", "Activity", "Overview" })
                {
                    await RouteButtonAsync(ControllerButton.RightBumper, ControllerEventPhase.Pressed, ControllerInputOrigin.AccessibilityAutomation);
                    await RouteButtonAsync(ControllerButton.RightBumper, ControllerEventPhase.Released, ControllerInputOrigin.AccessibilityAutomation);
                    await Until(() => Nodes(surface.CurrentBinding!.View.Root).Any(node =>
                        node.Id == "playnite-library.details.tab.content" &&
                        string.Equals(node.Transition?.Key, tab, StringComparison.OrdinalIgnoreCase)));
                    Check(Element("playnite-library.details.content") is { IsLoaded: true },
                        "Actual Playnite " + tab + " tab stays inside its loaded dialog");
                }
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed, ControllerInputOrigin.AccessibilityAutomation);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released, ControllerInputOrigin.AccessibilityAutomation);
                await Until(() => surface.CanLeaveRootScope && FocusId() == focus);
                Check(true, "Closing actual Playnite details restores the same library occurrence");
            }

            async Task CheckMusicResize()
            {
                var original = Appearance;
                try
                {
                    Appearance = Appearance with { Motion = WidgetRail.PlatformSettings.MotionPreference.Full,
                        AnimateWidgetSwitching = false, WidgetAnimationSpeed = .5,
                        Contrast = WidgetRail.PlatformSettings.ContrastPreference.Standard,
                        Transparency = WidgetRail.PlatformSettings.TransparencyPreference.Full };
                    surface!.ApplyAppearance(Appearance, true);
                    await Until(() => surface.WidgetResizeCompletion is null or { IsCompleted: true });
                    var originalExtent = new SurfaceExtent(surface.Width, surface.Height);
                    foreach (var button in new[] { ControllerButton.Y, ControllerButton.B })
                    {
                        var before = new SurfaceExtent(surface.Width, surface.Height);
                        var priorPlayback = surface.WidgetResizeCompletion;
                        var starts = surface.WidgetResizeStarts;
                        await RouteButtonAsync(button, ControllerEventPhase.Pressed);
                        await RouteButtonAsync(button, ControllerEventPhase.Released);
                        await Until(() => surface.Width != before.Width || surface.Height != before.Height);
                        await Until(() => surface.WidgetResizeCompletion is { } playback && !ReferenceEquals(playback, priorPlayback));
                        var playback = surface.WidgetResizeCompletion!;
                        ConfigureProductionViewport(shellViewport);
                        Check(ReferenceEquals(playback, surface.WidgetResizeCompletion),
                            "Unchanged production layout preserves the same resize timeline");
                        Check(await playback == Motion.WidgetMotionOutcome.Completed,
                            "YouTube Music " + button + " resize completes without cancellation");
                        Check(surface.WidgetResizeStarts == starts + 1,
                            "YouTube Music content resize starts exactly one compositor timeline with switching disabled");
                        Check(button == ControllerButton.Y ? surface.Width < before.Width && surface.Height < before.Height :
                            surface.Width == originalExtent.Width && surface.Height == originalExtent.Height,
                            "YouTube Music Settings shrinks and Back restores the original extent");
                    }
                }
                finally { Appearance = original; surface!.ApplyAppearance(original, systemUi.AnimationsEnabled); }
            }

            async Task CheckCollectionReopen(string collectionId, string label)
            {
                await Until(() => Element(collectionId) is WidgetIndexedCollectionView { IsLoaded: true } collection &&
                    collection.NativeView.Items.Count > 2);
                var collection = (WidgetIndexedCollectionView)Element(collectionId)!;
                collection.NativeView.ScrollIntoView(collection.NativeView.Items[2]);
                await Until(() => collection.NativeView.ContainerFromIndex(2) is Control { IsLoaded: true, IsEnabled: true });
                var target = (Control)collection.NativeView.ContainerFromIndex(2);
                await Until(() => target.Focus(FocusState.Keyboard) && collection.FocusedRow()?.Lease.IsCurrent == true);
                var key = collection.FocusedRow()!.Item.Key;
                var id = AutomationProperties.GetAutomationId(target)["Widget.".Length..];
                await CheckReopen(id, label + " noninitial poster");
                Check(collection.FocusedRow()?.Item.Key == key, label + " restores exact item identity, not its header or first item");
            }

            async Task CheckReopen(string id, string label)
            {
                var widgetId = activeWidget!;
                var target = Element(id) as Control ?? throw new InvalidOperationException("Missing focus target " + id);
                await Until(() => target.Focus(FocusState.Keyboard));
                await Task.Delay(100);
                var cancelSwitcher = Appearance.WidgetSwitcher;
                Appearance = Appearance with { WidgetSwitcher = WidgetRail.PlatformSettings.WidgetSwitcherLayout.Radial };
                var unchangedInstance = surface!.CurrentBinding!.Frame.Authority.WidgetInstanceId;
                var unchangedPresenter = surface;
                PrepareRadialBackEntry(); SetInteractive(false); FocusTray();
                await Until(() => RadialOpen && FocusedTrayWidget()?.Id == widgetId);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                Check(shellOwnedReleases.Contains(ControllerButton.B), label + " radial return owns the closing B release");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                try { await Until(() => interactive && !RadialOpen && FocusId() == "Widget." + id); }
                catch (TimeoutException error) { throw new InvalidOperationException(label + " radial cancel: " + surface?.FocusDiagnostics(), error); }
                Check(activeWidget == widgetId && ReferenceEquals(surface, unchangedPresenter) &&
                    surface!.CurrentBinding!.Frame.Authority.WidgetInstanceId == unchangedInstance,
                    label + " radial cancel restores the same control without recreating the widget");
                Appearance = Appearance with { WidgetSwitcher = cancelSwitcher };
                for (var cycle = 0; cycle < 2; ++cycle)
                {
                    var trayTransfers = 0;
                    void TrayFocused(object sender, RoutedEventArgs args) => ++trayTransfers;
                    Tray.GotFocus += TrayFocused;
                    try
                    {
                        SetVisible(false);
                        SetForeground(false);
                        await Until(() => surface is { IsPresentationActive: false });
                        Check(interactive, label + " keeps logical widget ownership while hidden");
                        var hidden = surface!.CurrentBinding!.Frame.Authority;
                        Check(!surface.IsInteractionCurrent(hidden), label + " revokes input while retaining focus presentation");
                        SetVisible(true);
                        SetForeground(true);
                        QueueEntryFocus();
                        try { await Until(() => !switching && interactive && FocusId() == "Widget." + id); }
                        catch (TimeoutException error) { throw new InvalidOperationException(label + ": " + surface?.FocusDiagnostics(), error); }
                        await Task.Delay(150);
                        Check(trayTransfers == 0, label + " restores with no transient tray focus");
                        Check(FocusId() == "Widget." + id, label + " restores exact target after lifecycle publications");
                    }
                    finally { Tray.GotFocus -= TrayFocused; }
                }
                await SelectAsync(widgetId == "media-sessions" ? "widgetrail.samples.sdk-gallery" : "media-sessions", true);
                await SelectAsync(widgetId, true);
                try { await Until(() => !switching && interactive && FocusId() == "Widget." + id); }
                catch (TimeoutException error) { throw new InvalidOperationException(label + " switch-away/back: " + surface?.FocusDiagnostics(), error); }
                Check(true, label + " restores exact visible target after switching away and back");
                // Physical switching previews a visible (noninteractive) widget
                // before A enters it. Direct SelectAsync(id, true) skips this.
                var other = widgetId == "media-sessions" ? "widgetrail.samples.sdk-gallery" : "media-sessions";
                var originalSwitcher = Appearance.WidgetSwitcher;
                foreach (var switcher in new[] { WidgetRail.PlatformSettings.WidgetSwitcherLayout.Rail, WidgetRail.PlatformSettings.WidgetSwitcherLayout.Radial })
                {
                    Appearance = Appearance with { WidgetSwitcher = switcher };
                    foreach (var destination in new[] { other, widgetId })
                    {
                        PrepareRadialBackEntry(); SetInteractive(false); FocusTray();
                        await SelectAsync(destination, false);
                        await Until(() => !switching && !interactive && FocusedTrayWidget()?.Id == destination);
                        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                        await Until(() => !switching && interactive && activeWidget == destination);
                    }
                    try { await Until(() => FocusId() == "Widget." + id); }
                    catch (TimeoutException error) { throw new InvalidOperationException(label + " " + switcher + " preview-then-enter: " + surface?.FocusDiagnostics(), error); }
                    Check(true, label + " restores after " + switcher + " preview then controller A entry");
                }
                Appearance = Appearance with { WidgetSwitcher = originalSwitcher };
            }
            string FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused
                ? AutomationProperties.GetAutomationId(focused) : string.Empty;

            void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); checks.Add(message); }
            void Write(bool passed, string? error)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { passed, checks, error,
                    hostError = validationFailure?.ToString(), focus = surface?.FocusDiagnostics() }));
            }
            FrameworkElement? Element(string id) => surface is null ? null : Descendants(surface).OfType<FrameworkElement>()
                .FirstOrDefault(element => element.IsHitTestVisible && AutomationProperties.GetAutomationId(element) == "Widget." + id);
            WidgetPresentationSurface? Background() => surface is null ? null : Descendants(surface).OfType<WidgetPresentationSurface>()
                .FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == "Widget.playnite-library.cinematic");
            void Activate(string action)
            {
                var ids = Nodes(surface!.CurrentBinding!.View.Root).Where(node => node.ActionId == action).Select(node => node.Id);
                var target = ids.Select(Element).OfType<Button>().FirstOrDefault(button => button.Visibility == Visibility.Visible && button.ActualWidth > 0)
                    ?? throw new InvalidOperationException("Missing production action " + action);
                target.Command!.Execute(target.CommandParameter);
            }
            static IEnumerable<ViewNode> Nodes(ViewNode root) { yield return root; foreach (var child in root.Children) foreach (var node in Nodes(child)) yield return node; }
            static IEnumerable<DependencyObject> Descendants(DependencyObject root)
            { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
            static async Task Until(Func<bool> ready, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(ready))] string? condition = null)
            {
                var end = Environment.TickCount64 + 15000;
                while (!ready()) { if (Environment.TickCount64 > end) throw new TimeoutException(condition); await Task.Delay(20); }
            }
        };
    }
}
