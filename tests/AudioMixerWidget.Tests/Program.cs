using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using GameBarAlternative.FirstPartyWidgets.AudioMixer;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Pinned master and all-session scroll surfaces are compact and valid", StateSurfaces),
    ("D-pad and analog focus graph covers every row", ExplicitFocusGraph),
    ("Per-row actions route by stable identity without session shortcuts", ControllerRoutes),
    ("Volume updates immediately and resists stale in-flight events", OptimisticVolume),
    ("Rapid slider changes coalesce latest-wins without freezing other rows", RapidVolumeCoalescing),
    ("Older request failure cannot roll back a newer slider target", OlderFailurePreservesNewerTarget),
    ("Provider confirmation cannot release a setter worker still awaiting", ConfirmationKeepsWorkerOwnership),
    ("Volume remains adjustable while mute is pending", VolumeDuringPendingMute),
    ("App mute retains the exact slider focus target while pending", MuteRetainsFocusTarget),
    ("Post-ack stale events cannot snap a slider back", PostAckStaleEvent),
    ("External authoritative changes apply when no command is pending", ExternalAuthoritativeUpdate),
    ("Mute and volume failures roll back with bounded feedback", OptimisticRollback),
    ("Master output updates immediately reconciles and rolls back", MasterOutputControls),
    ("Session churn preserves stable row identity and nearest anchor", StableSelectionDuringChurn),
    ("Many sessions and long labels remain bounded and uniquely focusable", ManySessionsRemainBounded),
    ("Capability failure codes render distinct recovery states", CapabilityFailureStates),
    ("Live provider loss remains distinct from an empty session list", LiveProviderAvailability),
    ("Unavailable host service fails closed without OS fallback", UnavailableService),
    ("Visible lifetime fetches once subscribes and never polls", LifecycleAndNoPolling),
    ("Acknowledged subscription closes the snapshot fetch event gap", SubscriptionPrecedesSnapshot),
    ("Lifecycle cancellation rolls back without an error state", CancellationIsNotFailure),
    ("Manifest permissions and polished GBSS validate", ShippedAssetsValidate),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static async Task StateSurfaces()
{
    var fake = new FakeCapabilityClient();
    var widget = Create(fake);
    var initial = Snapshot(widget, 0);
    Assert.Equal(AudioMixerViewState.Initial, widget.ViewState);
    Assert.Equal("audio.retry", initial.InitialFocusId);
    Assert.Equal(WidgetSurfaceMode.Compact, initial.Surface!.Mode);
    Assert.Equal(520D, initial.Surface.PreferredWidth);
    Assert.Equal(520D, initial.Surface.PreferredHeight);
    Assert.Equal(360D, initial.Surface.MinimumHeight);
    Assert.Valid(initial);

    await Activate(widget);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.Empty);
    var empty = Snapshot(widget, 1);
    Assert.Contains("No application audio", Text(empty.Root, "audio.state.title").Text!);
    Assert.Equal(0.6D, Node(empty.Root, "audio.master.volume.slider").Value);
    Assert.Equal("audio.master.volume.slider", empty.InitialFocusId);
    Assert.Valid(empty);

    fake.Emit([Session("game", "Space Game", 0.72, active: true)]);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.Ready);
    var session = Snapshot(widget, 2);
    Assert.Equal("audio.master.volume.slider", session.InitialFocusId);
    var prefix = SessionPrefix(session.Root, "Space Game");
    Assert.Equal("Space Game", Text(session.Root, $"{prefix}.name").Text);
    Assert.Equal(0.72D, Node(session.Root, $"{prefix}.volume.slider").Value);
    Assert.Equal(WidgetGlyph.Volume, Node(session.Root, $"{prefix}.mute.icon").Glyph);
    Assert.True(!Buttons(session.Root).Any(button => button.Text is "−" or "+" or "Mute" or "Unmute"),
        "The old stepper/mute pill controls are still rendered.");
    Assert.Equal(ViewNodeKind.Scroll, Node(session.Root, "audio.sessions.scroll").Kind);
    Assert.Equal(ScrollAxis.Vertical, Node(session.Root, "audio.sessions.scroll").ScrollAxis);
    Assert.Equal(1, Node(session.Root, "audio.sessions.scroll").Children.Count);
    Assert.Valid(session);
    await Background(widget);
}

