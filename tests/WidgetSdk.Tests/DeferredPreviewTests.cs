using System.Threading.Channels;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class DeferredPreviewTests
{
    internal static async Task Run()
    {
        var widget=new Fixture();await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
        try
        {
            using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"deferred-preview");
            Check(widget.Reads==0&&host.CurrentSnapshot.Root.Children[0].Children[0].FocusPresentation is null,"Render carries references only");
            Check(host.CurrentSnapshot.ProtocolVersion==95,"Deferred contract is versioned");
            using var first=await host.AcquirePreviewAsync("surface","first");
            Check(widget.Reads==1&&first.Range.Items[0].Root.Children.Count==2,"One demand resolves one complete fragment");
            var content=first.Range.Items[0].Root;var rich=content.Children[1];
            Check(rich.Kind==ViewNodeKind.RichText&&rich.Children.Count==80,"Rich formatting survives independently of the row budget");
            Check(first.RoutePreviewAction("first","apply","apply")==WidgetOperationAdmission.Enqueued,"Only resolved fragment actions are admitted");
            Check(await widget.Actions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3))=="one:first","Captured context and key reach the action");
            Check(first.RoutePreviewAction("first","apply","forged") is null,"Undeclared action is rejected");
            widget.Source.Publish("two");host.PublishSnapshot();
            Check(first.RoutePreviewAction("first","apply","apply") is null,"Context replacement retires cached actions");
            widget.Hold=new(TaskCreationOptions.RunContinuationsAsynchronously);widget.Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation=new CancellationTokenSource();var pending=host.AcquirePreviewAsync("surface","slow",cancellation.Token).AsTask();
            await widget.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));cancellation.Cancel();
            await Throws<OperationCanceledException>(async()=>await pending);
            await Throws<InvalidOperationException>(async()=>{using var ignored=await host.AcquirePreviewAsync("surface","new");});
            widget.Hold.SetResult();await Task.Delay(50);widget.Hold=null;
            using var fresh=await host.AcquirePreviewAsync("surface","new");
            Check(fresh.Range.Items[0].Key=="new","Provider slot recovers only after actual completion");
            using(var artwork=await host.AcquirePreviewAsync("surface","artwork"))
                Check(await artwork.ResolveArtworkAsync("artwork",new("cover")) is {ContentType:WidgetArtworkContentType.Png},"Artwork uses the captured preview resolver");
            widget.Interactive=false;host.PublishSnapshot();
            await Throws<ProtocolValidationException>(async()=>{using var invalid=await host.AcquirePreviewAsync("surface","readonly");});
            var passive=new ViewNode{Id="text",Kind=ViewNodeKind.RichText,Children=[new(){Id="span",Kind=ViewNodeKind.Text,Text="Colored"}]};
            FocusPreviewContentContract.Validate(passive,false);
            await Throws<ProtocolValidationException>(()=>{FocusPreviewContentContract.Validate(new(){Id="too-many",Kind=ViewNodeKind.Stack,
                Children=Enumerable.Range(0,257).Select(index=>new ViewNode{Id="node"+index,Kind=ViewNodeKind.Text,Text="Text"}).ToArray()},false);return Task.CompletedTask;});
        }
        finally{widget.Hold?.TrySetResult();await WidgetTestHost.DestroyAsync(widget);}
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static async Task Throws<T>(Func<Task> action)where T:Exception{try{await action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private sealed class Fixture:Widget
    {
        internal readonly WidgetFocusPreviewSource<string> Source;
        internal int Reads;internal bool Interactive=true;
        internal TaskCompletionSource? Hold,Entered;
        internal readonly Channel<string> Actions=Channel.CreateUnbounded<string>();
        internal Fixture()
        {
            Source=CreateFocusPreviewSource("provider","one",new WidgetFocusPreviewOptions<string>
            {
                Resolve=async(context,key,token)=>
                {
                    Interlocked.Increment(ref Reads);Entered?.TrySetResult();if(Hold is {} hold)await hold.Task;
                    if(key=="artwork")return UI.Artwork(new("cover"),"image","Preview artwork");
                    return UI.Stack("content",UI.Button("Apply","apply","apply"),UI.RichText("rich",
                        Enumerable.Range(0,80).Select(index=>(WidgetElement)UI.Text("Span "+index,"span"+index)).ToArray()));
                },
                OnAction=(context,key,action,token)=>Actions.Writer.WriteAsync(context+":"+key,token),
                ResolveArtwork=(context,key,handle,token)=>ValueTask.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAIUlEQVR4nGP4f83hPwiriJmAMTqfgaACXBIwPmEFg8ANADROkCEUK5WhAAAAAElFTkSuQmCC"))),
            });
        }
        public override WidgetView Render()=>new(UI.FocusPresentationSurface(UI.Stack("rows",UI.Button("First","choose","first").PresentOnFocus("first")),UI.Text("","empty"),"surface")
            with{Preview=new(){EnterButton=Interactive?ControllerButton.X:null},PreviewSource=Source.Descriptor},"first");
    }
}
