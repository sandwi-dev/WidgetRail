using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

internal static class ControllerControlScenarios
{
    internal static async Task GatingAndPersistence()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-controller-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PlatformSettingsStore(new PlatformSettingsPaths(root));
            var clock = new Clock();
            var control = new BridgeControllerControl(store, clock);
            Check(!(await control.ReadPreferenceAsync(default)).ExclusiveControl, "Default must be off.");
            Check(!(await control.SetAsync(true, default)).Accepted, "Missing native report must refuse enable.");
            foreach (var flags in Enumerable.Range(0, 7))
            {
                control.Report(new(ControllerControlState.Off, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0));
                Check(!(await control.SetAsync(true, default)).Accepted, "Every prerequisite must be present.");
            }
            var ready = new ControllerControlStatus(ControllerControlState.Off, true, true, true);
            control.Report(ready);
            var enabled = await control.SetAsync(true, default);
            Check(enabled.Accepted && enabled.Status.State == ControllerControlState.Starting,
                "Saving intent cannot claim active routing.");
            var saved = await control.ReadPreferenceAsync(default);
            Check(saved.ExclusiveControl && saved.Revision == 1, "Preference and revision must persist together.");
            clock.Advance(6);
            Check(control.Status == ControllerControlStatus.Unavailable && !(await control.SetAsync(true, default)).Accepted,
                "Stale native readiness must refuse enable.");
            Check((await control.SetAsync(false, default)).Accepted, "Disable remains available with stale status.");
            control.Report(ready with { State = ControllerControlState.RecoveryRequired });
            Check(!(await control.SetAsync(true, default)).Accepted, "Recovery blocks enable.");
            Check((await control.SetAsync(false, default)).Accepted, "Recovery can retry even while already off.");
            saved = await control.ReadPreferenceAsync(default);
            Check(!saved.ExclusiveControl && saved.Revision == 3, "Off retry needs a new revision.");
            var restarted = new BridgeControllerControl(store, clock);
            Check(await restarted.ReadPreferenceAsync(default) == saved, "Restart must retain intent.");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { await control.SetAsync(false, cancelled.Token); throw new Exception("Cancelled write accepted."); }
            catch (OperationCanceledException) { }
            Check(await control.ReadPreferenceAsync(default) == saved, "Cancellation must not mutate settings.");
            Check(!(await new BridgeControllerControl(null).SetAsync(false, default)).Accepted, "Missing store must refuse.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class Clock : TimeProvider
    {
        private long _seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => _seconds;
        public void Advance(long seconds) => _seconds += seconds;
    }
}
