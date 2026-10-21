using LauncherWidget = WidgetRail.Samples.PlayniteLibrary.PlayniteLibraryWidget;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    [TestMethod]
    public async Task InspectionFailureShowsReachableSettingsInsteadOfSilentlySkippingSetup()
    {
        var host = new FakeHost(1);
        var services = host.Services();
        var setup = new SetupFixture { InspectionFailure = true };
        var widget = WidgetTestHost.Attach(new LauncherWidget(new TestApplicationService(services, host),
            new FakeConnectionClient(), setupService: setup), services);
        await Interactive(widget);
        try
        {
            await WaitFor(() => widget.RenderState.Value.SetupDialogOpen, "inspection failure guidance");
            var snapshot = Snapshot(widget, 175_001);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
            var initial = Nodes(snapshot.Root).Single(node => node.Id == snapshot.InitialFocusId);
            Assert.AreEqual(LauncherWidget.PlayniteOpenActionId, initial.ActionId);
            Assert.IsFalse(initial.IsDisabled == true);
            Assert.IsTrue(Nodes(snapshot.Root).Any(node => node.Text == "Fixture inspection failed."));
        }
        finally { await Background(widget); }
    }

    [TestMethod]
    [DataRow("Install", true, false)]
    [DataRow("Install", false, false)]
    [DataRow("Start", false, false)]
    [DataRow("Connect", true, false)]
    [DataRow("Enable", true, false)]
    [DataRow("Repair", true, false)]
    [DataRow("Restart", true, false)]
    [DataRow("Manual", true, false)]
    [DataRow("Install", true, true)]
    [DataRow("Start", false, true)]
    public void AutomaticSetupDialogMatrixKeepsAnEnabledInitialControl(string action, bool running, bool busy)
    {
        var fixture = new SetupFixture();
        var state = new PlayniteSetupView { Plan = fixture.Plan with
            { Action = Enum.Parse<PlayniteSetupAction>(action), Installation = fixture.Installation with { Running = running } },
            Busy = busy, Progress = busy ? "Connecting…" : null };
        var snapshot = new WidgetView(UI.Stack("base"))
            .WithModal(PlayniteLibraryConnectionPresentation.SetupDialog(state)).CreateSnapshot("matrix", 1);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        var initial = Nodes(snapshot.Root).Single(node => node.Id == snapshot.InitialFocusId);
        Assert.IsFalse(initial.IsDisabled == true);
        Assert.AreEqual(action == "Manual" ? LauncherWidget.PlayniteOpenActionId :
            action == "Start" ? LauncherWidget.SetupStartAction : LauncherWidget.SetupPrefix + "apply", initial.ActionId);
        Assert.AreEqual(busy, initial.IsBusy == true);
    }

    [TestMethod]
    public async Task RunningPortableWithoutBridgeAutomaticallyOffersInstallAndRestart()
    {
        var host = new FakeHost(1);
        var services = host.Services();
        var setup = new SetupFixture { MissingIntegration = true, Running = true };
        var widget = WidgetTestHost.Attach(new LauncherWidget(new TestApplicationService(services, host),
            new FakeConnectionClient(), setupService: setup), services);
        await Interactive(widget);
        try
        {
            await WaitFor(() => widget.RenderState.Value.SetupDialogOpen, "running portable setup");
            var snapshot = Snapshot(widget, 174_001);
            var primary = Nodes(snapshot.Root).Single(node => node.Id == snapshot.InitialFocusId);
            Assert.AreEqual("Install integration and restart Playnite", primary.Text);
            Assert.AreEqual(LauncherWidget.SetupPrefix + "apply", primary.ActionId);
            Assert.AreEqual(0, setup.ApplyCalls, "Discovery never installs without a chosen action.");
        }
        finally { await Background(widget); }
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(2, true)]
    public void SetupProfileSelectionMatrixRendersBeforeAndAfterSelection(int count, bool selected)
    {
        var setup = new SetupFixture();
        var installations = Enumerable.Range(0, count).Select(index => setup.Installation with { Id = "profile" + index }).ToArray();
        var state = new PlayniteSetupView { Installations = installations,
            Plan = selected ? setup.Plan with { Installation = installations[1] } : null };
        var snapshot = PlayniteLibraryConnectionPresentation.Render(new(PlayniteBridgeConnectionKind.Unavailable,
            false, "offline", true, null, state)).CreateSnapshot("profiles", 1);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        var picker = Nodes(snapshot.Root).SingleOrDefault(node => node.Id == LauncherWidget.SetupPrefix + "installation");
        if (count > 1)
        {
            Assert.IsNotNull(picker);
            Assert.AreEqual(1, picker.SelectOptions.Count(option => option.IsSelected));
            Assert.AreEqual(!selected, picker.SelectOptions.Single(option => option.IsSelected).IsDisabled);
        }
        else Assert.IsNull(picker);
    }

    [TestMethod]
    public async Task EnteringFromPreviewRechecksStoppedPlayniteAndOffersMissingIntegrationSetup()
    {
        var host = new FakeHost(1);
        var services = host.Services();
        var setup = new SetupFixture();
        var widget = WidgetTestHost.Attach(new LauncherWidget(new TestApplicationService(services, host),
            new FakeConnectionClient(), setupService: setup), services);
        await Visible(widget);
        await Ready(widget, host);
        setup.Stopped = true;
        setup.MissingIntegration = true;
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        try
        {
            await WaitFor(() => widget.RenderState.Value.SetupDialogOpen, "preview entry detects stopped Playnite");
            var snapshot = Snapshot(widget, 173_001);
            var primary = Nodes(snapshot.Root).Single(node => node.Id == snapshot.InitialFocusId);
            Assert.AreEqual(LauncherWidget.SetupPrefix + "apply", primary.ActionId);
            Assert.AreEqual("Install integration and start Playnite", primary.Text);
            Assert.IsFalse(primary.IsDisabled == true);
            await widget.OnActionAsync(new(LauncherWidget.SetupPrefix + "dismiss", "playnite-start-dialog.close"));
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await WaitFor(() => widget.RenderState.Value.SetupDialogOpen, "reopening rechecks");
            await widget.OnActionAsync(new(primary.ActionId!, primary.Id));
            await WaitFor(() => setup.ApplyCalls == 1 && !widget.RenderState.Value.Setup.Busy, "setup applied");
            Assert.AreEqual(0, setup.StartCalls, "A missing integration must use installation setup, not a launch that can never connect.");
        }
        finally { await Background(widget); }
    }

    [TestMethod]
    public async Task StoppedPlayniteOpensFocusedDialogAndDismissesToReachableRecovery()
    {
        var warm = new PlayniteLibraryPrivateState(PlayniteLibraryPrivateState.CurrentVersion,
            [new("saved-offline", "Cached game", "Steam")]);
        var host = new FakeHost(1, new WidgetTestPrivateState(System.Text.Json.JsonSerializer.Serialize(warm), 1))
            { FailAfterOffset = 0 };
        var services = host.Services();
        var setup = new SetupFixture { Stopped = true, Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var widget = WidgetTestHost.Attach(new LauncherWidget(new TestApplicationService(services, host),
            new FakeConnectionClient(), setupService: setup), services);
        await Interactive(widget);
        try
        {
            await WaitFor(() => widget.Collection.Status == WidgetPagedResourceStatus.Error &&
                widget.RenderState.Value.SetupRequired, "stopped cached library");
            var modal = Snapshot(widget, 172_001);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(modal).Count);
            Assert.AreEqual("playnite-start-dialog.scope", modal.ActiveInputScopeId);
            Assert.AreEqual(LauncherWidget.SetupPrefix + "stopped.primary", modal.InitialFocusId);
            Assert.IsNull(modal.FocusGroupEntryRequest);
            var primary = Nodes(modal.Root).Single(node => node.Id == modal.InitialFocusId);
            Assert.IsFalse(primary.IsDisabled == true);
            Assert.IsFalse(Nodes(modal.Root).Any(node => node.Kind == ViewNodeKind.IndexedCollection),
                "Offline recovery must not leave focusable collection shells full of disabled cached games.");
            await widget.OnActionAsync(new(LauncherWidget.SetupPrefix + "dismiss", "playnite-start-dialog.close"));
            var page = Snapshot(widget, 172_002);
            Assert.IsFalse(Nodes(page.Root).Any(node => node.Kind == ViewNodeKind.ModalLayer));
            Assert.AreEqual("playnite-library.offline.action", page.InitialFocusId);
            Assert.IsFalse(Nodes(page.Root).Single(node => node.Id == page.InitialFocusId).IsDisabled == true);
            await widget.OnActionAsync(new(LauncherWidget.SetupPrefix + "prompt", page.InitialFocusId!));
            Assert.AreEqual("playnite-start-dialog.scope", Snapshot(widget, 172_003).ActiveInputScopeId);
            await widget.OnActionAsync(new(LauncherWidget.SetupStartAction, LauncherWidget.SetupStartAction));
            await WaitFor(() => setup.StartCalls == 1, "start from dialog");
            var busy = Snapshot(widget, 172_004);
            Assert.AreEqual(LauncherWidget.SetupPrefix + "stopped.primary", busy.InitialFocusId);
            Assert.IsTrue(Nodes(busy.Root).Single(node => node.Id == busy.InitialFocusId).IsBusy == true);
            setup.Pending.SetResult(new(false, "Not ready", "Finish the Playnite startup dialog, then retry."));
            await WaitFor(() => !widget.RenderState.Value.Setup.Busy, "failed start");
            Assert.AreEqual("playnite-start-dialog.scope", Snapshot(widget, 172_005).ActiveInputScopeId);
            setup.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await widget.OnActionAsync(new(LauncherWidget.SetupStartAction, LauncherWidget.SetupStartAction));
            await WaitFor(() => setup.StartCalls == 2, "second start");
            await widget.OnActionAsync(new(LauncherWidget.SetupPrefix + "dismiss", "playnite-start-dialog.close"));
            setup.Pending.SetResult(new(false, "Not ready", "Try again."));
            await WaitFor(() => !widget.RenderState.Value.Setup.Busy, "dismissed operation finishes");
            Assert.AreEqual("playnite-library.offline.action", Snapshot(widget, 172_007).InitialFocusId,
                "Finishing a dismissed operation must not navigate to another page.");
            await widget.OnActionAsync(new(LauncherWidget.SetupPrefix + "prompt", "playnite-library.offline.action"));
            setup.Pending = null;
            host.FailAfterOffset = null;
            await widget.OnActionAsync(new(LauncherWidget.SetupStartAction, LauncherWidget.SetupStartAction));
            await WaitFor(() => !widget.RenderState.Value.SetupRequired &&
                widget.Collection.Status == WidgetPagedResourceStatus.Ready, "start recovery");
            Assert.IsFalse(Nodes(Snapshot(widget, 172_006).Root).Any(node => node.Kind == ViewNodeKind.ModalLayer));
        }
        finally { setup.Pending?.TrySetCanceled(); await Background(widget); }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void GuidedSetupUsesStablePrimaryActionAndKeepsManualTokenUnderAdvanced(bool busy, bool advanced)
    {
        var setup = new SetupFixture();
        var state = new PlayniteSetupView { Installations = [setup.Installation], Plan = setup.Plan,
            Busy = busy, Advanced = advanced, Progress = busy ? "Connecting…" : null };
        var view = PlayniteLibraryConnectionPresentation.Render(new(PlayniteBridgeConnectionKind.NotConfigured,
            false, "credential_missing", true, null, state)).CreateSnapshot("setup", 1);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(view).Count,
            string.Join("; ", ViewSnapshotValidator.Validate(view).Select(error => error.Message)));
        var nodes = Nodes(view.Root).ToArray();
        var primary = nodes.Single(node => node.Id == LauncherWidget.SetupPrefix + "apply");
        Assert.IsFalse(primary.IsDisabled == true, "Progress must not disable and move focus away from the active action.");
        Assert.AreEqual(busy, primary.IsBusy == true);
        Assert.AreEqual(advanced, nodes.Any(node => node.Kind == ViewNodeKind.TextEntry && node.TextEntryInputKind == TextEntryInputKind.Sensitive));
        Assert.AreEqual(LauncherWidget.SetupPrefix + "apply", view.InitialFocusId);
    }

    [TestMethod]
    public async Task SetupDetectsStoppedPlayniteAndAdmitsOnlyOneStartOperation()
    {
        var host = new FakeHost(1);
        var services = host.Services();
        var setup = new SetupFixture { Stopped = true, Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var application = new TestApplicationService(services, host);
        var widget = WidgetTestHost.Attach(new LauncherWidget(application, new FakeConnectionClient(), setupService: setup), services);
        await Interactive(widget);
        await Ready(widget, host);
        await WaitFor(() => widget.RenderState.Value.SetupRequired, "stopped Playnite action");
        var before = Snapshot(widget, 170_001);
        Assert.IsTrue(Nodes(before.Root).Any(node => node.ActionId == LauncherWidget.SetupStartAction));
        await widget.OnActionAsync(new(LauncherWidget.SetupStartAction, LauncherWidget.SetupStartAction));
        await WaitFor(() => setup.StartCalls == 1, "start requested");
        await widget.OnActionAsync(new(LauncherWidget.SetupStartAction, LauncherWidget.SetupStartAction));
        Assert.AreEqual(1, setup.StartCalls);
        Assert.AreEqual(0, setup.ApplyCalls, "Starting stopped Playnite must not install or replace its integration.");
        var busy = Snapshot(widget, 170_002);
        Assert.IsTrue(Nodes(busy.Root).Single(node => node.ActionId == LauncherWidget.SetupStartAction).IsBusy == true);
        setup.Pending.SetResult(new(true, "Connected", "Ready"));
        await WaitFor(() => !widget.RenderState.Value.Setup.Busy && !widget.RenderState.Value.SetupRequired, "connected");
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(Snapshot(widget, 170_003)).Count);
        await Background(widget);
    }

    [TestMethod]
    public async Task SetupSelectionChecksBeforeApplyingAndReturnsToLibraryAfterSuccess()
    {
        var host = new FakeHost(1);
        var services = host.Services();
        var setup = new SetupFixture();
        var widget = WidgetTestHost.Attach(new LauncherWidget(new TestApplicationService(services, host),
            new FakeConnectionClient(), setupService: setup), services);
        await Interactive(widget);
        await Ready(widget, host);
        await widget.OnActionAsync(new(LauncherWidget.PlayniteOpenActionId, "menu"));
        await WaitFor(() => !widget.RenderState.Value.Setup.Busy && widget.RenderState.Value.Setup.Plan is not null, "setup inspection");
        Assert.AreEqual(0, setup.ApplyCalls);
        var checkedView = Snapshot(widget, 171_001);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(checkedView).Count);
        await widget.OnActionAsync(new(LauncherWidget.SetupPrefix + "apply", LauncherWidget.SetupPrefix + "apply"));
        await WaitFor(() => setup.ApplyCalls == 1 && !widget.RenderState.Value.Setup.Busy, "apply complete");
        Assert.IsTrue(widget.RenderState.Value.Setup.Connected);
        Assert.AreEqual(PlayniteBridgeConnectionKind.Connected, widget.RenderState.Value.PlayniteKind);
        await WaitFor(() => !Nodes(Snapshot(widget, 171_002).Root).Any(node => node.Id == "playnite-library.playnite.root"), "return to library");
        await Background(widget);
    }

    private sealed class SetupFixture : IPlayniteSetupService
    {
        internal readonly PlayniteSetupInstallation Installation = new("fixture", @"C:\Games\Playnite", @"D:\Profiles\Playnite", "Playnite profile", false, false);
        internal PlayniteSetupPlan Plan => new(Installation with { Running = Running }, Stopped && !MissingIntegration ? PlayniteSetupAction.Start : PlayniteSetupAction.Install,
            "Connect Playnite", "Install integration and connect.", null, null, null);
        internal bool Running;
        internal bool MissingIntegration;
        internal bool InspectionFailure;
        internal bool Stopped;
        internal int ApplyCalls;
        internal int StartCalls;
        internal TaskCompletionSource<PlayniteSetupResult>? Pending;
        public ValueTask<PlayniteSetupOffer> CheckSetupAsync(CancellationToken token) => InspectionFailure
            ? ValueTask.FromException<PlayniteSetupOffer>(new PlayniteSetupException("Fixture inspection failed."))
            : ValueTask.FromResult(new PlayniteSetupOffer(new([Installation]), Plan, Stopped || MissingIntegration));
        public ValueTask<PlayniteSetupDiscovery> DiscoverAsync(string? programPath, string? dataPath, CancellationToken token) => ValueTask.FromResult(new PlayniteSetupDiscovery([Installation]));
        public ValueTask<PlayniteSetupPlan> InspectAsync(PlayniteSetupInstallation installation, CancellationToken token) => ValueTask.FromResult(Plan);
        public ValueTask<PlayniteSetupResult> StartAsync(PlayniteSetupInstallation installation, Action<string> progress, CancellationToken token)
        { StartCalls++; return Pending is null ? ValueTask.FromResult(new PlayniteSetupResult(true, "Connected", "Ready")) : new(Pending.Task.WaitAsync(token)); }
        public ValueTask<PlayniteSetupResult> ApplyAsync(PlayniteSetupPlan plan, Action<string> progress, CancellationToken token)
        { ApplyCalls++; return ValueTask.FromResult(new PlayniteSetupResult(true, "Connected", "Ready")); }
    }
}
