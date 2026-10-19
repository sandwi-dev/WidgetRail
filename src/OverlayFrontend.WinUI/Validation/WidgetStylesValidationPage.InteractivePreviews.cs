using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        Exception? failure = null;
        var root = new Grid { Width = 820, Height = 600 }; var page = new WidgetViewPresenter { Width = 800, Height = 580 };
        root.Children.Add(page); PresentationLayers.Attach(root); presenter.Visibility = Visibility.Collapsed; host.Children.Add(root);
        var popup = new NativePopupTheme(); popup.Attach(page);
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(directory));
        long sequence = 0; var sourceVisible = true; var busy = false; var primary = 0; var applied = 0;
        var selected = "one"; var delay = 0;
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Reduced };
        var materialStyle = WrssParser.Parse(Environment.GetCommandLineArgs().Contains("--interactive-preview-acrylic")
            ? "preview { background-material: acrylic; }" : "", "interactive-preview.wrss");
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
            var bounds = (chrome!.ActualWidth, chrome.ActualHeight);
            await page.HandleControllerButtonAsync(ControllerButton.RightStick);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), apply));
            Check(ReferenceEquals(page.FocusPreviewChrome, chrome) && ReferenceEquals(apply, Find<Button>("Widget.preview.apply", root)),
                "Entry preserves panel and action control instances");
            Check(bounds == (chrome.ActualWidth, chrome.ActualHeight) && sequence == 1 && applied == 0,
                "Entry changes no geometry and sends no widget action");
            busy = true; Publish(); await Task.Delay(80);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), apply) && apply.IsEnabled,
                "Busy updates retain focus and the original control");
            page.ActivateFocused(); Check(applied == 0, "Busy action does not dispatch");
            busy = false; Publish(); await Task.Delay(80);
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
            await page.SetPresentationActiveAsync(false); Check(!page.IsFocusPreviewOpen, "Suspension revokes the preview");
            if (failure is not null) throw failure;
        }
        catch (Exception error) { failure = error; }
        finally { await page.DisposeAsync(); host.Children.Remove(root); }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;

        void Publish()
        {
            var source = UI.Button("Choose item", "choose", "preview.source")
                .PresentOnFocus(UI.Stack("preview.content", UI.Text("Item details", "preview.summary"),
                    UI.Button("Apply", "apply", "preview.apply").Busy(busy),
                    UI.Select("Option", (SelectOption[])[new("one", "One", "one", selected == "one"), new("two", "Two", "two", selected == "two")], "preview.select"))).TransitionLayout("preview-source", "one", 0);
            var view = new WidgetView(UI.Stack("preview.page",
                UI.FocusPresentationSurface(UI.Stack("preview.sources", sourceVisible ? source : UI.Text("Removed", "preview.removed")),
                    UI.Text("", "preview.empty"), "preview.surface") with { Preview = new(360, 240, DelayMilliseconds: delay) { EnterButton = ControllerButton.RightStick, SizeToContentHeight = true } })
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
