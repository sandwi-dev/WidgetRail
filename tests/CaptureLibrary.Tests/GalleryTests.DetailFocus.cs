using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task DetailFocusSurvivesReopen()
    {
        await using var fixture=new GalleryFixture();await fixture.Start();using var host=await OpenEditingSource(fixture);
        await fixture.Action("capture-library.focus-actions");
        var request=fixture.View().FocusGroupEntryRequest!;
        Check(ViewSnapshotValidator.Validate(fixture.View()).Count==0,"Initial detail focus request was invalid");
        await fixture.Action("capture-library.back");await fixture.Until(()=>fixture.Collection is not null&&!fixture.Busy);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.OpenOverride=async(value,_)=>{entered.TrySetResult();await release.Task;return fixture.Items.Single(item=>item.Id==value.Id).Media with{Id=Guid.NewGuid().ToString("N"),Origin=CaptureAttachmentOrigin.SavedLibrary};};
        try
        {
            using(var lease=await host.AcquireAsync("capture-library.entries",0,3))
                Check(lease.RouteAction(fixture.Items[1].Id,ControllerButton.A) is not null,"Reopen was not admitted");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var opening=fixture.View();
            var errors=ViewSnapshotValidator.Validate(opening);
            Check(errors.Count==0,"Retained action focus invalidated loading detail: "+string.Join(",",errors.Select(error=>error.Code+"@"+error.Path)));
            Check(opening.FocusGroupEntryRequest?.RequestId==request.RequestId,"Loading invented a new focus intent");
        }
        finally{release.TrySetResult();await fixture.Until(()=>Preview(fixture) is not null&&!fixture.Busy);}
        Check(ViewSnapshotValidator.Validate(fixture.View()).Count==0,"Loaded detail focus request became invalid");
        await fixture.Action("capture-library.focus-actions");
        Check(fixture.View().FocusGroupEntryRequest is {GroupId:"capture-library.actions",TargetId:null} next&&next.RequestId>request.RequestId,
            "L3 must enter the current remembered/default action using one new SDK intent");
    }

    internal static async Task DetailFocusSurvivesDraftSave()
    {
        var draft=new CaptureAttachment(new string('f',32),"video/mp4",1280,720,65536,5,long.MaxValue)
            {Origin=CaptureAttachmentOrigin.LibraryRecordingDraft,Properties=new(1280,720,30,150,Encoder:CaptureEncoderMode.Hardware)};
        await using var fixture=new GalleryFixture((builder,owner)=>builder
            .WithResponse(WidgetCaptureCapabilities.Validate,new CaptureConfigurationValidation(true))
            .WithResponse(WidgetCaptureCapabilities.Start,new WindowCaptureTicket(new string('e',32)))
            .WithResponse(WidgetCaptureCapabilities.Status,new WindowCaptureStatus(new string('e',32),WindowCapturePhase.Ready,draft))
            .WithHandler(WidgetCaptureLibraryCapabilities.SaveCurrent,(_,_)=>
            {var saved=new CaptureLibraryItem(draft.Id,draft with{Origin=CaptureAttachmentOrigin.SavedLibrary},1000);owner.Items.Add(saved);return ValueTask.FromResult(saved);}));
        await fixture.Start();await fixture.Action("capture-library.video");await fixture.Until(()=>Preview(fixture)?.Id==draft.Id&&!fixture.Busy);
        await fixture.Action("capture-library.focus-actions");var request=fixture.View().FocusGroupEntryRequest!;
        await fixture.Action("capture-library.save");await fixture.Until(()=>Preview(fixture)?.Origin==CaptureAttachmentOrigin.SavedLibrary&&!fixture.Busy);
        var savedView=fixture.View();var errors=ViewSnapshotValidator.Validate(savedView);
        Check(errors.Count==0,"Removing Save after publication retained an invalid focus target: "+string.Join(",",errors.Select(error=>error.Code+"@"+error.Path)));
        Check(savedView.FocusGroupEntryRequest?.RequestId==request.RequestId&&fixture.Node("capture-library.save") is null,
            "Save changed focus intent identity or kept the obsolete draft action");
    }
}
