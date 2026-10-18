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
    private async Task PobItemInspectionAsync(string directory)
    {
        Exception? failure = null;
        var root = new Grid();
        var page = new WidgetViewPresenter();
        root.Children.Add(page); PresentationLayers.Attach(root);
        presenter.Visibility = Visibility.Collapsed; host.Children.Add(root);
        var popupTheme = new NativePopupTheme(); popupTheme.Attach(page);
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
        var package = WrssParser.Parse(File.ReadAllText(Path.Combine(directory, "pob.wrss")), "pob.wrss");
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.Resize(new((int)(1480 * scale), (int)(1100 * scale)));
        page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        page.Failed = error => failure ??= error;
        try
        {
            foreach (var themeId in new[] { ThemeIdentity.BuiltInMonoInk, ThemeIdentity.BuiltInMineralLight })
            foreach (var game in new[] { 1, 2 })
            foreach (var kind in new[] { "inventory", "unique" })
            foreach (var size in new[] { (Width: 1320d, Height: 900d), (Width: 820d, Height: 600d) })
            {
                root.Width = page.Width = size.Width; root.Height = page.Height = size.Height;
                var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,
                    new((WrssDocument[])[package.Document], []), catalog.Load(themeId, "1.0.0").Package).Theme!;
                var inspection = SnapshotJson.Deserialize(File.ReadAllBytes(Path.Combine(directory, $"poe{game}-{kind}-inspection.json")));
                var row = JsonSerializer.Deserialize<ViewNode>(File.ReadAllBytes(Path.Combine(directory, $"poe{game}-{kind}-row.json")))!;
                var collectionId = inspection.Root.AnchoredPresentation!.Anchor.IndexedItem!.CollectionId;
                // Real exported declarations, tooltip and demanded row. Indexed lease/provider
                // authority is tested by the real-engine suite. This fixture materializes one
                // resident row solely to qualify native styling, geometry and input continuity.
                ViewNode Materialize(ViewNode node) => node.Id == collectionId && node.Kind == ViewNodeKind.IndexedCollection
                    ? new ViewNode { Id = node.Id, Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical,
                        StyleClasses = node.StyleClasses, Children = (ViewNode[])[row] }
                    : node with { Children = node.Children.Select(Materialize).ToArray() };
                inspection = inspection with { Root = Materialize(inspection.Root) with
                    { AnchoredPresentation = inspection.Root.AnchoredPresentation with { Anchor = new(ElementId: row.Id) } },
                    FocusGroupEntryRequest = null };
                var background = inspection.Root.Children[0];
                var picker = inspection with { Root = background, ActiveInputScopeId = background.InputScopeId!,
                    InitialFocusId = row.Id, FocusGroupEntryRequest = null };
                var open = false;
                var selected = 0;
                var equipped = 0;
                var slotChanges = 0;
                var detailNode = inspection.Root.Children[1];
                Check(detailNode.Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.B &&
                    shortcut.ActionId.StartsWith("candidate-inspection-close.", StringComparison.Ordinal)),
                    "Exported item modal declares B dismissal in its own input scope");
                page.DispatchActionAsync = request =>
                {
                    var action = request.Action.ActionId;
                    if (action.StartsWith("candidate-inspection-close.", StringComparison.Ordinal)) { open = false; Publish(); }
                    else if (action.StartsWith("candidate-inspection-slot.", StringComparison.Ordinal))
                    {
                        ++slotChanges;
                        ViewNode SelectSlot(ViewNode node) => node.Id == "pob.candidate-inspection.slot"
                            ? node with { SelectOptions = node.SelectOptions.Select(option => option with { IsSelected = option.ActionId == action }).ToArray() }
                            : node with { Children = node.Children.Select(SelectSlot).ToArray() };
                        detailNode = SelectSlot(detailNode); Publish();
                    }
                    else if (action.StartsWith("candidate-inspection-equip.", StringComparison.Ordinal)) ++equipped;
                    else if (action == "select") ++selected;
                    return Task.CompletedTask;
                };
                Publish();
                await Wait(() => Find<Button>("Widget." + row.Id, root) is { IsLoaded: true });
                var opener = Find<Button>("Widget." + row.Id, root)!;
                opener.Focus(FocusState.Keyboard);
                await Wait(() => page.IsFocusPreviewOpen);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), opener), "Item passive preview keeps source row focus");
                Check(selected == 0 && equipped == 0, "Preview does not select or equip an item");
                Check(row.Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.X && shortcut.ActionId == "inspect") &&
                    row.ContextActions.Any(action => action.ActionId == "inspect"), "Real item row declares local X and context inspection");
                open = true; Publish();
                await Wait(() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused &&
                    AutomationProperties.GetAutomationId(focused) == "Widget." + inspection.InitialFocusId);
                var panel = Find<FrameworkElement>("Widget.pob.candidate-inspection", root)!;
                await Wait(() => panel.ActualWidth > 0 && panel.ActualHeight > 0);
                var bounds = panel.TransformToVisual(page).TransformBounds(new(0, 0, panel.ActualWidth, panel.ActualHeight));
                Check(bounds.Left >= 15 && bounds.Top >= 15 && bounds.Right <= page.ActualWidth - 15 && bounds.Bottom <= page.ActualHeight - 15,
                    $"PoE{game} {kind} inspection fits {size.Width}x{size.Height} in {themeId}");
                var scrolls = Descendants(panel).OfType<ScrollViewer>().ToArray();
                Check(scrolls.Length > 0 && scrolls.All(scroll => scroll.ExtentWidth <= scroll.ViewportWidth + 1), "Item tooltip uses vertical scrolling without horizontal overflow");
                var choose = Descendants(panel).OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button).Contains("candidate-inspection-choose.", StringComparison.Ordinal));
                choose.StartBringIntoView();
                await Wait(() => scrolls.All(scroll => scroll.ScrollableHeight <= 0 || WidgetReadingScrollFocus.IsVisible(choose, scroll)));
                Check(choose.IsEnabled && choose.Focus(FocusState.Keyboard), "Select item or Open item draft stays reachable after scrolling");
                if (kind == "inventory")
                {
                    var slot = Find<Button>("Widget.pob.candidate-inspection.slot", root)!;
                    slot.StartBringIntoView();
                    await Wait(() => scrolls.All(scroll => scroll.ScrollableHeight <= 0 || WidgetReadingScrollFocus.IsVisible(slot, scroll)));
                    Check(slot.Focus(FocusState.Keyboard), "Inventory equipment slot control accepts focus");
                    page.ActivateFocused(); await Wait(() => page.HasTransientControl);
                    await page.HandleControllerButtonAsync(ControllerButton.B);
                    await Wait(() => !page.HasTransientControl && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), slot));
                    Check(slotChanges == 0 && equipped == 0, "Cancelling slot dropdown keeps slot focus and performs no equip");
                    page.ActivateFocused(); await Wait(() => page.HasTransientControl &&
                        FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused &&
                        AutomationProperties.GetAutomationId(focused).Contains(".Option.", StringComparison.Ordinal));
                    await page.HandleControllerButtonAsync(ControllerButton.A);
                    // The real fixture item has one eligible slot. Explicit A
                    // confirms that selection once, without equipping the item.
                    await Wait(() => !page.HasTransientControl && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), slot));
                    Check(slotChanges == 1 && equipped == 0, "Confirming the slot dispatches once and preserves focus without equipping");
                    var equip = Descendants(panel).OfType<Button>().Single(button =>
                        AutomationProperties.GetAutomationId(button).Contains("candidate-inspection-equip.", StringComparison.Ordinal));
                    equip.StartBringIntoView();
                    await Wait(() => scrolls.All(scroll => scroll.ScrollableHeight <= 0 || WidgetReadingScrollFocus.IsVisible(equip, scroll)));
                    Check(equip.Focus(FocusState.Keyboard) && equip.IsEnabled, "Explicit Equip remains independently reachable");
                }
                else Check(!Descendants(panel).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button)
                    .Contains("candidate-inspection-equip.", StringComparison.Ordinal)), "Unique inspection exposes a draft action without an implicit Equip action");
                if (size.Width == 1320) await Capture(Path.Combine(directory, $"native-poe{game}-{kind}-{themeId}.png"));
                Find<Button>("Widget." + inspection.InitialFocusId, root)!.Focus(FocusState.Keyboard);
                page.ActivateFocused();
                await Wait(() => !open && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), opener));
                Check(selected == 0 && equipped == 0, "Close returns inspection to the same native item without selecting or equipping");
                page.ActivateFocused();
                Check(selected == 1 && equipped == 0, "A retains the item's existing selection action after inspection");

                void Publish()
                {
                    var snapshot = (open ? inspection with { Root = inspection.Root with { Children = (ViewNode[])[background, detailNode] } } : picker)
                        with { Sequence = ++sequence, WidgetInstanceId = "pob-item-fixture" };
                    var styles = new Dictionary<string, BridgeNodeRenderStyles>();
                    void Visit(ViewNode node)
                    {
                        styles[node.Id] = Compute(theme, node);
                        foreach (var child in node.Children) Visit(child);
                        if (node.FocusPresentation is { } fragment) Visit(fragment);
                        if (node.DefaultFocusPresentation is { } fallback) Visit(fallback);
                    }
                    Visit(snapshot.Root);
                    foreach (var role in new[] { "body", "canvas", "panel", "hint", "tray-item" }) styles[role] = Compute(theme, role, role, []);
                    popupTheme.Update(styles, AppearanceSettings.Default);
                    var descriptor = new BridgeWidgetDescriptor { Id = "pob", Name = "PoB item fixture", InstanceId = snapshot.WidgetInstanceId,
                        RuntimeGeneration = "pob-item", PresentationGeneration = "pob-item", Icon = WidgetGlyph.Settings, PackageContentDigest = "" };
                    page.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                        snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                    page.UpdateLayout();
                }
            }
        }
        catch (Exception error) { failure = error; await Capture(Path.Combine(directory, "native-failure.png")); }
        finally { await page.DisposeAsync(); host.Children.Remove(root); }
        await File.WriteAllTextAsync(Path.Combine(directory, "native-result.json"), JsonSerializer.Serialize(new
        {
            passed = failure is null, checks, error = failure?.ToString(),
            scope = "Native layout/input with one materialized exported row; provider leases, actual inventory edits and unique drafts are covered separately by real-engine tests."
        }));
        if (failure is not null) throw failure;
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