static async Task ExplicitFocusGraph()
{
    var fake = new FakeCapabilityClient { Sessions = [Session("a", "Game", 0.5), Session("b", "Chat", 0.4)] };
    var widget = Create(fake);
    await ActivateReady(widget);
    var snapshot = Snapshot(widget, 1);
    var sliders = Sliders(snapshot.Root).ToDictionary(node => node.Id, StringComparer.Ordinal);
    var game = SessionPrefix(snapshot.Root, "Game");
    var chat = SessionPrefix(snapshot.Root, "Chat");
    Assert.SequenceEqual(
        ["audio.master.volume.slider", $"{game}.volume.slider", $"{chat}.volume.slider"],
        sliders.Keys);
    Assert.Equal($"{game}.volume.slider", sliders["audio.master.volume.slider"].Focus!.Down);
    Assert.Equal("audio.master.volume.slider", sliders[$"{game}.volume.slider"].Focus!.Up);
    Assert.Equal($"{chat}.volume.slider", sliders[$"{game}.volume.slider"].Focus!.Down);
    Assert.Equal($"{game}.volume.slider", sliders[$"{chat}.volume.slider"].Focus!.Up);
    Assert.True(sliders.Values.All(slider => slider.Focus?.Left is null && slider.Focus?.Right is null),
        "A slider leaked horizontal focus instead of owning Left/Right adjustment.");
    Assert.True(sliders.Values.All(slider => slider.ActionId is not null),
        "A slider does not expose its A-button mute activation.");
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task ControllerRoutes()
{
    var fake = new FakeCapabilityClient { Sessions = [Session("a", "Game", 0.5), Session("b", "Chat", 0.4)] };
    var widget = Create(fake);
    await ActivateReady(widget);
    var snapshot = widget.RenderSnapshot("audio.test", 42);
    Assert.Equal(0, snapshot.QuickActions.Count);
    var scope = Node(snapshot.Root, "audio.root");
    Assert.Equal(0, scope.Shortcuts.Count);
    Assert.True(!await Route(widget, snapshot, ControllerButton.LeftBumper, "audio.master.volume.slider"),
        "LB retained the removed session-cycle shortcut.");
    Assert.True(!await Route(widget, snapshot, ControllerButton.RightBumper, "audio.master.volume.slider"),
        "RB retained the removed session-cycle shortcut.");
    Assert.True(!await Route(widget, snapshot, ControllerButton.LeftTrigger, "audio.master.volume.slider"),
        "LT retained the removed session-volume shortcut.");
    Assert.True(!await Route(widget, snapshot, ControllerButton.RightTrigger, "audio.master.volume.slider"),
        "RT retained the removed session-volume shortcut.");

    var chat = SessionPrefix(snapshot.Root, "Chat");
    Assert.True(await Route(widget, snapshot, ControllerButton.A, $"{chat}.volume.slider"),
        "A did not activate the focused Chat mute control.");
    await WaitUntil(() => fake.MuteRequests.Count == 1);
    Assert.Equal("b", fake.MuteRequests[0].SessionId);
    Assert.Equal(true, fake.MuteRequests[0].IsMuted);
    var current = widget.RenderSnapshot("audio.test", 43);
    var game = SessionPrefix(current.Root, "Game");
    Assert.True(await Route(widget, current, ControllerButton.DPadLeft, $"{game}.volume.slider", 0.45),
        "Left did not adjust the focused Game slider.");
    await WaitUntil(() => fake.VolumeRequests.Count == 1);
    Assert.Equal("a", fake.VolumeRequests[0].SessionId);
    Assert.Near(0.45D, fake.VolumeRequests[0].Volume);
    await widget.OnActionAsync(new($"{game}.volume.set", $"{game}.volume.slider", RequestedValue: 0.451));
    await Task.Delay(30);
    Assert.Equal(1, fake.VolumeRequests.Count);

    var master = widget.RenderSnapshot("audio.test", 44);
    Assert.True(await Route(widget, master, ControllerButton.A, "audio.master.volume.slider"),
        "A did not activate the master slider mute action.");
    await WaitUntil(() => fake.OutputMuteRequests.Count == 1);
    Assert.Equal(true, fake.OutputMuteRequests[0].IsMuted);

    var dashboard = widget.RenderSnapshot("audio.test", 45);
    Assert.True(!await widget.OnControllerInputAsync(new ControllerInputEvent(
        ControllerButton.LeftBumper,
        ControllerEventPhase.Pressed,
        ControllerInputContext.DashboardQuickAction,
        SnapshotSequence: dashboard.Sequence)), "Dashboard LB unexpectedly cycled an audio session.");
    await Background(widget);
}

static async Task OptimisticVolume()
{
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("game", "Game", 0.5)],
        ControlGate = gate.Task,
    };
    var widget = Create(fake);
    await ActivateReady(widget);
    var before = Snapshot(widget, 0);
    var game = SessionPrefix(before.Root, "Game");
    await widget.OnActionAsync(new($"{game}.volume.set", $"{game}.volume.slider", RequestedValue: 0.55));
    await WaitUntil(() => fake.VolumeRequests.Count == 1);
    var optimistic = Snapshot(widget, 1);
    Assert.Equal(0.55D, Node(optimistic.Root, $"{game}.volume.slider").Value);
    Assert.True(Node(optimistic.Root, $"{game}.row").StyleClasses.Contains("is-pending"),
        "The optimistic row does not expose bounded pending feedback.");
    Assert.True(Node(optimistic.Root, $"{game}.volume.slider").IsBusy is not true,
        "A pending volume blocked latest-wins slider input.");

    fake.Emit([Session("game", "Game", 0.5)]);
    await Task.Delay(30);
    Assert.Equal(0.55D, Node(Snapshot(widget, 2).Root, $"{game}.volume.slider").Value);
    gate.SetResult();
    await Task.Delay(30);
    Assert.Equal(0.55D, Node(Snapshot(widget, 3).Root, $"{game}.volume.slider").Value);
    fake.Emit([Session("game", "Game", 0.55)]);
    await WaitUntil(() => !Node(Snapshot(widget, 4).Root, $"{game}.row").StyleClasses.Contains("is-pending"));
    await Background(widget);
}

static async Task RapidVolumeCoalescing()
{
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("game", "Game", 0.5), Session("chat", "Chat", 0.4)],
        ControlGate = gate.Task,
    };
    var widget = Create(fake);
    await ActivateReady(widget);
    var first = Snapshot(widget, 0);
    var game = SessionPrefix(first.Root, "Game");
    var chat = SessionPrefix(first.Root, "Chat");
    await widget.OnActionAsync(new($"{game}.volume.set", $"{game}.volume.slider", RequestedValue: 0.55));
    await WaitUntil(() => fake.VolumeRequests.Count == 1);
    await widget.OnActionAsync(new($"{game}.volume.set", $"{game}.volume.slider", RequestedValue: 0.6));
    await widget.OnActionAsync(new($"{game}.volume.set", $"{game}.volume.slider", RequestedValue: 0.65));
    await widget.OnActionAsync(new($"{chat}.mute", $"{chat}.volume.slider"));
    await WaitUntil(() => fake.MuteRequests.Count == 1);
    var pending = Snapshot(widget, 1);
    Assert.Equal(0.65D, Node(pending.Root, $"{game}.volume.slider").Value);
    Assert.True(Node(pending.Root, $"{chat}.volume.slider").IsBusy is not true,
        "Pending mute blocked independent volume adjustment.");
    Assert.Equal($"{chat}.volume.slider", Node(pending.Root, $"{chat}.volume.slider").Id);
    gate.SetResult();
    await WaitUntil(() => fake.VolumeRequests.Count == 2);
    Assert.Near(0.55, fake.VolumeRequests[0].Volume);
    Assert.Near(0.65, fake.VolumeRequests[1].Volume);
    Assert.True(!fake.VolumeRequests.Any(request => VolumesNear(request.Volume, 0.6)),
        "An intermediate slider repeat escaped latest-wins coalescing.");
    await Background(widget);
}

