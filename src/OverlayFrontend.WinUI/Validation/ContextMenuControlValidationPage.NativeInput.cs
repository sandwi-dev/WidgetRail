using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class ContextMenuControlValidationPage
{
    private async Task NativeDesktopContextsAsync()
    {
        Check(WidgetViewPresenter.IsNativeContextKey(Windows.System.VirtualKey.Application, false) &&
            WidgetViewPresenter.IsNativeContextKey(Windows.System.VirtualKey.F10, true) &&
            !WidgetViewPresenter.IsNativeContextKey(Windows.System.VirtualKey.F10, false), "Only Apps or Shift+F10 request keyboard context");
        var before = actions.Count;
        await FocusAsync("poster");
        var clicked = (Control)Find(presenter, "Widget.default")!;
        await NativeContextProbeInput.RightClickAsync(clicked);
        await WaitAsync(() => presenter.HasTransientControl && FocusedId.StartsWith("Widget.default.Context.", StringComparison.Ordinal));
        Check(actions.Count == before, "Native right click targets the clicked surface without invoking its primary action");
        NativeContextProbeInput.Escape();
        await WaitAsync(() => !presenter.HasTransientControl && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), clicked));
        Check(true, "Right-click dismissal returns to the clicked surface rather than the old focused surface");
        foreach (var shiftF10 in new[] { true, false })
        {
            NativeContextProbeInput.Keyboard(shiftF10);
            await WaitAsync(() => presenter.HasTransientControl && FocusedId.StartsWith("Widget.default.Context.", StringComparison.Ordinal));
            Check(actions.Count == before, (shiftF10 ? "Shift+F10" : "Apps key") + " opens declared context without primary activation");
            presenter.DismissTransientControl();
            await WaitAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), clicked));
        }
        NativeContextProbeInput.Keyboard(shiftF10: true);
        await WaitAsync(() => presenter.HasTransientControl && FocusedId.Contains(".Context.", StringComparison.Ordinal));
        await presenter.HandleControllerButtonAsync(ControllerButton.A);
        await WaitAsync(() => actions.Count == before + 1);
        Check(actions[^1].Action.SourceElementId == "default" && actions[^1].Action.ActionId == "play.changed", "Keyboard-opened context retains exact declared owner and invokes only the chosen action");
        await WaitAsync(() => !presenter.HasTransientControl);
        ownerBusy = true; Apply();
        await NativeContextProbeInput.RightClickAsync((Control)Find(presenter, "Widget.default")!);
        await Task.Delay(100);
        Check(!presenter.HasTransientControl && actions.Count == before + 1, "Busy ordinary surface cannot open or execute a native context menu");
        ownerBusy = false; Apply();
        await FocusAsync("default"); NativeContextProbeInput.Keyboard(shiftF10: true);
        await WaitAsync(() => presenter.HasTransientControl);
        presenter.SetPresentationInputEnabled(false);
        Check(!presenter.HasTransientControl, "Withdrawing input revokes a keyboard-opened context menu");
        presenter.SetPresentationInputEnabled(true);
        await FocusAsync("default");
        presenter.ActivateFocused();
        await WaitAsync(() => actions.Count == before + 2);
        Check(actions[^1].Action.ActionId == "open", "Primary A activation still works after native context interactions");
    }
}
