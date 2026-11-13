using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ChannelSuggestionTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var longName=new string('x',94)+"🎮"+new string('z',24);
        var fake=new FakeSession();
        fake.State=fake.State with{Channels=[new("20","1","Lobby","First guild"),new("21","2","Lobby","Second guild"),
            new("22","2",longName,new string('g',120))]};
        var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(fake,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder().Build());
        await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
        try
        {
            await Wait(()=>Nodes(Snapshot().Root).Any(node=>node.Id=="discord.main.mute"));
            await OpenPicker();
            var query=Query("lob");var initialSource=query.SourceKey;var reads=fake.Reads;
            var result=await widget.OnSuggestTextAsync(query,default);
            check(result.Items.Count==2&&result.Items.Select(item=>item.Description).Order().SequenceEqual(new[]{"First guild","Second guild"})&&
                result.Items.Select(item=>item.Id).Distinct().Count()==2&&result.Items.All(item=>item.Value=="Lobby"),
                "Loaded channel suggestions preserve duplicate channel identities and show each guild");
            check(result.Items.All(item=>item.Value.Length<=120)&&TextSuggestionContract.IsValid(result,6,120),
                "Channel suggestions satisfy the keyboard result and field-length limits");
            check(fake.Reads==reads&&fake.Commands.IsEmpty&&fake.MicrophoneRequests==0&&
                Nodes(Snapshot().Root).Single(node=>node.Id=="discord.channels.search").TextEntryValue=="",
                "Suggestion lookup neither fetches Discord nor commits text, joins voice, or requests microphone access");
            var unicode=await widget.OnSuggestTextAsync(Query("xxx"),default);
            check(unicode.Items.Single().Value==longName&&unicode.Items.Single().Label==new string('x',94)+"…"&&
                TextSuggestionContract.IsValid(unicode,6,120),
                "Long Unicode labels use SDK-safe boundaries while retaining the complete120-unit search value");
            var tail=await widget.OnSuggestTextAsync(Query("zzzz"),default);
            check(tail.Items.Single().Value==longName,
                "Keyboard suggestions match the complete channel name beyond its abbreviated label");
            await widget.OnActionAsync(new("search","discord.channels.search"){CommittedText="zzzz"});
            check(Nodes(Snapshot().Root).Single(node=>node.Id=="discord.channels").IndexedCollection!.Count==1,
                "Result-list search retains full-name suffix matches beyond the suggestion label limit");
            check((await widget.OnSuggestTextAsync(Query("xxx") with{MaximumValueLength=20},default)).Items.Count==0,
                "The shared catalog respects a smaller editor value budget");
            check((await widget.OnSuggestTextAsync(Query("   "),default)).Items.Count==0,
                "Whitespace alone does not produce arbitrary channel suggestions");

            await widget.OnActionAsync(new("search","discord.channels.search"){CommittedText=result.Items[0].Value});
            check(Nodes(Snapshot().Root).Single(node=>node.Id=="discord.channels").IndexedCollection!.Count==2&&fake.Commands.IsEmpty,
                "Committing a completed channel name only filters the list; joining remains a separate action");
            fake.State=fake.State with{Muted=!fake.State.Muted};await Refresh();
            check(Query("lob").SourceKey==initialSource,"Unchanged account catalog survives ordinary presence/call polling without rebuilding its suggestion source");
            fake.State=fake.State with{Channels=[new("20","1","Renamed","First guild")]};await Refresh();
            check((await widget.OnSuggestTextAsync(query,default)).Unavailable&&Query("Ren").SourceKey!=initialSource&&
                (await widget.OnSuggestTextAsync(Query("Ren"),default)).Items.Single().Value=="Renamed",
                "A replaced channel catalog retires prior suggestion results and exposes current names");

            var previous=Query("Ren");fake.State=fake.State with{Account="2"};await Refresh();
            check((await widget.OnSuggestTextAsync(previous,default)).Unavailable,
                "Prior-account suggestion sources cannot expose channel names after an account switch");
            previous=Query("Ren");fake.Reference=new(new string('b',32));
            check((await widget.OnSuggestTextAsync(previous,default)).Unavailable,
                "Backend replacement immediately retires suggestions even before the next state read");
            await Refresh();check(Query("Ren").SourceKey!=previous.SourceKey,"A recovered backend receives a fresh account-bound suggestion source");

            previous=Query("Ren");
            var picker=Snapshot();var back=Nodes(picker.Root).Single(node=>node.Id=="discord.picker.back");
            await widget.OnActionAsync(new(back.ActionId!,back.Id,InputScopeId:picker.ActiveInputScopeId));
            check((await widget.OnSuggestTextAsync(previous,default)).Unavailable,"Leaving Channels rejects delayed keyboard suggestion work");
            await OpenPicker();
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Background);
            check((await widget.OnSuggestTextAsync(previous,default)).Unavailable,"Background widgets do not service the retired editor");
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel();
                try{await widget.OnSuggestTextAsync(Query("Ren"),cancelled.Token);throw new Exception("Cancelled query completed.");}
                catch(OperationCanceledException){check(true,"Suggestion lookup observes host cancellation");}
            }
            check((await widget.OnSuggestTextAsync(Query("Ren") with{FieldId="other.field"},default)).Unavailable&&
                (await widget.OnSuggestTextAsync(Query("Ren") with{Revision=0},default)).Unavailable,
                "Wrong-field and malformed suggestion queries fail closed");
            fake.State=fake.State with{Channels=Enumerable.Range(1,1000).Select(id=>new DiscordChannel(id.ToString(),"1","Channel "+id,"Guild")).ToArray()};
            await Refresh();
            check((await widget.OnSuggestTextAsync(Query("Channel") with{MaximumResults=3},default)).Items.Count==3,
                "A thousand loaded channels still returns only the requested bounded completion set");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}

        ViewSnapshot Snapshot()=>widget.Render().CreateSnapshot("channel.suggestions",1);
        ValueTask OpenPicker()=>widget.OnActionAsync(new("server-picker","discord.main.servers",InputScopeId:Snapshot().ActiveInputScopeId)
            {FocusedElementId="discord.main.servers"});
        TextSuggestionQuery Query(string text)
        {
            var field=Nodes(Snapshot().Root).Single(node=>node.Id=="discord.channels.search");
            return new(field.Id,field.TextEntrySuggestions!.Key,Guid.NewGuid().ToString("N"),1,text,6,120);
        }
        async Task Refresh()
        {await widget.OnActionAsync(new("refresh","test"));await widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));}
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
    private static async Task Wait(Func<bool> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!condition())await Task.Delay(10,timeout.Token);}
}
