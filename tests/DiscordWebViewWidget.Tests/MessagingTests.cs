using System.Collections.Concurrent;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class MessagingTests
{
    internal static async Task FocusEntryAsync(Action<bool,string> check)
    {await InitialRevealAsync(check);await NotificationNavigationAsync(check);await SentMessageRevealAsync(check);}
    internal static async Task RunAsync(Action<bool,string> check)
    {
        var session=new MessagesSession();
        var widget=WidgetTestHost.Attach(new DiscordWebViewWidget(session,refreshInterval:TimeSpan.FromHours(1)),new WidgetTestHostServicesBuilder().Build());
        try
        {
            await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Act("discord.messages.open");await Wait(()=>Find("discord.conversation.10") is not null);
            check(WidgetTestHost.ValidateWinUiPresentation(Snapshot()).Count==0,"Native conversation list uses valid shared presentation controls");
            await Act("discord.conversation.10");await Wait(()=>Find("discord.message.history")?.IndexedCollection?.Count==1);
            var nativeErrors=WidgetTestHost.ValidateWinUiPresentation(Snapshot());
            check(nativeErrors.Count==0,"Native history and composer satisfy protocol and WinUI declarations: "+string.Join("; ",nativeErrors));
            check(Find("discord.message.history")?.IndexedDirectionalEntry==IndexedDirectionalEntryMode.VisibleEdge,
                "Chat history uses host-visible directional entry while explicit notification/return intents retain their own policy");
            using(var rows=WidgetTestHost.CreateIndexedCollectionHost(widget,"message.rows"))
            {
                rows.PublishSnapshot();using var range=await rows.AcquireAsync("discord.message.history",0,1);
                check(Nodes(range.Range.Items.Single().Root).Any(node=>node.Text=="Hello from history"),"Native message text comes from the selected channel history");
            }
            await Act("discord.message.draft","first draft");
            var staleSend=Find("discord.message.send")!.ActionId!;
            session.SendGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Act("discord.message.send");await Wait(()=>session.Sends==1);
            await widget.OnActionAsync(new(staleSend,"discord.message.send",InputScopeId:Snapshot().ActiveInputScopeId));
            await Act("discord.message.draft","edited while sending");
            session.SendGate.TrySetResult();await widget.WaitForMessageSendAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(session.Sends==1 && Find("discord.message.draft")?.Text=="edited while sending","Explicit send is single flight and acknowledgment preserves a subsequently edited draft");
            session.Ambiguous=true;
            await Act("discord.message.send");await Wait(()=>Find("discord.message.send")?.Text=="Check delivery");
            await Wait(()=>Find("discord.message.status")?.Text?.Contains("not been confirmed")==true);
            await Act("discord.message.send");await Wait(()=>session.Reconciles==1);
            check(session.Sends==2,"Ambiguous delivery exposes read-only reconciliation instead of a duplicate POST");
            session.Inner.State=session.Inner.State with{Account="2"};
            await widget.OnActionAsync(new("refresh","test"));await widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(Find("discord.message.draft") is null,"Changing account removes prior-account message history and draft");
            check(Find("discord.section.content")?.Transition?.Key.EndsWith(".2",StringComparison.Ordinal)==true,
                "Account replacement uses a distinct section-memory identity");
            await widget.OnActionAsync(new(staleSend,"discord.message.send",InputScopeId:Snapshot().ActiveInputScopeId));
            check(session.Sends==2,"Stale send cannot target a replacement account");
        }
        finally{session.SendGate?.TrySetResult();await WidgetTestHost.DestroyAsync(widget);}
        await PendingNavigationAsync(check);
        await RenameAsync(check);
        await DefiniteFailureAsync(check);
        await CatalogPagingAsync(check);
        await MessageIndicatorAsync(check);
        await InitialHistoryIndicatorAsync(check);
        await InitialRevealAsync(check);
        await LiveMessagesAsync(check);
        await MessagePresentationAsync(check);
        await LiveTailViewportAsync(check);
        await ReadStateAsync(check);
        await IntegratedNavigationAsync(check);
        await PrivateCallsAsync(check);
        await NotificationNavigationAsync(check);
        await UploadCancellationAsync(check);
        await SubmittedCancellationAsync(check);
        await ShareWhileSendingAsync(check);
        ViewSnapshot Snapshot()
        {try{return widget.Render().CreateSnapshot("messages.test",1);}catch(ProtocolValidationException error){throw new InvalidDataException(string.Join("; ",error.Errors),error);}}
        ViewNode? Find(string id)=>Nodes(Snapshot().Root).FirstOrDefault(node=>node.Id==id)??ConversationRowsHarness.Find(widget,Snapshot(),id);
        async Task Act(string id,string? text=null)
        {
            if(ConversationRowsHarness.IsRow(id)){await ConversationRowsHarness.ActAsync(widget,id);return;}
            var node=Find(id)??throw new Exception("Missing "+id);
            await widget.OnActionAsync(new(node.ActionId!,id,InputScopeId:Snapshot().ActiveInputScopeId){CommittedText=text,FocusedElementId=id});
        }
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
    private static async Task Wait(Func<bool> condition)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!condition())await Task.Delay(10,timeout.Token);}

    private static async Task PendingNavigationAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync();var session=scenario.Session;session.Ambiguous=true;
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        await scenario.Act("discord.message.attach");await Wait(()=>scenario.Find("discord.message.file.0") is not null);
        await scenario.Act("discord.message.draft","Uncertain original content");await scenario.Act("discord.message.send");
        await Wait(()=>scenario.Find("discord.message.status")?.Text?.Contains("not been confirmed")==true);
        var sent=session.Calls.Last(call=>call.Method=="messageSend").Request;
        await scenario.Act("discord.messages.back");await scenario.Act("discord.conversation.20");await scenario.WaitForHistory();
        check(scenario.Find("discord.message.draft")?.TextEntryValue=="" && scenario.Find("discord.message.send") is { Glyph: WidgetGlyph.Send, AccessibilityLabel: "Send message" },
            "An uncertain send does not leak its text or block another recipient's composer");
        check(scenario.Find("discord.message.file.0") is null && scenario.Released.IsEmpty,
            "Another recipient does not inherit or revoke attachment grants held by the uncertain send");
        session.Ambiguous=false;await scenario.Act("discord.message.draft","Other recipient message");await scenario.Act("discord.message.send");
        await scenario.Widget.WaitForMessageSendAsync().WaitAsync(TimeSpan.FromSeconds(5));
        check(session.Calls.Last(call=>call.Method=="messageSend").Request.GetProperty("channelId").GetString()=="20",
            "Another recipient remains sendable while the first channel has unresolved delivery");
        await scenario.Act("discord.messages.back");await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        check(scenario.Find("discord.message.draft")?.Text=="Uncertain original content" && scenario.Find("discord.message.send")?.Text=="Check delivery",
            "Returning to the original conversation restores its unresolved content and reconciliation action");
        check(scenario.Find("discord.message.file.0")?.Text?.Contains(scenario.File.Name,StringComparison.Ordinal)==true &&
            scenario.ReadIds.All(id=>id==scenario.File.Id) && scenario.Uploads==1 && scenario.Released.IsEmpty,
            "Returning restores the exact admitted attachment without rereading, reuploading, or releasing its grant");
        await scenario.Act("discord.message.send");await scenario.Widget.WaitForMessageSendAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var reconciled=session.Calls.Last(call=>call.Method=="messageReconcile").Request;
        check(reconciled.GetProperty("nonce").GetString()==sent.GetProperty("nonce").GetString() &&
            reconciled.GetProperty("content").GetString()==sent.GetProperty("content").GetString() &&
            reconciled.GetProperty("channelId").GetString()=="10" && session.Sends==2,
            "Reopening checks the exact original nonce/content/channel without repeating the POST");
        await Wait(()=>scenario.Released.Contains(scenario.File.Id));
        check(scenario.Uploads==1 && scenario.Released.Count(id=>id==scenario.File.Id)==1 && scenario.Find("discord.message.file.0") is null,
            "Confirmed reconciliation releases the held attachment exactly once without uploading it again");
        check(WidgetTestHost.ValidateWinUiPresentation(scenario.Snapshot()).Count==0,
            "Pending-send restoration keeps the production indexed WinUI presentation valid");
    }

    private static async Task RenameAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync();
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        await scenario.Act("discord.message.draft","Keep this unsent draft");await scenario.Act("discord.messages.back");
        scenario.Session.FriendName="Renamed friend";await scenario.Act("discord.refresh");
        await Wait(()=>scenario.Find("discord.conversation.name.10")?.Text=="Renamed friend");
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        check(scenario.Find("discord.message.title")?.Text=="Renamed friend" && scenario.Find("discord.message.draft")?.Text=="Keep this unsent draft",
            "Recipient display-name changes do not change channel identity or discard its draft");
    }

    private static async Task DefiniteFailureAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync();
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        scenario.Session.PreflightFailure="recipient_unavailable";
        await scenario.Act("discord.message.draft","Retain after definite rejection");await scenario.Act("discord.message.send");
        await scenario.Widget.WaitForMessageSendAsync().WaitAsync(TimeSpan.FromSeconds(5));
        check(scenario.Notifications.Any(command=>command.Notification?.Body?.StartsWith("Message was not sent.",StringComparison.Ordinal)==true) &&
            scenario.Find("discord.message.status") is null,"Definite send failure uses a toast without a permanent status line");
        check(scenario.Find("discord.message.send") is { Glyph: WidgetGlyph.Send, AccessibilityLabel: "Send message" } && scenario.Find("discord.message.draft")?.Text=="Retain after definite rejection" && scenario.Session.Reconciles==0,
            "Definite provider preflight rejection preserves draft and clears pending delivery instead of trapping Check delivery");
        scenario.Session.PreflightFailure=null;await scenario.Act("discord.message.send");
        await scenario.Widget.WaitForMessageSendAsync().WaitAsync(TimeSpan.FromSeconds(5));
        check(scenario.Session.Calls.Count(call=>call.Method=="messageSend")==2,
            "After a definite preflight rejection a later explicit Send is admitted normally");
    }

    private static async Task CatalogPagingAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync(new(){ConversationCount=81});
        check(scenario.Find("discord.messages.next") is null && scenario.Find("discord.messages.previous") is null &&
            scenario.Find("discord.messages.list")?.IndexedCollection?.Count==81,"The inbox declares all positions without paging buttons");
        check(scenario.Session.Calls.Count(call=>call.Method=="messageConversations")==1,"Initial inbox rendering fetches only its first bounded page");
        using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"conversation-paging");rows.PublishSnapshot();
        using var oldRange=await rows.AcquireAsync("discord.messages.list",40,40);
        var next=scenario.Session.Calls.Last(call=>call.Method=="messageConversations").Request;
        check(next.GetProperty("offset").GetInt32()==40 && next.GetProperty("version").GetString()=="v1",
            "Host range demand carries the exact provider catalog version");
        var reads=scenario.Session.Calls.Count(call=>call.Method=="messageConversations");
        check(await rows.LocateAsync("discord.messages.list",new("180"))==80 &&
            scenario.Session.Calls.Count(call=>call.Method=="messageConversations")==reads,"Preferred deep conversation lookup does not load intervening pages");
        var before=scenario.Find("discord.messages.list")!.IndexedCollection!;
        await scenario.Act("discord.refresh");await Wait(()=>scenario.Find("discord.messages.list")!.IndexedCollection!=before);
        var refreshed=scenario.Session.Calls.Last(call=>call.Method=="messageConversations").Request;
        check(refreshed.GetProperty("offset").GetInt32()==0 && refreshed.GetProperty("version").ValueKind==JsonValueKind.Null,
            "Explicit catalog refresh restarts at the first page without reusing an obsolete catalog version");
        rows.PublishSnapshot();using var refreshedRange=await rows.AcquireAsync("discord.messages.list",40,1);
        before=scenario.Find("discord.messages.list")!.IndexedCollection!;
        scenario.Session.CatalogVersion="v2";scenario.Session.CatalogRevision++;
        await scenario.Widget.OnActionAsync(new("refresh","test"));await scenario.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Wait(()=>scenario.Find("discord.messages.list")!.IndexedCollection!.QueryGeneration>before.QueryGeneration);
        rows.PublishSnapshot();
        check(oldRange.RouteAction("140",ControllerButton.A) is null && refreshedRange.RouteAction("140",ControllerButton.A) is null,
            "Catalog replacement revokes earlier recipient actions");
        var invalidated=scenario.Session.Calls.Last(call=>call.Method=="messageConversations").Request;
        check(invalidated.GetProperty("offset").GetInt32()==0 && invalidated.GetProperty("version").ValueKind==JsonValueKind.Null,
            "A live catalog revision retires the old offset and loads a fresh first page");
    }

    private static async Task MessageIndicatorAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync();
        // Another conversation changed after the native snapshot but before this
        // conversation acquired its watch. That older revision is not new here.
        scenario.Session.MessageRevision=7;
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        await Wait(()=>scenario.Session.ObservedChannel=="10");
        await RefreshState();
        check(scenario.Find("discord.message.latest") is null,
            "Initial conversation observation suppresses older-channel revisions instead of inventing new messages");
        scenario.Session.CatalogRevision++;await RefreshState();
        check(scenario.Find("discord.message.latest") is null,
            "Read acknowledgments and catalog-only changes do not mark the loaded history as new");
        scenario.Session.MessageRevision++;await RefreshState();
        check(scenario.Find("discord.message.latest")?.Text=="New messages",
            "A real watched-channel mutation after the observation watermark exposes fresh history");
        await scenario.Act("discord.message.latest");await scenario.WaitForHistory();await RefreshState();
        check(scenario.Find("discord.message.latest") is null,
            "Refreshing latest history does not recreate the indicator from an unchanged message revision");
        async Task RefreshState()
        {
            await scenario.Widget.OnActionAsync(new("refresh","test"));
            await scenario.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task InitialRevealAsync(Action<bool,string> check)
    {
        await using(var unread=await Scenario.OpenAsync(new(){HistoryCount=18,OldestUnread="105"}))
        {
            await unread.Act("discord.conversation.10");await unread.WaitForHistory();
            check(unread.Snapshot().ScrollRevealRequest?.IndexedItem is {ItemKey:"105",Index:5} &&
                unread.Session.Calls.Single(call=>call.Method=="messageHistory" && call.Request.TryGetProperty("around",out var around) && around.ValueKind==JsonValueKind.String).Request.GetProperty("around").GetString()=="105",
                "Fresh entry reveals the authoritative first unread message with one bounded around read");
            await unread.Widget.OnActionAsync(new("message-focus-composer","test"){InputScopeId=unread.Snapshot().ActiveInputScopeId});
            using var rows=WidgetTestHost.CreateIndexedCollectionHost(unread.Widget,"messages.unread.entry");
            using var boundary=await rows.AcquireAsync("discord.message.history",5,1);
            check(Nodes(boundary.Range.Items[0].Root).Any(node=>node.Text=="Unread messages") && unread.Snapshot().ScrollRevealRequest is null,
                "Explicit composer focus cancels the reveal without discarding the entry unread separator");
        }
        await using(var stale=await Scenario.OpenAsync(new(){HistoryCount=18,OldestUnread="999"}))
        {
            await stale.Act("discord.conversation.10");await stale.WaitForHistory();
            check(stale.Snapshot().ScrollRevealRequest?.IndexedItem is {ItemKey:"117"} && stale.Session.Calls.Count(call=>call.Method=="messageHistory")==2,
                "Missing unread boundary falls back to latest exactly once without an acknowledgment");
        }
        await using(var scenario=await Scenario.OpenAsync(new(){HistoryCount=18}))
        {
            await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
            var initial=scenario.Snapshot();var reveal=initial.ScrollRevealRequest;
            check(scenario.Find("discord.message.history")?.IndexedDirectionalEntry==IndexedDirectionalEntryMode.VisibleEdge,
                "Conversation declares visible-edge directional entry independently of exact reveal intent");
            var source=scenario.Find("discord.message.history")!.IndexedCollection!;
            check(reveal?.IndexedItem is {ItemKey:"117",Index:17} target && target.CollectionId=="discord.message.history" &&
                target.SourceId==source.SourceId && target.QueryGeneration==source.QueryGeneration,
                "Cold history reveals its exact latest occurrence from the current indexed publication");
            check(initial.InitialFocusId=="discord.message.draft" && initial.FocusGroupEntryRequest?.IndexedItem is null &&
                WidgetTestHost.ValidateWinUiPresentation(initial).Count==0,
                "Initial latest visibility uses supported indexed scroll reveal without transferring composer focus");
            check(scenario.Snapshot().ScrollRevealRequest?.RequestId==reveal!.RequestId && scenario.Snapshot().ScrollRevealRequest?.RequestId==reveal.RequestId,
                "Repeated rendering reuses one event-owned reveal instead of allocating new intents");
            var reads=scenario.Session.Calls.Count(call=>call.Method=="messageHistory");
            await scenario.Act("discord.messages.back");
            check(scenario.Snapshot().ScrollRevealRequest is null,"Leaving the conversation withdraws its initial scroll intent");
            await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
            check(scenario.Snapshot().ScrollRevealRequest is {IndexedItem.ItemKey:"117"} reopened && reopened.RequestId>reveal!.RequestId &&
                scenario.Snapshot().FocusGroupEntryRequest?.TargetId=="discord.message.draft" && scenario.Session.Calls.Count(call=>call.Method=="messageHistory")==reads &&
                scenario.Find("discord.message.history")!.IndexedCollection!.QueryGeneration==source.QueryGeneration,
                "Explicit recipient reopening reveals latest and focuses composer while reusing its current loaded query without another read");
            var controls=scenario.Snapshot().Root.Shortcuts;
            check(controls.Any(item=>item.Button==ControllerButton.X && item.ActionId=="message-focus-composer") &&
                !controls.Any(item=>item.Button==ControllerButton.LeftStick) && scenario.Find("discord.message.options") is null &&
                controls.Any(item=>item.Button==ControllerButton.Y && item.ActionId=="message-latest"),"Conversation keeps write/latest shortcuts without redundant header controls");
            await scenario.Widget.OnActionAsync(new("message-focus-composer","test",InputScopeId:scenario.Snapshot().ActiveInputScopeId));
            check(scenario.Snapshot().FocusGroupEntryRequest?.TargetId=="discord.message.draft" && scenario.Snapshot().ScrollRevealRequest is null,
                "Composer shortcut replaces obsolete initial reveal with an exact shared focus intent");
        }
        await using(var scenario=await Scenario.OpenAsync(new(){HistoryCount=18}))
        {
            await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
            check(scenario.Snapshot().ScrollRevealRequest is not null,"A fresh conversation has one initial latest intent");
            await WidgetTestHost.SetLifecycleStateAsync(scenario.Widget,WidgetLifecycleState.Background);
            check(scenario.Snapshot().ScrollRevealRequest is null,"Background retirement prevents initial scroll publication");
            await WidgetTestHost.SetLifecycleStateAsync(scenario.Widget,WidgetLifecycleState.Interactive);
            check(scenario.Snapshot().ScrollRevealRequest is null,"Returning after active-lifetime retirement cannot replay the departed initial intent");
        }
        var delayed=new MessagesSession{HistoryCount=18,HistoryGate=new(TaskCreationOptions.RunContinuationsAsynchronously),IgnoreHistoryCancellation=true};
        await using(var scenario=await Scenario.OpenAsync(delayed))
        {
            await scenario.Act("discord.conversation.10");await Wait(()=>delayed.Calls.Any(call=>call.Method=="messageHistory"));
            check(scenario.Snapshot().ScrollRevealRequest is {DeferredSource:not null,IndexedItem:null},
                "Loading history admits a cancellable deferred reveal without fabricating a target");
            await scenario.Act("discord.messages.back");delayed.HistoryGate.TrySetResult();
            await Wait(()=>delayed.HistoryReplies==1);
            check(scenario.Snapshot().ScrollRevealRequest is null && scenario.Find("discord.messages.page") is not null,
                "Late history completion after Back cannot publish a reveal into the retired navigation visit");
        }
    }

    private sealed class Scenario: IAsyncDisposable
    {
        internal MessagesSession Session {get;}
        internal DiscordWebViewWidget Widget {get;}
        internal readonly WidgetFileReference File=new(new string('e',32),"pending-capture.png","image/png",3,long.MaxValue);
        internal readonly WidgetFileReference IncomingFile=new(new string('a',32),"incoming.png","image/png",3,long.MaxValue);
        internal readonly ConcurrentQueue<string> Released=new(),ReadIds=new();
        internal readonly ConcurrentQueue<WidgetNotificationCommand> Notifications=new();
        internal int Uploads;
        internal TaskCompletionSource? UploadGate;
        internal bool IgnoreUploadCancellation;
        private Scenario(MessagesSession session,TimeProvider? clock=null)
        {
            Session=session;
            var services=new WidgetTestHostServicesBuilder()
                .WithHandler(WidgetNotificationCapabilities.Publish,(request,token)=>
                {token.ThrowIfCancellationRequested();Notifications.Enqueue(request);return ValueTask.FromResult(new WidgetNotificationReceipt(true));})
                .WithHandler(WidgetFileCapabilities.Choose,(_,_)=>ValueTask.FromResult(new WidgetFileSelection([File])))
                .WithHandler(WidgetFileCapabilities.Accept,(_,_)=>ValueTask.FromResult(new WidgetFileSelection([IncomingFile])))
                .WithHandler(WidgetFileCapabilities.Get,(request,_)=>request.Id==File.Id?ValueTask.FromResult(File):throw new InvalidDataException("Foreign file"))
                .WithHandler(WidgetFileCapabilities.Read,(request,_)=>
                {
                    if(request.Id!=File.Id || request.Offset<0 || request.Offset>File.ByteLength)throw new InvalidDataException("Foreign read");
                    ReadIds.Enqueue(request.Id);var count=(int)Math.Min(request.Count,File.ByteLength-request.Offset);
                    return ValueTask.FromResult(new WidgetFileReadChunk(request.Offset,Enumerable.Repeat((byte)42,count).ToArray(),request.Offset+count==File.ByteLength));
                })
                .WithHandler(WidgetFileCapabilities.Release,(request,_)=>{Released.Enqueue(request.Id);return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));}).Build();
            Widget=WidgetTestHost.Attach(new DiscordWebViewWidget(session,time:clock,refreshInterval:TimeSpan.FromHours(1),uploadAttachment:async(_,stream,progress,token)=>
            {
                Interlocked.Increment(ref Uploads);
                var chunk=new byte[checked((int)stream.Length/2)];await stream.ReadExactlyAsync(chunk,token);progress(stream.Position);
                if(UploadGate is {} gate)await(IgnoreUploadCancellation?gate.Task:gate.Task.WaitAsync(token));
                if(!IgnoreUploadCancellation){await stream.CopyToAsync(Stream.Null,token);progress(stream.Position);}
            }),services);
        }
        internal static async Task<Scenario> OpenAsync(MessagesSession? session=null,TimeProvider? clock=null)
        {
            var result=new Scenario(session??new(),clock);
            try
            {
                await WidgetTestHost.InitializeAsync(result.Widget);await WidgetTestHost.SetLifecycleStateAsync(result.Widget,WidgetLifecycleState.Interactive);
                await result.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await result.Act("discord.messages.open");await Wait(()=>result.Find("discord.conversation.10") is not null);return result;
            }
            catch{await result.DisposeAsync();throw;}
        }
        internal ViewSnapshot Snapshot()=>Widget.Render().CreateSnapshot("messages.lifecycle",1);
        internal ViewNode? Find(string id)=>Nodes(Snapshot().Root).FirstOrDefault(node=>node.Id==id)??ConversationRowsHarness.Find(Widget,Snapshot(),id);
        internal Task WaitForHistory()=>Wait(()=>Find("discord.message.history")?.IndexedCollection?.Count==Session.HistoryCount);
        internal async Task Act(string id,string? text=null)
        {
            if(ConversationRowsHarness.IsRow(id)){await ConversationRowsHarness.ActAsync(Widget,id);return;}
            var node=Find(id)??throw new InvalidOperationException("Missing "+id);
            await Widget.OnActionAsync(new(node.ActionId!,id,InputScopeId:Snapshot().ActiveInputScopeId){CommittedText=text,FocusedElementId=id});
        }
        public async ValueTask DisposeAsync(){UploadGate?.TrySetResult();Session.SendGate?.TrySetResult();Session.HistoryGate?.TrySetResult();Session.ReadGate?.TrySetResult();Session.ObservationGate?.TrySetResult();await WidgetTestHost.DestroyAsync(Widget);}
    }

    private sealed class MessagesSession:IDiscordSession
    {
        public event Action<WebSessionReference>? Invalidated {add=>Inner.Invalidated+=value;remove=>Inner.Invalidated-=value;}
        internal FakeSession Inner=new();
        internal int Sends,Reconciles;
        internal bool Ambiguous;
        internal string FriendName="Friend",CatalogVersion="v1";
        internal string? CatalogOrderVersion;
        internal string? OldestUnread;
        internal string? LastMessage;
        internal Func<DiscordConversation,DiscordConversation>? ConversationMetadata;
        internal bool CanMarkRead;
        internal bool? HasUnread;
        internal string SentId="100";
        internal int? LatestStartAfterSend;
        internal string? RejectReadBeyond;
        internal bool OwnHistory;
        internal string? PreflightFailure;
        internal int ConversationCount=2;
        internal int HistoryCount=1,HistoryReplies;
        internal bool IgnoreHistoryCancellation;
        internal bool IgnoreSendCancellation;
        internal bool MissingNotificationTarget,PagedNotificationHistory;
        internal TaskCompletionSource? NotificationGate;
        internal long CatalogRevision;
        internal long MessageRevision,ObservedSinceRevision;
        internal bool LiveUpdates;
        internal long WatchEpoch;
        internal long RecoveryFloor;
        internal string RecoveryText="Recovered current text";
        internal bool RecoveryComplete=true;
        internal TaskCompletionSource? RecoveryGate;
        internal readonly ConcurrentQueue<DiscordMessageChange> Changes=new();
        internal string? ObservedChannel;
        internal readonly ConcurrentQueue<(string Method,JsonElement Request)> Calls=new();
        internal TaskCompletionSource? SendGate;
        internal TaskCompletionSource? HistoryGate;
        internal TaskCompletionSource? ReadGate;
        internal TaskCompletionSource? ObservationGate;
        internal Func<string,JsonElement,Task>? CallEffect;
        public WebSessionReference? Reference=>Inner.Reference;
        public ValueTask<DiscordState> ReadAsync(CancellationToken token)=>ValueTask.FromResult(Inner.State with
        {Messaging=new(Inner.State.Account,"ready",true,true,true,2000,50,MessageRevision:MessageRevision,ObservedChannelId:ObservedChannel,
            CatalogRevision:CatalogRevision,ObservedSinceRevision:ObservedSinceRevision,WatchEpoch:WatchEpoch,CanMarkRead:CanMarkRead,HasUnreadConversations:HasUnread)});
        public async ValueTask<JsonElement> InvokeAsync(string method,object? args,CancellationToken token)
        {
            var request=JsonSerializer.SerializeToElement(args);
            Calls.Enqueue((method,request));
            if(method=="messageNotificationTarget")
            {
                if(NotificationGate is {} gate)await gate.Task.WaitAsync(token);
                return MissingNotificationTarget?JsonSerializer.SerializeToElement<object?>(null):JsonSerializer.SerializeToElement(new
                {
                    account=Inner.State.Account,messageId=request.GetProperty("tag").GetString(),
                    conversation=new DiscordConversation("20","20",null,"Friend 20","dm",null,null,true,true)
                });
            }
            if(method=="messageLocateConversation")
            {
                var key=request.GetProperty("key").GetString();
                var version=request.GetProperty("version").GetString();
                if(version!=CatalogVersion)throw new DiscordCommandException("conversation_catalog_changed");
                int? found=key=="10"?0:key=="20"?1:int.TryParse(key,out var numeric)?numeric-100:null;
                if(found is <0 || found>=ConversationCount)found=null;
                return JsonSerializer.SerializeToElement(new{account=Inner.State.Account,version=CatalogVersion,totalCount=ConversationCount,key,index=found});
            }
            if(method=="messageConversations")
            {
                var offset=request.GetProperty("offset").GetInt32();var limit=request.GetProperty("limit").GetInt32();
                var version=request.TryGetProperty("version",out var supplied)?supplied.GetString():null;
                if(version is not null && version!=CatalogVersion)throw new DiscordCommandException("conversation_catalog_changed");
                var items=Enumerable.Range(offset,Math.Min(limit,ConversationCount-offset)).Select(index=>
                {var id=index==0?"10":index==1?"20":(100+index).ToString();var row=new DiscordConversation(id,id,null,index==0?FriendName:"Friend "+id,"dm",null,LastMessage,OldestUnread is not null,true){OldestUnreadMessageId=OldestUnread};return ConversationMetadata?.Invoke(row)??row;}).ToArray();
                return JsonSerializer.SerializeToElement(new DiscordConversationPage(new(Inner.State.Account,CatalogVersion,ConversationCount,offset,offset+items.Length<ConversationCount)
                    {OrderVersion=CatalogOrderVersion},items));
            }
            var channel=request.TryGetProperty("channelId",out var channelValue)&&channelValue.ValueKind==JsonValueKind.String?channelValue.GetString()!:"10";
            if(method=="messageMarkRead")
            {
                // Model an external action that can finish after cancellation.
                if(ReadGate is {} readGate)await readGate.Task;
                if(RejectReadBeyond is {} head && long.Parse(request.GetProperty("messageId").GetString()!)>long.Parse(head))
                    throw new DiscordCommandException("invalid_read_target");
                return JsonSerializer.SerializeToElement(new{account=request.GetProperty("account").GetString(),channelId=channel,
                    messageId=request.GetProperty("messageId").GetString(),status="applied"});
            }
            if(method=="messageHistory")
            {
                if(HistoryGate is {} gate)await(IgnoreHistoryCancellation?gate.Task:gate.Task.WaitAsync(token));
                var before=request.TryGetProperty("before",out var beforeValue)?beforeValue.GetString():null;
                var after=request.TryGetProperty("after",out var afterValue)?afterValue.GetString():null;
                var around=request.TryGetProperty("around",out var aroundValue)?aroundValue.GetString():null;
                var start=PagedNotificationHistory?(before is not null?int.Parse(before)-HistoryCount:after is not null?int.Parse(after)+1:100):100;
                if(Sends>0 && LatestStartAfterSend is {} latest && before is null && after is null)
                    start=around is null?latest:int.Parse(around)-HistoryCount/2;
                var items=Enumerable.Range(start,HistoryCount).Select(index=>Message("Hello from history",null,channel) with{
                    Id=index.ToString(),IsOwn=OwnHistory || Sends>0 && LatestStartAfterSend.HasValue && index.ToString()==SentId}).ToArray();
                Interlocked.Increment(ref HistoryReplies);
                return JsonSerializer.SerializeToElement(new DiscordMessagePage(new(Inner.State.Account,channel,before,
                    items.Length==0?null:items[0].Id,PagedNotificationHistory,After:after,NextAfter:items.Length==0?null:items[^1].Id,Around:around),items));
            }
            if(method=="messageObserve")
            {
                var next=request.GetProperty("active").GetBoolean() && request.GetProperty("channelId").ValueKind==JsonValueKind.String
                    ?request.GetProperty("channelId").GetString():null;
                if(next is not null && ObservationGate is {} observing)await observing.Task.WaitAsync(token);
                if(ObservedChannel!=next){ObservedChannel=next;ObservedSinceRevision=MessageRevision;if(LiveUpdates)WatchEpoch++;}
                if(LiveUpdates)return JsonSerializer.SerializeToElement((await ReadAsync(token)).Messaging);
                return JsonSerializer.SerializeToElement(new{ok=true});
            }
            if(method=="messageChanges")
            {
                var after=request.GetProperty("afterRevision").GetInt64();
                return JsonSerializer.SerializeToElement(new DiscordMessageChanges(
                    new(Inner.State.Account,channel,WatchEpoch,after,MessageRevision,after<RecoveryFloor,false),Changes.Where(change=>change.Revision>after).ToArray()));
            }
            if(method=="messageRecover")
            {
                var epoch=request.GetProperty("watchEpoch").GetInt64();
                if(RecoveryGate is {} gate)await gate.Task;
                var keys=request.GetProperty("ids").EnumerateArray().Select(value=>value.GetString()!).ToArray();
                return JsonSerializer.SerializeToElement(new DiscordMessageRecovery(new(Inner.State.Account,channel,epoch,MessageRevision,keys[^1],RecoveryComplete),
                    RecoveryComplete?keys.Select(id=>new DiscordMessageChange(MessageRevision,"upsert",id,Message(RecoveryText,null,channel) with{Id=id})).ToArray():[]));
            }
            if(method=="messagePrepareAttachments")return JsonSerializer.SerializeToElement(new DiscordUploadBatch(Inner.State.Account,channel,new string('f',32),
                DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeMilliseconds(),request.GetProperty("files").EnumerateArray().Select(file=>
                    new DiscordUploadTarget(file.GetProperty("id").GetString()!,file.GetProperty("fileName").GetString()!,file.GetProperty("byteLength").GetInt64(),
                        "https://discord-attachments-uploads-prd.storage.googleapis.com/fixture","pending/"+file.GetProperty("fileName").GetString())).ToArray()));
            if(method is "messageSend" or "messageReconcile")
            {
                if(method=="messageSend")Interlocked.Increment(ref Sends);else Interlocked.Increment(ref Reconciles);
                if(method=="messageSend" && PreflightFailure is {} code)throw new DiscordCommandException(code);
                if(SendGate is {} gate)await(IgnoreSendCancellation?gate.Task:gate.Task.WaitAsync(token));
                var nonce=request.GetProperty("nonce").GetString()!;
                return JsonSerializer.SerializeToElement(new DiscordMessageSendResult(Inner.State.Account,channel,nonce,
                    Ambiguous?"unknown":"sent",Ambiguous?null:Message(request.GetProperty("content").GetString()!,nonce,channel),null));
            }
            if(method is "callStart" or "callAnswer" or "callDecline" && CallEffect is {} effect)
            {await effect(method,request);return JsonSerializer.SerializeToElement(new{});}
            return await Inner.InvokeAsync(method,args,token);
        }
        private DiscordMessage Message(string text,string? nonce,string channel="10")=>new(nonce is null?"100":SentId,channel,Inner.State.Account,"Friend",null,text,1,null,nonce,[],nonce is not null);
        public ValueTask<IDiscordReceiver> OpenReceiverAsync(CancellationToken token)=>Inner.OpenReceiverAsync(token);
        public ValueTask<WebSessionMicrophoneResult> RequestMicrophoneAsync(CancellationToken token)=>Inner.RequestMicrophoneAsync(token);
        public ValueTask<WebSessionPermissionResult> RequestNotificationsAsync(CancellationToken token)=>Inner.RequestNotificationsAsync(token);
        public ValueTask DisposeAsync()=>Inner.DisposeAsync();
    }
}
