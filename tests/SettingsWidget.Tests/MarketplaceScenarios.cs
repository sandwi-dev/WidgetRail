using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class MarketplaceScenarios
{
    internal static async Task ControllerFlow()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-marketplace-settings-" + Guid.NewGuid().ToString("N"));
        var service = new Service();
        var widget = new SettingsWidget(new PlatformSettingsStore(new(root)), diagnostics: service);
        ViewSnapshot Snapshot() => widget.Render().CreateSnapshot("settings-marketplace-test", 1);
        ViewNode Node(string id) => Walk(Snapshot().Root).Single(n => n.Id == id);
        async Task Action(string action) => await widget.OnActionAsync(new(action, "test"));
        try
        {
            await widget.InitializeAsync(default);
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, default);
            await widget.InitializationTask;
            await Action("open.installed-widgets");
            Check(Node("installed.marketplace").ActionId == "open.marketplace", "No marketplace entry in Widgets");
            Check(Node("installed.marketplace").Text == "Browse catalogue", "Catalogue entry label missing");
            await Action("open.marketplace");
            Check(Snapshot().ActiveInputScopeId == "marketplace.page", "Wrong browse input scope");
            Check(Node("marketplace.heading").Text == "WidgetRail Catalogue", "Catalogue page heading missing");
            var listing = Walk(Snapshot().Root).Single(n => n.ActionId?.StartsWith("marketplace.select.") == true);
            await Action(listing.ActionId!);
            Check(Node("marketplace.screenshot").Kind == ViewNodeKind.Image, "Screenshot was not displayed");
            Check(Snapshot().Root.Shortcuts.Any(s => s.Button == ControllerButton.RightBumper && s.ActionId == "marketplace.screenshot.next"),
                "Screenshot controller shortcut missing");
            Check(ViewSnapshotValidator.Validate(Snapshot()).Count == 0 && WinUiPresentationContract.Validate(Snapshot()).Count == 0,
                "Marketplace detail view violates the host contract");
            await Action("marketplace.install");
            Check(service.InstallCalls == 0, "Installation was admitted without opening review");
            await Action("marketplace.download");
            Check(Node("marketplace.primary").IsBusy == true && Node("marketplace.primary").IsDisabled != true,
                "Download removed the focus target");
            service.Ready();
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
                while (Node("marketplace.primary").ActionId != "marketplace.review.open") await Task.Delay(20, timeout.Token);
            await Action("marketplace.review.open");
            Check(Snapshot().InitialFocusId == "marketplace.review.cancel", "Full-access review must start on Cancel");
            Check(Node("marketplace.review.trust").Text!.Contains("Full access"), "Full-access explanation missing");
            Check(Node("marketplace.review.Required.0").Text!.Contains("new"), "Increased permission was not marked");
            Check(ViewSnapshotValidator.Validate(Snapshot()).Count == 0, "Review modal is invalid");
            await Action("marketplace.review.cancel");
            Check(service.InstallCalls == 0 && Snapshot().ActiveInputScopeId == "marketplace.details", "Cancel installed or lost its return scope");
            await Action("marketplace.review.open");
            await Action("marketplace.install");
            Check(service.InstallCalls == 1 && service.ApprovedFullTrust, "Exact review was not approved");
            Check(Node("marketplace.version").Text!.Contains("2.0.0"), "Installed version did not refresh");
            await Action("back");
            Check(Snapshot().InitialFocusId == listing.Id, "Back lost the selected listing");
        }
        finally
        {
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, default);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Service : IPlatformDiagnosticsService
    {
        internal int InstallCalls;
        internal bool ApprovedFullTrust;
        private MarketplaceSnapshot _snapshot = new(1,
            [new("dev.test.marketplace", "Test widget", "Description", ["https://raw.githubusercontent.com/sandwi-dev/WidgetRail/main/docs/images/overview-rail.png",
                "https://raw.githubusercontent.com/sandwi-dev/WidgetRail/main/docs/images/pinned-video.png"], "2.0.0", "1.0.0", 1000)], CheckedAtUtc: DateTimeOffset.UtcNow);
        public ValueTask<PlatformDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken token = default) => ValueTask.FromResult(PlatformDiagnosticsSnapshot.Unavailable());
        public ValueTask<MarketplaceSnapshot> MarketplaceAsync(MarketplaceCommand command, CancellationToken token = default)
        {
            if (command.Action == "download")
                _snapshot = _snapshot with { Revision = 2, Operation = new(new string('A', 32), "download", "running", "dev.test.marketplace") };
            if (command.Action == "install")
            {
                InstallCalls++; ApprovedFullTrust = command.ApproveFullTrust;
                _snapshot = _snapshot with { Revision = 4, Items = [_snapshot.Items[0] with { InstalledVersion = "2.0.0" }],
                    Operation = new(new string('B', 32), "install", "completed", "dev.test.marketplace", Message: "Installed disabled.") };
            }
            return ValueTask.FromResult(_snapshot);
        }
        internal void Ready() => _snapshot = _snapshot with { Revision = 3, Operation = _snapshot.Operation! with
        {
            State = "review", Review = new(new string('C', 64), "dev.test.marketplace", "Test widget", "2.0.0", "1.0.0", true, true,
                "Compatible with this host.", ["system.audio.sessions.read.v1"], [], ["system.audio.sessions.read.v1"], new string('D', 64)),
        } };
    }
}