static async Task OlderFailurePreservesNewerTarget()
{
    var firstSessionVolume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var sessionVolumeFake = new FakeCapabilityClient { Sessions = [Session("game", "Game", 0.5)] };
    sessionVolumeFake.PlanSessionVolume(firstSessionVolume.Task, new InvalidOperationException("old failure"));
    sessionVolumeFake.PlanSessionVolume();
    var sessionVolumeWidget = Create(sessionVolumeFake);
    await ActivateReady(sessionVolumeWidget);
    var game = SessionPrefix(Snapshot(sessionVolumeWidget, 0).Root, "Game");
    await sessionVolumeWidget.OnActionAsync(new($"{game}.volume.set", $"{game}.volume.slider", RequestedValue: 0.55));
    await WaitUntil(() => sessionVolumeFake.VolumeRequests.Count == 1);
    await sessionVolumeWidget.OnActionAsync(new($"{game}.volume.set", $"{game}.volume.slider", RequestedValue: 0.7));
    firstSessionVolume.SetResult();
    await WaitUntil(() => sessionVolumeFake.VolumeRequests.Count == 2);
    Assert.Near(0.7, sessionVolumeFake.VolumeRequests[1].Volume);
    Assert.Near(0.7, sessionVolumeWidget.Sessions.Single().Volume);
    Assert.True(!Text(Snapshot(sessionVolumeWidget, 1).Root, "audio.status").StyleClasses.Contains("is-error"),
        "An older session-volume failure surfaced after a newer target replaced it.");
    sessionVolumeFake.Emit([Session("game", "Game", 0.7)]);
    await Background(sessionVolumeWidget);

    var firstSessionMute = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var sessionMuteFake = new FakeCapabilityClient { Sessions = [Session("game", "Game", 0.5)] };
    sessionMuteFake.PlanSessionMute(firstSessionMute.Task, new InvalidOperationException("old failure"));
    sessionMuteFake.PlanSessionMute();
    var sessionMuteWidget = Create(sessionMuteFake);
    await ActivateReady(sessionMuteWidget);
    game = SessionPrefix(Snapshot(sessionMuteWidget, 0).Root, "Game");
    await sessionMuteWidget.OnActionAsync(new($"{game}.mute", $"{game}.volume.slider"));
    await WaitUntil(() => sessionMuteFake.MuteRequests.Count == 1);
    await sessionMuteWidget.OnActionAsync(new($"{game}.mute", $"{game}.volume.slider"));
    firstSessionMute.SetResult();
    await WaitUntil(() => sessionMuteFake.MuteRequests.Count == 2);
    Assert.Equal(false, sessionMuteFake.MuteRequests[1].IsMuted);
    Assert.Equal(false, sessionMuteWidget.Sessions.Single().IsMuted);
    sessionMuteFake.Emit([Session("game", "Game", 0.5, muted: false)]);
    await Background(sessionMuteWidget);

    var firstOutputVolume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var outputVolumeFake = new FakeCapabilityClient { Sessions = [Session("game", "Game", 0.5)] };
    outputVolumeFake.PlanOutputVolume(firstOutputVolume.Task, new InvalidOperationException("old failure"));
    outputVolumeFake.PlanOutputVolume();
    var outputVolumeWidget = Create(outputVolumeFake);
    await ActivateReady(outputVolumeWidget);
    await outputVolumeWidget.OnActionAsync(new("output.volume.set", "audio.master.volume.slider", RequestedValue: 0.65));
    await WaitUntil(() => outputVolumeFake.OutputVolumeRequests.Count == 1);
    await outputVolumeWidget.OnActionAsync(new("output.volume.set", "audio.master.volume.slider", RequestedValue: 0.8));
    firstOutputVolume.SetResult();
    await WaitUntil(() => outputVolumeFake.OutputVolumeRequests.Count == 2);
    Assert.Near(0.8, outputVolumeFake.OutputVolumeRequests[1].Volume);
    Assert.Near(0.8, outputVolumeWidget.Output!.Volume);
    outputVolumeFake.EmitOutput(new WidgetAudioOutput(0.8, false));
    await Background(outputVolumeWidget);

    var firstOutputMute = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var outputMuteFake = new FakeCapabilityClient { Sessions = [Session("game", "Game", 0.5)] };
    outputMuteFake.PlanOutputMute(firstOutputMute.Task, new InvalidOperationException("old failure"));
    outputMuteFake.PlanOutputMute();
    var outputMuteWidget = Create(outputMuteFake);
    await ActivateReady(outputMuteWidget);
    await outputMuteWidget.OnActionAsync(new("output.mute.toggle", "audio.master.volume.slider"));
    await WaitUntil(() => outputMuteFake.OutputMuteRequests.Count == 1);
    await outputMuteWidget.OnActionAsync(new("output.mute.toggle", "audio.master.volume.slider"));
    firstOutputMute.SetResult();
    await WaitUntil(() => outputMuteFake.OutputMuteRequests.Count == 2);
    Assert.Equal(false, outputMuteFake.OutputMuteRequests[1].IsMuted);
    Assert.Equal(false, outputMuteWidget.Output!.IsMuted);
    outputMuteFake.EmitOutput(new WidgetAudioOutput(0.6, false));
    await Background(outputMuteWidget);
}

static async Task ConfirmationKeepsWorkerOwnership()
{
    var setterGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeCapabilityClient { Sessions = [Session("game", "Game", 0.5)] };
    fake.PlanSessionVolume(setterGate.Task);
    fake.PlanSessionVolume();
    var widget = Create(fake);
    await ActivateReady(widget);
    var game = SessionPrefix(Snapshot(widget, 0).Root, "Game");
    await widget.OnActionAsync(new($"{game}.volume.set", $"{game}.volume.slider", RequestedValue: 0.55));
    await WaitUntil(() => fake.VolumeRequests.Count == 1);
    fake.Emit([Session("game", "Game", 0.55)]);
    await WaitUntil(() => VolumesNear(widget.Sessions.Single().Volume, 0.55));
    await widget.OnActionAsync(new($"{game}.volume.set", $"{game}.volume.slider", RequestedValue: 0.7));
    await Task.Delay(80);
    Assert.Equal(1, fake.VolumeRequests.Count);
    setterGate.SetResult();
    await WaitUntil(() => fake.VolumeRequests.Count == 2);
    Assert.Near(0.7, fake.VolumeRequests[1].Volume);
    await Task.Delay(80);
    Assert.Equal(2, fake.VolumeRequests.Count);
    fake.Emit([Session("game", "Game", 0.7)]);
    await Background(widget);
}

static async Task VolumeDuringPendingMute()
{
    var muteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeCapabilityClient { Sessions = [Session("game", "Game", 0.5)] };
    fake.PlanSessionMute(muteGate.Task);
    var widget = Create(fake);
    await ActivateReady(widget);
    var snapshot = widget.RenderSnapshot("audio.test", 70);
    var game = SessionPrefix(snapshot.Root, "Game");
    var sliderId = $"{game}.volume.slider";
    Assert.True(await Route(widget, snapshot, ControllerButton.A, sliderId),
        "Mute activation did not route through the focused slider.");
    await WaitUntil(() => fake.MuteRequests.Count == 1);
    var mutePending = widget.RenderSnapshot("audio.test", 71);
    Assert.True(Node(mutePending.Root, sliderId).IsBusy is not true,
        "Pending mute generically blocked the entire slider.");
    Assert.True(await Route(widget, mutePending, ControllerButton.DPadRight, sliderId, 0.55),
        "Volume input did not route while mute was pending.");
    await WaitUntil(() => fake.VolumeRequests.Count == 1);
    Assert.Near(0.55, fake.VolumeRequests.Single().Volume);
    Assert.Near(0.55, widget.Sessions.Single().Volume);
    muteGate.SetResult();
    fake.Emit([Session("game", "Game", 0.55, muted: true)]);
    await Background(widget);
}

