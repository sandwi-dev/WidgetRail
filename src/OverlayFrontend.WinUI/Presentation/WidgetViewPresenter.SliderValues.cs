using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record SliderValueEntry(Binding Binding, WidgetPresentationBinding Origin,
        string Action, double Minimum, double Maximum, double Step, SliderInteractionMode? Mode, SliderValueState State);
    private readonly Dictionary<Slider, SliderValueEntry> sliderValues = [];
    private DispatcherTimer? sliderValueTimer;

    private bool SliderOwnerCurrent(SliderValueEntry entry, ViewNode node) =>
        !disposed && presentationActive && entry.Origin.SameInput(presentation) &&
        bindings.TryGetValue(entry.Binding.Identity.Id, out var current) && ReferenceEquals(current, entry.Binding) &&
        current.Identity.Scope == activeScope && node.ValueChangedActionId == entry.Action &&
        node.Minimum == entry.Minimum && node.Maximum == entry.Maximum && node.Step == entry.Step && node.SliderInteractionMode == entry.Mode;

    private double ReconcileSliderValue(Slider slider, ViewNode node)
    {
        if (!sliderValues.TryGetValue(slider, out var entry)) return node.Value!.Value;
        if (!SliderOwnerCurrent(entry, node)) { sliderValues.Remove(slider); return node.Value!.Value; }
        entry.State.Observe(node.Value!.Value, presentation!.View.Sequence, node.IsBusy == true,
            node.IsDisabled == true, Environment.TickCount64);
        return entry.State.Value;
    }

    private void QueueSliderValue(Binding binding, ViewNode node, double target)
    {
        var slider = (Slider)binding.Element;
        _ = ReconcileSliderValue(slider, node);
        if (!sliderValues.TryGetValue(slider, out var entry))
        {
            // Like the native host, bound retained intents independently of tree size.
            if (sliderValues.Count >= 256) { SetSliderValue(slider, node.Value!.Value); return; }
            entry = new(binding, presentation!, node.ValueChangedActionId!, node.Minimum!.Value,
                node.Maximum!.Value, node.Step!.Value, node.SliderInteractionMode, new SliderValueState(node.Value!.Value, presentation!.View.Sequence,
                    node.Maximum.Value - node.Minimum.Value));
            sliderValues.Add(slider, entry);
        }
        entry.State.Request(target, Environment.TickCount64);
        SetSliderValue(slider, entry.State.Value);
        if (sliderValueTimer is null)
        {
            sliderValueTimer = new() { Interval = TimeSpan.FromMilliseconds(25) };
            sliderValueTimer.Tick += PumpSliderValues;
        }
        if (!sliderValueTimer.IsEnabled) sliderValueTimer.Start();
    }

    private void PumpSliderValues(object? sender, object args)
    {
        if (disposed || !presentationActive || !presentationInputEnabled) { ResetSliderValues(); return; }
        foreach (var (slider, entry) in sliderValues.ToArray())
        {
            if (!declarations.TryGetValue(entry.Binding.Identity.Id, out var declaration) || !SliderOwnerCurrent(entry, declaration.Node) || !Navigable(entry.Binding))
            { sliderValues.Remove(slider); continue; }
            SetSliderValue(slider, ReconcileSliderValue(slider, declaration.Node));
            FlushSlider(slider);
            if (!entry.State.HasPending) sliderValues.Remove(slider);
        }
        if (sliderValues.Count == 0) sliderValueTimer?.Stop();
    }

    private void FlushSlider(Slider slider, bool force = false)
    {
        if (applying || !CanDispatchAction || !sliderValues.TryGetValue(slider, out var entry) || !Navigable(entry.Binding) ||
            !declarations.TryGetValue(entry.Binding.Identity.Id, out var declaration) || !SliderOwnerCurrent(entry, declaration.Node)) return;
        _ = ReconcileSliderValue(slider, declaration.Node);
        if (entry.State.TakeDispatch(Environment.TickCount64, force) is { } dispatch)
            _ = DispatchSliderValueAsync(slider, entry, presentation!, dispatch.Value, dispatch.Generation);
    }

    private async Task DispatchSliderValueAsync(Slider slider, SliderValueEntry entry,
        WidgetPresentationBinding displayed, double value, long generation)
    {
        try
        {
            await DispatchCapturedActionAsync(displayed, new WidgetActionEvent(entry.Action, entry.Binding.Identity.Id,
                Sequence: ++actionSequence, MonotonicTimestampMicroseconds: Environment.TickCount64 * 1000,
                RequestedValue: value, InputScopeId: displayed.Scope) { FocusedElementId = entry.Binding.Identity.Id });
        }
        catch (WidgetPresentationSessionException error) when (IsRetiredInput(error)) { Reject(); }
        catch (OperationCanceledException) { Reject(); }
        catch (Exception error) { if (Reject()) ReportFailure(error); }

        bool Reject()
        {
            if (disposed || !sliderValues.TryGetValue(slider, out var current) || !ReferenceEquals(current, entry)) return false;
            var wasLatest = entry.State.IsCurrent(generation);
            entry.State.Reject(generation);
            SetSliderValue(slider, entry.State.Value);
            return wasLatest;
        }
    }

    private void SetSliderValue(Slider slider, double value)
    {
        if (slider.Value == value) return;
        var previous = updatingSlider;
        updatingSlider = true;
        try { slider.Value = value; }
        finally { updatingSlider = previous; }
    }

    private void RetireSlider(Binding binding)
    {
        if (binding.Element is Slider slider) sliderValues.Remove(slider);
        if (sliderValues.Count == 0) sliderValueTimer?.Stop();
    }

    private void ResetSliderValues()
    {
        sliderValueTimer?.Stop();
        foreach (var (slider, entry) in sliderValues)
            if (declarations.TryGetValue(entry.Binding.Identity.Id, out var declaration) && declaration.Node.Value is { } value)
                SetSliderValue(slider, value);
        sliderValues.Clear();
    }
}
