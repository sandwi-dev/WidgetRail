using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private Binding? adjustingSlider;
    private bool sliderBackPending;
    private bool updatingSlider;
    private Slider CreateSlider(WidgetElementIdentity identity, object token)
    {
        var slider = new Slider { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 120,
            IsThumbToolTipEnabled = false };
        slider.ValueChanged += (_, args) =>
        {
            if (!applying && !updatingSlider) _ = CommitSliderAsync(identity, token, args.NewValue);
        };
        slider.LosingFocus += (_, args) =>
        {
            if (adjustingSlider?.Element == slider && FindBinding(args.NewFocusedElement) != adjustingSlider) adjustingSlider = null;
        };
        return slider;
    }
    private void UpdateSlider(Slider slider, ViewNode node)
    {
        updatingSlider = true;
        try
        {
            // Widen first: replacing e.g. 0..1 with 100..200 must not ask the
            // native RangeBase for a temporarily inverted range.
            slider.Maximum = Math.Max(slider.Maximum, node.Maximum!.Value);
            slider.Minimum = Math.Min(slider.Minimum, node.Minimum!.Value);
            slider.Maximum = node.Maximum!.Value;
            slider.Minimum = node.Minimum.Value;
            slider.StepFrequency = slider.SmallChange = node.Step!.Value;
            slider.LargeChange = Math.Min(node.Maximum.Value - node.Minimum.Value, node.Step.Value * 10);
            slider.Value = node.Value!.Value;
            AutomationProperties.SetHelpText(slider, node.AccessibilityValue ?? string.Empty);
        }
        finally { updatingSlider = false; }
    }
    private void ValidateSliderAdjustment()
    {
        if (adjustingSlider is not { } binding) return;
        if (!bindings.TryGetValue(binding.Identity.Id, out var current) || !ReferenceEquals(binding, current) ||
            !Eligible(binding) || declarations[binding.Identity.Id].Node.SliderInteractionMode != SliderInteractionMode.ActivateToAdjust)
            adjustingSlider = null;
    }
    private bool HandleSliderButton(ControllerButton button, ControllerEventPhase phase)
    {
        if (button == ControllerButton.B && sliderBackPending)
        {
            if (phase == ControllerEventPhase.Released) sliderBackPending = false;
            if (phase != ControllerEventPhase.Pressed) return true;
            sliderBackPending = false;
        }
        if (FocusedBinding() is not { Identity.Kind: ViewNodeKind.Slider } binding || !Eligible(binding)) return false;
        var node = declarations[binding.Identity.Id].Node;
        if (node.SliderInteractionMode != SliderInteractionMode.ActivateToAdjust) return false;
        if (button == ControllerButton.A)
        {
            if (phase == ControllerEventPhase.Pressed) adjustingSlider = ReferenceEquals(adjustingSlider, binding) ? null : binding;
            return true;
        }
        if (ReferenceEquals(adjustingSlider, binding) && button == ControllerButton.B)
        {
            if (phase == ControllerEventPhase.Pressed) { adjustingSlider = null; sliderBackPending = true; }
            return true;
        }
        return false;
    }
    private bool MoveSlider(FocusNavigationDirection direction)
    {
        if (FocusedBinding() is not { Element: Slider slider } binding || !Eligible(binding)) return false;
        var node = declarations[binding.Identity.Id].Node;
        if (node.SliderInteractionMode == SliderInteractionMode.ActivateToAdjust && !ReferenceEquals(adjustingSlider, binding)) return false;
        if (direction is not (FocusNavigationDirection.Left or FocusNavigationDirection.Right))
            return ReferenceEquals(adjustingSlider, binding);
        StepSlider(binding, slider, direction);
        return true;
    }
    private void StepSlider(Binding binding, Slider slider, FocusNavigationDirection direction)
    {
        var node = declarations[binding.Identity.Id].Node;
        slider.Value = direction == FocusNavigationDirection.Left
            ? SliderMath.Decrement(slider.Value, node.Minimum!.Value, node.Maximum!.Value, node.Step!.Value)
            : SliderMath.Increment(slider.Value, node.Minimum!.Value, node.Maximum!.Value, node.Step!.Value);
    }
    private async Task CommitSliderAsync(WidgetElementIdentity identity, object token, double value)
    {
        if (disposed || presentationOnly || frame is null || DispatchActionAsync is null ||
            !bindings.TryGetValue(identity.Id, out var binding) || binding.Identity != identity ||
            !ReferenceEquals(binding.Token, token) || !Eligible(binding)) return;
        var node = declarations[identity.Id].Node;
        if (node.ValueChangedActionId is not { } action) return;
        // Native pointer/UIA updates also use the SDK's minimum-anchored grid.
        var target = Math.Clamp(node.Minimum!.Value + Math.Round((value - node.Minimum.Value) / node.Step!.Value) * node.Step.Value,
            node.Minimum.Value, node.Maximum!.Value);
        if (value == node.Maximum.Value) target = value;
        if (!SliderMath.IsValidRequestedValue(target, node.Minimum.Value, node.Maximum.Value, node.Step.Value)) return;
        if (binding.Element is Slider slider && slider.Value != target)
        { updatingSlider = true; try { slider.Value = target; } finally { updatingSlider = false; } }
        var authority = frame.Authority;
        try
        {
            await DispatchActionAsync(new(authority, new WidgetActionEvent(action, identity.Id, Sequence: ++actionSequence,
                MonotonicTimestampMicroseconds: Environment.TickCount64 * 1000, RequestedValue: target,
                InputScopeId: authority.ActiveInputScopeId) { FocusedElementId = identity.Id }));
        }
        catch (WidgetPresentationSessionException error) when (error.Code is "snapshot_stale" or "input_scope_stale" or "presentation_stale") { }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) { ReportFailure(error); }
    }
}
