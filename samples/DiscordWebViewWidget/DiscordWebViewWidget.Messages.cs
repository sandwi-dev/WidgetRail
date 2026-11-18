using System.Globalization;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private sealed record ConversationQuery(string Account,WebSessionReference Session,string Search="",int Offset=0,string? Version=null);
    private sealed record MessageSelection(string Account,WebSessionReference Session,string Channel,string Name,bool CanSend=true);
    private sealed record MessageLine(DiscordMessage Message,bool ShowAuthor,bool ShowDate,bool RevealSpoilers,bool ShowUnread=false);
    private sealed record InitialMessageReveal(WidgetFocusRequest Request,MessageSelection Selection,long NavigationRevision,
        CancellationToken RouteLifetime,CancellationToken ActiveLifetime,string? MessageId=null,bool UnreadBoundary=false);
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
        MessageControlFocus? ControlFocus=null,string? EntryUnreadBoundaryId=null)
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
                var json=await client!.InvokeAsync("messageConversations",new{account=query.Account,query=query.Search,offset=query.Offset,version=query.Version,limit=40},token);
                var page=json.Deserialize<DiscordConversationPage>(MessageJson)??throw DiscordMessageValues.Invalid();
                page.Validate(query.Account,query.Offset,40);
                DemandMessageAccount(query.Account,query.Session);
                return page;
            },
            MapError=_=>new("discord.messages.unavailable","Messages are unavailable. Try Refresh or open Discord."),
            CacheDuration=TimeSpan.FromSeconds(30)
        });
        history=CreateCursorResource<DiscordMessage>("discord.message.history",new()
        {
            PageSize=20,MaximumRetainedItems=40,
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
                Operations.Cancel("discord.message.send");
                var retired=pendingMessages.Clear().Concat(messageDrafts.Clear()).Concat(current.Files??[]).Concat(current.Incoming??[]).DistinctBy(file=>file.Id).ToArray();
                messages.Update(_=>new MessageUi());ReleaseMessageFiles(retired);conversations.Reset();history.Reset();ResetMessageUpdates();
                observedCatalogRevision=observedMessageRevision=-1;
                return;
            }
            if(next.Messaging is not {} messaging)return;
            if(navigation.Value.Route==Route.Messages && current.Query is {} active && observedCatalogRevision!=messaging.CatalogRevision)
            {
                var first=active with{Offset=0,Version=null};
                messages.Update(value=>value with{Query=first});conversations.EnsureLoaded(first,forceRefresh:true);
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
        if(navigation.Value.Route==Route.Messages)conversations.EnsureLoaded(query);
        else if(navigation.Value.Route==Route.Conversation)history.EnsureLoaded();
        ObserveMessages();
    }

    private bool TryMessageAction(WidgetActionEvent action,CancellationToken token,out ValueTask result)
    {
        result=ValueTask.CompletedTask;
        if(action.ActionId=="messages" && CurrentState() is {Messaging.CanRead:true} data && client?.Reference is {} session)
        {
            var query=new ConversationQuery(data.Account,session);
            messages.Update(value=>value.Query?.Account==data.Account && value.Query.Session==session?value with{Query=query}:new(Query:query));
            conversations.EnsureLoaded(query);PushRoute(Route.Messages,action);ObserveMessages();return true;
        }
        var current=messages.Value;
        if(navigation.Value.Route==Route.Messages && TryIncomingFileAction(action,current))return true;
        if(navigation.Value.Route==Route.Messages && current.IncomingFiles && action.ActionId==IncomingFilesAction(current))
        {
            CancelIncomingShare(current);return true;
        }
        if(current.Query is not {} selectedQuery || !MessageAccountCurrent(selectedQuery.Account,selectedQuery.Session))return false;
        if(navigation.Value.Route==Route.Messages)
        {
            if(action.ActionId=="message-search" && action.CommittedText is {Length:<=120} text)
            {
                var query=selectedQuery with{Search=text,Offset=0,Version=null};messages.Update(value=>value with{Query=query});conversations.EnsureLoaded(query);return true;
            }
            var snapshot=conversations.GetSnapshot(selectedQuery);
            if(snapshot.HasValue)
            {
                var page=snapshot.Value!;
                foreach(var item in page.Items)
                if(action.ActionId==ConversationAction(selectedQuery,item,current))
                {result=OpenConversationAsync(selectedQuery,item,action,token,expectedIntake:current);return true;}
                if(action.ActionId==ConversationPageAction(selectedQuery,-40) && selectedQuery.Offset>0 ||
                    action.ActionId==ConversationPageAction(selectedQuery,40) && page.Context.HasMore)
                {
                    var offset=action.ActionId==ConversationPageAction(selectedQuery,40)?selectedQuery.Offset+40:Math.Max(0,selectedQuery.Offset-40);
                    var query=selectedQuery with{Offset=offset,Version=page.Context.Version};messages.Update(value=>value with{Query=query});conversations.EnsureLoaded(query);return true;
                }
            }
            if(action.ActionId=="message-refresh")
            {var first=selectedQuery with{Offset=0,Version=null};messages.Update(value=>value with{Query=first});conversations.EnsureLoaded(first,forceRefresh:true);return true;}
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
        if(action.ActionId is "message-focus-header" or "message-focus-composer")
        {
            var composer=action.ActionId=="message-focus-composer";
            var request=CreateFocusRequest().FocusElement(composer?"discord.message.actions":"discord.message.header",
                composer?"discord.message.draft":"discord.messages.back");
            messages.Update(value=>value with{InitialReveal=null,ControlFocus=new(request,selection,navigation.Value.Revision)});
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
            messages.Update(value=>value with{HistoryAnchor=null,NewMessages=false,InitialReveal=reveal,ControlFocus=null});
            ResetMessageUpdates();ObserveMessages();history.Refresh();return true;
        }
        return false;
    }

    private static string ConversationAction(ConversationQuery query,DiscordConversation item,MessageUi current)=>
        DomainAction("message-open",query.Account,query.Session.Id,query.Search,query.Offset.ToString(CultureInfo.InvariantCulture),item.Id,item.ChannelId,item.UserId,current.IncomingOfferId,string.Join(",",(current.Incoming??[]).Select(file=>file.Id)));
    private static string ConversationPageAction(ConversationQuery query,int offset)=>
        DomainAction("message-page",query.Account,query.Session.Id,query.Search,query.Offset.ToString(CultureInfo.InvariantCulture),offset.ToString(CultureInfo.InvariantCulture));
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
                messages.Update(value=>sameConversation?value with{Selection=selection,Files=previous.IncomingFiles?combined:value.Files,Incoming=null,IncomingOfferId=null,IncomingBinding=null,Notice="",InitialReveal=null,ControlFocus=null,HistoryAnchor=entryTarget,EntryUnreadBoundaryId=item.OldestUnreadMessageId}:value with{Selection=selection,Draft=saved?.Text??pending?.Content??"",
                    Notice=pending is null?"":"Delivery has not been confirmed. Check delivery before sending again.",PendingNonce=pending?.Nonce,PendingContent=pending?.Content,NewMessages=false,
                    Files=pending?.Files.ToArray()??combined,Incoming=null,IncomingOfferId=null,IncomingBinding=null,InitialReveal=null,ControlFocus=null,HistoryAnchor=entryTarget,EntryUnreadBoundaryId=item.OldestUnreadMessageId});
                ReleaseMessageFiles(duplicateIncoming);
                if(targetMessage is not null)
                {
                    navigation.NavigateRoot(Route.Home);
                    navigation.Push(Route.Messages,"discord.messages.open");
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
                messages.Update(value=>value.PendingNonce!=nonce?value:sent.Status=="sent"
                    ?value with{Draft=value.Draft==content?"":value.Draft,PendingNonce=null,PendingContent=null,Notice="Sent",Files=null}
                    :sent.Status=="failed"?value with{PendingNonce=null,PendingContent=null,Notice="Message was not sent. Your draft is saved."}
                    :value with{Notice="Delivery has not been confirmed. Check delivery before sending again."});
                if(sent.Status=="sent" && sent.Message is {} committed)ApplySentMessage(selected,committed);
                if(sent.Status=="sent")ReleaseMessageFiles(captured.Files);
            }
            catch(OperationCanceledException) when(operation.CancellationToken.IsCancellationRequested)
            {
                if(!submitted)pendingMessages.TryResolve(key,nonce,out _);
                if(MessageSelectionCurrent(selected))messages.Update(value=>value.PendingNonce!=nonce?value:submitted
                    ?value with{Notice="Stopped waiting. Delivery may have completed. Check delivery before sending again."}
                    :value with{PendingNonce=null,PendingContent=null,Notice="Upload cancelled. Your draft and files are kept."});
            }
            catch(DiscordCommandException error) when(!reconcile && error.Code is "recipient_unavailable" or "invalid_conversation" or "conversation_unavailable" or
                "messaging_unavailable" or "invalid_message" or "invalid_attachments" or "attachment_batch_unavailable" or "message_send_capacity")
            {
                pendingMessages.TryResolve(key,nonce,out _);
                if(MessageSelectionCurrent(selected))messages.Update(value=>value.PendingNonce==nonce?
                    value with{PendingNonce=null,PendingContent=null,Notice="Message was not sent. Your draft and files are kept."}:value);
            }
            catch(Exception)
            {
                if(!submitted)pendingMessages.TryResolve(key,nonce,out _);
                if(MessageSelectionCurrent(selected))messages.Update(value=>value.PendingNonce!=nonce?value:submitted
                    ?value with{Notice="Delivery has not been confirmed. Check delivery before sending again."}
                    :value with{PendingNonce=null,PendingContent=null,Notice="Attachment upload failed. Your draft and files are kept; try again."});
            }
            finally
            {
                if(MessageSelectionCurrent(selected))messages.Update(value=>value.PendingNonce is null || value.PendingNonce==nonce
                    ?value with{Uploading=false,CancellingSend=false}:value);
            }
        },WidgetOperationLifetime.Widget);
    }

    private WidgetElement MessagePage(DiscordState data,Route route,out string first,out ScrollRevealRequest? reveal)
    {
        reveal=null;
        first="discord.messages.back";
        var current=messages.Value;
        var back=UI.IconButton(WidgetGlyph.Back,navigation.Value.BackActionId!,"discord.messages.back","Back").Classes("discord-control","discord-message-back");
        if(current.Query is not {} query || !MessageAccountCurrent(query.Account,query.Session))
            return UI.Stack("discord.messages.unavailable",back,UI.Text("Messages are unavailable.","discord.messages.status"));
        if(route==Route.Messages)
        {
            first="discord.messages.search";
            var snapshot=conversations.GetSnapshot(query);
            var rows=new List<WidgetElement>();
            if(snapshot.HasValue)
                foreach(var item in snapshot.Value!.Items)
                    rows.Add(UI.ActionSurface(ConversationAction(query,item,current),"discord.conversation."+item.Id,item.Name,ActionSurfaceOrientation.Horizontal,
                        Avatar(item.AvatarUrl,"discord.conversation.icon."+item.Id,item.Name),
                        UI.Stack("discord.conversation.copy."+item.Id,UI.Text(item.Name,"discord.conversation.name."+item.Id).Classes("discord-row-title"),
                            UI.Text(item.Unread?"Unread":item.Kind=="group"?"Group message":item.Kind=="friend"?"Friend":"Direct message","discord.conversation.detail."+item.Id).Classes("discord-secondary")))
                        .Classes("discord-row"));
            if(rows.Count==0)rows.Add(UI.Text(snapshot.Error?.Message??(snapshot.HasValue?"No conversations found.":"Loading conversations…"),"discord.messages.status"));
            return UI.Stack("discord.messages.page",
                UI.Row("discord.messages.header",back,UI.Text("Messages","discord.messages.title").Classes("discord-section-title","discord-copy"),
                    current.IncomingFiles?UI.Button("Cancel sharing",IncomingFilesAction(current),"discord.messages.cancel-share").Classes("discord-control"):MessageOptions()).Classes("discord-message-header"),
                UI.Row("discord.messages.search.row",UI.TextEntry(query.Search,"Search conversations and friends","message-search","discord.messages.search",120).Classes("discord-search"),
                    UI.IconButton(WidgetGlyph.Refresh,"message-refresh","discord.messages.refresh","Refresh conversations").Classes("discord-control")),
                UI.VerticalScroll("discord.messages.list",rows.ToArray()).Classes("discord-list"),
                UI.Text(current.Notice.Length>0?current.Notice:current.IncomingFiles?$"Choose a recipient for {current.Incoming?.Length??0} shared file(s).":snapshot.Value?.Context.Truncated==true?"Search to narrow the available conversations.":"","discord.messages.notice").Classes("discord-secondary"),
                current.IncomingFiles?IncomingFilesPanel(current):UI.Spacer("discord.messages.no-incoming"),
                UI.Row("discord.messages.paging",UI.Button("Previous",ConversationPageAction(query,-40),"discord.messages.previous").Disabled(query.Offset==0).Classes("discord-control"),
                    UI.Button("Next",ConversationPageAction(query,40),"discord.messages.next").Disabled(snapshot.Value?.Context.HasMore!=true).Classes("discord-control"))).Classes("discord-page");
        }
        var selected=current.Selection;
        if(selected is null)return UI.Stack("discord.messages.empty",back);
        first="discord.message.draft";
        var page=history.Snapshot;
        var lines=page.Items.Select((item,index)=>new MessageLine(item,
            index==0 || page.Items[index-1].AuthorId!=item.AuthorId || item.Timestamp-page.Items[index-1].Timestamp>300000 || MessageDate(page.Items[index-1])!=MessageDate(item),
            index==0 || MessageDate(page.Items[index-1])!=MessageDate(item),current.RevealedSpoilers?.Contains(item.Id)==true,
            current.EntryUnreadBoundaryId==item.Id)).ToArray();
        messageRows.Publish(selected.Session.Id+":"+selected.Account+":"+selected.Channel,selected.Channel,lines);
        var list=UI.CollectionList("discord.message.history",messageRows,60,"Messages")
            .DirectionalEntry(IndexedDirectionalEntryMode.VisibleEdge)
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
        var messagePreview=UI.FocusPresentationSurface(list,UI.Text("","discord.message.preview.empty"),"discord.message.preview") with
            {Preview=new(520,440,AnchorPlacement.Above){SizeToContentHeight=true,EnterButton=ControllerButton.A,AllowMedia=true}};
        var header=new List<WidgetElement>{back,UI.Text(selected.Name,"discord.message.title").Classes("discord-section-title","discord-copy")};
        if(page.Status is WidgetPagedResourceStatus.Loading or WidgetPagedResourceStatus.LoadingAdjacent)
            header.Add(UI.Text(page.Status==WidgetPagedResourceStatus.Loading?"Loading messages…":page.LoadingDirection==WidgetCursorDirection.Before?"Loading older…":"Loading newer…",
                "discord.message.boundary.status").Classes("discord-secondary"));
        else if(page.Status==WidgetPagedResourceStatus.Ready && page.Items.Count>0 && !page.HasBefore && messageRows.ViewportState.IsAtStart)
            header.Add(UI.Text("Beginning of conversation","discord.message.boundary.end").Classes("discord-secondary"));
        header.Add(MessageOptions());
        var children=new List<WidgetElement>{
            UI.Row("discord.message.header",header.ToArray()).Classes("discord-message-header"),
            messagePreview.Classes("discord-list")};
        if(page.Status==WidgetPagedResourceStatus.Ready && page.Items.Count==0)children.Add(UI.Text("No messages yet","discord.message.empty").Classes("discord-secondary"));
        if(page.Error is not null)children.Add(UI.Button("Try again","message-retry","discord.message.retry").Classes("discord-control"));
        var missingNotification=page.Status==WidgetPagedResourceStatus.Ready && current.InitialReveal is {UnreadBoundary:false,MessageId:{} requestedMessage} &&
            !page.Items.Any(item=>item.Id==requestedMessage);
        if((page.Error?.Message??(missingNotification?"The requested message is unavailable.":current.Notice)) is {Length:>0} notice)
            children.Add(UI.Text(notice,"discord.message.status").Classes("discord-secondary"));
        if(current.Files is {Length:>0})children.Add(MessageFiles(current));
        var actions=new List<WidgetElement>{UI.TextEntry(current.Draft,"Write a message…",MessageAction("message-draft",current),"discord.message.draft",2000).Classes("discord-message-composer")};
        if(current.NewMessages || page.HasAfter || messageRows.ViewportState is {ActiveViews:>0,IsAtEnd:false})
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
    private static SelectElement MessageOptions()=>UI.CommandMenu("More",
        MessageCommands().Select(command=>new SelectOption(command.ActionId,command.Label,command.ActionId)).ToArray(),
        "discord.message.options","Conversation options").Classes("discord-control");

    private bool CurrentMessageEntry(InitialMessageReveal? entry)=>entry is not null && entry.Selection==messages.Value.Selection &&
        entry.NavigationRevision==navigation.Value.Revision && !entry.RouteLifetime.IsCancellationRequested &&
        !entry.ActiveLifetime.IsCancellationRequested && IsActive;

    private static DateTime MessageDate(DiscordMessage item) => DateTimeOffset.FromUnixTimeMilliseconds(item.Timestamp).ToLocalTime().Date;
    private static WidgetElement MessageRow(MessageLine line,WidgetIndexedItemContext ids)
    {
        var item=line.Message;
        var preview=new List<WidgetElement>();
        var body=new List<WidgetElement>();
        if(line.ShowAuthor)body.Add(UI.Row(ids.Id("heading"),
            UI.Text(item.IsOwn?"You":item.AuthorName,ids.Id("author")).Classes("discord-message-author"),
            UI.Text(DateTimeOffset.FromUnixTimeMilliseconds(item.Timestamp).ToLocalTime().ToString("t",CultureInfo.CurrentCulture)+(item.EditedTimestamp is null?"":" · edited"),ids.Id("time")).Classes("discord-message-time"))
            .Classes("discord-message-heading"));
        if(item.Reply is {} reply)body.Add(UI.Text("↪ "+reply.Author+": "+(reply.Unavailable?"Original message unavailable":
            MessageExcerpt(reply.Content,line.RevealSpoilers)),ids.Id("reply")).Classes("discord-message-reply"));
        if(item.SystemText is {} system)body.Add(UI.Text(system,ids.Id("system")).Classes("discord-secondary"));
        var textParts=DiscordMessageText.Parse(item.Content,item.Mentions,line.RevealSpoilers,item.ContentTruncated);
        if(textParts.Count>0)body.Add(MessageRichText(textParts,ids));
        foreach(var link in textParts.Where(part=>part.Url is not null).DistinctBy(part=>part.Url).Take(8))
            preview.Add(UI.Button(link.Text,ids.Id("open-link-"+preview.Count),ids.Id("link-"+preview.Count))
                .OpenIntent(WidgetIntentContracts.Web,JsonSerializer.SerializeToElement(new{url=link.Url})).Classes("discord-control"));
        var attachmentCount=0;var previewMediaCount=0;
        foreach(var file in item.Attachments)
        {
            if(!line.RevealSpoilers && file.FileName.StartsWith("SPOILER_",StringComparison.OrdinalIgnoreCase))
            {
                if(attachmentCount++<3)body.Add(UI.Text("[Spoiler attachment]",ids.Id("attachment-hidden-"+file.Id)).Classes("discord-text-spoiler"));
                continue;
            }
            var image=file.ContentType is "image/png" or "image/jpeg" or "image/gif" or "image/webp";
            if(attachmentCount++<3)
                body.Add(image&&file.Url is {} imageUrl?UI.Image(imageUrl,ids.Id("attachment-image-"+file.Id),file.FileName,ImageFit.Contain).Classes("discord-message-image"):
                    UI.Text(file.FileName,ids.Id("attachment-name-"+file.Id)).Classes("discord-message-file-name"));
            if(file.Url is {} mediaUrl&&previewMediaCount<1)
            {
                if(image){preview.Add(UI.Image(mediaUrl,ids.Id("preview-image-"+file.Id),file.FileName,ImageFit.Contain).Classes("discord-message-preview-image"));previewMediaCount++;}
                else if(file.ContentType is "video/mp4" or "video/webm" or "audio/mpeg" or "audio/ogg" or "audio/mp4")
                {preview.Add(UI.MediaPlayer(MediaPlayerSource.WebUrl(mediaUrl),ids.Id("preview-media-"+file.Id),file.FileName,new(Muted:true))
                    .Classes("discord-message-preview-media"));previewMediaCount++;}
            }
            preview.Add(file.Url is {} url?UI.Button(file.FileName,ids.Id("file-action-"+file.Id),ids.Id("file-"+file.Id))
                .OpenIntent(WidgetIntentContracts.Web,JsonSerializer.SerializeToElement(new{url})).Classes("discord-control"):
                UI.Text(file.FileName+" · Link unavailable",ids.Id("file-"+file.Id)).Classes("discord-secondary"));
        }
        if(item.Attachments.Length>3)body.Add(UI.Text($"{item.Attachments.Length} attachments · Open preview to view all",ids.Id("files")).Classes("discord-secondary"));
        AddMessageExtras(line,ids,body,preview);
        if(item.ContentTruncated)body.Add(UI.Text("Message shortened · Open Discord to read all",ids.Id("truncated")).Classes("discord-secondary"));
        if(body.Count==0)body.Add(UI.Text("Message content is available in Discord",ids.Id("unsupported")).Classes("discord-secondary"));
        var avatar=line.ShowAuthor?Avatar(item.AvatarUrl,ids.Id("avatar"),item.AuthorName).AddClasses("discord-message-avatar"):
            UI.Spacer(ids.Id("avatar-space")).Classes("discord-message-avatar");
        var bubble=UI.Stack(ids.Id("bubble"),body.ToArray()).Classes("discord-message-bubble",item.IsOwn?"discord-message-own":"discord-message-other");
        // Star measurement wraps long text to available width; alignment keeps
        // short bubbles content-sized without adding another layout wrapper.
        var content=UI.Grid(ids.Id("content"),[],item.IsOwn?[GridTrack.Star(),GridTrack.Auto()]:[GridTrack.Auto(),GridTrack.Star()],
            bubble.InGrid(column:item.IsOwn?0:1).AlignInGrid(item.IsOwn?GridCellAlignment.End:GridCellAlignment.Start,GridCellAlignment.Start),
            avatar.InGrid(column:item.IsOwn?1:0).AlignInGrid(GridCellAlignment.Start,GridCellAlignment.Start))
            .Spacing(0,8).Classes("discord-message-content");
        var children=new List<WidgetElement>();
        if(line.ShowDate)children.Add(UI.Text(MessageDate(item).ToString("D",CultureInfo.CurrentCulture),ids.Id("date")).Classes("discord-message-date"));
        if(line.ShowUnread)children.Add(UI.Text("Unread messages",ids.Id("unread")).Classes("discord-message-unread"));
        children.Add(content);
        var row=UI.ReadingSurface(ids.Id("row"),item.AuthorName+", message",ActionSurfaceOrientation.Vertical,children.ToArray())
            .FocusVisualTarget(ids.Id("bubble")).Classes("discord-message-row");
        if(item.Content.Contains("||",StringComparison.Ordinal) || item.Reply?.Content.Contains("||",StringComparison.Ordinal)==true ||
            item.Embeds.Any(embed=>embed.Description.Contains("||",StringComparison.Ordinal)) ||
            item.Attachments.Any(file=>file.FileName.StartsWith("SPOILER_",StringComparison.OrdinalIgnoreCase)))row=row.ContextAction(line.RevealSpoilers?"message.spoiler.hide":"message.spoiler.show",
            line.RevealSpoilers?"Hide spoilers":"Reveal spoilers").ContextMenuShortcut(ControllerButton.Menu);
        return preview.Count==0?row:row.PresentOnFocus(UI.VerticalScroll(ids.Id("preview"),preview.ToArray()).Classes("discord-page-scroll"));
    }

    private ValueTask MessageRowAction(string channel,MessageLine line,WidgetActionEvent action,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(messages.Value.Selection is not { } selected || selected.Channel!=channel || !MessageSelectionCurrent(selected))return ValueTask.CompletedTask;
        if(action.ActionId is "message.spoiler.show" or "message.spoiler.hide")
            messages.Update(state=>state with{RevealedSpoilers=action.ActionId=="message.spoiler.show"
                ?(state.RevealedSpoilers??[]).Append(line.Message.Id).Distinct().TakeLast(40).ToArray()
                :(state.RevealedSpoilers??[]).Where(id=>id!=line.Message.Id).ToArray()});
        return ValueTask.CompletedTask;
    }
    private static WidgetElement MessageRichText(IReadOnlyList<DiscordTextPart> parts,WidgetIndexedItemContext ids)
    {
        var spans=new List<WidgetElement>();var emojiCount=0;
        foreach(var part in parts)
        {
            var id=ids.Id("span-"+spans.Count);
            if(part.EmojiId is { } emoji && emojiCount++<4)
            {spans.Add(UI.Image("https://cdn.discordapp.com/emojis/"+emoji+".png?size=32",id,part.Text,ImageFit.Contain));continue;}
            var classes=new List<string>();
            if(part.Style.HasFlag(DiscordTextStyle.Bold))classes.Add("discord-text-bold");
            if(part.Style.HasFlag(DiscordTextStyle.Italic))classes.Add("discord-text-italic");
            if(part.Style.HasFlag(DiscordTextStyle.Underline))classes.Add("discord-text-underline");
            if(part.Style.HasFlag(DiscordTextStyle.Strike))classes.Add(part.Style.HasFlag(DiscordTextStyle.Underline)?"discord-text-decorated":"discord-text-strike");
            if(part.Style.HasFlag(DiscordTextStyle.Code))classes.Add("discord-text-code");
            if(part.Style.HasFlag(DiscordTextStyle.Spoiler))classes.Add("discord-text-spoiler");
            if(part.Style.HasFlag(DiscordTextStyle.Quote))classes.Add("discord-text-quote");
            if(part.Url is not null)classes.Add("discord-text-link");
            spans.Add(UI.Text(part.Text,id).Classes(classes.ToArray()));
        }
        return UI.RichText(ids.Id("text"),spans.ToArray()).Classes("discord-message-text");
    }
}
