using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using System.Text.Json;

internal static class WebCaptureServiceTests
{
    internal static async Task Run()
    {
        ProtocolChoices();
        var session = new WebSessionReference(new string('a', 32));
        var ticket = new WebCaptureStreamTicket(session, new string('b', 32));
        var expected = new WebCaptureStreamStatus(WebCaptureStreamPhase.Prepared)
        { Ticket = ticket, Quality = new(480, 15), ApplicationName = "Synthetic application", ApplicationAudio = true };
        WebCaptureStreamRequest? prepared = null;
        WebCaptureStreamTicket? stopped = null;
        var response = expected;
        var acknowledged = true;
        var constrained = false;
        var widget = WidgetTestHost.Attach(new Probe(), new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetWebCaptureCapabilities.Prepare, (request, _) =>
            { constrained = false; prepared = request; return ValueTask.FromResult(response); })
            .WithHandler(WidgetWebCaptureCapabilities.PrepareConstrained, (request, _) =>
            { constrained = true; prepared = request; return ValueTask.FromResult(response); })
            .WithHandler(WidgetWebCaptureCapabilities.Status, (_, _) => ValueTask.FromResult(response))
            .WithHandler(WidgetWebCaptureCapabilities.Stop, (request, _) =>
            { stopped = request; return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(acknowledged)); }).Build());
        try
        {
            var result = await widget.Capture.PrepareAsync(session, new(720, 30));
            if (constrained || prepared?.AllowedQualities is not null || prepared?.Session != session || prepared.Options != new WebCaptureStreamOptions(720, 30) || result != expected)
                throw new Exception("Source/audio/quality result did not retain the host choice independently of preferred settings.");
            WebCaptureStreamOptions[] choices = [new(720,30),new(1080,60)];
            response = expected with { Quality = choices[1] };
            var restricted = await widget.Capture.PrepareAsync(session,choices[0],choices,default);
            choices[1] = new(1440,60);
            if (!constrained || prepared?.AllowedQualities?.Count != 2 || prepared.AllowedQualities[1] != new WebCaptureStreamOptions(1080,60) || restricted.Quality != new WebCaptureStreamOptions(1080,60))
                throw new Exception("Constrained preparation did not use its distinct operation and immutable choices.");
            response=expected with{Quality=new(1080,60){ContentPreference=WebCaptureContentPreference.Motion}};
            var motion=await widget.Capture.PrepareAsync(session,new(720,30){ContentPreference=WebCaptureContentPreference.Motion},[new(720,30),new(1080,60)],default);
            if(prepared?.Options.ContentPreference!=WebCaptureContentPreference.Motion||motion.Quality?.ContentPreference!=WebCaptureContentPreference.Motion)
                throw new Exception("Content preference must survive preparation and the host's actual selected result independently of provider mode restrictions.");
            response = expected with { Quality = new(1080,60) };
            await widget.Capture.PrepareAsync(session,new(720,30),[new(720,30),new(1080,60)],allowContentPreferenceSelection:false);
            if(prepared?.FixedContentPreference!=true)throw new Exception("Fixed content policy was not sent to the host.");
            response = expected with {Quality=new(1080,60){ContentPreference=WebCaptureContentPreference.Motion}};
            await Malformed(async()=>{await widget.Capture.PrepareAsync(session,new(720,30),[new(720,30),new(1080,60)],allowContentPreferenceSelection:false);});
            response = expected with { Quality = new(1440,60) };
            await Malformed(async () => { await widget.Capture.PrepareAsync(session,new(720,30),[new(720,30),new(1080,60)],default); });
            await Malformed(async () => { await widget.Capture.PrepareAsync(session); });
            response = expected;
            await widget.Capture.StopAsync(ticket);
            if (stopped != ticket) throw new Exception("Stop lost its exact capture ticket.");
            response = expected with { Ticket = new(new(new string('c', 32)), ticket.Id) };
            await Malformed(async () => { await widget.Capture.PrepareAsync(session); });
            response = expected with { Ticket = new(session, new string('d', 32)) };
            await Malformed(async () => { await widget.Capture.GetStatusAsync(ticket); });
            foreach(var phase in new[]{WebCaptureStreamPhase.Stopped,WebCaptureStreamPhase.Unavailable,WebCaptureStreamPhase.Cancelled,WebCaptureStreamPhase.Failed})
            {
                response=new(phase);
                await Malformed(async () => { await widget.Capture.GetStatusAsync(ticket); });
            }
            response = expected with { Quality = new(4320, 240) };
            await Malformed(async () => { await widget.Capture.PrepareAsync(session); });
            acknowledged = false;
            await Malformed(async () => { await widget.Capture.StopAsync(ticket); });
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    private static void ProtocolChoices()
    {
        var session = new WebSessionReference(new string('a',32));
        var all = (from height in new[]{480,720,1080,1440} from fps in new[]{15,30,60} select new WebCaptureStreamOptions(height,fps)).ToArray();
        if (all.Length != 12 || all.Any(quality => !quality.IsWellFormed()))
            throw new Exception("Supported capture quality matrix is incomplete.");
        foreach (var invalid in new[]{new WebCaptureStreamOptions(2160,30),new(720,24),new(0,30),new(720,0)})
            if (invalid.IsWellFormed()) throw new Exception("Unsupported capture quality accepted.");
        var legacy = new WebCaptureStreamRequest(session,new());
        if (!legacy.IsWellFormed() || (legacy with { Options=new(1080,60) }).IsWellFormed() || JsonSerializer.Serialize(legacy).Contains("AllowedQualities"))
            throw new Exception("Legacy preparation must retain its original four choices and wire shape.");
        if(JsonSerializer.Serialize(legacy).Contains("ContentPreference")||
            JsonSerializer.Deserialize<WebCaptureStreamOptions>("{\"Height\":720,\"FrameRate\":30}")!.ContentPreference!=WebCaptureContentPreference.Detail||
            !(legacy with{Options=new(){ContentPreference=WebCaptureContentPreference.Motion}}).IsWellFormed()||
            (legacy with{Options=new(){ContentPreference=(WebCaptureContentPreference)99}}).IsWellFormed())
            throw new Exception("Content preference changed the legacy wire/default or admitted an unknown value.");
        var duplicatePreference=legacy with{AllowedQualities=[new(),new(){ContentPreference=WebCaptureContentPreference.Motion}]};
        if(duplicatePreference.IsWellFormed())throw new Exception("Content preferences cannot duplicate a provider resolution/FPS mode.");
        var request = legacy with { AllowedQualities=all };
        if(JsonSerializer.Serialize(legacy).Contains("FixedContentPreference") ||
            (legacy with{FixedContentPreference=true}).IsWellFormed() ||
            !(request with{FixedContentPreference=true}).IsWellFormed())
            throw new Exception("Fixed content policy changed legacy wire shape or bypassed constrained preparation.");
        var command = new WebSessionCommand(WebSessionOperation.PrepareWebCaptureConstrained){Session=session,CaptureOptions=new(),CaptureQualities=all};
        all[0]=new(2160,240);
        if (!request.IsWellFormed() || request.AllowedQualities![0]!=new WebCaptureStreamOptions(480,15) || command.CaptureQualities![0]!=new WebCaptureStreamOptions(480,15))
            throw new Exception("Capture quality collections retain caller-owned mutable storage.");
        if (request.AllowedQualities is IList<WebCaptureStreamOptions> mutable && !mutable.IsReadOnly)
            throw new Exception("Published capture choices remain mutable.");
        var restored=JsonSerializer.Deserialize<WebCaptureStreamRequest>(JsonSerializer.Serialize(request))!;
        if (!restored.IsWellFormed() || !restored.AllowedQualities!.SequenceEqual(request.AllowedQualities))
            throw new Exception("Capture restrictions were not preserved by JSON.");
        IReadOnlyList<WebCaptureStreamOptions>[] invalidLists = [[],[new(720,30),new(720,30)],[new(1080,60)],[null!],Enumerable.Repeat(new WebCaptureStreamOptions(),13).ToArray(),[new(720,30),new(2160,30)]];
        foreach(var choices in invalidLists)
            if((legacy with{AllowedQualities=choices}).IsWellFormed())throw new Exception("Invalid capture restriction list accepted.");
    }

    private static async Task Malformed(Func<Task> action)
    {
        try { await action(); }
        catch (WidgetCapabilityException error) when (error.ErrorCode == "malformed_response") { return; }
        throw new Exception("SDK accepted a mismatched or unconfirmed capture response.");
    }
    private sealed class Probe : Widget
    {
        internal WidgetWebCaptureService Capture => HostServices.Capture.WebStreams;
        public override WidgetView Render() => new(UI.Text("Capture fixture", "test"));
    }
}
