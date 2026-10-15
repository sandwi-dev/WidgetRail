using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.GameHelp;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.GameHelpWidget.Tests;

[TestClass]
public sealed class GameHelpTests
{
    [TestMethod]
    public async Task SavedCapturePagingCanPassTheFormerTwoHundredItemLimit()
    {
        var offsets=new List<int>();
        var media=new CaptureAttachment(new string('A',32),"image/png",100,100,1000,0,long.MaxValue);
        var client=new WidgetTestHostServicesBuilder()
            .WithResponse(WidgetPermissionCapabilities.Request,new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted))
            .WithHandler(WidgetCaptureLibraryCapabilities.List,(request,_)=>
            {
                offsets.Add(request.Offset);
                return ValueTask.FromResult(new CaptureLibraryPage([new((request.Offset+1).ToString("x32"),media,1)],request.Offset,240,240000));
            }).Build();
        var widget=WidgetTestHost.Attach(new WidgetRail.Samples.GameHelp.GameHelpWidget(new FakeService()),client);
        try
        {
            await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await Until(()=>Walk(Snapshot(widget).Root).Any(n=>n.Id=="help.composer"));
            for(var offset=0;offset<=220;offset+=20)
            {
                await widget.OnActionAsync(new(offset==0?"library.open":"library.next","help.library.next"));
                var id="help.library.item."+(offset+1).ToString("x32");
                await Until(()=>Walk(Snapshot(widget).Root).Any(n=>n.Id==id && n.IsBusy!=true));
            }
            Assert.AreEqual(220,offsets.Last());
            Assert.AreEqual(0,ViewSnapshotValidator.Validate(Snapshot(widget)).Count);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }
    [TestMethod]
    public async Task SavedCaptureSelectionOpensContextPreviewAndWaitsForExplicitSend()
    {
        var service = new FakeService();
        var media = new CaptureAttachment(new string('A', 32), "image/png", 1920, 1080, 1000, 0, long.MaxValue) { Origin = CaptureAttachmentOrigin.SavedLibrary };
        var id = new string('B', 32); var requested = CaptureImageResolution.Original;
        var widget = WidgetTestHost.Attach(new WidgetRail.Samples.GameHelp.GameHelpWidget(service), new WidgetTestHostServicesBuilder()
            .WithResponse(WidgetPermissionCapabilities.Request, new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted))
            .WithResponse(WidgetCaptureLibraryCapabilities.List, new CaptureLibraryPage([new(id, media, 1)], 0, 1, 1000))
            .WithHandler(WidgetCaptureLibraryCapabilities.Open, (request, _) => { requested = request.Resolution; return ValueTask.FromResult(media); })
            .WithResponse(WidgetCaptureLibraryCapabilities.Release, new WidgetCapabilityAcknowledgement(true)).Build());
        try
        {
            await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Id == "help.composer"));
            await widget.OnActionAsync(new("draft", "help.composer") { CommittedText = "How do I get past this boss?" });
            await widget.OnActionAsync(new("library.open", "help.add-library"));
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Id == "help.library.item." + id && n.IsBusy != true));
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(Snapshot(widget)).Count);
            await widget.OnActionAsync(new("library.select." + id, "help.library.item." + id));
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Id == "help.preview-media"));
            Assert.AreEqual(CaptureImageResolution.Context, requested);
            Assert.AreEqual(0, service.Requests.Count, "Selecting a saved capture uploaded it before confirmation");
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(Snapshot(widget)).Count);
            await widget.OnActionAsync(new("preview.send", "help.preview-send"));
            await Until(() => service.Requests.Count == 1);
            Assert.AreEqual(media, service.Requests[0].Attachment);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }
    [TestMethod]
    public async Task SourceIntentsPreferExistingBrowserOrYouTubeSurfaceAndPreserveRedirects()
    {
        const string redirect = "https://vertexaisearch.cloud.google.com/grounding-api-redirect/fixture";
        var service = new FakeService { Reply = _ => Task.FromResult(new GameHelpAnswer("Hint", [], "", [], [],
            [new("Video", "https://www.youtube.com/watch?v=abcdefghijk&t=42"), new("Guide", "https://example.com/guide"), new("youtube.com", redirect)])) };
        var widget = Attach(service, []);
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Id == "help.composer"));
            await widget.OnActionAsync(new("starter.0", "help.starter.0"));
            await widget.OnActionAsync(new("ask.plain", "help.answer-plain"));
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Id == "help.message.2.sources-toggle"));
            await widget.OnActionAsync(new("sources.2", "help.message.2.sources-toggle"));
            var intents = Walk(Snapshot(widget).Root).Where(n => n.Id.StartsWith("help.message.2.source.")).Select(n => n.Intent!).ToArray();
            Assert.AreEqual(3, intents.Length);
            Assert.IsTrue(intents.All(intent => intent.Routing == WidgetIntentRouting.WidgetPreferred && intent.Presentation == WidgetIntentPresentation.PreferExistingSurface));
            Assert.AreEqual(WidgetIntentContracts.OpenVideo, intents[0].ContractId);
            Assert.AreEqual(42, intents[0].Payload.GetProperty("startTimeSeconds").GetDouble());
            Assert.AreEqual(WidgetIntentContracts.OpenWebPage, intents[1].ContractId);
            Assert.AreEqual(redirect, intents[2].Payload.GetProperty("url").GetString());
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task SearchAndSpoilerPreferencesSurviveWidgetRecreationAndNewConversation()
    {
        var store = new WidgetTestPrivateState();
        WidgetHostServices Services() => new WidgetTestHostServicesBuilder().WithPrivateState(store)
            .WithResponse(WidgetPermissionCapabilities.Request, new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted)).Build();
        var first = WidgetTestHost.Attach(new WidgetRail.Samples.GameHelp.GameHelpWidget(new FakeService()), Services());
        try
        {
            await WidgetTestHost.InitializeAsync(first);
            await WidgetTestHost.SetLifecycleStateAsync(first, WidgetLifecycleState.Interactive);
            await Until(() => Walk(Snapshot(first).Root).Any(node => node.Id == "help.composer"));
            await first.OnActionAsync(new("grounding.on", "help.grounding"));
            await first.OnActionAsync(new("spoiler.2", "help.spoilers"));
            await Until(() => store.Json?.Contains("Full solution") == true);
            await first.OnActionAsync(new("new.confirm", "help.new"));
        }
        finally { await WidgetTestHost.DestroyAsync(first); }
        var service = new FakeService();
        var second = WidgetTestHost.Attach(new WidgetRail.Samples.GameHelp.GameHelpWidget(service), Services());
        try
        {
            await WidgetTestHost.InitializeAsync(second);
            await WidgetTestHost.SetLifecycleStateAsync(second, WidgetLifecycleState.Interactive);
            await Until(() => Walk(Snapshot(second).Root).Any(node => node.Id == "help.composer"));
            await second.OnActionAsync(new("starter.0", "help.starter.0"));
            await second.OnActionAsync(new("ask.plain", "help.answer-plain"));
            await Until(() => service.Requests.Count == 1);
            Assert.IsTrue(service.Requests[0].WebGrounding);
            Assert.AreEqual("Full solution", service.Requests[0].SpoilerPreference);
            var before=store.Revision;
            await second.OnActionAsync(new("grounding.off", "help.grounding"));
            await Until(() => store.Revision > before);
            Assert.IsFalse(JsonSerializer.Deserialize<GameHelpPreferences>(store.Json!)!.Grounding);
        }
        finally { await WidgetTestHost.DestroyAsync(second); }
    }

    [TestMethod]
    public async Task YouTubeSearchDeclaresOneWebFallbackAndSelectsItOnlyForDefiniteRejection()
    {
        var service = new FakeService { Reply = _ => Task.FromResult(new GameHelpAnswer("Hint", [], "", [], ["boss guide"], [])) };
        var widget = Attach(service, []);
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Id == "help.composer"));
            await widget.OnActionAsync(new("starter.0", "help.starter.0"));
            await widget.OnActionAsync(new("ask.plain", "help.answer-plain"));
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Id == "help.message.2.youtube.0"));
            var snapshot = Snapshot(widget);
            var request = Walk(snapshot.Root).Single(n => n.Id == "help.message.2.youtube.0").Intent!;
            Assert.AreEqual(WidgetIntentContracts.SearchVideo, request.ContractId);
            Assert.AreEqual("boss guide", request.Payload.GetProperty("query").GetString());
            Assert.AreEqual(WidgetIntentContracts.OpenWebPage, request.Fallback!.ContractId);
            Assert.AreEqual(WidgetIntentRouting.WidgetPreferred, request.Fallback.Routing);
            Assert.AreEqual(72, snapshot.ProtocolVersion);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
            foreach (var status in Enum.GetValues<WidgetIntentStatus>())
                Assert.AreEqual(status is WidgetIntentStatus.Unavailable or WidgetIntentStatus.Rejected,
                    await widget.OnIntentCompletedAsync(new(Guid.NewGuid().ToString("N"), request, status)));
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task SolutionsExpandLocallyAndSuggestedReplySendsItsMessageWithReveal()
    {
        var service = new FakeService { Reply = _ => Task.FromResult(new GameHelpAnswer("Look for a side entrance.", [], "",
            [new("Another hint", "Give me another hint without revealing the solution.")], [], [], DetailedSolution: "Use the office door, then cross the cubicles.")) };
        var widget = Attach(service, []);
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Id == "help.composer"));
            await widget.OnActionAsync(new("starter.0", "help.starter.0"));
            await widget.OnActionAsync(new("ask.plain", "help.answer-plain"));
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Id == "help.message.2.solution-toggle"));
            Assert.IsFalse(Walk(Snapshot(widget).Root).Any(n => n.Text == "Use the office door, then cross the cubicles."));
            await widget.OnActionAsync(new("solution.2", "help.message.2.solution-toggle"));
            Assert.IsTrue(Walk(Snapshot(widget).Root).Any(n => n.Text == "Use the office door, then cross the cubicles."));
            await widget.OnActionAsync(new("solution.2", "help.message.2.solution-toggle"));
            Assert.IsFalse(Walk(Snapshot(widget).Root).Any(n => n.Id == "help.message.2.solution"));
            Assert.AreEqual(1, service.Requests.Count, "Expansion/collapse is local, not an AI request.");
            var before = Snapshot(widget).ScrollRevealRequest!.RequestId;
            await widget.OnActionAsync(new("suggest.2.0", "help.message.2.suggest.0"));
            await Until(() => service.Requests.Count == 2);
            Assert.AreEqual("Give me another hint without revealing the solution.", service.Requests[1].Conversation.Last().Text);
            var snapshot = Snapshot(widget);
            Assert.AreEqual("help.message.3", snapshot.ScrollRevealRequest!.TargetId);
            Assert.IsTrue(snapshot.ScrollRevealRequest.RequestId > before);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public void RevealIsVersionedScopedAndCannotTargetAnotherScrollContainer()
    {
        var view = new WidgetView(UI.Stack("root",
            UI.VerticalScroll("first", UI.Text("First", "one")),
            UI.VerticalScroll("second", UI.Text("Second", "two"))))
            { ScrollRevealRequest = new(1, "first", "one") };
        var snapshot = view.CreateSnapshot("fixture", 1);
        Assert.AreEqual(ProtocolConstants.ScrollRevealVersion, snapshot.ProtocolVersion);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        Assert.IsTrue(ViewSnapshotValidator.Validate(snapshot with { ScrollRevealRequest = new(2, "first", "two") }).Count > 0);
        Assert.IsTrue(ViewSnapshotValidator.Validate(snapshot with { ScrollRevealRequest = new(0, "first", "one") }).Count > 0);
        Assert.IsTrue(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 69 }).Count > 0);
        var modal = view.WithModal(new("dialog", "Dialog", UI.Button("Close", "close", "close"), "close", "close"));
        Assert.IsNull(modal.ScrollRevealRequest);
    }

    [TestMethod]
    [DataRow("https://youtu.be/abcDEF123_-?t=1m23s", 83d, true)]
    [DataRow("https://www.youtube.com/watch?v=abcDEF123_-&start=42.5", 42.5d, true)]
    [DataRow("https://youtu.be/abcDEF123_-", -1d, true)]
    [DataRow("https://youtu.be/abcDEF123_-?t=90000", -1d, false)]
    public async Task CitedVideoLinksPreserveProvidedTimeOrKeepTheOriginalWebLink(string url, double seconds, bool video)
    {
        var service = new FakeService { Reply = _ => Task.FromResult(new GameHelpAnswer("Here is a cited guide", [], "", [], [],
            [new("Video guide", url)], new(new string('a', 32), ProviderDocumentReference.RestrictedHtml))) };
        var widget = Attach(service, []);
        try
        {
            await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => Walk(Snapshot(widget).Root).Any(node => node.Id == "help.composer"));
            await widget.OnActionAsync(new("grounding.on", "help.grounding"));
            await widget.OnActionAsync(new("starter.0", "help.starter.0"));
            await widget.OnActionAsync(new("ask.plain", "help.answer-plain"));
            await Until(() => Walk(Snapshot(widget).Root).Any(node => node.Id == "help.message.2.sources-toggle"));
            await widget.OnActionAsync(new("sources.2", "help.message.2.sources-toggle"));
            var intent = Walk(Snapshot(widget).Root).Single(node => node.Id == "help.message.2.source.0").Intent!;
            Assert.AreEqual(video ? WidgetIntentContracts.OpenVideo : WidgetIntentContracts.OpenWebPage, intent.ContractId);
            if (!video) Assert.AreEqual(url, intent.Payload.GetProperty("url").GetString());
            else if (seconds < 0) Assert.IsFalse(intent.Payload.TryGetProperty("startTimeSeconds", out _));
            else Assert.AreEqual(seconds, intent.Payload.GetProperty("startTimeSeconds").GetDouble());
            Assert.AreEqual(0, WinUiPresentationContract.Validate(Snapshot(widget)).Count);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task ApplicationOwnedServiceDoesNotRequireAGeminiHostCapability()
    {
        var service = new FakeService { Reply = _ => Task.FromResult(new GameHelpAnswer("Application answer", [], "", [], [], [])) };
        var widget = Attach(service, []);
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Id == "help.composer"));
            await widget.OnActionAsync(new("starter.0", "help.starter.0"));
            await widget.OnActionAsync(new("ask.plain", "help.answer-plain"));
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Text == "Application answer"));
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod]
    public async Task LateAnswerCannotReplaceNewConversationOrItsBusyState()
    {
        var first = new TaskCompletionSource<GameHelpAnswer>();
        var second = new TaskCompletionSource<GameHelpAnswer>();
        var service = new FakeService { Reply = request => request.Conversation[0].Text.Contains("area") ? first.Task : second.Task };
        var widget = Attach(service, []);
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Id == "help.composer"));
            await widget.OnActionAsync(new("starter.0", "help.starter.0"));
            await widget.OnActionAsync(new("ask.plain", "help.answer-plain"));
            await Until(() => service.Requests.Count == 1);
            await widget.OnActionAsync(new("new", "help.new"));
            await widget.OnActionAsync(new("new.confirm", "help.new-proceed"));
            await widget.OnActionAsync(new("starter.1", "help.starter.1"));
            await widget.OnActionAsync(new("ask.plain", "help.answer-plain"));
            first.SetResult(new("Retired answer", [], "", [], [], []));
            await Until(() => service.Requests.Count == 2);
            Assert.IsFalse(Walk(Snapshot(widget).Root).Any(n => n.Text == "Retired answer"));
            Assert.IsTrue(Walk(Snapshot(widget).Root).Any(n => n.Id == "help.answer-cancel"));
            second.SetResult(new("Current answer", [], "", [], [], []));
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Text == "Current answer"));
        }
        finally { first.TrySetCanceled(); second.TrySetCanceled(); await WidgetTestHost.DestroyAsync(widget); }
    }
    [TestMethod]
    public async Task CaptureOptionsNeedNoApplicationListOrSelection()
    {
        var service=new FakeService();
        var widget=Attach(service,[]);
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            Snapshot(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await Until(()=>Walk(Snapshot(widget).Root).Any(n=>n.Id=="help.composer"));
            Assert.AreEqual(0,ViewSnapshotValidator.Validate(Snapshot(widget)).Count);
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
        widget=Attach(service,[new("window-one",new string('a',120),new string('b',240),false)]);
        try
        {
            await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await Until(()=>Walk(Snapshot(widget).Root).Any(n=>n.Id=="help.composer"));
            await widget.OnActionAsync(new("context.open", "help.context"));
            await Until(()=>Walk(Snapshot(widget).Root).Any(n=>n.Id=="help.add-image"));
            Assert.IsFalse(Walk(Snapshot(widget).Root).Any(n=>n.Id is "help.application" or "help.refresh"));
            Assert.AreEqual(0,ViewSnapshotValidator.Validate(Snapshot(widget)).Count);
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
    }
    [TestMethod]
    public async Task CaptureNeedsPreviewSendBeforeGeminiAndUsesReusableMedia()
    {
        var service=new FakeService();
        service.Reply = _ => service.Requests.Count == 1 ? Task.FromException<GameHelpAnswer>(new GameHelpException("connection_failed", "Fixture connection failure")) :
            Task.FromResult(new GameHelpAnswer("Useful answer", [], "", [new("Next step", "What should I try next?")], [], []));
        var ticket=new WindowCaptureTicket(new string('a',32));
        var data=new byte[32];var attachment=new CaptureAttachment(new string('b',32),"image/png",64,48,data.Length,0,DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds())
            { SourceApplication = new("007 First Light", "007FirstLight"), Properties = new(64,48) };
        var host=new WidgetTestHostServicesBuilder()
            .WithResponse(WidgetPermissionCapabilities.Request, new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted))
            .WithHandler(WidgetCaptureCapabilities.Start, (request, _) =>
            {
                Assert.AreEqual(new ScreenshotOptions(1920, 1080), request.Screenshot);
                Assert.IsNull(request.Recording);
                Assert.AreEqual((long)CaptureLimits.MaximumContextBytes, request.MaximumBytes);
                return ValueTask.FromResult(ticket);
            })
            .WithResponse(WidgetCaptureCapabilities.Status,new WindowCaptureStatus(ticket.RequestId,WindowCapturePhase.Ready,attachment))
            .WithResponse(WidgetCaptureCapabilities.Read,new CaptureReadChunk(0,data,true))
            .WithResponse(WidgetCaptureCapabilities.Cancel,new WidgetCapabilityAcknowledgement(true))
            .WithResponse(WidgetCaptureCapabilities.Discard,new WidgetCapabilityAcknowledgement(true)).Build();
        var widget=WidgetTestHost.Attach(new WidgetRail.Samples.GameHelp.GameHelpWidget(service),host);
        try
        {
            await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await Until(()=>Walk(Snapshot(widget).Root).Any(n=>n.Id=="help.composer"));
            await widget.OnActionAsync(new("starter.0","help.starter.0"));
            Assert.IsTrue(Walk(Snapshot(widget).Root).Any(n=>n.Text=="Help me understand this area"));
            await widget.OnActionAsync(new("capture.image","help.capture-image"));
            await Until(()=>Snapshot(widget).Root.Kind==ViewNodeKind.ModalLayer);
            Assert.AreEqual(0,service.Requests.Count);
            Assert.AreEqual(ProtocolConstants.ConfigurableCaptureVersion,Snapshot(widget).ProtocolVersion);
            Assert.IsTrue(Walk(Snapshot(widget).Root).Any(n=>n.Kind==ViewNodeKind.MediaPlayer&&n.MediaPlayer!.Source.Attachment==attachment));
            Assert.AreEqual(ViewNodeKind.TextEntry, Walk(Snapshot(widget).Root).Single(n => n.Id == "help.preview-question").Kind);
            await widget.OnActionAsync(new("preview.question", "help.preview-question") { CommittedText = "How do I reach the office?" });
            Assert.AreEqual(0, WinUiPresentationContract.Validate(Snapshot(widget)).Count);
            await widget.OnActionAsync(new("preview.send","help.preview-send"));
            await Until(()=>Walk(Snapshot(widget).Root).Any(n=>n.Text=="Fixture connection failure"));
            await widget.OnActionAsync(new("answer.retry","help.retry"));
            await Until(()=>Walk(Snapshot(widget).Root).Any(n=>n.Text=="Useful answer"));
            Assert.AreEqual(2,service.Requests.Count);Assert.AreEqual(attachment,service.Requests[0].Attachment);
            Assert.AreEqual(attachment,service.Requests[1].Attachment, "Retry must retain the explicitly sent capture.");
            Assert.AreEqual("007 First Light",service.Requests[0].ApplicationName, "Use captured application metadata, not foreground order when sending.");
            Assert.AreEqual("007FirstLight",service.Requests[0].WindowTitle);
            Assert.AreEqual(service.Requests[0].ApplicationName,service.Requests[1].ApplicationName, "Retry retains capture identity.");
            Assert.AreEqual("How do I reach the office?", service.Requests[0].Conversation.Last().Text);
            Assert.AreEqual(1, service.Requests[0].Conversation.Count(turn => turn.Role == "user"), "Editing replaces the pending turn instead of duplicating it.");
            Assert.AreEqual(0,ViewSnapshotValidator.Validate(Snapshot(widget)).Count);
            await widget.OnActionAsync(new("capture.image", "help.capture-image"));
            await Until(() => Walk(Snapshot(widget).Root).Any(n => n.Id == "help.preview-question"));
            await widget.OnActionAsync(new("preview.question", "help.preview-question") { CommittedText = "Which doorway should I use?" });
            Assert.IsTrue(Walk(Snapshot(widget).Root).Any(n => n.Text == "How do I reach the office?"), "Editing a later capture must preserve the earlier answered question.");
            await widget.OnActionAsync(new("preview.discard", "help.preview-discard"));
            await widget.OnActionAsync(new("suggest.2.0","help.message.2.suggest.0"));
            await Until(()=>service.Requests.Count==3);
            Assert.IsTrue(service.Requests[2].Conversation.Any(turn=>turn.Role=="user"&&turn.Text=="What should I try next?"));
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
    }
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task LightweightVideoPrefersAnAdvertisedHardwareEncoder(bool hardwareAvailable)
    {
        var service = new FakeService();
        var expected = hardwareAvailable ? CaptureEncoderMode.Hardware : CaptureEncoderMode.Software;
        var profiles = new List<CaptureRecordingProfile> { new("software",1280,720,15,1_000_000,6_000_000,CaptureEncoderMode.Software) };
        if (hardwareAvailable) profiles.Add(new("hardware",1280,720,15,1_000_000,6_000_000,CaptureEncoderMode.Hardware));
        var ticket = new WindowCaptureTicket(new string('c',32));
        var media = new CaptureAttachment(new string('d',32),"video/mp4",1280,720,1000,5,DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()) {
            Properties = new(1280,720,15,75,0,expected) };
        RecordingOptions? requested = null;
        var host = new WidgetTestHostServicesBuilder()
            .WithResponse(WidgetPermissionCapabilities.Request, new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted))
            .WithResponse(WidgetCaptureCapabilities.Capabilities, new CaptureCapabilities(true,[CaptureImageFormat.Png],profiles,[CaptureAudioMode.None],true,120))
            .WithHandler(WidgetCaptureCapabilities.Start, (request, _) => { requested = request.Recording; return ValueTask.FromResult(ticket); })
            .WithResponse(WidgetCaptureCapabilities.Status,new WindowCaptureStatus(ticket.RequestId,WindowCapturePhase.Ready,media))
            .WithResponse(WidgetCaptureCapabilities.Discard,new WidgetCapabilityAcknowledgement(true)).Build();
        var widget = WidgetTestHost.Attach(new WidgetRail.Samples.GameHelp.GameHelpWidget(service),host);
        try
        {
            await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await Until(()=>Walk(Snapshot(widget).Root).Any(n=>n.Id=="help.composer"));
            await widget.OnActionAsync(new("capture.video","help.capture-video"));
            await Until(()=>Snapshot(widget).Root.Kind==ViewNodeKind.ModalLayer);
            Assert.AreEqual(new RecordingOptions(1280,720,15,3_000_000,5,expected),requested);
            Assert.AreEqual(0,service.Requests.Count);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }
    [TestMethod]
    public void GroundedSourcesComeOnlyFromAnnotationsAndSearchesStaySeparate()
    {
        var answer=JsonSerializer.Serialize(new{hint="Useful answer",detailedSolution="",observations=new[]{"Possible bonfire name"},uncertainty="Location is uncertain",replyOptions=new[]{new { label="Boss help", message="Help me approach this boss." }},searchQueries=new[]{"Elden Ring bonfire guide"}});
        var response=JsonSerializer.SerializeToUtf8Bytes(new{status="completed",steps=new[]{new{type="model_output",content=new[]{new{type="text",text=answer,annotations=new[]{new{type="url_citation",url="https://example.com/guide",title="Guide"},new{type="url_citation",url="file:///C:/private",title="bad"}}}}}}});
        Assert.Throws<GameHelpException>(() => GeminiGameHelpClient.ParseResponse(response, true), "Citations must not lose their required search suggestions.");
        var document = System.Text.Json.Nodes.JsonNode.Parse(response)!;
        document["steps"]!.AsArray().Insert(0, System.Text.Json.JsonSerializer.SerializeToNode(new { type = "google_search_result", result = new[] { new { search_suggestions = "<div><a href=\"https://www.google.com/search?q=fixture\">Fixture search</a></div>" } } }));
        response = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString());
        var grounded=GeminiGameHelpClient.ParseResponse(response,true);
        Assert.AreEqual(1, grounded.SearchSuggestionsHtml!.Count);
        Assert.IsFalse(JsonSerializer.Serialize(grounded).Contains("<div>"), "Provider markup remains host-only.");
        Assert.AreEqual(1,grounded.Sources.Count);Assert.AreEqual("https://example.com/guide",grounded.Sources[0].Url);
        Assert.AreEqual(0,GeminiGameHelpClient.ParseResponse(response,false).Sources.Count);
        Assert.AreEqual("Elden Ring bonfire guide",grounded.SearchQueries.Single());
    }
    [TestMethod]
    public void ManifestAndMalformedResponsesAreValidated()
    {
        var style = WidgetRail.WidgetStyling.WrssPackageLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "default.wrss"));
        Assert.IsTrue(style.IsValid, string.Join("; ", style.Diagnostics));
        var compiledStyle = WidgetRail.WidgetStyling.WrssThemeCompiler.Compile(style);
        Assert.IsTrue(compiledStyle.IsValid, string.Join("; ", compiledStyle.Diagnostics));
        var manifest=ManifestJson.Deserialize(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"manifest.json")));
        Assert.AreEqual(0,WidgetManifestValidator.Validate(manifest).Count,string.Join("; ",WidgetManifestValidator.Validate(manifest)));
        Assert.Throws<GameHelpException>(()=>GeminiGameHelpClient.ParseResponse("{\"steps\":[]}"u8.ToArray(),false));
    }
    [TestMethod]
    public async Task SetupIsSeparateAndChatUsesScopedControllerShortcuts()
    {
        var service = new FakeService { Configured = false };
        var widget = Attach(service, []);
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => Walk(Snapshot(widget).Root).All(node => node.Id != "help.setup-loading"));
            var setup = Snapshot(widget);
            Assert.AreEqual("help.key", setup.InitialFocusId);
            Assert.IsFalse(Walk(setup.Root).Any(node => node.Id is "help.settings" or "help.new" or "help.context"));
            Assert.IsFalse(Walk(setup.Root).Any(node => node.Id == "help.error"));
            Assert.AreEqual(WidgetIntentRouting.Windows, Walk(setup.Root).Single(node => node.Id == "help.key-link").Intent!.Routing);
            AssertValid(setup);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }

        widget = Attach(new FakeService(), []);
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => Walk(Snapshot(widget).Root).Any(node => node.Id == "help.composer"));
            var chat = Snapshot(widget);
            Assert.IsTrue(chat.Root.Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.X && shortcut.ActionId == "context.open"));
            Assert.IsTrue(chat.Root.Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.LeftBumper && shortcut.ActionId == "new"));
            Assert.IsTrue(Walk(chat.Root).Any(node => node.Id == "help.header-new"));
            Assert.IsTrue(chat.Root.Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.Y && shortcut.ActionId == "settings"));
            Assert.IsTrue(chat.Root.Shortcuts.Any(shortcut => shortcut.Button == ControllerButton.RightStick && shortcut.ActionId == "compose.focus"));
            await widget.OnActionAsync(new("compose.focus", "help.root"));
            Assert.AreEqual("help.compose-focus", Snapshot(widget).FocusGroupEntryRequest?.GroupId);
            AssertValid(Snapshot(widget));
            await widget.OnActionAsync(new("context.open", "help.context"));
            Assert.AreEqual("help.capture-options.scope", Snapshot(widget).ActiveInputScopeId);
            AssertValid(Snapshot(widget));
            await widget.OnActionAsync(new("context.close", "help.capture-options.close"));
            await widget.OnActionAsync(new("settings", "help.settings"));
            Assert.AreEqual("help.spoilers", Snapshot(widget).InitialFocusId);
            AssertValid(Snapshot(widget));
            await widget.OnActionAsync(new("new", "help.new"));
            Assert.AreEqual("help.new-cancel", Snapshot(widget).InitialFocusId);
            AssertValid(Snapshot(widget));
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }

        static void AssertValid(ViewSnapshot snapshot)
        {
            var diagnostics = ViewSnapshotValidator.Validate(snapshot);
            Assert.AreEqual(0, diagnostics.Count, string.Join("; ", diagnostics));
        }
    }

    [TestMethod]
    public async Task ResultNotificationCompletesInBackgroundAndOpensWithoutRepeatingQuestion()
    {
        var completion = new TaskCompletionSource<GameHelpAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeService { Reply = _ => completion.Task };
        WidgetNotification? notice = null;
        var services = new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetNotificationCapabilities.Publish, (command, _) => { notice = command.Notification; return ValueTask.FromResult(new WidgetNotificationReceipt(true)); }).Build();
        var widget = WidgetTestHost.Attach(new WidgetRail.Samples.GameHelp.GameHelpWidget(service), services);
        try
        {
            await WidgetTestHost.InitializeAsync(widget);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Until(() => Walk(Snapshot(widget).Root).Any(node => node.Id == "help.composer"));
            await widget.OnActionAsync(new("draft", "help.composer") { CommittedText = "Synthetic question" });
            await widget.OnActionAsync(new("ask", "help.send"));
            await Until(() => service.Requests.Count == 1);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
            completion.SetResult(new("Synthetic answer", [], "", [], [], []));
            await Until(() => notice is not null);
            Assert.AreEqual("results", notice!.CategoryId);
            Assert.AreEqual(WidgetNotificationVisibility.SourceHidden, notice.Visibility);
            Assert.AreEqual(WidgetNotificationRetention.Pending, notice.Retention);
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await widget.OnActionAsync(new("settings", "help.settings"));
            Assert.AreEqual(WidgetIntentResult.Accepted, await widget.OnIntentAsync(notice.OpenIntent!));
            Assert.AreEqual("help.message.2", Snapshot(widget).ScrollRevealRequest?.TargetId);
            Assert.AreEqual(1, service.Requests.Count);
            await widget.OnActionAsync(new("new.confirm", "help.new-proceed"));
            Assert.AreEqual(WidgetIntentResult.Accepted, await widget.OnIntentAsync(notice.OpenIntent!));
            Assert.IsNull(Snapshot(widget).ScrollRevealRequest);
            Assert.AreEqual(1, service.Requests.Count);
            Assert.AreEqual(WidgetIntentResult.Rejected, await widget.OnIntentAsync(notice.OpenIntent! with { SchemaDigest = new string('a', 64) }));
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    private static WidgetRail.Samples.GameHelp.GameHelpWidget Attach(FakeService service,IReadOnlyList<WidgetTaskWindow> windows)=>
        WidgetTestHost.Attach(new WidgetRail.Samples.GameHelp.GameHelpWidget(service),new WidgetTestHostServicesBuilder().WithResponse(WidgetPermissionCapabilities.Request, new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted)).WithResponse(WidgetTaskSwitcherCapabilities.List,windows).Build());
    private static ViewSnapshot Snapshot(Widget widget)=>widget.RenderSnapshot("fixture.game-help",1);
    private static IEnumerable<ViewNode> Walk(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Walk));
    private static async Task Until(Func<bool> predicate){var end=Environment.TickCount64+5000;while(!predicate()){if(Environment.TickCount64>=end)throw new TimeoutException();await Task.Delay(20);}}
    private sealed class FakeService:IGameHelpService
    {
        internal List<GameHelpRequest> Requests=[];
        internal bool Configured = true;
        internal Func<GameHelpRequest, Task<GameHelpAnswer>>? Reply;
        public Task<bool> HasKeyAsync(CancellationToken token)=>Task.FromResult(Configured);
        public Task SaveKeyAsync(string key,CancellationToken token)=>Task.CompletedTask;
        public Task DeleteKeyAsync(CancellationToken token)=>Task.CompletedTask;
        public Task<GameHelpAnswer> AskAsync(GameHelpRequest request,CancellationToken token)
        {Requests.Add(request);return Reply?.Invoke(request) ?? Task.FromResult(new GameHelpAnswer("Useful answer",[],"",[new("Next step", "What should I try next?")],[],[]));}
    }
}
