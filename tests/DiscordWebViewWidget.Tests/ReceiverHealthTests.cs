using WidgetRail.Samples.DiscordWebView;

internal static class ReceiverHealthTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var lease=new string('a',32);var health=new DiscordReceiverHealth();health.Bind(lease);
        health.SessionEnded();
        check((await health.Failure)==new DiscordReceiverFailure(lease,"session_closed"),"Direct video shutdown reports the exact bound lease");
        var retired=new DiscordReceiverHealth();retired.Bind(lease);retired.Retire();retired.SessionEnded();
        check(retired.Failure.IsCanceled,"Intentional receiver retirement is not a false browser failure");
        var early=new DiscordReceiverHealth();early.SessionEnded();
        try{early.Bind(lease);throw new Exception("Bound an already closed receiver");}catch(InvalidOperationException){check(true,"A session closed before negotiation cannot publish a receiver");}

    }
}
