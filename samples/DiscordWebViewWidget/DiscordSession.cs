using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

internal interface IDiscordSession : IAsyncDisposable
{
    event Action<WebSessionReference>? Invalidated { add { } remove { } }
    WebSessionReference? Reference { get; }
    ValueTask<DiscordState> ReadAsync(CancellationToken token);
    ValueTask<JsonElement> InvokeAsync(string method,object? args,CancellationToken token);
    ValueTask<IDiscordReceiver> OpenReceiverAsync(CancellationToken token);
    ValueTask<WebSessionMicrophoneResult> RequestMicrophoneAsync(CancellationToken token);
}

internal interface IDiscordReceiver : IAsyncDisposable
{
    Task<DiscordReceiverFailure> Failure {get;}
    WebSessionReference Reference { get; }
    ValueTask<JsonElement> AcceptAsync(JsonElement offer,CancellationToken token);
    ValueTask<JsonElement> ReadDiagnosticsAsync(string lease,CancellationToken token)=>throw new NotSupportedException("Receiver measurements are unavailable.");
}

internal sealed class DiscordWebSessionClient(WidgetWebSessionService sessions) : IDiscordSession
{
    public event Action<WebSessionReference>? Invalidated;
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim connectionGate=new(1,1);
    private readonly SemaphoreSlim readGate=new(1,1);
    private readonly CancellationTokenSource lifetime=new();
    private readonly string owner=Guid.NewGuid().ToString("N");
    private WidgetWebSession? session;
    private Task? drain;
    private string? channelVersion;
    private string? channelAccount;
    private DiscordChannel[] channels=[];
    private bool disposed;
    public WebSessionReference? Reference => session?.Reference;

    public async ValueTask<DiscordState> ReadAsync(CancellationToken token)
    {
        await readGate.WaitAsync(token);
        try
        {
        var current=await EnsureAsync(token);
        var response=await current.RequestAsync<object,JsonElement>("snapshot",new{owner},token);
        var context=response.GetProperty("context");
        var version=context.GetProperty("channelVersion").GetString();
        var count=context.GetProperty("channelCount").GetInt32();
        if(count is <0 or >1000)throw new InvalidDataException("Invalid channel count.");
        if(!Guid.TryParseExact(version,"N",out _))throw new InvalidDataException("Invalid channel catalog version.");
        var state=context.Deserialize<DiscordState>(Json)??throw new InvalidDataException("Missing Discord snapshot.");
        var participants=new List<DiscordParticipant>();var streams=new List<DiscordStream>();var activities=new List<DiscordFriendActivity>();
        foreach(var item in response.GetProperty("items").EnumerateArray())
        {
            switch(item.GetProperty("kind").GetString())
            {
                case "participant" when participants.Count<100:participants.Add(item.GetProperty("value").Deserialize<DiscordParticipant>(Json)??throw new InvalidDataException("Invalid participant."));break;
                case "stream" when streams.Count<50:streams.Add(item.GetProperty("value").Deserialize<DiscordStream>(Json)??throw new InvalidDataException("Invalid stream."));break;
                case "activity" when activities.Count<50:activities.Add(item.GetProperty("value").Deserialize<DiscordFriendActivity>(Json)??throw new InvalidDataException("Invalid activity."));break;
                default:throw new InvalidDataException("Invalid status item.");
            }
        }
        state=state with{Channels=[],Participants=participants.ToArray(),Streams=streams.ToArray(),Activities=activities.ToArray()};state.Validate();
        if(participants.Select(participant=>participant.Id).Distinct(StringComparer.Ordinal).Count()!=participants.Count)
            throw new InvalidDataException("Duplicate participant identity.");
        var nextChannels=channels;
        if(version!=channelVersion || channelAccount!=state.Account)
        {
            if(count==0)nextChannels=[];
            else
            {
                var catalog=await current.RequestAsync<object,JsonElement>("channels",new{owner,account=state.Account,channelVersion=version},token);
                var catalogContext=catalog.GetProperty("context");
                if(catalogContext.GetProperty("channelVersion").GetString()!=version || catalogContext.GetProperty("account").GetString()!=state.Account)
                    throw new InvalidDataException("Discord account or channel catalog changed during loading.");
                nextChannels=catalog.GetProperty("items").Deserialize<DiscordChannel[]>(Json)??throw new InvalidDataException("Missing channels.");
            }
        }
        if(nextChannels.Length!=count)throw new InvalidDataException("Discord channel count changed without its contents.");
        var complete=state with{Channels=nextChannels};complete.Validate();token.ThrowIfCancellationRequested();
        channels=nextChannels;channelVersion=version;channelAccount=state.Account;
        return complete;
        }
        finally{readGate.Release();}
    }

    public ValueTask<WebSessionMicrophoneResult> RequestMicrophoneAsync(CancellationToken token)=>(session??throw new InvalidOperationException("Discord is not connected.")).RequestMicrophoneAsync(token);

    public async ValueTask<JsonElement> InvokeAsync(string method,object? args,CancellationToken token)
    {
        // Commands never reopen/replay on transport failure. A later read can recover the connection.
        var current=session??throw new InvalidOperationException("Discord is not connected.");
        var response=await current.RequestAsync<object,JsonElement>("command",new{owner,method,args},token);
        if(!response.TryGetProperty("ok",out var ok)||ok.ValueKind!=JsonValueKind.True)
            throw new InvalidOperationException("Discord could not confirm this command.");
        return response.GetProperty("value").Clone();
    }

