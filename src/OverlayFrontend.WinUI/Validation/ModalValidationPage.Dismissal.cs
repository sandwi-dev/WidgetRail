using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class ModalValidationPage
{
    private async Task CheckDismissalInputAsync()
    {
        var priorDispatch = presenter.DispatchActionAsync;
        var chrome = new Button { Content = "Shell chrome", HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top };
        ((Grid)Content).Children.Add(chrome);
        var dismissals = 0;
        var generation = 0;
        var chromeEscapes = 0;
        chrome.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Escape) { chromeEscapes++; args.Handled = true; }
        };
        var pointerTrace = new List<string>();
        presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        presenter.DispatchActionAsync = request =>
        {
            actions.Add(request);
            if (request.Action.ActionId == "dismiss") { ++dismissals; modal = null; Apply(); }
            return Task.CompletedTask;
        };
        async Task<WidgetModalLayer> Open()
        {
            modal = "native.details." + ++generation;
            Apply();
            await Until(() => FocusedId == "Widget.play");
            var layer = AncestorLayer(Find(modal)!);
            layer.PointerTrace = text => { if (pointerTrace.Count < 80) pointerTrace.Add(text); };
            return layer;
        }
        static Point Outside(WidgetModalLayer layer) => new(8, layer.ActualHeight / 2);
        try
        {
            await Open();
            NativeContextProbeInput.Escape();
            await Until(() => modal is null);
            Check(dismissals == 1, "Native Escape dismisses modal from its focused control exactly once");

            await Open();
            Check(chrome.Focus(FocusState.Keyboard), "Sibling shell chrome can own native focus while modal remains displayed");
            NativeContextProbeInput.Escape();
            await Until(() => modal is null);
            Check(dismissals == 2, "Native Escape reaches the modal from sibling shell chrome through its root owner");
            Check(chromeEscapes == 0, "Modal Escape takes precedence over sibling shell preview-key actions");

            var layer = await Open();
            await NativeContextProbeInput.LeftClickAsync(layer, Outside(layer));
            await Until(() => modal is null);
            Check(dismissals == 3, "Native primary backdrop click dismisses modal without invoking its background");

            layer = await Open();
            var outside = Outside(layer);
            pointerTrace.Clear();
            await NativeContextProbeInput.PointerGestureAsync(layer, outside, new(outside.X, outside.Y + 16), returnToStart: true);
            if (modal is null || dismissals != 3) throw new InvalidOperationException("Backdrop drag returning to its starting point does not dismiss: " + string.Join("; ", pointerTrace));
            Check(true, "Backdrop drag returning to its starting point does not dismiss");
            var panel = Find(modal!)!;
            var inside = panel.TransformToVisual(layer).TransformPoint(new(panel.ActualWidth - 8, 8));
            await NativeContextProbeInput.PointerGestureAsync(layer, inside, outside);
            Check(modal is not null && dismissals == 3, "Dragging from modal content onto backdrop does not dismiss");

            await NativeContextProbeInput.LeftClickAsync(layer, outside, () => { ++owner; Apply(); });
            Check(modal is not null && dismissals == 3, "Pointer release cannot dismiss a replacement runtime after backdrop press");

            // Real flyout input owns the first Escape/outside click; the same
            // native event must never fall through and dismiss the parent too.
            layer = await Open();
            var picker = (Button)Find("picker")!;
            await NativeContextProbeInput.LeftClickAsync(picker, new(picker.ActualWidth / 2, picker.ActualHeight / 2));
            await Until(() => presenter.HasTransientControl);
            NativeContextProbeInput.Escape();
            await Until(() => !presenter.HasTransientControl);
            Check(modal is not null && dismissals == 3, "Native Escape closes an inner Select without dismissing its modal");

            await NativeContextProbeInput.LeftClickAsync(picker, new(picker.ActualWidth / 2, picker.ActualHeight / 2));
            await Until(() => presenter.HasTransientControl);
            await NativeContextProbeInput.LeftClickAsync(layer, Outside(layer));
            await Until(() => !presenter.HasTransientControl);
            Check(modal is not null && dismissals == 3, "Native outside click closes an inner Select without dismissing its modal");
            await NativeContextProbeInput.LeftClickAsync(layer, Outside(layer));
            await Until(() => modal is null);
            Check(dismissals == 4, "A subsequent backdrop click dismisses after the child popup has retired");

            dismissOnBackdrop = false;
            layer = await Open();
            await NativeContextProbeInput.LeftClickAsync(layer, Outside(layer));
            Check(modal is not null && dismissals == 4, "Explicit blocking backdrop policy preserves the dialog");
            NativeContextProbeInput.Escape();
            await Until(() => modal is null);
            Check(dismissals == 5, "Backdrop opt-out preserves native Escape cancellation");

            dismissOnBackdrop = true;
            await Open();
        }
        finally
        {
            ((Grid)Content).Children.Remove(chrome);
            dismissOnBackdrop = true;
            presenter.DispatchActionAsync = priorDispatch;
            presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full }, true);
        }
    }
}
