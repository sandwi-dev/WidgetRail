using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
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
    private async Task NativeTogglesAsync()
    {
        var styles = new Dictionary<string, BridgeNodeRenderStyles>();
        var node = new WidgetView(UI.Switch("Example setting", true, "toggle", "toggle")).CreateSnapshot("toggle-fixture", 1).Root;
        ViewNode Root(ViewNode value) => new() { Id = "toggles", Kind = ViewNodeKind.Stack, Children = new[] { value } };
        var before = actions;
        presenter.Apply(CreateFrame(Root(node), styles));
        await Wait(() => Find<ToggleSwitch>("Widget.toggle") is { IsLoaded: true });
        var toggle = Find<ToggleSwitch>("Widget.toggle")!;
        Check(toggle.IsOn && Equals(toggle.Header, "Example setting") && actions == before, "initial on publication creates a native toggle without dispatch");
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(toggle);
        var provider = peer.GetPattern(PatternInterface.Toggle) as IToggleProvider;
        Check(provider is not null && provider.ToggleState == ToggleState.On && peer.GetPattern(PatternInterface.Invoke) is null,
            "stock ToggleSwitch exposes Toggle semantics and authored state");
        Check(peer.GetName() == "Example setting" && peer.GetItemStatus() == string.Empty, "accessible name and native state are not duplicated as selection");
        toggle.Focus(FocusState.Keyboard);
        node = node with { IsSelected = false, Text = "Example setting  Off" };
        presenter.Apply(CreateFrame(Root(node), styles));
        Check(ReferenceEquals(toggle, Find<ToggleSwitch>("Widget.toggle")) && !toggle.IsOn && actions == before &&
            ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), toggle), "remote state update retains native identity and focus without dispatch");
        await presenter.HandleControllerButtonAsync(ControllerButton.A);
        await Wait(() => actions == before + 1);
        await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Repeated);
        await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
        Check(toggle.IsOn && actions == before + 1, "controller A dispatches once; hold/release do not toggle again");
        node = node with { IsSelected = true, Text = "Example setting  On" };
        presenter.Apply(CreateFrame(Root(node), styles));
        Check(actions == before + 1 && toggle.IsOn, "acknowledged on state is not another user action");
        provider!.Toggle();
        await Wait(() => actions == before + 2);
        Check(!toggle.IsOn, "native automation toggles through the same action path");
        presenter.Apply(CreateFrame(Root(node with { IsBusy = true }), styles));
        provider.Toggle();
        Check(actions == before + 2 && toggle.IsOn && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), toggle),
            "busy toggle remains focused and rejects user changes without feedback dispatch");
        presenter.Apply(CreateFrame(Root(node with { IsDisabled = true }), styles));
        toggle.IsOn = false;
        Check(!toggle.IsEnabled && toggle.IsOn && actions == before + 2, "disabled toggle cannot mutate or dispatch");
        presenter.Apply(CreateFrame(Root(node), styles));
        presenter.SetPresentationInputEnabled(false);
        provider.Toggle();
        Check(toggle.IsOn && actions == before + 2, "passive presentation rejects native activation and restores published state");
        presenter.SetPresentationInputEnabled(true);
        var savedDispatch = presenter.DispatchActionAsync;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        presenter.DispatchActionAsync = async _ => { ++actions; await completion.Task; };
        toggle.Focus(FocusState.Keyboard);
        provider.Toggle();
        await Wait(() => actions == before + 3);
        presenter.Apply(CreateFrame(Root(node with { IsBusy = true }), styles));
        Check(ReferenceEquals(toggle, Find<ToggleSwitch>("Widget.toggle")) && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), toggle),
            "pending action and intermediate busy snapshot retain toggle focus");
        completion.SetResult();
        await Task.Delay(30);
        presenter.DispatchActionAsync = savedDispatch;
        presenter.Apply(CreateFrame(Root(node), styles));
        Check(actions == before + 3, "completion and publication do not synthesize a second toggle");
        before++;
        var catalog = new ThemeCatalog(new PlatformSettingsPaths(Path.Combine(Path.GetTempPath(), "wrail-toggle-themes")));
        foreach (var themeEntry in catalog.BuiltInThemes)
        {
            var theme = ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package, new([], []), themeEntry.Package).Theme!;
            styles[node.Id] = Compute(theme, node.Id, "button", ["wrail-switch", "wrail-switch--on"], WrssPseudoState.Selected);
            presenter.Apply(CreateFrame(Root(node), styles));
            await Task.Delay(30);
            Check(ColorOf(toggle.Resources["ToggleSwitchFillOn"] as Microsoft.UI.Xaml.Media.Brush) !=
                ColorOf(toggle.Resources["ToggleSwitchKnobFillOn"] as Microsoft.UI.Xaml.Media.Brush),
                themeEntry.Descriptor.Id + ": native toggle track and thumb retain contrast");
            Check(toggle.Resources["ToggleSwitchFillOffPointerOver"] is Windows.UI.Color &&
                toggle.Resources["ToggleSwitchStrokeOffDisabled"] is Windows.UI.Color,
                themeEntry.Descriptor.Id + ": animated Off resources use native Color types");
            foreach (var state in new[] { "PointerOver", "Pressed", "Normal" })
                Check(VisualStateManager.GoToState(toggle, state, false), themeEntry.Descriptor.Id + ": stock " + state + " state remains available");
            Check(ReferenceEquals(toggle, Find<ToggleSwitch>("Widget.toggle")) && actions == before + 2,
                themeEntry.Descriptor.Id + ": theme change retains control and never invokes it");
        }
        WidgetViewPresenter.SetHighContrastStyleOverride(true);
        await Task.Delay(40);
        Check(ColorOf(toggle.Resources["ToggleSwitchFillOn"] as Microsoft.UI.Xaml.Media.Brush) !=
            ColorOf(toggle.Resources["ToggleSwitchKnobFillOn"] as Microsoft.UI.Xaml.Media.Brush), "high contrast retains distinct toggle track and thumb");
        WidgetViewPresenter.SetHighContrastStyleOverride(null);
        presenter.Apply(CreateFrame(Root(node with { StyleClasses = [], IsSelected = true }), styles));
        Check(Find<Button>("Widget.toggle") is not null, "changing semantic control type retires the toggle instead of reusing the wrong native class");
        toggle.IsOn = false;
        Check(actions == before + 2, "retired native toggle cannot dispatch into a replacement command");
    }
}
