using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ReceiverLifecycleTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var fake=new FakeSession();
        var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(fake,refreshInterval:TimeSpan.FromHours(1)),
            new WidgetTestHostServicesBuilder().WithHandler(WidgetPinningCapabilities.Open,(_,token)=>{token.ThrowIfCancellationRequested();return ValueTask.FromResult(new WidgetPinReceipt(true));}).Build());
        await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
        var pin=WidgetTestHost.CreatePinnedLayoutHost(widget,"health",100);
        try
        {
            await Until(()=>Nodes(Snapshot(widget).Root).Any(n=>n.Id=="discord.stream.30"));
            await Act(widget,"watch.30");await pin.ReplaceSnapshotAsync();await pin.SelectAsync("discord.stream");
            await Until(()=>fake.Receivers.Count==1&&PinnedSession(widget)==fake.Receivers.First().Reference);
            var first=fake.Receivers.First();var watches=fake.Commands.Count(c=>c.Method=="watch");
            first.FailureSource.TrySetResult(new(first.Lease,"rtc_failed"));
            await Until(()=>first.Disposed&&PinnedSession(widget) is null);
            check(Nodes(Snapshot(widget).PinnedLayouts.Single(p=>p.Id=="discord.stream").Root!).Any(n=>n.Text?.Contains("retry")==true),
                "Terminal receiver health replaces the stale browser with actionable retry guidance");
            check(fake.Commands.Count(c=>c.Method=="watch")==watches&&!fake.Commands.Any(c=>c.Method is "stopShare" or "leave"),
                "Receiver failure does not automatically replay watch or affect a call/broadcast");
            check(fake.Commands.Any(c=>c.Method=="unwatch"&&c.Args.GetProperty("lease").GetString()==first.Lease),
                "Receiver failure releases only its exact provider lease");

            await Act(widget,"watch.30");await Until(()=>fake.Receivers.Count==2&&PinnedSession(widget)==fake.Receivers.Last().Reference);
            var old=fake.Receivers.Last();old.KeepFailurePendingOnDispose=true;
            await Act(widget,"watch.30");await Until(()=>fake.Receivers.Count==3&&PinnedSession(widget)==fake.Receivers.Last().Reference);
            var replacement=fake.Receivers.Last();old.FailureSource.TrySetResult(new(old.Lease,"session_closed"));
            await Task.Delay(50);
            check(!replacement.Disposed&&PinnedSession(widget)==replacement.Reference,
                "Delayed failure of a retired receiver cannot remove its replacement");
            await pin.RevokeAsync();await Until(()=>replacement.Disposed);
            check(replacement.Failure.IsCanceled,"Pin revocation cancels health observation without a false failure");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
    }
    private static ViewSnapshot Snapshot(DiscordWebViewWidget widget)=>widget.Render().CreateSnapshot("receiver.health",1);
    private static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
    private static WebSessionReference? PinnedSession(DiscordWebViewWidget widget)=>Nodes(Snapshot(widget).PinnedLayouts.Single(p=>p.Id=="discord.stream").Root!)
        .Select(node=>node.WebBrowser?.HostedSession).SingleOrDefault(reference=>reference is not null);
    private static ValueTask Act(DiscordWebViewWidget widget,string action)
    {
        var snapshot=Snapshot(widget);action=DeclaredActions.Resolve(snapshot,action);var node=Nodes(snapshot.Root).FirstOrDefault(n=>n.ActionId==action);
        return widget.OnActionAsync(new(action,node?.Id??"test",InputScopeId:snapshot.ActiveInputScopeId){FocusedElementId=node?.Id});
    }
    private static async Task Until(Func<bool> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));while(!condition())await Task.Delay(10,timeout.Token);}
}
