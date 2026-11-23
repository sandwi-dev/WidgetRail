using System.Globalization;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private sealed record ConversationQuery(string Account,WebSessionReference Session,string Search="");
    private sealed record MessageSelection(string Account,WebSessionReference Session,string Channel,string Name,bool CanSend=true);
    private sealed record MessageLine(DiscordMessage Message,bool ShowAuthor,bool ShowDate,bool RevealSpoilers,bool ShowUnread=false);
    private sealed record InitialMessageReveal(WidgetFocusRequest Request,MessageSelection Selection,long NavigationRevision,
        CancellationToken RouteLifetime,CancellationToken ActiveLifetime,string? MessageId=null,bool UnreadBoundary=false,bool SentMessage=false);
    private sealed record MessageControlFocus(FocusGroupEntryRequest Request,MessageSelection Selection,long NavigationRevision);
    private sealed class IncomingAccountBinding(WebSessionReference? session)
    {
        internal WebSessionReference? Session=session;
        internal string? Account;
    }
    private sealed record MessageUi(ConversationQuery? Query=null,MessageSelection? Selection=null,string Draft="",
        string Notice="",string? PendingNonce=null,string? PendingContent=null,bool NewMessages=false,
        WidgetFileReference[]? Files=null,WidgetFileReference[]? Incoming=null,InitialMessageReveal? InitialReveal=null,
        bool Uploading=false,bool CancellingSend=false,string[]? RevealedSpoilers=null,string? HistoryAnchor=null,string? IncomingOfferId=null,IncomingAccountBinding? IncomingBinding=null,
        MessageControlFocus? ControlFocus=null,string? EntryUnreadBoundaryId=null,bool LatestRequired=false,MessageMediaDialog? Media=null)
    {
        internal bool IncomingFiles=>Incoming is {Length:>0};
    }
    private WidgetModel<MessageUi> messages=null!;
    private WidgetQueryResource<ConversationQuery,DiscordConversationPage> conversations=null!;
    private WidgetCursorResource<DiscordMessage> history=null!;
    private WidgetLoadedCollection<string,MessageLine> messageRows=null!;
    private readonly DiscordPendingMessages pendingMessages=new();
    private readonly DiscordMessageDrafts messageDrafts=new();
    // Short domain admission transactions span the model, draft custody and
    // navigator. No provider await or input-routing policy runs under this gate.
    private readonly object messageAdmissionGate=new();
    private static DiscordPendingMessageKey PendingKey(MessageSelection selected)=>new(selected.Account,selected.Session.Id,selected.Channel);
    private static readonly JsonSerializerOptions MessageJson=new(JsonSerializerDefaults.Web);
    private long observedCatalogRevision=-1,observedMessageRevision=-1;
    // A first disconnected snapshot is ambiguous while Discord hydrates its
    // stores. Once an account was verified, losing it is never a cold start.
    private volatile bool hasObservedDiscordAccount;

    private void InitializeMessages()
    {
        messages=CreateModel(new MessageUi());
        conversations=CreateQueryResource<ConversationQuery,DiscordConversationPage>("discord.conversations",new()
        {
            Load=async(query,token)=>
            {
                DemandMessageAccount(query.Account,query.Session);
                var json=await client!.InvokeAsync("messageConversations",new{account=query.Account,query=query.Search,offset=0,version=(string?)null,limit=40},token);
                var page=json.Deserialize<DiscordConversationPage>(MessageJson)??throw DiscordMessageValues.Invalid();
                page.Validate(query.Account,0,40);
                DemandMessageAccount(query.Account,query.Session);
                return page;
            },
            MapError=_=>new("discord.messages.unavailable","Messages are unavailable. Try Refresh or open Discord."),
            CacheDuration=TimeSpan.FromSeconds(30)
        });
        InitializeConversationRows();
        history=CreateCursorResource<DiscordMessage>("discord.message.history",new()
        {
            PageSize=20,MaximumRetainedItems=40,PrepareAdjacentPages=true,
            LiveUpdates=MessageLiveOptions,
            LoadPage=LoadMessageHistoryAsync,
            MapError=_=>new("discord.history.unavailable","Could not load messages. Try again."),
            Viewports=[new("discord.message.history",item=>new(item.Id),item=>"discord.message."+item.Id)]
        });
        messageRows=CreateLoadedCollection<string,MessageLine>("discord.message.rows","","",[],new()
        {
            ItemKey=item=>new(item.Message.Id),PreserveOccurrences=true,FollowEnd=true,
            LiveOrder=(left,right)=>MessageLiveOptions.Compare(left.Message,right.Message),
            RenderItem=(_,item,ids)=>MessageRow(item,ids),
            OnAction=MessageRowAction,
            OnViewportChanged=MessageViewportChanged,
            OnBoundaryDemand=LoadMessageBoundaryAsync,
            OnRecoverViewport=RecoverMessageViewportAsync
        });
    }

    private bool MessageAccountCurrent(string account,WebSessionReference session)=>
        CurrentState() is {Messaging.CanRead:true} data && data.Account==account && client?.Reference==session;
    private void DemandMessageAccount(string account,WebSessionReference session)
    {if(!MessageAccountCurrent(account,session))throw new InvalidOperationException("The Discord account changed.");}
    private bool MessageSelectionCurrent(MessageSelection selected)=>messages.Value.Selection==selected && MessageAccountCurrent(selected.Account,selected.Session);
    private string MessageAction(string action,MessageUi current)=>DomainAction(action,current.Selection?.Session.Id,current.Selection?.Account,
        current.Selection?.Channel,current.Draft,current.PendingNonce,string.Join(",",(current.Files??[]).Select(file=>file.Id)));

    private async ValueTask<WidgetCursorPage<DiscordMessage>> LoadMessageHistoryAsync(WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,int count,CancellationToken token)
    {
        var selected=messages.Value.Selection??throw new InvalidOperationException("No conversation selected.");
        DemandMessageAccount(selected.Account,selected.Session);
        if(messageObservation is {} observation)
            await observation.Completion.WaitAsync(token);
        if(!MessageSelectionCurrent(selected))throw new OperationCanceledException();
        var before=direction==WidgetCursorDirection.Before?cursor?.Value:null;
        var after=direction==WidgetCursorDirection.After?cursor?.Value:null;
        var around=cursor is null?messages.Value.HistoryAnchor:null;
        var json=await client!.InvokeAsync("messageHistory",new{account=selected.Account,channelId=selected.Channel,before,after,around,limit=count},token);
        var page=json.Deserialize<DiscordMessagePage>(MessageJson)??throw DiscordMessageValues.Invalid();
        page.Validate(selected.Account,selected.Channel,before,count,after,around);
        if(!MessageSelectionCurrent(selected))throw new OperationCanceledException();
        if(cursor is null && around is null && messages.Value.InitialReveal is {SentMessage:true,MessageId:{} sentId} sent &&
            CurrentMessageEntry(sent) && !page.Items.Any(item=>item.Id==sentId))
        {
            // A busy channel can already have a full page of newer messages.
            // Keep the explicit send destination rather than revealing a different row.
            around=sentId;
            json=await client.InvokeAsync("messageHistory",new{account=selected.Account,channelId=selected.Channel,around,limit=count},token);
            page=json.Deserialize<DiscordMessagePage>(MessageJson)??throw DiscordMessageValues.Invalid();
            page.Validate(selected.Account,selected.Channel,null,count,around:around);
            if(!CurrentMessageEntry(sent))throw new OperationCanceledException();
        }
        if(around is not null && messages.Value.InitialReveal is {UnreadBoundary:true} unread && unread.MessageId==around &&
            !page.Items.Any(item=>item.Id==around) && CurrentMessageEntry(unread))
        {
            // A deleted/stale unread identity is not a usable entry boundary.
            // One latest read replaces it; no read receipt or repeated probing.
            json=await client.InvokeAsync("messageHistory",new{account=selected.Account,channelId=selected.Channel,limit=count},token);
            page=json.Deserialize<DiscordMessagePage>(MessageJson)??throw DiscordMessageValues.Invalid();
            page.Validate(selected.Account,selected.Channel,null,count);
            if(!MessageSelectionCurrent(selected) || !CurrentMessageEntry(unread))throw new OperationCanceledException();
            around=null;
            messages.Update(value=>ReferenceEquals(value.InitialReveal,unread)
                ?value with{HistoryAnchor=null,EntryUnreadBoundaryId=null,InitialReveal=unread with{MessageId=null,UnreadBoundary=false}}:value);
        }
        // Both directions are provider cursors. The shared cursor resource owns
        // window eviction, boundary reversal, focus anchors and delayed results.
        return new(page.Items,
            page.Context.NextBefore is {} older && (around is not null || after is not null || page.Context.HasMore)?new(older):null,
            page.Context.NextAfter is {} newer && (around is not null || before is not null || after is not null && page.Context.HasMore)?new(newer):null);

    }

    private void ReconcileMessages(DiscordState next)
    {
        lock(messageAdmissionGate)
        {
            hasObservedDiscordAccount|=next.Ready;
            var current=messages.Value;
            if(current.Query is {} query && (!next.Ready || next.Account!=query.Account || client?.Reference!=query.Session))
            {
                if(navigation.Value.Route==Route.MessageMedia)navigation.Back();
                Operations.Cancel("discord.message.send");
                var retired=pendingMessages.Clear().Concat(messageDrafts.Clear()).Concat(current.Files??[]).Concat(current.Incoming??[]).DistinctBy(file=>file.Id).ToArray();
                messages.Update(_=>new MessageUi());ReleaseMessageFiles(retired);conversations.Reset();ResetConversationRows();history.Reset();ResetMessageUpdates();
                observedCatalogRevision=observedMessageRevision=-1;
                return;
            }
            if(next.Messaging is not {} messaging)return;
            if(navigation.Value.Route is Route.Messages or Route.Conversation && current.Query is {} active && observedCatalogRevision!=messaging.CatalogRevision)
            {
                conversations.EnsureLoaded(active,forceRefresh:true);
            }
            if(messaging.WatchEpoch>0)ReconcileMessageUpdates(messaging);
            else if(navigation.Value.Route==Route.Conversation && current.Selection is {} selected &&
                messaging.ObservedChannelId==selected.Channel && observedMessageRevision>=0 &&
                messaging.MessageRevision>Math.Max(observedMessageRevision,messaging.ObservedSinceRevision))
                messages.Update(value=>value with{NewMessages=true});
            observedCatalogRevision=messaging.CatalogRevision;observedMessageRevision=messaging.MessageRevision;
        }
    }

    private void ObserveMessages()
    {
        var current=messages.Value;
        if(current.Query is not {} query)return;
        var active=navigation.Value.Route is Route.Messages or Route.Conversation;
        // Keep the single retained conversation observed while its inbox is
        // visible. Returning can replay local deltas instead of refetching it.
        var channel=active?current.Selection?.Channel:null;
        messageObservation=Operations.RunLatest("discord.message.observer",async context=>
        {
            try
            {
                var json=await client!.InvokeAsync("messageObserve",new{account=query.Account,channelId=channel,active},context.CancellationToken);
                if(context.IsCurrent && MessageAccountCurrent(query.Account,query.Session) && channel is not null)
                {
                    var observed=json.Deserialize<DiscordMessagingState>(MessageJson);
                    if(observed is not null){observed.Validate(query.Account);ReconcileMessageUpdates(observed);}
                }
            }
            catch(Exception) when(!context.CancellationToken.IsCancellationRequested){ }
        });
    }

    private void ResumeMessages()
    {
        if(messages.Value.Query is not {} query || !MessageAccountCurrent(query.Account,query.Session))return;
        if(navigation.Value.Route is Route.Messages or Route.Conversation)conversations.EnsureLoaded(query);
        if(navigation.Value.Route==Route.Conversation)history.EnsureLoaded();
        ObserveMessages();
    }

    private bool TryMessageAction(WidgetActionEvent action,CancellationToken token,out ValueTask result)
    {
        result=ValueTask.CompletedTask;
        if(action.ActionId=="messages" && CurrentState() is {Messaging.CanRead:true} data && client?.Reference is {} session)
        {
            if(IsMessageRoute(navigation.Value.Route))return true;
            var query=messages.Value.Query is {} existing && existing.Account==data.Account && existing.Session==session
                ?existing:new ConversationQuery(data.Account,session);
            messages.Update(value=>value.Query?.Account==data.Account && value.Query.Session==session?value with{Query=query}:new(Query:query));
            conversations.EnsureLoaded(query);
            navigation.NavigateRoot(Route.Messages);
            if(messages.Value.Selection is {} retained && MessageSelectionCurrent(retained) && !messages.Value.IncomingFiles)
            {
                var retainedRow=conversations.GetSnapshot(query).Value?.Items.FirstOrDefault(item=>item.ChannelId==retained.Channel);
                navigation.Push(Route.Conversation,"discord.messages.list");
                BeginReadVisit(retained,retainedRow?.LastMessageId,false);
                history.EnsureLoaded();
            }
            ObserveMessages();return true;
        }
        var current=messages.Value;
        if(navigation.Value.Route==Route.Messages && TryIncomingFileAction(action,current))return true;
        if(navigation.Value.Route==Route.Messages && current.IncomingFiles && action.ActionId==IncomingFilesAction(current))
        {
            CancelIncomingShare(current);return true;
        }
        if(current.Query is not {} selectedQuery || !MessageAccountCurrent(selectedQuery.Account,selectedQuery.Session))return false;
        if(navigation.Value.Route is Route.Messages or Route.Conversation)
        {
            if(action.ActionId=="message-search" && action.CommittedText is {Length:<=120} text)
            {
                var query=selectedQuery with{Search=text};messages.Update(value=>value with{Query=query});conversations.EnsureLoaded(query);return true;
            }
            if(action.ActionId=="refresh")
            {
                conversations.EnsureLoaded(selectedQuery,forceRefresh:true);
                state.Refresh();return true;
            }
        }
        if(navigation.Value.Route!=Route.Conversation || current.Selection is not {} selection)return false;
        if(action.ActionId=="message-retry")
        {
            if(current.InitialReveal is {} pending && history.Snapshot.Items.Count==0)
            {
                var retry=pending with{Request=CreateFocusRequest()};
                messages.Update(value=>ReferenceEquals(value.InitialReveal,pending)?value with{InitialReveal=retry}:value);
            }
            history.Retry();return true;
        }
        if(action.ActionId=="message-focus-composer")
        {
            var request=CreateFocusRequest().FocusElement("discord.message.actions","discord.message.draft");
            messages.Update(value=>value with{InitialReveal=null,ControlFocus=new(request,selection,navigation.Value.Revision)});
            return true;
        }
        if(action.ActionId=="message-focus-call")
        {
            if(CurrentState() is {} callData && MessageSelectionCurrent(selection) &&
                ConversationCallButton(callData,selection) is {IsDisabled:not true})
            {
                var request=CreateFocusRequest().FocusElement("discord.message.header","discord.message.call");
                messages.Update(value=>value with{InitialReveal=null,ControlFocus=new(request,selection,navigation.Value.Revision)});
            }
            return true;
        }
        if(action.ActionId==MessageAction("message-cancel",current) && current.PendingNonce is not null && Operations.IsBusy("discord.message.send") && !current.CancellingSend)
        {
            messages.Update(value=>value with{CancellingSend=true,Notice=value.Uploading?"Cancelling upload…":"Stopping delivery check…"});
            Operations.Cancel("discord.message.send");return true;
        }
        if(TryMessageFileAction(action,current,token,out result))return true;
        if(action.ActionId==MessageAction("message-draft",current) && action.CommittedText is {Length:<=2000} draft)
        {
            if(draft.Any(c=>char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                messages.Update(value=>value with{Notice="The message contains an unsupported control character."});
            else messages.Update(value=>value.Selection==selection?value with{Draft=draft}:value);
            return true;
        }
        if(action.ActionId==MessageAction("message-send",current) && (current.Draft.Trim().Length>0 || current.Files is {Length:>0}) && current.PendingNonce is null)
        {SendMessage(current);return true;}
        if(action.ActionId==MessageAction("message-reconcile",current) && current.PendingNonce is not null)
        {SendMessage(current,reconcile:true);return true;}
        if(action.ActionId=="message-latest")
        {
            var visit=navigation.Value;
            var reveal=new InitialMessageReveal(CreateFocusRequest(),selection,visit.Revision,visit.RouteCancellationToken,ActiveCancellationToken);
            messages.Update(value=>value with{HistoryAnchor=null,NewMessages=false,LatestRequired=false,InitialReveal=reveal,ControlFocus=null});
            ResetMessageUpdates();ObserveMessages();history.Refresh();return true;
        }
        return false;
    }

    private static string IncomingFilesAction(MessageUi current)=>DomainAction("message-cancel-share",current.Query?.Account,current.Query?.Session.Id,current.IncomingOfferId,
        string.Join(",",(current.Incoming??[]).Select(file=>file.Id)));

    private async ValueTask OpenConversationAsync(ConversationQuery query,DiscordConversation item,WidgetActionEvent action,CancellationToken token,string? targetMessage=null,MessageUi? expectedIntake=null)
    {
        var unreadBoundary=targetMessage is null && item.Unread && item.OldestUnreadMessageId is not null;
        var entryTarget=targetMessage??(unreadBoundary?item.OldestUnreadMessageId:null);
        var route=navigation.Value;
        var intake=expectedIntake??messages.Value;
        lock(messageAdmissionGate)
        {
            if(token.IsCancellationRequested || !MessageAccountCurrent(query.Account,query.Session) || messages.Value.Query!=query ||
                messages.Value.IncomingOfferId!=intake.IncomingOfferId || !ReferenceEquals(messages.Value.Incoming,intake.Incoming))return;
            if(Operations.IsBusy("discord.message.send"))
            {messages.Update(value=>value with{Notice="Your current message is still sending. These files are safe here; choose a recipient when it finishes."});return;}
        }
        var channel=item.ChannelId;
        try
        {
            if(channel is null)
            {
                var json=await client!.InvokeAsync("messageOpenDm",new{account=query.Account,userId=item.UserId},token);
                var opened=json.Deserialize<DiscordOpenConversation>(MessageJson)??throw DiscordMessageValues.Invalid();
                if(opened.Account!=query.Account || opened.UserId!=item.UserId || !DiscordMessageValues.Id(opened.ChannelId))throw DiscordMessageValues.Invalid();
                channel=opened.ChannelId;
            }
            lock(messageAdmissionGate)
            {
                if(token.IsCancellationRequested || !MessageAccountCurrent(query.Account,query.Session) || navigation.Value!=route || messages.Value.Query!=query ||
                    messages.Value.IncomingOfferId!=intake.IncomingOfferId || !ReferenceEquals(messages.Value.Incoming,intake.Incoming))return;
                if(Operations.IsBusy("discord.message.send"))return;
                var previous=messages.Value;
                var sameConversation=previous.Selection is {} prior && prior.Account==query.Account && prior.Session==query.Session && prior.Channel==channel;
                var selection=new MessageSelection(query.Account,query.Session,channel,item.Name,item.CanSend);
                pendingMessages.TryGet(PendingKey(selection),out var pending);
                if(pending is not null && previous.IncomingFiles)
                {messages.Update(value=>value with{Notice="This conversation has an unconfirmed send. Check its delivery before adding these files, or choose another recipient."});return;}
                var saved=sameConversation?new DiscordMessageDraft(previous.Draft,previous.Files??[]):messageDrafts.Get(PendingKey(selection));
                var combined=CombineMessageFiles(saved?.Files??[],previous.Incoming??[]);
                if(previous.IncomingFiles)
                {
                    if(!selection.CanSend){messages.Update(value=>value with{Notice="Discord does not allow sending to this recipient. Choose another recipient."});return;}
                    if(combined.Length>10){messages.Update(value=>value with{Notice=$"This draft would have {combined.Length} attachments. Discord allows 10; remove files from this share or choose another recipient."});return;}
                    if(CurrentState()?.Messaging?.MaximumAttachmentBytes is {} maximum && combined.FirstOrDefault(file=>file.ByteLength>maximum) is {} oversized)
                    {messages.Update(value=>value with{Notice=$"{oversized.Name} exceeds this account's attachment size limit. Remove it to continue."});return;}
                }
                if(!sameConversation && previous.Selection is {} priorSelection && !messageDrafts.TryStore(PendingKey(priorSelection),previous.Draft,
                    (previous.Files??[]).Where(file=>!pendingMessages.HoldsFile(file.Id))))
                {messages.Update(value=>value with{Notice="You have 16 unsent conversation drafts. Finish or clear one before opening another draft."});return;}
                if(!sameConversation)messageDrafts.Remove(PendingKey(selection));
                // A draft belongs to one exact account/session/channel. Opening a
                // different recipient cannot silently carry text to that recipient.
                var retainedHistory=sameConversation && history.Snapshot is {Status:WidgetPagedResourceStatus.Ready,Items.Count:>0} &&
                    (entryTarget is null?history.Snapshot.After is null:history.Snapshot.Items.Any(message=>message.Id==entryTarget));
                if(!retainedHistory){history.Reset();ResetMessageUpdates();}
                var duplicateIncoming=(previous.Incoming??[]).Where(file=>!combined.Any(kept=>kept.Id==file.Id)).ToArray();
                messages.Update(value=>sameConversation?value with{Selection=selection,Files=previous.IncomingFiles?combined:value.Files,Incoming=null,IncomingOfferId=null,IncomingBinding=null,Notice="",InitialReveal=null,ControlFocus=null,HistoryAnchor=entryTarget,EntryUnreadBoundaryId=item.OldestUnreadMessageId,NewMessages=false,LatestRequired=false}:value with{Selection=selection,Draft=saved?.Text??pending?.Content??"",
                    Notice=pending is null?"":"Delivery has not been confirmed. Check delivery before sending again.",PendingNonce=pending?.Nonce,PendingContent=pending?.Content,NewMessages=false,LatestRequired=false,
                    Files=pending?.Files.ToArray()??combined,Incoming=null,IncomingOfferId=null,IncomingBinding=null,InitialReveal=null,ControlFocus=null,HistoryAnchor=entryTarget,EntryUnreadBoundaryId=item.OldestUnreadMessageId});
                ReleaseMessageFiles(duplicateIncoming);
                if(route.Route==Route.Conversation)
                    navigation.ReplaceCurrent(Route.Conversation);
                else if(targetMessage is not null)
                {
                    navigation.NavigateRoot(Route.Messages);
                    navigation.Push(Route.Conversation,"discord.messages.search");
                }
                else PushRoute(Route.Conversation,action);
                // Explicit recipient selection is a fresh entry intent even
                // when its latest window is resident. Child-route Back does
                // not call this method and retains its existing reading visit.
                {
                    // Allocate only for this explicit cold entry, before any readable
                    // history is loaded. Render derives its exact published target.
                    var visit=navigation.Value;
                    var reveal=new InitialMessageReveal(CreateFocusRequest(),selection,visit.Revision,visit.RouteCancellationToken,ActiveCancellationToken,entryTarget,unreadBoundary);
                    messages.Update(value=>value with{InitialReveal=reveal});
                    BeginReadVisit(selection,item.LastMessageId,targetMessage is not null);
                }
            }
            ObserveMessages();history.EnsureLoaded();
        }
        catch(Exception) when(!token.IsCancellationRequested)
        {
            lock(messageAdmissionGate)
                if(navigation.Value==route && MessageAccountCurrent(query.Account,query.Session) && messages.Value.Query==query && messages.Value.IncomingOfferId==intake.IncomingOfferId && ReferenceEquals(messages.Value.Incoming,intake.Incoming))
                    messages.Update(value=>value with{Notice="Could not open this conversation. Try again or open Discord."});
        }
    }

    private void SendMessage(MessageUi captured,bool reconcile=false)
    {
        var selected=captured.Selection!;
        if(!MessageSelectionCurrent(selected) || !reconcile && (!selected.CanSend || CurrentState()?.Messaging?.CanSend!=true))return;
        var visit=navigation.Value;
        var active=ActiveCancellationToken;
        Operations.RunSingleFlight("discord.message.send",async operation=>
        {
            if(!MessageSelectionCurrent(selected))return;
            // A submitted nonce survives an ambiguous response. Retry performs
            // read-only reconciliation; it never creates a second send.
            var nonce=captured.PendingNonce??System.Security.Cryptography.RandomNumberGenerator.GetInt32(1,int.MaxValue).ToString(CultureInfo.InvariantCulture)+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            var content=captured.PendingContent??captured.Draft;
            var key=PendingKey(selected);
            if(!reconcile && !pendingMessages.TryAdmit(key,nonce,content,captured.Files,out _))
            {messages.Update(value=>value with{Notice="Check delivery of the pending messages before sending another."});return;}
            if(reconcile && (!pendingMessages.TryGet(key,out var pending) || pending?.Nonce!=nonce))return;
            var uploading=!reconcile && captured.Files is {Length:>0};
            messages.Update(value=>value.Selection==selected?value with{PendingNonce=nonce,PendingContent=content,Uploading=uploading,CancellingSend=false,
                Notice=reconcile?"Checking delivery…":uploading?"Uploading files… 0%":"Sending…"}:value);
            var submitted=reconcile;
            try
            {
                var upload=uploading?await UploadMessageFilesAsync(selected,captured.Files!,percent=>
                {
                    if(operation.IsCurrent && MessageSelectionCurrent(selected))messages.Update(value=>value.PendingNonce==nonce && !value.CancellingSend
                        ?value with{Notice=$"Uploading files… {percent}%"}:value);
                },operation.CancellationToken):null;
                operation.CancellationToken.ThrowIfCancellationRequested();
                if(!MessageSelectionCurrent(selected))return;
                messages.Update(value=>value.PendingNonce==nonce?value with{Uploading=false,Notice=reconcile?"Checking delivery…":"Sending…"}:value);
                submitted=true;
                var json=await client!.InvokeAsync(reconcile?"messageReconcile":"messageSend",new{account=selected.Account,channelId=selected.Channel,nonce,content,
                    attachmentBatchId=upload?.BatchId,attachments=upload?.Files.Select(file=>new{id=file.Id,fileName=file.FileName,uploadedFileName=file.UploadedFileName}).ToArray()},operation.CancellationToken);
                var sent=json.Deserialize<DiscordMessageSendResult>(MessageJson)??throw DiscordMessageValues.Invalid();
                sent.Validate(selected.Account,selected.Channel,nonce);
                if(sent.Status is "sent" or "failed")pendingMessages.TryResolve(key,nonce,out _);
                if(!MessageSelectionCurrent(selected)){if(sent.Status=="sent")ReleaseMessageFiles(captured.Files);return;}
                var completed=messages.Update(value=>value.PendingNonce!=nonce?(value,false):sent.Status=="sent"
                    ?(value with{Draft=value.Draft==content?"":value.Draft,PendingNonce=null,PendingContent=null,Notice="",Files=null},true)
                    :sent.Status=="failed"?(value with{PendingNonce=null,PendingContent=null,Notice=""},true)
                    :(value with{Notice="Delivery has not been confirmed. Check delivery before sending again."},false));
                if(completed.Result)PublishFeedback(sent.Status=="sent"?"Message sent.":"Message was not sent. Your draft is saved.");
                if(sent.Status=="sent" && sent.Message is {} committed)
                {
                    var reveal=completed.Result && visit.Route==Route.Conversation && visit==navigation.Value && !active.IsCancellationRequested
                        ?new InitialMessageReveal(CreateFocusRequest(),selected,visit.Revision,visit.RouteCancellationToken,active,committed.Id,SentMessage:true):null;
                    ApplySentMessage(selected,committed,reveal);
                }
                if(sent.Status=="sent")ReleaseMessageFiles(captured.Files);
            }
            catch(OperationCanceledException) when(operation.CancellationToken.IsCancellationRequested)
            {
                if(!submitted)pendingMessages.TryResolve(key,nonce,out _);
                if(MessageSelectionCurrent(selected))
                {
                    var cancelled=messages.Update(value=>value.PendingNonce!=nonce?(value,false):submitted
                        ?(value with{Notice="Stopped waiting. Delivery may have completed. Check delivery before sending again."},false)
                        :(value with{PendingNonce=null,PendingContent=null,Notice=""},true));
                    if(cancelled.Result)PublishFeedback("Upload cancelled. Your draft and files are kept.");
                }
            }
            catch(DiscordCommandException error) when(!reconcile && error.Code is "recipient_unavailable" or "invalid_conversation" or "conversation_unavailable" or
                "messaging_unavailable" or "invalid_message" or "invalid_attachments" or "attachment_batch_unavailable" or "message_send_capacity")
            {
                pendingMessages.TryResolve(key,nonce,out _);
                CompleteFailedMessage(selected,nonce,"Message was not sent. Your draft and files are kept.");
            }
            catch(Exception)
            {
                if(!submitted)pendingMessages.TryResolve(key,nonce,out _);
                if(!submitted)CompleteFailedMessage(selected,nonce,"Attachment upload failed. Your draft and files are kept; try again.");
                else if(MessageSelectionCurrent(selected))messages.Update(value=>value.PendingNonce!=nonce?value:
                    value with{Notice="Delivery has not been confirmed. Check delivery before sending again."});
            }
            finally
            {
                if(MessageSelectionCurrent(selected))messages.Update(value=>value.PendingNonce is null || value.PendingNonce==nonce
                    ?value with{Uploading=false,CancellingSend=false}:value);
            }
        },WidgetOperationLifetime.Widget);
    }

    private void CompleteFailedMessage(MessageSelection selected,string nonce,string feedbackText)
    {
        if(!MessageSelectionCurrent(selected))return;
        var completed=messages.Update(value=>value.PendingNonce!=nonce?(value,false):
            (value with{PendingNonce=null,PendingContent=null,Notice=""},true));
        if(completed.Result)PublishFeedback(feedbackText);
    }

    internal Task WaitForMessageSendAsync(CancellationToken token=default)=>Operations.WhenIdleAsync("discord.message.send",token);

    private WidgetElement ConversationList(ConversationQuery query,MessageUi current,bool sidebar=false)
    {
            var snapshot=conversations.GetSnapshot(query);
            var search=new List<WidgetElement>();
            if(!sidebar)search.Add(UI.IconButton(WidgetGlyph.Back,navigation.Value.BackActionId??"discord.section.call","discord.messages.back",
                navigation.Value.CanGoBack?"Back":"Back to Servers").Classes("discord-control","discord-message-back"));
            var searchInput=UI.TextEntry(query.Search,"Search","message-search","discord.messages.search",120).Classes("discord-search");
            search.Add(sidebar?searchInput.FocusRight("discord.message.draft"):searchInput);
            var content=new List<WidgetElement>();
            if(current.IncomingFiles)content.Add(UI.Row("discord.messages.header",
                UI.Text("Choose recipient","discord.messages.title").Classes("discord-section-title","discord-copy"),
                UI.Button("Cancel sharing",IncomingFilesAction(current),"discord.messages.cancel-share").Classes("discord-control")).Classes("discord-message-header"));
            content.Add(UI.Row("discord.messages.search.row",search.ToArray()));
            if(snapshot.Value is {} catalog)content.Add(SectionTitle("discord.messages.heading",query.Search.Length==0?"Recent conversations":"Conversations",catalog.Context.TotalCount.ToString(CultureInfo.CurrentCulture))
                .VisibleWhen(ResponsiveVisibility.ExpandedOnly));
            content.Add(ConversationCollection(query,current,snapshot.Value,sidebar));
            if(snapshot.Value?.Context.TotalCount is not >0)
                content.Add(UI.Text(snapshot.Error?.Message??(snapshot.HasValue?"No conversations found.":"Loading conversations…"),"discord.messages.status"));
            var notice=!sidebar&&current.Notice.Length>0?current.Notice:current.IncomingFiles?$"Choose a recipient for {current.Incoming?.Length??0} shared file(s).":
                snapshot.Value?.Context.Truncated==true?"Search to narrow the available conversations.":"";
            if(notice.Length>0)content.Add(UI.Text(notice,"discord.messages.notice").Classes("discord-secondary"));
            if(current.IncomingFiles)content.Add(IncomingFilesPanel(current));
            return UI.Stack("discord.messages.page",content.ToArray()).Classes("discord-page");
    }

    private WidgetElement MessagePage(DiscordState data,Route route,out string first,out ScrollRevealRequest? reveal)
    {
        reveal=null;
        first="discord.messages.back";
        var current=messages.Value;
        var back=UI.IconButton(WidgetGlyph.Back,navigation.Value.BackActionId??"discord.section.call","discord.messages.back","Back").Classes("discord-control","discord-message-back");
        if(current.Query is not {} query || !MessageAccountCurrent(query.Account,query.Session))
            return UI.Stack("discord.messages.unavailable",back,UI.Text("Messages are unavailable.","discord.messages.status"));
        if(route==Route.Messages)
        {
            first="discord.messages.search";
            return ConversationList(query,current);
        }
        var selected=current.Selection;
        if(selected is null)return UI.Stack("discord.messages.empty",back);
        const string sidebarTarget="discord.messages.list";
        back=back.FocusLeft(sidebarTarget);
        first="discord.message.draft";
        var page=history.Snapshot;
        var lines=page.Items.Select((item,index)=>new MessageLine(item,
            index==0 || page.Items[index-1].AuthorId!=item.AuthorId || item.Timestamp-page.Items[index-1].Timestamp>300000 || MessageDate(page.Items[index-1])!=MessageDate(item),
            index==0 || MessageDate(page.Items[index-1])!=MessageDate(item),current.RevealedSpoilers?.Contains(item.Id)==true,
            current.EntryUnreadBoundaryId==item.Id)).ToArray();
        messageRows.Publish(selected.Session.Id+":"+selected.Account+":"+selected.Channel,selected.Channel,lines);
        var list=UI.CollectionList("discord.message.history",messageRows,60,"Messages")
            .DirectionalEntry(IndexedDirectionalEntryMode.VisibleEdge)
            .FocusLeft(sidebarTarget)
            .AdjacentPages(page.Error is null&&page.HasBefore,page.Error is null&&page.HasAfter).Classes("discord-list","discord-message-history");
        if(current.InitialReveal is {} initial && CurrentMessageEntry(initial))
        {
            if(page.Status is WidgetPagedResourceStatus.Loading or WidgetPagedResourceStatus.Refreshing)
                reveal=initial.Request.DeferCollectionReveal(messageRows,"discord.message.history");
            else if(page is {Status:WidgetPagedResourceStatus.Ready,Items.Count:>0})
            {
                var target=initial.MessageId is null?page.Items.Count-1:page.Items.ToList().FindIndex(item=>item.Id==initial.MessageId);
                if(target>=0)reveal=initial.Request.RevealCollection(messageRows,"discord.message.history",
                    messageRows.FocusTarget("discord.message.history",new(page.Items[target].Id),target));
            }
        }
        var header=new List<WidgetElement>{back,UI.Text(selected.Name,"discord.message.title").Classes("discord-section-title","discord-copy")};
        if(ConversationCallButton(data,selected) is {} call)
        {
            header.Add(call);
            if(call.IsDisabled!=true)header.Add(UI.ControllerGlyph(ControllerButton.LeftStick,"discord.message.call.hint","Focus call button"));
        }
        if(page.Status is WidgetPagedResourceStatus.Loading or WidgetPagedResourceStatus.LoadingAdjacent)
            header.Add(UI.Text(page.Status==WidgetPagedResourceStatus.Loading?"Loading messages…":page.LoadingDirection==WidgetCursorDirection.Before?"Loading older…":"Loading newer…",
                "discord.message.boundary.status").Classes("discord-secondary"));
        else if(page.Status==WidgetPagedResourceStatus.Ready && page.Items.Count>0 && !page.HasBefore && messageRows.ViewportState.IsAtStart)
            header.Add(UI.Text("Beginning of conversation","discord.message.boundary.end").Classes("discord-secondary"));
        var children=new List<WidgetElement>{
            UI.Row("discord.message.header",header.ToArray()).Classes("discord-message-header"),
            list};
        if(page.Status==WidgetPagedResourceStatus.Ready && page.Items.Count==0)children.Add(UI.Text("No messages yet","discord.message.empty").Classes("discord-secondary"));
        if(page.Error is not null)children.Add(UI.Button("Try again","message-retry","discord.message.retry").Classes("discord-control"));
        var missingNotification=page.Status==WidgetPagedResourceStatus.Ready && current.InitialReveal is {UnreadBoundary:false,MessageId:{} requestedMessage} &&
            !page.Items.Any(item=>item.Id==requestedMessage);
        if((page.Error?.Message??(missingNotification?"The requested message is unavailable.":current.Notice)) is {Length:>0} notice)
            children.Add(UI.Text(notice,"discord.message.status").Classes("discord-secondary"));
        if(current.Files is {Length:>0})children.Add(MessageFiles(current));
        var actions=new List<WidgetElement>{UI.TextEntry(current.Draft,"Write a message…",MessageAction("message-draft",current),"discord.message.draft",2000)
            .FocusLeft(sidebarTarget).Classes("discord-message-composer")};
        if(current.NewMessages || current.LatestRequired || page.HasAfter || messageRows.ViewportState is {ActiveViews:>0,IsAtEnd:false})
            actions.Add(UI.Button(current.NewMessages?"New messages":"Latest","message-latest","discord.message.latest")
                .Icon(DiscordIcons.Latest,"Jump to latest messages").Classes("discord-control"));
        actions.Add(Operations.IsBusy("discord.message.send") && current.PendingNonce is not null
                    ?UI.Button(current.Uploading?"Cancel upload":"Stop waiting",MessageAction("message-cancel",current),"discord.message.cancel")
                        .Disabled(current.CancellingSend).Classes("discord-control")
                    :UI.IconButton(WidgetGlyph.Attach,MessageAction("message-attach",current),"discord.message.attach","Attach files").Disabled(current.PendingNonce is not null || Operations.IsBusy("discord.message.files") || current.Files is {Length:>=10}).Classes("discord-control"));
        actions.Add((current.PendingNonce is null?UI.IconButton(WidgetGlyph.Send,MessageAction("message-send",current),"discord.message.send","Send message"):
                    UI.Button("Check delivery",MessageAction("message-reconcile",current),"discord.message.send"))
                    .Disabled(Operations.IsBusy("discord.message.send") || current.PendingNonce is null &&
                        (data.Messaging?.CanSend!=true || !selected.CanSend || current.Draft.Trim().Length==0 && current.Files is not {Length:>0}))
                    .Classes("discord-control","discord-primary"));
        children.Add(UI.Row("discord.message.actions",actions.ToArray()).Classes("discord-actions"));
        return UI.Stack("discord.message.page",children.ToArray()).Classes("discord-page");
    }

    private static WidgetContextAction[] MessageCommands()=>[new("refresh","Refresh Discord"),new("account","Open Discord")];

    private bool CurrentMessageEntry(InitialMessageReveal? entry)=>entry is not null && entry.Selection==messages.Value.Selection &&
        entry.NavigationRevision==navigation.Value.Revision && !entry.RouteLifetime.IsCancellationRequested &&
        !entry.ActiveLifetime.IsCancellationRequested && IsActive;

    private static DateTime MessageDate(DiscordMessage item) => DateTimeOffset.FromUnixTimeMilliseconds(item.Timestamp).ToLocalTime().Date;
    private static WidgetElement MessageRow(MessageLine line,WidgetIndexedItemContext ids)
    {
        var item=line.Message;
        var mediaItems=MessageMediaItems(line);
        var mediaUrls=mediaItems.Select(media=>media.Source.Location!).ToHashSet(StringComparer.Ordinal);
        if(!HideMessageEmbeds(line))mediaUrls.UnionWith(item.Embeds.Where(embed=>embed.IsAnimation).Select(embed=>embed.Url).OfType<string>());
        var textParts=DiscordMessageText.Parse(item.Content,item.Mentions,line.RevealSpoilers,item.ContentTruncated);
        var cardTargets=MessageCardTargets(line,mediaUrls,textParts);
        var cardUrls=cardTargets.Values.ToHashSet(StringComparer.Ordinal);
        var linkTargets=new HashSet<string>(StringComparer.Ordinal);
        var body=new List<WidgetElement>();
        if(line.ShowAuthor)body.Add(UI.Row(ids.Id("heading"),
            UI.Text(item.IsOwn?"You":item.AuthorName,ids.Id("author")).Classes("discord-message-author"),
            UI.Text(DateTimeOffset.FromUnixTimeMilliseconds(item.Timestamp).ToLocalTime().ToString("t",CultureInfo.CurrentCulture)+(item.EditedTimestamp is null?"":" · edited"),ids.Id("time")).Classes("discord-message-time"))
            .Classes("discord-message-heading"));
        if(item.Reply is {} reply)body.Add(UI.Text("↪ "+reply.Author+": "+(reply.Unavailable?"Original message unavailable":
            MessageExcerpt(reply.Content,line.RevealSpoilers)),ids.Id("reply")).Classes("discord-message-reply"));
        if(item.SystemText is {} system)body.Add(UI.Text(system,ids.Id("system")).Classes("discord-secondary"));
        if(MessageRichText(textParts,ids,linkTargets,mediaUrls,cardUrls) is {} text)body.Add(text);
        foreach(var file in item.Attachments)
        {
            if(!line.RevealSpoilers && file.HasSpoiler)
            {body.Add(UI.Text("[Spoiler attachment]",ids.Id("attachment-hidden-"+file.Id)).Classes("discord-text-spoiler"));continue;}
            if(file.HasAnimation && file.ContentType is "image/gif" or "image/png" or "image/webp" && file.Url is {} gif)
                body.Add(UI.PublicImage(gif,ids.Id("attachment-animation-"+file.Id),file.FileName,ImageFit.Contain).Classes("discord-message-image"));
            else if(file.HasAnimation && file.ContentType is "video/mp4" or "video/webm" && file.Url is {} animation)
                body.Add(UI.PublicAnimation(animation,ids.Id("attachment-animation-"+file.Id),file.FileName).Classes("discord-message-image"));
            if(mediaItems.Any(media=>media.Id=="attachment."+file.Id))continue;
            body.Add(file.Url is {} url && linkTargets.Add(url)
                ?UI.Button(file.FileName,ids.Id("file-action-"+file.Id),ids.Id("file-"+file.Id))
                    .OpenIntent(WidgetIntentContracts.Web,JsonSerializer.SerializeToElement(new{url})).Classes("discord-message-file-link")
                :UI.Text(file.FileName+(file.Url is null?" · Link unavailable":""),ids.Id("file-"+file.Id)).Classes("discord-secondary"));
        }
        AddMessageExtras(line,ids,body,linkTargets,cardTargets);
        // Inline animations already expose the complete content. A separate
        // gallery action only earns a focus stop when there is other media.
        if (MessageInlineVideo(line,ids,mediaItems) is { } inlineVideo) body.Add(inlineVideo);
        else if(mediaItems.Any(media=>media.Options?.IsAnimation!=true))
        {
            var label=$"Open {mediaItems.Count} attachment"+(mediaItems.Count==1?"":"s")+": "+
                string.Join(" · ",mediaItems.Take(2).Select(media=>media.Label));
            body.Add(UI.ActionSurface("message.media.open",ids.Id("media-open"),label,ActionSurfaceOrientation.Vertical,
                MessageMediaThumbnails(line,ids,mediaItems))
                .Classes("discord-message-attachments","discord-message-gallery-action"));
        }
        if(item.ContentTruncated)body.Add(UI.Text("Message shortened · Open Discord to read all",ids.Id("truncated")).Classes("discord-secondary"));
        if(body.Count==0)body.Add(UI.Text("Message content is available in Discord",ids.Id("unsupported")).Classes("discord-secondary"));
        var avatar=line.ShowAuthor?Avatar(item.AvatarUrl,ids.Id("avatar"),item.AuthorName).AddClasses("discord-message-avatar"):
            UI.Spacer(ids.Id("avatar-space")).Classes("discord-message-avatar");
        var bubble=UI.Stack(ids.Id("bubble"),body.ToArray()).Classes("discord-message-bubble");
        // Star measurement wraps long text to available width; alignment keeps
        // short bubbles content-sized without adding another layout wrapper.
        var content=UI.Grid(ids.Id("content"),[],[GridTrack.Auto(),GridTrack.Star()],
            bubble.InGrid(column:1).AlignInGrid(GridCellAlignment.Start,GridCellAlignment.Start),
            avatar.InGrid(column:0).AlignInGrid(GridCellAlignment.Start,GridCellAlignment.Start))
            .Spacing(0,8).Classes("discord-message-content");
        var children=new List<WidgetElement>();
        if(line.ShowDate)children.Add(UI.Text(MessageDate(item).ToString("D",CultureInfo.CurrentCulture),ids.Id("date")).Classes("discord-message-date"));
        if(line.ShowUnread)children.Add(UI.Text("Unread messages",ids.Id("unread")).Classes("discord-message-unread"));
        children.Add(content);
        var row=UI.ReadingGroup(ids.Id("row"),item.AuthorName+", message",ActionSurfaceOrientation.Vertical,children.ToArray())
            .FocusVisualTarget(ids.Id("bubble")).Classes("discord-message-row");
        if(item.Content.Contains("||",StringComparison.Ordinal) || item.Reply?.Content.Contains("||",StringComparison.Ordinal)==true ||
            item.Embeds.Any(embed=>embed.Description.Contains("||",StringComparison.Ordinal)) ||
            item.Attachments.Any(file=>file.HasSpoiler))row=row.ContextAction(line.RevealSpoilers?"message.spoiler.hide":"message.spoiler.show",
            line.RevealSpoilers?"Hide spoilers":"Reveal spoilers").ContextMenuShortcut(ControllerButton.Menu);
        return row;
    }

    private ValueTask MessageRowAction(string channel,MessageLine line,WidgetActionEvent action,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(messages.Value.Selection is not { } selected || selected.Channel!=channel || !MessageSelectionCurrent(selected))return ValueTask.CompletedTask;
        if(action.ActionId=="message.media.open"){OpenMessageMedia(line,action);return ValueTask.CompletedTask;}
        if(action.ActionId is "message.spoiler.show" or "message.spoiler.hide")
            messages.Update(state=>state with{RevealedSpoilers=action.ActionId=="message.spoiler.show"
                ?(state.RevealedSpoilers??[]).Append(line.Message.Id).Distinct().TakeLast(40).ToArray()
                :(state.RevealedSpoilers??[]).Where(id=>id!=line.Message.Id).ToArray()});
        return ValueTask.CompletedTask;
    }
    private static WidgetElement? MessageRichText(IReadOnlyList<DiscordTextPart> parts,WidgetIndexedItemContext ids,
        HashSet<string> links,HashSet<string> mediaUrls,HashSet<string> cardUrls)
    {
        var spans=new List<WidgetElement>();var emojiCount=0;var linkCount=0;
        var visible=parts.Where(part=>part.Url is null || part.Text!=part.Url || !(mediaUrls.Contains(part.Url)||cardUrls.Contains(part.Url))).ToList();
        if(visible.Count>0&&visible.Count!=parts.Count)visible[^1]=visible[^1] with {Text=visible[^1].Text.TrimEnd()};
        foreach(var part in visible)
        {
            var id=ids.Id("span-"+spans.Count);
            if(part.EmojiId is { } emoji && emojiCount++<4)
            {spans.Add(UI.PublicImage("https://cdn.discordapp.com/emojis/"+emoji+".png?size=32",id,part.Text,ImageFit.Contain));continue;}
            var classes=new List<string>();
            if(part.Style.HasFlag(DiscordTextStyle.Bold))classes.Add("discord-text-bold");
            if(part.Style.HasFlag(DiscordTextStyle.Italic))classes.Add("discord-text-italic");
            if(part.Style.HasFlag(DiscordTextStyle.Underline))classes.Add("discord-text-underline");
            if(part.Style.HasFlag(DiscordTextStyle.Strike))classes.Add(part.Style.HasFlag(DiscordTextStyle.Underline)?"discord-text-decorated":"discord-text-strike");
            if(part.Style.HasFlag(DiscordTextStyle.Code))classes.Add("discord-text-code");
            if(part.Style.HasFlag(DiscordTextStyle.Spoiler))classes.Add("discord-text-spoiler");
            if(part.Style.HasFlag(DiscordTextStyle.Quote))classes.Add("discord-text-quote");
            if(part.Url is {} url && (mediaUrls.Contains(url)||cardUrls.Contains(url)))
            {if(part.Text!=url)spans.Add(UI.Text(part.Text,id).Classes(classes.ToArray()));continue;}
            if(part.Url is not null)classes.Add("discord-text-link");
            spans.Add(part.Url is {} destination && linkCount<8 && links.Add(destination)
                ?UI.Button(part.Text,ids.Id("inline-link-"+linkCount++),id)
                    .OpenIntent(WidgetIntentContracts.Web,JsonSerializer.SerializeToElement(new{url=destination})).Classes(classes.ToArray())
                :UI.Text(part.Text,id).Classes(classes.ToArray()));
        }
        return spans.Count==0?null:UI.RichText(ids.Id("text"),spans.ToArray()).Classes("discord-message-text");
    }
}
