using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class DiagnosticsExportScenarios
{
    internal static async Task ExportRetainsFocusAndResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-export-ui-" + Guid.NewGuid().ToString("N"));
        var service = new ExportService();
        var widget = new SettingsWidget(new PlatformSettingsStore(new(root)), diagnostics: service);
        ViewSnapshot Snapshot() => widget.Render().CreateSnapshot("settings-diagnostics-test", 1);
        ViewNode Node(string id) => Walk(Snapshot().Root).Single(node => node.Id == id);
        try
        {
            await widget.InitializeAsync(default);
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, default);
            await widget.InitializationTask;
            // Export cannot be invoked from unrelated Settings pages.
            await widget.OnActionAsync(new("diagnostics.export", "test"));
            Check(service.Calls == 0, "Export was admitted on the wrong page");
            await widget.OnActionAsync(new("open.diagnostics", "test"));
            var before = Node("diagnostics.export");
            var export = widget.OnActionAsync(new("diagnostics.export", before.Id)).AsTask();
            await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var busy = Node("diagnostics.export");
            Check(busy.IsBusy == true && busy.Kind == ViewNodeKind.Button && busy.Focus == before.Focus,
                "Export changed its focus target while busy");
            Check(ViewSnapshotValidator.Validate(Snapshot()).Count == 0, "Busy view is invalid");
            service.Complete.TrySetResult(new(SupportBundleExportStatus.Saved, "WidgetRail-support-test.zip"));
            await export;
            Check(Node("diagnostics.export").IsBusy != true, "Busy state remained after export");
            Check(Node("diagnostics.export.saved").Text!.Contains("WidgetRail-support-test.zip"), "Saved filename was not retained");
            await widget.OnActionAsync(new("diagnostics.details.expand", "diagnostics.details"));
            Check(Node("diagnostics.export.saved").Text!.Contains("WidgetRail-support-test.zip"), "Expanding details lost the result");
        }
        finally
        {
            service.Complete.TrySetResult(new(SupportBundleExportStatus.Failed));
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, default);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class ExportService : IPlatformDiagnosticsService
    {
        internal int Calls;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<SupportBundleExportResult> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<PlatformDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken token = default) =>
            ValueTask.FromResult(PlatformDiagnosticsSnapshot.Unavailable());
        public async ValueTask<SupportBundleExportResult> ExportSupportBundleAsync(CancellationToken token = default)
        {
            Calls++;
            Entered.TrySetResult();
            return await Complete.Task.WaitAsync(token);
        }
    }

    private static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
