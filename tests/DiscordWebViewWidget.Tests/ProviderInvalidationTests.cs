using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ProviderInvalidationTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var fake=new FakeSession();
        var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(fake,refreshInterval:TimeSpan.FromHours(1)),
            new WidgetTestHostServicesBuilder().Build());
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
        await widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var reads=fake.Reads;
            fake.State=fake.State with{Muted=false};
            fake.NotifyChanged();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while(fake.Reads==reads)await Task.Delay(10,timeout.Token);
            await widget.WaitForStateReadAsync().WaitAsync(timeout.Token);
            var snapshot=widget.Render().CreateSnapshot("invalidation",1);
            check(Nodes(snapshot.Root).Any(node=>node.Id=="discord.main.mute"&&node.Text=="Mute"),
                "Provider notification refreshes current call controls without waiting for the periodic poll");
            check(fake.Commands.IsEmpty,"Read invalidations never join mute watch or broadcast automatically");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
        var retiredReads=fake.Reads;fake.NotifyChanged();await Task.Delay(30);
        check(fake.Reads==retiredReads,"Widget teardown removes its provider invalidation subscription");
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
}