static async Task MuteRetainsFocusTarget()
{
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("game", "Game", 0.5), Session("chat", "Chat", 0.4)],
        ControlGate = gate.Task,
    };
    var widget = Create(fake);
    await ActivateReady(widget);
    var before = widget.RenderSnapshot("audio.test", 50);
    var chat = SessionPrefix(before.Root, "Chat");
    var focusId = $"{chat}.volume.slider";
    Assert.True(await Route(widget, before, ControllerButton.A, focusId),
        "A did not route through the session slider activation action.");
    await WaitUntil(() => fake.MuteRequests.Count == 1);
    var pending = Snapshot(widget, 51);
    var pendingTarget = Node(pending.Root, focusId);
    Assert.Equal(ViewNodeKind.Slider, pendingTarget.Kind);
    Assert.True(pendingTarget.IsBusy is not true,
        "Generic Busy blocked the slider while mute was pending.");
    Assert.True(pendingTarget.IsDisabled is not true,
        "Ordinary async mute work removed the focused row from navigation.");
    Assert.Equal(focusId, Sliders(pending.Root).Single(slider => slider.Id == focusId).Id);
    gate.SetResult();
    fake.Emit([Session("game", "Game", 0.5), Session("chat", "Chat", 0.4, muted: true)]);
    await WaitUntil(() => Node(Snapshot(widget, 52).Root, focusId).IsBusy is not true);
    var reconciled = Snapshot(widget, 53);
    Assert.Equal(focusId, Sliders(reconciled.Root).Single(slider => slider.Id == focusId).Id);
    Assert.Equal(WidgetGlyph.Muted, Node(reconciled.Root, $"{chat}.mute.icon").Glyph);
    Assert.Valid(reconciled);
    await Background(widget);
}

static async Task PostAckStaleEvent()
{
    var fake = new FakeCapabilityClient { Sessions = [Session("game", "Game", 0.5)] };
    var widget = Create(fake);
    await ActivateReady(widget);
    var game = SessionPrefix(Snapshot(widget, 0).Root, "Game");
    await widget.OnActionAsync(new($"{game}.volume.set", $"{game}.volume.slider", RequestedValue: 0.7));
    await WaitUntil(() => fake.VolumeRequests.Count == 1);
    await Task.Delay(30);
    fake.Emit([Session("game", "Game", 0.5)]);
    await Task.Delay(30);
    Assert.Near(0.7D, Node(Snapshot(widget, 1).Root, $"{game}.volume.slider").Value ?? -1);
    fake.Emit([Session("game", "Game", 0.7)]);
    await WaitUntil(() => VolumesNear(widget.Sessions.Single().Volume, 0.7));
    await Background(widget);
}

static async Task ExternalAuthoritativeUpdate()
{
    var fake = new FakeCapabilityClient { Sessions = [Session("game", "Game", 0.5)] };
    var widget = Create(fake);
    await ActivateReady(widget);
    var game = SessionPrefix(Snapshot(widget, 0).Root, "Game");
    fake.Emit([Session("game", "Game", 0.82, muted: true)]);
    await WaitUntil(() => VolumesNear(widget.Sessions.Single().Volume, 0.82));
    var updated = Snapshot(widget, 1);
    Assert.Equal(0.82D, Node(updated.Root, $"{game}.volume.slider").Value);
    Assert.Equal(WidgetGlyph.Muted, Node(updated.Root, $"{game}.mute.icon").Glyph);
    await Background(widget);
}

static async Task OptimisticRollback()
{
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("game", "Game", 0.5)],
        ControlException = new WidgetCapabilityException("permission_denied", "denied"),
    };
    var widget = Create(fake);
    await ActivateReady(widget);
    var game = SessionPrefix(Snapshot(widget, 0).Root, "Game");
    await widget.OnActionAsync(new($"{game}.mute", $"{game}.mute"));
    await WaitUntil(() => fake.MuteRequests.Count == 1);
    await WaitUntil(() => widget.Sessions.Single().IsMuted == false);
    Assert.Equal(false, widget.Sessions.Single().IsMuted);
    Assert.Contains("permission denied", Text(Snapshot(widget, 1).Root, "audio.status").Text!);
    Assert.True(Text(Snapshot(widget, 2).Root, "audio.status").StyleClasses.Contains("is-error"),
        "Control denial did not expose error feedback.");

    fake.ControlException = new InvalidOperationException("provider details must not leak");
    await widget.OnActionAsync(new($"{game}.volume.set", $"{game}.volume.slider", RequestedValue: 0.55));
    await WaitUntil(() => fake.VolumeRequests.Count == 1);
    await WaitUntil(() => VolumesNear(widget.Sessions.Single().Volume, 0.5));
    Assert.Equal(0.5D, widget.Sessions.Single().Volume);
    var status = Text(Snapshot(widget, 3).Root, "audio.status").Text!;
    Assert.Contains("previous value restored", status);
    Assert.True(!status.Contains("provider details", StringComparison.Ordinal),
        "Provider exception details leaked into the UI.");
    await Background(widget);
}

static async Task MasterOutputControls()
{
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("game", "Game", 0.5)],
        Output = new WidgetAudioOutput(0.6, false),
        ControlGate = gate.Task,
    };
    var widget = Create(fake);
    await ActivateReady(widget);
    await widget.OnActionAsync(
        new("output.volume.set", "audio.master.volume.slider", RequestedValue: 0.65));
    await WaitUntil(() => fake.OutputVolumeRequests.Count == 1);
    Assert.Equal(0.65D, Node(Snapshot(widget, 1).Root, "audio.master.volume.slider").Value);
    Assert.True(Node(Snapshot(widget, 2).Root, "audio.master.volume.slider").IsBusy is not true,
        "Master volume pending state blocked rapid slider adjustment.");

    fake.EmitOutput(new WidgetAudioOutput(0.6, false));
    await Task.Delay(30);
    Assert.Equal(0.65D, Node(Snapshot(widget, 3).Root, "audio.master.volume.slider").Value);
    gate.SetResult();
    await Task.Delay(30);
    Assert.Equal(0.65D, Node(Snapshot(widget, 4).Root, "audio.master.volume.slider").Value);

    fake.ControlGate = null;
    fake.ControlException = new WidgetCapabilityException("permission_denied", "private");
    await widget.OnActionAsync(new("output.mute.toggle", "audio.master.volume.slider"));
    await WaitUntil(() => fake.OutputMuteRequests.Count == 1);
    await WaitUntil(() => widget.Output!.IsMuted == false);
    Assert.Equal(false, widget.Output!.IsMuted);
    Assert.Contains("permission denied", Text(Snapshot(widget, 5).Root, "audio.status").Text!);
    await Background(widget);
}

