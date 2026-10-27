using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

internal static class PinningServiceTests
{
    internal static async Task Contract()
    {
        WidgetPinRequest? observed=null;
        var widget=WidgetTestHost.Attach(new Probe(),new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetPinningCapabilities.Open,(request,token)=>
            { token.ThrowIfCancellationRequested();observed=request;return ValueTask.FromResult(new WidgetPinReceipt(true)); }).Build());
        if(!(await widget.Pin()).Accepted || observed?.LayoutId!="stream")throw new Exception("Pin handle did not use the intrinsic host path");
        foreach(var invalid in new[]{"", "host.full-widget", "another/widget", new string('x',129)})
        {
            try{await widget.Pin(invalid);throw new Exception("Invalid pin identity accepted");}
            catch(ArgumentException) { }
        }
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        try{await widget.Pin("stream",cancel.Token);throw new Exception("Cancelled pin request was submitted");}
        catch(OperationCanceledException) { }
        await WidgetTestHost.DestroyAsync(widget);
    }
    private sealed class Probe:Widget
    {
        private readonly PinnedLayoutHandle stream;
        internal Probe() => stream=CreatePinnedLayoutHandle("stream","Stream",new());
        internal ValueTask<WidgetPinReceipt> Pin() => HostServices.Pinning.RequestOpenAsync(stream);
        internal ValueTask<WidgetPinReceipt> Pin(string id,CancellationToken token=default) => HostServices.Pinning.RequestOpenAsync(id,token);
        public override WidgetView Render()=>new(UI.Text("Pin fixture","pin.fixture")){PinnedLayouts=[stream.Present(UI.Text("Stream","pin.stream"))]};
    }
}
