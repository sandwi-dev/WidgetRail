using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

var checks = new (string Name, Func<Task> Run)[]
{
    ("Reviewed durable owner replacement retires old work and reauthorizes without reviving Stop", PolicyTests.ReviewedOwnerReplacement),
    ("Detailed replay settings protect legacy readers and exact source constraints", BrokerTests.EncodingCompatibility),
    ("Pending gestures cancel without admitting recording", PolicyTests.PendingCancellation),
    ("Admitted recording outlives widget and stale stop", PolicyTests.HostLifetime),
    ("Automatic start never admits a different application", PolicyTests.AutomaticIdentity),
    ("Capture permission revocation disables recording intent", PolicyTests.Revocation),
    ("Partial startup catalog does not erase enabled intent", PolicyTests.StartupAuthority),
    ("Replay save commits one complete output and deduplicates receipt", PolicyTests.AtomicSave),
    ("Accepted save drains while explicit stop disables recording", PolicyTests.StopDuringSave),
    ("Revoked save cannot publish delayed encoder output", PolicyTests.SaveRevocation),
    ("Inspected duration cannot exceed requested replay", PolicyTests.StrictDuration),
    ("Asynchronous unsupported encoder does not retry automatically", PolicyTests.TerminalFailure),
    ("Save drain preserves terminal failure and suppresses automatic retry", PolicyTests.TerminalFailureDuringSave),
    ("Stop still ends recording when settings storage fails", PolicyTests.StopStorageFailure),
    ("Suspend retires buffer and requires fresh automatic admission", PolicyTests.Power),
    ("Coordinator renews while saving and orders save before stop", CoordinatorTests.SaveThenStop),
    ("Coordinator rejects cancelled chooser and retires on suspend", CoordinatorTests.CancelAndSuspend),
    ("Stop before approval prevents a late source from starting", PolicyTests.StopBeforeApproval),
    ("Permission and package removal withdraw pending and admitted authority", PolicyTests.Removal),
    ("Revocation during media inspection cannot publish output", PolicyTests.InspectionRevocation),
    ("Broker replay read events and dual-grant save continuation retain authority", BrokerTests.Policy),
    ("Independent save caller revocation preserves recorder but withdraws output", PolicyTests.GuestSaveRevocation),
    ("Coordinator transport failure closes backend without blocking host teardown", CoordinatorTests.TransportFailure),
    ("Pending first-start permission loss completes without waiting for chooser timeout", PolicyTests.PendingPermission),
    ("Cancelled save caller can recover one admitted completion", PolicyTests.CancelledSaveWait),
    ("Automatic settings storage failure does not restart native workers", PolicyTests.AutomaticStorageFailure),
    ("Durable external uninstall reconciles running settings without stale restart", PolicyTests.DurableRemoval),
    ("Transient bridge saturation keeps recording without renewing stale authority", CoordinatorTests.BusyThenReady),
    ("Sustained bridge saturation retires at original authority expiry", CoordinatorTests.BusyExpires),
    ("A completed native save is not repeated while library commit is pending", CoordinatorTests.OneEncodingWhileCommitPending),
};
var failures = 0;
foreach (var check in checks)
    try { await check.Run(); Console.WriteLine("PASS " + check.Name); }
    catch (Exception error) { ++failures; Console.WriteLine("FAIL " + check.Name + ": " + error); }
Console.WriteLine($"{checks.Length - failures}/{checks.Length} passed");
return failures == 0 ? 0 : 1;