static async Task StableSelectionDuringChurn()
{
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("a", "Game", 0.3), Session("b", "Chat", 0.4), Session("c", "Music", 0.5)],
    };
    var widget = Create(fake);
    await ActivateReady(widget);
    var original = Snapshot(widget, 0);
    var stableB = SessionPrefix(original.Root, "Chat");
    await widget.OnActionAsync(new($"{stableB}.mute", $"{stableB}.mute"));
    Assert.Equal("b", widget.SelectedSessionId);

    fake.Emit([Session("c", "Music", 0.5), Session("b", "Chat renamed", 0.6), Session("d", "Browser", 0.2)]);
    await WaitUntil(() => widget.Sessions.First().SessionId == "c");
    Assert.Equal("b", widget.SelectedSessionId);
    var retained = Snapshot(widget, 1);
    Assert.Equal("Chat renamed", Text(retained.Root, $"{stableB}.name").Text);
    Assert.True(Nodes(retained.Root).Any(node => node.Id == $"{stableB}.volume.slider"),
        "The stable focus target changed after display-name/order churn.");
    Assert.Equal("audio.master.volume.slider", retained.InitialFocusId);

    fake.Emit([Session("c", "Music", 0.5), Session("d", "Browser", 0.2)]);
    await WaitUntil(() => widget.Sessions.Count == 2);
    Assert.Equal("d", widget.SelectedSessionId);
    Assert.Equal("audio.master.volume.slider", Snapshot(widget, 2).InitialFocusId);
    await Background(widget);
}

static async Task ManySessionsRemainBounded()
{
    var sessions = Enumerable.Range(0, 128)
        .Select(index => Session(
            $"opaque/session:{index}:\u2603",
            index == 73 ? new string('W', 4_096) : $"Application {index:000}",
            (index % 21) / 20D,
            muted: index % 7 == 0,
            active: index % 3 == 0))
        .ToArray();
    var fake = new FakeCapabilityClient { Sessions = sessions };
    var widget = Create(fake);
    await ActivateReady(widget);
    var snapshot = Snapshot(widget, 1);
    var scroll = Node(snapshot.Root, "audio.sessions.scroll");
    Assert.Equal(128, scroll.Children.Count);
    Assert.Equal(128, Sliders(scroll).Count());
    Assert.Equal(128, Sliders(scroll).Select(node => node.Id).Distinct(StringComparer.Ordinal).Count());
    Assert.True(Nodes(scroll).All(node => node.Id.Length <= 128),
        "A long or unsafe provider identifier escaped into a protocol node ID.");
    Assert.True(Nodes(scroll).All(node => node.Id.All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')),
        "An unsafe provider identifier escaped into a protocol node ID.");
    Assert.Valid(snapshot);

    var target = SessionPrefix(snapshot.Root, "Application 101");
    await widget.OnActionAsync(new($"{target}.volume.set", $"{target}.volume.slider", RequestedValue: 0));
    await WaitUntil(() => fake.VolumeRequests.Count == 1);
    Assert.Equal("opaque/session:101:☃", fake.VolumeRequests.Single().SessionId);
    await Background(widget);
}

static async Task CapabilityFailureStates()
{
    var cases = new[]
    {
        ("permission_denied", AudioMixerViewState.PermissionDenied, "Audio access is off"),
        ("lifecycle_denied", AudioMixerViewState.LifecycleDenied, "paused by lifecycle"),
        ("channel_closed", AudioMixerViewState.ChannelClosed, "disconnected"),
        ("platform_unavailable", AudioMixerViewState.ServiceUnavailable, "service unavailable"),
    };
    foreach (var (code, state, expectedTitle) in cases)
    {
        var fake = new FakeCapabilityClient
        {
            GetException = new WidgetCapabilityException(code, "private broker detail"),
        };
        var widget = Create(fake);
        await Activate(widget);
        await WaitUntil(() => widget.ViewState == state);
        var snapshot = Snapshot(widget, 1);
        Assert.Contains(expectedTitle, Text(snapshot.Root, "audio.state.title").Text!);
        Assert.True(!Nodes(snapshot.Root).Any(node =>
                node.Text?.Contains("private broker detail", StringComparison.Ordinal) == true),
            "Broker details leaked into a recovery state.");
        Assert.Valid(snapshot);
        await Background(widget);
    }
}

static async Task LiveProviderAvailability()
{
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("game", "Game", 0.5)],
    };
    var widget = Create(fake);
    await ActivateReady(widget);

    fake.Emit([], isAvailable: false);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.ServiceUnavailable);
    Assert.Contains("service unavailable",
        Text(Snapshot(widget, 1).Root, "audio.state.title").Text!);

    fake.Emit([Session("game", "Game", 0.6)], isAvailable: true);
    fake.EmitOutput(new WidgetAudioOutput(0.6, false), isAvailable: true);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.Ready);
    var recovered = Snapshot(widget, 2);
    var game = SessionPrefix(recovered.Root, "Game");
    Assert.Equal(0.6D, Node(recovered.Root, $"{game}.volume.slider").Value);
    await Background(widget);
}

static async Task UnavailableService()
{
    var widget = new AudioMixerWidget();
    await Activate(widget);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.ServiceUnavailable);
    var snapshot = Snapshot(widget, 1);
    Assert.Contains("service unavailable", Text(snapshot.Root, "audio.state.title").Text!);
    Assert.Valid(snapshot);
    await Background(widget);
}

static async Task LifecycleAndNoPolling()
{
    var fake = new FakeCapabilityClient { Sessions = [Session("a", "Game", 0.5)] };
    var widget = Create(fake);
    await WidgetTestHost.InitializeAsync(widget);
    Assert.Equal(0, fake.GetCalls);
    Assert.Equal(0, fake.SubscriptionCount);

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.Ready && fake.SubscriptionCount == 2);
    Assert.Equal(1, widget.ActivationCount);
    Assert.Equal(1, widget.FetchCount);
    Assert.Equal(1, fake.GetCalls);
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
    await Task.Delay(120);
    Assert.Equal(1, fake.GetCalls);
    Assert.Equal(2, fake.SubscriptionCount);

    await Background(widget);
    await WaitUntil(() => fake.CanceledSubscriptions == 2);
    var calls = fake.GetCalls;
    await Task.Delay(120);
    Assert.Equal(calls, fake.GetCalls);

    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
    await WaitUntil(() => fake.GetCalls == 2 && fake.SubscriptionCount == 4);
    Assert.Equal(2, widget.ActivationCount);
    await Background(widget);
}

static async Task SubscriptionPrecedesSnapshot()
{
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("old", "Old snapshot", 0.2)],
    };
    fake.OnGet = () => fake.Emit([Session("new", "Gap event", 0.8, active: true)]);
    var widget = Create(fake);
    await Activate(widget);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.Ready &&
                          widget.SelectedSessionId == "new");
    Assert.Equal(2, fake.SubscriptionCount);
    Assert.Equal(1, fake.GetCalls);
    Assert.Equal("Gap event", widget.Sessions.Single().DisplayName);
    Assert.Equal(0.8D, widget.Sessions.Single().Volume);
    await Background(widget);
}

