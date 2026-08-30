using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

/// <summary>
/// Held-button action repeat is a provider-neutral opt-in. These cover the
/// public contract only: which buttons may opt in, what the opt-in costs on the
/// wire, and that a held action never accumulates a backlog behind itself.
/// </summary>
internal static class HeldButtonActionRepeatTests
{
    internal static async Task Run()
    {
        EligibleButtonsMayOptIn();
        ReservedButtonsStayEdgeOnly();
        OptingInRequiresTheHeldRepeatProtocolVersion();
        TheWireCarriesTheExactHostToken();
        await HeldRepeatsCoalesceBehindTheirOwnAction();
    }

    // LT and RT are the first consumers; X proves the primitive is not a
    // trigger-only contract. A non-opted control must stay edge-only.
    private static void EligibleButtonsMayOptIn()
    {
        foreach (var button in new[]
        {
            ControllerButton.LeftTrigger,
            ControllerButton.RightTrigger,
            ControllerButton.X,
            ControllerButton.Y,
            ControllerButton.LeftBumper,
            ControllerButton.RightBumper,
            ControllerButton.LeftStick,
            ControllerButton.RightStick,
        })
        {
            var snapshot = HeldShortcutView(button).CreateSnapshot("repeat.instance", 1);
            Equal(
                ControllerActionRepeatPolicy.WhileHeld,
                snapshot.Root.Shortcuts.Single().RepeatPolicy);

        }

        // Y is repeat-eligible as a shortcut but host-reserved on the
        // dashboard, so the dashboard set is the intersection of both rules.
        foreach (var button in new[]
        {
            ControllerButton.LeftTrigger,
            ControllerButton.RightTrigger,
            ControllerButton.X,
            ControllerButton.LeftBumper,
            ControllerButton.RightBumper,
            ControllerButton.LeftStick,
            ControllerButton.RightStick,
        })
        {
            var quick = QuickActionView(button, ControllerActionRepeatPolicy.WhileHeld)
                .CreateSnapshot("repeat.instance", 2);
            Equal(
                ControllerActionRepeatPolicy.WhileHeld,
                quick.QuickActions!.Single().RepeatPolicy);
        }

        var edgeOnly = new WidgetView(
                UI.Stack("root").Shortcut(ControllerButton.X, "tap"))
            .CreateSnapshot("repeat.instance", 3);
        Equal(
            ControllerActionRepeatPolicy.None,
            edgeOnly.Root.Shortcuts.Single().RepeatPolicy);
    }

    // The host owns activation, back, and navigation. No authored declaration
    // can turn those into repeating widget actions.
    private static void ReservedButtonsStayEdgeOnly()
    {
        foreach (var button in new[]
        {
            ControllerButton.A,
            ControllerButton.B,
            ControllerButton.DPadUp,
            ControllerButton.DPadDown,
            ControllerButton.DPadLeft,
            ControllerButton.DPadRight,
        })
        {
            var exception = Throws<ProtocolValidationException>(
                () => HeldShortcutView(button).CreateSnapshot("repeat.instance", 4));
            True(
                exception.Errors.Any(error =>
                    error.Code is "reserved_repeat_button" or "reserved_shortcut_button"),
                $"{button} must not be able to opt into held repeat.");
        }

        // Menu is an admissible dashboard button that still may not repeat:
        // the repeat rule is its own gate, not a restatement of reservation.
        var quickException = Throws<ProtocolValidationException>(() =>
            QuickActionView(ControllerButton.Menu, ControllerActionRepeatPolicy.WhileHeld)
                .CreateSnapshot("repeat.instance", 5));
        True(
            quickException.Errors.Any(error => error.Code == "reserved_repeat_button"),
            "An admissible dashboard button must still be refused held repeat.");
    }

    private static void OptingInRequiresTheHeldRepeatProtocolVersion()
    {
        var held = HeldShortcutView(ControllerButton.LeftTrigger)
            .CreateSnapshot("repeat.instance", 6);
        Equal(ProtocolConstants.HeldButtonActionRepeatVersion, held.ProtocolVersion);

        var heldQuickAction = QuickActionView(
                ControllerButton.RightTrigger, ControllerActionRepeatPolicy.WhileHeld)
            .CreateSnapshot("repeat.instance", 7);
        Equal(
            ProtocolConstants.HeldButtonActionRepeatVersion,
            heldQuickAction.ProtocolVersion);

        // The default must not drag every widget onto the newest protocol.
        var edgeOnly = QuickActionView(
                ControllerButton.RightTrigger, ControllerActionRepeatPolicy.None)
            .CreateSnapshot("repeat.instance", 8);
        True(
            edgeOnly.ProtocolVersion < ProtocolConstants.HeldButtonActionRepeatVersion,
            "Edge-only authoring must not require the held-repeat protocol version.");
    }

