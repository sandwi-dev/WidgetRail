using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task InteractivePreviewsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var centeredOnly=Environment.GetCommandLineArgs().Contains("--centered-preview-only");
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.ResizeClient(new((int)Math.Ceiling(1100 * scale), (int)Math.Ceiling(900 * scale)));
        Exception? failure = null; object? failedState = null; object? readingState = null; object? hoverFailure = null; var readingTaps = 0; var readingPresses = 0; var readingReleases = 0;
        var hoverRoute = new List<object>();
        var observeHover = false;
        var sourceHoverMoves = 0;
        var presenterExits = 0;
        var root = new Grid { Width = 820, Height = 600 }; var page = new WidgetViewPresenter { Width = 800, Height = 580 };
        root.Children.Add(page); PresentationLayers.Attach(root); presenter.Visibility = Visibility.Collapsed; host.Children.Add(root);
        PointerEventHandler exited = (_, _) => { if (observeHover) ++presenterExits; };
        page.PointerExited += exited;
        PointerEventHandler moved = (sender, args) =>
        {
            if (!observeHover) return;
            if (hoverRoute.Count >= 48) hoverRoute.RemoveAt(0);
            var point = args.GetCurrentPoint(page).Position;
            var route = new List<string>();
            for (var node = args.OriginalSource as DependencyObject; node is not null && route.Count < 8;
                 node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
                route.Add(node.GetType().Name + ":" + AutomationProperties.GetAutomationId(node));
            if(route.Contains("WidgetReadingSurface:Widget.preview.source"))++sourceHoverMoves;
            hoverRoute.Add(new { point.X, point.Y, args.Handled, route, observer = ReferenceEquals(sender, root) ? "fixture" : "xamlRoot" });
        };
        root.AddHandler(UIElement.PointerMovedEvent, moved, true);
        var inputRoot = XamlRoot.Content as UIElement;
        inputRoot?.AddHandler(UIElement.PointerMovedEvent, moved, true);
        var popup = new NativePopupTheme(); popup.Attach(page);
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
        long sequence = 0; var sourceVisible = true; var busy = false; var primary = 0; var applied = 0;
        var selected = "one"; var delay = 0; var readingSource = false; var passiveFocus = true; var passiveHover = true; var enterButton = ControllerButton.RightStick; double volume = 50; var valueWrites = 0;
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Reduced };
        var materialStyle = WrssParser.Parse(Environment.GetCommandLineArgs().Contains("--interactive-preview-acrylic")
            ? "preview { background-material: acrylic; } .preview-source-row { width:180px; }"
            : ".preview-source-row { width:180px; }", "interactive-preview.wrss");
        var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new((WrssDocument[])[materialStyle.Document], []),
            catalog.Load("widgetrail.builtin.velvet-dusk", "1.0.0").Package).Theme!;
        page.ApplyAppearance(appearance, false); page.Failed = error => failure ??= error;
        page.DispatchActionAsync = request =>
        {
            switch (request.Action.ActionId)
            {
                case "choose": primary++; break;
                case "apply": applied++; break;
                case "one": selected = "one"; break;
                case "two": selected = "two"; break;
                case "volume" when request.Action.RequestedValue is { } value: volume = value; valueWrites++; break;
            }
            Publish(); return Task.CompletedTask;
        };
        try
        {
            Publish(); await Wait(() => Find<Button>("Widget.preview.source", root) is { IsLoaded: true });
            page.Enter(restoreNativeFocus: false);
            var source = Find<Button>("Widget.preview.source", root)!; source.Focus(FocusState.Keyboard);
            await Wait(() => page.IsFocusPreviewOpen);
            await Wait(() => Find<Button>("Widget.preview.apply", root) is { IsLoaded: true });
            var apply = Find<Button>("Widget.preview.apply", root)!;
            var chrome = page.FocusPreviewChrome;
            Check(apply is { IsLoaded: true } && Find<Button>("Widget.preview.select", root) is not null,
                "Full actions and selector are visible before entry");
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), source) && primary == 0 && applied == 0,
                "Showing full preview keeps source focus without dispatch");
            if(centeredOnly)
            {
                await Wait(()=>chrome!.ActualWidth>0 && chrome.ActualHeight>0);
                var anchor=source.TransformToVisual(page).TransformBounds(new(0,0,source.ActualWidth,source.ActualHeight));
                var placed=chrome!.TransformToVisual(page).TransformBounds(new(0,0,chrome.ActualWidth,chrome.ActualHeight));
                Check(placed.Left>=0 && placed.Top>=0 && placed.Right<=page.ActualWidth && placed.Bottom<=page.ActualHeight,
                    "Centered preview stays clamped to its widget viewport");
                Check(placed.Left<anchor.Right && placed.Right>anchor.Left && placed.Top<anchor.Bottom && placed.Bottom>anchor.Top,
                    "Centered preview overlays the edge source instead of choosing a free side");
                await NativeContextProbeInput.HoverTransitAsync(source,()=>apply,()=>Wait(()=>page.IsFocusPreviewOpen),()=>
                {Check(ReferenceEquals(page.FocusPreviewChrome,chrome),"Overlapping source-to-preview hover keeps the same chrome");return Task.CompletedTask;});
                await page.HandleControllerButtonAsync(ControllerButton.RightStick);
                await Wait(()=>ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),apply));
                await page.HandleControllerButtonAsync(ControllerButton.B);
                await Wait(()=>ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),source));
                Check(ReferenceEquals(page.FocusPreviewChrome,chrome),"Centered preview entry and Back preserve its existing owner");
                var outside=new Button{Content="Outside",Width=130,Height=40,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom};
                var clicks=0;outside.Click+=(_,_)=>clicks++;root.Children.Add(outside);
                await Wait(()=>outside.IsLoaded && outside.ActualWidth>0);
                await NativeContextProbeInput.LeftClickAsync(outside,new(65,20));
                await Wait(()=>!page.IsFocusPreviewOpen && clicks==1);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),outside)&&primary==0&&applied==0,
                    "Centered preview outside click dismisses and activates only the existing outside target");
            }
            else
            {
            var bounds = (chrome!.ActualWidth, chrome.ActualHeight);
            await NativeContextProbeInput.HoverTransitAsync(source,
                () => Find<Button>("Widget.preview.select", root)!,
                () => Wait(() => page.IsFocusPreviewOpen),
                () =>
                {
                    Check(page.IsFocusPreviewOpen && ReferenceEquals(page.FocusPreviewChrome, chrome),
                        "Real slow diagonal mouse transit across adjacent content retains the same interactive preview");
                    Check(primary == 0 && applied == 0 && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), source),
                        "Hover transit neither activates the source nor steals keyboard focus");
                    return Task.CompletedTask;
                });
            await page.HandleControllerButtonAsync(ControllerButton.RightStick);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), apply));
            Check(ReferenceEquals(page.FocusPreviewChrome, chrome) && ReferenceEquals(apply, Find<Button>("Widget.preview.apply", root)),
                "Entry preserves panel and action control instances");
            Check(page.OwnsKeyboardEscape, "Entered preview owns Escape ahead of outer modal or pin return");
            Check(bounds == (chrome.ActualWidth, chrome.ActualHeight) && sequence == 1 && applied == 0,
                "Entry changes no geometry and sends no widget action");
            busy = true; Publish(); await Task.Delay(80);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), apply) && apply.IsEnabled,
                "Busy updates retain focus and the original control");
            page.ActivateFocused(); Check(applied == 0, "Busy action does not dispatch");
            busy = false; Publish(); await Task.Delay(80);
            var slider = Find<Slider>("Widget.preview.volume", root)!;
            slider.Focus(FocusState.Keyboard); page.ActivateFocused();
            page.MoveFocus(FocusNavigationDirection.Right);
            await Wait(() => volume == 55 && valueWrites == 1);
            Check(ReferenceEquals(page.FocusPreviewChrome, chrome) && ReferenceEquals(slider, Find<Slider>("Widget.preview.volume", root)),
                "Preview slider sends its numeric value and retains native controls across the acknowledgement");
            Check(page.CaptureControllerGuide().Any(hint => hint.Button == ControllerButton.B && hint.Label == "Finish adjustment"),
                "Preview guide identifies the slider's first Back action");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            Check(page.CaptureControllerGuide().Any(hint => hint.Button == ControllerButton.B && hint.Label == "Back to source"),
                "Preview guide updates after adjustment finishes");
            Check(page.IsFocusPreviewOpen && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), slider),
                "First B leaves slider adjustment without dismissing the interactive preview");
            NativeContextProbeInput.PressKey(Windows.System.VirtualKey.Right);
            await Wait(() => volume == 60 && valueWrites == 2);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), slider),
                "Real keyboard Right adjusts the preview slider without controller engagement or focus movement");
            NativeContextProbeInput.PressKey(Windows.System.VirtualKey.Left);
            await Wait(() => volume == 55 && valueWrites == 3);
            var select = Find<Button>("Widget.preview.select", root)!;
            select.Focus(FocusState.Keyboard); page.ActivateFocused();
            await page.HandleControllerButtonAsync(ControllerButton.B);
            Check(!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), source), "First B closes only nested selector");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), source), "Second B returns to exact source");
            Check(ReferenceEquals(page.FocusPreviewChrome, chrome) && ReferenceEquals(apply, Find<Button>("Widget.preview.apply", root)),
                "Return leaves the same complete preview visible");
            await page.HandleControllerButtonAsync(ControllerButton.RightStick);
            apply.Focus(FocusState.Keyboard); page.ActivateFocused(); await Wait(() => applied == 1);
            Check(primary == 0, "Preview Apply does not activate the source");
            var admission=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            page.EnsureInteractionAsync=(_,_)=>admission.Task;
            page.ActivateFocused();
            sourceVisible = false; Publish(); await Wait(() => !page.IsFocusPreviewOpen);
            admission.SetResult(true); await Task.Delay(60);
            Check(applied==1,"Late admission after source removal cannot dispatch");
            page.EnsureInteractionAsync=null;
            Check(true, "Source removal revokes the preview");
            sourceVisible = true; delay=1500; Publish(); source = Find<Button>("Widget.preview.source", root)!;
            source.Focus(FocusState.Keyboard);
            await page.HandleControllerButtonAsync(ControllerButton.RightStick);
            await Wait(()=>ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),Find<Button>("Widget.preview.apply",root)));
            Check(page.IsFocusPreviewOpen,"Explicit entry does not wait for the hover settling delay");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            Find<Button>("Widget.preview.apply", root)!.Focus(FocusState.Pointer);
            Check(primary == 0, "Pointer entry transfers focus without choosing the source");
            delay = 0; Publish(); source.Focus(FocusState.Keyboard);
            await page.HandleControllerButtonAsync(ControllerButton.RightStick);
            await Wait(() => page.IsFocusPreviewOpen);
            var dragged = Find<Slider>("Widget.preview.volume", root)!;
            Check(dragged.Focus(FocusState.Keyboard), "Drag cancellation fixture focuses its actual preview slider");
            await Task.Delay(60);
            await NativeContextProbeInput.HoldSliderDragAsync(dragged, async () =>
            {
                await Task.Delay(180);
                sourceVisible = false; Publish();
                await Wait(() => !page.IsFocusPreviewOpen);
                Check(!WalkPointerTree(dragged).Any(element => element.PointerCaptures is { Count: > 0 }),
                    "Source removal releases native slider and thumb pointer capture before detaching the preview");
            }, () => Task.CompletedTask);
            sourceVisible = true; Publish();
            await Wait(() => Find<Button>("Widget.preview.source", root) is { IsLoaded: true });
            Find<Button>("Widget.preview.source", root)!.Focus(FocusState.Keyboard);
            await page.HandleControllerButtonAsync(ControllerButton.RightStick);
            await Wait(() => page.IsFocusPreviewOpen && Find<Slider>("Widget.preview.volume", root) is { IsLoaded: true });
            var reopenedSlider = Find<Slider>("Widget.preview.volume", root)!;
            var afterRelease = volume;
            await NativeContextProbeInput.HoverAsync(reopenedSlider, async () =>
            {
                await Task.Delay(250);
                Check(volume == afterRelease, "Reopened preview ignores unpressed pointer movement after an interrupted drag");
            });
            Find<Button>("Widget.preview.apply", root)!.Focus(FocusState.Keyboard);
            await page.HandleControllerButtonAsync(ControllerButton.B);
            source = Find<Button>("Widget.preview.source", root)!; source.Focus(FocusState.Keyboard);
            await page.HandleControllerButtonAsync(ControllerButton.A);
            await Wait(() => primary == 1);
            Check(primary == 1, "Ordinary primary action is preserved for a RightStick-entry preview");
            enterButton = ControllerButton.A; Publish(); source.Focus(FocusState.Keyboard);
            await Wait(() => page.IsFocusPreviewOpen);
            chrome = page.FocusPreviewChrome;
            Check(page.CaptureControllerGuide().Any(hint => hint.Button == ControllerButton.A && hint.Prompt == ControllerPrompt.A),
                "Primary preview advertises the A glyph");
            await page.HandleControllerButtonAsync(ControllerButton.A);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.preview.apply", root)));
            Check(primary == 1 && ReferenceEquals(chrome, page.FocusPreviewChrome), "A enters existing preview without invoking its source action");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            await NativeContextProbeInput.LeftClickAsync(source, new(source.ActualWidth / 2, source.ActualHeight / 2));
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.preview.apply", root)));
            Check(primary == 1, "Native button click enters primary preview without a widget command");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            sourceVisible = false; Publish(); await Wait(() => !page.IsFocusPreviewOpen);
            readingSource = true; delay = 1500; sourceVisible = true; Publish();
            await Wait(() => Find<WidgetReadingSurface>("Widget.preview.source", root) is { IsLoaded: true });
            var reading = Find<WidgetReadingSurface>("Widget.preview.source", root)!;
            readingState = new { type = reading.GetType().Name, reading.ActualWidth, reading.ActualHeight,
                template = reading.Template is not null, visualChildren = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(reading),
                reading.IsLoaded, reading.IsHitTestVisible,
                background = reading.Background?.GetType().Name, backgroundColor = ColorOf(reading.Background).ToString(),
                primaryHitGuard = NativeComputedStyleAdapter.For(reading)?.RequiresHitTestBackground,
                nativeRoot = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(reading, 0).GetType().Name,
                rootBackground = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(reading, 0) is ContentPresenter cp ? cp.Background?.GetType().Name : null,
                bounds = reading.TransformToVisual(null).TransformBounds(new(0, 0, reading.ActualWidth, reading.ActualHeight)),
                visible = WidgetViewPresenter.VisibleAnchorBounds(reading, page), labelVisible = Find<TextBlock>("Widget.preview.source.label", root)?.IsLoaded };
            Check(reading.ActualWidth > 0 && reading.ActualHeight > 0 && Find<TextBlock>("Widget.preview.source.label", root) is { IsLoaded: true },
                "Primary reading source realizes its inherited content template and nonempty native hit rectangle");
            reading.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => readingPresses++), true);
            reading.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => readingReleases++), true);
            reading.AddHandler(UIElement.TappedEvent, new TappedEventHandler((_, _) => readingTaps++), true);
            Find<Button>("Widget.preview.neighbor", root)!.Focus(FocusState.Keyboard);
            await NativeContextProbeInput.LeftClickAsync(reading, new(reading.ActualWidth / 2, reading.ActualHeight / 2));
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.preview.apply", root)));
            Check(readingTaps == 1 && primary == 1, "Actionless reading source click enters before the 1500ms hover delay without prior focus");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), reading), "B returns to exact actionless source");
            NativeContextProbeInput.PressKey(Windows.System.VirtualKey.Enter);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.preview.apply", root)));
            Check(primary == 1, "Keyboard Enter enters the same actionless preview without a widget command");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(reading)!;
            Check(peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider && peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider,
                "Primary reading source exposes native Invoke and ExpandCollapse patterns");
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.preview.apply", root)));
            Check(((IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)).ExpandCollapseState == ExpandCollapseState.Expanded,
                "UIA Invoke enters the same primary preview and reports expanded");
            ((IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)).Collapse();
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), reading), "UIA Collapse returns to source");
            enterButton = ControllerButton.RightStick; Publish();
            Check(peer.GetPattern(PatternInterface.Invoke) is null && peer.GetPattern(PatternInterface.ExpandCollapse) is null,
                "Ordinary reading surfaces expose no primary Invoke or ExpandCollapse after opt-in is removed");
            enterButton = ControllerButton.A; passiveFocus = false; Publish(); reading.Focus(FocusState.Keyboard);
            await Wait(() => !page.IsFocusPreviewOpen);
            Check(page.CaptureControllerGuide().Any(hint => hint.Button == ControllerButton.A && hint.Label == "Interact"),
                "Primary entry remains discoverable when passive focus presentation is disabled");
            await page.HandleControllerButtonAsync(ControllerButton.A);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.preview.apply", root)));
            Check(primary == 1, "A explicitly enters a hover-only reading preview without a domain action");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            passiveFocus = true; passiveHover = false; Publish();
            Find<Button>("Widget.preview.neighbor", root)!.Focus(FocusState.Keyboard);
            await NativeContextProbeInput.LeftClickAsync(reading, new(reading.ActualWidth / 2, reading.ActualHeight / 2));
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.preview.apply", root)));
            Check(primary == 1, "Native click explicitly enters when passive hover presentation is disabled");
            var outside = new Button { Content = "Outside widget", Width = 130, Height = 40,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new(20, 10, 0, 0) };
            var outsideClicks = 0; outside.Click += (_, _) => ++outsideClicks; root.Children.Add(outside);
            await Wait(() => outside.IsLoaded && outside.ActualWidth > 0);
            await NativeContextProbeInput.LeftClickAsync(outside, new(65, 20));
            await Wait(() => !page.IsFocusPreviewOpen && outsideClicks == 1);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), outside), "Outside click closes preview without stealing focus or swallowing its action");
            await NativeContextProbeInput.LeftClickAsync(reading, new(reading.ActualWidth / 2, reading.ActualHeight / 2));
            await Wait(() => page.IsFocusPreviewOpen && !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), reading));
            var blank = new Border { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), Width = 130, Height = 40,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 80, 0, 0) };
            var blankPresses = 0;
            blank.PointerPressed += (_, args) => { ++blankPresses; args.Handled = true; };
            root.Children.Add(blank);
            await Wait(() => blank.IsLoaded && blank.ActualWidth > 0);
            await NativeContextProbeInput.LeftClickAsync(blank, new(65, 20));
            await Wait(() => !page.IsFocusPreviewOpen);
            await Task.Delay(250);
            Check(!page.IsFocusPreviewOpen && blankPresses == 1, "Handled background press closes preview without consuming or replaying the existing press");
            passiveHover = true; passiveFocus = false; Publish(); outside.Focus(FocusState.Keyboard);
            observeHover = true;
            for (var cycle = 0; cycle < 4; ++cycle)
            {
                // Outside dismissal intentionally stays closed if the cursor is
                // already over the opener; exercise a real leave-and-return.
                await NativeContextProbeInput.HoverAsync(outside, async () => await Wait(() => !page.IsFocusPreviewOpen));
                await NativeContextProbeInput.HoverAsync(reading, async () =>
                {
                    try { await Wait(() => page.IsFocusPreviewOpen); }
                    catch
                    {
                        hoverFailure = new { cycle, pointer = NativeContextProbeInput.LastPointerTrace, preview = page.FocusPreviewDiagnostics(),
                            sameSource = ReferenceEquals(reading, Find<ContentControl>("Widget.preview.source", root)), reading.IsLoaded, reading.IsHitTestVisible };
                        throw;
                    }
                });
            }
            Check(true, "Standalone primary preview repeatedly reopens after hovering a non-preview control");
            Check(sourceHoverMoves>0, "Native hover reaches the reading source through its actual window coordinates");
            await NativeContextProbeInput.HoverAsync(outside, async () => await Wait(() => !page.IsFocusPreviewOpen));
            await Task.Delay(250);
            Check(!page.IsFocusPreviewOpen, "Moving onto overlapping sibling chrome dismisses without reopening from the retired pointer source");
            passiveFocus = true; Publish();
            root.Children.Remove(outside); root.Children.Remove(blank);
            await NativeContextProbeInput.LeftClickAsync(reading, new(reading.ActualWidth / 2, reading.ActualHeight / 2));
            await Wait(() => page.IsFocusPreviewOpen);
            sourceVisible = false; Publish(); await Wait(() => !page.IsFocusPreviewOpen && !reading.IsLoaded);
            Check(!reading.IsLoaded, "Removing a primary preview source retires its native activation target");
            await page.SetPresentationActiveAsync(false); Check(!page.IsFocusPreviewOpen, "Suspension revokes the preview");
            foreach (var container in new Microsoft.UI.Xaml.Controls.Primitives.SelectorItem[] { new GridViewItem(), new ListViewItem() })
            {
                var text = new TextBlock { Text = "Collection typography" };
                container.Content = text; container.Width = 280; container.Height = 50;
                root.Children.Add(container);
                await Wait(() => text.IsLoaded);
                using var style = new NativeComputedStyleAdapter(container);
                style.InheritedFontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI");
                style.Update(FontStyles(null));
                Check(text.FontFamily.Source == "Segoe UI", $"{container.GetType().Name}: unstyled item inherits the collection font instead of the native item default");
                style.UpdateInheritedFontFamily(new("Consolas"));
                Check(text.FontFamily.Source == "Consolas", $"{container.GetType().Name}: inherited font updates without rebuilding the row");
                style.Update(FontStyles("Georgia"));
                style.UpdateInheritedFontFamily(new("Arial"));
                Check(text.FontFamily.Source == "Georgia", $"{container.GetType().Name}: explicit widget font wins over inherited font changes");
                style.Update(FontStyles(null));
                Check(text.FontFamily.Source == "Arial", $"{container.GetType().Name}: removing explicit font restores the latest collection font");
                var size = new Windows.Foundation.Size(container.ActualWidth, container.ActualHeight);
                try
                {
                    NativeComputedStyleAdapter.SetAccessibilityPolicy(appearance with { InterfaceScale = .9 });
                    style.RefreshTextScale();
                    Check(container.RasterizationScale == 1.5, $"{container.GetType().Name}: zoomed item uses bounded XAML rasterization");
                    Check(container.ActualWidth == size.Width && container.ActualHeight == size.Height,
                        $"{container.GetType().Name}: raster quality does not change layout or hit geometry");
                    style.Dispose();
                    Check(container.RasterizationScale == 1, $"{container.GetType().Name}: retiring the style restores native raster quality");
                }
                finally { NativeComputedStyleAdapter.SetAccessibilityPolicy(appearance); }
                root.Children.Remove(container);
            }
            static BridgeNodeRenderStyles FontStyles(string? family)
            {
                var values = new Dictionary<string, BridgeComputedStyleValue>();
                if (family is not null) values["font-family"] = new() { Kind = WrssValueKind.FontFamily, Text = family };
                return new() { Base = values, Focused = values, Pressed = values };
            }
            }
            if (failure is not null) throw failure;
        }
        catch (Exception error)
        {
            failure = error;
            failedState = new { preview = page.FocusPreviewDiagnostics(), primary, applied, readingState, hoverFailure, hoverRoute, presenterExits,
                pointer = NativeContextProbeInput.LastPointerTrace, readingTaps, readingPresses, readingReleases, focused =
                FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(focused) : null };
        }
        finally { page.PointerExited -= exited; inputRoot?.RemoveHandler(UIElement.PointerMovedEvent, moved); root.RemoveHandler(UIElement.PointerMovedEvent, moved); await page.DisposeAsync(); host.Children.Remove(root); }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, failedState, presenterExits, error = failure?.ToString() }));
        if (failure is not null) throw failure;

        static IEnumerable<UIElement> WalkPointerTree(DependencyObject node)
        {
            if (node is UIElement element) yield return element;
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var child in WalkPointerTree(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i))) yield return child;
        }

        void Publish()
        {
            var content = UI.Stack("preview.content", UI.Text("Item details", "preview.summary"),
                    UI.Button("Apply", "apply", "preview.apply").Busy(busy),
                    UI.Select("Option", (SelectOption[])[new("one", "One", "one", selected == "one"), new("two", "Two", "two", selected == "two")], "preview.select"),
                    UI.Slider(volume, 0, 100, 5, "volume", "preview.volume", "Volume").RequireControllerActivation());
            WidgetElement source = readingSource
                ? UI.ReadingSurface("preview.source", "Filters and view", ActionSurfaceOrientation.Horizontal, UI.Text("Filters and view", "preview.source.label"))
                    .Classes("preview-source-row").PresentOnFocus(content)
                : UI.Button("Choose item", "choose", "preview.source").Classes("preview-source-row").PresentOnFocus(content)
                    .TransitionLayout("preview-source", "one", 0);
            var view = new WidgetView(UI.Stack("preview.page",
                UI.FocusPresentationSurface(UI.Stack("preview.sources", sourceVisible ? source : UI.Text("Removed", "preview.removed"),
                    UI.Button("Neighbor", "choose", "preview.neighbor").Classes("preview-source-row")
                        .PresentOnFocus(UI.Text("Neighbor preview", "preview.neighbor-content"))),
                    UI.Text("", "preview.empty"), "preview.surface") with { Preview = new(360, 320, centeredOnly?AnchorPlacement.CenterOnAnchor:AnchorPlacement.Auto, OnFocus: passiveFocus, OnHover: passiveHover, DelayMilliseconds: delay) { EnterButton = enterButton, SizeToContentHeight = true } })
                .InputScope("preview.page"), sourceVisible ? "preview.source" : null);
            var snapshot = view.CreateSnapshot("interactive-preview-fixture", ++sequence);
            var styles = new Dictionary<string, BridgeNodeRenderStyles>();
            void Visit(ViewNode node) { styles[node.Id] = Compute(theme, node); foreach (var child in node.Children) Visit(child);
                if (node.FocusPresentation is { } content) Visit(content); if (node.DefaultFocusPresentation is { } empty) Visit(empty); }
            Visit(snapshot.Root); foreach (var role in new[] { "body", "canvas", "panel", "preview", "hint", "tray-item" }) styles[role] = Compute(theme, role, role, []);
            popup.Update(styles, appearance);
            var descriptor = new BridgeWidgetDescriptor { Id = "preview", Name = "Interactive preview", InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = "interactive-preview", PresentationGeneration = "interactive-preview", Icon = WidgetGlyph.Settings, PackageContentDigest = "" };
            page.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1, snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
            page.UpdateLayout();
        }
    }
}
