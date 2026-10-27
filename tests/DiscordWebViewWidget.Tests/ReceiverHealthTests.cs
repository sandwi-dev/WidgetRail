using System.Collections.Concurrent;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ReceiverHealthTests
{
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var lease=new string('a',32);var health=new DiscordReceiverHealth();health.Bind(lease);
        foreach(var payload in new object[]{new{receiverHealth=1,lease,phase="disconnected"},new{receiverHealth="1",lease,phase="rtc_failed"},new{receiverHealth=1,lease=new string('b',32),phase="rtc_failed"},new{ready=true}})
            health.Read(JsonSerializer.SerializeToElement(payload));
        check(!health.Failure.IsCompleted,"Recoverable malformed and foreign-lease receiver messages cannot terminate ownership");
        health.Read(JsonSerializer.SerializeToElement(new{receiverHealth=1,lease,phase="playback_failed"}));
        health.Read(JsonSerializer.SerializeToElement(new{receiverHealth=1,lease,phase="rtc_closed"}));
        check((await health.Failure).Code=="playback_failed","Receiver terminal health is bounded to the first exact-lease failure");
        var retired=new DiscordReceiverHealth();retired.Bind(lease);retired.Retire();retired.SessionEnded();
        check(retired.Failure.IsCanceled,"Intentional receiver retirement is not a false browser failure");
        var early=new DiscordReceiverHealth();early.SessionEnded();
        try{early.Bind(lease);throw new Exception("Bound an already closed receiver");}catch(InvalidOperationException){check(true,"A session closed before negotiation cannot publish a receiver");}

        var messages=new ConcurrentQueue<string>();var readFailed=false;var optedIn=false;var reads=0;
        var probe=WidgetTestHost.Attach(new TransportProbe(),new WidgetTestHostServicesBuilder().WithHandler(WidgetWebSessionCapabilities.Command,async(command,token)=>
        {
            if(command.Operation==WebSessionOperation.Open)return new(){Session=new(Guid.NewGuid().ToString("N"))};
            if(command.Operation==WebSessionOperation.Close)return new();
            if(command.Operation==WebSessionOperation.ReadMessages)
            {
                await Task.Delay(2,token);Interlocked.Increment(ref reads);
                if(readFailed)throw new IOException("Synthetic browser closed");
                return new(){MessagesBase64=messages.TryDequeue(out var message)?[message]:[]};
            }
            if(command.Operation==WebSessionOperation.PostMessage)
            {
                using var doc=JsonDocument.Parse(Convert.FromBase64String(command.MessageBase64!));var request=doc.RootElement;
                if(request.GetProperty("kind").GetString()!="request")return new();
                var payload=request.GetProperty("payload");optedIn=payload.GetProperty("healthEvents").GetBoolean();
                messages.Enqueue(Encode(new{widgetRail=1,kind="response",id=request.GetProperty("id").GetString(),payload=new{lease=payload.GetProperty("lease").GetString(),answer=new{type="answer",sdp="generated"}}}));
            }
            return new();
        }).Build());
        try
        {
            await using var client=new DiscordWebSessionClient(probe.Sessions);
            await using(var receiver=await client.OpenReceiverAsync(default))
            {
                await receiver.AcceptAsync(JsonSerializer.SerializeToElement(new{lease,offer=new{type="offer",sdp="generated"}}),default);
                check(optedIn,"Production receiver explicitly opts into lease-correlated terminal events");
                messages.Enqueue(Encode(new{receiverHealth=1,lease=new string('b',32),phase="rtc_failed"}));
                var before=reads;using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));while(reads<before+3)await Task.Delay(5,deadline.Token);
                check(!receiver.Failure.IsCompleted,"Real SDK message drain ignores a stale receiver lease");
                messages.Enqueue(Encode(new{receiverHealth=1,lease,phase="rtc_failed"}));
                var result=await receiver.Failure.WaitAsync(TimeSpan.FromSeconds(3));
                check(result.Lease==lease&&result.Code=="rtc_failed","Real SDK message stream propagates exact receiver failure");
            }
            await using(var receiver=await client.OpenReceiverAsync(default))
            {
                await receiver.AcceptAsync(JsonSerializer.SerializeToElement(new{lease,offer=new{type="offer",sdp="generated"}}),default);
                readFailed=true;
                var result=await receiver.Failure.WaitAsync(TimeSpan.FromSeconds(3));
                check(result.Lease==lease&&result.Code=="session_closed","SDK session completion becomes an actionable exact-lease receiver failure");
            }
        }
        finally{await WidgetTestHost.DestroyAsync(probe);}
    }
    private static string Encode(object value)=>Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value));
}