static async Task CancellationIsNotFailure()
{
    var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new FakeCapabilityClient
    {
        Sessions = [Session("a", "Game", 0.5)],
        ControlGate = never.Task,
    };
    var widget = Create(fake);
    await ActivateReady(widget);
    var game = SessionPrefix(Snapshot(widget, 0).Root, "Game");
    await widget.OnActionAsync(
        new($"{game}.volume.set", $"{game}.volume.slider", RequestedValue: 0.55));
    await WaitUntil(() => fake.VolumeRequests.Count == 1);
    await Background(widget);
    await WaitUntil(() => VolumesNear(widget.Sessions.Single().Volume, 0.5));
    Assert.Equal(0.5D, widget.Sessions.Single().Volume);
    Assert.True(!Text(Snapshot(widget, 1).Root, "audio.status").StyleClasses.Contains("is-error"),
        "Normal lifecycle cancellation rendered as an error.");
}

static async Task ShippedAssetsValidate()
{
    var project = ProjectDirectory();
    var manifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(Path.Combine(project, "manifest.json")));
    var errors = WidgetManifestValidator.Validate(manifest);
    Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    Assert.SequenceEqual(["system.audio.sessions.read.v1", "system.audio.output.read.v1"], manifest.Permissions);
    Assert.SequenceEqual(["system.audio.sessions.control.v1", "system.audio.output.control.v1"], manifest.OptionalPermissions);
    Assert.Equal("suspend", manifest.BackgroundPolicy);
    Assert.Equal(64, manifest.ResourceRequest.MemoryMb);
    Assert.SequenceEqual(["x64"], manifest.Architectures);
    var package = GbssPackageLoader.LoadFile(
        Path.Combine(project, "styles", "default.gbss"),
        Path.Combine(project, "styles"));
    var compiled = GbssThemeCompiler.Compile(package);
    Assert.True(compiled.IsValid, string.Join(Environment.NewLine, compiled.Diagnostics));
    AssertResponsiveLayoutBudget(compiled.Theme!);
    var style = await File.ReadAllTextAsync(Path.Combine(project, "styles", "default.gbss"));
    Assert.Contains("width: 100vw", style);
    Assert.Contains("max-width: 560px", style);
    Assert.Contains(".audio-session-list", style);
    Assert.Contains("max-height: 340px", style);
    Assert.Contains(".audio-volume-slider", style);
    Assert.Contains(".audio-mute-icon", style);
    Assert.True(!style.Contains(".audio-volume-action", StringComparison.Ordinal) &&
                !style.Contains(".audio-mute-action", StringComparison.Ordinal),
        "Legacy plus/minus or text-pill mute styles remain in the shipped theme.");
    Assert.True(!style.Contains("min-width: 440px", StringComparison.Ordinal),
        "The widget retained a desktop-only hard width floor.");

    var catalogPath = Path.Combine(project, "..", "..", "OverlayHost", "widget-catalog.json");
    using var catalog = JsonDocument.Parse(await File.ReadAllBytesAsync(catalogPath));
    var trusted = catalog.RootElement.GetProperty("widgets").EnumerateArray().Single(item =>
        item.GetProperty("packageId").GetString() == manifest.Id);
    Assert.Equal(manifest.Publisher, trusted.GetProperty("publisherId").GetString());
    Assert.Equal(manifest.ResourceRequest.MemoryMb, trusted.GetProperty("memoryLimitMb").GetInt32());
    var manifestCapabilities = manifest.Permissions.Concat(manifest.OptionalPermissions)
        .Order(StringComparer.Ordinal).ToArray();
    var trustedCapabilities = trusted.GetProperty("declaredCapabilities").EnumerateArray()
        .Select(item => item.GetString()!).Order(StringComparer.Ordinal).ToArray();
    Assert.SequenceEqual(manifestCapabilities, trustedCapabilities);
}

static void AssertResponsiveLayoutBudget(GbssTheme theme)
{
    var root = Resolve(theme, "stack", "audio.root", "audio-mixer-widget");
    var card = Resolve(theme, "stack", "audio.session.test.row", "audio-session-card");
    var controls = Resolve(theme, "row", "audio.session.test.controls", "audio-volume-control-row");
    var slider = Resolve(theme, "slider", "audio.session.test.volume.slider", "audio-volume-slider");
    var muteIcon = Resolve(theme, "icon", "audio.session.test.mute.icon", "audio-mute-icon");
    var value = Resolve(theme, "text", "audio.session.test.volume.value", "audio-volume-value");
    var list = Resolve(theme, "scroll", "audio.sessions.scroll", "audio-session-list");

    Assert.True(Pixels(slider.Get("height")!, 280) >= 44,
        "The slider focus target fell below the compact 44px controller budget.");
    Assert.Equal(30D, Pixels(muteIcon.Get("width")!, 280));
    Assert.Equal(30D, Pixels(muteIcon.Get("height")!, 280));
    Assert.True(Pixels(list.Get("min-height")!, 280) >= 150 &&
                Pixels(list.Get("max-height")!, 280) <= 360,
        "The application list escaped its bounded scroll viewport.");

    foreach (var viewport in new[] { 280D, 320D, 1280D, 3840D })
    {
        var preferred = Pixels(root.Get("width")!, viewport);
        var rootWidth = Math.Min(viewport, Math.Clamp(
            preferred,
            Pixels(root.Get("min-width")!, viewport),
            Pixels(root.Get("max-width")!, viewport)));
        var rootInner = rootWidth - HorizontalSpacing(root.Get("padding")!, viewport);
        var cardInner = rootInner - HorizontalSpacing(card.Get("padding")!, viewport);

        var controlMinimum =
            Pixels(muteIcon.Get("width")!, viewport) +
            Pixels(slider.Get("min-width")!, viewport) +
            Pixels(value.Get("width")!, viewport) +
            2 * Pixels(controls.Get("gap")!, viewport);
        Assert.True(controlMinimum <= cardInner,
            $"Audio controls need {controlMinimum}px but only {cardInner}px is available at {viewport}px.");

        Assert.True(rootWidth <= 560 && rootWidth <= viewport,
            $"Root width {rootWidth}px escaped its viewport/max bound at {viewport}px.");
    }
}

static GbssResolvedStyle Resolve(GbssTheme theme, string role, string id, string styleClass) =>
    theme.Resolve(new GbssElement(role, id,
        new HashSet<string>([styleClass], StringComparer.Ordinal)));

static double Pixels(GbssComputedValue value, double viewport)
{
    var unit = value.Unit;
    var number = value.Number;
    if (unit is null)
    {
        unit = value.Text.EndsWith("px", StringComparison.Ordinal) ? "px" :
            value.Text.EndsWith("vw", StringComparison.Ordinal) ? "vw" : null;
        if (unit is not null && double.TryParse(value.Text[..^unit.Length], out var parsed))
            number = parsed;
    }
    return unit switch
    {
        "px" => number ?? throw new InvalidOperationException("Length has no numeric value."),
        "vw" => (number ?? 0) * viewport / 100,
        _ => throw new InvalidOperationException($"Unsupported test length '{value.Text}'."),
    };
}

