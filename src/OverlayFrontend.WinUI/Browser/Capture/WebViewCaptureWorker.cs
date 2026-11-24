using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.WidgetProtocol;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using WidgetRail.PlatformBroker;

namespace WidgetRail.OverlayFrontend.WinUI.Browser.Capture;

/// <summary>Capture transport: WGC/loopback live in a kill-on-close worker;
/// only bounded metadata enters the frontend. COM buffer calls remain on its UI thread.</summary>
internal sealed class WebViewCaptureWorker:IWebViewCaptureTransport
{
    private readonly CoreWebView2 core;
    private readonly DispatcherQueue dispatcher;
    private readonly string origin;
    private readonly Func<bool> documentCurrent;
#if ENABLE_WIDGET_VALIDATION
    private readonly bool testBlockedExit;
#endif
    private readonly SemaphoreSlim lifecycle=new(1,1);
    private readonly DispatcherTimer heartbeat=new(){Interval=TimeSpan.FromSeconds(2)};
    private CaptureWorkerNative.BufferHandle? buffers;
    private CaptureWorkerNative.WorkerHandle? worker;
    private CancellationTokenSource? cancellation;
    private Task? pump;
    private TaskCompletionSource? consumed;
    private long epoch,videoSequence,audioSequence,submitted,dropped;
    private uint processId;
    private int? result;
    private sealed record ExitState(bool Exited,bool Forced,int Result);
    private ExitState stopped=new(true,false,0);
    private int started,capturing,ended,terminalLogged,cancellationResult;
    private LiveCaptureQuality quality=LiveCaptureQuality.Default;
    private string? reason;
    private bool disposed,heartbeatPending;
    private bool nativeH264;
    private int lastEncoderGeneration;
    private bool encoderOpen;
    public bool IsRunning=>pump is{IsCompleted:false};
    public bool HasEnded=>Volatile.Read(ref ended)!=0;
    public bool HasStarted=>Volatile.Read(ref started)!=0;
    public bool IsCapturing=>Volatile.Read(ref capturing)!=0 && !HasEnded;
    public string? Reason=>Volatile.Read(ref reason);
    public CaptureTransportStatus Status { get { var exit=Volatile.Read(ref stopped);return new(
        HasEnded?(Reason is null?WebCaptureStreamPhase.Stopped:WebCaptureStreamPhase.Failed):
        IsCapturing?WebCaptureStreamPhase.Capturing:WebCaptureStreamPhase.Prepared,Reason,exit.Exited,exit.Forced); } }
    public event Action? Ended;
    public object Statistics=>new {transport="worker",submitted,dropped,running=IsRunning,result,reason,processId,
        forcedTermination=stopped.Forced,workerExited=stopped.Exited,stopResult=stopped.Result,
        quality=quality.Id,maximumWidth=quality.MaximumWidth,maximumHeight=quality.MaximumHeight,targetFrameRate=quality.FrameRate};