internal static class PolicyTests
{
    internal static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static async Task ReviewedOwnerReplacement()
    {
        foreach (var scenario in new[] { "active", "saving", "stopped", "denied", "unpublished", "stale-revision", "commit-during-revocation" })
        {
            await using var f = new ReplayFixture();
            var old = await f.Start(); await f.Report(old.SessionId!, CaptureReplayPhase.Recording);
            Task<CaptureReplaySaveResult>? saving = null; HostReplayCommand? output = null;
            if (scenario == "saving") { saving = f.Service.SaveAsync(ReplayFixture.Actor, new(old.SessionId!, Guid.NewGuid().ToString("N"))); output = await f.WaitSave(); }
            var successor = ReplayFixture.Actor with { PublisherId = "new.publisher", InstanceId = "new.instance" };
            var updated = f.Configuration with { Revision = f.Configuration.Revision + (scenario == "stale-revision" ? 0 : 1),
                Owner = successor, Settings = f.Configuration.Settings with { Enabled = scenario != "stopped", AutoStart = true } };
            if (scenario == "commit-during-revocation") f.BeforePersist = () => { f.BeforePersist = null; f.Configuration = updated; };
            else if (scenario != "unpublished") f.Configuration = updated;
            f.ActiveIdentity = successor; f.CaptureAllowed = scenario != "denied";
            var work = await f.Report(old.SessionId!, CaptureReplayPhase.Recording);
            Check(work.AbortRequested && work.Command is null && work.SessionId is null, scenario + " kept or retagged the old recording");
            Check(!await f.Service.CompleteHostAsync(f.Started(old)), scenario + " accepted old late completion");
            if (output is not null)
            {
                Check(!await f.Service.CompleteHostAsync(f.Encoded(output)), "Transferred an old save lease to replacement owner");
                Check((await saving!.WaitAsync(TimeSpan.FromSeconds(3))).Outcome != CaptureReplayOutcome.Accepted &&
                    (await f.Library.ListAsync(0, default)).TotalCount == 0, "Published output after owner replacement");
            }
            f.Clock.Advance(TimeSpan.FromSeconds(2));
            var resumed = await f.Service.PollHostAsync();
            if (scenario is "active" or "saving" or "commit-during-revocation")
                Check(resumed.Command is { Automatic: true } command && command.Widget == successor && command.SessionId != old.SessionId &&
                    command.ApprovedApplication == ReplayFixture.Application, "Replacement did not receive fresh approved-app admission");
            else Check(resumed.Command is null && !resumed.Status.Settings.Enabled, scenario + " revived recording without valid newer authority");
        }
    }
    internal static async Task PendingCancellation()
    {
        await using var f = new ReplayFixture(); await f.Service.PollHostAsync();
        using var cancellation = new CancellationTokenSource();
        var waiting = f.Service.StartAsync(new object(), ReplayFixture.Actor, cancellation.Token);
        var command = (await f.Service.PollHostAsync()).Command!;
        cancellation.Cancel();
        try { _ = await waiting; } catch (OperationCanceledException) { }
        Check(!await f.Service.CompleteHostAsync(f.Started(command)), "Retired start accepted a late completion");
        Check(!f.Configuration.Settings.Enabled && f.Service.Status.SessionId is null, "Cancelled start enabled recording");
    }
    internal static async Task HostLifetime()
    {
        await using var f = new ReplayFixture(); var owner = new object(); var command = await f.Start(owner);
        Check(f.Service.Status.Phase == CaptureReplayPhase.Starting, "Worker launch falsely claimed frames were recording");
        f.Service.CancelPending(owner);
        await f.Report(command.SessionId!, CaptureReplayPhase.Recording);
        Check(f.Service.Status.Phase == CaptureReplayPhase.Recording, "Widget retirement stopped admitted recorder");
        Check((await f.Service.StopAsync(Guid.NewGuid().ToString("N"))).Outcome == CaptureReplayOutcome.Conflict, "Stale stop affected current recorder");
        Check((await f.Service.StopAsync(null)).Outcome == CaptureReplayOutcome.Conflict, "Waiting-menu stop affected a newer recorder");
        var changed = await f.Service.ConfigureAsync(owner, ReplayFixture.Actor, new(f.Configuration.Revision, f.Configuration.Settings with { ReplayDurationSeconds = 30 }));
        Check(changed.Outcome == CaptureReplayOutcome.Busy, "Live settings silently reset the buffer");
        Check((await f.Service.StopAsync(command.SessionId)).Outcome == CaptureReplayOutcome.Accepted && !f.Configuration.Settings.Enabled, "Explicit stop did not disable recording intent");
        await f.Report(command.SessionId!, CaptureReplayPhase.Off, ended: true);
        Check(f.Service.Status.Phase == CaptureReplayPhase.Off && f.Service.Status.SessionId is null, "Stopped recorder retained a session");
    }
    internal static async Task AutomaticIdentity()
    {
        await using var f = new ReplayFixture(enabled: true, automatic: true);
        var command = (await f.Service.PollHostAsync()).Command!;
        Check(command.Automatic && command.ApprovedApplication == ReplayFixture.Application, "Automatic start lost approved application");
        Check(!await f.Service.CompleteHostAsync(f.Started(command) with { ApprovedApplication = new string('b', 64) }), "Automatic start admitted another app");
        Check((await f.Service.PollHostAsync()).Command is null, "Invalid automatic target caused repeated capture attempts");
    }
    internal static async Task Revocation()
    {
        await using var f = new ReplayFixture(); var command = await f.Start(); await f.Report(command.SessionId!, CaptureReplayPhase.Recording);
        f.CaptureAllowed = false;
        var work = await f.Service.PollHostAsync(new(command.SessionId!, CaptureReplayPhase.Recording, 2, 1024, 0));
        Check(work.AbortRequested && work.SessionId is null && !f.Configuration.Settings.Enabled, "Revocation left recorder authorized");
        f.CaptureAllowed = true;
        Check((await f.Service.PollHostAsync()).Command is null, "Grant restoration silently re-enabled recording");
    }
    internal static async Task StartupAuthority()
    {
        await using var f = new ReplayFixture(enabled: true, automatic: true); f.CatalogReady = false;
        Check((await f.Service.PollHostAsync()).Command is null && f.Configuration.Settings.Enabled, "Partial catalog was treated as uninstall");
        f.CatalogReady = true;
        Check((await f.Service.PollHostAsync()).Command?.Automatic == true, "Approved auto-start did not resume after catalog admission");
    }
    internal static async Task AtomicSave()
    {
        await using var f = new ReplayFixture(); var start = await f.Start(); await f.Report(start.SessionId!, CaptureReplayPhase.Recording);
        var request = new CaptureReplaySaveRequest(start.SessionId!, Guid.NewGuid().ToString("N"));
        var saving = f.Service.SaveAsync(ReplayFixture.Actor, request);
        var work = await f.WaitSave();
        Check((await f.Library.ListAsync(0, default)).TotalCount == 0, "Reservation appeared as saved media");
        await File.WriteAllBytesAsync(work.OutputPath!, ReplayFixture.Bytes);
        Check((await f.Library.ListAsync(0, default)).TotalCount == 0, "Uncommitted codec output appeared in library");
        var capturedAt = f.Clock.GetUtcNow().ToUnixTimeMilliseconds(); f.Clock.Advance(TimeSpan.FromSeconds(12));
        Check(await f.Service.CompleteHostAsync(f.Encoded(work)), "Completed output was rejected");
        var result = await saving.WaitAsync(TimeSpan.FromSeconds(5));
        Check(result.Outcome == CaptureReplayOutcome.Accepted && result.IsWellFormed(), "Complete output was not saved");
        Check(result.Item!.CapturedAtUnixMilliseconds <= capturedAt && result.Item.CapturedAtUnixMilliseconds < f.Clock.GetUtcNow().ToUnixTimeMilliseconds(), "Mux completion changed capture timestamp");
        Check(!File.Exists(work.OutputPath) && (await f.Library.ListAsync(0, default)).TotalCount == 1, "Staging publication was not atomic");
        Check(await f.Service.SaveAsync(ReplayFixture.Actor, request) == result && (await f.Library.ListAsync(0, default)).TotalCount == 1, "Retry duplicated a saved capture");
    }
    internal static async Task StopDuringSave()
    {
        await using var f = new ReplayFixture(); var start = await f.Start(); await f.Report(start.SessionId!, CaptureReplayPhase.Recording);
        var saving = f.Service.SaveAsync(ReplayFixture.Actor, new(start.SessionId!, Guid.NewGuid().ToString("N")));
        var work = await f.WaitSave(); await File.WriteAllBytesAsync(work.OutputPath!, ReplayFixture.Bytes);
        await f.Service.StopAsync(start.SessionId);
        var stopping = await f.Service.PollHostAsync(new(start.SessionId!, CaptureReplayPhase.Stopping, 2, 1024, 0, Ended: true));
        Check(stopping.StopRequested && stopping.SessionId == start.SessionId && stopping.Status.Saving, "Stop discarded accepted save custody");
        await f.Service.CompleteHostAsync(f.Encoded(work));
        Check((await saving.WaitAsync(TimeSpan.FromSeconds(5))).Outcome == CaptureReplayOutcome.Accepted, "Graceful stop lost saved clip");
        Check(f.Service.Status.Phase == CaptureReplayPhase.Off && !f.Configuration.Settings.Enabled, "Save drain restarted recording");
    }
    internal static async Task SaveRevocation()
    {
        await using var f = new ReplayFixture(); var start = await f.Start(); await f.Report(start.SessionId!, CaptureReplayPhase.Recording);
        var saving = f.Service.SaveAsync(ReplayFixture.Actor, new(start.SessionId!, Guid.NewGuid().ToString("N")));
        var work = await f.WaitSave(); await File.WriteAllBytesAsync(work.OutputPath!, ReplayFixture.Bytes);
        f.SaveAllowed = false; f.Service.CancelPendingSave(ReplayFixture.Actor);
        _ = await f.Service.CompleteHostAsync(f.Encoded(work));
        Check((await saving.WaitAsync(TimeSpan.FromSeconds(5))).Outcome != CaptureReplayOutcome.Accepted &&
            (await f.Library.ListAsync(0, default)).TotalCount == 0, "Revoked save published late output");
        Check(f.Service.Status.SessionId == start.SessionId, "Save-only revocation stopped capture authority");
    }
    internal static async Task StrictDuration()
    {
        await using var f = new ReplayFixture(); f.InspectedDuration = 60.001;
        var start = await f.Start(); await f.Report(start.SessionId!, CaptureReplayPhase.Recording);
        var saving = f.Service.SaveAsync(ReplayFixture.Actor, new(start.SessionId!, Guid.NewGuid().ToString("N")));
        var command = await f.WaitSave(); await File.WriteAllBytesAsync(command.OutputPath!, ReplayFixture.Bytes);
        await f.Service.CompleteHostAsync(f.Encoded(command) with { ActualDurationSeconds = 60 });
        Check((await saving.WaitAsync(TimeSpan.FromSeconds(5))).Outcome != CaptureReplayOutcome.Accepted &&
            (await f.Library.ListAsync(0, default)).TotalCount == 0, "Container overrun crossed strict replay bound");
    }
    internal static async Task TerminalFailure()
    {
        await using var f = new ReplayFixture(enabled: true, automatic: true);
        var command = (await f.Service.PollHostAsync()).Command!;
        Check(await f.Service.CompleteHostAsync(f.Started(command)), "Automatic worker did not start");
        await f.Service.PollHostAsync(new(command.SessionId!, CaptureReplayPhase.Error, 0, 0, 0, CaptureReplayIssue.KeyframesUnavailable, Ended: true));
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        Check((await f.Service.PollHostAsync()).Command is null && f.Service.Status.Issue == CaptureReplayIssue.KeyframesUnavailable,
            "Unsupported encoder restarted automatically");
    }
    internal static async Task TerminalFailureDuringSave()
    {
        await using var f = new ReplayFixture(enabled: true, automatic: true);
        var command = (await f.Service.PollHostAsync()).Command!; await f.Service.CompleteHostAsync(f.Started(command));
        await f.Report(command.SessionId!, CaptureReplayPhase.Recording);
        var saving = f.Service.SaveAsync(ReplayFixture.Actor, new(command.SessionId!, Guid.NewGuid().ToString("N")));
        var output = await f.WaitSave(); await File.WriteAllBytesAsync(output.OutputPath!, ReplayFixture.Bytes);
        await f.Service.PollHostAsync(new(command.SessionId!, CaptureReplayPhase.Error, 2, 1024, 0, CaptureReplayIssue.AudioUnavailable, Ended: true));
        await f.Service.CompleteHostAsync(f.Encoded(output));
        Check((await saving.WaitAsync(TimeSpan.FromSeconds(5))).Outcome == CaptureReplayOutcome.Accepted, "Terminal report discarded accepted output");
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        Check((await f.Service.PollHostAsync()).Command is null && f.Service.Status.Issue == CaptureReplayIssue.AudioUnavailable,
            "Save completion erased recorder failure and restarted it");
    }
    internal static async Task StopStorageFailure()
    {
        await using var f = new ReplayFixture(); var start = await f.Start(); f.PersistFails = true;
        var stopped = await f.Service.StopAsync(start.SessionId);
        Check(stopped.Issue == CaptureReplayIssue.StorageUnavailable && !stopped.Status.Settings.Enabled, "Storage error left enabled intent");
        Check((await f.Service.PollHostAsync()).StopRequested, "Storage error prevented acquisition stop");
        await f.Report(start.SessionId!, CaptureReplayPhase.Off, ended: true); f.Clock.Advance(TimeSpan.FromMinutes(1));
        Check((await f.Service.PollHostAsync()).Command is null, "Storage error restarted recording");
    }
    internal static async Task Power()
    {
        await using var f = new ReplayFixture(enabled: true, automatic: true);
        var command = (await f.Service.PollHostAsync()).Command!; await f.Service.CompleteHostAsync(f.Started(command));
        await f.Service.SetHostPowerStateAsync(1, true); f.Clock.Advance(TimeSpan.FromMinutes(10));
        Check((await f.Service.PollHostAsync()).Command is null && f.Configuration.Settings.Enabled && f.Service.Status.SessionId is null,
            "Suspend retained acquisition or erased automatic preference");
        await f.Service.SetHostPowerStateAsync(2, false); await f.Service.SetHostPowerStateAsync(1, true);
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        var resumed = (await f.Service.PollHostAsync()).Command;
        Check(resumed?.Automatic == true && resumed.SessionId != command.SessionId, "Resume reused stale session or accepted stale power event");
    }
    internal static async Task StopBeforeApproval()
    {
        await using var f = new ReplayFixture(); await f.Service.PollHostAsync();
        var waiting = f.Service.StartAsync(new object(), ReplayFixture.Actor);
        var command = (await f.Service.PollHostAsync()).Command!;
        Check((await f.Service.StopAsync(command.SessionId)).Outcome == CaptureReplayOutcome.Accepted, "Pending recording could not stop");
        Check((await waiting).Outcome == CaptureReplayOutcome.Cancelled && !await f.Service.CompleteHostAsync(f.Started(command)), "Stopped chooser admitted late source");
        Check(!f.Configuration.Settings.Enabled && f.Service.Status.SessionId is null, "Stopped chooser persisted recording");
    }
    internal static async Task Removal()
    {
        await using var f = new ReplayFixture(); await f.Service.PollHostAsync();
        var pending = f.Service.StartAsync(new object(), ReplayFixture.Actor);
        var command = (await f.Service.PollHostAsync()).Command!;
        f.CaptureAllowed = false;
        Check(!await f.Service.IsHostCommandCurrentAsync(command.Id), "Fresh native admission ignored consent revocation");
        Check(!await f.Service.CompleteHostAsync(f.Started(command)), "Revoked chooser admitted recording");
        _ = await pending;
        f.CaptureAllowed = true; var start = await f.Start(); f.Removed = true;
        var retired = await f.Report(start.SessionId!, CaptureReplayPhase.Recording);
        Check(retired.AbortRequested && !f.Configuration.Settings.Enabled && retired.Status.Issue == CaptureReplayIssue.FeatureRemoved,
            "Package removal retained recorder authority");
    }
    internal static async Task InspectionRevocation()
    {
        await using var f = new ReplayFixture(); var start = await f.Start(); await f.Report(start.SessionId!, CaptureReplayPhase.Recording);
        var inspecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.InspectBarrier = async token => { inspecting.TrySetResult(); await resume.Task.WaitAsync(token); };
        var saving = f.Service.SaveAsync(ReplayFixture.Actor, new(start.SessionId!, Guid.NewGuid().ToString("N")));
        var output = await f.WaitSave(); await File.WriteAllBytesAsync(output.OutputPath!, ReplayFixture.Bytes);
        await f.Service.CompleteHostAsync(f.Encoded(output)); await inspecting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.SaveAllowed = false; f.Service.CancelPendingSave(ReplayFixture.Actor); resume.SetResult();
        Check((await saving.WaitAsync(TimeSpan.FromSeconds(5))).Outcome != CaptureReplayOutcome.Accepted &&
            (await f.Library.ListAsync(0, default)).TotalCount == 0, "Late inspection published revoked media");
    }
    internal static async Task GuestSaveRevocation()
    {
        await using var f = new ReplayFixture(); var start = await f.Start(); await f.Report(start.SessionId!, CaptureReplayPhase.Recording);
        var guest = ReplayFixture.Actor with { InstanceId = "other.instance" };
        var saving = f.Service.SaveAsync(guest, new(start.SessionId!, Guid.NewGuid().ToString("N")));
        var output = await f.WaitSave(); await File.WriteAllBytesAsync(output.OutputPath!, ReplayFixture.Bytes);
        await f.Service.RevokeAsync(guest);
        await f.Service.CompleteHostAsync(f.Encoded(output));
        Check((await saving.WaitAsync(TimeSpan.FromSeconds(5))).Outcome != CaptureReplayOutcome.Accepted &&
            (await f.Library.ListAsync(0, default)).TotalCount == 0, "Independent caller retained revoked save authority");
        Check(f.Service.Status.SessionId == start.SessionId && f.Configuration.Settings.Enabled, "Unrelated caller revocation stopped recorder owner");
    }
    internal static async Task PendingPermission()
    {
        await using var f = new ReplayFixture(); await f.Service.PollHostAsync();
        var start = f.Service.StartAsync(new object(), ReplayFixture.Actor);
        Check((await f.Service.PollHostAsync()).Command is not null, "First start did not queue");
        f.CaptureAllowed = false;
        Check((await f.Service.PollHostAsync()).Command is null, "First start kept revoked command");
        Check((await start.WaitAsync(TimeSpan.FromSeconds(1))).Issue == CaptureReplayIssue.PermissionRevoked,
            "First start waited for chooser timeout after grant loss");
    }
    internal static async Task CancelledSaveWait()
    {
        await using var f = new ReplayFixture(); var start = await f.Start(); await f.Report(start.SessionId!, CaptureReplayPhase.Recording);
        using var stopWaiting = new CancellationTokenSource();
        var request = new CaptureReplaySaveRequest(start.SessionId!, Guid.NewGuid().ToString("N"));
        var saving = f.Service.SaveAsync(ReplayFixture.Actor, request, stopWaiting.Token); var output = await f.WaitSave();
        stopWaiting.Cancel(); try { await saving; throw new InvalidOperationException("Caller wait ignored cancellation"); } catch (OperationCanceledException) { }
        await File.WriteAllBytesAsync(output.OutputPath!, ReplayFixture.Bytes); await f.Service.CompleteHostAsync(f.Encoded(output));
        var recovered = await f.Service.SaveAsync(ReplayFixture.Actor, request).WaitAsync(TimeSpan.FromSeconds(5));
        Check(recovered.Outcome == CaptureReplayOutcome.Accepted && (await f.Library.ListAsync(0, default)).TotalCount == 1,
            "Cancelled caller lost or duplicated its admitted save");
    }
    internal static async Task AutomaticStorageFailure()
    {
        await using var f = new ReplayFixture(enabled: true, automatic: true);
        var command = (await f.Service.PollHostAsync()).Command!; f.PersistFails = true;
        try { await f.Service.CompleteHostAsync(f.Started(command)); throw new InvalidOperationException("Injected storage failure was ignored"); }
        catch (IOException) { }
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        Check((await f.Service.PollHostAsync()).Command is null && f.Service.Status.Issue == CaptureReplayIssue.StorageUnavailable,
            "Storage failure repeatedly respawned automatic workers");
    }
    internal static async Task DurableRemoval()
    {
        await using var f = new ReplayFixture(); var start = await f.Start();
        var revision = f.Configuration.Revision + 1;
        f.Configuration = new(revision, f.Configuration.Settings with { Enabled = false, AutoStart = false });
        f.Removed = true;
        var removed = await f.Report(start.SessionId!, CaptureReplayPhase.Recording);
        Check(removed.AbortRequested && removed.Status.SettingsRevision == revision && removed.Status.Issue == CaptureReplayIssue.FeatureRemoved &&
            !removed.Status.Settings.Enabled && f.Service.Owner is null, "Running host overwrote or ignored durable uninstall revision");
        f.Removed = false;
        Check((await f.Service.PollHostAsync()).Command is null, "Reinstall revived old automatic intent");
        var restarted = await f.Start();
        Check(restarted.SessionId != start.SessionId && f.Configuration.Revision == revision + 1, "Reconciled host could not accept fresh explicit recording");
    }
}

