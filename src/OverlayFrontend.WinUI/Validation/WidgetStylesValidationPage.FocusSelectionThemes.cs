using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task FocusAndSelectionThemesAsync()
    {
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(Path.Combine(Path.GetTempPath(), "wrail-native-focus-theme-check")));
        var stop = new ViewNode { Id = "theme.stop", Kind = ViewNodeKind.Button, Text = "Stop", ActionId = "stop" };
        var device = new ViewNode { Id = "theme.device", Kind = ViewNodeKind.Button, Text = "WidgetRail", ActionId = "device", IsSelected = true };
        var root = new ViewNode { Id = "theme.rows", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[stop, device] };
        foreach (var entry in catalog.BuiltInThemes)
        {
            var compiled = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new([], []), entry.Package);
            var theme = compiled.Theme ?? throw new InvalidOperationException("Invalid theme " + entry.Descriptor.Id);
            presenter.Apply(CreateFrame(root, new Dictionary<string, BridgeNodeRenderStyles>
            {
                [stop.Id] = Compute(theme, stop.Id, "button", ["wrail-settings-row__action"]),
                [device.Id] = Compute(theme, device.Id, "button", ["wrail-choice-row", "wrail-choice-row--selected"], WrssPseudoState.Selected),
            }));
            await Wait(() => Find<Button>("Widget.theme.stop") is { IsLoaded: true });
            var action = Find<Button>("Widget.theme.stop")!;
            var selected = Find<Button>("Widget.theme.device")!;
            var selectedPeer = FrameworkElementAutomationPeer.CreatePeerForElement(selected);
            Check(selectedPeer.GetItemStatus() == "Selected" &&
                selectedPeer.GetPattern(PatternInterface.Invoke) is not null &&
                selectedPeer.GetPattern(PatternInterface.Toggle) is null,
                entry.Descriptor.Id + " authored selection is accessible without changing command semantics");
            Check(FrameworkElementAutomationPeer.CreatePeerForElement(action).GetItemStatus() == string.Empty,
                entry.Descriptor.Id + " unselected command has no selected accessibility status");
            action.Focus(FocusState.Keyboard);
            await Wait(() => action.FocusState == FocusState.Keyboard && NativeComputedStyleAdapter.For(action)?.Interaction.Focused == true);
            var selectedAlpha = Alpha(selected.Background);
            Check(Alpha(action.Background) == 0 && selectedAlpha > 0,
                entry.Descriptor.Id + " native SettingsRow action stays transparent beside a selected choice");
            selected.Focus(FocusState.Keyboard);
            await Wait(() => selected.FocusState == FocusState.Keyboard && NativeComputedStyleAdapter.For(selected)?.Interaction.Focused == true);
            Check(Alpha(selected.Background) == selectedAlpha && Alpha(action.Background) == 0,
                entry.Descriptor.Id + " focus moves without stealing or clearing the selected choice fill");
        }
        var selectedCommand = Find<Button>("Widget.theme.device")!;
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(selectedCommand);
        foreach (var state in new[] { (Selected: true, Busy: true, Status: "Selected, Busy"),
            (Selected: false, Busy: true, Status: "Busy"), (Selected: false, Busy: false, Status: "") })
        {
            presenter.Apply(CreateFrame(root with { Children = new[] { stop,
                device with { IsSelected = state.Selected, IsBusy = state.Busy } } },
                new Dictionary<string, BridgeNodeRenderStyles>()));
            Check(ReferenceEquals(selectedCommand, Find<Button>("Widget.theme.device")) && peer.GetItemStatus() == state.Status,
                "native accessibility status follows selection and busy publications on the retained button");
        }
        outside.Focus(FocusState.Keyboard);

        static double Alpha(Brush? brush) => brush switch
        {
            SolidColorBrush solid => solid.Color.A * solid.Opacity,
            GradientBrush gradient when gradient.GradientStops.Count > 0 =>
                gradient.GradientStops.Min(stop => stop.Color.A) * gradient.Opacity,
            _ => 0,
        };
    }
}
