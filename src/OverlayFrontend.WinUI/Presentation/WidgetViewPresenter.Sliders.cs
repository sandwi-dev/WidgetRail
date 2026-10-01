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
        var slider = new WidgetSlider { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 120,
            IsThumbToolTipEnabled = false, UseSystemFocusVisuals = true };
        slider.ValueChanged += (_, args) =>
        {
            // WinUI restores its entry value before raising FocusDisengaged.
            // That is a native cancellation side effect, never a new user edit.
            if (ReferenceEquals(adjustingSlider?.Element, slider) && !slider.IsFocusEngaged) return;
            if (!applying && !updatingSlider) RequestSliderValue(identity, token, args.NewValue);
        };
        slider.LostFocus += (_, _) =>
        {
            if (slider.XamlRoot is null || FindBinding(FocusManager.GetFocusedElement(slider.XamlRoot) as DependencyObject)?.Element != slider)
            {
                FlushSlider(slider, force: true);
                if (adjustingSlider?.Element == slider) SetSliderAdjustment(null);
            }
        };
        slider.FocusDisengaged += (_, _) =>
        {
            if (adjustingSlider?.Element != slider) return;
            FlushSlider(slider, force: true); SetSliderAdjustment(null);
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
            slider.Value = ReconcileSliderValue(slider, node);
            UpdateSliderHelp(slider, node);
        }
        finally { updatingSlider = false; }
    }
    private void ValidateSliderAdjustment()
    {
        if (adjustingSlider is not { } binding) return;
        if (!bindings.TryGetValue(binding.Identity.Id, out var current) || !ReferenceEquals(binding, current) ||
            !Navigable(binding) || declarations[binding.Identity.Id].Node.SliderInteractionMode != SliderInteractionMode.ActivateToAdjust)
            SetSliderAdjustment(null);
    }
    private void SetSliderAdjustment(Binding? next)
    {
        if (ReferenceEquals(adjustingSlider, next)) return;
        var previous = adjustingSlider;
        adjustingSlider = next;
        if (previous?.Element is Slider old)
        {
            // Native disengagement may restore its entry value. Preserve the
            // shared local-intent owner's value without dispatching a rollback.
            var value = sliderValues.TryGetValue(old, out var entry) ? entry.State.Value : old.Value;
            updatingSlider = true;
            try { old.IsFocusEngaged = false; old.Value = value; }
            finally { updatingSlider = false; }
            if (declarations.TryGetValue(previous.Identity.Id, out var declaration)) UpdateSliderHelp(old, declaration.Node);
        }
        if (next?.Element is Slider slider)
        {
            slider.IsFocusEngagementEnabled = true;
            slider.IsFocusEngaged = true;
            UpdateSliderHelp(slider, declarations[next.Identity.Id].Node);
        }
        NotifyControllerGuideChanged();
    }
    private void UpdateSliderHelp(Slider slider, ViewNode node)
    {
        var instruction = node.SliderInteractionMode != SliderInteractionMode.ActivateToAdjust ? string.Empty :
            ReferenceEquals(adjustingSlider?.Element, slider) ? "Adjustment active. Left/right to adjust; A or B to finish." : "Press A to adjust.";
        AutomationProperties.SetHelpText(slider, string.Join(" ", new[] { node.AccessibilityValue, instruction }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }
    private bool HandleSliderButton(ControllerButton button, ControllerEventPhase phase)
    {
        if (button == ControllerButton.B && sliderBackPending)
        {
            if (phase == ControllerEventPhase.Released) sliderBackPending = false;
            if (phase != ControllerEventPhase.Pressed) return true;
            sliderBackPending = false;
        }
        if (FocusedBinding() is not { Identity.Kind: ViewNodeKind.Slider } binding || !Navigable(binding)) return false;
        var node = declarations[binding.Identity.Id].Node;
        if (node.SliderInteractionMode != SliderInteractionMode.ActivateToAdjust) return false;
        if (button == ControllerButton.A)
        {
            if (phase == ControllerEventPhase.Pressed && (ReferenceEquals(adjustingSlider, binding) || node.IsBusy != true))
            {
                if (ReferenceEquals(adjustingSlider, binding)) FlushSlider((Slider)binding.Element, force: true);
                SetSliderAdjustment(ReferenceEquals(adjustingSlider, binding) ? null : binding);
            }
            return true;
        }
        if (ReferenceEquals(adjustingSlider, binding) && button == ControllerButton.B)
        {
            if (phase == ControllerEventPhase.Pressed) { FlushSlider((Slider)binding.Element, force: true); SetSliderAdjustment(null); sliderBackPending = true; }
            return true;
        }
        return false;
    }
    private bool MoveSlider(FocusNavigationDirection direction)
    {
        if (FocusedBinding() is not { Element: Slider slider } binding || !Navigable(binding)) return false;
        var node = declarations[binding.Identity.Id].Node;
        if (node.SliderInteractionMode == SliderInteractionMode.ActivateToAdjust && !ReferenceEquals(adjustingSlider, binding)) return false;
        if (node.IsBusy == true && !ReferenceEquals(adjustingSlider, binding)) return true;
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
    private void RequestSliderValue(WidgetElementIdentity identity, object token, double value)
    {
        if (bindings.TryGetValue(identity.Id, out var busy) && busy.Identity == identity && ReferenceEquals(busy.Token, token) &&
            declarations[identity.Id].Node is { IsBusy: true } busyNode && busy.Element is Slider busySlider &&
            !ReferenceEquals(adjustingSlider, busy))
        {
            UpdateSlider(busySlider, busyNode);
            return;
        }
        if (disposed || presentationOnly || frame is null || !CanDispatchAction ||
            !bindings.TryGetValue(identity.Id, out var binding) || binding.Identity != identity ||
            !ReferenceEquals(binding.Token, token) || !Navigable(binding)) return;
        var node = declarations[identity.Id].Node;
        if (node.ValueChangedActionId is not { } action) return;
        // Native pointer/UIA updates also use the SDK's minimum-anchored grid.
        var target = Math.Clamp(node.Minimum!.Value + Math.Round((value - node.Minimum.Value) / node.Step!.Value) * node.Step.Value,
            node.Minimum.Value, node.Maximum!.Value);
        if (value == node.Maximum.Value) target = value;
        if (!SliderMath.IsValidRequestedValue(target, node.Minimum.Value, node.Maximum.Value, node.Step.Value)) return;
        if (binding.Element is Slider slider && slider.Value != target)
        { updatingSlider = true; try { slider.Value = target; } finally { updatingSlider = false; } }
        QueueSliderValue(binding, node, target);
    }
}
