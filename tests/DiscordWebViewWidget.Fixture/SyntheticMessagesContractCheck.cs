using System.Text.Json;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SyntheticMessagesContractCheck
{
    internal static async Task RunAsync(string directory)
    {
        directory=Path.GetFullPath(directory);
        if(Directory.Exists(directory))throw new InvalidOperationException("Use a fresh fixture contract-check directory.");
        Directory.CreateDirectory(directory);
        var auditPath=Path.Combine(directory,"messages-provider.json");
        var checks=new List<string>();
        var viewportId=Guid.NewGuid().ToString("N");long viewportSequence=0;
        var provider=new SyntheticDiscordSession(messagesAuditPath:auditPath);
        var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(provider,refreshInterval:TimeSpan.FromHours(1)),
            new WidgetTestHostServicesBuilder().Build());
        Exception? failure=null;
        try
        {
            await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(Node("discord.messages.open") is not null&&Audit().GetProperty("history").GetArrayLength()==0,"Synthetic call page exposes Messages without fetching history");
            await Act("discord.messages.open");await Until(()=>Node("discord.conversation.10") is not null);
            Check(Snapshot().ActiveInputScopeId=="discord.root","Synthetic inbox shares the Call/Messages root scope");
            Check(WidgetTestHost.ValidateWinUiPresentation(Snapshot()).Count==0,"Synthetic inbox passes production WinUI declaration validation");
            await Act("discord.conversation.10");await Until(()=>Node("discord.message.history")?.IndexedCollection?.Count==20);
            Check(Snapshot().ActiveInputScopeId=="discord.conversation.scope","Synthetic conversation has matching navigation and input scope");
            Check(WidgetTestHost.ValidateWinUiPresentation(Snapshot()).Count==0,"Synthetic conversation passes production WinUI declaration validation");
            using(var rows=WidgetTestHost.CreateIndexedCollectionHost(widget,"messages.fixture.check"))
            using(var lease=await rows.AcquireAsync("discord.message.history",0,2))
                Check(lease.Range.Items.All(item=>item.Root.FocusPresentation is null&&Nodes(item.Root).Any(node=>node.Kind==ViewNodeKind.RichText&&
                    string.Concat(node.Children.Select(child=>child.Text)).Length>2000)),
                    "Synthetic ordinary rows contain full inline rich text without previews");
            await Boundary(IndexedCollectionBoundary.Before);await Until(()=>Node("discord.message.history")?.IndexedCollection?.Count==40&&Node("discord.message.boundary.status") is null);
            await Boundary(IndexedCollectionBoundary.Before);await Until(()=>Audit().GetProperty("history").GetArrayLength()>=3&&Node("discord.message.history")?.CollectionBoundaries?.After==true);
            Check(Node("discord.message.history")!.IndexedCollection!.Count<=40,"Synthetic older cursors retain at most40messages");
            await Boundary(IndexedCollectionBoundary.After);await Until(()=>Audit().GetProperty("history").EnumerateArray().Any(item=>item.GetProperty("after").ValueKind==JsonValueKind.String)&&Node("discord.message.boundary.status") is null);
            Check(Node("discord.message.history")!.IndexedCollection!.Count<=40,"Synthetic newer cursor reverses the bounded window");
            Check(Audit().GetProperty("mutationAttempts").GetInt32()==0,"Fixture provider received no send, upload or DM-creation mutation");
            var account=(await provider.ReadAsync(CancellationToken.None)).Account!;
            var around=(await provider.InvokeAsync("messageHistory",new{account,channelId="10",around="1065",limit=40},CancellationToken.None))
                .Deserialize<DiscordMessagePage>(DiscordWebSessionClient.Json)!;
            around.Validate(account,"10",null,40,around:"1065");
            Check(around.Items.Any(item=>item.Id=="1065")&&around.Items.Length==40,
                "Synthetic recovery accepts an around-only request and returns one bounded matching interval");
        }
        catch(Exception error){failure=error;}
        finally{await WidgetTestHost.DestroyAsync(widget);}
        await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{passed=failure is null,checks,error=failure?.ToString()}));
        foreach(var check in checks)Console.WriteLine("PASS "+check);
        if(failure is not null)throw new InvalidOperationException("Synthetic Messages contract check failed.",failure);
        Console.WriteLine($"{checks.Count}/{checks.Count} synthetic Messages checks passed.");

        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks.Add(label);}
        JsonElement Audit(){using var value=JsonDocument.Parse(File.ReadAllText(auditPath));return value.RootElement.Clone();}
        ViewSnapshot Snapshot()=>widget.Render().CreateSnapshot("messages.fixture",1);
        ViewNode? Node(string id)=>Nodes(Snapshot().Root).FirstOrDefault(node=>node.Id==id)??ConversationRowsHarness.Find(widget,Snapshot(),id);
        async Task Act(string id)
        {
            if(ConversationRowsHarness.IsRow(id)){await ConversationRowsHarness.ActAsync(widget,id);return;}
            var snapshot=Snapshot();var node=Nodes(snapshot.Root).Single(node=>node.Id==id);
            await widget.OnActionAsync(new(node.ActionId!,id,InputScopeId:snapshot.ActiveInputScopeId){FocusedElementId=id});
        }
        async Task Boundary(IndexedCollectionBoundary direction)
        {
            using var rows=WidgetTestHost.CreateIndexedCollectionHost(widget,"messages.fixture.boundary");
            await rows.ObserveViewportAsync("discord.message.history",new(viewportId,++viewportSequence,true,false){Boundary=direction});
        }
        async Task Until(Func<bool> condition){using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!condition())await Task.Delay(10,limit.Token);}
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
    private static IEnumerable<ViewNode> WithPreviews(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(WithPreviews)).Concat(node.FocusPresentation is {} preview?WithPreviews(preview):[]);
}
