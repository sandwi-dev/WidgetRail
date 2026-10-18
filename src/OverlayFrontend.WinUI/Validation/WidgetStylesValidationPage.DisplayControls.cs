using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    // Real widget declarations with local action completion only. This fixture
    // never instantiates a display provider or performs a hardware write.
    private async Task DisplayControlsAsync(string directory)
    {
        Exception? failure = null;
        TaskCompletionSource? pendingDispatch = null;
        var root = new Grid();
        var page = new WidgetViewPresenter();
        root.Children.Add(page); PresentationLayers.Attach(root);
        presenter.Visibility = Visibility.Collapsed; host.Children.Add(root);
        var popupTheme = new NativePopupTheme(); popupTheme.Attach(page);
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
        var package = WrssParser.Parse(File.ReadAllText(Path.Combine(directory, "display.wrss")), "display.wrss");
        var ready = Read("controls"); var busy = Read("controls-busy"); var confirmation = Read("controls-confirmation");
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.Resize(new((int)(1000 * scale), (int)(1000 * scale)));
        page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        try
        {
            foreach (var themeId in new[] { ThemeIdentity.BuiltInMonoInk, ThemeIdentity.BuiltInMineralLight })
            foreach (var size in new[] { (Width: 780d, Height: 700d), (Width: 640d, Height: 420d) })
            {
                root.Width = page.Width = size.Width; root.Height = page.Height = size.Height;
                var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,
                    new((WrssDocument[])[package.Document], []), catalog.Load(themeId, "1.0.0").Package).Theme!;
                var current = ready;
                var selected = 0; var applied = 0; var decided = 0;
                var release = pendingDispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                page.DispatchActionAsync = request =>
                {
                    if (request.Action.ActionId == "controls-apply") { ++applied; return release.Task; }
                    if (request.Action.ActionId is "keep" or "revert") { ++decided; current = ready; Publish(); }
                    else if (request.Action.ActionId.StartsWith("controls-mode.", StringComparison.Ordinal) ||
                        request.Action.ActionId.StartsWith("controls-resolution.", StringComparison.Ordinal) ||
                        request.Action.ActionId.StartsWith("controls-monitor.", StringComparison.Ordinal)) ++selected;
                    return Task.CompletedTask;
                };
                Publish();
                await Wait(() => Find<Button>("Widget.display.control.monitor", root) is { IsLoaded: true, ActualWidth: > 0 });
                foreach (var id in new[] { "display.control.monitor", "display.control.resolution", "display.control.refresh-rate" })
                {
                    var selector = Find<Button>("Widget." + id, root)!;
                    await Focus(selector);
                    Check(await page.HandleControllerButtonAsync(ControllerButton.A), id + " opens with A");
                    await Wait(() => page.HasTransientControl && FocusedId().Contains(".Option.", StringComparison.Ordinal));
                    Check(FocusedId().StartsWith("Widget." + id + ".Option.", StringComparison.Ordinal), id + " popup owns focus");
                    await page.HandleControllerButtonAsync(ControllerButton.B);
                    await Wait(() => !page.HasTransientControl && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), selector));
                    Check(selected == 0, id + " B dismisses without selection or leaving controls");
                }
                var resolution = Find<Button>("Widget.display.control.resolution", root)!;
                await Focus(resolution);
                await page.HandleControllerButtonAsync(ControllerButton.A);
                await Wait(() => page.HasTransientControl && FocusedId().Contains(".Option.", StringComparison.Ordinal));
                page.MoveFocus(FocusNavigationDirection.Down);
                await page.HandleControllerButtonAsync(ControllerButton.A);
                await Wait(() => selected == 1 && !page.HasTransientControl && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), resolution));
                Check(selected == 1, "Resolution choice dispatches once and returns to its selector");

                var apply = Find<Button>("Widget.display.control.apply", root)!;
                await Focus(apply);
                var command = apply.Command ?? throw new InvalidOperationException("Display apply has no native command.");
                command.Execute(null);
                await Wait(() => applied == 1);
                current = busy; Publish();
                await Task.Delay(30);
                Check(ReferenceEquals(apply, Find<Button>("Widget.display.control.apply", root)) && apply.IsEnabled &&
                    ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), apply), "Busy apply retains exact native control and focus");
                command.Execute(null);
                await Task.Delay(30);
                Check(applied == 1, "Busy native apply rejects duplicate dispatch");
                var hdr = Find<Button>("Widget.display.control.hdr", root)!;
                await Focus(hdr);
                release.TrySetResult(); current = ready; Publish();
                await Task.Delay(30);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), hdr), "Completion preserves an explicit move to HDR");
                var scrolls = Descendants(root).OfType<ScrollViewer>().ToArray();
                Check(scrolls.All(scroll => scroll.ExtentWidth <= scroll.ViewportWidth + 1), "Display controls avoid horizontal overflow");
                await Focus(Find<Button>("Widget.display.control.monitor", root)!);
                await Capture(Path.Combine(directory, $"native-{themeId}-{(int)size.Width}.png"));

                foreach (var decision in new[] { "revert", "keep" })
                {
                    // Every real apply obtains a new restore transaction/scope.
                    // Replaying one exported scope would correctly restore its
                    // previous focus, rather than exercise new-transaction entry.
                    var scope = "display.restore.fixture" + sequence;
                    current = confirmation with { ActiveInputScopeId = scope,
                        Root = confirmation.Root with { InputScopeId = scope } }; Publish();
                    await Wait(() => FocusedId() == "Widget.display.revert");
                    var panel = Find<FrameworkElement>("Widget.display.confirmation", root)!;
                    await Wait(() => panel.ActualWidth > 0 && panel.ActualHeight > 0);
                    var bounds = panel.TransformToVisual(page).TransformBounds(new(0, 0, panel.ActualWidth, panel.ActualHeight));
                    Check(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= page.ActualWidth + 1 && bounds.Bottom <= page.ActualHeight + 1,
                        $"Keep/Revert fits {size.Width}x{size.Height} in {themeId}");
                    foreach (var id in new[] { "display.keep", "display.revert" })
                        await Focus(Find<Button>("Widget." + id, root)!);
                    await Focus(Find<Button>("Widget.display." + decision, root)!);
                    var before = decided;
                    await page.HandleControllerButtonAsync(ControllerButton.A);
                    await Wait(() => decided == before + 1 && FocusedId().StartsWith("Widget.display.control.", StringComparison.Ordinal));
                    Check(Find<Button>("Widget.display.control.apply", root) is { IsEnabled: true }, decision + " returns to usable display controls");
                }

                async Task Focus(Button button)
                {
                    button.StartBringIntoView();
                    await Wait(() => Descendants(root).OfType<ScrollViewer>().Where(scroll => IsAncestor(scroll, button))
                        .All(scroll => scroll.ScrollableHeight <= 0 || WidgetReadingScrollFocus.IsVisible(button, scroll)));
                    Check(button.IsEnabled && button.Focus(FocusState.Keyboard), AutomationProperties.GetAutomationId(button) + " is reachable");
                    await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), button));
                }
                void Publish()
                {
                    var snapshot = current with { Sequence = ++sequence, WidgetInstanceId = "display-controls-fixture" };
                    var errors = ViewSnapshotValidator.Validate(snapshot);
                    if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors));
                    var styles = new Dictionary<string, BridgeNodeRenderStyles>();
                    void Visit(ViewNode node)
                    {
                        styles[node.Id] = Compute(theme, node.Id, node.Kind.ToString().ToLowerInvariant(), node.StyleClasses.ToArray());
                        foreach (var child in node.Children) Visit(child);
                    }
                    Visit(snapshot.Root);
                    foreach (var role in new[] { "body", "canvas", "panel", "hint", "tray-item" }) styles[role] = Compute(theme, role, role, []);
                    popupTheme.Update(styles, AppearanceSettings.Default);
                    var descriptor = new BridgeWidgetDescriptor { Id = "display-controls", Name = "Display Controls fixture", InstanceId = snapshot.WidgetInstanceId,
                        RuntimeGeneration = "display-controls", PresentationGeneration = "display-controls", Icon = WidgetGlyph.Fullscreen, PackageContentDigest = "" };
                    page.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                        snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                    page.UpdateLayout();
                }
            }
        }
        catch (Exception error) { failure = error; await Capture(Path.Combine(directory, "native-failure.png")); }
        finally { pendingDispatch?.TrySetResult(); await page.DisposeAsync(); host.Children.Remove(root); }
        await File.WriteAllTextAsync(Path.Combine(directory, "native-result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;
        string FocusedId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : "";
        static bool IsAncestor(DependencyObject ancestor, DependencyObject child)
        {
            for (var node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(child); node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
                if (ReferenceEquals(node, ancestor)) return true;
            return false;
        }
        ViewSnapshot Read(string name)
        {
            var path = Path.Combine(directory, name + ".json");
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Display fixture exceeds bound.");
            return SnapshotJson.Deserialize(File.ReadAllBytes(path));
        }
        async Task Capture(string path)
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(root);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync(); stream.Seek(0);
            using var output = File.Create(path); await stream.AsStreamForRead().CopyToAsync(output);
        }
    }
}
