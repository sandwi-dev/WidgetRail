using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeAppearancePublicationAsync()
    {
        var root = new ViewNode { Id = "appearance.root", Kind = ViewNodeKind.Row, Children =
            new[] { "a", "b", "c" }.Select(id => new ViewNode
                { Id = "appearance." + id, Kind = ViewNodeKind.Button, Text = id.ToUpperInvariant(), ActionId = id }).ToArray() };
        var initial = CreateFrame(root, new Dictionary<string, BridgeNodeRenderStyles>());
        presenter.Apply(initial);
        await Wait(() => Find<Button>("Widget.appearance.b") is { IsLoaded: true });
        var b = Find<Button>("Widget.appearance.b")!;
        var c = Find<Button>("Widget.appearance.c")!;
        b.Focus(FocusState.Keyboard);
        await Wait(() => b.FocusState != FocusState.Unfocused);
        var reordered = Reordered();
        presenter.Apply(reordered);
        var nativeFocusMoved = b.FocusState == FocusState.Unfocused;
        // Deliberately no dispatcher yield between structural mutation and theme.
        var styled = Theme(reordered);
        presenter.Apply(styled);
        await Wait(() => b.FocusState != FocusState.Unfocused);
        Check(nativeFocusMoved, "structural reorder exercises displaced native focus before the low-priority restore");
        Check(ReferenceEquals(b, Find<Button>("Widget.appearance.b")), "appearance publication preserves the structural focus target and native identity");
        await Wait(() => b.ActualHeight >= 72);
        Check(b.Height == 72, "appearance-only geometry changes still perform native layout");

        // Reestablish the original order, then let a genuine native focus choice
        // supersede the pending structural restore before the theme is applied.
        presenter.Apply(CreateFrame(root, new Dictionary<string, BridgeNodeRenderStyles>()));
        b.Focus(FocusState.Keyboard);
        await Task.Delay(30);
        reordered = Reordered();
        presenter.Apply(reordered);
        c.Focus(FocusState.Keyboard);
        presenter.Apply(Theme(reordered));
        await Task.Delay(80);
        Check(c.FocusState != FocusState.Unfocused, "new native focus choice before appearance publication supersedes structural restoration");

        presenter.Apply(CreateFrame(root, new Dictionary<string, BridgeNodeRenderStyles>()));
        b.Focus(FocusState.Keyboard);
        await Task.Delay(30);
        reordered = Reordered();
        presenter.Apply(reordered);
        presenter.Apply(Theme(reordered));
        c.Focus(FocusState.Keyboard);
        await Task.Delay(80);
        Check(c.FocusState != FocusState.Unfocused, "new native focus choice after appearance publication supersedes structural restoration");

        WidgetPresentationFrame Reordered() => CreateFrame(root with { Children = [root.Children[1], root.Children[0], root.Children[2]] }, new Dictionary<string, BridgeNodeRenderStyles>());
        static WidgetPresentationFrame Theme(WidgetPresentationFrame frame) => frame with
        {
            AppearanceRevision = frame.AppearanceRevision + 1,
            RenderStyles = new Dictionary<string, BridgeNodeRenderStyles>
            {
                ["appearance.b"] = Compute("button { height: 72px; color: #abcdef; }", "appearance.b", "button"),
            },
        };
    }
}