    internal WebViewCaptureWorker(CoreWebView2 core,DispatcherQueue dispatcher,string origin,Func<bool>? documentCurrent=null
#if ENABLE_WIDGET_VALIDATION
        ,bool testBlockedExit=false
#endif
        )
    {
        this.core=core;this.dispatcher=dispatcher;this.origin=origin;this.documentCurrent=documentCurrent??(()=>true);
#if ENABLE_WIDGET_VALIDATION
        this.testBlockedExit=testBlockedExit;
#endif
        if(Marshal.SizeOf<CaptureWorkerNative.Configuration>()!=568||Marshal.SizeOf<CaptureWorkerNative.Packet>()!=56||Marshal.SizeOf<CaptureWorkerNative.StopResult>()!=16)
            throw new InvalidOperationException("Capture worker ABI layout mismatch.");
        core.WebMessageReceived+=Message;core.NavigationStarting+=Navigating;core.ProcessFailed+=Failed;
        heartbeat.Tick+=HeartbeatTick;
    }
    private async void HeartbeatTick(object? sender,object args)=>await HeartbeatAsync();
    private void Cancel(string value,int hresult=0)
    {
        if(Interlocked.CompareExchange(ref reason,value,null) is null)Volatile.Write(ref cancellationResult,hresult);
        cancellation?.Cancel();
    }
    private void LogTermination(string fallbackReason,int? failureResult=null)
    {
        if(epoch==0||Interlocked.Exchange(ref terminalLogged,1)!=0)return;
        var exit=Volatile.Read(ref stopped);
        var selectedReason=Volatile.Read(ref reason)??fallbackReason;
        // Only host-owned codes and numeric counters are recorded. Never serialize
        // exception text, page messages, source identity, titles, account or URL.
        if(selectedReason.Length>64||selectedReason.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))selectedReason="unclassified";
        var captureResult=result??failureResult;
        var hr=captureResult is {} code?$"0x{code:X8}":"none";
        Diagnostics.FrontendFailureLog.Current.Write("web-capture-terminal",null,
            FormattableString.Invariant($"reason={selectedReason} capture_hresult={hr} cause_hresult=0x{Volatile.Read(ref cancellationResult):X8} submitted_packets={Interlocked.Read(ref submitted)} dropped_video={Interlocked.Read(ref dropped)} worker_started={HasStarted} first_video_posted={Volatile.Read(ref capturing)!=0} worker_exited={exit.Exited} forced_termination={exit.Forced} stop_hresult=0x{exit.Result:X8} preset={(quality.IsSupported?quality.Id:"unknown")}"));
    }
    private void Navigating(CoreWebView2 sender,CoreWebView2NavigationStartingEventArgs args)
    {
        // The browser's earlier policy handler may reject an external link.
        // Rejected navigation leaves the admitted capture document unchanged.
        if(!args.Cancel)Cancel("navigation");
    }
    private void Failed(CoreWebView2 sender,CoreWebView2ProcessFailedEventArgs args)
    {
        if(BrowserSurface.RequiresPageRecovery(args.ProcessFailedKind))Cancel("browser_process_failed");
    }
    private bool OriginCurrent=>documentCurrent()&&Uri.TryCreate(core.Source,UriKind.Absolute,out var uri)&&uri.GetLeftPart(UriPartial.Authority)==origin;
    private nint CoreReference()=>WinRT.MarshalInspectable<CoreWebView2>.FromManaged(core);
    private async Task HeartbeatAsync()
    {
        if(heartbeatPending||disposed||cancellation is null)return;
        heartbeatPending=true;var current=epoch;
        try
        {
            if(!OriginCurrent){Cancel("origin_changed");return;}
            var ending=HasEnded;
            var acknowledgement=await core.ExecuteScriptAsync(ending
                ?$"globalThis.WidgetRailNativeMedia?.stop?.({current})"
                :$"globalThis.WidgetRailNativeMedia?.keep?.({current})").AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            // An old script completion must not renew or cancel a replacement.
            if(current!=epoch||disposed||cancellation?.IsCancellationRequested!=false)return;
            if(ending||HasEnded){heartbeat.Stop();return;}
            if(!OriginCurrent){Cancel("origin_changed");return;}
            if(!IsActiveHeartbeat(acknowledgement)){Cancel("receiver_heartbeat_invalid");return;}
            if(Volatile.Read(ref worker) is {IsClosed:false} currentWorker)
            {
                var hr=CaptureWorkerNative.WrailCaptureWorkerHeartbeat(currentWorker,(ulong)current);
                if(hr<0 && hr!=unchecked((int)0x80000013))Cancel("worker_heartbeat_failed",hr);
            }
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        {if(current==epoch&&!disposed&&cancellation?.IsCancellationRequested==false)Cancel("browser_heartbeat_failed",error.HResult);}
        finally{heartbeatPending=false;}
    }
    internal static bool IsActiveHeartbeat(string? acknowledgement)
    {
        if(acknowledgement is not {Length:>0 and <=2048})return false;
        try
        {
            using var document=JsonDocument.Parse(acknowledgement,new JsonDocumentOptions{MaxDepth=4});
            return document.RootElement.ValueKind==JsonValueKind.Object&&
                document.RootElement.TryGetProperty("active",out var active)&&active.ValueKind==JsonValueKind.True;
        }
        catch(JsonException){return false;}
    }
    private void Message(CoreWebView2 sender,CoreWebView2WebMessageReceivedEventArgs args)
    {
        if(!Uri.TryCreate(args.Source,UriKind.Absolute,out var uri)||uri.GetLeftPart(UriPartial.Authority)!=origin)return;
        try
        {
            var json=args.WebMessageAsJson;if(json.Length>2048||!OriginCurrent)return;
            using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=4});var value=document.RootElement;
            if(value.ValueKind!=JsonValueKind.Object||!value.TryGetProperty("captureEpoch",out var id)||!id.TryGetInt64(out var selected)||selected!=epoch)return;
            if(value.TryGetProperty("captureConsume",out var consume)&&consume.ValueKind==JsonValueKind.True){consumed?.TrySetResult();return;}
            if(value.TryGetProperty("captureError",out _)){Cancel("receiver_error");return;}
            if(value.TryGetProperty("captureEncoder",out var encoder)){
                if(!nativeH264||cancellation?.IsCancellationRequested!=false||worker is not {IsClosed:false} current||
                    !encoder.TryGetInt32(out var generation)||generation<=0||generation<lastEncoderGeneration||
                    !value.TryGetProperty("bitrate",out var rate)||!rate.TryGetInt32(out var bitrate)||bitrate< -1||
                    (generation==lastEncoderGeneration&&!encoderOpen))return;
                var key=value.TryGetProperty("keyframe",out var request)&&request.ValueKind==JsonValueKind.True;
                lastEncoderGeneration=generation;encoderOpen=bitrate>=0;
                _=CaptureWorkerNative.WrailCaptureWorkerEncoding(current,(ulong)selected,encoderOpen?(uint)generation:0,
                    (uint)Math.Max(0,bitrate),key?1u:0u);
                return;
            }
            if(!value.TryGetProperty("captureAck",out var kindValue)||!kindValue.TryGetUInt32(out var kind)||kind is not(1 or 2)||
                !value.TryGetProperty("captureSequence",out var sequenceValue)||!sequenceValue.TryGetInt64(out var sequence)||sequence<=0)return;
            if(cancellation?.IsCancellationRequested!=false||Volatile.Read(ref worker) is not {IsClosed:false} currentWorker)return;
            ref var pending=ref(kind==1?ref videoSequence:ref audioSequence);
            if(Interlocked.CompareExchange(ref pending,0,sequence)!=sequence)return;
            _=CaptureWorkerNative.WrailCaptureWorkerAcknowledge(currentWorker,(ulong)selected,kind,(ulong)sequence);
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException or ObjectDisposedException){ }
    }
    public async Task StartAsync(NativeWindowPreviewTarget target,bool includeAudio,LiveCaptureQuality? requestedQuality=null,CancellationToken cancellationToken=default)
    {
        var selected=requestedQuality??LiveCaptureQuality.Default;
        if(!selected.IsSupported)throw new ArgumentException("Unsupported capture quality.",nameof(requestedQuality));
        if(target.ClassName.Length>=256)throw new ArgumentException("Invalid source class.",nameof(target));
        await lifecycle.WaitAsync(cancellationToken);
        long attemptedEpoch=0;
        try
        {
            ObjectDisposedException.ThrowIf(disposed,this);await StopCoreAsync();
            if(!OriginCurrent)throw new InvalidOperationException("Capture origin changed.");
            quality=selected;var current=attemptedEpoch=epoch=CaptureSessionIdentity.Next();var cts=cancellation=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            consumed=new(TaskCreationOptions.RunContinuationsAsynchronously);
            submitted=dropped=0;processId=0;result=null;reason=null;stopped=new(true,false,0);started=capturing=ended=terminalLogged=cancellationResult=0;
            var videoBytes=checked(selected.MaximumWidth*selected.MaximumHeight*4);
            if(videoBytes>LiveCaptureQuality.MaximumVideoBytes)throw new InvalidOperationException("Capture video exceeds the transport budget.");
            nativeH264=selected.ContentHint=="detail"&&
                Environment.GetEnvironmentVariable("WIDGETRAIL_EXPERIMENTAL_NATIVE_H264")=="1";
            lastEncoderGeneration=0;encoderOpen=false;
            if(nativeH264)videoBytes=checked(videoBytes+524288+24);
            var reference=CoreReference();
            try
            {
                Marshal.ThrowExceptionForHR(CaptureWorkerNative.WrailCaptureBuffersCreate(reference,videoBytes,includeAudio?192000u:0,out var value));
                buffers=new(value,dispatcher);
            }
            finally{Marshal.Release(reference);}
            Marshal.ThrowExceptionForHR(CaptureWorkerNative.WrailCaptureBuffersMappings(buffers,out var videoMap,out var audioMap));
            using var jsStarted=JsonDocument.Parse(await core.ExecuteScriptAsync($"WidgetRailNativeMedia.start({current},{(includeAudio?"true":"false")},'{selected.Id}','{selected.ContentHint}',{(nativeH264?"true":"false")})").AsTask().WaitAsync(TimeSpan.FromSeconds(5),cts.Token));
            cts.Token.ThrowIfCancellationRequested();
            if(!OriginCurrent)throw new OperationCanceledException("Capture document changed.");
            if(jsStarted.RootElement.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Generated media unavailable.");
            var configuration=new CaptureWorkerNative.Configuration
            {
                Size=568,
#if ENABLE_WIDGET_VALIDATION
                Flags=(testBlockedExit?2u:0)|(nativeH264?4u:0),
#else
                Flags=nativeH264?4u:0,
#endif
                Epoch=(ulong)current,Window=ulong.Parse(target.Handle,NumberStyles.HexNumber),
                Created=ulong.Parse(target.ProcessCreated,NumberStyles.HexNumber),ProcessId=target.ProcessId,IncludeAudio=includeAudio?1u:0,
                MaximumWidth=selected.MaximumWidth,MaximumHeight=selected.MaximumHeight,FrameRate=selected.FrameRate,ClassName=target.ClassName,
            };
            var executable=Path.Combine(AppContext.BaseDirectory,"WebRtcCaptureWorker.exe");
            if(!File.Exists(executable))throw new InvalidOperationException("The capture worker is not installed.");
            var selectedBuffers=buffers;var lease=false;selectedBuffers.DangerousAddRef(ref lease);
            try{pump=Task.Run(()=>PumpAsync(executable,configuration,videoMap,videoBytes,audioMap,selectedBuffers,consumed.Task,cts.Token));}
            catch{if(lease)selectedBuffers.DangerousRelease();throw;}
            heartbeat.Start();
        }
        catch(Exception error)
        {
            try{await StopCoreAsync();}
            finally
            {
                // Once a pump exists it owns the terminal record, including any
                // late native exit after a parent wait timeout.
                if(attemptedEpoch!=0&&pump is null)LogTermination("start_failed",error.HResult);
            }
            throw;
        }
        finally{lifecycle.Release();}
    }
    private async Task PumpAsync(string executable,CaptureWorkerNative.Configuration configuration,ulong videoMap,uint videoBytes,ulong audioMap,
        CaptureWorkerNative.BufferHandle selectedBuffers,Task admitted,CancellationToken token)
    {
        CaptureWorkerNative.WorkerHandle? owned=null;
        try
        {
            await admitted.WaitAsync(TimeSpan.FromSeconds(10),token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(CaptureWorkerNative.WrailCaptureWorkerStart(executable,in configuration,videoMap,videoBytes,audioMap,configuration.IncludeAudio!=0?192000u:0,out var value));
            owned=new(value);Volatile.Write(ref stopped,new(false,false,0));Volatile.Write(ref worker,owned);processId=CaptureWorkerNative.WrailCaptureWorkerProcessId(owned);
            while(!token.IsCancellationRequested)
            {
                var hr=CaptureWorkerNative.WrailCaptureWorkerRead(owned,out var packet,250);
                if(hr==1)continue;
                if(hr<0){result=hr;Interlocked.CompareExchange(ref reason,"worker_disconnected",null);break;}
                if(packet.Kind==3){Volatile.Write(ref started,1);continue;}
                if(packet.Kind==4){result=packet.Result;if(packet.Result<0)Interlocked.CompareExchange(ref reason,
                    packet.Result==unchecked((int)0x80070578)?"source_closed":"capture_ended",null);break;}
                if(packet.Kind is not(1 or 2))continue;
                if(packet.Epoch!=configuration.Epoch||packet.Sequence>long.MaxValue)throw new InvalidDataException("Capture worker epoch changed.");
                dropped=packet.Dropped;
                if(!dispatcher.TryEnqueue(()=>Publish(packet,selectedBuffers,token)))throw new InvalidOperationException("Capture dispatcher ended.");
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception error) when(error is not OutOfMemoryException){result=error.HResult;Interlocked.CompareExchange(ref reason,"worker_failed",null);}
        finally
        {
            if(owned is not null)
            {
                _=CaptureWorkerNative.WrailCaptureWorkerStop(owned,600,out var nativeStop);
                Volatile.Write(ref stopped,new(nativeStop.Exited!=0,nativeStop.Forced!=0,nativeStop.Result));
                if(nativeStop.Exited==0)Interlocked.Exchange(ref reason,"stop_unconfirmed");
                if(nativeStop.Exited!=0){Interlocked.CompareExchange(ref worker,null,owned);owned.Dispose();}
                // Retain the exact process/job handle when exit is unconfirmed.
                // A later StopAsync retries verification rather than losing authority.
            }
            selectedBuffers.DangerousRelease();
            Volatile.Write(ref ended,1);
            LogTermination(token.IsCancellationRequested?"cancelled":"source_ended");
            dispatcher.TryEnqueue(()=>{if((ulong)epoch==configuration.Epoch)Ended?.Invoke();});
        }
    }
    private void Publish(CaptureWorkerNative.Packet packet,CaptureWorkerNative.BufferHandle selectedBuffers,CancellationToken token)
    {
        if(token.IsCancellationRequested||disposed||packet.Epoch!=(ulong)epoch||!ReferenceEquals(buffers,selectedBuffers))return;
        try
        {
            if(!OriginCurrent){Cancel("origin_changed");return;}
            var reference=CoreReference();
            try
            {
                if(packet.Kind==1)Interlocked.Exchange(ref videoSequence,(long)packet.Sequence);else Interlocked.Exchange(ref audioSequence,(long)packet.Sequence);
                Marshal.ThrowExceptionForHR(CaptureWorkerNative.WrailCaptureBuffersPost(reference,selectedBuffers,packet.Kind,
                    JsonSerializer.Serialize(new CaptureBufferMetadata(packet.Kind,packet.Width,packet.Height,packet.Microseconds,packet.Bytes,packet.Epoch,packet.Sequence),CaptureBufferJsonContext.Default.CaptureBufferMetadata)));
                if(packet.Kind==1)Volatile.Write(ref capturing,1);
                Interlocked.Increment(ref submitted);
            }
            finally{Marshal.Release(reference);}
        }
        catch(Exception error) when(error is not OutOfMemoryException){Cancel("buffer_publish_failed",error.HResult);}
    }
    public async Task StopAsync(){await lifecycle.WaitAsync();try{await StopCoreAsync();}finally{lifecycle.Release();}}
    private async Task StopCoreAsync()
    {
        var stoppingEpoch=epoch;heartbeat.Stop();cancellation?.Cancel();consumed?.TrySetCanceled();
        if(pump is { } ending)
        {
            // Native stop itself is bounded. Also bound the parent wait if OS
            // process creation/IPC unexpectedly stalls; a retiring pump retains
            // the mappings and must finish before this transport can start again.
            try{await ending.WaitAsync(TimeSpan.FromSeconds(3));}
            catch(TimeoutException){Interlocked.Exchange(ref reason,"stop_unconfirmed");throw;}
            if(!Volatile.Read(ref stopped).Exited && Volatile.Read(ref worker) is {} retiring)
            {
                var retried=await Task.Run(()=>{_=CaptureWorkerNative.WrailCaptureWorkerStop(retiring,600,out var state);return state;});
                Volatile.Write(ref stopped,new(retried.Exited!=0,retried.Forced!=0,retried.Result));
                if(retried.Exited!=0)
                {
                    Interlocked.CompareExchange(ref worker,null,retiring);retiring.Dispose();
                    Interlocked.CompareExchange(ref reason,null,"stop_unconfirmed");
                }
            }
            if(!Volatile.Read(ref stopped).Exited){Interlocked.Exchange(ref reason,"stop_unconfirmed");throw new InvalidOperationException("Capture worker exit could not be confirmed.");}
            pump=null;
        }
        try{await core.ExecuteScriptAsync($"globalThis.WidgetRailNativeMedia?.stop({stoppingEpoch})").AsTask().WaitAsync(TimeSpan.FromSeconds(2));}
        catch(Exception error) when(error is COMException or ObjectDisposedException or InvalidOperationException or TimeoutException){ }
        buffers?.Dispose();buffers=null;cancellation?.Dispose();cancellation=null;consumed=null;
        videoSequence=audioSequence=0;
    }
    public async ValueTask DisposeAsync()
    {
        if(!disposed)
        {
            disposed=true;
            // Core may already be closed: detach each subscription independently
            // so one failed COM removal cannot skip the remaining subscriptions.
            Detach(()=>core.WebMessageReceived-=Message);
            Detach(()=>core.NavigationStarting-=Navigating);
            Detach(()=>core.ProcessFailed-=Failed);
            Detach(()=>heartbeat.Tick-=HeartbeatTick);
        }
        try{await StopAsync();}
        catch(Exception error) when(error is not OutOfMemoryException)
        {
            Interlocked.CompareExchange(ref reason,"dispose_incomplete",null);
            buffers?.Dispose();buffers=null; // Pump's lease protects late startup.
            // Native exit remains unconfirmed. Keep the exact job/process handle
            // and let a later DisposeAsync or StopAsync retry verification.
            throw;
        }
        static void Detach(Action remove)
        {
            try{remove();}
            catch(Exception error) when(error is COMException or ObjectDisposedException or InvalidOperationException){ }
        }
    }
}

internal sealed record CaptureBufferMetadata(uint kind,uint width,uint height,long time,uint bytes,ulong epoch,ulong sequence);
[JsonSerializable(typeof(CaptureBufferMetadata))]
internal partial class CaptureBufferJsonContext:JsonSerializerContext { }
