using System.IO.Pipes;
using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Trusted bundle fixture through real session admission, native WebView2 and SDK adapter runtime.</summary>
internal sealed class EmbeddedMediaValidationPage : Page, IAsyncDisposable
{
    private readonly TextBlock status = new() { Text = "Embedded media checks pending", TextWrapping = TextWrapping.Wrap };
    private readonly Grid viewport = new() { Width = 720, Height = 360 };
    private readonly List<string> checks = [];
    private readonly List<string> diagnostics = [];
    private readonly List<EmbeddedMediaPlaybackEvent> events = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly NamedPipeServerStream pipe;
    private readonly string pipeName = "wrail-native-media-" + Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, (string Type, byte[] Bytes)> assets = [];
    private PresentationSession? session;
    private EmbeddedMediaSurface? surface;
    private Task? run;
    private Task? serving;
    private Exception? serverFailure;
    private ContentDialog? dialog;
    private EmbeddedMediaSession? declaration;
    private bool parked;
    private long snapshotSequence;
    private int resolveCount;
    private bool retired;
    private static BridgeWidgetDescriptor Descriptor => new() { Id = "media-validation", Name = "Embedded media", InstanceId = "media-validation.instance",
        RuntimeGeneration = new('a', 32), PresentationGeneration = new('b', 32), PackageContentDigest = new('c', 64), Icon = WidgetGlyph.Music };