    // The native host matches the serialized policy by exact token. Pinning the
    // casing here keeps the managed opt-in and the host's repeat owner from
    // silently drifting apart.
    private static void TheWireCarriesTheExactHostToken()
    {
        var snapshot = HeldShortcutView(ControllerButton.LeftTrigger)
            .CreateSnapshot("repeat.instance", 9);
        var json = SnapshotJson.Serialize(snapshot);
        using (var document = JsonDocument.Parse(json))
        {
            var shortcut = document.RootElement
                .GetProperty("root").GetProperty("shortcuts")[0];
            Equal("whileHeld", shortcut.GetProperty("repeatPolicy").GetString());
            Equal("leftTrigger", shortcut.GetProperty("button").GetString());
            Equal("pressed", shortcut.GetProperty("phase").GetString());
        }

        var restored = SnapshotJson.Deserialize(json);
        Equal(
            ControllerActionRepeatPolicy.WhileHeld,
            restored.Root.Shortcuts.Single().RepeatPolicy);

        var quickJson = SnapshotJson.Serialize(
            QuickActionView(ControllerButton.RightTrigger,
                ControllerActionRepeatPolicy.WhileHeld)
                .CreateSnapshot("repeat.instance", 10));
        using var quickDocument = JsonDocument.Parse(quickJson);
        Equal(
            "whileHeld",
            quickDocument.RootElement.GetProperty("quickActions")[0]
                .GetProperty("repeatPolicy").GetString());
    }

    // Backpressure: while a held action is still owned, due ticks coalesce.
    // Unrelated actions can interleave, so the whole queue has to be consulted
    // -- checking only the tail lets a hold fill the bounded FIFO.
    private static async Task HeldRepeatsCoalesceBehindTheirOwnAction()
    {
        var widget = new RepeatWidget();
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);

        Equal(WidgetOperationAdmission.Enqueued, widget.Admit(Seek(ControllerEventPhase.Pressed)));
        Equal(WidgetOperationAdmission.Joined, widget.Admit(Seek(ControllerEventPhase.Repeated)));
        Equal(WidgetOperationAdmission.Joined, widget.Admit(Seek(ControllerEventPhase.Repeated)));

        // An unrelated action between two due ticks must not let the hold
        // enqueue a second copy of itself.
        Equal(
            WidgetOperationAdmission.Enqueued,
            widget.Admit(new WidgetActionEvent(
                "youtube.playback.toggle", "youtube.root",
                ControllerButton.X, ControllerEventPhase.Pressed,
                InputScopeId: "youtube.root")));
        Equal(WidgetOperationAdmission.Joined, widget.Admit(Seek(ControllerEventPhase.Repeated)));

        // A different held button is its own action and keeps its own slot.
        Equal(
            WidgetOperationAdmission.Enqueued,
            widget.Admit(new WidgetActionEvent(
                "youtube.playback.seek-forward", "youtube.root",
                ControllerButton.RightTrigger, ControllerEventPhase.Repeated,
                InputScopeId: "youtube.root")));

        // A fresh press is never coalesced away: a tap is always one action.
        Equal(WidgetOperationAdmission.Enqueued, widget.Admit(Seek(ControllerEventPhase.Pressed)));

        await WidgetTestHost.DestroyAsync(widget);
    }

    private static WidgetActionEvent Seek(ControllerEventPhase phase) => new(
        "youtube.playback.seek-backward",
        "youtube.root",
        ControllerButton.LeftTrigger,
        phase,
        InputScopeId: "youtube.root");

    private static WidgetView HeldShortcutView(ControllerButton button) => new(
        UI.Stack("root").Shortcut(
            button,
            "held",
            ControllerEventPhase.Pressed,
            ControllerActionRepeatPolicy.WhileHeld));

    private static WidgetView QuickActionView(
        ControllerButton button,
        ControllerActionRepeatPolicy repeatPolicy) => new(
        UI.Stack("root", UI.Text("Fixture", "title")))
    {
        QuickActions =
        [
            new WidgetQuickAction(button, "held", "Held", RepeatPolicy: repeatPolicy),
        ],
    };

    private sealed class RepeatWidget : Widget
    {
        // Actions never complete, so every held tick meets a still-owned
        // incumbent -- the exact state the coalescing rule governs.
        private readonly TaskCompletionSource _never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WidgetOperationAdmission Admit(WidgetActionEvent action) =>
            AdmitAction(action);

        public override ValueTask OnActionAsync(
            WidgetActionEvent action,
            CancellationToken cancellationToken) =>
            new(_never.Task.WaitAsync(cancellationToken));

        public override WidgetView Render() =>
            new(UI.Stack("youtube.root", UI.Text("Fixture", "title")));
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
