using System.Text.Json;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.Samples.DiscordWebView.Domain;

internal sealed partial class SyntheticDiscordSession
{
    private readonly object messageGate = new();
    private string? messageAuditPath;
    private int messageConversationReads, messageMutationAttempts, messageUploadPreparations, messageUploads, messageUploadCancellations;
    private readonly List<object> messageHistoryReads = [];

    private void InitializeMessagesFixture(string? auditOverride=null)
    {
        var marker = Path.Combine(AppContext.BaseDirectory, "fixture-messages.enabled");
        if (auditOverride is null && !File.Exists(marker)) return;
        var audit = auditOverride ?? File.ReadAllText(marker).Trim();
        if (!Path.IsPathFullyQualified(audit) || Path.GetFileName(audit) != "messages-provider.json") throw new InvalidDataException("Invalid synthetic message audit path.");
        messageAuditPath = audit;
        state = state with { Messaging = new(state.Account, "ready", true, true, true, 2000, 50, MaximumAttachmentBytes: 1024 * 1024) };
        WriteMessageAudit();
    }

    private async ValueTask<JsonElement> InvokeMessagesFixtureAsync(string method, JsonElement request, CancellationToken token)
    {
        if (request.GetProperty("account").GetString() != state.Account) throw new InvalidDataException("Synthetic message account changed.");
        if (method == "messageObserve") return JsonSerializer.SerializeToElement(new { ok = true });
        if (method == "messageConversations")
        {
            var all = new DiscordConversation[]
            {
                new("10", "10", null, "Avery — synthetic messages", "dm", null, "1120", true, true),
                new("11", "11", null, "Morgan — synthetic messages", "dm", null, "1120", false, true)
            };
            var search = request.GetProperty("query").GetString() ?? "";
            var offset = request.GetProperty("offset").GetInt32(); var limit = request.GetProperty("limit").GetInt32();
            var matches = all.Where(item => item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
            lock (messageGate) { ++messageConversationReads; WriteMessageAudit(); }
            return JsonSerializer.SerializeToElement(new DiscordConversationPage(new(state.Account, "fixture.messages.v1", matches.Length, offset, offset + limit < matches.Length),
                matches.Skip(offset).Take(limit).ToArray()), DiscordWebSessionClient.Json);
        }
        if (method == "messageHistory")
        {
            var channel = request.GetProperty("channelId").GetString()!;
            if (channel is not ("10" or "11")) throw new InvalidDataException("Unknown synthetic conversation.");
            var before=request.TryGetProperty("before",out var beforeValue)?beforeValue.GetString():null;
            var after=request.TryGetProperty("after",out var afterValue)?afterValue.GetString():null;
            var around=request.TryGetProperty("around",out var aroundValue)?aroundValue.GetString():null;
            var limit = request.GetProperty("limit").GetInt32();
            if (limit is < 1 or > 50 || new[]{before,after,around}.Count(value=>value is not null)>1) throw new InvalidDataException("Unbounded synthetic history request.");
            var ids = Enumerable.Range(1001, 120).Where(id => before is null || id < int.Parse(before)).Where(id => after is null || id > int.Parse(after));
            var pageIds = (around is not null?ids.OrderBy(id=>Math.Abs(id-int.Parse(around))).Take(limit).Order():after is null ? ids.TakeLast(limit) : ids.Take(limit)).ToArray();
            lock (messageGate) { messageHistoryReads.Add(new { channel, before, after, around, limit, count = pageIds.Length }); WriteMessageAudit(); }
            await Task.Delay(100, token); // Exercise real loading/publication without a remote service.
            var messages = pageIds.Select(id => new DiscordMessage(id.ToString(), channel, id % 2 == 0 ? "1" : "2",
                id % 2 == 0 ? "You" : "Avery", null, LongMessage(id), 1700000000000 + id * 1000L, null, null,
                id==1120?Enumerable.Range(1,10).Select(index=>new DiscordMessageAttachment((5000+index).ToString(),"fixture-"+index+".txt","text/plain",128,null)).ToArray():[], id % 2 == 0)).ToArray();
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
        lock (messageGate) { ++messageMutationAttempts; WriteMessageAudit(); }
        throw new InvalidOperationException("Synthetic Messages validation prohibits provider mutations: " + method);
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
            uploadPreparations=messageUploadPreparations,uploads=messageUploads,uploadCancellations=messageUploadCancellations }));
        File.Move(staging, messageAuditPath, true);
    }
}
