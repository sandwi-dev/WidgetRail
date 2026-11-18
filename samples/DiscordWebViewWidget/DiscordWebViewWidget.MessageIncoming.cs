using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private void CancelIncomingShare(MessageUi current)
    {
        lock(messageAdmissionGate)
        {
            var retired=messages.Update(value=>value.IncomingFiles && ReferenceEquals(value.Incoming,current.Incoming)
                ?(value with{Incoming=null,IncomingOfferId=null,IncomingBinding=null,Notice=""},true):(value,false)).Result;
            if(retired){Operations.Cancel("discord.message.incoming");ReleaseMessageFiles(current.Incoming);}
        }
    }

    private static bool OwnsUnboundShare(MessageUi current,MessageUi pending)=>current.IncomingFiles && current.Query is null &&
        current.IncomingOfferId==pending.IncomingOfferId && ReferenceEquals(current.Incoming,pending.Incoming);

    private void CompleteColdIncomingShare(MessageUi pending)
    {
        var visit=navigation.Value;
        var accountBinding=pending.IncomingBinding??throw new InvalidOperationException("Missing incoming account binding.");
        var admission=Operations.RunLatest("discord.message.incoming",async operation=>
        {
            try
            {
                // The intent callback has already accepted recipient custody.
                // Browser/account initialization belongs to the normal resource,
                // outside the short host intent RPC. The root lifetime survives
                // a nested browser/sign-in detour; explicit Cancel/Back from
                // Messages and active retirement still cancel this wait.
                var binding=await WaitForIncomingAccountAsync(accountBinding,visit.RootRouteCancellationToken,operation.CancellationToken);
                visit.RootRouteCancellationToken.ThrowIfCancellationRequested();operation.CancellationToken.ThrowIfCancellationRequested();
                lock(messageAdmissionGate)
                {
                    visit.RootRouteCancellationToken.ThrowIfCancellationRequested();operation.CancellationToken.ThrowIfCancellationRequested();
                    if(!OwnsUnboundShare(messages.Value,pending))return;
                    if(CurrentState() is not {Ready:true,Messaging.CanSend:true} data || client?.Reference is not {} session ||
                        binding.Session is not null && session!=binding.Session || binding.Account is not null && data.Account!=binding.Account)
                    {RetireUnboundShare(pending,"Discord is not available for sharing. Open Discord, then try sharing these captures again.");return;}
                    var query=new ConversationQuery(data.Account,session);
                    var bound=messages.Update(value=>OwnsUnboundShare(value,pending)
                        ?(value with{Query=query,IncomingBinding=null,Notice=""},true):(value,false)).Result;
                    if(bound){conversations.EnsureLoaded(query);ObserveMessages();}
                }
            }
            catch(OperationCanceledException) when(operation.CancellationToken.IsCancellationRequested || visit.RootRouteCancellationToken.IsCancellationRequested)
            {RetireUnboundShare(pending,"");}
            catch(Exception)
            {RetireUnboundShare(pending,"Discord could not load. Your shared files were released; try sharing again.");}
        },WidgetOperationLifetime.Active);
        if(!admission.IsAccepted)RetireUnboundShare(pending,"");
    }

    private async Task<(WebSessionReference? Session,string? Account)> WaitForIncomingAccountAsync(IncomingAccountBinding binding,CancellationToken route,CancellationToken active)
    {
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe()
        {
            if(client?.Reference is {} currentSession)Interlocked.CompareExchange(ref binding.Session,currentSession,null);
            var snapshot=state.Snapshot;
            if(snapshot is {HasValue:true,Value.Ready:true})Interlocked.CompareExchange(ref binding.Account,snapshot.Value.Account,null);
            if(snapshot.Status==WidgetResourceStatus.Error || snapshot.HasValue && snapshot.Value?.Ready==true ||
                hasObservedDiscordAccount && snapshot.HasValue && snapshot.Value?.Ready==false ||
                binding.Session is not null && client?.Reference!=binding.Session)ready.TrySetResult();
        }
        // Resource publication already invalidates this widget. Observe that
        // supported signal, including a completion before subscription, instead
        // of starting another timer or treating an initial disconnected frame
        // as a confirmed sign-out while Discord hydrates its stores.
        void Changed(object? sender,WidgetInvalidatedEventArgs args)=>Observe();
        Invalidated+=Changed;
        try
        {
            state.EnsureLoaded();Observe();
            using var ownership=CancellationTokenSource.CreateLinkedTokenSource(route,active);
            await ready.Task.WaitAsync(ownership.Token);
            return (binding.Session,binding.Account);
        }
        finally{Invalidated-=Changed;}
    }

    private void RetireUnboundShare(MessageUi pending,string notice)
    {
        lock(messageAdmissionGate)
        {
            var retired=messages.Update(value=>OwnsUnboundShare(value,pending)
                ?(value with{Incoming=null,IncomingOfferId=null,IncomingBinding=null,Notice=notice},true):(value,false)).Result;
            if(retired)ReleaseMessageFiles(pending.Incoming);
        }
    }

    private WidgetElement UnboundMessageSharePage(out string first)
    {
        var current=messages.Value;var pending=current.IncomingFiles;
        first=pending?"discord.messages.cancel-share":"discord.messages.back";
        return UI.Stack("discord.messages.pending",
            UI.Row("discord.messages.header",UI.Text("Messages","discord.messages.title").Classes("discord-section-title","discord-copy"),
                pending?UI.Button("Cancel sharing",IncomingFilesAction(current),"discord.messages.cancel-share").Classes("discord-control"):UI.Spacer("discord.messages.no-share"),
                UI.Button("Back",navigation.Value.BackActionId!,"discord.messages.back").Classes("discord-control")),
            UI.Text(current.Notice.Length>0?current.Notice:"Connecting to Discord before choosing a recipient…","discord.messages.notice").Classes("discord-secondary"),
            IncomingFilesPanel(current))
            .Classes("discord-page");
    }
}
