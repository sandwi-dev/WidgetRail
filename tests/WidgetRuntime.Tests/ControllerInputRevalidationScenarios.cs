using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class ControllerInputRevalidationScenarios
{
    internal static async Task CustomHandlersAreConservative()
    {
        var standard = new StandardWidget();
        var input = Publish(standard);
        if (await WidgetWorkerServer.AdmitRevalidatedControllerInputAsync(
                standard, input, CancellationToken.None) != false)
            throw new InvalidOperationException("Unbound standard B must retain host fallback.");
        foreach (var widget in new CustomWidget[] { new CustomWidget(), new InheritedWidget() })
        {
            var customInput = Publish(widget);
            if (await WidgetWorkerServer.AdmitRevalidatedControllerInputAsync(
                    widget, customInput, CancellationToken.None) is not null || widget.Calls != 0)
                throw new InvalidOperationException("A custom override was replayed.");
            await widget.OnControllerInputAsync(customInput);
            if (widget.Calls != 1) throw new InvalidOperationException("Exact raw input was disabled.");
            // A verified declarative binding preserves override bookkeeping.
            if (await WidgetWorkerServer.AdmitRevalidatedControllerInputAsync(widget,
                    customInput with { Button = ControllerButton.A }, CancellationToken.None, "action") != true ||
                widget.Calls != 2)
                throw new InvalidOperationException("Declared input lost override bookkeeping.");
        }
        // A non-virtual method hiding the name does not replace Widget's slot.
        var hidden = new HiddenWidget();
        if (await WidgetWorkerServer.AdmitRevalidatedControllerInputAsync(
                hidden, Publish(hidden), CancellationToken.None) != false || hidden.Calls != 0)
            throw new InvalidOperationException("Hidden method was treated as the virtual handler.");
    }

    private static ControllerInputEvent Publish(Widget widget)
    {
        var snapshot = widget.RenderPublication("runtime.test", new string('0', 32),
            1, 0, PresentationUpdateCapabilities.None,
            WidgetPresentationTransactionKind.OrdinaryCheckpoint).Snapshot;
        return new(ControllerButton.B, ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget, "button", Sequence: 1,
            ActiveInputScopeId: snapshot.ActiveInputScopeId, SnapshotSequence: snapshot.Sequence);
    }

    private class StandardWidget : Widget
    {
        public override WidgetView Render() => new(
            UI.Stack("root", UI.Button("Action", "action", "button")), "button");
    }
    private class CustomWidget : StandardWidget
    {
        internal int Calls;
        public override ValueTask<bool> OnControllerInputAsync(ControllerInputEvent input,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(true);
        }
    }
    private sealed class InheritedWidget : CustomWidget { }
    private sealed class HiddenWidget : StandardWidget
    {
        internal int Calls;
        public new ValueTask<bool> OnControllerInputAsync(ControllerInputEvent input,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(true);
        }
    }
}
