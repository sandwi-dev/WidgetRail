using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task DisclosuresAsync(string output)
    {
        Exception? failure = null;
        var expanded = false;
        var innerExpanded = true;
        var disabled = false;
        var busy = false;
        var holdPublication = false;
        var include = true;
        var moveChildOutside = false;
        var leadingIcon = false;
        long focusRequest = 0;
        var dispatched = new List<string>();
        WidgetRail.WidgetStyling.WrssTheme? theme = null;
        var surface = (Grid)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" RequestedTheme=\"Dark\" Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\" Padding=\"24\" />");
        Content = null; surface.Children.Add(host); Content = surface;
        void Render()
        {
            var content = new List<WidgetElement> { UI.Button("Other action", "other", "disclosure.other") };
            if (moveChildOutside && !expanded) content.Add(UI.Button("Child action", "child", "disclosure.child"));
            if (include)
            {
                var disclosureHeader = UI.DisclosureHeader("disclosure.header", "Details", expanded, "disclosure.body", "expand", "collapse").Disabled(disabled).Busy(busy);
                content.Add(leadingIcon ? disclosureHeader.Icon(WidgetGlyph.Settings) : disclosureHeader);
                if (expanded) content.Add(UI.Stack("disclosure.body", UI.Button("Child action", "child", "disclosure.child"),
                    UI.Disclosure("inner", "Nested details", innerExpanded, "inner.expand", "inner.collapse",
                        () => (WidgetElement[])[UI.Button("Nested child", "nested-child", "disclosure.nested-child")])));
            }
            var root = new WidgetView(UI.Stack("disclosure.root", content.ToArray())).CreateSnapshot("fixture", 1).Root;
            var styles = new Dictionary<string, BridgeNodeRenderStyles>();
            if (theme is not null)
            {
                void Visit(ViewNode node) { styles[node.Id] = Compute(theme, node); foreach (var child in node.Children) Visit(child); }
                Visit(root);
            }
            var frame = CreateFrame(root, styles);
            presenter.Apply(frame with { Snapshot = frame.Snapshot with { InitialFocusId = "disclosure.other",
                FocusGroupEntryRequest = focusRequest == 0 ? null : new() { RequestId = focusRequest, GroupId = "disclosure.root", TargetId = "disclosure.other" } } });
        }
        Button Button(string id) => Find<Button>("Widget." + id, presenter) ?? throw new InvalidOperationException("Missing " + id);
        IExpandCollapseProvider Peer(string id) => (IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(Button(id))
            .GetPattern(PatternInterface.ExpandCollapse);
        bool Focused(string id) => Button(id).FocusState != FocusState.Unfocused;
        async Task Settled() => await Task.Delay(60);
        presenter.DispatchActionAsync = request =>
        {
            var action = request.Action.ActionId;
            dispatched.Add(action);
            if (action == "expand") expanded = true;
            else if (action == "collapse") expanded = false;
            else if (action == "inner.expand") innerExpanded = true;
            else if (action == "inner.collapse") innerExpanded = false;
            if (!holdPublication) Render();
            return Task.CompletedTask;
        };
        try
        {
            Render(); await Wait(() => Find<Button>("Widget.disclosure.header", presenter)?.IsLoaded == true);
            var header = Button("disclosure.header");
            var peer = Peer("disclosure.header");
            Check(peer.ExpandCollapseState == ExpandCollapseState.Collapsed, "UIA exposes authored collapsed state");
            peer.Collapse(); await Settled();
            Check(dispatched.Count == 0, "UIA Collapse is a no-op when already collapsed");
            header.Focus(FocusState.Keyboard);
            holdPublication = true;
            peer.Expand(); peer.Expand(); await Settled();
            Check(dispatched.SequenceEqual(new[] { "expand", "expand" }) && peer.ExpandCollapseState == ExpandCollapseState.Collapsed,
                "repeated UIA Expand requests remain explicit and cannot optimistically mutate host state");
            holdPublication = false; Render(); await Settled();
            Check(ReferenceEquals(header, Button("disclosure.header")) && peer.ExpandCollapseState == ExpandCollapseState.Expanded,
                "accepted expansion retains header/peer and exposes the new UIA state");
            Check(Focused("disclosure.header"), "expansion does not move focus into the content");
            var before = dispatched.Count;
            peer.Expand(); await Settled(); Check(dispatched.Count == before, "UIA Expand is a no-op when already expanded");
            Button("disclosure.child").Focus(FocusState.Keyboard); Render(); await Settled();
            Check(Focused("disclosure.child"), "unchanged expanded refresh preserves child focus");
            peer.Collapse(); await Wait(() => Focused("disclosure.header"));
            Check(peer.ExpandCollapseState == ExpandCollapseState.Collapsed && Find<Button>("Widget.disclosure.child", presenter) is null,
                "collapse removes its children and restores their focus to the stable header");
            expanded = true; Render(); await Settled();
            Button("disclosure.other").Focus(FocusState.Keyboard); peer.Collapse(); await Settled();
            Check(Focused("disclosure.other"), "collapse does not steal focus from outside its content");
            header.Focus(FocusState.Keyboard);
            await presenter.HandleControllerButtonAsync(ControllerButton.A); await Settled();
            Check(expanded && dispatched[^1] == "expand", "controller A uses the same explicit expansion action as UIA");
            before = dispatched.Count; disabled = true; Render(); peer.Collapse(); await Settled();
            Check(dispatched.Count == before && expanded, "disabled disclosure rejects UIA collapse");
            disabled = false; busy = true; Render(); peer.Collapse(); await Settled();
            Check(dispatched.Count == before && expanded, "busy disclosure rejects UIA collapse");
            busy = false; Render(); await Settled();
            Button("disclosure.nested-child").Focus(FocusState.Keyboard);
            Peer("inner.header").Collapse(); await Wait(() => Focused("inner.header"));
            Check(true, "nested collapse restores focus to the closest surviving header");
            innerExpanded = true; Render(); await Settled();
            Button("disclosure.nested-child").Focus(FocusState.Keyboard);
            peer.Collapse(); await Wait(() => Focused("disclosure.header"));
            Check(true, "outer collapse restores focus when the nested header is removed too");
            expanded = true; Render(); await Settled(); Button("disclosure.child").Focus(FocusState.Keyboard);
            expanded = false; focusRequest = 1; Render(); await Wait(() => Focused("disclosure.other"));
            Check(true, "new explicit focus request takes priority over collapse recovery");
            focusRequest = 0;
            expanded = true; Render(); await Settled(); Button("disclosure.child").Focus(FocusState.Keyboard);
            moveChildOutside = true; expanded = false; Render(); await Settled();
            Check(Focused("disclosure.child"), "collapse preserves the focused control when it survives outside the removed body");
            moveChildOutside = false; leadingIcon = true; Render(); await Settled();
            Check(VisualChildren(Button("disclosure.header")).OfType<FontIcon>().Any(icon => icon.Glyph == "\uE70D"),
                "a leading icon preserves the disclosure chevron in the native visual tree");
            peer.Expand(); await Settled();
            Check(VisualChildren(Button("disclosure.header")).OfType<FontIcon>().Any(icon => icon.Glyph == "\uE70E"),
                "the retained chevron updates after an icon-bearing disclosure expands");
            presenter.SetPresentationInputEnabled(false); presenter.SetAutomaticFocusEnabled(false);
            await presenter.SetPresentationActiveAsync(false); outside.Focus(FocusState.Keyboard);
            expanded = true; Render(); expanded = false; Render(); before = dispatched.Count; peer.Expand(); await Settled();
            Check(dispatched.Count == before && outside.FocusState != FocusState.Unfocused,
                "inactive publication and retained UIA peer neither activate nor steal focus");
            await presenter.SetPresentationActiveAsync(true); presenter.SetPresentationInputEnabled(true); presenter.SetAutomaticFocusEnabled(true);
            include = false; Render(); before = dispatched.Count; peer.Expand(); await Settled();
            Check(dispatched.Count == before, "retired disclosure peer cannot dispatch against a removed control");
            include = true; expanded = true; innerExpanded = true; Render(); await Settled();
            Button("disclosure.header").Focus(FocusState.Keyboard);
            await DisclosurePreviewAsync();
            var catalog = new WidgetRail.PlatformSettings.ThemeCatalog(new WidgetRail.PlatformSettings.PlatformSettingsPaths(Path.GetDirectoryName(output)!));
            foreach (var entry in catalog.BuiltInThemes)
            {
                theme = WidgetRail.PlatformSettings.ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new([], []), entry.Package).Theme!;
                Render(); await Settled();
                var themed = Button("disclosure.header");
                var chevron = VisualChildren(themed).OfType<FontIcon>().Single(icon => icon.Glyph == "\uE70E");
                Check(ColorOf(themed.Foreground).A > 0 && ColorOf(chevron.Foreground) == ColorOf(themed.Foreground) && chevron.ActualHeight > 0,
                    entry.Descriptor.Id + " disclosure chevron follows the native themed foreground");
            }
            WidgetRail.OverlayFrontend.WinUI.Presentation.WidgetViewPresenter.SetHighContrastStyleOverride(true);
            Render(); await Settled();
            Check(ColorOf(VisualChildren(Button("disclosure.header")).OfType<FontIcon>().Single(icon => icon.Glyph == "\uE70E").Foreground) ==
                ColorOf(Button("disclosure.header").Foreground), "high contrast keeps the disclosure chevron on the native control foreground");
            WidgetRail.OverlayFrontend.WinUI.Presentation.WidgetViewPresenter.SetHighContrastStyleOverride(null);
            theme = null; Render(); await Settled();
        }
        catch (Exception error) { failure = error; }
        await File.WriteAllTextAsync(output, System.Text.Json.JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;
    }
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); ++index)
            foreach (var child in VisualChildren(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private async Task DisclosurePreviewAsync()
    {
        var root = new Grid { Width = 680, Height = 340 };
        var page = new WidgetRail.OverlayFrontend.WinUI.Presentation.WidgetViewPresenter { Width = 660, Height = 320 };
        root.Children.Add(page);
        WidgetRail.OverlayFrontend.WinUI.Presentation.PresentationLayers.Attach(root);
        presenter.Visibility = Visibility.Collapsed; host.Children.Add(root);
        var expanded = true;
        void Publish()
        {
            var source = UI.Button("Preview source", "choose", "disclosure.preview.source").PresentOnFocus(
                UI.Disclosure("disclosure.preview", "Preview details", expanded, "preview.expand", "preview.collapse",
                    () => (WidgetElement[])[UI.Button("Preview child", "preview.child", "disclosure.preview.child")]));
            var view = new WidgetView(UI.Stack("disclosure.preview.page", UI.FocusPresentationSurface(source, UI.Text("", "preview.empty"), "preview.surface")
                with { Preview = new(300, 200, DelayMilliseconds: 0) { EnterButton = ControllerButton.X, SizeToContentHeight = true } }));
            var frame = CreateFrame(view.CreateSnapshot("preview", 1).Root, new Dictionary<string, BridgeNodeRenderStyles>());
            page.Apply(frame); page.UpdateLayout();
        }
        page.DispatchActionAsync = request => { if (request.Action.ActionId == "preview.collapse") expanded = false; Publish(); return Task.CompletedTask; };
        try
        {
            Publish(); await Wait(() => Find<Button>("Widget.disclosure.preview.source", root)?.IsLoaded == true);
            Find<Button>("Widget.disclosure.preview.source", root)!.Focus(FocusState.Keyboard);
            await Wait(() => page.IsFocusPreviewOpen && Find<Button>("Widget.disclosure.preview.header", root)?.IsLoaded == true);
            await page.HandleControllerButtonAsync(ControllerButton.X);
            var child = Find<Button>("Widget.disclosure.preview.child", root)!;
            child.Focus(FocusState.Keyboard);
            var header = Find<Button>("Widget.disclosure.preview.header", root)!;
            var peer = (IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(header).GetPattern(PatternInterface.ExpandCollapse);
            peer.Collapse();
            await Wait(() => header.FocusState != FocusState.Unfocused);
            Check(!expanded && page.IsFocusPreviewOpen && peer.ExpandCollapseState == ExpandCollapseState.Collapsed,
                "an entered interactive preview recovers collapsed-child focus inside the same preview");
            await page.HandleControllerButtonAsync(ControllerButton.B);
            Check(Find<Button>("Widget.disclosure.preview.source", root)!.FocusState != FocusState.Unfocused,
                "B after preview disclosure returns to its original source");
        }
        finally { host.Children.Remove(root); await page.DisposeAsync(); presenter.Visibility = Visibility.Visible; }
    }

}
