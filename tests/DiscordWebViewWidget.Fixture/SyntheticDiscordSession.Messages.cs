using System.Text.Json;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.Samples.DiscordWebView.Domain;

internal sealed partial class SyntheticDiscordSession
{
    private readonly object messageGate = new();
    private string? messageAuditPath;
    private int messageConversationReads, messageMutationAttempts, messageUploadPreparations, messageUploads, messageUploadCancellations;
    private readonly List<object> messageHistoryReads = [];
    private string? historyGate;
    private bool allowsSyntheticSend;
    private DiscordMessage? sentFixtureMessage;
    private readonly List<object> messageReadAcks=[];
    private int readTargetsAheadOfHead;
    private readonly List<object> conversationRanges=[];
    private readonly List<object> conversationLookups=[];
    private bool largeConversations;
    private bool richConversations;
    private bool publicPortrait;
    private bool mediaMessages;
    private bool heldScrollMessages;
    private int historyDelayMilliseconds = 100;
    private readonly Dictionary<string,int> messageReadHeads=new(StringComparer.Ordinal);
    private readonly Dictionary<string,HashSet<string>> knownMessages=new(StringComparer.Ordinal);

    private void InitializeMessagesFixture(string? auditOverride=null)
    {
        var marker = Path.Combine(AppContext.BaseDirectory, "fixture-messages.enabled");
        if (auditOverride is null && !File.Exists(marker)) return;
        var audit = auditOverride ?? File.ReadAllText(marker).Trim();
        if (!Path.IsPathFullyQualified(audit) || Path.GetFileName(audit) != "messages-provider.json") throw new InvalidDataException("Invalid synthetic message audit path.");
        messageAuditPath = audit;
        allowsSyntheticSend=File.Exists(Path.Combine(Path.GetDirectoryName(audit)!,"generated-send"));
        var gate=Path.Combine(Path.GetDirectoryName(audit)!,"hold-history");
        if(File.Exists(gate))historyGate=gate;
        largeConversations = File.Exists(Path.Combine(AppContext.BaseDirectory, "fixture-large-conversations.enabled"));
        richConversations = File.Exists(Path.Combine(AppContext.BaseDirectory, "fixture-rich-conversations.enabled"));
        publicPortrait = File.Exists(Path.Combine(AppContext.BaseDirectory, "fixture-public-portrait.enabled"));
        mediaMessages = File.Exists(Path.Combine(AppContext.BaseDirectory, "fixture-media-messages.enabled"));
        heldScrollMessages = File.Exists(Path.Combine(AppContext.BaseDirectory, "fixture-held-scroll.enabled"));
        var delayPath = Path.Combine(AppContext.BaseDirectory, "fixture-history-delay.txt");
        if (File.Exists(delayPath) && (!int.TryParse(File.ReadAllText(delayPath), out historyDelayMilliseconds) ||
            historyDelayMilliseconds is < 0 or > 2000)) throw new InvalidDataException("Invalid synthetic history delay.");
        // Advertise a focusable share button for navigation checks only. The
        // fixture never invokes it and owns no capture/session source.
        state = state with { Capabilities=state.Capabilities with{Share=true}, Messaging = new(state.Account, "ready", true, true, true, 2000, 50, MaximumAttachmentBytes: 1024 * 1024,
            CanMarkRead:true,HasUnreadConversations:true) };
        WriteMessageAudit();
    }