static double HorizontalSpacing(GbssComputedValue value, double viewport)
{
    var parts = value.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    static GbssComputedValue Part(string text)
    {
        var unit = text.EndsWith("px", StringComparison.Ordinal) ? "px" :
            text.EndsWith("vw", StringComparison.Ordinal) ? "vw" : null;
        if (unit is null || !double.TryParse(text[..^unit.Length], out var number))
            throw new InvalidOperationException($"Unsupported spacing '{text}'.");
        return new GbssComputedValue(GbssValueKind.Length, text, number, unit);
    }
    return parts.Length switch
    {
        1 => 2 * Pixels(Part(parts[0]), viewport),
        2 or 3 => 2 * Pixels(Part(parts[1]), viewport),
        4 => Pixels(Part(parts[1]), viewport) + Pixels(Part(parts[3]), viewport),
        _ => throw new InvalidOperationException($"Unsupported spacing list '{value.Text}'."),
    };
}

static AudioMixerWidget Create(FakeCapabilityClient fake)
{
    return WidgetTestHost.Attach(new AudioMixerWidget(), fake.BuildServices());
}

static async Task Activate(AudioMixerWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);

static async Task ActivateReady(AudioMixerWidget widget)
{
    await Activate(widget);
    await WaitUntil(() => widget.ViewState == AudioMixerViewState.Ready);
}

static async Task Background(AudioMixerWidget widget) =>
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);

static async ValueTask<bool> Route(
    AudioMixerWidget widget,
    ViewSnapshot snapshot,
    ControllerButton button,
    string focusedElementId,
    double? requestedValue = null) =>
    await widget.OnControllerInputAsync(new ControllerInputEvent(
        button,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        FocusedElementId: focusedElementId,
        Sequence: 7,
        ActiveInputScopeId: snapshot.ActiveInputScopeId,
        SnapshotSequence: snapshot.Sequence,
        RequestedValue: requestedValue));

static WidgetAudioSession Session(
    string id,
    string name,
    double volume,
    bool muted = false,
    bool active = false) => new(id, name, volume, muted, active);

static bool VolumesNear(double left, double right) => Math.Abs(left - right) <= 0.001;

static ViewSnapshot Snapshot(AudioMixerWidget widget, long sequence) =>
    widget.Render().CreateSnapshot("audio.test", sequence);

static IEnumerable<ViewNode> Nodes(ViewNode node)
{
    yield return node;
    foreach (var child in node.Children)
    foreach (var descendant in Nodes(child))
        yield return descendant;
}

static IEnumerable<ViewNode> Buttons(ViewNode root) =>
    Nodes(root).Where(node => node.Kind == ViewNodeKind.Button);

static IEnumerable<ViewNode> Sliders(ViewNode root) =>
    Nodes(root).Where(node => node.Kind == ViewNodeKind.Slider);

static ViewNode Node(ViewNode root, string id) =>
    Nodes(root).Single(node => node.Id == id);

static ViewNode Text(ViewNode root, string id) =>
    Nodes(root).Single(node => node.Id == id && node.Kind == ViewNodeKind.Text);

static string SessionPrefix(ViewNode root, string displayName)
{
    var name = Nodes(root).Single(node =>
        node.Kind == ViewNodeKind.Text &&
        string.Equals(node.Text, displayName, StringComparison.Ordinal) &&
        node.Id.EndsWith(".name", StringComparison.Ordinal));
    return name.Id[..^".name".Length];
}

static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds = 2_000)
{
    var deadline = Environment.TickCount64 + timeoutMilliseconds;
    while (!condition())
    {
        if (Environment.TickCount64 >= deadline)
            throw new TimeoutException("Timed out waiting for asynchronous widget state.");
        await Task.Delay(10);
    }
}

static string ProjectDirectory()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName, "src", "FirstPartyWidgets", "AudioMixerWidget");
        if (Directory.Exists(candidate)) return candidate;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate AudioMixerWidget project directory.");
}

