using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

internal sealed class NativeReplayCaptureBackendFactory(
    Func<NativeWindowPreviewTarget,CaptureReplaySettings,CancellationToken,Task<ResolvedCaptureRecording?>>? resolve=null) : IReplayCaptureBackendFactory
{
    public async Task<IReplayCaptureBackend> StartAsync(NativeWindowPreviewTarget target, CaptureReplaySettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(!settings.IsWellFormed())throw new ReplayBackendException(CaptureReplayIssue.HardwareEncoderUnavailable);
        ResolvedCaptureRecording? resolved=null;
        if(settings.Recording.Selection is not null || settings.Recording.Encoding is not null)
        {
            if(resolve is null)throw new ReplayBackendException(CaptureReplayIssue.HardwareEncoderUnavailable);
            resolved=await resolve(target,settings,cancellationToken).ConfigureAwait(false);
            if(resolved is null || !resolved.Configuration.Satisfies(settings.Recording) || resolved.NativeAdapterLuid==0)
                throw new ReplayBackendException(CaptureReplayIssue.HardwareEncoderUnavailable);
            settings=settings with{Recording=resolved.Configuration.Recording};
        }
        return StartResolved(target,settings,resolved,cancellationToken);
    }
    private static unsafe IReplayCaptureBackend StartResolved(NativeWindowPreviewTarget target,CaptureReplaySettings settings,
        ResolvedCaptureRecording? resolved,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!settings.IsWellFormed() || Marshal.SizeOf<ReplayWorkerNative.Configuration>() != 624 || Marshal.SizeOf<ReplayWorkerNative.State>() != 168)
            throw new ReplayBackendException(CaptureReplayIssue.HardwareEncoderUnavailable);
        if (!ulong.TryParse(target.Handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var window) || window == 0 ||
            !ulong.TryParse(target.ProcessCreated, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var created) || created == 0 || target.ProcessId == 0)
            throw new ReplayBackendException(CaptureReplayIssue.TargetUnavailable);
        var epoch = (BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))) & 0x1fffffffffffffUL) | 1;
        var recording = settings.Recording;
        var configuration = new ReplayWorkerNative.Configuration
        {
            Size = 624, Version = 2, Epoch = epoch, Window = window, Created = created, ProcessId = target.ProcessId,
            MaximumWidth = checked((uint)recording.MaximumWidth), MaximumHeight = checked((uint)recording.MaximumHeight),
            FramesPerSecond = checked((uint)recording.FramesPerSecond), Bitrate = checked((uint)recording.Bitrate), AudioMode = (uint)recording.Audio,
            IncludeCursor = settings.IncludeCursor ? 1u : 0, DurationSeconds = checked((uint)settings.ReplayDurationSeconds), MemoryBytes = checked((ulong)settings.MemoryBudgetBytes)
        };
        if(resolved is not null)
        {
            var encoding=recording.Encoding!;
            configuration.Codec=(uint)encoding.Codec;configuration.PixelFormat=(uint)encoding.PixelFormat;
            configuration.RateControl=(uint)encoding.RateControl;configuration.Quality=(uint)(encoding.Quality??75);
            configuration.AdapterLuid=resolved.NativeAdapterLuid;
            configuration.ExpectedSourceWidth=(uint)resolved.Configuration.SourceWidth;configuration.ExpectedSourceHeight=(uint)resolved.Configuration.SourceHeight;
        }
        configuration.SetClassName(target.ClassName);
        nint worker = 0;
        try
        {
            var result = ReplayWorkerNative.WrailReplayWorkerStart(Path.Combine(AppContext.BaseDirectory, "ReplayCaptureWorker.exe"), in configuration, out worker);
            if (result < 0 || worker == 0) throw new ReplayBackendException(CaptureReplayIssue.HardwareEncoderUnavailable, result);
            var handle = new ReplayWorkerNative.WorkerHandle(worker); worker = 0;
            IReplayCaptureBackend backend = new NativeReplayCaptureBackend(handle, epoch, settings,resolved);
            if (cancellationToken.IsCancellationRequested) { handle.Dispose(); cancellationToken.ThrowIfCancellationRequested(); }
            return backend;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { throw new ReplayBackendException(CaptureReplayIssue.HardwareEncoderUnavailable, error.HResult); }
        finally { if (worker != 0) new ReplayWorkerNative.WorkerHandle(worker).Dispose(); }
    }
}