    private async ValueTask<JsonElement> InvokeMessagesFixtureAsync(string method, JsonElement request, CancellationToken token)
    {
        if (request.GetProperty("account").GetString() != state.Account) throw new InvalidDataException("Synthetic message account changed.");
        if(method=="messageObserve")
        {
            var channel=request.GetProperty("active").GetBoolean()&&request.TryGetProperty("channelId",out var selected)?selected.GetString():null;
            if(channel is not null && !SyntheticConversations().Any(item=>item.ChannelId==channel))throw new InvalidDataException("Unknown synthetic watch.");
            lock(messageGate)
            {
                if(state.Messaging!.ObservedChannelId!=channel)state=state with{Messaging=state.Messaging with
                    {ObservedChannelId=channel,WatchEpoch=state.Messaging.WatchEpoch+1}};
                return JsonSerializer.SerializeToElement(state.Messaging,DiscordWebSessionClient.Json);
            }
        }
        if(method=="messageMarkRead")
        {
            var channel=request.GetProperty("channelId").GetString()!;
            var message=request.GetProperty("messageId").GetString()!;
            lock(messageGate)
            {
                if(channel!=state.Messaging!.ObservedChannelId||request.GetProperty("watchEpoch").GetInt64()!=state.Messaging.WatchEpoch||
                    !knownMessages.TryGetValue(channel,out var known)||!known.Contains(message)||!int.TryParse(message,out var head))
                    throw new InvalidDataException("Read request escaped its presented synthetic conversation.");
                var status=messageReadHeads.GetValueOrDefault(channel)>=head?"already-read":"applied";
                if(allowsSyntheticSend && head>1120 && status!="already-read")
                {
                    // Sending has succeeded, but the resident read store still
                    // reports its pre-send head. Match the provider's write guard.
                    ++readTargetsAheadOfHead;
                    WriteMessageAudit();
                    throw new DiscordCommandException("invalid_read_target");
                }
                if(status=="applied")
                {
                    messageReadHeads[channel]=head;messageReadAcks.Add(new{channel,message});
                    state=state with{Messaging=state.Messaging with{CatalogRevision=state.Messaging.CatalogRevision+1,
                        HasUnreadConversations=messageReadHeads.GetValueOrDefault("10")<1120}};
                    WriteMessageAudit();
                }
                return JsonSerializer.SerializeToElement(new{account=state.Account,channelId=channel,messageId=message,status});
            }
        }
        if(method=="messageRecover")
        {
            var channel=request.GetProperty("channelId").GetString()!;var epoch=request.GetProperty("watchEpoch").GetInt64();
            var ids=request.GetProperty("ids").EnumerateArray().Select(value=>value.GetString()!).ToArray();
            if(channel!=state.Messaging!.ObservedChannelId||epoch!=state.Messaging.WatchEpoch||ids.Length is <1 or >40||
                ids.Any(id=>!int.TryParse(id,out var value)||value is <1001 or >1120))throw new InvalidDataException("Unknown synthetic recovery.");
            return JsonSerializer.SerializeToElement(new DiscordMessageRecovery(new(state.Account!,channel,epoch,0,ids[^1],true),
                ids.Select(id=>new DiscordMessageChange(0,"upsert",id,SyntheticMessage(int.Parse(id),channel))).ToArray()),DiscordWebSessionClient.Json);
        }
        if(method=="messageChanges")
        {
            var channel=request.GetProperty("channelId").GetString()!;var epoch=request.GetProperty("watchEpoch").GetInt64();
            var after=request.GetProperty("afterRevision").GetInt64();
            if(channel!=state.Messaging!.ObservedChannelId||epoch!=state.Messaging.WatchEpoch||after!=0)
                throw new InvalidDataException("Unknown synthetic revision read.");
            return JsonSerializer.SerializeToElement(new DiscordMessageChanges(new(state.Account!,channel,epoch,after,0,false,false),[]),DiscordWebSessionClient.Json);
        }
        if (method is "messageConversations" or "messageLocateConversation")
        {
            var search = request.GetProperty("query").GetString() ?? "";
            var matches = SyntheticConversations().Where(item => item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
            var currentVersion="fixture.messages.v"+state.Messaging!.CatalogRevision;
            if(request.TryGetProperty("version",out var version) && version.ValueKind!=JsonValueKind.Null && version.GetString()!=currentVersion)
                throw new DiscordCommandException("conversation_catalog_changed");
            if(method=="messageLocateConversation")
            {
                var key=request.GetProperty("key").GetString();
                var index=Array.FindIndex(matches,item=>item.Id==key);
                lock(messageGate){conversationLookups.Add(new{key,query=search,index});WriteMessageAudit();}
                return JsonSerializer.SerializeToElement(new{account=state.Account,version=currentVersion,totalCount=matches.Length,key,index=index<0?(int?)null:index});
            }
            var offset = request.GetProperty("offset").GetInt32(); var limit = request.GetProperty("limit").GetInt32();
            if(offset<0 || limit is <1 or >50)throw new InvalidDataException("Unbounded synthetic conversation request.");
            lock (messageGate) { ++messageConversationReads; conversationRanges.Add(new{offset,limit,query=search});WriteMessageAudit(); }
            return JsonSerializer.SerializeToElement(new DiscordConversationPage(new(state.Account, currentVersion, matches.Length, offset, offset + limit < matches.Length)
                {OrderVersion="fixture.order."+search},
                matches.Skip(offset).Take(limit).ToArray()), DiscordWebSessionClient.Json);
        }
        if (method == "messageHistory")
        {
            var channel = request.GetProperty("channelId").GetString()!;
            if (!SyntheticConversations().Any(item=>item.ChannelId==channel)) throw new InvalidDataException("Unknown synthetic conversation.");
            var before=request.TryGetProperty("before",out var beforeValue)?beforeValue.GetString():null;
            var after=request.TryGetProperty("after",out var afterValue)?afterValue.GetString():null;
            var around=request.TryGetProperty("around",out var aroundValue)?aroundValue.GetString():null;
            var limit = request.GetProperty("limit").GetInt32();
            if (limit is < 1 or > 50 || new[]{before,after,around}.Count(value=>value is not null)>1) throw new InvalidDataException("Unbounded synthetic history request.");
            var sent=Volatile.Read(ref sentFixtureMessage);
            var ids = Enumerable.Range(1001, sent?.ChannelId==channel?121:120).Where(id => before is null || id < int.Parse(before)).Where(id => after is null || id > int.Parse(after));
            var pageIds = (around is not null?ids.OrderBy(id=>Math.Abs(id-int.Parse(around))).Take(limit).Order():after is null ? ids.TakeLast(limit) : ids.Take(limit)).ToArray();
            lock (messageGate)
            {
                if(!knownMessages.TryGetValue(channel,out var known))knownMessages[channel]=known=[];
                foreach(var id in pageIds)known.Add(id.ToString());
                messageHistoryReads.Add(new { channel, before, after, around, limit, count = pageIds.Length }); WriteMessageAudit();
            }
            var gate=Interlocked.Exchange(ref historyGate,null);
            if(gate is not null)
                while(File.Exists(gate))await Task.Delay(20,token);
            await Task.Delay(historyDelayMilliseconds, token); // Exercise delayed publication without a remote service.
            var messages=pageIds.Select(id=>id==1121 && sent is not null?sent:SyntheticMessage(id,channel)).ToArray();
            if(gate is not null)File.WriteAllText(gate+".completed",channel);
            return JsonSerializer.SerializeToElement(new DiscordMessagePage(new(state.Account, channel, before,
                pageIds.Length == 0 ? null : pageIds[0].ToString(), pageIds.Length > 0 && (after is null ? pageIds[0] > 1001 : pageIds[^1] < 1120),
                after, pageIds.Length == 0 ? null : pageIds[^1].ToString()){Around=around}, messages), DiscordWebSessionClient.Json);
        }
        if(method=="messagePrepareAttachments")
        {
            var channel=request.GetProperty("channelId").GetString()!;
            var files=request.GetProperty("files").EnumerateArray().ToArray();
            if(channel!="10" || files.Length!=1 || files[0].GetProperty("fileName").GetString()!="messages-upload-fixture.txt" ||
                files[0].GetProperty("byteLength").GetInt64()!=16384)throw new InvalidDataException("Only the isolated synthetic upload file is admitted.");
            lock(messageGate){++messageUploadPreparations;WriteMessageAudit();}
            return JsonSerializer.SerializeToElement(new DiscordUploadBatch(state.Account,channel,new string('f',32),
                DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds(),
                [new("0","messages-upload-fixture.txt",16384,"https://discord-attachments-uploads-prd.storage.googleapis.com/never-requested","fixture/messages-upload-fixture.txt")]));
        }
        lock (messageGate)
        {
            ++messageMutationAttempts; WriteMessageAudit();
            if(allowsSyntheticSend && method=="messageSend" && request.GetProperty("channelId").GetString() is "10" && sentFixtureMessage is null &&
                request.GetProperty("content").GetString()=="Generated outgoing message")
            {
                var nonce=request.GetProperty("nonce").GetString()!;
                var sent=new DiscordMessage("1121","10",state.Account,"You",null,"Generated outgoing message",1700001121000,null,nonce,[],true);
                Volatile.Write(ref sentFixtureMessage,sent);
                return JsonSerializer.SerializeToElement(new DiscordMessageSendResult(state.Account,"10",nonce,"sent",sent,null));
            }
        }
        throw new InvalidOperationException("Synthetic Messages validation prohibits provider mutations: " + method);
    }

    private DiscordMessage SyntheticMessage(int id,string channel)=>mediaMessages&&id>=1117?MediaMessage(id,channel):new(id.ToString(),channel,id%2==0?"1":"2",
        id%2==0?"You":"Avery",null,heldScrollMessages?string.Join('\n',Enumerable.Range(1,1+id%5).Select(line=>$"Message {id}, line {line}: synthetic conversation content.")):LongMessage(id),1700000000000+id*1000L,null,null,
        id==1120?Enumerable.Range(1,10).Select(index=>new DiscordMessageAttachment((5000+index).ToString(),"fixture-"+index+".txt","text/plain",128,null)).ToArray():[],id%2==0);

    private static DiscordMessage MediaMessage(int id,string channel)
    {
        var message=new DiscordMessage(id.ToString(),channel,id%2==0?"1":"2",id%2==0?"You":"Avery",null,
            id switch {1117=>"Ready when you are. I'll meet you in the lobby.",1118=>"Here's the guide: https://example.invalid/guide",
                1119=>"Found this breakdown of controller media playback.\nhttps://example.invalid/media-design#:~:text=readable%20layouts%20and%20predictable%20navigation",_=>"A few captures from the last session."},
            1700000000000+id*1000L,null,null,[],id%2==0);
        if(id==1117)return message with {Content="A clip from the last session.",Attachments=[
            new("5003","Match highlight.mp4","video/mp4",4096,"https://cdn.discordapp.com/attachments/10/5003/widgetrail-fixture.mp4")]};
        if(id==1119)return message with {Embeds=[new("Video Player UI: Patterns & UX Tips",
            "Explore playback controls, readable layouts and predictable navigation for controller-first apps.","https://example.invalid/media-design",null)]};
        if(id==1120)return message with {Attachments=[
            new("5001","Mountain lookout.png","image/png",4096,"https://cdn.discordapp.com/attachments/10/5001/widgetrail-fixture.png"),
            new("5002","Co-op victory.png","image/png",4096,"https://cdn.discordapp.com/attachments/10/5002/widgetrail-fixture.png")]};
        return message;
    }

    internal async Task UploadMessagesFixtureAsync(DiscordUploadTarget target,Stream source,Action<long> progress,CancellationToken token)
    {
        if(messageAuditPath is null || target.FileName!="messages-upload-fixture.txt" || target.ByteLength!=16384)
            throw new InvalidDataException("Synthetic upload is unavailable.");
        var buffer=new byte[8192];await source.ReadExactlyAsync(buffer,token);
        if(buffer.Any(value=>value!=(byte)'x'))throw new InvalidDataException("Unexpected synthetic upload contents.");
        lock(messageGate){++messageUploads;WriteMessageAudit();}
        progress(buffer.Length);
        try{await Task.Delay(Timeout.Infinite,token);}
        catch(OperationCanceledException) when(token.IsCancellationRequested)
        {lock(messageGate){++messageUploadCancellations;WriteMessageAudit();}throw;}
    }

    private static string LongMessage(int id) => id==1111?"Hi":id==1112?"Short\nreply":"Long fixture message " + id + "\n**Bold** *Italic* __Underline__ ~~Strike~~ 👨‍👩‍👧‍👦\n" + string.Join('\n', Enumerable.Range(1, 26)
        .Select(line => $"Line {line}: this synthetic conversation checks readable full text, scrolling, controller entry and keyboard return."));

    private void WriteMessageAudit()
    {
        if (messageAuditPath is null) return;
        var staging = messageAuditPath + ".tmp";
        File.WriteAllText(staging, JsonSerializer.Serialize(new { conversations = messageConversationReads, history = messageHistoryReads, mutationAttempts = messageMutationAttempts,
            uploadPreparations=messageUploadPreparations,uploads=messageUploads,uploadCancellations=messageUploadCancellations,readAcks=messageReadAcks,readTargetsAheadOfHead,
            conversationRanges,conversationLookups }));
        File.Move(staging, messageAuditPath, true);
    }
    private DiscordConversation[] SyntheticConversations()=>richConversations?RichConversations():[
        new DiscordConversation("10", "10", null, "Avery — synthetic messages", "dm", null, "1120", messageReadHeads.GetValueOrDefault("10")<1120, true)
            {OldestUnreadMessageId=allowsSyntheticSend?"1040":null},
        new("11", "11", null, "Morgan — synthetic messages", "dm", null, "1120", false, true),
        .. largeConversations ? Enumerable.Range(12,118).Select(id=>new DiscordConversation(id.ToString(),id.ToString(),null,
            $"Synthetic friend {id:000}","dm",null,"1120",false,true)) : []
    ];
    private DiscordConversation[] RichConversations()
    {
        var now=DateTimeOffset.Now;
        return [
            new("10","10","2","Avery","dm",publicPortrait?"https://cdn.discordapp.com/embed/avatars/0.png":null,"1120",messageReadHeads.GetValueOrDefault("10")<1120,true)
                {Presence="online",LastMessageTimestamp=now.AddMinutes(-4).ToUnixTimeMilliseconds(),LastMessagePreview="Shared a GIF"},
            new("11","11","3","Morgan","dm",null,"1120",true,true)
                {Presence="idle",LastMessageTimestamp=now.AddMinutes(-30).ToUnixTimeMilliseconds(),LastMessagePreview="Are we playing co-op tonight?"},
            new("12","12","4","Sky","dm",null,"1120",false,true)
                {Presence="dnd",LastMessageTimestamp=now.AddDays(-1).ToUnixTimeMilliseconds(),LastMessagePreview="Shared 4 attachments"},
            new("13","13",null,"Reese, Morgan","group",null,"1120",false,true)
                {LastMessageTimestamp=now.AddDays(-1).ToUnixTimeMilliseconds(),LastMessagePreview="Join us in the lobby",LastMessageAuthorName="Reese"},
            new("14","14","5","Alexandra with a long display name","dm",null,"1120",false,true)
                {Presence="offline",LastMessageTimestamp=now.AddDays(-3).ToUnixTimeMilliseconds(),LastMessagePreview="Updated the controller layout",LastMessageIsOwn=true},
            new("15","15","6","Jordan","dm",null,null,false,true)];
    }
}
