using System.Runtime.InteropServices;
using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.WidgetProtocol;

internal static partial class Program
{
    [LibraryImport("ReplayWorkerHost.dll")]
    private static partial int WrailReplayWorkerTestSaveDelay(ReplayWorkerNative.WorkerHandle worker, uint milliseconds);
    [LibraryImport("ReplayWorkerHost.dll")]
    private static partial uint WrailReplayWorkerProcessId(ReplayWorkerNative.WorkerHandle worker);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    public static async Task Main(string[] args)
    {
        Check(Marshal.SizeOf<ReplayWorkerNative.Configuration>() == 624 && Marshal.SizeOf<ReplayWorkerNative.State>() == 168, "native ABI sizes");
        Check(Marshal.OffsetOf<ReplayWorkerNative.Configuration>("MemoryBytes").ToInt32() == 72 &&
            Marshal.OffsetOf<ReplayWorkerNative.Configuration>("ClassName").ToInt32() == 80 &&
            Marshal.OffsetOf<ReplayWorkerNative.State>("SavedBytes").ToInt32() == 120 &&
            Marshal.OffsetOf<ReplayWorkerNative.Configuration>("Codec").ToInt32() == 592 &&
            Marshal.OffsetOf<ReplayWorkerNative.Configuration>("AdapterLuid").ToInt32() == 608 &&
            Marshal.OffsetOf<ReplayWorkerNative.State>("Codec").ToInt32() == 144 &&
            Marshal.OffsetOf<ReplayWorkerNative.State>("AdapterLuid").ToInt32() == 160, "native ABI offsets");
        var rejected = false;
        try { await new NativeReplayCaptureBackendFactory().StartAsync(new("invalid", 1, "1", "class"), new(), CancellationToken.None); }
        catch (ReplayBackendException error) when (error.Issue == CaptureReplayIssue.TargetUnavailable) { rejected = true; }
        Check(rejected, "invalid target rejected before native launch");
        var advanced=new CaptureReplaySettings{Recording=new(1280,720,30,6_000_000,Encoder:CaptureEncoderMode.Hardware){Selection=new()}};
        rejected=false;
        try{await new NativeReplayCaptureBackendFactory().StartAsync(new("1",1,"1","class"),advanced,CancellationToken.None);}
        catch(ReplayBackendException error)when(error.Issue==CaptureReplayIssue.HardwareEncoderUnavailable){rejected=true;}
        Check(rejected,"advanced recording cannot bypass the injected resolver and silently use legacy encoding");
        using(var cancelled=new CancellationTokenSource()){
            cancelled.Cancel();var called=false;
            var factory=new NativeReplayCaptureBackendFactory((_,_,_)=>{called=true;return Task.FromResult<ResolvedCaptureRecording?>(null);});
            try{await factory.StartAsync(new("1",1,"1","class"),advanced,cancelled.Token);}catch(OperationCanceledException){}
            Check(!called,"cancelled admission does not probe or launch a native encoder");
        }
        if (args.Length == 0) return; // Structural checks do not require Windows native binaries.
        var directory = Path.GetFullPath(args[0]);
        NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (name, _, _) =>
            name == "ReplayWorkerHost.dll" ? NativeLibrary.Load(Path.Combine(directory, name)) : 0);
        var settings = new CaptureReplaySettings { ReplayDurationSeconds = 15, MemoryBudgetBytes = 32L * 1024 * 1024,
            Recording = new(1280, 720, 30, 2_000_000, Encoder: CaptureEncoderMode.Hardware, Audio: CaptureAudioMode.None) };
        var config = new ReplayWorkerNative.Configuration { Size = 624, Version = 2, Flags = 1, Epoch = 37, MaximumWidth = 1280,
            MaximumHeight = 720, FramesPerSecond = 30, Bitrate = 2_000_000, DurationSeconds = 15, MemoryBytes = 32UL * 1024 * 1024 };
        if (args.Length > 1 && args[1] == "--measure")
        {
            await Measure(directory, settings with { ReplayDurationSeconds = 60, MemoryBudgetBytes = 128L * 1024 * 1024,
                Recording = settings.Recording with { Bitrate = 6_000_000 } }, config);
            return;
        }
        if (args.Length > 1 && args[1] == "--production")
        {
            var result = ReplayWorkerNative.WrailReplayWorkerStart(Path.Combine(directory, "ReplayCaptureWorker.exe"), in config, out var rejectedWorker);
            if (rejectedWorker != 0) new ReplayWorkerNative.WorkerHandle(rejectedWorker).Dispose();
            Check(result == unchecked((int)0x80070057) && rejectedWorker == 0, "production ABI rejects synthetic capture flags");
            config.Flags = 0; config.Window = 1; config.ProcessId = 1; config.Created = 1; config.SetClassName("NotARealWindow");
            Marshal.ThrowExceptionForHR(ReplayWorkerNative.WrailReplayWorkerStart(Path.Combine(directory, "ReplayCaptureWorker.exe"), in config, out var deniedRaw));
            using var denied = new ReplayWorkerNative.WorkerHandle(deniedRaw);
            var deadline = System.Diagnostics.Stopwatch.StartNew(); ReplayWorkerNative.State deniedState;
            do { Marshal.ThrowExceptionForHR(ReplayWorkerNative.WrailReplayWorkerRead(denied, out deniedState, 100)); }
            while ((deniedState.Flags & 2) == 0 && deadline.Elapsed < TimeSpan.FromSeconds(5));
            Check((deniedState.Flags & 2) != 0 && deniedState.Issue == 1, "production rejects missing exact source before acquiring graphics/audio");
            var stale = ReplayWorkerNative.WrailReplayWorkerSave(denied, config.Epoch, Path.Combine(directory, "denied.pending"), 2, out _);
            Check(stale < 0 && !File.Exists(Path.Combine(directory, "denied.pending")), "terminal source cannot admit new save work");
            return;
        }
        Marshal.ThrowExceptionForHR(ReplayWorkerNative.WrailReplayWorkerStart(Path.Combine(directory, "ReplayCaptureWorker.exe"), in config, out var raw));
        await using var backend = new NativeReplayCaptureBackend(new(raw), 37, settings);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        ReplayBackendState state;
        do
        {
            backend.RenewAuthority(); state = await backend.ReadStateAsync(timeout.Token);
            if (state.Phase == CaptureReplayPhase.Error) throw new Exception("Worker failed " + state.Issue);
            await Task.Delay(100, timeout.Token);
        } while (state.BufferedDurationSeconds < 2);
        Check(state.Phase == CaptureReplayPhase.Recording, "managed backend observes encoded live state");
        var output = Path.Combine(directory, "managed-clip.pending");
        var save = backend.SaveAsync(output, 2, timeout.Token);
        var stop = backend.StopAsync(timeout.Token);
        while (!stop.IsCompleted) { backend.RenewAuthority(); await Task.WhenAny(stop, Task.Delay(100)); }
        var clip = await save; await stop;
        Check(clip.ActualDurationSeconds is > 0 and <= 2 && clip.Properties.IsWellFormed(true) && clip.Properties.EncodedFrames <= 60,
            "managed save then stop preserves accepted snapshot and bounded clip metadata");
        Check(new FileInfo(output).Length == clip.ByteLength, "managed result reports actual staging length");
        await CancellationAndDisposal(directory, settings, config);
        Console.WriteLine("Replay managed backend checks passed");
    }
    private static async Task Measure(string directory, CaptureReplaySettings settings, ReplayWorkerNative.Configuration config)
    {
        config.DurationSeconds = 60; config.MemoryBytes = 128UL * 1024 * 1024; config.Bitrate = 6_000_000;
        Marshal.ThrowExceptionForHR(ReplayWorkerNative.WrailReplayWorkerStart(Path.Combine(directory, "ReplayCaptureWorker.exe"), in config, out var raw));
        var handle = new ReplayWorkerNative.WorkerHandle(raw);
        await using var backend = new NativeReplayCaptureBackend(handle, config.Epoch, settings);
        using var process = System.Diagnostics.Process.GetProcessById(checked((int)WrailReplayWorkerProcessId(handle)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var started = System.Diagnostics.Stopwatch.StartNew(); var observations = new List<object>(); var next = 0;
        while (started.Elapsed.TotalSeconds < 76)
        {
            backend.RenewAuthority(); var state = await backend.ReadStateAsync(timeout.Token);
            if (state.Phase == CaptureReplayPhase.Error || state.Ended) throw new Exception("Measurement worker failed: " + state.Issue);
            if (state.ResidentBytes > settings.MemoryBudgetBytes || state.BufferedDurationSeconds > 60)
                throw new Exception("Measurement exceeded replay bounds.");
            if (started.Elapsed.TotalSeconds >= next)
            {
                process.Refresh(); observations.Add(new { elapsedSeconds = started.Elapsed.TotalSeconds,
                    privateBytes = process.PrivateMemorySize64, workingSet = process.WorkingSet64, cpuMilliseconds = process.TotalProcessorTime.TotalMilliseconds,
                    state.ResidentBytes, state.BufferedDurationSeconds, state.DroppedFrames }); next += 15;
            }
            await Task.Delay(250, timeout.Token);
        }
        var clip = await backend.SaveAsync(Path.Combine(directory, "measured-static.pending"), 60, timeout.Token);
        await backend.StopAsync(timeout.Token);
        var json = System.Text.Json.JsonSerializer.Serialize(new { scope = "Synthetic static GPU texture, 1280x720/30 hardware H264, no audio; not gameplay or WGC overhead",
            observations, clip.ActualDurationSeconds, clip.ByteLength, clip.Properties }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, "measurement.json"), json); Console.WriteLine(json);
    }
    private static async Task CancellationAndDisposal(string directory, CaptureReplaySettings settings, ReplayWorkerNative.Configuration config)
    {
        Marshal.ThrowExceptionForHR(ReplayWorkerNative.WrailReplayWorkerStart(Path.Combine(directory, "ReplayCaptureWorker.exe"), in config, out var raw));
        var handle = new ReplayWorkerNative.WorkerHandle(raw);
        await using var backend = new NativeReplayCaptureBackend(handle, config.Epoch, settings);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while ((await backend.ReadStateAsync(timeout.Token)).BufferedDurationSeconds < 2)
        { backend.RenewAuthority(); await Task.Delay(100, timeout.Token); }
        Marshal.ThrowExceptionForHR(WrailReplayWorkerTestSaveDelay(handle, 1000));
        using var cancelled = new CancellationTokenSource();
        var path = Path.Combine(directory, "managed-cancel.pending");
        var saving = backend.SaveAsync(path, 2, cancelled.Token); cancelled.Cancel();
        var cancelledObserved = false;
        try { await saving; } catch (OperationCanceledException) { cancelledObserved = true; }
        Check(cancelledObserved && !File.Exists(path), "cancellation awaits terminal file cleanup");
        Marshal.ThrowExceptionForHR(WrailReplayWorkerTestSaveDelay(handle, 2000));
        using var process = System.Diagnostics.Process.GetProcessById(checked((int)WrailReplayWorkerProcessId(handle)));
        var revokedPath = Path.Combine(directory, "managed-revoked.pending");
        var revoked = backend.SaveAsync(revokedPath, 2, CancellationToken.None);
        await backend.DisposeAsync();
        try { await revoked; } catch (ObjectDisposedException) { } catch (ReplayBackendException) { }
        Check(process.HasExited && !File.Exists(revokedPath), "awaited disposal terminates owned worker before output custody retires");
    }
}
