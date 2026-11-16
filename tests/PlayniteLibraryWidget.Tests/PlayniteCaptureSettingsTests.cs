using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Playnite;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryTests
{
    private const string CaptureSettingsPrefix="playnite-library.captures";
    [TestMethod]
    public async Task CaptureSettingsKeepDraftUntilSaveAndUseExistingModelAndModalFocus()
    {
        var provider=new FakeHost(1);var widget=Create(provider,out TestApplicationService app);
        var current=CaptureSettingsFixture("profile-a");IReadOnlyDictionary<string,JsonElement>? saved=null;
        app.CaptureSettingsHandler=_=>ValueTask.FromResult(current);
        app.CaptureUpdateHandler=(before,patch,_)=>{saved=patch;current=before with{Revision=new string('b',64),Values=before.Values.Concat(patch).GroupBy(pair=>pair.Key).ToDictionary(group=>group.Key,group=>group.Last().Value)};return ValueTask.FromResult(current);};
        await Interactive(widget);await Ready(widget,provider);
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".open","capture-settings"));await widget.WhenCaptureSettingsIdleAsync();
        var initial=Snapshot(widget,900001);
        Assert.AreEqual(0,WidgetTestHost.ValidateWinUiPresentation(initial).Count,string.Join(";",WidgetTestHost.ValidateWinUiPresentation(initial)));
        Assert.IsTrue(Nodes(initial.Root).Any(node=>node.Id==CaptureSettingsPrefix+".modal.close"));
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".set.screenshotsEnabled.on","option"));
        Assert.IsNull(saved,"Changing a draft option must not write provider settings.");
        Assert.IsTrue(widget.RenderState.Value.CaptureSettings.Changes["screenshotsEnabled"].GetBoolean());
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".save","save"));await widget.WhenCaptureSettingsIdleAsync();
        Assert.AreEqual(1,saved!.Count);Assert.IsTrue(saved["screenshotsEnabled"].GetBoolean());
        Assert.AreEqual(0,widget.RenderState.Value.CaptureSettings.Changes.Count);
        Assert.IsTrue(widget.RenderState.Value.CaptureSettings.Message!.Contains("saved",StringComparison.Ordinal));
        if(Environment.GetEnvironmentVariable("PLAYNITE_CAPTURE_FIXTURE") is {Length:>0} file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            var theme=WidgetRail.Tests.RendererFixtureExporter.CompileTheme(Path.Combine(AppContext.BaseDirectory,"styles"),"default.wrss");
            var modal=(WidgetModal)typeof(WidgetRail.Samples.PlayniteLibrary.PlayniteLibraryWidget).GetMethod("CaptureSettingsModal",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(widget,null)!;
            var view=new WidgetView(UI.Stack("capture-settings.parent",UI.Button("Capture settings","open","capture-settings.open"))).WithModal(modal);
            WidgetRail.Tests.RendererFixtureExporter.Write(file,new CaptureModalFixture(view).RenderSnapshot("playnite.capture-settings.fixture",1),theme);
        }
        Assert.IsTrue(await Route(widget,Snapshot(widget,900006),ControllerButton.B,CaptureSettingsPrefix+".modal.close"));
        await WaitIndexed(()=>!widget.RenderState.Value.CaptureSettings.Open);
        await WidgetTestHost.DestroyAsync(widget);
    }
    [TestMethod]
    public async Task CaptureSettingsOldProfileReplyCannotReplaceReconnectedProfile()
    {
        var provider=new FakeHost(1);var widget=Create(provider,out TestApplicationService app);
        var current=CaptureSettingsFixture("old-profile");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource<PlayniteCaptureSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.CaptureSettingsHandler=_=>ValueTask.FromResult(current);
        app.CaptureUpdateHandler=(_,_,_)=>{entered.TrySetResult();return new(release.Task);};
        await Interactive(widget);await Ready(widget,provider);
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".open","open"));await widget.WhenCaptureSettingsIdleAsync();
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".set.screenshotsEnabled.on","on"));
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".save","save"));await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        typeof(WidgetRail.Samples.PlayniteLibrary.PlayniteLibraryWidget).GetMethod("RetireConnectionPresentation",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(widget,null);
        current=CaptureSettingsFixture("new-profile") with{Revision=new string('c',64)};
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".open","open"));
        release.TrySetResult(CaptureSettingsFixture("old-profile") with{Revision=new string('d',64)});
        await widget.WhenCaptureSettingsIdleAsync();
        Assert.IsNull(widget.RenderState.Value.CaptureSettings.Message,"A retired save cannot report success in another profile.");
        Assert.IsFalse(Nodes(Snapshot(widget,900003).Root).Any(node=>node.Text=="Capture settings saved."));
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".set.screenshotsEnabled.on","on"));
        PlayniteCaptureSettings? admitted=null;
        app.CaptureUpdateHandler=(before,_,_)=>{admitted=before;return ValueTask.FromResult(before);};
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".save","save"));await widget.WhenCaptureSettingsIdleAsync();
        Assert.AreEqual("new-profile",admitted?.ConnectionId);Assert.AreEqual(new string('c',64),admitted?.Revision);
        await WidgetTestHost.DestroyAsync(widget);
    }
    [TestMethod]
    public async Task CaptureSettingsMissingAddonIsNormalAndRelatedIntentRejectsWrongProfile()
    {
        var provider=new FakeHost(1);var widget=Create(provider,out TestApplicationService app);
        var profile=Guid.NewGuid().ToString("D");
        app.CaptureSettingsHandler=_=>ValueTask.FromResult(PlayniteCaptureSettings.Unavailable with{Availability="missing",ConnectionId="binding"});
        app.CaptureIntegrationHandler=_=>ValueTask.FromResult<PlayniteCaptureIntegration?>(new(profile,[],[],"binding"));
        await Interactive(widget);await Ready(widget,provider);
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".open","open"));await widget.WhenCaptureSettingsIdleAsync();
        Assert.IsTrue(Nodes(Snapshot(widget,900004).Root).Any(node=>node.Text?.Contains("Install or enable PlayniteAchievements",StringComparison.Ordinal)==true));
        var request=WidgetIntentRequest.Create(WidgetIntentContracts.RelatedItem,JsonSerializer.SerializeToElement(new{packageId="widgetrail.samples.playnite-library",kind=CaptureRelatedIdentity.GameKind,id=CaptureRelatedIdentity.Game(Guid.NewGuid().ToString("D"),Guid.NewGuid())}));
        Assert.IsFalse(await widget.IsIntentAvailableAsync(request));
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".close","close"));
        Assert.AreEqual(WidgetIntentResult.Rejected,await widget.OnIntentAsync(request));
        await WidgetTestHost.DestroyAsync(widget);
    }
    private static PlayniteCaptureSettings CaptureSettingsFixture(string connection)
    {
        var values=new Dictionary<string,JsonElement>();
        foreach(var key in new[]{"screenshotsEnabled","screenshotClean","screenshotWithToast","screenshotFramed","recordingsEnabled","recordingClean","recordingWithToast","recordingFramed","recordingAudio","recordingMicrophone"})values[key]=JsonSerializer.SerializeToElement(false);
        foreach(var key in new[]{"screenshotDirectory","recordingDirectory"})values[key]=JsonSerializer.SerializeToElement("");
        values["screenshotDelaySeconds"]=JsonSerializer.SerializeToElement(0);values["recordingSeconds"]=JsonSerializer.SerializeToElement(30);values["recordingFps"]=JsonSerializer.SerializeToElement(30);
        var options=new Dictionary<string,string[]>();
        foreach(var key in new[]{"screenshotResolution","recordingResolution","recordingQuality","recordingAudioSource"}){values[key]=JsonSerializer.SerializeToElement("Native");options[key]=["Native","Low"];}
        return new("ready",new string('a',64),false,values,options,connection);
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetiredFolderSetupNeverRemovesGrantAfterAnotherProfileAppears(bool rejected)
    {
        var provider=new FakeHost(1);var source=new CaptureLibrarySource(new string('a',32),"Fixture",true,0,0,@"C:\CaptureFixture");
        var sources=new List<CaptureLibrarySource>();var removed=0;
        ConfigureCaptureServices(provider,sources,source,()=>removed++);
        var widget=Create(provider,out TestApplicationService app);var binding="old-profile";
        app.CaptureSettingsHandler=_=>ValueTask.FromResult(CaptureSettingsFixture(binding));
        app.CaptureIntegrationHandler=_=>ValueTask.FromResult<PlayniteCaptureIntegration?>(new(Guid.NewGuid().ToString("D"),[],[],binding));
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var reply=new TaskCompletionSource<PlayniteCaptureSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.CaptureUpdateHandler=(_,_,_)=>{entered.TrySetResult();return new(reply.Task);};
        await Interactive(widget);await Ready(widget,provider);
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".open","open"));await widget.WhenCaptureSettingsIdleAsync();
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".folder.screenshots","choose"));await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        InvokePrivate(widget,"RetireConnectionPresentation");binding="new-profile";
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".open","open"));
        reply.TrySetException(rejected?new PlayniteCaptureSettingsRejectedException("capture_settings_changed"):new IOException("Response lost after save"));
        await widget.WhenCaptureSettingsIdleAsync();
        Assert.AreEqual(0,removed,"A retired or different-profile readback cannot prove a new grant is safe to remove.");
        Assert.AreEqual(source.Id,sources.Single().Id);
        Assert.IsNull(widget.RenderState.Value.CaptureSettings.Message,"Old setup feedback must not appear in the new profile.");
        await WidgetTestHost.DestroyAsync(widget);
    }
    [TestMethod]
    public async Task ExistingGrantSurvivesRejectedSetupAndDisconnectWorksWhilePlayniteIsOffline()
    {
        var provider=new FakeHost(1);var source=new CaptureLibrarySource(new string('b',32),"Existing",true,2,0,@"C:\ExistingCaptures");
        var sources=new List<CaptureLibrarySource>{source};var removed=0;
        ConfigureCaptureServices(provider,sources,source,()=>removed++);
        var widget=Create(provider,out TestApplicationService app);
        app.CaptureSettingsHandler=_=>ValueTask.FromResult(CaptureSettingsFixture("profile"));
        app.CaptureIntegrationHandler=_=>ValueTask.FromResult<PlayniteCaptureIntegration?>(new(Guid.NewGuid().ToString("D"),[],[],"profile"));
        app.CaptureUpdateHandler=(_,_,_)=>ValueTask.FromException<PlayniteCaptureSettings>(new PlayniteCaptureSettingsRejectedException("capture_settings_editing"));
        await Interactive(widget);await Ready(widget,provider);
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".open","open"));await widget.WhenCaptureSettingsIdleAsync();
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".folder.screenshots","choose"));await widget.WhenCaptureSettingsIdleAsync();
        Assert.AreEqual(0,removed,"Choosing an existing root cannot make it a cleanup candidate.");
        app.CaptureSettingsHandler=_=>ValueTask.FromException<PlayniteCaptureSettings>(new PlayniteBridgeTransportException(false));
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".refresh","refresh"));await widget.WhenCaptureSettingsIdleAsync();
        Assert.IsTrue(Nodes(Snapshot(widget,900007).Root).Any(node=>node.ActionId==CaptureSettingsPrefix+".disconnect."+source.Id));
        await widget.OnActionAsync(new(CaptureSettingsPrefix+".disconnect."+source.Id,"disconnect"));await widget.WhenCaptureSettingsIdleAsync();
        Assert.AreEqual(1,removed);Assert.AreEqual(0,sources.Count);
        await WidgetTestHost.DestroyAsync(widget);
    }
    private static void ConfigureCaptureServices(FakeHost host,List<CaptureLibrarySource> sources,CaptureLibrarySource chosen,Action removed)
    {
        host.ConfigureServices=builder=>builder
            .WithHandler(WidgetPermissionCapabilities.Check,(query,_)=>ValueTask.FromResult(new WidgetPermissionStatuses(query.CapabilityIds.Select(id=>new WidgetPermissionStatus(id,WidgetPermissionState.Granted)).ToArray())))
            .WithResponse(WidgetPermissionCapabilities.Request,new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted))
            .WithHandler(WidgetCaptureLibrarySourceCapabilities.List,(_,_)=>ValueTask.FromResult(new CaptureLibrarySources(sources.ToArray())))
            .WithHandler(WidgetCaptureLibrarySourceCapabilities.Choose,(_,_)=>{if(!sources.Any(item=>item.Id==chosen.Id))sources.Add(chosen);return ValueTask.FromResult(new CaptureLibrarySourceChoice(chosen));})
            .WithHandler(WidgetCaptureLibrarySourceCapabilities.Remove,(request,_)=>{removed();sources.RemoveAll(item=>item.Id==request.SourceId);return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));});
    }
    private sealed class CaptureModalFixture(WidgetView view):Widget {public override WidgetView Render()=>view;}
}
