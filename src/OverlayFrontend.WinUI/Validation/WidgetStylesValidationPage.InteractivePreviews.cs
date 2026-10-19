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
        long sequence = 0; var opened = false; var sourceVisible = true; var busy = false; var primary = 0; var applied = 0;
        var selected = "one";
        var appearance = AppearanceSettings.Default with { Motion = MotionPreference.Reduced };
        var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new([], []),
            catalog.Load("widgetrail.builtin.velvet-dusk", "1.0.0").Package).Theme!;
        page.ApplyAppearance(appearance, false); page.Failed = error => failure ??= error;
        page.DispatchActionAsync = request =>
        {
            switch (request.Action.ActionId)
            {
                case "enter": opened = true; break;
                case "back": opened = false; break;
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
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), source) && primary == 0 && !opened, "Opening an enterable preview never activates or takes source focus");
            Check(page.FocusPreviewChrome?.IsHitTestVisible == true, "Enterable preview accepts mouse entry");
            page.SetPreviewPointerTarget(source); page.SetPreviewPointerTarget(null); await Task.Delay(40);
            page.SetPreviewPointerTarget(source); await Task.Delay(180);
            Check(page.IsFocusPreviewOpen, "Returning across the hover gap cancels pending preview dismissal");
            page.ActivateFocused(); Check(primary == 1 && !opened, "A retains the source primary action");
            await page.HandleControllerButtonAsync(ControllerButton.RightStick);
            await Wait(() => opened && Find<Button>("Widget.preview.apply", root) is { IsLoaded: true });
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.preview.apply", root)));
            Check(!page.IsFocusPreviewOpen, "Interactive publication replaces the passive preview");
            Check(Descendants(root).OfType<WidgetModalLayer>().Single().Chrome.Visibility == Visibility.Collapsed, "Interactive preview has no dimmed backdrop");
            var apply = Find<Button>("Widget.preview.apply", root)!; busy = true; Publish();
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), apply) && apply.IsEnabled, "Pending preview action keeps the same focusable control");
            page.ActivateFocused(); Check(applied == 0, "Busy preview action suppresses duplicates"); busy = false; Publish();
            var select = Find<Button>("Widget.preview.select", root)!; select.Focus(FocusState.Keyboard); page.ActivateFocused();
            Check(page.HasTransientControl, "Nested selector opens within interactive preview");
            await page.HandleControllerButtonAsync(ControllerButton.B); Check(opened && !page.HasTransientControl, "First B closes the nested selector only");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            await Wait(() => !opened && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.preview.source", root)));
            await Wait(() => page.IsFocusPreviewOpen); Check(true, "Second B returns to the exact source and restores its passive preview");
            source = Find<Button>("Widget.preview.source", root)!; page.SetPreviewPointerTarget(source);
            await Wait(() => page.IsFocusPreviewOpen); await page.EnterPreviewFromPointerAsync();
            await Wait(() => opened); Check(primary == 1, "Mouse preview entry does not invoke the source primary action");
            sourceVisible = false; Publish();
            await Wait(() => !opened); Check(true, "Removing the exact source dismisses the interactive preview instead of centering stale controls");
            sourceVisible = true; Publish(); source = Find<Button>("Widget.preview.source", root)!; source.Focus(FocusState.Keyboard);
            await page.HandleControllerButtonAsync(ControllerButton.RightStick); await Wait(() => opened);
            await page.SetPresentationActiveAsync(false); Check(!page.IsFocusPreviewOpen, "Suspending the widget revokes preview presentation");
            if (failure is not null) throw failure;
        }
        catch (Exception error) { failure = error; }
        finally { await page.DisposeAsync(); host.Children.Remove(root); }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;

        void Publish()
        {
            var source = UI.Button("Choose item", "choose", "preview.source").Shortcut(ControllerButton.RightStick, "Interact", actionId: "enter")
                .PresentOnFocus(UI.Text("Item details", "preview.summary"));
            var view = new WidgetView(UI.Stack("preview.page",
                UI.FocusPresentationSurface(UI.Stack("preview.sources", sourceVisible ? source : UI.Text("Removed", "preview.removed")),
                    UI.Text("", "preview.empty"), "preview.surface") with { Preview = new(360, 240, DelayMilliseconds: 0) { EnterButton = ControllerButton.RightStick, SizeToContentHeight = true } })
                .InputScope("preview.page"), sourceVisible ? "preview.source" : null);
            if (opened) view = view.WithInteractivePreview(new("preview.interactive", "Item details",
                UI.Stack("preview.content", UI.Button("Apply", "apply", "preview.apply").Busy(busy),
                    UI.Select("Option", (SelectOption[])[new("one", "One", "one", selected == "one"), new("two", "Two", "two", selected == "two")], "preview.select")),
                "preview.apply", "back", new("preview.source")) { Width = 360, Height = 240 });
            var snapshot = view.CreateSnapshot("interactive-preview-fixture", ++sequence);
            var styles = new Dictionary<string, BridgeNodeRenderStyles>();
            void Visit(ViewNode node) { styles[node.Id] = Compute(theme, node); foreach (var child in node.Children) Visit(child);
                if (node.FocusPresentation is { } content) Visit(content); if (node.DefaultFocusPresentation is { } empty) Visit(empty); }
            Visit(snapshot.Root); foreach (var role in new[] { "body", "canvas", "panel", "hint", "tray-item" }) styles[role] = Compute(theme, role, role, []);
            popup.Update(styles, appearance);
            var descriptor = new BridgeWidgetDescriptor { Id = "preview", Name = "Interactive preview", InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = "interactive-preview", PresentationGeneration = "interactive-preview", Icon = WidgetGlyph.Settings, PackageContentDigest = "" };
            page.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1, snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
            page.UpdateLayout();
        }
    }
}
