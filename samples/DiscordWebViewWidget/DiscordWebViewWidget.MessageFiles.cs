using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private static readonly CompiledWidgetIntentContract filesIntent=CompiledWidgetIntentContract.Create(WidgetIntentContracts.Files);
    private bool TryMessageFileAction(WidgetActionEvent action,MessageUi current,CancellationToken token,out ValueTask result)
    {
        result=ValueTask.CompletedTask;
        if(current.PendingNonce is not null || Operations.IsBusy("discord.message.send"))return false;
        if(action.ActionId==MessageAction("message-clear-files",current) && current.Files is {Length:>0})
        {messages.Update(value=>value with{Files=null,Notice=""});ReleaseMessageFiles(current.Files);return true;}
        if(action.ActionId==MessageAction("message-attach",current))
        {
            var selected=current.Selection!;
            Operations.RunSingleFlight("discord.message.files",async operation=>
            {
                IReadOnlyList<WidgetFileReference>? chosen=null;
                try
                {
                    chosen=await HostServices.Files.ChooseAsync(new(){Title="Attach files to Discord",Multiple=true},operation.CancellationToken);
                    if(!MessageSelectionCurrent(selected) || messages.Value.PendingNonce is not null)return;
                    var combined=CombineMessageFiles(messages.Value.Files??[],chosen);
                    if(combined.Length>10){messages.Update(value=>value with{Notice="Discord accepts up to 10 attachments per message."});return;}
                    if(CurrentState()?.Messaging?.MaximumAttachmentBytes is {} limit && chosen.Any(file=>file.ByteLength>limit))
                    {messages.Update(value=>value with{Notice="A selected file exceeds this account's upload limit."});return;}
                    messages.Update(value=>value with{Files=combined,Notice=""});
                    var duplicate=chosen.Where(file=>!combined.Any(kept=>kept.Id==file.Id)).ToArray();chosen=null;
                    ReleaseMessageFiles(duplicate);
                }
                catch(Exception) when(!operation.CancellationToken.IsCancellationRequested)
                {if(MessageSelectionCurrent(selected))messages.Update(value=>value with{Notice="Could not attach these files. Try selecting them again."});}
                finally{if(chosen is not null)ReleaseMessageFiles(chosen);}
            });
            return true;
        }
        foreach(var file in current.Files??[])
        if(action.ActionId==DomainAction("message-remove-file",current.Selection?.Account,current.Selection?.Session.Id,current.Selection?.Channel,file.Id))
        {
            messages.Update(value=>value with{Files=value.Files?.Where(item=>item.Id!=file.Id).ToArray()});ReleaseMessageFiles([file]);return true;
        }
        return false;
    }

    private WidgetElement MessageFiles(MessageUi current)=>UI.Row("discord.message.files",
        UI.HorizontalScroll("discord.message.file-items",
        (current.Files??[]).Select((file,index)=>(WidgetElement)UI.Button(file.Name+" · Remove",
            DomainAction("message-remove-file",current.Selection?.Account,current.Selection?.Session.Id,current.Selection?.Channel,file.Id),"discord.message.file."+index)
            .Disabled(current.PendingNonce is not null).Classes("discord-control")).ToArray()).Classes("discord-message-file-items"),
        UI.Button("Clear attachments",MessageAction("message-clear-files",current),"discord.message.clear-files")
            .Disabled(current.PendingNonce is not null || Operations.IsBusy("discord.message.send")).Classes("discord-control"))
        .Classes("discord-message-files");

    private static string IncomingFileAction(MessageUi current,WidgetFileReference file)=>DomainAction("incoming-remove-file",
        current.Query?.Account,current.Query?.Session.Id,current.IncomingOfferId,file.Id,string.Join(",",(current.Incoming??[]).Select(value=>value.Id)));

    private bool TryIncomingFileAction(WidgetActionEvent action,MessageUi current)
    {
        lock(messageAdmissionGate)
        {
            foreach(var file in current.Incoming??[])
            if(action.ActionId==IncomingFileAction(current,file))
            {
                var remaining=current.Incoming!.Where(value=>value.Id!=file.Id).ToArray();
                var update=messages.Update(value=>ReferenceEquals(value.Incoming,current.Incoming) && value.IncomingOfferId==current.IncomingOfferId
                    ?(value with{Incoming=remaining.Length==0?null:remaining,IncomingOfferId=remaining.Length==0?null:value.IncomingOfferId,IncomingBinding=remaining.Length==0?null:value.IncomingBinding,
                        Notice=remaining.Length>10?$"Discord accepts up to 10 attachments. Remove {remaining.Length-10} before choosing a recipient.":""},true)
                    :(value,false));
                if(update.Result)
                {
                    ReleaseMessageFiles([file]);
                    if(update.Current.Query is null)
                    {
                        if(update.Current.IncomingFiles)CompleteColdIncomingShare(update.Current);
                        else Operations.Cancel("discord.message.incoming");
                    }
                }
                return true;
            }
            return false;
        }
    }

    private WidgetElement IncomingFilesPanel(MessageUi current)=>UI.HorizontalScroll("discord.incoming.files",
        (current.Incoming??[]).Select((file,index)=>(WidgetElement)UI.Button(file.Name+" · Remove",IncomingFileAction(current,file),
            "discord.incoming.file."+index).Classes("discord-control")).ToArray()).Classes("discord-message-files");

    private async Task<DiscordUploadBatch> UploadMessageFilesAsync(MessageSelection selected,WidgetFileReference[] files,Action<int> progress,CancellationToken token)
    {
        DemandMessageAccount(selected.Account,selected.Session);
        var json=await client!.InvokeAsync("messagePrepareAttachments",new{account=selected.Account,channelId=selected.Channel,
            files=files.Select((file,index)=>new{id=index.ToString(System.Globalization.CultureInfo.InvariantCulture),fileName=file.Name,byteLength=file.ByteLength}).ToArray()},token);
        var batch=json.Deserialize<DiscordUploadBatch>(MessageJson)??throw DiscordMessageValues.Invalid();
        batch.Validate(selected.Account,selected.Channel,files,DateTimeOffset.UtcNow);
        var total=files.Sum(file=>file.ByteLength);long completed=0;var previous=-1;
        for(var index=0;index<files.Length;index++)
        {
            if(!MessageSelectionCurrent(selected))throw new OperationCanceledException();
            await using var stream=await HostServices.Files.OpenReadAsync(files[index],token);
            var file=files[index];
            await uploadAttachment(batch.Files[index],stream,bytes=>
            {
                token.ThrowIfCancellationRequested();
                var percent=total==0?100:(int)((completed+Math.Clamp(bytes,0,file.ByteLength))*100/total);
                if(percent!=previous){previous=percent;progress(percent);}
            },token);
            token.ThrowIfCancellationRequested();completed+=file.ByteLength;
        }
        return batch;
    }

    private void ReleaseMessageFiles(IEnumerable<WidgetFileReference>? files)
    {
        if(files is null)return;
        foreach(var file in files)
        if(!pendingMessages.HoldsFile(file.Id) && !messageDrafts.HoldsFile(file.Id) &&
            !((messages.Value.Files??[]).Concat(messages.Value.Incoming??[])).Any(held=>held.Id==file.Id))
            Operations.RunSingleFlight("discord.message.release."+file.Id,async context=>
            {try{await HostServices.Files.ReleaseAsync(file,context.CancellationToken);}catch(Exception) when(!context.CancellationToken.IsCancellationRequested){ }},WidgetOperationLifetime.Widget);
    }

    private static bool IsFilesIntent(WidgetIntentRequest request)=>request.IsWellFormed() && request.ContractId==filesIntent.Id &&
        request.Version==filesIntent.Version && request.SchemaDigest==filesIntent.SchemaDigest && filesIntent.Accepts(request.Payload);
    public override ValueTask<bool> IsIntentAvailableAsync(WidgetIntentRequest request,CancellationToken cancellationToken=default)
    {
        if(IsMessageNotification(request))return ValueTask.FromResult(CanOpenMessageNotification(request));
        var available=IsFilesIntent(request) && (CurrentState() is {Ready:true,Messaging.CanSend:true} ||
            !hasObservedDiscordAccount && state.Snapshot.Status!=WidgetResourceStatus.Error &&
                (state.Snapshot.Status is WidgetResourceStatus.NotLoaded or WidgetResourceStatus.Loading || state.Snapshot.Value is {Ready:false}));
        return ValueTask.FromResult(available);
    }
    public override async ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request,CancellationToken cancellationToken=default)
    {
        if(IsMessageNotification(request))return await OpenMessageNotificationAsync(request,cancellationToken);
        if(!IsActive || !await IsIntentAvailableAsync(request,cancellationToken))return WidgetIntentResult.Rejected;
        var beforeVisit=navigation.Value;
        var beforeData=CurrentState();var beforeSession=client?.Reference;
        IReadOnlyList<WidgetFileReference>? files=null;
        try
        {
            files=await HostServices.Files.AcceptShareAsync(request.Payload.GetProperty("offerId").GetString()!,cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock(messageAdmissionGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(!IsActive || navigation.Value!=beforeVisit || beforeData is {Ready:true} &&
                    (beforeSession is null || !MessageAccountCurrent(beforeData.Account,beforeSession)))return WidgetIntentResult.Rejected;
                var data=CurrentState();var session=client?.Reference;
                if(data is not null && (data.Ready!=true || data.Messaging?.CanSend!=true) ||
                    hasObservedDiscordAccount && data is null || beforeSession is not null && session!=beforeSession)
                    return WidgetIntentResult.Rejected;
                var priorBinding=messages.Value.IncomingBinding;
                if(priorBinding?.Session is {} incomingSession && session!=incomingSession ||
                    priorBinding?.Account is {} incomingAccount && data?.Account!=incomingAccount)return WidgetIntentResult.Rejected;
                var query=data is {Ready:true,Messaging.CanSend:true} && session is not null?new ConversationQuery(data.Account,session):null;
                var received=files.ToArray();
                var adopted=messages.Update(value=>
                {
                    var combined=CombineMessageFiles(value.Incoming??[],received);
                    var duplicate=received.Where(file=>!combined.Any(kept=>kept.Id==file.Id)).ToArray();
                    return(value with{Query=query,Incoming=combined,IncomingOfferId=request.Payload.GetProperty("offerId").GetString(),
                        IncomingBinding=query is null?value.IncomingBinding??new IncomingAccountBinding(beforeSession):null,
                        Notice=combined.Length>10?$"Discord accepts up to 10 attachments. Remove {combined.Length-10} before choosing a recipient.":""},duplicate);
                });
                var pending=adopted.Current;
                files=null;ReleaseMessageFiles(adopted.Result);
                if(navigation.Value.Route!=Route.Messages)
                {
                    // A new share starts recipient selection. Reset through the
                    // navigator rather than pushing an already-present scope over
                    // an existing conversation visit.
                    navigation.NavigateRoot(Route.Home);
                    navigation.Push(Route.Messages,"discord.messages.open");
                }
                if(query is not null){conversations.EnsureLoaded(query);ObserveMessages();}
                else CompleteColdIncomingShare(pending);
                return WidgetIntentResult.Accepted;
            }
        }
        catch(Exception) when(!cancellationToken.IsCancellationRequested){return WidgetIntentResult.Rejected;}
        finally{if(files is not null)ReleaseMessageFiles(files);}
    }

    private static string MessageFileIdentity(WidgetFileReference file)=>file.ContentIdentity is {} identity?"content:"+identity:"grant:"+file.Id;
    private static WidgetFileReference[] CombineMessageFiles(IEnumerable<WidgetFileReference> existing,IEnumerable<WidgetFileReference> incoming)=>
        existing.Concat(incoming).DistinctBy(MessageFileIdentity,StringComparer.Ordinal).ToArray();
}
