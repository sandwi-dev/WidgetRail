using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class AppUpdateScenarios
{
    internal static async Task ControllerFlow()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-update-settings-" + Guid.NewGuid().ToString("N"));
        var service = new Service();
        var widget = new SettingsWidget(new PlatformSettingsStore(new(root)), diagnostics: service);
        ViewSnapshot Snapshot() => widget.Render().CreateSnapshot("settings-update-test", 1);
        ViewNode Node(string id) => Walk(Snapshot().Root).Single(n => n.Id == id);
        async Task Action(string action) => await widget.OnActionAsync(new(action, "test"));
        try
        {
            await widget.InitializeAsync(default);
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, default);
            await widget.InitializationTask;
            await Action("open.about"); Check(Node("about.updates").ActionId == "open.app-updates");
            await Action("open.app-updates");
            Check(Snapshot().ActiveInputScopeId == "app-update.page");
            Check(Node("app-update.auto").ActionId == "app-update.auto");
            await Action("app-update.auto"); Check(!service.Auto);
            var intent = WidgetIntentRequest.Create(WidgetIntentContracts.AppUpdates, System.Text.Json.JsonSerializer.SerializeToElement(new { }), WidgetIntentPresentation.OpenWidget);
            await Action("back");
            Check(await widget.OnIntentAsync(intent) == WidgetIntentResult.Accepted);
            await widget.IntentNavigationTask;
            Check(Snapshot().ActiveInputScopeId == "app-update.page");
            await Action("app-update.notes"); Check(Node("app-update.notes.text").Text == "Notes");
            await Action("app-update.download"); Check(Node("app-update.primary").IsBusy == true && Node("app-update.primary").IsDisabled != true);
            service.Ready(); await Action("open.app-updates");
            await Action("app-update.install"); Check(service.Installs == 0);
            await Action("app-update.review");
            Check(Snapshot().InitialFocusId == "app-update.dismiss");
            Check(ViewSnapshotValidator.Validate(Snapshot()).Count == 0 && WinUiPresentationContract.Validate(Snapshot()).Count == 0);
            await Action("app-update.dismiss"); Check(service.Installs == 0);
            await Action("app-update.review"); await Action("app-update.install"); Check(service.Installs == 1);
            Check(Snapshot().ActiveInputScopeId == "app-update.page");
            await Action("back"); Check(Snapshot().ActiveInputScopeId == "about.page");
        }
        finally { await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, default); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    private static void Check(bool value) { if (!value) throw new Exception("App update controller flow assertion failed"); }
    private sealed class Service : IPlatformDiagnosticsService
    {
        internal int Installs;
        internal bool Auto = true;
        private AppUpdateSnapshot snapshot = new(1, "available", "0.1.0-preview.29", "0.1.0-preview.30", "Notes", "Ready to download", CanInstall: true);
        public ValueTask<PlatformDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken token = default) => ValueTask.FromResult(PlatformDiagnosticsSnapshot.Unavailable());
        public ValueTask<AppUpdateSnapshot> AppUpdatesAsync(AppUpdateCommand command, CancellationToken token = default)
        {
            if (command.Action is "auto-enable" or "auto-disable")
            { Auto = command.Action == "auto-enable"; snapshot = snapshot with { Revision = snapshot.Revision + 1, AutomaticallyCheck = Auto }; }
            if (command.Action == "download") snapshot = snapshot with { Revision = 2, State = "downloading" };
            if (command.Action == "install") { Installs++; snapshot = snapshot with { Revision = 4, State = "handoff", ReviewToken = null }; }
            return ValueTask.FromResult(snapshot);
        }
        internal void Ready() => snapshot = snapshot with { Revision = 3, State = "ready", ReviewToken = new string('C', 64) };
    }
}
