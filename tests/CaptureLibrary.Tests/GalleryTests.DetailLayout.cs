using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task DetailUnsavedDoesNotReuseSavedIdentity()
    {
        var temporary=new CaptureAttachment(new string('f',32),"image/png",321,123,12,0,long.MaxValue);
        await using var fixture=new GalleryFixture((builder,_)=>builder
            .WithResponse(WidgetCaptureCapabilities.Validate,new CaptureConfigurationValidation(true))
            .WithResponse(WidgetCaptureCapabilities.Start,new WindowCaptureTicket(new string('e',32)))
            .WithResponse(WidgetCaptureCapabilities.Status,new WindowCaptureStatus(new string('e',32),WindowCapturePhase.Ready,temporary)));
        await fixture.Start();using var host=await OpenEditingSource(fixture);
        await fixture.Action("capture-library.back");await fixture.Until(()=>fixture.Collection is not null&&!fixture.Busy);
        await fixture.Action("capture-library.photo");await fixture.Until(()=>Preview(fixture)?.Id==temporary.Id&&!fixture.Busy);
        Check(fixture.Node("capture-library.heading")?.Text=="Review capture"&&fixture.Node("capture-library.edit-title") is null&&
            fixture.Node("capture-library.detail.date") is null&&fixture.Node("capture-library.metadata")?.Text?.Contains("321 × 123")==true,
            "Unsaved review reused the previous saved capture's title/date/metadata");
        Check(fixture.Node("capture-library.save") is not null&&fixture.Node("capture-library.delete") is null&&fixture.Released==1,
            "Unsaved review retained saved mutations or leaked the departed saved lease");
    }

    internal static async Task DetailActionsAndInlineBindings()
    {
        foreach(var linked in new[]{false,true})
        {
            await using var fixture=new GalleryFixture();
            fixture.Items[0]=fixture.Items[0] with{SourceId=linked?new string('b',32):null};
            await fixture.Start();using var host=await OpenEditingSource(fixture);
            var snapshot=fixture.View();
            Check(ViewSnapshotValidator.Validate(snapshot).Count==0,"Detail declaration is invalid");
            var layout=fixture.Node("capture-library.detail.layout")!;
            Check(layout.GridMinimumColumnWidth is null&&layout.GridLayout is {Columns.Count:2} grid&&
                grid.Columns[0].Sizing==GridTrackSizing.Star&&grid.Columns[0].Value==7&&
                grid.Columns[1].Sizing==GridTrackSizing.Star&&grid.Columns[1].Value==3&&
                fixture.Node("capture-library.detail.stage")?.GridCell is {Column:0,VerticalAlignment:GridCellAlignment.Center}&&
                fixture.Node("capture-library.detail.stage")?.GridLayout is {Rows.Count:1,Columns.Count:1}&&
                fixture.Node("capture-library.preview")?.GridCell?.Column==0&&
                fixture.Node("capture-library.detail.scroll")?.GridCell is {Column:1,VerticalAlignment:GridCellAlignment.Center},
                "Detail must keep the media/sidebar relationship in explicit grid tracks");
            foreach(var id in new[]{"share","favorite","edit-copy","reveal","copy","delete"})
                Check(fixture.Node("capture-library."+id) is {Kind:ViewNodeKind.Button or ViewNodeKind.ActionSurface,IsDisabled:not true,IsBusy:not true},"Direct detail action missing: "+id);
            Check(fixture.Node("capture-library.detail.more") is null&&fixture.Node("capture-library.title.label")?.Text=="Title"&&
                fixture.Node("capture-library.edit-title")?.TextEntryValue=="Boss fight","Detail hid common commands or lost the labeled title field");
            Check(fixture.Node("capture-library.share")?.Intent is {PreparationKey:not null} share&&share.ContractId==WidgetIntentContracts.Files.Id,
                "Direct Share lost its exact-source preparation route");
            Check(fixture.Node("capture-library.delete")?.Text==(linked?"Hide from library":"Move to Recycle Bin"),
                "Direct removal no longer distinguishes linked originals");
            Check(snapshot.Root.Shortcuts.Any(shortcut=>shortcut.Button==ControllerButton.LeftBumper&&shortcut.ActionId=="capture-library.viewer.previous")&&
                snapshot.Root.Shortcuts.Any(shortcut=>shortcut.Button==ControllerButton.RightBumper&&shortcut.ActionId=="capture-library.viewer.next"),
                "Capture navigation must preserve the existing bumper bindings");
            CheckInlineBinding(fixture,"capture-library.back",ControllerPrompt.B,ControllerButton.B,"capture-library.back");
            CheckInlineBinding(fixture,"capture-library.favorite",ControllerPrompt.Y,ControllerButton.Y,"capture-library.favorite");
            foreach(var id in new[]{"capture-library.viewer.previous","capture-library.viewer.next"})
                Check(fixture.Node(id) is {Kind:ViewNodeKind.Button,IsDisabled:not true,IsBusy:not true} button && button.ActionId==id &&
                    fixture.Node(id+".hint") is null,
                    "Previous/Next must remain direct buttons without conflicting bumper hints");
            var original=Preview(fixture);
            var input=fixture.View();
            Check(await fixture.Widget.OnControllerInputAsync(new(ControllerButton.Y,ControllerEventPhase.Pressed,ControllerInputContext.OpenWidget,
                "capture-library.favorite",1,ActiveInputScopeId:input.ActiveInputScopeId,SnapshotSequence:input.Sequence)),"Y shortcut was not routed through SDK input");
            await fixture.Until(()=>fixture.Items[0].IsFavorite&&!fixture.Busy);
            Check(fixture.Node("capture-library.favorite")?.IsSelected==true&&Preview(fixture)==original&&fixture.Released==0,
                "Favorite shortcut lost selected state or rebuilt the retained preview");
            await fixture.Action("capture-library.focus-actions");
            var request=fixture.View().FocusGroupEntryRequest;
            Check(request is {GroupId:"capture-library.actions"}&&request.TargetId is "capture-library.share" or null,
                "L3 must use existing action-group focus intent");
            Check(Preview(fixture)==original&&fixture.Opened==1&&fixture.Released==0,"Action focus changed media ownership");
        }
    }

    internal static async Task DetailBusyAndUnavailableActions()
    {
        await using(var fixture=new GalleryFixture())
        {
            await fixture.Start();using var host=await OpenEditingSource(fixture);
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.OpenOverride=async(request,_)=>{entered.TrySetResult();await release.Task;return fixture.Items.Single(item=>item.Id==request.Id).Media with{Id=Guid.NewGuid().ToString("N"),Origin=CaptureAttachmentOrigin.SavedLibrary};};
            try
            {
                await fixture.Action("capture-library.viewer.next");await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                foreach(var id in new[]{"share","favorite","edit-copy","reveal","copy","delete"})
                    Check(fixture.Node("capture-library."+id) is {IsBusy:true} or {IsDisabled:true},"Busy detail action remained admitted: "+id);
                Check(!fixture.View().Root.Shortcuts.Any(shortcut=>shortcut.Button is ControllerButton.Y or ControllerButton.LeftBumper or ControllerButton.RightBumper),
                    "Busy detail retained mutation/navigation shortcuts");
            }
            finally{release.TrySetResult();await fixture.Until(()=>!fixture.Busy);}
        }
        await using(var fixture=new GalleryFixture())
        {
            fixture.Items[0]=fixture.Items[0] with{Available=false};
            fixture.OpenOverride=(_,_)=>throw new WidgetCapabilityException("capture_library_not_found","Unavailable");
            await fixture.Start();using var host=WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget,"missing-detail",100);fixture.Host=host;
            using(var page=await host.AcquireAsync("capture-library.entries",0,3))
                Check(page.RouteAction(fixture.Items[0].Id,ControllerButton.A) is not null,"Unavailable entry cannot be reviewed");
            await fixture.Until(()=>fixture.Node("capture-library.detail.unavailable") is not null&&!fixture.Busy);
            Check(fixture.Node("capture-library.share") is null&&fixture.Node("capture-library.edit-copy") is null&&
                fixture.Node("capture-library.reveal") is {IsDisabled:true}&&fixture.Node("capture-library.copy") is {IsDisabled:true}&&
                fixture.Node("capture-library.delete") is {Text:"Remove unavailable entry",IsDisabled:not true},
                "Missing media exposes file actions or hides metadata removal");
            Check(ViewSnapshotValidator.Validate(fixture.View()).Count==0,"Unavailable detail declaration invalid");
        }
    }

    private static void CheckInlineBinding(GalleryFixture fixture,string control,ControllerPrompt prompt,ControllerButton button,string action)
    {
        var snapshot=fixture.View();
        var target=fixture.Node(control)!;
        if(target.Kind==ViewNodeKind.ActionSurface)
            Check(target.ActionId==action && target.Children.Count(node=>node.ControllerPrompt==prompt)==1 &&
                target.Children.All(node=>node.ActionId is null && node.Kind is ViewNodeKind.Icon or ViewNodeKind.ControllerGlyph or ViewNodeKind.Text),
                "Toolbar icon and badge must share one action/focus target: "+control);
        else
        {
            var parent=Flatten(snapshot.Root).Single(node=>node.Children.Any(child=>child.Id==control));
            Check(parent.Children.Count(node=>node.Kind==ViewNodeKind.Button)==1&&parent.Children.Any(node=>node.ControllerPrompt==prompt),
                "Inline hint is not adjacent to its actual control: "+control);
        }
        Check(snapshot.Root.Shortcuts.Count(shortcut=>shortcut.Button==button&&shortcut.ActionId==action)==1,
            "Inline hint has no matching page input binding: "+control);
        Check(Flatten(snapshot.Root).Count(node=>node.ControllerPrompt==prompt)==1,"Inline hint was duplicated in a page footer: "+control);
    }
}