file sealed class FakeCapabilityClient
{
    private readonly object _gate = new();
    private readonly List<Channel<WidgetAudioSessionsChanged>> _subscribers = [];
    private readonly List<Channel<WidgetAudioOutputChanged>> _outputSubscribers = [];
    private readonly Queue<ControlPlan> _sessionVolumePlans = [];
    private readonly Queue<ControlPlan> _sessionMutePlans = [];
    private readonly Queue<ControlPlan> _outputVolumePlans = [];
    private readonly Queue<ControlPlan> _outputMutePlans = [];
    private int _getCalls;
    private int _subscriptionCount;
    private int _canceledSubscriptions;

    public bool IsAvailable => true;
    public IReadOnlyList<WidgetAudioSession> Sessions { get; set; } = [];
    public WidgetAudioOutput Output { get; set; } = new(0.6, false);
    public Exception? GetException { get; set; }
    public Exception? ControlException { get; set; }
    public Task? ControlGate { get; set; }
    public Action? OnGet { get; set; }
    public List<SetWidgetAudioSessionVolumeRequest> VolumeRequests { get; } = [];
    public List<SetWidgetAudioSessionMutedRequest> MuteRequests { get; } = [];
    public List<SetWidgetAudioOutputVolumeRequest> OutputVolumeRequests { get; } = [];
    public List<SetWidgetAudioOutputMutedRequest> OutputMuteRequests { get; } = [];
    public int GetCalls => Volatile.Read(ref _getCalls);
    public int SubscriptionCount => Volatile.Read(ref _subscriptionCount);
    public int CanceledSubscriptions => Volatile.Read(ref _canceledSubscriptions);

    public void PlanSessionVolume(Task? gate = null, Exception? failure = null)
    {
        lock (_gate) _sessionVolumePlans.Enqueue(new ControlPlan(gate, failure));
    }

    public void PlanSessionMute(Task? gate = null, Exception? failure = null)
    {
        lock (_gate) _sessionMutePlans.Enqueue(new ControlPlan(gate, failure));
    }

    public void PlanOutputVolume(Task? gate = null, Exception? failure = null)
    {
        lock (_gate) _outputVolumePlans.Enqueue(new ControlPlan(gate, failure));
    }

    public void PlanOutputMute(Task? gate = null, Exception? failure = null)
    {
        lock (_gate) _outputMutePlans.Enqueue(new ControlPlan(gate, failure));
    }

    public WidgetHostServices BuildServices() => new WidgetTestHostServicesBuilder()
        .WithHandler(
            WidgetAudioCapabilities.GetSessions,
            (request, cancellationToken) => InvokeAsync(
                WidgetAudioCapabilities.GetSessions, request, cancellationToken))
        .WithHandler(
            WidgetAudioCapabilities.GetOutput,
            (request, cancellationToken) => InvokeAsync(
                WidgetAudioCapabilities.GetOutput, request, cancellationToken))
        .WithHandler(
            WidgetAudioCapabilities.SetSessionVolume,
            (request, cancellationToken) => InvokeAsync(
                WidgetAudioCapabilities.SetSessionVolume, request, cancellationToken))
        .WithHandler(
            WidgetAudioCapabilities.SetSessionMuted,
            (request, cancellationToken) => InvokeAsync(
                WidgetAudioCapabilities.SetSessionMuted, request, cancellationToken))
        .WithHandler(
            WidgetAudioCapabilities.SetOutputVolume,
            (request, cancellationToken) => InvokeAsync(
                WidgetAudioCapabilities.SetOutputVolume, request, cancellationToken))
        .WithHandler(
            WidgetAudioCapabilities.SetOutputMuted,
            (request, cancellationToken) => InvokeAsync(
                WidgetAudioCapabilities.SetOutputMuted, request, cancellationToken))
        .WithEventStream(
            WidgetAudioCapabilities.SessionsChanged,
            OpenEventStream)
        .WithEventStream(
            WidgetAudioCapabilities.OutputChanged,
            OpenOutputEventStream)
        .Build();

    public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        WidgetCapabilityOperation<TRequest, TResponse> operation,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        if (operation.OperationId == WidgetAudioCapabilities.GetSessions.OperationId)
        {
            Interlocked.Increment(ref _getCalls);
            if (GetException is not null) throw GetException;
            var snapshot = Sessions.ToArray();
            OnGet?.Invoke();
            return (TResponse)(object)snapshot;
        }
        if (operation.OperationId == WidgetAudioCapabilities.GetOutput.OperationId)
        {
            if (GetException is not null) throw GetException;
            return (TResponse)(object)Output;
        }
        if (operation.OperationId == WidgetAudioCapabilities.SetSessionVolume.OperationId)
        {
            ControlPlan plan;
            lock (_gate)
            {
                VolumeRequests.Add((SetWidgetAudioSessionVolumeRequest)(object)request!);
                plan = NextPlan(_sessionVolumePlans);
            }
            if (plan.Gate is not null) await plan.Gate.WaitAsync(cancellationToken);
            if (plan.Failure is not null) throw plan.Failure;
            return (TResponse)(object)new WidgetCapabilityAcknowledgement(true);
        }
        if (operation.OperationId == WidgetAudioCapabilities.SetSessionMuted.OperationId)
        {
            ControlPlan plan;
            lock (_gate)
            {
                MuteRequests.Add((SetWidgetAudioSessionMutedRequest)(object)request!);
                plan = NextPlan(_sessionMutePlans);
            }
            if (plan.Gate is not null) await plan.Gate.WaitAsync(cancellationToken);
            if (plan.Failure is not null) throw plan.Failure;
            return (TResponse)(object)new WidgetCapabilityAcknowledgement(true);
        }
        if (operation.OperationId == WidgetAudioCapabilities.SetOutputVolume.OperationId)
        {
            var control = (SetWidgetAudioOutputVolumeRequest)(object)request!;
            ControlPlan plan;
            lock (_gate)
            {
                OutputVolumeRequests.Add(control);
                plan = NextPlan(_outputVolumePlans);
            }
            if (plan.Gate is not null) await plan.Gate.WaitAsync(cancellationToken);
            if (plan.Failure is not null) throw plan.Failure;
            Output = Output with { Volume = control.Volume };
            return (TResponse)(object)new WidgetCapabilityAcknowledgement(true);
        }
        if (operation.OperationId == WidgetAudioCapabilities.SetOutputMuted.OperationId)
        {
            var control = (SetWidgetAudioOutputMutedRequest)(object)request!;
            ControlPlan plan;
            lock (_gate)
            {
                OutputMuteRequests.Add(control);
                plan = NextPlan(_outputMutePlans);
            }
            if (plan.Gate is not null) await plan.Gate.WaitAsync(cancellationToken);
            if (plan.Failure is not null) throw plan.Failure;
            Output = Output with { IsMuted = control.IsMuted };
            return (TResponse)(object)new WidgetCapabilityAcknowledgement(true);
        }
        throw new WidgetCapabilityException("unsupported_operation", operation.OperationId);
    }

    private ControlPlan NextPlan(Queue<ControlPlan> plans) =>
        plans.Count == 0 ? new ControlPlan(ControlGate, ControlException) : plans.Dequeue();

    private IAsyncEnumerable<WidgetAudioSessionsChanged> OpenEventStream(
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<WidgetAudioSessionsChanged>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        lock (_gate) _subscribers.Add(channel);
        Interlocked.Increment(ref _subscriptionCount);
        return ReadEvents(channel, cancellationToken);
    }

    private IAsyncEnumerable<WidgetAudioOutputChanged> OpenOutputEventStream(
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<WidgetAudioOutputChanged>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        lock (_gate) _outputSubscribers.Add(channel);
        Interlocked.Increment(ref _subscriptionCount);
        return ReadOutputEvents(channel, cancellationToken);
    }

    private async IAsyncEnumerable<WidgetAudioSessionsChanged> ReadEvents(
        Channel<WidgetAudioSessionsChanged> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                yield return item;
        }
        finally
        {
            lock (_gate) _subscribers.Remove(channel);
            Interlocked.Increment(ref _canceledSubscriptions);
        }
    }

    private async IAsyncEnumerable<WidgetAudioOutputChanged> ReadOutputEvents(
        Channel<WidgetAudioOutputChanged> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                yield return item;
        }
        finally
        {
            lock (_gate) _outputSubscribers.Remove(channel);
            Interlocked.Increment(ref _canceledSubscriptions);
        }
    }

    public void Emit(IReadOnlyList<WidgetAudioSession> sessions, bool isAvailable = true)
    {
        Sessions = sessions;
        Channel<WidgetAudioSessionsChanged>[] subscribers;
        lock (_gate) subscribers = _subscribers.ToArray();
        foreach (var subscriber in subscribers)
            subscriber.Writer.TryWrite(new WidgetAudioSessionsChanged(sessions, isAvailable));
    }

    public void EmitOutput(WidgetAudioOutput? output, bool isAvailable = true)
    {
        if (output is not null) Output = output;
        Channel<WidgetAudioOutputChanged>[] subscribers;
        lock (_gate) subscribers = _outputSubscribers.ToArray();
        foreach (var subscriber in subscribers)
            subscriber.Writer.TryWrite(new WidgetAudioOutputChanged(output, isAvailable));
    }
}

file sealed record ControlPlan(Task? Gate, Exception? Failure);

file static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
    }

    public static void Near(double expected, double actual, double tolerance = 0.000001)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    public static async Task ThrowsCanceled(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        throw new InvalidOperationException("Expected an OperationCanceledException.");
    }

    public static void Valid(ViewSnapshot snapshot)
    {
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }
}
