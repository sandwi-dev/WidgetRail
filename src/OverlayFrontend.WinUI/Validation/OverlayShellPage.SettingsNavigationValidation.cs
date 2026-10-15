using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
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
    private List<string?>? settingsNavigationCommits;

    partial void ObserveCommittedNavigation(string widgetId, string? scopeId)
    {
        if (widgetId == "settings") settingsNavigationCommits?.Add(scopeId);
    }

    internal void EnableSettingsNavigationValidation(string resultPath)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            var samples = new List<object>();
            var commits = settingsNavigationCommits = [];
            string? failure = null;
            try
            {
                if (startup is not null) await startup;
                await Until(() => activeWidget == "source" && !switching && foreground);
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    if (cycle != 0) await SelectAsync("source");
                    await Open("manage");
                    Check(activeWidget == "settings" && interactive && !switching, "Settings accepts " + (cycle == 0 ? "cold" : "warm") + " navigation without blocking on optional details");
                    Check(!HostChoiceActive && !RecoveryVisible, "No false intent failure dialog or recovery surface");
                    Check(commits.Count == cycle + 1 && commits.All(scope => scope is not null && scope != "settings-root"),
                        "Only destination/loading frames are committed, never Settings home");
                    await Until(() => Nodes().Any(node => node.Text?.Contains("Synthetic navigation target", StringComparison.Ordinal) == true));
                    Check(!HostChoiceActive, "The requested widget details arrive without an error dialog");
                    // Let the intentionally delayed inspection complete on the
                    // first cycle; the second tests already initialized Settings.
                    if (cycle == 0) await Task.Delay(3300);
                }
                await SelectAsync("source");
                await Open("missing");
                await Until(() => Nodes().Any(node => node.Id == "intent.destination.error"));
                Check(!HostChoiceActive && Nodes().Any(node => node.Id == "intent.retry"),
                    "A genuinely missing package shows a destination error with retry instead of false success");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => surface?.CurrentBinding?.Frame.Snapshot.ActiveInputScopeId == "settings-root");
                Check(!HostChoiceActive && !RecoveryVisible, "Back leaves the unavailable destination and restores Settings navigation");
                await Action("open.installed-widgets");
                await Until(() => Collection()?.NativeView.Items.Count == 13);
                await Action("installed.filter.builtin");
                await Until(() => Collection()?.NativeView.Items.Count == 1);
                Check(Nodes().All(node => node.ActionId is not ("installed.next-page" or "installed.previous-page")), "Normal widget management has no community-only pagination");
                await Action("installed.filter.community");
                await Until(() => Collection()?.NativeView.Items.Count == 12);
                var tab = Descendants(surface!).OfType<Control>().Single(control => AutomationProperties.GetAutomationId(control) == "Widget.installed.filter.community");
                tab.Focus(FocusState.Keyboard);
                surface!.MoveFocus(FocusNavigationDirection.Down);
                await Until(() => Collection()?.FocusedRow() is not null);
                Check(true, "Controller Down from the filter enters the virtualized list");
                var collection = Collection()!;
                collection.NativeView.ScrollIntoView(collection.NativeView.Items[11]);
                await Until(() => collection.NativeView.ContainerFromIndex(11) is Control);
                ((Control)collection.NativeView.ContainerFromIndex(11)).Focus(FocusState.Keyboard);
                await Until(() => collection.FocusedRow() is { } row && row.Item.Key.StartsWith("community.", StringComparison.Ordinal));
                var returnKey = collection.FocusedRow()!.Item.Key;
                Check(collection.ActualHeight > 100, "The list receives a usable bounded viewport");
                Check(await collection.InvokeFocusedInputAsync(ControllerButton.A, ControllerEventPhase.Pressed), "A opens a realized widget row");
                await Until(() => surface?.CurrentBinding?.Frame.Snapshot.ActiveInputScopeId == "installed.details");
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => Collection()?.FocusedRow()?.Item.Key == returnKey);
                await Task.Delay(250);
                await CaptureSettingsListAsync(surface!, Path.ChangeExtension(resultPath, ".before-refresh.png"));
                CheckVisibleRow("Back");
                Check(true, "Back restores and reveals the offscreen selected row");
                var before = surface!.CurrentBinding!.Frame.Authority.SnapshotSequence;
                await Action("refresh");
                await Until(() => surface?.CurrentBinding?.Frame.Authority.SnapshotSequence > before && Nodes().Any(node => node.Id == "installed.retry" && node.IsBusy != true));
                await Until(() => Collection()?.FocusedRow()?.Item.Key == returnKey);
                Check(true, "Refreshing unchanged inventory preserves the selected row and filter");
                await Task.Delay(250);
                await CaptureSettingsListAsync(surface!, Path.ChangeExtension(resultPath, ".widgets.png"));
                CheckVisibleRow("Refresh");
                await Until(() => Nodes().All(node => node.Id != "settings.toast"));
                await Task.Delay(250);
                var refresh = Descendants(surface!).OfType<Control>().Single(control => AutomationProperties.GetAutomationId(control) == "Widget.installed.retry");
                refresh.Focus(FocusState.Keyboard);
                var scroll = Descendants(Collection()!).OfType<ScrollViewer>().First();
                var offset = scroll.VerticalOffset;
                var reveals = Collection()!.FocusedViewportRevealCount;
                var viewportBefore = scroll.ViewportHeight;
                before = surface!.CurrentBinding!.Frame.Authority.SnapshotSequence;
                await Action("refresh");
                await Until(() => surface?.CurrentBinding?.Frame.Authority.SnapshotSequence > before && Nodes().Any(node => node.Id == "installed.retry" && node.IsBusy != true));
                await Task.Delay(250);
                Check(FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && AutomationProperties.GetAutomationId(focused) == "Widget.installed.retry",
                    "Viewport changes do not reclaim focus after leaving the list");
                samples.Add(new { phase = "Unfocused shrink", offsetBefore = offset, offsetAfter = scroll.VerticalOffset,
                    viewportBefore, viewportAfter = scroll.ViewportHeight, revealsBefore = reveals, revealsAfter = Collection()!.FocusedViewportRevealCount });
                Check(Collection()!.FocusedViewportRevealCount == reveals, "An unfocused viewport resize issues no host focus-reveal request");
                Check(Math.Abs((scroll.VerticalOffset + scroll.ViewportHeight) - (offset + viewportBefore)) <= 1,
                    "Native resize anchoring preserves the visible end of the unfocused list");
                await ValidateStoppedSurfaceAsync(Check);
            }
            catch (Exception error) { failure = error.ToString(); }
            finally { settingsNavigationCommits = null; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = failure is null, checks, commits, samples, error = failure }));

            async Task Open(string id)
            {
                await InvokeAsync(new(surface!.CurrentBinding!.Frame, new(id, id, ControllerButton.A, InputScopeId: "root")));
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            }
            async Task Action(string action)
            {
                var node = Nodes().First(node => node.ActionId == action);
                await InvokeAsync(new(surface!.CurrentBinding!.Frame, new(action, node.Id, ControllerButton.A,
                    InputScopeId: surface.CurrentBinding.Frame.Snapshot.ActiveInputScopeId)));
            }
            WidgetIndexedCollectionView? Collection() => surface is null ? null : Descendants(surface).OfType<WidgetIndexedCollectionView>().SingleOrDefault();
            void CheckVisibleRow(string phase)
            {
                var collection = Collection()!;
                var item = collection.NativeView.ContainerFromIndex(11) as FrameworkElement;
                var focus = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                var bounds = item?.TransformToVisual(collection.NativeView).TransformBounds(new(0,0,item.ActualWidth,item.ActualHeight));
                var ownsFocus = item is not null && focus is not null && Descendants(item).Any(node => ReferenceEquals(node,focus));
                samples.Add(new { phase, focus = focus is null ? null : AutomationProperties.GetAutomationId(focus), ownsFocus,
                    top = bounds?.Y, bottom = bounds?.Bottom, viewport = collection.NativeView.ActualHeight });
                Check(ownsFocus, phase + " keeps actual native focus on the selected row");
                Check(bounds is { Y: >= -1 } rectangle && rectangle.Bottom <= collection.NativeView.ActualHeight + 1,
                    phase + " keeps the entire selected row inside the list viewport");
            }
            IEnumerable<ViewNode> Nodes() => surface?.CurrentBinding is { } binding ? Walk(binding.Frame.Snapshot.Root) : [];
            async Task Until(Func<bool> condition)
            {
                var end = Environment.TickCount64 + 15_000;
                while (!condition())
                {
                    if (HostChoiceActive || RecoveryVisible || validationFailure is not null)
                        throw new InvalidOperationException($"Settings navigation unexpectedly opened a failure surface: {Status.Text}; dialog={HostChoiceActive}; scope={surface?.CurrentBinding?.Frame.Snapshot.ActiveInputScopeId}.", validationFailure);
                    if (Environment.TickCount64 >= end) throw new TimeoutException("Settings navigation did not settle.");
                    await Task.Delay(20);
                }
            }
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        };
        static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
        static IEnumerable<DependencyObject> Descendants(DependencyObject node)
        {
            yield return node;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(node,index))) yield return child;
        }
    }

    private static async Task CaptureSettingsListAsync(FrameworkElement element, string path)
    {
        // Render only the synthetic XAML control, never desktop/window pixels.
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync(); stream.Seek(0);
        using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes);
    }
}
