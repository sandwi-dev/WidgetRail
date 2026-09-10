using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

internal static class SettingsControlsScenarios
{
    internal static async Task QueueAndCatalog()
    {
        static ConfiguredWidget Widget(string id, string package) => new()
        {
            Id = id, PackageId = package, PublisherId = "widgetrail.firstparty", Name = id,
            InstanceId = id, WorkerExecutable = "unused.exe",
            WorkerFingerprint = new string('a', 64), CatalogFingerprint = new string('b', 64),
        };
        var catalog = new BridgeCatalog([
            Widget("settings", BuiltInWidgetSettings.SettingsWidgetId),
            Widget("audio-mixer", "widgetrail.firstparty.audio-mixer"),
        ]);
        var filtered = catalog.WithWidgetSettings(new() { DisabledIds = ["widgetrail.firstparty.audio-mixer", BuiltInWidgetSettings.SettingsWidgetId] });
        if (filtered.Widgets.Count != 1 || filtered.Widgets[0].Id != "settings")
            throw new Exception("Filtering must use package identity and always retain Settings.");
        if (catalog.WithWidgetSettings(new()).Widgets.Count != 2)
            throw new Exception("Re-enabling must restore the original catalog entry.");
        await using var server = new WidgetBridgeServer("settings-control-test-" + Guid.NewGuid().ToString("N"), new BridgeCatalog([]));
        if (!(await server.RequestApplicationControlAsync(false, default)).Accepted ||
            (await server.RequestApplicationControlAsync(true, default)).Accepted ||
            server.TakeRequestedApplicationControl() != 1 || server.TakeRequestedApplicationControl() != 0)
            throw new Exception("A quit request must be bounded and consumed once.");
        if (!(await server.RequestApplicationControlAsync(true, default)).Accepted || server.TakeRequestedApplicationControl() != 2)
            throw new Exception("Restart must retain its distinct action.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await server.RequestApplicationControlAsync(false, cancelled.Token); throw new Exception("Cancelled request accepted."); }
        catch (OperationCanceledException) { }
        if (server.TakeRequestedApplicationControl() != 0) throw new Exception("Cancellation queued an action.");
    }
}
