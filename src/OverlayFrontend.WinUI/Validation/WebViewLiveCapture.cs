using WidgetRail.OverlayFrontend.WinUI.Browser.Capture;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using WidgetRail.PlatformBroker;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

// Development transport. Only native source selection grants capture authority;
// page messages can acknowledge/consume that grant, never select another window.
internal sealed class WebViewLiveCapture : IWebViewCaptureTransport
{
    private readonly CoreWebView2 core;
    private readonly DispatcherQueue dispatcher;
    private readonly string origin;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromSeconds(2) };
    private byte[] videoBytes = [];
    private LiveCaptureQuality quality = LiveCaptureQuality.Default;
    private const int AudioCapacity = 192000;
    private readonly Queue<(byte[] Bytes, long Time)> audioQueue = new();
    private CancellationTokenSource? cancellation;
    private Task<int>? worker;
    private TaskCompletionSource? consumed;
    private CoreWebView2SharedBuffer? video, audio;
    private int videoPending, audioPending, audioQueued;
    private long epoch, submitted, dropped, lastAck, lastHeartbeat;
    private int? lastResult;
    private string? stopReason;
    private bool disposed, heartbeatPending;
    public bool IsRunning => worker is { IsCompleted: false };
    public bool HasEnded => worker is { IsCompleted: true };
    public object Statistics => new { transport="in-process",submitted, dropped, running = IsRunning,
        result = worker?.IsCompletedSuccessfully == true ? worker.Result : lastResult, reason = stopReason, audioQueued,
        quality = quality.Id, maximumWidth = quality.MaximumWidth, maximumHeight = quality.MaximumHeight, targetFrameRate = quality.FrameRate };

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Continue();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void FrameCallback(uint kind, uint width, uint height, long time, nint bytes, uint length);
    [DllImport("WebRtcCapturePrototype.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WrailLiveCaptureRunV2(ulong window, uint pid, ulong created, string className, uint includeAudio,
        uint maximumWidth, uint maximumHeight, uint framesPerSecond, Continue current, FrameCallback frame);

    internal WebViewLiveCapture(CoreWebView2 core, DispatcherQueue dispatcher, string origin)
    {
        this.core = core; this.dispatcher = dispatcher; this.origin = origin;
        core.WebMessageReceived += Message;
        core.NavigationStarting += Navigating;
        core.ProcessFailed += Failed;
        heartbeat.Tick += async (_, _) => await KeepAliveAsync();
    }
    private void Cancel(string reason)
    {
        Interlocked.CompareExchange(ref stopReason, reason, null);
        cancellation?.Cancel();
    }
    private void Navigating(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e) => Cancel("navigation");
    private void Failed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs e) => Cancel("browser_process_failed");
    private async Task KeepAliveAsync()
    {
        if (heartbeatPending || disposed || cancellation is null) return;
        heartbeatPending = true; var selectedEpoch = epoch;
        try
        {
            if (HasEnded) { heartbeat.Stop(); await core.ExecuteScriptAsync($"WidgetRailNativeMedia.stop({selectedEpoch})"); }
            else await core.ExecuteScriptAsync($"WidgetRailNativeMedia.keep({selectedEpoch})");
            if (epoch == selectedEpoch) Volatile.Write(ref lastHeartbeat, Environment.TickCount64);
        }
        catch { if (epoch == selectedEpoch) Cancel("browser_heartbeat_failed"); }
        finally { heartbeatPending = false; }
    }
    private void Message(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != origin) return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson); var value = doc.RootElement;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("captureEpoch", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number) || number != epoch) return;
            if (value.TryGetProperty("captureConsume", out var consume) && consume.ValueKind == JsonValueKind.True) { consumed?.TrySetResult(); return; }
            if (value.TryGetProperty("captureError", out _)) { Cancel("receiver_error"); return; }
            if (!value.TryGetProperty("captureAck", out var ack) || ack.ValueKind != JsonValueKind.Number || !ack.TryGetInt32(out var kind) || kind is not (1 or 2)) return;
            Volatile.Write(ref lastAck, Environment.TickCount64);
            if (kind == 1) Interlocked.Exchange(ref videoPending, 0);
            else { Interlocked.Exchange(ref audioPending, 0); PublishAudio(epoch); }
        }
        catch (JsonException) { /* Unrelated page messages confer no authority. */ }
    }
    public async Task StartAsync(NativeWindowPreviewTarget target, bool includeAudio, LiveCaptureQuality? requestedQuality = null,CancellationToken cancellationToken=default)
    {
        var selectedQuality = requestedQuality ?? LiveCaptureQuality.Default;
        if(!selectedQuality.IsSupported)throw new ArgumentException("Unsupported capture quality.",nameof(requestedQuality));
        await lifecycle.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await StopCoreAsync();
            quality = selectedQuality;
            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var source) || source.GetLeftPart(UriPartial.Authority) != origin)
                throw new InvalidOperationException("Capture origin changed.");
            var selectedEpoch = epoch = CaptureSessionIdentity.Next();
            var cts = cancellation = new();
            var admitted = consumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            stopReason = null; lastResult = null; submitted = dropped = 0; lastHeartbeat = lastAck = Environment.TickCount64;
            using var started = JsonDocument.Parse(await core.ExecuteScriptAsync($"WidgetRailNativeMedia.start({selectedEpoch},{(includeAudio ? "true" : "false")},{JsonSerializer.Serialize(selectedQuality.Id)},{JsonSerializer.Serialize(selectedQuality.ContentHint)})"));
            if (started.RootElement.ValueKind != JsonValueKind.True) throw new InvalidOperationException("Generated media unavailable.");
            videoBytes = new byte[checked((int)(selectedQuality.MaximumWidth * selectedQuality.MaximumHeight * 4))];
            video = core.Environment.CreateSharedBuffer((ulong)videoBytes.Length);
            if (includeAudio) audio = core.Environment.CreateSharedBuffer(AudioCapacity);
            heartbeat.Start();
            Continue current = () =>
            {
                if (cts.IsCancellationRequested || Volatile.Read(ref epoch) != selectedEpoch) return 0;
                if (Environment.TickCount64 - Volatile.Read(ref lastHeartbeat) >= 10000)
                { Interlocked.CompareExchange(ref stopReason, "host_heartbeat_timeout", null); cts.Cancel(); return 0; }
                if ((Volatile.Read(ref videoPending) == 0 && Volatile.Read(ref audioPending) == 0) ||
                    Environment.TickCount64 - Volatile.Read(ref lastAck) < 10000) return 1;
                Interlocked.CompareExchange(ref stopReason, "receiver_timeout", null); cts.Cancel(); return 0;
            };
            void End(string reason) { if (Volatile.Read(ref epoch) == selectedEpoch) { Interlocked.CompareExchange(ref stopReason, reason, null); cts.Cancel(); } }
            FrameCallback callback = (kind, width, height, time, pointer, length) =>
            {
                try
                {
                    if (current() == 0 || kind is not (1 or 2)) return;
                    if (kind == 2)
                    {
                        lock (audioQueue)
                        {
                            if (length == 0 || length > AudioCapacity || audioQueued + length > AudioCapacity) { End("audio_queue_full"); return; }
                            var packet = new byte[length]; Marshal.Copy(pointer, packet, 0, (int)length);
                            audioQueue.Enqueue((packet, time)); audioQueued += (int)length;
                        }
                        if (!dispatcher.TryEnqueue(() => PublishAudio(selectedEpoch))) End("dispatcher_closed");
                        return;
                    }
                    if (width < 2 || height < 2 || width > selectedQuality.MaximumWidth || height > selectedQuality.MaximumHeight ||
                        length != (ulong)width * height * 4 || length > videoBytes.Length) { End("invalid_video_size"); return; }
                    if (Interlocked.CompareExchange(ref videoPending, 1, 0) != 0) { Interlocked.Increment(ref dropped); return; }
                    Marshal.Copy(pointer, videoBytes, 0, (int)length);
                    if (!dispatcher.TryEnqueue(() =>
                    {
                        if (current() == 0) return;
                        try
                        {
                            using (var memory = video!.OpenStream().AsStreamForWrite()) memory.Write(videoBytes, 0, (int)length);
                            core.PostSharedBufferToScript(video, CoreWebView2SharedBufferAccess.ReadOnly,
                                JsonSerializer.Serialize(new { kind, width, height, time, bytes = length, epoch = selectedEpoch }));
                            Interlocked.Increment(ref submitted);
                        }
                        catch { End("video_publish_failed"); }
                    })) End("dispatcher_closed");
                }
                catch { End("native_callback_failed"); } // Never unwind across the C++ ABI.
            };
            worker = Task.Run(async () =>
            {
                try { await admitted.Task.WaitAsync(TimeSpan.FromSeconds(10), cts.Token); }
                catch (OperationCanceledException) { return 0; }
                catch (TimeoutException) { End("stream_not_consumed"); return unchecked((int)0x800705b4); }
                Volatile.Write(ref lastAck, Environment.TickCount64);
                var hr = WrailLiveCaptureRunV2(ulong.Parse(target.Handle, NumberStyles.HexNumber), target.ProcessId,
                    ulong.Parse(target.ProcessCreated, NumberStyles.HexNumber), target.ClassName, includeAudio ? 1u : 0u,
                    selectedQuality.MaximumWidth,selectedQuality.MaximumHeight,selectedQuality.FrameRate,current, callback);
                GC.KeepAlive(current); GC.KeepAlive(callback); return hr;
            });
        }
        catch { await StopCoreAsync(); throw; }
        finally { lifecycle.Release(); }
    }
    private void PublishAudio(long selectedEpoch)
    {
        if (epoch != selectedEpoch || cancellation?.IsCancellationRequested != false || audio is null) return;
        if (Interlocked.CompareExchange(ref audioPending, 1, 0) != 0) return;
        (byte[] Bytes, long Time) packet;
        lock (audioQueue)
        {
            if (!audioQueue.TryDequeue(out packet)) { Interlocked.Exchange(ref audioPending, 0); return; }
            audioQueued -= packet.Bytes.Length;
        }
        try
        {
            using (var memory = audio.OpenStream().AsStreamForWrite()) memory.Write(packet.Bytes);
            core.PostSharedBufferToScript(audio, CoreWebView2SharedBufferAccess.ReadOnly,
                JsonSerializer.Serialize(new { kind = 2, width = 48000, height = 2, time = packet.Time, bytes = packet.Bytes.Length, epoch = selectedEpoch }));
            Interlocked.Increment(ref submitted);
        }
        catch { Cancel("audio_publish_failed"); }
    }
    public async Task StopAsync()
    {
        await lifecycle.WaitAsync();
        try { await StopCoreAsync(); }
        finally { lifecycle.Release(); }
    }
    private async Task StopCoreAsync()
    {
        var stoppingEpoch=epoch;epoch=0; heartbeat.Stop(); cancellation?.Cancel();
        try
        {
            if (worker is { } ending) lastResult = await ending;
            try { await core.ExecuteScriptAsync($"globalThis.WidgetRailNativeMedia?.stop({stoppingEpoch})"); } catch (COMException) { }
        }
        finally
        {
            worker = null; cancellation?.Dispose(); cancellation = null; consumed = null;
            lock (audioQueue) { audioQueue.Clear(); audioQueued = 0; }
            video?.Dispose(); audio?.Dispose(); video = audio = null;
            videoBytes = [];
            Interlocked.Exchange(ref videoPending, 0); Interlocked.Exchange(ref audioPending, 0);
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        core.WebMessageReceived -= Message; core.NavigationStarting -= Navigating; core.ProcessFailed -= Failed;
        await StopAsync();
    }
}