internal sealed class ReplayFixture : IAsyncDisposable
{
    internal static readonly BrokerWidgetIdentity Actor = new("test.replay", "test.replay.publisher", "replay.instance");
    internal const string Application = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal static readonly byte[] Bytes = Enumerable.Repeat((byte)0x56, 256).ToArray();
    private readonly string baseRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WidgetRailReplayPolicyTests"));
    internal readonly string Root;
    internal readonly CaptureLibraryStore Library;
    internal readonly CaptureReplayService Service;
    internal readonly ReplayClock Clock = new();
    internal CaptureReplayConfiguration Configuration;
    internal bool CaptureAllowed = true, SaveAllowed = true, CatalogReady = true, PersistFails, Removed;
    internal BrokerWidgetIdentity? ActiveIdentity;
    internal double? InspectedDuration;
    internal Func<CancellationToken, Task>? InspectBarrier;
    internal Action? BeforePersist;
    internal ReplayFixture(bool enabled = false, bool automatic = false)
    {
        Root = Path.Combine(baseRoot, Guid.NewGuid().ToString("N"));
        Library = new(Path.Combine(Root, "index"), Path.Combine(Root, "captures"), null, null,
            async (path, media, token) => { token.ThrowIfCancellationRequested(); if (InspectBarrier is not null) await InspectBarrier(token); return media with
                { ByteLength = new FileInfo(path).Length, DurationSeconds = InspectedDuration ?? media.DurationSeconds, Properties = null }; });
        Configuration = new(0, new() { Enabled = enabled, AutoStart = automatic }, enabled ? Actor : null,
            enabled ? Application : null, enabled ? "Fixture game" : null);
        Service = new(Library, Configuration, (next, expected, token) =>
        { token.ThrowIfCancellationRequested(); BeforePersist?.Invoke(); if (PersistFails) throw new IOException("Injected storage failure"); if (Configuration.Revision != expected) throw new BrokerException("replay_settings_conflict", "Persistence revision mismatch"); Configuration = next; return Task.FromResult(next); },
            (actor, capability, _) => Task.FromResult(!CatalogReady ? CaptureReplayIssue.HostUnavailable : Removed || ActiveIdentity is not null && actor != ActiveIdentity ? CaptureReplayIssue.FeatureRemoved :
                capability == PlatformCapabilities.WindowCaptureV1 && !CaptureAllowed || capability == PlatformCapabilities.CaptureLibrarySaveV1 && !SaveAllowed
                    ? CaptureReplayIssue.PermissionRevoked : CaptureReplayIssue.None), Clock, _ => Task.FromResult(Configuration));
    }
    internal async Task<HostReplayCommand> Start(object? owner = null)
    {
        await Service.PollHostAsync();
        var waiting = Service.StartAsync(owner ?? new object(), Actor);
        var command = (await Service.PollHostAsync()).Command ?? throw new InvalidOperationException("Start not queued");
        PolicyTests.Check(await Service.CompleteHostAsync(Started(command)), "Start completion rejected");
        PolicyTests.Check((await waiting).Outcome == CaptureReplayOutcome.Accepted, "Start failed"); return command;
    }
    internal HostReplayCompletion Started(HostReplayCommand command) => new(command.Id, CaptureReplayOutcome.Accepted)
    { SessionId = command.SessionId, Target = new("123", 456, "789", "FixtureWindow"), ApprovedApplication = Application,
        ApplicationName = "Fixture game", SourceApplication = new("Fixture game", "Fixture level") };
    internal HostReplayCompletion Encoded(HostReplayCommand command) => new(command.Id, CaptureReplayOutcome.Accepted)
    { SessionId = command.SessionId, Width = 1280, Height = 720, ByteLength = Bytes.Length, ActualDurationSeconds = 2,
        Properties = new(1280, 720, 30, 60, 0, CaptureEncoderMode.Hardware, CaptureAudioMode.Application) };
    internal Task<HostReplayWork> Report(string id, CaptureReplayPhase phase, bool ended = false) =>
        Service.PollHostAsync(new(id, phase, ended ? 0 : 2, ended ? 0 : 1024, 0, Ended: ended));
    internal async Task<HostReplayCommand> WaitSave()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var work = await Service.PollHostAsync(token: timeout.Token);
            if (work.Command is { Kind: HostReplayCommandKind.Save } save) return save;
            await Task.Delay(5, timeout.Token);
        }
    }
    public async ValueTask DisposeAsync()
    {
        await Service.DisposeAsync(); Library.Dispose();
        if (!Path.GetFullPath(Root).StartsWith(baseRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture cleanup path");
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
internal sealed class ReplayClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    internal void Advance(TimeSpan amount) => now += amount;
}
