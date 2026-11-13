using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WebSessionMicrophoneTests
{
    internal static async Task Run()
    {
        WebSessionCommand? observed=null;
        var block=false;
        string? failure=null;
        var widget=WidgetTestHost.Attach(new Probe(),new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetWebSessionCapabilities.Command,async(command,token)=>
            {
                if(command.Operation==WebSessionOperation.OpenBackend)return new(){Session=new(new string('d',32))};
                if(command.Operation==WebSessionOperation.ReadMessages)await Task.Delay(100,token);
                return new();
            })
            .WithHandler(WidgetWebSessionPermissionCapabilities.Request, (command, _) =>
            {
                observed=command;
                return ValueTask.FromResult(new WebSessionReply {Permission=new(WebSessionPermissionState.Granted,true)});
            })
            .WithHandler(WidgetWebSessionMicrophoneCapabilities.Request,async(command,token)=>
            {
                observed=command;
                if(failure is not null)throw new WidgetCapabilityException(failure,"Synthetic microphone rejection");
                if(block)await Task.Delay(Timeout.Infinite,token);
                return new(){Microphone=new(WebSessionMicrophoneState.Granted)};
            }).Build());
        await using(var session=await widget.Open())
        {
            var notification=await session.RequestPermissionAsync(WebSessionPermissionKind.Notifications);
            if (!notification.Remembered || notification.State!=WebSessionPermissionState.Granted ||
                observed?.Operation!=WebSessionOperation.RequestPermission || observed.PermissionKind!=WebSessionPermissionKind.Notifications ||
                observed.Session!=session.Reference) throw new Exception("Typed notification request lost authority or receipt");
            var result=await session.RequestMicrophoneAsync();
            if(result.State!=WebSessionMicrophoneState.Granted || observed?.Operation!=WebSessionOperation.RequestMicrophone || observed.Session!=session.Reference || observed.Destination is not null)
                throw new Exception("SDK microphone request lost its owned session or supplied an origin");
            foreach(var code in new[]{"lifecycle_denied","request_canceled"})
            {
                failure=code;
                if((await session.RequestMicrophoneAsync()).State!=WebSessionMicrophoneState.Cancelled)
                    throw new Exception("Interactive ownership loss became a user-facing error");
            }
            foreach(var code in new[]{"session_closed","unsupported_operation"})
            {
                failure=code;
                try{await session.RequestMicrophoneAsync();throw new Exception("Incompatible/closed session was masked as cancellation");}
                catch(WidgetCapabilityException error)when(error.ErrorCode==code) { }
            }
            using(var alreadyCancelled=new CancellationTokenSource())
            {
                alreadyCancelled.Cancel();failure="lifecycle_denied";
                try{await session.RequestMicrophoneAsync(alreadyCancelled.Token);throw new Exception("Explicit cancellation became a receipt");}
                catch(OperationCanceledException) { }
            }
            failure=null;
            block=true;
            using var cancellation=new CancellationTokenSource(50);
            try{await session.RequestMicrophoneAsync(cancellation.Token);throw new Exception("Cancelled microphone wait completed");}catch(OperationCanceledException){}
        }
        await WidgetTestHost.DestroyAsync(widget);
    }
    private sealed class Probe:Widget
    {
        public override WidgetView Render()=>new(UI.Text("Microphone fixture","test"));
        internal ValueTask<WidgetWebSession> Open()=>HostServices.WebSessions.OpenAsync(new("backend","Backend",WebSessionSource.Remote("https://example.com"))
            {PageAdapter=new(){Mode=WebSessionPageAdapterMode.SessionBackend,ScriptAsset="backend.js",Origins=["https://example.com"]}});
    }
}