    private async Task<WidgetWebSession> EnsureAsync(CancellationToken token)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
        await connectionGate.WaitAsync(linked.Token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(session is { } previous && previous.Completion.IsCompleted)
            {session=null;await previous.DisposeAsync();channelVersion=null;channelAccount=null;channels=[];}
            if(session is not null)return session;
            var opened=await sessions.OpenAsync(new("discord.backend","Discord",WebSessionSource.Remote("https://discord.com/app"))
            {
                NativeCapture=true,
                Browser=new(){Storage=BrowserStorage.Persistent,ProfileKey="discord",Background=BrowserBackgroundMode.KeepAlive,
                    Chrome=BrowserChrome.OnDemand,Popups=BrowserPopupMode.SystemBrowser,ExternalLinks=BrowserExternalLinkMode.OtherOriginsInWindows},
                PageAdapter=new(){ScriptAsset="payload/web/backend.js",Origins=["https://discord.com"],Mode=WebSessionPageAdapterMode.SessionBackend}
            },linked.Token);
            if(linked.IsCancellationRequested){await opened.DisposeAsync();linked.Token.ThrowIfCancellationRequested();}
            session=opened;drain=DrainAsync(opened,lifetime.Token);return opened;
        }
        finally{connectionGate.Release();}
    }

    public async ValueTask<IDiscordReceiver> OpenReceiverAsync(CancellationToken token)
    {
        var receiver=await sessions.OpenAsync(new("discord.receiver","Discord shared screen",
            WebSessionSource.Package("payload/web/receiver.html",
                [new(){Path="payload/web/receiver.html",ContentType="text/html"},
                 new(){Path="payload/web/receiver.js",ContentType="text/javascript"},
                 new(){Path="payload/web/web-session.js",ContentType="text/javascript"}]) with{AllowAutoplay=true}),token);
        return new DiscordReceiver(receiver);
    }

    private async Task DrainAsync(WidgetWebSession current,CancellationToken token)
    {
        try { await foreach(var message in current.ReadMessagesAsync(token))
        {
            if(!disposed && ReferenceEquals(session,current) && message.ValueKind==JsonValueKind.Object &&
                message.TryGetProperty("kind",out var kind) && kind.ValueKind==JsonValueKind.String && kind.GetString()=="backend-invalidated")
                Invalidated?.Invoke(current.Reference);
        } }
        catch(Exception) { /* Requests and Completion expose the connection failure to its owner. */ }
    }
    public async ValueTask DisposeAsync()
    {
        if(disposed)return;disposed=true;lifetime.Cancel();
        await connectionGate.WaitAsync();
        try{if(session is { } current){session=null;await current.DisposeAsync();}if(drain is not null)await drain;}
        finally{connectionGate.Release();lifetime.Dispose();}
    }
    private sealed class DiscordReceiver : IDiscordReceiver
    {
        private readonly WidgetWebSession session;
        private readonly CancellationTokenSource lifetime=new();
        private readonly DiscordReceiverHealth health=new();
        private readonly Task drain,completion;
        private int disposed;
        internal DiscordReceiver(WidgetWebSession session)
        {
            this.session=session;
            drain=DrainHealthAsync();completion=ObserveCompletionAsync();
        }
        public WebSessionReference Reference=>session.Reference;
        public Task<DiscordReceiverFailure> Failure=>health.Failure;
        public ValueTask<JsonElement> ReadDiagnosticsAsync(string lease,CancellationToken token)=>
            session.RequestAsync<object,JsonElement>("diagnostics",new{lease},token);
        public async ValueTask<JsonElement> AcceptAsync(JsonElement offer,CancellationToken token)
        {
            var lease=offer.GetProperty("lease").GetString()??throw new InvalidDataException("Missing receiver lease.");
            health.Bind(lease);
            var answer=await session.RequestAsync<object,JsonElement>("offer",new{lease,offer=offer.GetProperty("offer"),healthEvents=true},token);
            if(health.Failure.IsCompleted)throw new IOException("Receiver ended during negotiation.");
            return answer;
        }
        private async Task DrainHealthAsync()
        {
            try{await foreach(var message in session.ReadMessagesAsync(lifetime.Token))health.Read(message);}
            catch(OperationCanceledException) when(lifetime.IsCancellationRequested){ }
            catch(Exception error) when(error is not OutOfMemoryException){health.SessionEnded();}
        }
        private async Task ObserveCompletionAsync()
        {
            try{await session.Completion.WaitAsync(lifetime.Token);if(!lifetime.IsCancellationRequested)health.SessionEnded();}
            catch(OperationCanceledException) when(lifetime.IsCancellationRequested){ }
            catch(Exception error) when(error is not OutOfMemoryException){health.SessionEnded();}
        }
        public async ValueTask DisposeAsync()
        {
            if(Interlocked.Exchange(ref disposed,1)!=0)return;
            health.Retire();lifetime.Cancel();
            try{await session.DisposeAsync();}
            finally{await Task.WhenAll(drain,completion);lifetime.Dispose();}
        }
    }
}
