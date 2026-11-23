using System.Collections.Concurrent;
using WidgetRail.Samples.DiscordPrototype;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class SharingTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        await BandwidthTests(check);
        await EncoderPreferenceTests(check);
        await QualityTests(check);
        await ShareDetailsTests(check);
        await using(var harness=await Harness.CreateAsync())
        {
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            harness.Provider.State=harness.Provider.State with{Sharing=true,ShareQualityWarning="share_quality_unconfirmed"};
            harness.Capture.SetPhase(WebCaptureStreamPhase.Capturing);await harness.Refresh();
            check(Nodes(harness.Snapshot.Root).Single(node=>node.Id=="discord.share.quality-warning").Text!.Contains("could not confirm the selected stream quality")&&
                !harness.StopDisabled&&harness.Capture.Stops.IsEmpty,"A late quality warning stays visible without tearing down an active capture and Stop remains available");
            harness.Provider.State=harness.Provider.State with{ShareQualityWarning="share_content_preference_unconfirmed"};
            await harness.Refresh();
            check(Nodes(harness.Snapshot.Root).Single(node=>node.Id=="discord.share.quality-warning").Text!.Contains("Smoothness/Clarity")&&
                !harness.StopDisabled&&harness.Capture.Stops.IsEmpty,"An unconfirmed content preference explains the limitation without stopping sharing");
        }
        foreach(var (failure,message) in new (Exception,string)[]
        {
            (new WidgetCapabilityException("session_timeout","private diagnostic"),"setup timed out"),
            (new TimeoutException("private diagnostic"),"setup timed out"),
            (new WidgetCapabilityException("request_canceled","private diagnostic"),"setup was cancelled"),
            (new WidgetCapabilityException("lifecycle_denied","private diagnostic"),"setup was cancelled"),
            (new WidgetCapabilityException("permission_denied","private diagnostic"),"Check capture access")
        })
        {
            await using var harness=await Harness.CreateAsync();harness.Capture.PrepareFailure=failure;
            await harness.Act("share");await Wait(()=>harness.Capture.Prepares.Count==1&&!harness.Preparing);
            var notice=Nodes(harness.Snapshot.Root).Single(node=>node.Id=="discord.notice").Text!;
            check(notice.Contains(message)&&!notice.Contains("private diagnostic")&&harness.CanShare&&harness.ShareCalls==0,
                "Capture setup "+failure.GetType().Name+" gives bounded recovery without broadcasting");
            check(harness.Capture.Prepares.Count==1,"Failed setup is not retried automatically");
            harness.Capture.PrepareFailure=null;await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            check(harness.Capture.Prepares.Count==2,"An explicit retry can open a fresh capture request after setup failure");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            harness.Capture.PrepareDelivery=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await harness.Act("share");await Wait(()=>harness.Capture.Prepares.Count==1);
            var cancel=DeclaredActions.Resolve(harness.Snapshot,"stopShare");
            harness.Capture.PrepareDelivery.SetResult();await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            check(DeclaredActions.Resolve(harness.Snapshot,"stopShare")==cancel,
                "Cancel keeps the SDK-admitted attempt identity when chooser delivery becomes a native capture ticket");
            await harness.Widget.OnActionAsync(new(cancel,"discord.share.start"));
            await Wait(()=>harness.Capture.AllStopped);
            check(true,"A Cancel admitted before chooser delivery still stops that same attempt afterward");
        }
        foreach(var (name,change) in new (string,Action<FakeSession>)[]
        {
            ("account",provider=>provider.State=provider.State with{Account="2"}),
            ("call",provider=>provider.State=provider.State with{ChannelId="20"}),
            ("session",provider=>provider.Reference=new(new string('b',32)))
        })
        {
            await using var harness=await Harness.CreateAsync();
            var share=DeclaredActions.Resolve(harness.Snapshot,"share");
            change(harness.Provider);await harness.Refresh();
            await harness.Widget.OnActionAsync(new(share,"discord.share.start"));
            check(harness.Capture.PermissionRequests==0,$"A stale Share cannot prompt for capture after {name} changes");
            harness.Provider.State=harness.Provider.State with{Sharing=true};await harness.Refresh();
            var stop=DeclaredActions.Resolve(harness.Snapshot,"stopShare");
            // Change each owner again, retaining an active provider stream.
            harness.Provider.State=harness.Provider.State with{Account="3",ChannelId="30"};
            harness.Provider.Reference=new(new string('c',32));await harness.Refresh();
            await harness.Widget.OnActionAsync(new(stop,"discord.share.start"));
            check(harness.Provider.Commands.IsEmpty,"A stale Stop sharing cannot stop a newer account/call/session's provider stream");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            var stop=DeclaredActions.Resolve(harness.Snapshot,"stopShare");
            harness.Provider.State=harness.Provider.State with{SharePending=true};await harness.Refresh();
            check(DeclaredActions.Resolve(harness.Snapshot,"stopShare")==stop,"Provider pending state preserves Stop intent for the same native ticket");
            harness.Provider.State=harness.Provider.State with{SharePending=false};await harness.Refresh();
            await harness.Act("stopShare");await Wait(()=>harness.CanShare);
            await harness.Act("share");await Wait(()=>harness.ShareCalls==2&&!harness.Preparing);
            var stops=harness.Capture.Stops.Count;
            await harness.Widget.OnActionAsync(new(stop,"discord.share.start"));
            check(harness.Capture.Stops.Count==stops&&!harness.CanShare,"A delayed Stop sharing cannot stop a newer native capture ticket in the same call");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            harness.Capture.Permission=WidgetPermissionOutcome.Denied;
            await harness.Act("share");await Wait(()=>harness.Capture.PermissionRequests==1&&!harness.Preparing);
            check(harness.Capture.Prepares.IsEmpty&&harness.Provider.Commands.IsEmpty,"Capture consent denial cannot open a chooser or start broadcasting");
        }
        foreach(var phase in new[]{WebCaptureStreamPhase.Cancelled,WebCaptureStreamPhase.Failed})
        {
            await using var harness=await Harness.CreateAsync();
            harness.Capture.PreparePhase=phase;harness.Capture.ReturnTicket=phase==WebCaptureStreamPhase.Failed;
            await harness.Act("share");await Wait(()=>harness.Capture.Prepares.Count==1&&!harness.Preparing);
            check(harness.ShareCalls==0&&(phase!=WebCaptureStreamPhase.Failed||harness.Capture.Stops.Count==1),
                phase+" preparation does not broadcast and releases any returned native ticket");
        }
        foreach(var (reason,message) in new[]{("worker_disconnected","capture connection ended unexpectedly"),
            ("source_closed","window is no longer available"),("browser_process_failed","browser connection was interrupted"),
            ("receiver_heartbeat_invalid","browser connection was interrupted"),
            ("document_retired","page changed or closed"),("receiver_error","Video delivery to Discord stopped"),
            ("private_provider_secret","capture ended unexpectedly")})
        {
            await using var harness=await Harness.CreateAsync();
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            harness.Provider.State=harness.Provider.State with{Sharing=true};
            harness.Capture.SetPhase(WebCaptureStreamPhase.Capturing);await harness.Refresh();
            harness.Capture.ReportedReason=reason;harness.Capture.SetPhase(WebCaptureStreamPhase.Stopped);await harness.Refresh();
            check(harness.Status.Contains(message,StringComparison.OrdinalIgnoreCase)&&harness.Status.Contains("Discord still reports a stream")&&
                harness.Status.Contains("Stop sharing")&&!harness.Status.Contains(reason,StringComparison.Ordinal)&&!harness.StopDisabled&&
                harness.Provider.State.Sharing&&!harness.Provider.Commands.Any(command=>command.Method=="stopShare"),
                reason+": terminal capture has bounded specific recovery without claiming the Discord broadcast stopped");
            var stops=harness.Capture.Stops.Count;await harness.Refresh();
            check(harness.Capture.Stops.Count==stops&&harness.Status.Contains(message,StringComparison.OrdinalIgnoreCase),
                reason+": refresh preserves interruption recovery without reissuing cleanup or provider mutations");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            harness.Provider.State=harness.Provider.State with{Sharing=true};
            harness.Capture.ReportedReason="worker_disconnected";harness.Capture.SetPhase(WebCaptureStreamPhase.Stopped);await harness.Refresh();
            harness.Provider.State=harness.Provider.State with{Account="2",ChannelId="40"};await harness.Refresh();
            check(!harness.Status.Contains("connection ended")&&!harness.Provider.Commands.Any(command=>command.Method=="stopShare"),
                "Account/call changes retire only their own capture-interruption presentation and preserve the new provider stream");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            harness.Capture.ReportedReason="worker_disconnected";harness.Capture.SetPhase(WebCaptureStreamPhase.Stopped);await harness.Refresh();
            check(harness.Status.Contains("Choose Share game"),"Confirmed provider idle offers explicit new source selection after capture loss");
            harness.Capture.ReportedReason=null;
            await harness.Act("share");await Wait(()=>harness.ShareCalls==2&&!harness.Preparing);
            check(!harness.Status.Contains("connection ended")&&harness.Status.Contains("Waiting"),
                "An explicit new sharing attempt clears only the prior interruption and uses a new capture ticket");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            await harness.Act("watch.30");
            var pin=WidgetTestHost.CreatePinnedLayoutHost(harness.Widget,"sharing-pin",100);
            await pin.SelectAsync("discord.stream");
            await Wait(()=>harness.Provider.Receivers.Count==1&&Nodes(harness.Snapshot.PinnedLayouts.Single(layout=>layout.Id=="discord.stream").Root!).Any(node=>node.WebBrowser is not null));
            var incoming=harness.Provider.Receivers.Single();
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            var command=harness.Provider.Commands.Single(item=>item.Method=="share");
            check(harness.Capture.PermissionIds.SequenceEqual([WidgetCaptureCapabilities.Permission])&&harness.Capture.Prepares.Single().Options==(new WebCaptureStreamOptions(){ContentPreference=WebCaptureContentPreference.Motion}),
                "Outgoing setup requests only capture access and starts with720p30; no window-list permission is requested");
            check(command.Args.GetProperty("captureQuality").GetProperty("resolution").GetInt32()==480&&
                command.Args.GetProperty("captureQuality").GetProperty("frameRate").GetInt32()==15,
                "Provider sharing uses the quality actually selected by the host chooser");
            check(harness.Status.Contains("Waiting"),"Prepared native capture and a requested provider action are not advertised as a confirmed broadcast");
            harness.Provider.State=harness.Provider.State with{Sharing=true};await harness.Refresh();
            check(harness.Status.Contains("Waiting"),"Provider sharing alone does not imply that native capture is running");
            harness.Capture.SetPhase(WebCaptureStreamPhase.Capturing);await harness.Refresh();
            check(harness.Status.Contains("Sharing Synthetic game")&&harness.Status.Contains("480p")&&harness.Status.Contains("15 FPS")&&harness.Status.Contains("application audio"),
                "UI joins native capture status with authoritative Discord sharing state and selected audio/quality");
            await WidgetTestHost.SetLifecycleStateAsync(harness.Widget,WidgetLifecycleState.Background);await harness.Refresh();
            check(harness.Capture.Stops.IsEmpty&&!incoming.Disposed,"Background continuation preserves outgoing capture and the simultaneous incoming receiver");
            await WidgetTestHost.SetLifecycleStateAsync(harness.Widget,WidgetLifecycleState.Interactive);
            harness.Capture.SetPhase(WebCaptureStreamPhase.Stopped);await harness.Refresh();
            check(harness.Capture.Stops.Count>0&&!incoming.Disposed&&!harness.Provider.Commands.Any(command=>command.Method=="stopShare"),
                "Source loss releases local capture without disturbing incoming video or blindly stopping another provider broadcast");
            await harness.Act("stopShare");await Wait(()=>harness.Provider.Commands.Any(command=>command.Method=="stopShare"));
            check(true,"An explicit Stop sharing action can stop the provider's remaining stream");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            harness.Capture.PreparePhase=WebCaptureStreamPhase.Capturing;
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            check(harness.Capture.Stops.IsEmpty&&harness.Status.Contains("Waiting"),
                "Capture already running when Prepare arrives is retained without claiming remote broadcast confirmation");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            harness.Capture.PrepareDelivery=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await harness.Act("share");await Wait(()=>harness.Capture.Prepares.Count==1);
            // The host has confirmed the selection, but its prepared reply is
            // still in transit. Hide must not revoke that admitted user choice.
            await WidgetTestHost.SetLifecycleStateAsync(harness.Widget,WidgetLifecycleState.Background);
            harness.Capture.PrepareDelivery.SetResult();
            await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            check(harness.Capture.Stops.IsEmpty,"Confirmed source selection can finish provider startup after overlay hide");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            harness.Capture.PrepareDelivery=new(TaskCreationOptions.RunContinuationsAsynchronously);harness.Capture.IgnorePrepareCancellation=true;
            await harness.Act("share");await Wait(()=>harness.Capture.Prepares.Count==1);
            harness.Provider.State=harness.Provider.State with{Account="2",ChannelId="20"};await harness.Refresh();
            harness.Capture.PrepareDelivery.SetResult();await Wait(()=>harness.Capture.Stops.Count>0&&!harness.Preparing);
            check(harness.ShareCalls==0,"Account/call change during chooser delivery releases a late prepared ticket without sharing");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            harness.Provider.FailCommands=true;
            await harness.Act("share");await Wait(()=>harness.Capture.Stops.Count>0&&!harness.Preparing);
            await harness.Refresh();
            check(harness.ShareCalls==1,"Uncertain provider startup stops native capture and never retries the broadcast command");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            harness.Provider.State=harness.Provider.State with{Sharing=true};harness.Capture.SetPhase(WebCaptureStreamPhase.Capturing);await harness.Refresh();
            harness.Provider.State=harness.Provider.State with{Account="2",ChannelId="40"};await harness.Refresh();
            check(harness.Capture.AllStopped&&!harness.Provider.Commands.Any(command=>command.Method=="stopShare"),
                "An account/call change after broadcast stops only the owned native source, not a newer account's stream");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            harness.Capture.ReportedQuality=new(720,30);await harness.Refresh();
            check(harness.Capture.AllStopped,"Native status cannot silently substitute a different selected quality");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            harness.Provider.ShareGate=new(TaskCreationOptions.RunContinuationsAsynchronously);harness.Provider.IgnoreShareCancellation=true;
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1);
            await harness.Act("stopShare");await Wait(()=>harness.Capture.Stops.Count>0);
            await Wait(()=>harness.Capture.AllStopped);
            harness.Capture.StopFailures=1;
            harness.Provider.ShareGate.SetResult();await Wait(()=>!harness.Preparing);
            check(harness.Capture.AllStopped&&harness.ShareCalls==1,"Cancellation fences a late provider acknowledgement and leaves its native ticket stopped");
            check(harness.CanShare&&!harness.Status.Contains("Could not confirm"),
                "A failed redundant cleanup after late acknowledgement cannot resurrect a capture already stopped explicitly");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            harness.Provider.State=harness.Provider.State with{Sharing=true};harness.Capture.SetPhase(WebCaptureStreamPhase.Capturing);await harness.Refresh();
            harness.Capture.StopFailures=1;
            await harness.Act("stopShare");await Wait(()=>harness.Status.Contains("Could not confirm"));
            check(!harness.Capture.AllStopped&&!harness.StopDisabled,"Unconfirmed native stop retains the ticket and a reachable Stop sharing action");
            await harness.Act("stopShare");await Wait(()=>harness.Capture.AllStopped);
            check(true,"Explicit stop retry resolves retained native capture ownership");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            harness.Provider.State=harness.Provider.State with{Sharing=true};
            harness.Capture.SetPhase(WebCaptureStreamPhase.Capturing);await harness.Refresh();
            harness.Capture.StopFailures=1;
            harness.Provider.State=harness.Provider.State with
                {ChannelId=null,ChannelName="",Sharing=false,SharePending=false,Participants=[],Streams=[]};
            await harness.Refresh();
            var idle=Nodes(harness.Snapshot.Root).ToArray();
            var cleanup=idle.SingleOrDefault(node=>node.Id=="discord.share.start");
            check(!harness.Capture.AllStopped&&idle.Any(node=>node.Id=="discord.channels.server")&&
                cleanup is {IsDisabled:not true}&&cleanup.ActionId?.StartsWith("stopShare.",StringComparison.Ordinal)==true&&
                harness.Status.Contains("Could not confirm",StringComparison.Ordinal),
                "Call loss with an unconfirmed local stop keeps the owned ticket and an actionable cleanup control beside the idle server picker");
            check(!idle.SelectMany(node=>node.Shortcuts).Any(shortcut=>shortcut.Button is ControllerButton.X or ControllerButton.Y),
                "Idle capture recovery does not repurpose call mute or start-sharing shortcuts");
            await harness.Act("stopShare");await Wait(()=>harness.Capture.AllStopped);
            check(!harness.Provider.Commands.Any(command=>command.Method=="stopShare"),
                "Explicit idle cleanup retries the retained native ticket without sending a provider stop into a missing call");
        }
        await using(var harness=await Harness.CreateAsync())
        {
            await harness.Act("share");await Wait(()=>harness.ShareCalls==1&&!harness.Preparing);
            harness.Capture.SetPhase(WebCaptureStreamPhase.Capturing);harness.Clock.Advance(TimeSpan.FromSeconds(20));await harness.Refresh();
            check(harness.Capture.AllStopped&&harness.ShareCalls==1,"Provider confirmation timeout stops local capture without replaying sharing");
        }
        var shutdown=await Harness.CreateAsync();
        await shutdown.Act("share");await Wait(()=>shutdown.ShareCalls==1&&!shutdown.Preparing);
        await shutdown.DisposeAsync();
        check(shutdown.Capture.AllStopped&&shutdown.Provider.Disposed,"Widget teardown stops its outgoing native ticket before releasing the backend session");
    }

    private static async Task QualityTests(Action<bool,string> check)
    {
        var baseline=DiscordShareQuality.Baseline;
        var all=(from height in new[]{480,720,1080,1440} from fps in new[]{15,30,60} select new DiscordShareQuality(height,fps)).ToArray();
        var state=new FakeSession().State;state.Validate();
        check(state.ShareQualities.SequenceEqual(baseline),"Legacy provider state retains the four baseline capture choices");
        (state with{ShareQualities=all,ShareQualityAvailability="ready",PreferredShareQuality=new(1440,60)}).Validate();
        foreach(var invalid in new[]{
            state with{ShareQualities=[new(2160,30)]},state with{ShareQualities=[new(720,120)]},
            state with{ShareQualities=[new(720,30),new(720,30)]},state with{ShareQualities=all},
            state with{ShareQualities=null!},state with{ShareQualityAvailability="unknown"},
            state with{PreferredShareQuality=new(2160,60)}})
        {
            var rejected=false;try{invalid.Validate();}catch(InvalidDataException){rejected=true;}
            check(rejected,"Malformed or unverified higher-quality provider state fails closed");
        }
        // Resolution/FPS come from the provider's persisted preference; content
        // type is a separate account-scoped WidgetRail preference.
        for(var lifetime=0;lifetime<2;lifetime++)
        {
            await using var h=await Harness.CreateAsync(all,new(1440,60));
            h.Capture.SelectedQuality=h.Capture.ReportedQuality=new(1440,60);
            await h.Act("share");await Wait(()=>h.ShareCalls==1&&!h.Preparing);
            var request=h.Capture.Prepares.Single();
            check(request.Options==(new WebCaptureStreamOptions(1440,60){ContentPreference=WebCaptureContentPreference.Motion})&&
                request.AllowedQualities!.SequenceEqual(all.Select(q=>new WebCaptureStreamOptions(q.Resolution,q.FrameRate))),
                "Native chooser receives provider-approved choices and persisted preference on widget lifetime "+lifetime);
            var command=h.Provider.Commands.Single(c=>c.Method=="share");
            check(command.Args.GetProperty("captureQuality").GetProperty("resolution").GetInt32()==1440&&
                command.Args.GetProperty("captureQuality").GetProperty("frameRate").GetInt32()==60,
                "Provider receives the actual host-approved high-resolution60FPS choice");
        }
        var localPreferences=new WidgetTestPrivateState();
        for(var lifetime=0;lifetime<2;lifetime++)
        {
            await using var h=await Harness.CreateAsync(all,new(1080,60),localPreferences);
            h.Capture.SelectedQuality=new(1080,60){ContentPreference=WebCaptureContentPreference.Detail};
            await h.Act("share");await Wait(()=>h.ShareCalls==1&&!h.Preparing);
            await h.Widget.WaitForPreferenceWriteAsync();
            check(h.Capture.Prepares.Single().Options.ContentPreference==(lifetime==0?WebCaptureContentPreference.Motion:WebCaptureContentPreference.Detail),
                "New Discord users prefer smoothness and confirmed clarity persists across widget lifetimes: "+lifetime);
        }
        await using(var h=await Harness.CreateAsync(baseline,new(1440,60)))
        {
            await h.Act("share");await Wait(()=>h.ShareCalls==1&&!h.Preparing);
            check(h.Capture.Prepares.Single().Options==(new WebCaptureStreamOptions(){ContentPreference=WebCaptureContentPreference.Motion})&&
                h.Provider.State.PreferredShareQuality==new DiscordShareQuality(1440,60),
                "Currently ineligible saved quality falls back to720p30 without erasing the provider preference");
        }
        await using(var h=await Harness.CreateAsync(all,new(720,30)))
        {
            h.Capture.SelectedQuality=new(1080,60);h.Capture.PreparePhase=WebCaptureStreamPhase.Cancelled;h.Capture.ReturnTicket=false;
            await h.Act("share");await Wait(()=>h.Capture.Prepares.Count==1&&!h.Preparing);
            check(h.ShareCalls==0&&h.Provider.State.PreferredShareQuality==new DiscordShareQuality(720,30),
                "Canceled source chooser neither broadcasts nor saves its tentative resolution");
        }
        await using(var h=await Harness.CreateAsync(all,new(1440,60)))
        {
            h.Capture.SelectedQuality=new(1440,60);h.Capture.PrepareDelivery=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await h.Act("share");await Wait(()=>h.Capture.Prepares.Count==1);
            h.Provider.State=h.Provider.State with{ShareQualities=baseline};await h.Refresh();
            h.Capture.PrepareDelivery.SetResult();await Wait(()=>!h.Preparing&&h.Capture.Stops.Count==1);
            check(h.ShareCalls==0,"Eligibility lost during source selection retires the native ticket before provider broadcast");
        }
        await using(var h=await Harness.CreateAsync([],null))
        {
            await h.Act("share");
            check(h.Capture.Prepares.IsEmpty&&h.Capture.PermissionRequests==0&&h.ShareCalls==0,
                "No provider-approved quality means no capture prompt or broadcast");
        }
    }

    private static async Task ShareDetailsTests(Action<bool,string> check)
    {
        var measured=System.Text.Json.JsonSerializer.SerializeToElement(new{
            source=new{width=2560,height=1440},outbound=new[]{new{width=2560,height=1440,framesPerSecond=24,codec="H264",
                qualityLimitationReason="cpu",encoderImplementation="OpenH264",measurement=new{milliseconds=1000,encodedFps=24,sentFps=23.5,
                    bitrateMbps=8.25,meanEncodeMs=32.5,meanQp=35},remote=new{roundTripMs=32,fractionLost=.02}}},advertised=new[]{new{width=2560,height=1440,frameRate=60}}});
        var measuredText=DiscordWebViewWidget.FormatShareDetails(measured);
        check(measuredText.Contains("24 encoded FPS · 23.5 sent FPS · 8.25 Mb/s actual · 32.5 ms/frame encoding · mean QP 35")&&
            measuredText.Contains("H264 · CPU limited")&&measuredText.Contains("Encoder implementation: OpenH264")&&
            measuredText.Contains("32 ms round trip · 2% reported packet loss")&&measuredText.Contains("Advertised: 2560 × 1440 · 60 FPS"),
            "Stream details separate measured throughput and transport loss from advertised quality");
        static string DetailsAction(Harness h)=>h.Snapshot.Root.ContextActions.Single(action=>action.Label=="Stream details").ActionId;
        static string Notice(Harness h)=>Nodes(h.Snapshot.Root).FirstOrDefault(node=>node.Id=="discord.notice")?.Text??"";
        static async Task Start(Harness h)
        {
            await h.Act("share");await Wait(()=>h.ShareCalls==1&&!h.Preparing);
            h.Provider.State=h.Provider.State with{Sharing=true};h.Capture.SetPhase(WebCaptureStreamPhase.Capturing);await h.Refresh();
        }
        await using(var h=await Harness.CreateAsync())
        {
            check(!h.Snapshot.Root.ContextActions.Any(action=>action.Label=="Stream details"),"Stream details is absent without an owned outgoing capture");
            await Start(h);
            var before=h.Provider.Commands.Count;
            await h.Act(DetailsAction(h));await h.Widget.WaitForShareDetailsAsync().WaitAsync(TimeSpan.FromSeconds(8));
            check(Notice(h)=="Capture: 2560 × 1440\nSent: 1920 × 1080 · 59.5 FPS\nAdvertised: 1280 × 720 · 60 FPS",
                "Stream details separates capture, actual encoded dimensions and advertised quality");
            check(h.Provider.Commands.Count==before+2&&h.Provider.Commands.Skip(before).All(command=>command.Method=="streamDiagnostics")&&h.Capture.Stops.IsEmpty,
                "Reading details takes two bounded samples with no sharing mutation or capture restart");
            h.Provider.Diagnostics=System.Text.Json.JsonSerializer.SerializeToElement(new{
                source=new{width=2560,height=1440},outbound=new[]{new{width=1280,height=720,framesPerSecond=59.8,codec="VP8",qualityLimitationReason="bandwidth"}},
                advertised=new[]{new{width=2560,height=1440,frameRate=60}},sender=new{contentHint="detail",degradationPreference="maintain-framerate",
                    encodings=new[]{new{maxBitrate=8000000,scaleResolutionDownBy=2}},transactionId="private"}});
            await h.Act(DetailsAction(h));await h.Widget.WaitForShareDetailsAsync().WaitAsync(TimeSpan.FromSeconds(8));
            check(Notice(h).Contains("Encoder: VP8 · bandwidth limited")&&Notice(h).Contains("Policy: Clarity · preserve frame rate · 8 Mb/s max · 2× scale")&&
                !Notice(h).Contains("private"),"On-demand stream details expose bounded effective encoder policy without private metadata");
            h.Provider.Diagnostics=System.Text.Json.JsonSerializer.SerializeToElement(new{
                source=new{width=2560,height=1440},outbound=new[]{new{width=1280,height=720,codec="private",qualityLimitationReason="private"}},
                advertised=Array.Empty<object>(),sender=new{contentHint="private",degradationPreference="private"}});
            await h.Act(DetailsAction(h));await h.Widget.WaitForShareDetailsAsync().WaitAsync(TimeSpan.FromSeconds(8));
            check(!Notice(h).Contains("private")&&!Notice(h).Contains("Policy:")&&!Notice(h).Contains("Encoder:"),
                "Unknown encoder and policy labels are not displayed as facts");
            h.Provider.Diagnostics=System.Text.Json.JsonSerializer.SerializeToElement(new{source=new{},outbound=Array.Empty<object>(),advertised=Array.Empty<object>(),
                attachment=new{nativeFrames=432,videoSupported=false,inputTrack=(string?)null,desktopTrack="live",senderTrack=(string?)null,peerState="connected",qualityBinding=(object?)null,qualityCheck="rtc-connection"}});
            await h.Act(DetailsAction(h));await h.Widget.WaitForShareDetailsAsync().WaitAsync(TimeSpan.FromSeconds(8));
            check(Notice(h).Contains("432 captured frames · video disabled · input unavailable · desktop live · sender unavailable · peer connected · quality hook unavailable"),
                "Blank-stream diagnostics distinguish capture progress from provider attachment without inventing a sent frame");
            check(Notice(h).Contains("Quality check: rtc-connection"),"The failed quality checkpoint remains available after retiring the hook");
            h.Provider.Diagnostics=System.Text.Json.JsonSerializer.SerializeToElement(new{source=new{width=(int?)null,height=(int?)null},outbound=Array.Empty<object>(),advertised=Array.Empty<object>()});
            await h.Act(DetailsAction(h));await h.Widget.WaitForShareDetailsAsync().WaitAsync(TimeSpan.FromSeconds(8));
            check(Notice(h)=="Capture: not available\nSent: not available\nAdvertised: not available","Missing measurements are not replaced with the selected quality");
            h.Provider.Diagnostics=System.Text.Json.JsonSerializer.SerializeToElement(new{source=new{width=-1,height=1440},outbound=Array.Empty<object>(),advertised=Array.Empty<object>()});
            await h.Act(DetailsAction(h));await h.Widget.WaitForShareDetailsAsync().WaitAsync(TimeSpan.FromSeconds(8));
            check(Notice(h).Contains("unavailable in this Discord version"),"Malformed diagnostics produce bounded recovery instead of misleading dimensions");
        }
        foreach(var change in new[]{"account","call","session","replacement","background"})
        {
            await using var h=await Harness.CreateAsync();await Start(h);
            var action=DetailsAction(h);h.Provider.DiagnosticsGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await h.Act(action);await Wait(()=>h.Provider.Commands.Any(command=>command.Method=="streamDiagnostics"));
            Task? transition=null;
            switch(change)
            {
                case "account":h.Provider.State=h.Provider.State with{Account="2"};await h.Refresh();break;
                case "call":h.Provider.State=h.Provider.State with{ChannelId="20"};await h.Refresh();break;
                case "session":h.Provider.Reference=new(new string('b',32));await h.Refresh();break;
                case "replacement":
                    await h.Act("stopShare");await Wait(()=>h.Capture.Stops.Count>0);
                    h.Provider.State=h.Provider.State with{Sharing=false};await h.Refresh();
                    await h.Act("share");await Wait(()=>h.ShareCalls==2&&!h.Preparing);break;
                case "background":transition=WidgetTestHost.SetLifecycleStateAsync(h.Widget,WidgetLifecycleState.Background).AsTask();break;
            }
            var expected=Notice(h);h.Provider.DiagnosticsGate.SetResult();
            if(transition is not null)await transition.WaitAsync(TimeSpan.FromSeconds(8));
            await h.Widget.WaitForShareDetailsAsync().WaitAsync(TimeSpan.FromSeconds(8));
            check(Notice(h)==expected,$"A late diagnostics read cannot overwrite presentation after {change}");
            if(change!="background")
            {
                var before=h.Provider.Commands.Count;await h.Act(action);
                await h.Widget.WaitForShareDetailsAsync().WaitAsync(TimeSpan.FromSeconds(8));
                check(h.Provider.Commands.Count==before,$"A stale Stream details action is rejected after {change}");
            }
        }
    }

    private static async Task Wait(Func<bool> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));while(!condition())await Task.Delay(10,timeout.Token);}
    private static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
    private sealed class Harness:IAsyncDisposable
    {
        internal readonly FakeSession Provider=new();
        internal readonly CaptureProbe Capture=new();
        internal readonly Clock Clock=new();
        internal DiscordWebViewWidget Widget=null!;
        internal ViewSnapshot Snapshot=>Widget.Render().CreateSnapshot("sharing.test",1);
        internal string Status=>Nodes(Snapshot.Root).FirstOrDefault(node=>node.Id=="discord.share.status")?.Text??"";
        internal bool Preparing=>Widget.IsPreparingSharing;
        internal int ShareCalls=>Provider.Commands.Count(command=>command.Method=="share");
        internal bool StopDisabled=>Nodes(Snapshot.Root).Single(node=>node.Id=="discord.share.start").IsDisabled==true;
        internal bool CanShare=>Nodes(Snapshot.Root).Single(node=>node.Id=="discord.share.start").ActionId?.StartsWith("share.",StringComparison.Ordinal)==true;
        internal static async Task<Harness> CreateAsync(DiscordShareQuality[]? qualities=null,DiscordShareQuality? preferred=null,WidgetTestPrivateState? preferences=null)
        {
            var harness=new Harness();
            if(qualities is not null)harness.Provider.State=harness.Provider.State with
                {ShareQualities=qualities,ShareQualityAvailability="ready",PreferredShareQuality=preferred};
            harness.Widget=WidgetTestHost.Attach(new DiscordWebViewWidget(harness.Provider,harness.Clock,TimeSpan.FromHours(1)),harness.Capture.Services(preferences));
            await WidgetTestHost.InitializeAsync(harness.Widget);await WidgetTestHost.SetLifecycleStateAsync(harness.Widget,WidgetLifecycleState.Interactive);
            await Wait(()=>Nodes(harness.Snapshot.Root).Any(node=>node.Id=="discord.main.mute"));
            return harness;
        }
        internal async Task Act(string action)
        {
            var snapshot=Snapshot;action=DeclaredActions.Resolve(snapshot,action);var node=Nodes(snapshot.Root).FirstOrDefault(node=>node.ActionId==action);
            await Widget.OnActionAsync(new(action,node?.Id??"test.action",InputScopeId:snapshot.ActiveInputScopeId){FocusedElementId=node?.Id});
        }
        internal async Task Refresh()
        {
            var before=Provider.Reads;await Act("refresh");await Wait(()=>Provider.Reads>before);
            // Read completion also reconciles the host status before publication.
            await Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(8));
        }
        public async ValueTask DisposeAsync()
        {
            Capture.PrepareDelivery?.TrySetResult();Provider.ShareGate?.TrySetResult();Provider.DiagnosticsGate?.TrySetResult();
            await WidgetTestHost.DestroyAsync(Widget);
        }
    }
    private sealed class Clock:TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Volatile.Read(ref ticks);
        public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
        internal void Advance(TimeSpan time)=>Interlocked.Add(ref ticks,time.Ticks);
    }
    private sealed class CaptureProbe
    {
        internal WidgetPermissionOutcome Permission=WidgetPermissionOutcome.Granted;
        internal readonly ConcurrentQueue<WebCaptureStreamRequest> Prepares=new();
        internal readonly ConcurrentQueue<WebCaptureStreamTicket> Stops=new();
        private readonly ConcurrentDictionary<string,WebCaptureStreamPhase> phases=new();
        internal readonly List<string> PermissionIds=[];
        internal int PermissionRequests,StopFailures;
        internal WebCaptureStreamPhase PreparePhase=WebCaptureStreamPhase.Prepared;
        internal WebCaptureStreamOptions ReportedQuality=new(480,15);
        internal WebCaptureStreamOptions SelectedQuality=new(480,15);
        internal string? ReportedReason;
        internal Exception? PrepareFailure;
        internal bool ReturnTicket=true,IgnorePrepareCancellation;
        internal TaskCompletionSource? PrepareDelivery;
        internal bool AllStopped=>phases.Count>0&&phases.Values.All(phase=>phase==WebCaptureStreamPhase.Stopped);
        internal void SetPhase(WebCaptureStreamPhase phase){foreach(var id in phases.Keys)phases[id]=phase;}
        internal WidgetHostServices Services(WidgetTestPrivateState? preferences=null)=>new WidgetTestHostServicesBuilder()
            .WithPrivateState(preferences??new WidgetTestPrivateState())
            .WithResponse(WidgetPinningCapabilities.Open,new WidgetPinReceipt(true))
            .WithHandler(WidgetPermissionCapabilities.Request,(query,token)=>
            {token.ThrowIfCancellationRequested();Interlocked.Increment(ref PermissionRequests);PermissionIds.AddRange(query.CapabilityIds);return ValueTask.FromResult(new WidgetPermissionRequestState(Permission));})
            .WithHandler(WidgetWebCaptureCapabilities.PrepareConstrained,async(request,token)=>
            {
                Prepares.Enqueue(request);if(PrepareFailure is { } failure)throw failure;
                var ticket=new WebCaptureStreamTicket(request.Session,Guid.NewGuid().ToString("N"));
                if(ReturnTicket)phases[ticket.Id]=PreparePhase;
                if(PrepareDelivery is { } delivery){if(IgnorePrepareCancellation)await delivery.Task;else await delivery.Task.WaitAsync(token);}
                return new WebCaptureStreamStatus(PreparePhase){Ticket=ReturnTicket?ticket:null,Quality=SelectedQuality,ApplicationName="Synthetic game",ApplicationAudio=true};
            })
            .WithHandler(WidgetWebCaptureCapabilities.Status,(ticket,token)=>
            {token.ThrowIfCancellationRequested();return ValueTask.FromResult(new WebCaptureStreamStatus(phases[ticket.Id]){Ticket=ticket,Quality=ReportedQuality,ApplicationName="Synthetic game",ApplicationAudio=true,Reason=ReportedReason});})
            .WithHandler(WidgetWebCaptureCapabilities.Stop,(ticket,token)=>
            {token.ThrowIfCancellationRequested();Stops.Enqueue(ticket);if(Interlocked.Decrement(ref StopFailures)>=0)throw new IOException("Synthetic stop failure");phases[ticket.Id]=WebCaptureStreamPhase.Stopped;return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));}).Build();
    }
}
