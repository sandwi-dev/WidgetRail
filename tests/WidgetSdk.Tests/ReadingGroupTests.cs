using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ReadingGroupTests
{
    public static async Task Run()
    {
        var group=UI.ReadingGroup("message","Message",ActionSurfaceOrientation.Vertical,
            UI.Text("Body","body"),UI.Button("Attachment","open","attachment"));
        var snapshot=new WidgetView(UI.Stack("root",group).InputScope("scope"),"message").CreateSnapshot("reading-test",1);
        Check(snapshot.ProtocolVersion>=ProtocolConstants.ReadingGroupVersion,"Reading groups require the new capability version");
        Check(ViewSnapshotValidator.Validate(snapshot).Count==0,"A bounded reading group validates");
        Check(ViewSnapshotValidator.Validate(snapshot with{ProtocolVersion=ProtocolConstants.ReadingGroupVersion-1}).Count>0,"Older peers cannot silently reinterpret the group");
        var rail = UI.ReadingGroup("captures", "Captures", ActionSurfaceOrientation.Vertical,
            UI.HorizontalScroll("strip", UI.Button("First", "open.first", "first"), UI.Button("Second", "open.second", "second")));
        var railSnapshot = new WidgetView(UI.Stack("root", rail).InputScope("scope"), "captures").CreateSnapshot("rail-test", 1);
        Check(ViewSnapshotValidator.Validate(railSnapshot).Count == 0 &&
            ViewSnapshotValidator.Validate(railSnapshot with { ProtocolVersion = ProtocolConstants.ReadingGroupViewportVersion - 1 }).Count > 0,
            "Bounded horizontal reading rails require a supporting host");
        Reject(() => UI.ReadingGroup("captures", "Captures", ActionSurfaceOrientation.Vertical,
            UI.VerticalScroll("strip", UI.Button("First", "open.first", "first"))));
        Reject(() => UI.ReadingGroup("captures", "Captures", ActionSurfaceOrientation.Vertical,
            UI.HorizontalScroll("strip", UI.Button("First", "open.first", "first")).Paginate(null, "load")));
        var attachment = new CaptureAttachment(new string('a', 32), "image/png", 3840, 2160, 1024, 0, long.MaxValue);
        var capture = new CaptureLibraryItem(attachment.Id, attachment, 100) { Revision = 42 };
        CaptureStripItem[] captures = [new(capture, new("thumb"), "Thursday afternoon", "open.capture")];
        ViewSnapshot Strip(bool expanded) => new WidgetView(UI.Stack("root",
            UI.CaptureStrip("photos", "Thursday captures", captures, capture.Id, expanded, "close.photos"))
            .InputScope("scope"), "photos").CreateSnapshot("strip-test", 1);
        var closed = Strip(false);
        var expanded = Strip(true);
        Check(closed.Root.Children[0].Children[0].Children.Count == 0 && ViewSnapshotValidator.Validate(expanded).Count == 0,
            "The closed strip publishes only the rail and the expanded strip is valid");
        var preview = expanded.Root.Children[0].Children.First().Children.Single().MediaPlayer;
        Check(preview?.Source.SavedCapture == new SavedCaptureMediaReference(capture.Id, 42) &&
            preview.Options.AutoPlay == false && closed.Root.Children[0].Children[^1].Id == expanded.Root.Children[0].Children[^1].Id,
            "Only the selected saved capture revision is exposed and the rail retains its identity");
        var closedRow = closed.Root.Children[0];
        var openRow = expanded.Root.Children[0];
        Check(closedRow.Children.Count == 2 && openRow.Children.Count == 2 &&
            closedRow.Children[1].Children[0].ActionId == "open.capture" &&
            openRow.Children[1].Children[0].ActionId == "close.photos",
            "The rail stays directly beneath the stage and the open thumbnail dismisses its own preview");
        Reject(() => UI.ReadingGroup("forged", "Unapproved player", ActionSurfaceOrientation.Vertical,
            UI.MediaPlayer(MediaPlayerSource.LocalFile(@"C:\example.mp4"), "unapproved")));
        var thumbnailAction=UI.ActionSurface("open","gallery","Attachments",ActionSurfaceOrientation.Vertical,
            UI.Row("thumbnails",UI.Grid("thumbnail",[GridTrack.Star()],[GridTrack.Star()],
                UI.Icon(WidgetGlyph.Play,"play","Video").InGrid())));
        var richGroup=UI.ReadingGroup("rich","Message",ActionSurfaceOrientation.Vertical,
            UI.Grid("layout",[],[GridTrack.Star()],UI.Stack("body",thumbnailAction).InGrid()));
        var richSnapshot=new WidgetView(UI.Stack("root",richGroup).InputScope("scope"),"rich").CreateSnapshot("reading-test",1);
        Check(ViewSnapshotValidator.Validate(richSnapshot).Count==0,
            "An admitted child action retains its own bounded presentational depth inside a reading group");
        Reject(()=>UI.ReadingGroup("deep","Message",ActionSurfaceOrientation.Vertical,
            UI.Stack("a",UI.Stack("b",UI.Stack("c",UI.Stack("d",UI.Text("Too deep","e")))))));
        Reject(()=>UI.ActionSurface("open","deep-action","Attachments",ActionSurfaceOrientation.Vertical,
            UI.Stack("a",UI.Stack("b",UI.Stack("c",UI.Stack("d",UI.Text("Too deep","e")))))));
        Reject(()=>UI.ReadingGroup("large","Message",ActionSurfaceOrientation.Vertical,
            Enumerable.Range(0,32).Select(i=>(WidgetElement)UI.ActionSurface("open."+i,"tile."+i,"Tile",ActionSurfaceOrientation.Vertical,
                UI.Text("One","one."+i),UI.Text("Two","two."+i),UI.Text("Three","three."+i),UI.Text("Four","four."+i))).ToArray()));
        var bounded=UI.ReadingGroup("bounded","Message",ActionSurfaceOrientation.Vertical,
            Enumerable.Range(0,8).Select(i=>(WidgetElement)UI.Stack("block."+i,
                Enumerable.Range(0,15).Select(j=>(WidgetElement)UI.Text("Text","text."+i+"."+j)).ToArray())).ToArray());
        var boundary=new WidgetView(UI.Stack("root",bounded).InputScope("scope"),"bounded").CreateSnapshot("reading-test",1);
        Check(ViewSnapshotValidator.Validate(boundary).Count==0,"Exactly 128 reading layout descendants are admitted");
        var firstBlock=boundary.Root.Children[0].Children[0];
        var oversizedGroup=boundary.Root.Children[0] with {Children=[firstBlock with {Children=[..firstBlock.Children,new(){Id="extra",Kind=ViewNodeKind.Text,Text="Extra"}]},..boundary.Root.Children[0].Children.Skip(1)]};
        Check(ViewSnapshotValidator.Validate(boundary with {Root=boundary.Root with {Children=[oversizedGroup]}})
            .Any(error=>error.Code=="action_surface_too_large"),"Protocol validation rejects a forged 129th reading layout descendant");
        Reject(()=>UI.ActionSurface("open","tile","Tile",ActionSurfaceOrientation.Vertical,UI.Button("Nested","nested","nested")));
        Reject(()=>UI.ReadingGroup("group","Group",ActionSurfaceOrientation.Vertical,
            UI.ReadingGroup("nested","Nested",ActionSurfaceOrientation.Vertical,UI.Button("Go","go","go"))));
        Reject(()=>UI.ReadingGroup("group","Group",ActionSurfaceOrientation.Vertical,
            UI.Button("Go","go","go").Shortcut(ControllerButton.X)));
        Reject(()=>UI.ReadingGroup("group","Group",ActionSurfaceOrientation.Vertical,
            UI.ActionSurface("open","attachment","Attachment",ActionSurfaceOrientation.Vertical,
                UI.MediaPlayer(MediaPlayerSource.WebUrl("https://example.com/video.mp4"),"media"))));
        Reject(()=>UI.ReadingGroup("group","Group",ActionSurfaceOrientation.Vertical,
            UI.Stack("actions",Enumerable.Range(0,33).Select(i=>(WidgetElement)UI.Button("Go","go."+i,"go."+i)).ToArray())));
        var primary=snapshot with{Root=snapshot.Root with{Children=[snapshot.Root.Children[0] with{ActionId="primary"}]}};
        Check(ViewSnapshotValidator.Validate(primary).Any(error=>error.Code=="invalid_reading_group"),"A primary action cannot compete with inline group entry");
        IndexedCollectionInputContract.ValidateInput(new(new(new string('a',32),"item"),ControllerButton.A){InlineAction=new("attachment","open")});
        Reject(()=>IndexedCollectionInputContract.ValidateInput(new(new(new string('a',32),"item"),ControllerButton.A)
            {InlineAction=new("attachment","open"),PreviewAction=new("attachment","open")}));

        var widget=WidgetTestHost.Attach(new Fixture(),new WidgetTestHostServicesBuilder().Build());
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
        try
        {
            using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"reading-group-test");
            using var lease=await host.AcquireAsync("messages",0,1);
            var row=lease.Range.Items.Single().Root;
            var child=row.Children.Single(node=>node.Kind==ViewNodeKind.Button);
            Check(lease.RouteInlineAction("item",child.Id,"open")==WidgetOperationAdmission.Enqueued,"Exact inline action is admitted");
            await WidgetTestHost.WaitForActionsAsync(widget);
            Check(widget.Last is {ActionId:"open",FocusedCollectionItem.ItemKey:"item"} && widget.Last.FocusedElementId==child.Id,
                "Inline action retains the exact child and parent collection occurrence for modal return");
            Check(lease.RouteInlineAction("item",row.Id,"open") is null,"Reading group cannot be forged into a primary command");
            Check(lease.RouteInlineAction("item",child.Id,"different") is null,"Action identity cannot be replaced");
            Check(lease.RouteInlineAction("other",child.Id,"open") is null,"Inline action cannot escape its row");
            widget.Source.UpdateContent(true);host.PublishSnapshot();
            Check(lease.RouteInlineAction("item",child.Id,"open") is null,"Retired content lease cannot activate old inline controls");
            using var disabled=await host.AcquireAsync("messages",0,1);
            var disabledChild=disabled.Range.Items.Single().Root.Children.Single(node=>node.Kind==ViewNodeKind.Button);
            Check(disabled.RouteInlineAction("item",disabledChild.Id,"open") is null,"Disabled inline action is not admitted");
            widget.Source.UpdateContent(false);widget.Source.BeginSelection();host.PublishSnapshot();
            using var selecting=await host.AcquireAsync("messages",0,1);
            var selectableChild=selecting.Range.Items.Single().Root.Children.Single(node=>node.Kind==ViewNodeKind.Button);
            Check(selectableChild.IsDisabled!=true && selecting.RouteInlineAction("item",selectableChild.Id,"open") is null,
                "Collection selection ownership denies even enabled inline actions at the shared admission boundary");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
    }

    private sealed class Fixture:Widget
    {
        internal WidgetIndexedCollection<bool,string> Source {get;}
        internal WidgetActionEvent? Last;
        internal Fixture()
        {
            Source=CreateIndexedCollection<bool,string>("reading.rows",false,1,new()
            {
                EnableSelection=true,ItemKey=item=>new(item),ReadRange=(_,_,_,_)=>ValueTask.FromResult<IReadOnlyList<string>>(["item"]),
                RenderItem=(disabled,item,ids)=>UI.ReadingGroup(ids.Id("row"),"Message",ActionSurfaceOrientation.Vertical,
                    UI.Text("Message text",ids.Id("text")),UI.Button("Attachment","open",ids.Id("attachment")).Disabled(disabled)),
                OnAction=(_,_,action,_)=>{Last=action;return ValueTask.CompletedTask;}
            });
        }
        public override WidgetView Render()=>new(UI.Stack("root",UI.CollectionList("messages",Source,80,"Messages")).InputScope("scope"),"messages");
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new InvalidOperationException("Unsafe reading group declaration was accepted.");}
}
