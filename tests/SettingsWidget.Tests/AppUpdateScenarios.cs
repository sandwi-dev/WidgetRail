using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class AppUpdateScenarios
{
    internal static async Task ExportAsync(string directory, Func<ViewSnapshot, string, Task> export)
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-update-layout-" + Guid.NewGuid().ToString("N"));
        var service = new Service();
        Directory.CreateDirectory(directory);
        service.SetNotes("WidgetRail update\n\nWhat's new\n\nChoose Install and restart for an unattended update when prerequisites are ready. If interactive setup is required, WidgetRail explains that before closing.\n\nSettings and sign-ins stay in place.\n\n" +
            string.Concat(Enumerable.Repeat("Long release notes remain readable in the page scroll area. There is no two-line helper-text limit.\n\n", 50)));
        service.Ready();
        var widget = new SettingsWidget(new PlatformSettingsStore(new(root)), diagnostics: service);
        try
        {
            await widget.InitializeAsync(default);
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, default);
            await widget.InitializationTask;
            await widget.OnActionAsync(new("open.app-updates", "test"));
            await export(widget.Render().CreateSnapshot("settings-update-layout", 1), Path.Combine(directory, "ready.json"));
            await widget.OnActionAsync(new("app-update.notes", "test"));
            await export(widget.Render().CreateSnapshot("settings-update-layout", 2), Path.Combine(directory, "notes.json"));
            await widget.OnActionAsync(new("app-update.notes", "test"));
            service.Interactive();
            await widget.OnActionAsync(new("open.app-updates", "test"));
            await export(widget.Render().CreateSnapshot("settings-update-layout", 3), Path.Combine(directory, "interactive-required.json"));
            foreach (var outcome in new[] { "cancelled", "failed", "restart-required", "startup-warning" })
            {
                service.Outcome(outcome);
                await widget.OnActionAsync(new("open.app-updates", "test"));
                var snapshot = widget.Render().CreateSnapshot("settings-update-layout", 4);
                Check(ViewSnapshotValidator.Validate(snapshot).Count == 0 && WinUiPresentationContract.Validate(snapshot).Count == 0);
                await export(snapshot, Path.Combine(directory, outcome + ".json"));
            }
            Check(service.Installs == 0);
        }
        finally { await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, default); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

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
            var appearance = WidgetIntentRequest.Create(WidgetIntentContracts.Appearance,
                System.Text.Json.JsonSerializer.SerializeToElement(new { }), WidgetIntentPresentation.OpenWidget);
            Check(await widget.OnIntentAsync(appearance) == WidgetIntentResult.Accepted);
            await widget.IntentNavigationTask;
            Check(Snapshot().ActiveInputScopeId == "appearance.page");
            Check(Node("appearance.theme").ActionId == "open.themes");
            await Action("back");
            Check(Node("settings.introduction").ActionId == "application.introduction");
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
            foreach (var notes in new[]
            {
                new string('a', 4096), new string('a', 4097), new string('a', 8000),
                new string('a', 4095) + "\U0001F3B5" + new string('b', 3903),
                new string('a', 4095) + "\r\n" + new string('b', 3903),
                string.Concat(Enumerable.Repeat("Release paragraph.\r\n\r\n", 300)),
            })
            {
                service.SetNotes(notes);
                await Action("app-update.check");
                var snapshot = Snapshot();
                var blocks = Walk(snapshot.Root).Where(n => n.Id == "app-update.notes.text" ||
                    n.Id.StartsWith("app-update.notes.text.", StringComparison.Ordinal)).ToArray();
                Check(string.Concat(blocks.Select(n => n.Text)) == notes);
                Check(blocks.All(n => n.StyleClasses.Contains("app-update-notes") && !n.StyleClasses.Contains("page-help")));
                Check(blocks.All(n => n.Text is { Length: > 0 } text && text.Length <= ProtocolConstants.MaximumStringLength &&
                    !char.IsHighSurrogate(text[^1]) && !char.IsLowSurrogate(text[0]) && text[^1] != '\r'));
                Check(ViewSnapshotValidator.Validate(snapshot).Count == 0 && WinUiPresentationContract.Validate(snapshot).Count == 0);
            }
            await Action("app-update.notes");
            Check(!Walk(Snapshot().Root).Any(n => n.Id.StartsWith("app-update.notes.text", StringComparison.Ordinal)));
            await Action("app-update.download"); Check(Node("app-update.primary").IsBusy == true && Node("app-update.primary").IsDisabled != true);
            service.Ready(); await Action("open.app-updates");
            Check(Node("app-update.primary").Text == "Install and restart");
            Check(!Walk(Snapshot().Root).Any(n => n.Id == "app-update.input-warning"));
            service.Interactive(); await Action("open.app-updates");
            Check(Node("app-update.primary").Text == "Open interactive setup");
            Check(Node("app-update.input-warning").Text!.Contains("Mouse", StringComparison.OrdinalIgnoreCase));
            service.Ready(); await Action("open.app-updates");
            Check(!Walk(Snapshot().Root).Any(n => n.Id.StartsWith("app-update.recovery", StringComparison.Ordinal)));
            Check(ViewSnapshotValidator.Validate(Snapshot()).Count == 0 && WinUiPresentationContract.Validate(Snapshot()).Count == 0);
            await Action("app-update.install"); Check(service.Installs == 1);
            await Action("app-update.install"); Check(service.Installs == 1);
            Check(Node("app-update.primary").IsDisabled == true);
            Check(Snapshot().ActiveInputScopeId == "app-update.page");
            await Action("back"); Check(Snapshot().ActiveInputScopeId == "about.page");
            foreach (var outcome in new[] { "cancelled", "failed", "restart-required", "startup-warning" })
            {
                service.Outcome(outcome); await Action("open.app-updates");
                Check(Node("app-update.status").Text == service.Message);
                Check(Node("app-update.status").StyleClasses.Contains("diagnostic-error"));
                Check(!Walk(Snapshot().Root).Any(n => n.Id is "app-update.primary" or "app-update.input-warning"));
                Check(Node("app-update.check").IsDisabled != true && Node("app-update.back").IsDisabled != true);
                var checks = service.Checks;
                await Action("app-update.check");
                Check(service.Checks == checks + 1 && service.Installs == 1);
                await Action("back"); Check(Snapshot().ActiveInputScopeId == "about.page");
            }
        }
        finally { await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, default); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    private static void Check(bool value) { if (!value) throw new Exception("App update controller flow assertion failed"); }
    private sealed class Service : IPlatformDiagnosticsService
    {
        internal int Installs;
        internal int Checks;
        internal string Message => snapshot.Message;
        internal bool Auto = true;
        internal void SetNotes(string notes) => snapshot = snapshot with { Revision = snapshot.Revision + 1, Notes = notes };
        private AppUpdateSnapshot snapshot = new(1, "available", "0.1.0-preview.29", "0.1.0-preview.30", "Notes", "Ready to download", CanInstall: true);
        public ValueTask<PlatformDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken token = default) => ValueTask.FromResult(PlatformDiagnosticsSnapshot.Unavailable());
        public ValueTask<AppUpdateSnapshot> AppUpdatesAsync(AppUpdateCommand command, CancellationToken token = default)
        {
            if (command.Action is "auto-enable" or "auto-disable")
            { Auto = command.Action == "auto-enable"; snapshot = snapshot with { Revision = snapshot.Revision + 1, AutomaticallyCheck = Auto }; }
            if (command.Action == "download") snapshot = snapshot with { Revision = 2, State = "downloading" };
            if (command.Action == "install") { Installs++; snapshot = snapshot with { Revision = 4, State = "handoff", ReviewToken = null }; }
            if (command.Action == "check")
            {
                Checks++;
                if (snapshot.InstallerOutcomePending) snapshot = snapshot with { Revision = snapshot.Revision + 1, State = "available",
                    InstallerOutcomePending = false, AvailableVersion = "0.1.0-preview.30", Message = "Ready to download" };
            }
            return ValueTask.FromResult(snapshot);
        }
        internal void Interactive() => snapshot = snapshot with { Revision = snapshot.Revision + 1, RequiresInteractiveSetup = true };
        internal void Ready() => snapshot = snapshot with { Revision = snapshot.Revision + 1, State = "ready", RequiresInteractiveSetup = false, ReviewToken = new string('C', 64) };
        internal void Outcome(string outcome) => snapshot = snapshot with
        {
            Revision = snapshot.Revision + 1, State = "failed", AvailableVersion = null, Notes = "", ReviewToken = null,
            RequiresInteractiveSetup = false, InstallerOutcomePending = true,
            // Presentation fixtures mirror the persisted outcomes produced by BridgeAppUpdates.
            Message = outcome switch
            {
                "cancelled" => "Setup was cancelled. Your verified installation reopened. Check for updates to retry when ready.",
                "failed" => "Setup did not finish. Your verified installation reopened. Check for updates to retry, or run the downloaded installer interactively.",
                "restart-required" => "Setup requires a Windows restart. Save your work and restart Windows before retrying the update.",
                "startup-warning" => "The update installed, but startup at sign-in could not be restored. Run setup again and select Start WidgetRail when I sign in.",
                _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
            },
        };
    }
}
