using System.Globalization;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class SyntheticMessagesWorker
{
    // Same native-worker test seam as CaptureLibraryWidgetFixture. These files
    // exist only in the deterministic service; no path or filesystem grant is
    // fabricated and this scenario cannot qualify OS picker authorization.
    internal static async Task RunAsync(string[] args)
    {
        var file=new WidgetFileReference(new string('e',32),"messages-upload-fixture.txt","text/plain",16384,long.MaxValue);
        var released=false;
        var services=new WidgetTestHostServicesBuilder().WithPrivateState(new())
            .WithHandler(WidgetFileCapabilities.Choose,(_,token)=>
            {token.ThrowIfCancellationRequested();released=false;return ValueTask.FromResult(new WidgetFileSelection([file]));})
            .WithHandler(WidgetFileCapabilities.Get,(request,token)=>
            {token.ThrowIfCancellationRequested();if(released||request.Id!=file.Id)throw new InvalidDataException("Foreign synthetic file");return ValueTask.FromResult(file);})
            .WithHandler(WidgetFileCapabilities.Read,(request,token)=>
            {
                token.ThrowIfCancellationRequested();
                if(released||request.Id!=file.Id||request.Offset<0||request.Offset>file.ByteLength)throw new InvalidDataException("Foreign synthetic read");
                var count=(int)Math.Min(request.Count,file.ByteLength-request.Offset);
                return ValueTask.FromResult(new WidgetFileReadChunk(request.Offset,Enumerable.Repeat((byte)'x',count).ToArray(),request.Offset+count==file.ByteLength));
            })
            .WithHandler(WidgetFileCapabilities.Release,(request,_)=>
            {if(request.Id!=file.Id)throw new InvalidDataException("Foreign synthetic release");released=true;return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));}).Build();
        var session=new SyntheticDiscordSession();
        await new WidgetWorkerServer(new DiscordWebViewWidget(session,uploadAttachment:session.UploadMessagesFixtureAsync),
            Required("--widget-instance"),Required("--widget-pipe"),int.Parse(Required("--max-message-bytes"),CultureInfo.InvariantCulture),
            services,Required("--widget-session-nonce")).RunAsync();

        string Required(string name)
        {var index=Array.IndexOf(args,name);return index>=0&&index+1<args.Length?args[index+1]:throw new ArgumentException(name);}
    }
}
