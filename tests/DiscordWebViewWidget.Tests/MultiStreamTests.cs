using WidgetRail.Samples.DiscordWebView;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class MultiStreamTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var fake=new FakeSession();
        fake.State=fake.State with {Streams=[new("guild:1:10:30","30","First"),new("guild:1:10:31","31","Second"),
            new("guild:1:10:32","32","Third",Audio:new(true,false,40)),new("guild:1:10:33","33","Fourth")]};
        var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(fake,refreshInterval:TimeSpan.FromHours(1)),
            new WidgetTestHostServicesBuilder().WithResponse(WidgetPinningCapabilities.Open,new WidgetPinReceipt(true)).Build());
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
        await widget.WaitForStateReadAsync();
        try
        {
            CheckNoDetachedCards("Unused stream slots never render Shared screen placeholder cards before pinning");
            check(!Snapshot().PinnedLayouts.Any(pin=>pin.Id.StartsWith("discord.stream",StringComparison.Ordinal)),
                "Unused stream slots are absent from host pin choices before any Watch intent");
            var emptySelection=Guid.NewGuid().ToString("N");
            check(!await Select("discord.stream.3",emptySelection,true),
                "The SDK rejects selection of an unpublished empty stream slot");
            await Act("discord.stream.30");
            check(Snapshot().PinnedLayouts.Where(pin=>pin.Id.StartsWith("discord.stream",StringComparison.Ordinal)).Select(pin=>pin.Id).SequenceEqual(new[]{"discord.stream"}),
                "Watch intent publishes only its chosen stream layout before requesting host selection");
            var first=Guid.NewGuid().ToString("N");
            await Select("discord.stream",first,true);
            await Until(()=>Video("discord.stream") is not null);
            var firstVideo=Video("discord.stream");
            CheckNoDetachedCards("One live stream pin does not render a placeholder card for the unused second slot");
            check(!Snapshot().PinnedLayouts.Any(pin=>pin.Id is "discord.stream.2" or "discord.stream.3"),
                "One playing stream does not expose the two unused slots in the tray menu");
            await Act("discord.stream.31");
            var second=Guid.NewGuid().ToString("N");
            await Select("discord.stream.2",second,true);
            await Until(()=>Video("discord.stream.2") is not null);
            var secondVideo=Video("discord.stream.2");
            check(Video("discord.stream")==firstVideo&&firstVideo!=secondVideo&&fake.Receivers.Count==2,
                "Two screen slots have distinct native video sessions and the first survives opening the second");
            CheckNoDetachedCards("Two live streams leave the unused third slot invisible");
            await Act("discord.stream.32");
            var third=Guid.NewGuid().ToString("N");
            await Select("discord.stream.3",third,true);
            await Until(()=>Video("discord.stream.3") is not null);
            var thirdVideo=Video("discord.stream.3");
            check(Video("discord.stream")==firstVideo&&Video("discord.stream.2")==secondVideo&&
                new[]{firstVideo,secondVideo,thirdVideo}.Distinct().Count()==3&&fake.Receivers.Count==3,
                "Three screens retain independent receiver sessions and the first two stable layout identities");
            check(fake.Commands.Where(command=>command.Method=="watch").Select(command=>command.Args.GetProperty("slot").GetInt32()).SequenceEqual(new[]{1,2,3}),
                "The third watch is admitted to provider slot 3 without reassigning either existing stream");
            check(WidgetTestHost.ValidateWinUiPresentation(Snapshot()).Count==0,
                "Three simultaneous stream pins and their main-page controls satisfy the WinUI presentation contract");
            check(Nodes(Snapshot().Root).Any(node=>node.Id=="discord.streams.manage.3")&&
                Nodes(Snapshot().Root).Any(node=>node.Id=="discord.streams.unpin.3")&&
                Nodes(Snapshot().Root).Any(node=>node.Id=="discord.streams.stop.3")&&
                Nodes(Snapshot().Root).Any(node=>node.Id=="discord.streams.audio.3.volume"&&node.Value==40),
                "Third-stream management, stop, unpin and audio controls have distinct identities");
            var thirdStop=Nodes(Snapshot().Root).Single(node=>node.Id=="discord.streams.stop.3").ActionId!;
            await Act("discord.stream.33");
            check(fake.ReceiverRequests==3&&Video("discord.stream")==firstVideo&&Video("discord.stream.2")==secondVideo&&Video("discord.stream.3")==thirdVideo&&
                Nodes(Snapshot().Root).Any(node=>node.Text=="All three screen slots are in use. Unpin a screen to watch another."),
                "A fourth watch reports the three-stream limit without evicting any live receiver");
            await Act("discord.streams.stop.3");
            check(!Snapshot().PinnedLayouts.Any(pin=>pin.Id=="discord.stream.3")&&Video("discord.stream")==firstVideo&&Video("discord.stream.2")==secondVideo,
                "Stopping slot 3 withdraws only its pin and preserves both other streams");
            await Select("discord.stream.3",third,false);
            CheckNoDetachedCards("Stopping the third stream leaves no empty slot card");
            await Act("discord.stream.32");
            var thirdReopened=Guid.NewGuid().ToString("N");
            await Select("discord.stream.3",thirdReopened,true);
            await Until(()=>Video("discord.stream.3") is not null&&Video("discord.stream.3")!=thirdVideo);
            thirdVideo=Video("discord.stream.3");
            await widget.OnActionAsync(new(thirdStop,"discord.streams.stop.3"));
            check(!await Select("discord.stream.3",third,false)&&Video("discord.stream.3")==thirdVideo,
                "Retired third-slot stop and close actions cannot affect its replacement");
            await Select("discord.stream",first,false);
            await Until(()=>fake.Receivers.First().Disposed);
            check(Video("discord.stream.2")==secondVideo&&Video("discord.stream.3")==thirdVideo&&!fake.Receivers.Last().Disposed,
                "Closing the first screen leaves the second and third receivers in their original slots");
            check(!Nodes(Snapshot().Root).Any(node=>node.Id is "discord.streams.unpin" or "discord.streams.stop" or "discord.streams.ended"),
                "Unpinning a live stream removes its commands without leaving a detached footer card");
            CheckNoDetachedCards("Unpinning one of three streams does not expose any internal slot as an empty card");
            check(!Snapshot().PinnedLayouts.Any(pin=>pin.Id=="discord.stream"),
                "A closed stream slot is withdrawn from host pin choices until a new Watch intent");
            await Act("discord.stream.30");
            var reopened=Guid.NewGuid().ToString("N");
            await Select("discord.stream",reopened,true);
            await Until(()=>Video("discord.stream") is not null);
            check(!await Select("discord.stream",first,false)&&Video("discord.stream.2")==secondVideo,
                "A late first-slot revoke cannot close its replacement or the second screen");
            var streams=fake.State.Streams;
            var ending=fake.State with{Streams=streams.Skip(1).ToArray()};
            fake.ReadGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads=fake.Reads;
            await widget.OnActionAsync(new("refresh","test"));
            await Until(()=>fake.Reads>reads);
            await Act("discord.stream.30");
            fake.ReadGate.SetResult(ending);
            await widget.WaitForStateReadAsync();fake.ReadGate=null;
            check(Snapshot().PinnedLayouts.Any(pin=>pin.Id=="discord.stream"),
                "A provider read begun before a replacement watch cannot withdraw that replacement's layout");
            fake.State=ending;
            await widget.OnActionAsync(new("refresh","test"));
            await widget.WaitForStateReadAsync();
            check(!Snapshot().PinnedLayouts.Any(pin=>pin.Id=="discord.stream")&&Video("discord.stream.2")==secondVideo&&Video("discord.stream.3")==thirdVideo,
                "An authoritative stream end withdraws only its layout and preserves both other streams");
            fake.State=fake.State with{Streams=streams};
            await widget.OnActionAsync(new("refresh","test"));await widget.WaitForStateReadAsync();
            await Act("discord.stream.30");
            var stoppedSelection=Guid.NewGuid().ToString("N");
            await Select("discord.stream",stoppedSelection,true);
            await Until(()=>Video("discord.stream") is not null);
            check(!await Select("discord.stream",reopened,false)&&Video("discord.stream") is not null,
                "A stream can be watched again after ending and rejects the retired selection's close");
            await Act("discord.streams.stop");
            check(!Snapshot().PinnedLayouts.Any(pin=>pin.Id=="discord.stream")&&Video("discord.stream.2")==secondVideo,
                "Stop watching withdraws that pin's layout while leaving the other receiver running");
            await Select("discord.stream",stoppedSelection,false);
            CheckNoDetachedCards("After the host closes a stopped stream, no detached slot cards remain");
            await Act("discord.stream.30");
            await Select("discord.stream",Guid.NewGuid().ToString("N"),true);
            await Until(()=>Video("discord.stream") is not null);
            check(Video("discord.stream.2")==secondVideo,"Watch and pin works again after explicitly stopping");
            fake.State=fake.State with {Account="2"};
            await widget.OnActionAsync(new("refresh","test"));
            await widget.WaitForStateReadAsync();
            await Until(()=>fake.Receivers.All(receiver=>receiver.Disposed));
            check(Video("discord.stream") is null&&Video("discord.stream.2") is null&&Video("discord.stream.3") is null,
                "An account change retires all three incoming screen leases");
            fake.FailReceivers=true;
            await Act("discord.stream.30");
            await Select("discord.stream",Guid.NewGuid().ToString("N"),true);
            await Until(()=>Snapshot().PinnedLayouts.Any(pin=>pin.Id=="discord.stream"&&
                Nodes(pin.Root!).Any(node=>node.Text=="Screen unavailable")));
            check(Nodes(Snapshot().Root).Any(node=>node.Id=="discord.stream.30.card")&&
                Nodes(Snapshot().Root).Any(node=>node.Id=="discord.streams.manage")&&
                Nodes(Snapshot().Root).Any(node=>node.Id=="discord.streams.unpin")&&
                Nodes(Snapshot().Root).Any(node=>node.Id=="discord.streams.stop"),
                "A failed selected receiver retains the real stream card and Manage, Unpin, and Stop watching controls");
        }
        finally {await WidgetTestHost.DestroyAsync(widget);}

        ViewSnapshot Snapshot()=>widget.RenderSnapshot("multi-streams",100);
        void CheckNoDetachedCards(string description)=>check(!Nodes(Snapshot().Root)
            .Any(node=>node.Id.StartsWith("discord.streams.ended",StringComparison.Ordinal)),description);
        WebSessionReference? Video(string layout)=>Snapshot().PinnedLayouts.SingleOrDefault(pin=>pin.Id==layout)?.Root is { } root
            ?Nodes(root).Select(node=>node.WebBrowser?.HostedSession).SingleOrDefault(value=>value is not null):null;
        async Task Act(string id)
        {
            var snapshot=Snapshot();
            var node=Nodes(snapshot.Root).Single(node=>node.Id==id);
            await widget.OnActionAsync(new(node.ActionId!,id,InputScopeId:snapshot.ActiveInputScopeId));
        }
        ValueTask<bool> Select(string layout,string selection,bool selected)
        {
            Snapshot();
            return widget.OnControllerInputAsync(new(ControllerButton.View,ControllerEventPhase.Pressed,
                ControllerInputContext.PinnedLayoutSelection,SnapshotSequence:100)
                {PinnedLayoutId=layout,PinnedSelectionId=selection,IsPinnedLayoutSelected=selected});
        }
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
    private static async Task Until(Func<bool> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!condition())await Task.Delay(10,timeout.Token);}
}