    public EmbeddedMediaValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "EmbeddedMedia.Status");
        AutomationProperties.SetAutomationId(viewport, "EmbeddedMedia.Viewport");
        Content = new StackPanel { Spacing = 12, Children = { status, viewport } };
        pipe = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Loaded += (_, _) => run ??= RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            AddAssets();
            declaration = new()
            {
                Id = "fixture-player", AccessibleName = "Trusted embedded player", EntryAsset = "adapter/index.html",
                Surface = new() { PreferredWidth = 720, PreferredHeight = 360, MinimumWidth = 320, MinimumHeight = 180 }, AspectRatio = 2,
                Resources = assets.Select(asset => new EmbeddedMediaResource { Path = asset.Key, ContentType = asset.Value.Type }).ToArray(),
                Commands = [EmbeddedMediaCommand.Activate, EmbeddedMediaCommand.Back, EmbeddedMediaCommand.TogglePlayback],
                AllowedFrameOrigins = ["https://www.youtube.com"], AllowedFrameDomainFamilies = ["googlevideo.com"],
            };
            serving = ObserveServerAsync();
            status.Text = "Connecting fixture session";
            session = await PresentationSession.ConnectAsync(pipeName);
            await session.ListWidgetsAsync();
            status.Text = "Admitting sealed media document";
            var frame = await SnapshotAsync();
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            CheckPolicy(document);
            CheckTransport();
            surface = new(session, document);
            surface.Diagnostic += code => diagnostics.Add(code);
            viewport.Children.Add(surface.Element);
            surface.UpdatePresentation(true, true);
            await Until(() => surface.IsReady);
            Check(surface.IsReady && surface.FailureCode is null, "sealed HTML and SDK runtime initialize in native WebView2");
            await Command(EmbeddedMediaPlaybackCommandKind.Load, 1);
            Check(events.Last().State == EmbeddedMediaPlaybackState.Ready, "typed Load round-trips through strict adapter transport and session admission");
            await Command(EmbeddedMediaPlaybackCommandKind.Play, 2);
            Check(events.Last().State == EmbeddedMediaPlaybackState.Playing && events.Last().ErrorCode is null, "host input dispatch creates a trusted activation and starts native audio");
            await Command(EmbeddedMediaPlaybackCommandKind.Pause, 3);
            Check(events.Last().State == EmbeddedMediaPlaybackState.Paused, "typed Pause reports the paused native audio state");
            parked = true;
            await SnapshotAsync(); surface.UpdatePresentation(false, false);
            Check(session.GetEmbeddedMediaState(document) is not null && !surface.IsRetired, "declared session without viewport parks the existing document");
            Check(!surface.Dispatch(EmbeddedMediaCommand.Activate), "parked media cannot consume spatial controller input");
            await Command(EmbeddedMediaPlaybackCommandKind.SetVolume, 4, volume: .3);
            Check(Math.Abs(events.Last().Volume - .3) < .001, "typed playback updates continue while parked");
            parked = false;
            await SnapshotAsync(); surface.UpdatePresentation(true, true);
            Check(resolveCount == 1 && surface.IsReady, "resume retains the exact admitted document without resource resolution");
            dialog = new() { XamlRoot = XamlRoot, Title = "Native modal above media", Content = "The media remains alive under this host modal.", CloseButtonText = "Close" };
            AutomationProperties.SetAutomationId(dialog, "EmbeddedMedia.Dialog");
            surface.UpdatePresentation(true, false);
            var showing = dialog.ShowAsync();
            await Task.Delay(500);
            Check(!surface.Dispatch(EmbeddedMediaCommand.Activate), "modal coverage gates controller input without closing media");
            Check(!surface.Element.IsHitTestVisible && surface.IsReady, "modal coverage also gates pointer input while keeping playback ready");
            // Leave enough time for an automated screenshot; no physical check is required.
            Write(new { passed = true, phase = "modal", checks, diagnostics });
            await Task.Delay(1500);
            dialog.Hide(); await showing; dialog = null;
            surface.UpdatePresentation(true, true);
            declaration = null;
            await SnapshotAsync(); surface.Refresh();
            Check(surface.IsRetired && session.GetEmbeddedMediaState(document) is null, "omitting the declaration retires and closes the native browser");
            status.Text = $"Embedded media checks passed: {checks.Count}";
            Write(new { passed = true, phase = "complete", checks, diagnostics, events });
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error)
        {
            status.Text = "Embedded media check failed: " + error.Message;
            Write(new { passed = false, checks, diagnostics, error = error.ToString(), events });
        }
    }

    private void CheckPolicy(WidgetPresentationEmbeddedMediaDocument document)
    {
        var policy = new EmbeddedMediaRequestPolicy(document, declaration!, "WidgetRail.Validation");
        Check(policy.IsDocument(policy.EntryUri) && !policy.IsDocument(policy.EntryUri + "?x=1"), "document authority requires the exact sealed entry URI");
        Check(policy.Resolve(policy.EntryUri, "GET", null)?.Status == 200 && policy.Resolve(policy.EntryUri + "?x=1", "GET", null) is null, "query variants cannot alias package resources");
        Check(policy.Resolve(policy.EntryUri, "POST", null)?.Status == 405, "package resources admit GET only");
        Check(policy.AllowsFrame("https://www.youtube.com/embed/test") && !policy.AllowsFrame("https://r1.googlevideo.com/video"), "frame navigation uses exact allowed origins, never domain families");
        Check(policy.AllowsRemoteResource("https://r1.googlevideo.com/video") && !policy.AllowsRemoteResource("https://googlevideo.com.evil.test/video") &&
            !policy.AllowsRemoteResource("https://www.youtube.com@evil.test/") && !policy.AllowsRemoteResource("http://www.youtube.com/video"), "remote resource hosts enforce HTTPS, origin and suffix boundaries");
        Check(policy.ApplicationReferer == "https://widgetrail.validation/" && EmbeddedMediaRequestPolicy.CreateApplicationReferer("bad/identity") is null, "remote referer comes only from canonical installed application identity");
        Check(new EmbeddedMediaRequestPolicy(document, declaration!, null).AllowsRemoteResource("https://www.youtube.com/embed/test") == false, "missing application identity fails closed for remote resources");
        using var reply = policy.Resolve(policy.Origin + "/adapter/tone.wav", "GET", "bytes=2-5")!.Content;
        Check(reply?.Length == 4 && reply.ReadByte() == assets["adapter/tone.wav"].Bytes[2], "media byte range returns the exact requested bytes");
        Check(EmbeddedMediaRequestPolicy.TryRange("bytes=-4", 12, out var start, out var length) && start == 8 && length == 4 &&
            EmbeddedMediaRequestPolicy.TryRange("bytes=4-", 12, out start, out length) && start == 4 && length == 8, "suffix and open-ended ranges preserve the native resource contract");
        foreach (var bad in new[] { "bytes=-0", "bytes=-13", "bytes=4-12", "bytes=12-", "bytes=4-2", "bytes=0-1,3-4", "bytes=1--2", "items=1-2" })
            Check(!EmbeddedMediaRequestPolicy.TryRange(bad, 12, out _, out _), "reject malformed or unsatisfiable range " + bad);
    }

    private void CheckTransport()
    {
        var transport = new EmbeddedMediaTransport("transport-test");
        var initialize = JsonNode.Parse(transport.Initialize())!.AsObject();
        JsonObject Envelope(long sequence, string type, long commandId, long commandSequence = 0)
        {
            var value = new JsonObject();
            foreach (var property in initialize.Where(property => property.Key.EndsWith("Generation", StringComparison.Ordinal))) value[property.Key] = property.Value!.DeepClone();
            value["type"] = type; value["eventSequence"] = sequence; value["commandId"] = commandId; value["commandSequence"] = commandSequence;
            value["focus"] = "play"; value["playing"] = false;
            value["bounds"] = new JsonObject { ["x"] = 4, ["y"] = 4, ["width"] = 100, ["height"] = 50 };
            return value;
        }
        string Json(JsonObject value) => value.ToJsonString(new() { WriteIndented = false });
        var ready = Envelope(1, "ready", 1);
        Check(!transport.TryAccept(Json(ready).Replace("\"type\":", "\"type\":\"ready\",\"type\":"), out _), "duplicate message fields are rejected");
        var stale = (JsonObject)ready.DeepClone(); stale["documentGeneration"] = 999999;
        Check(!transport.TryAccept(Json(stale), out _), "stale document message authority is rejected");
        Check(transport.TryAccept(Json(ready), out _) && transport.Ready, "valid ready message acknowledges exactly one initialize command");
        Check(!transport.TryAccept(Json(ready), out _), "replayed adapter event sequence is rejected");
        Check(transport.Dispatch(new EmbeddedMediaPlaybackCommand { Kind = EmbeddedMediaPlaybackCommandKind.Load, Sequence = 3, MediaKey = "song" }) is not null &&
            transport.Dispatch(EmbeddedMediaCommand.Activate) is null, "one command in flight prevents stale command queues");
        var terminal = Envelope(2, "media", 2, 3);
        terminal["mediaKey"] = "song"; terminal["playbackState"] = "ready"; terminal["positionSeconds"] = 0; terminal["durationSeconds"] = 10; terminal["volume"] = .5;
        var partial = (JsonObject)terminal.DeepClone(); partial["muted"] = true;
        Check(!transport.TryAccept(Json(partial), out _), "partial preference groups cannot enter playback state");
        var wrong = (JsonObject)terminal.DeepClone(); wrong["commandSequence"] = 9;
        Check(!transport.TryAccept(Json(wrong), out _), "acknowledgements require the exact pending command sequence");
        var mutations = new Dictionary<string, Action<JsonObject>>
        {
            ["unknown field"] = value => value["unexpected"] = true,
            ["missing field"] = value => value.Remove("focus"),
            ["wrong command id"] = value => value["commandId"] = 999,
            ["wrong media key"] = value => value["mediaKey"] = "other",
            ["unsafe integer"] = value => value["eventSequence"] = 9007199254740992L,
            ["negative position"] = value => value["positionSeconds"] = -1,
            ["position after duration"] = value => value["positionSeconds"] = 11,
            ["excessive duration"] = value => value["durationSeconds"] = 86401,
            ["excessive volume"] = value => value["volume"] = 1.1,
            ["inconsistent playing flag"] = value => value["playing"] = true,
            ["unknown playback state"] = value => value["playbackState"] = "sleeping",
            ["invalid action identifier"] = value => value["focus"] = "../action",
            ["unknown nested field"] = value => value["bounds"]!["extra"] = 1,
            ["empty action bounds"] = value => value["bounds"]!["width"] = 0,
            ["off-document bounds"] = value => value["bounds"]!["x"] = 8192,
            ["invalid error identifier"] = value => value["errorCode"] = "bad/error",
            ["excessive playback rate"] = value => { value["playbackRate"] = 4; value["muted"] = false; value["loop"] = false; },
        };
        foreach (var mutation in mutations)
        {
            var candidate = (JsonObject)terminal.DeepClone(); mutation.Value(candidate);
            Check(!transport.TryAccept(Json(candidate), out _), "reject adapter " + mutation.Key);
        }
        Check(transport.TryAccept(Json(terminal), out var observation) && observation!.Playback?.MediaKey == "song" && !transport.Busy, "valid terminal observation publishes playback and releases transport ownership");
        Check(transport.Dispatch(EmbeddedMediaCommand.Activate) is not null, "spatial activation uses the existing SDK arm-activate protocol");
        var armed = Envelope(3, "armed", 3);
        Check(transport.TryAccept(Json(armed), out _) && transport.Busy, "armed acknowledgement keeps command ownership until trusted input completes");
        armed["eventSequence"] = 4;
        Check(!transport.TryAccept(Json(armed), out _), "second armed acknowledgement cannot inject another click");
    }

    private async Task Command(EmbeddedMediaPlaybackCommandKind kind, long sequence, double? volume = null)
    {
        declaration = declaration! with { PendingCommand = new() { Kind = kind, Sequence = sequence, MediaKey = "fixture", Volume = volume } };
        await SnapshotAsync(); surface!.Refresh();
        await Until(() => events.Any(value => value.CommandSequence == sequence));
        Check(events.Last(value => value.CommandSequence == sequence).ErrorCode is null, "command " + kind + " completes without adapter failure");
    }

    private async Task<WidgetPresentationFrame> SnapshotAsync()
    {
        var next = session!.EstablishPresentationAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Interactive, lifetime.Token);
        if (await Task.WhenAny(next, serving!) == serving) throw serverFailure ?? new InvalidOperationException("Fixture server closed unexpectedly");
        return await next.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async Task ObserveServerAsync()
    {
        try { await ServeAsync(); }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error)
        {
            serverFailure = error;
            Write(new { passed = false, phase = "fixture-server", checks, diagnostics, error = error.ToString() });
            status.Text = "Fixture server failed: " + error.Message;
        }
    }

    private async Task ServeAsync()
    {
        await pipe.WaitForConnectionAsync(lifetime.Token);
        while (!lifetime.IsCancellationRequested)
        {
            var request = await ReadAsync();
            object body = new { }; string type = "acknowledged";
            switch (request.Type)
            {
                case "hello": type = "hello-accepted"; break;
                case "list-widgets": type = "widgets"; body = new { revision = 1, isComplete = true, widgets = new[] { Descriptor } }; break;
                case "resolve-embedded-media":
                    ++resolveCount;
                    var media = declaration!;
                    type = "embedded-media";
                    body = new { widgetId = Descriptor.Id, instanceId = Descriptor.InstanceId, runtimeGeneration = Descriptor.RuntimeGeneration, presentationGeneration = Descriptor.PresentationGeneration,
                        sequence = snapshotSequence, sessionId = media.Id, media.EntryAsset, media.Surface, media.AspectRatio, media.AccessibleName, media.Commands, media.AllowedFrameOrigins,
                        media.AllowedFrameDomainFamilies, media.PendingCommand, media.SupportedPresentations, media.MediaSeekStepSeconds,
                        resources = assets.Select(asset => new { path = asset.Key, contentType = asset.Value.Type, sha256 = Convert.ToHexString(SHA256.HashData(asset.Value.Bytes)).ToLowerInvariant(), contentBase64 = Convert.ToBase64String(asset.Value.Bytes) }).ToArray() };
                    break;
                case "embedded-media-playback-event":
                    var observed = request.Payload.GetProperty("event").Deserialize<EmbeddedMediaPlaybackEvent>(JsonOptions)!;
                    events.Add(observed); break;
                case "stop":
                    await ReplyAsync(request.RequestId, type, body); return;
                case "set-widget-lifecycle": case "get-snapshot":
                    type = "snapshot";
                    var snapshot = new ViewSnapshot { Sequence = ++snapshotSequence, WidgetInstanceId = Descriptor.InstanceId, ActiveInputScopeId = "page", EmbeddedMediaSession = declaration,
                        Root = new() { Id = "page", Kind = ViewNodeKind.Stack, Children = declaration is null || parked ? [] : [new ViewNode { Id = "viewport", Kind = ViewNodeKind.MediaViewport, MediaSessionId = declaration.Id, AccessibilityLabel = declaration.AccessibleName }] } };
                    var errors = ViewSnapshotValidator.Validate(snapshot);
                    if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(error => error.Message)));
                    using (var serialized = JsonDocument.Parse(SnapshotJson.Serialize(snapshot)))
                        body = new { widgetId = Descriptor.Id, transactionKind = "ordinaryCheckpoint", baseSequence = 0, recoveryOriginSequence = 0, snapshot = serialized.RootElement.Clone(), renderStyles = new Dictionary<string, BridgeNodeRenderStyles>() };
                    break;
                default: throw new InvalidOperationException("Unexpected fixture request: " + request.Type);
            }
            await ReplyAsync(request.RequestId, type, body);
        }
    }

    private sealed record FixtureEnvelope(string Type, long RequestId, JsonElement Payload);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    private async Task<FixtureEnvelope> ReadAsync()
    {
        var prefix = new byte[4]; await pipe.ReadExactlyAsync(prefix, lifetime.Token);
        var size = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (size is <= 0 or > BridgeProtocol.DefaultMaximumMessageBytes) throw new InvalidDataException("Fixture frame limit");
        var bytes = new byte[size]; await pipe.ReadExactlyAsync(bytes, lifetime.Token);
        return JsonSerializer.Deserialize<FixtureEnvelope>(bytes, JsonOptions) ?? throw new InvalidDataException("Missing fixture envelope");
    }
    private async Task ReplyAsync(long requestId, string type, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { protocolVersion = BridgeProtocol.CurrentVersion, type, requestId, payload }, JsonOptions);
        var prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await pipe.WriteAsync(prefix, lifetime.Token); await pipe.WriteAsync(bytes, lifetime.Token); await pipe.FlushAsync(lifetime.Token);
    }

    private void AddAssets()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WidgetRail.MediaValidation.AdapterRuntime.js") ?? throw new InvalidOperationException("SDK adapter runtime fixture resource missing");
        using var copy = new MemoryStream(); stream.CopyTo(copy);
        assets["adapter/runtime.js"] = ("text/javascript", copy.ToArray());
        assets["adapter/tone.wav"] = ("audio/wav", Wav());
        assets["adapter/index.html"] = ("text/html", Encoding.UTF8.GetBytes("""
            <!doctype html><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'unsafe-inline'">
            <style>body{margin:0;background:#162436;color:#f4f7fb;font:20px system-ui}h2,p{margin:16px}button{margin:16px;padding:20px;background:#aade55;border:0;color:#122000}audio{display:none}</style>
            <h2>Sealed media adapter</h2><p>Native WebView2 • SDK runtime • trusted input</p><button id="play">Play sealed audio</button><audio id="audio" src="tone.wav" loop></audio><script src="runtime.js"></script>
            <script>
            const audio=document.getElementById('audio'),button=document.getElementById('play');let key='',state='ready';
            const error=code=>WidgetRailEmbeddedMediaAdapter.error(code);
            WidgetRailEmbeddedMediaAdapter.create({focus:'play',bounds:()=>{let r=button.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height}},
              snapshot:()=>({mediaKey:key,playbackState:state,positionSeconds:audio.currentTime,durationSeconds:Number.isFinite(audio.duration)?audio.duration:1,volume:audio.volume,playbackRate:audio.playbackRate,muted:audio.muted,loop:audio.loop}),
              driver:{load:async o=>{key=o.mediaKey;audio.pause();audio.currentTime=0;state='ready';
                let r=await fetch('tone.wav',{headers:{Range:'bytes=0-3'}});if(r.status!==206||(await r.arrayBuffer()).byteLength!==4)throw error('range-failed');
                let missing=await fetch('missing.txt');if(missing.status!==404)throw error('missing-not-blocked');},
                activate:()=>({completion:new Promise((resolve,reject)=>button.addEventListener('click',async event=>{try{
                  if(!event.isTrusted||!navigator.userActivation.isActive)throw error('gesture-not-trusted');await audio.play();state='playing';button.textContent='Playing with trusted activation';resolve();}catch(e){reject(e)}},{once:true}))}),
                pause:()=>{audio.pause();state='paused';button.textContent='Paused'},setVolume:o=>{audio.volume=o.volume},
                toggle:()=>{audio.pause();state='paused'},back:()=>({type:'back'})}});
            </script>
            """));
    }
    private static byte[] Wav()
    {
        using var data = new MemoryStream(); using var writer = new BinaryWriter(data, Encoding.ASCII, leaveOpen: true);
        const int size = 8000;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + size); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(8000); writer.Write((short)1); writer.Write((short)8);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(size); writer.Write(Enumerable.Repeat((byte)128, size).ToArray());
        return data.ToArray();
    }
    private async Task Until(Func<bool> predicate)
    {
        for (var i = 0; i < 750; ++i)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            if (surface?.FailureCode is { } error) throw new InvalidOperationException(error);
            if (serverFailure is not null) throw serverFailure;
            if (predicate()) return;
            await Task.Delay(20, lifetime.Token);
        }
        throw new TimeoutException("Embedded media condition did not settle");
    }
    private void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
    private static void Write<T>(T value)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "embedded-media-result.json"), JsonSerializer.Serialize(value));
    }
    public async ValueTask DisposeAsync()
    {
        if (retired) return;
        retired = true; dialog?.Hide(); surface?.Dispose();
        if (session is not null) await session.DisposeAsync();
        lifetime.Cancel();
        if (run is not null) await run;
        try { if (serving is not null) await serving; } catch (OperationCanceledException) { }
        await pipe.DisposeAsync(); lifetime.Dispose();
    }
}