internal sealed class NativeReplayCaptureBackend(ReplayWorkerNative.WorkerHandle handle, ulong epoch, CaptureReplaySettings settings,ResolvedCaptureRecording? resolved=null) : IReplayCaptureBackend
{
    public CaptureRecordingConfiguration? ResolvedConfiguration=>resolved?.Configuration;
    private int disposed;
    private readonly object nativeGate = new();
    private ReplayWorkerNative.State Read(uint timeout = 0)
    {
        lock (nativeGate) return ReadCore(timeout);
    }
    private ReplayWorkerNative.State ReadCore(uint timeout)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var result = ReplayWorkerNative.WrailReplayWorkerRead(handle, out var state, timeout);
        if (result < 0) throw new ReplayBackendException(CaptureReplayIssue.HostUnavailable, result);
        if (state.Size != 168 || state.Version != 2 || state.Epoch != epoch || state.Phase is < 1 or > 6 || state.Issue > 10 || (state.Flags & ~7u) != 0 ||
            state.BufferedTicks < 0 || state.BufferedTicks > (long)settings.ReplayDurationSeconds * TimeSpan.TicksPerSecond ||
            state.ResidentBytes > (ulong)settings.MemoryBudgetBytes || state.DroppedFrames > long.MaxValue)
            throw new ReplayBackendException(CaptureReplayIssue.HostUnavailable);
        if(state.Codec is <1 or >3 || state.PixelFormat is <1 or >4 || state.RateControl>3 || state.Quality>100 ||
            resolved is not null && (state.Codec!=(uint)resolved.Configuration.Recording.Encoding!.Codec ||
                state.PixelFormat!=(uint)resolved.Configuration.Recording.Encoding.PixelFormat || state.RateControl!=(uint)resolved.Configuration.Recording.Encoding.RateControl ||
                state.AdapterLuid!=0 && state.AdapterLuid!=resolved.NativeAdapterLuid))
            throw new ReplayBackendException(CaptureReplayIssue.HostUnavailable);
        return state;
    }
    private static CaptureReplayIssue Issue(uint value) => value switch
    {
        0 => CaptureReplayIssue.None, 1 => CaptureReplayIssue.TargetUnavailable, 2 => CaptureReplayIssue.HardwareEncoderUnavailable,
        3 => CaptureReplayIssue.AudioUnavailable, 4 => CaptureReplayIssue.KeyframesUnavailable, 5 => CaptureReplayIssue.OutputOrderUnsupported,
        6 => CaptureReplayIssue.StorageUnavailable, 7 => CaptureReplayIssue.BufferNotReady, 8 => CaptureReplayIssue.SaveInProgress,
        9 => CaptureReplayIssue.Cancelled, _ => CaptureReplayIssue.HostUnavailable
    };
    public Task<ReplayBackendState> ReadStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); var state = Read();
        return Task.FromResult(new ReplayBackendState(state.Phase switch
        { 1 => CaptureReplayPhase.Starting, 2 => CaptureReplayPhase.Recording, 3 => CaptureReplayPhase.Paused, 4 => CaptureReplayPhase.Stopping,
            5 => CaptureReplayPhase.Off, _ => CaptureReplayPhase.Error }, state.BufferedTicks / (double)TimeSpan.TicksPerSecond,
            checked((long)state.ResidentBytes), checked((long)state.DroppedFrames), state.Phase == 6 ? Issue(state.Issue) : (state.Flags & 4) != 0 ? CaptureReplayIssue.AudioUnavailable : CaptureReplayIssue.None,
            (state.Flags & 2) != 0, (state.Flags & 1) != 0));
    }
    public void RenewAuthority()
    {
        lock (nativeGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            var result = ReplayWorkerNative.WrailReplayWorkerHeartbeat(handle, epoch);
            if (result < 0 && (Read().Flags & 2) != 0) return; // Completion may race the coordinator's final renewal.
            if (result < 0) throw new ReplayBackendException(CaptureReplayIssue.HostUnavailable, result);
        }
    }
    public Task<ReplayBackendSaveResult> SaveAsync(string trustedOutputPath, int durationSeconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(trustedOutputPath) || durationSeconds < 1 || durationSeconds > settings.ReplayDurationSeconds)
            throw new ArgumentException("Replay save requires a trusted absolute destination and bounded duration.");
        // Admission occurs before returning the task. SaveAsync followed by StopAsync
        // therefore cannot stop acquisition ahead of an already accepted snapshot.
        ulong saveId; int result;
        lock (nativeGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            result = ReplayWorkerNative.WrailReplayWorkerSave(handle, epoch, trustedOutputPath, checked((uint)durationSeconds), out saveId);
        }
        if (result < 0) throw new ReplayBackendException(result == unchecked((int)0x800700aa) ? CaptureReplayIssue.SaveInProgress : CaptureReplayIssue.SessionChanged, result);
        return Task.Run(() => AwaitSave(saveId, durationSeconds, cancellationToken), CancellationToken.None);
    }
    private ReplayBackendSaveResult AwaitSave(ulong saveId, int durationSeconds, CancellationToken cancellationToken)
    {
        var cancellationSent = false;
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(36))
        {
            // This loop already waits on the native progress event with a bounded
            // timeout. Send cancellation here rather than blocking the cancelling
            // UI thread behind an in-flight native read.
            if (cancellationToken.IsCancellationRequested && !cancellationSent)
            {
                lock (nativeGate)
                {
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                    ReplayWorkerNative.WrailReplayWorkerCancelSave(handle, epoch, saveId);
                }
                cancellationSent = true;
            }
            var state = Read(250);
            if (state.SaveId == saveId && state.SaveOutcome >= 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (state.SaveOutcome != 2) throw new ReplayBackendException(Issue(state.Issue), state.Error);
                var properties = new CaptureMediaProperties(checked((int)state.SavedSourceWidth), checked((int)state.SavedSourceHeight), settings.Recording.FramesPerSecond,
                    checked((int)state.SavedFrames), checked((int)state.SavedMissed), CaptureEncoderMode.Hardware, (CaptureAudioMode)state.SavedAudio);
                if (state.SavedWidth is < 2 or > 8192 || state.SavedHeight is < 2 or > 8192 || (ulong)state.SavedWidth * state.SavedHeight > 3840UL * 2160 || state.SavedWidth % 2 != 0 || state.SavedHeight % 2 != 0 ||
                    state.SavedDurationTicks <= 0 || state.SavedDurationTicks > (long)durationSeconds * TimeSpan.TicksPerSecond ||
                    state.SavedBytes is 0 or > 256UL * 1024 * 1024 || !properties.IsWellFormed(true) || properties.Audio != settings.Recording.Audio)
                    throw new ReplayBackendException(CaptureReplayIssue.StorageUnavailable);
                return new(checked((int)state.SavedWidth), checked((int)state.SavedHeight), state.SavedDurationTicks / (double)TimeSpan.TicksPerSecond,
                    checked((long)state.SavedBytes), properties);
            }
            if ((state.Flags & 2) != 0) throw new ReplayBackendException(Issue(state.Issue), state.Error);
        }
        DisposeCore(); throw new ReplayBackendException(CaptureReplayIssue.StorageUnavailable, unchecked((int)0x800705b4));
    }
    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int result;
        lock (nativeGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            result = ReplayWorkerNative.WrailReplayWorkerRequestStop(handle, epoch);
        }
        if (result < 0) throw new ReplayBackendException(CaptureReplayIssue.HostUnavailable, result);
        return Task.Run(() =>
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(36))
            {
                if (cancellationToken.IsCancellationRequested) { DisposeCore(); cancellationToken.ThrowIfCancellationRequested(); }
                if ((Read(250).Flags & 2) != 0) return;
            }
            DisposeCore(); throw new ReplayBackendException(CaptureReplayIssue.HostUnavailable, unchecked((int)0x800705b4));
        }, CancellationToken.None);
    }
    private void DisposeCore()
    {
        lock (nativeGate) if (Interlocked.Exchange(ref disposed, 1) == 0) handle.Dispose();
    }
    // Await the last bounded native read and process termination before the
    // coordinator retires output custody. Do not block its UI dispatcher.
    public ValueTask DisposeAsync() => new(Task.Run(DisposeCore));
}
