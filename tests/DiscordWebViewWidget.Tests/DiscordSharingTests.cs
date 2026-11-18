using System.Collections.Concurrent;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class DiscordSharingTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        await ColdSharingAsync(check);
        await RepeatedSharingAsync(check);
        await RecipientAdmissionAsync(check);
        await using (var existing = new SharingFixture())
        {
            await existing.Start();
            await existing.Act("discord.messages.open");
            await existing.Until(() => existing.Find("discord.conversation.10") is not null);
            await existing.Act("discord.conversation.10");
            await existing.Until(() => existing.Find("discord.message.draft") is not null);
            check(await existing.Widget.OnIntentAsync(existing.Request)==WidgetIntentResult.Accepted,
                "A new share is accepted while an existing empty conversation is open");
            await existing.Until(() => existing.Find("discord.messages.cancel-share") is not null);
            check(existing.Find("discord.messages.search") is not null && existing.Find("discord.message.draft") is null &&
                WidgetTestHost.ValidateWinUiPresentation(existing.Snapshot()).Count==0,
                "Incoming share replaces the conversation visit with recipient selection without duplicating its navigation scope");
            await existing.Act("discord.messages.back");
            check(existing.Find("discord.messages.open") is not null,
                "Back from externally opened recipient selection returns to Discord controls with a valid focus target");
        }
        await using (var cancelled = new SharingFixture())
        {
            await cancelled.Start();
            _ = await cancelled.Widget.OnIntentAsync(cancelled.Request);
            await cancelled.Until(() => cancelled.Find("discord.messages.cancel-share") is not null);
            await cancelled.Act("discord.messages.cancel-share");
            await cancelled.Until(() => cancelled.Released.Count == 2);
            check(await cancelled.Widget.IsIntentAvailableAsync(cancelled.Request) && cancelled.Session.Mutations == 0 && cancelled.Reads == 0,
                "Cancelling an incoming share releases its grants and restores availability without reading or uploading files");
        }
        await using (var fixture = new SharingFixture())
        {
            await fixture.Start();
            check(await fixture.Widget.IsIntentAvailableAsync(fixture.Request), "Discord offers the file-share handler only while messaging is available");
            var result = await fixture.Widget.OnIntentAsync(fixture.Request);
            await fixture.Until(() => fixture.Find("discord.conversation.10") is not null);
            check(result == WidgetIntentResult.Accepted && fixture.Accepts == 1 && fixture.Find("discord.messages.notice")?.Text?.Contains("2 shared file") == true,
                "A shared capture opens the recipient picker with the exact accepted file count");
            check(fixture.Session.Mutations == 0 && fixture.Reads == 0 && fixture.Chooses == 0,
                "Receiving a file-share intent never uploads, reads whole files, opens a picker or sends automatically");
            check(await fixture.Widget.IsIntentAvailableAsync(fixture.Request) && await fixture.Widget.OnIntentAsync(fixture.Request) == WidgetIntentResult.Accepted && fixture.Accepts == 2 &&
                fixture.Find("discord.incoming.file.2") is null,
                "Repeated unassigned sharing stays in recipient selection and deduplicates identical granted files");
            await fixture.Act("discord.conversation.10");
            await fixture.Until(() => fixture.Find("discord.message.file.1") is not null);
            check(fixture.Find("discord.message.file.0")?.Text == "achievement.png · Remove" && fixture.Find("discord.message.file.1")?.Text == "clip.mp4 · Remove" && fixture.Session.Mutations == 0,
                "Choosing a recipient preserves exact file metadata and still requires an explicit Send");
            await fixture.Act("discord.messages.back");
            await fixture.Until(() => fixture.Find("discord.conversation.11") is not null);
            await fixture.Act("discord.conversation.11");
            await fixture.Until(() => fixture.Find("discord.message.title")?.Text=="Second contact");
            check(fixture.Released.IsEmpty && fixture.Find("discord.message.file.0") is null,
                "Switching recipients retains the original draft without carrying its files to another contact");
            await fixture.Act("discord.messages.back");await fixture.Act("discord.conversation.10");
            check(fixture.Find("discord.message.file.1") is not null,"Reopening the original recipient restores its attachments");
        }

        await using (var fixture = new SharingFixture())
        {
            await fixture.Start();
            _ = await fixture.Widget.OnIntentAsync(fixture.Request);
            await fixture.ChangeAccount("2");
            await fixture.Until(() => fixture.Released.Count == 2);
            check(fixture.Released.Distinct().Count() == 2 && fixture.Session.Mutations == 0,
                "Changing account retires every incoming file reference without sending");
        }

        foreach (var stale in new[] { "account", "session" })
        {
            await using var fixture = new SharingFixture();
            fixture.AcceptGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await fixture.Start();
            var receiving = fixture.Widget.OnIntentAsync(fixture.Request).AsTask();
            await fixture.Until(() => fixture.Accepts == 1);
            if (stale == "account") await fixture.ChangeAccount("2");
            if (stale == "session") { fixture.Session.Inner.Reference = new(new string('d', 32)); await fixture.ChangeAccount("1"); }
            fixture.AcceptGate?.TrySetResult();
            check(await receiving == WidgetIntentResult.Rejected, "Late account/session incoming attachments are rejected: " + stale);
            await fixture.Until(() => fixture.Released.Count == fixture.Files.Length);
            check(fixture.Released.Distinct().Count() == fixture.Files.Length && fixture.Session.Mutations == 0,
                "Rejected incoming references are each released without provider side effects: " + stale);
        }
    }

    private sealed class SharingFixture : IAsyncDisposable
    {
        internal readonly SharingSession Session = new();
        internal readonly DiscordWebViewWidget Widget;
        internal readonly ConcurrentQueue<string> Released = new();
        internal WidgetIntentRequest Request = WidgetIntentRequest.Create(WidgetIntentContracts.Files,
            JsonSerializer.SerializeToElement(new { offerId = new string('c', 32) }));
        internal WidgetFileReference[] Files = [new(new string('a', 32), "achievement.png", "image/png", 12, long.MaxValue), new(new string('b', 32), "clip.mp4", "video/mp4", 20, long.MaxValue)];
        internal TaskCompletionSource? AcceptGate;
        internal int Accepts, Chooses, Reads;
        internal SharingFixture()
        {
            var services = new WidgetTestHostServicesBuilder()
                .WithHandler(WidgetFileCapabilities.Accept, async (request, _) =>
                {
                    if (request.Id != Request.Payload.GetProperty("offerId").GetString()) throw new Exception("File-share token changed");
                    Interlocked.Increment(ref Accepts);
                    var accepted=Files.ToArray();
                    if (AcceptGate is { } gate) await gate.Task;
                    return new WidgetFileSelection(accepted);
                })
                .WithHandler(WidgetFileCapabilities.Release, (request, _) =>
                { Released.Enqueue(request.Id); return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); })
                .WithHandler(WidgetFileCapabilities.Choose, (_, _) =>
                { Interlocked.Increment(ref Chooses); return ValueTask.FromResult(new WidgetFileSelection([])); })
                .WithHandler(WidgetFileCapabilities.Get, (_, _) =>
                { Interlocked.Increment(ref Reads); throw new Exception("A received intent attempted to upload before Send"); })
                .Build();
            Widget = WidgetTestHost.Attach(new DiscordWebViewWidget(Session, refreshInterval: TimeSpan.FromHours(1)), services);
        }
        internal async Task Start()
        {
            await WidgetTestHost.InitializeAsync(Widget);
            await WidgetTestHost.SetLifecycleStateAsync(Widget, WidgetLifecycleState.Interactive);
            await Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        internal async Task ChangeAccount(string account)
        {
            Session.Inner.State = Session.Inner.State with { Account = account };
            await Widget.OnActionAsync(new("refresh", "test"));
            await Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        internal ViewSnapshot Snapshot() => Widget.Render().CreateSnapshot("sharing.test", 1);
        internal ViewNode? Find(string id) => Nodes(Snapshot().Root).FirstOrDefault(node => node.Id == id);
        internal async Task Act(string id,string? text=null)
        {
            var snapshot = Snapshot();
            var node = Nodes(snapshot.Root).Single(item => item.Id == id);
            await Widget.OnActionAsync(new(node.ActionId!, id, InputScopeId: snapshot.ActiveInputScopeId) { FocusedElementId = id,CommittedText=text });
        }
        internal async Task Until(Func<bool> condition)
        { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (!condition()) await Task.Delay(10, timeout.Token); }
        public async ValueTask DisposeAsync() { AcceptGate?.TrySetResult(); Session.ReadGate?.TrySetResult(); Session.OpenGate?.TrySetResult(); await WidgetTestHost.DestroyAsync(Widget); }
        private static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
    }

    private sealed class SharingSession : IDiscordSession
    {
        internal readonly FakeSession Inner = new();
        internal int Mutations,ConversationReads,OpenRequests;
        internal bool FriendOnly;
        internal TaskCompletionSource? OpenGate;
        internal TaskCompletionSource? ReadGate;
        internal readonly TaskCompletionSource ReadStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool FailRead,CanSend=true,ReferenceUnavailable;
        public WebSessionReference? Reference => ReferenceUnavailable?null:Inner.Reference;
        public async ValueTask<DiscordState> ReadAsync(CancellationToken token)
        {
            ReadStarted.TrySetResult();if(ReadGate is {} gate)await gate.Task.WaitAsync(token);
            if(FailRead)throw new IOException("Synthetic initial provider failure");
            return Inner.State with { Messaging = new(Inner.State.Account, "ready", true, CanSend, true, 2000, 50, MaximumAttachmentBytes: 1024) };
        }
        public ValueTask<JsonElement> InvokeAsync(string method, object? args, CancellationToken token)
        {
            if (method == "messageConversations") {++ConversationReads;return ValueTask.FromResult(JsonSerializer.SerializeToElement(new DiscordConversationPage(
                new(Inner.State.Account, "v1", 2, 0, false),
                [new("10", FriendOnly?null:"10", FriendOnly?"30":null, "First contact", FriendOnly?"friend":"dm", null, null, false, true), new("11", "11", null, "Second contact", "dm", null, null, false, true)])));}
            if (method == "messageHistory")
            {
                var request = JsonSerializer.SerializeToElement(args);
                return ValueTask.FromResult(JsonSerializer.SerializeToElement(new DiscordMessagePage(new(Inner.State.Account,
                    request.GetProperty("channelId").GetString()!, null, null, false), [])));
            }
            if (method == "messageObserve") return ValueTask.FromResult(JsonSerializer.SerializeToElement(new { ok = true }));
            if(method=="messageOpenDm" && FriendOnly)return new(OpenFriendAsync(token));
            if (method is "messageSend" or "messagePrepareAttachments" or "messageOpenDm")
            { Interlocked.Increment(ref Mutations); throw new Exception("Unexpected provider mutation during incoming share"); }
            return Inner.InvokeAsync(method, args, token);
        }
        private async Task<JsonElement> OpenFriendAsync(CancellationToken token)
        {
            Interlocked.Increment(ref OpenRequests);
            if(OpenGate is {} gate)await gate.Task.WaitAsync(token);
            return JsonSerializer.SerializeToElement(new DiscordOpenConversation(Inner.State.Account,"10","30"));
        }
        public ValueTask<IDiscordReceiver> OpenReceiverAsync(CancellationToken token) => Inner.OpenReceiverAsync(token);
        public ValueTask<WebSessionMicrophoneResult> RequestMicrophoneAsync(CancellationToken token) => Inner.RequestMicrophoneAsync(token);
        public ValueTask<WebSessionPermissionResult> RequestNotificationsAsync(CancellationToken token) => Inner.RequestNotificationsAsync(token);
        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}
