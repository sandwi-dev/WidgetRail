using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class SliderControlValidationPage
{
    private async Task FractionalVolumeAsync()
    {
        minimum = 0; maximum = 1; value = .8; step = .05;
        Apply(); await Task.Delay(200); Focus("adjust");
        var slider = (Slider)Find(presenter, "Widget.adjust")!;
        await presenter.HandleControllerButtonAsync(ControllerButton.A);
        Windows.Foundation.TypedEventHandler<UIElement, LosingFocusEventArgs> cancel = (_, args) => args.TryCancel();
        slider.LosingFocus += cancel;
        try { Focus("after"); }
        finally { slider.LosingFocus -= cancel; }
        Check(FocusId == "Widget.adjust" && slider.IsFocusEngaged,
            "cancelled focus transfer does not silently leave slider adjustment");
        var count = actions.Count;
        presenter.MoveFocus(FocusNavigationDirection.Right);
        await Until(() => actions.Count > count);
        Check(Math.Abs(slider.Value - .85) < 1e-9, "fractional volume advances exactly one step");
        presenter.MoveFocus(FocusNavigationDirection.Right);
        busy = true; Apply();
        Check(slider.IsFocusEngaged && Math.Abs(slider.Value - .9) < 1e-9,
            "busy publication preserves a newer unsent fractional adjustment");
        presenter.MoveFocus(FocusNavigationDirection.Right);
        Check(Math.Abs(slider.Value - .95) < 1e-9, "active volume adjustment can accumulate its latest value while busy");
        count = actions.Count;
        await presenter.HandleControllerButtonAsync(ControllerButton.A);
        Check(!slider.IsFocusEngaged && Math.Abs(slider.Value - .95) < 1e-9 && actions.Count == count,
            "Done keeps the final positive value without dispatching into a busy owner");
        busy = false; value = .85; Apply();
        await Until(() => actions.Count > count);
        Check(Math.Abs(actions[^1].Action.RequestedValue!.Value - .95) < 1e-9,
            "busy owner completion dispatches the latest volume value once");
        value = .95; Apply(); Focus("adjust");
        await presenter.HandleControllerButtonAsync(ControllerButton.A);
        presenter.MoveFocus(FocusNavigationDirection.Left);
        count = actions.Count;
        slider.RemoveFocusEngagement();
        Check(!slider.IsFocusEngaged && Math.Abs(slider.Value - .9) < 1e-9 && actions.Count == count + 1 &&
            Math.Abs(actions[^1].Action.RequestedValue!.Value - .9) < 1e-9,
            "native disengagement cannot publish its positive entry-value rollback as user input");
        foreach (var padding in new[] { new Thickness(0), new Thickness(10, 8, 10, 8) })
        foreach (var endpoint in new[] { 0d, .5, 1d })
        {
            presenter.ResetPressedStyles(); value = endpoint; volumeGeometry = true; paddedVolumeGeometry = padding.Left > 0; Apply();
            Focus("adjust"); await presenter.HandleControllerButtonAsync(ControllerButton.A);
            await Task.Delay(50); slider.UpdateLayout();
            var thumb = Part<Thumb>(slider, "HorizontalThumb")!;
            var template = Part<Grid>(slider, "HorizontalTemplate")!;
            var slot = LayoutInformation.GetLayoutSlot(template);
            Check(template.ActualHeight <= slot.Height + .01 && template.ActualWidth <= slot.Width + .01,
                $"native template fits its layout slot without an implicit clipping rectangle at {endpoint}, padding {padding.Left}");
            var bounds = thumb.TransformToVisual(slider).TransformBounds(new(0, 0, thumb.ActualWidth, thumb.ActualHeight));
            var margin = thumb.FocusVisualMargin;
            Check(bounds.Left + margin.Left >= -.01 && bounds.Right - margin.Right <= slider.ActualWidth + .01 &&
                bounds.Top + margin.Top >= -.01 && bounds.Bottom - margin.Bottom <= slider.ActualHeight + .01,
                $"selected thumb outline stays inside slider at {endpoint}, padding {padding.Left}: {bounds}, margin {margin}, size {slider.ActualWidth}x{slider.ActualHeight}");
            Check(Math.Abs(bounds.Top + bounds.Height / 2 - slider.ActualHeight / 2) < 1,
                $"selected thumb remains vertically centered at {endpoint}, padding {padding.Left}");
        }
        presenter.ResetPressedStyles(); volumeGeometry = false; Apply();
    }

    private static T? Part<T>(DependencyObject owner, string name) where T : FrameworkElement
    {
        if (owner is T value && value.Name == name) return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); ++i)
            if (Part<T>(VisualTreeHelper.GetChild(owner, i), name) is { } child) return child;
        return null;
    }
}
