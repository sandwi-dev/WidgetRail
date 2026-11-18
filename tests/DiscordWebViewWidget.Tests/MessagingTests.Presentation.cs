using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class MessagingTests
{
    private static async Task MessagePresentationAsync(Action<bool,string> check)
    {
        await using var scenario=await Scenario.OpenAsync(new MessagesSession{LiveUpdates=true});
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        var session=scenario.Session;
        using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"messages.presentation.rows");
        rows.PublishSnapshot();
        using(var initial=await rows.AcquireAsync("discord.message.history",0,1))
            check(initial.Range.Items.Single().Root.FocusPresentation is null,"Ordinary message text has no redundant rich preview");
        var rich=new DiscordMessage("100","10","2","Friend",null,"**Hello** <:one:501> <:two:502> <:three:503> <:four:504>",1,null,null,
            Enumerable.Range(0,10).Select(i=>new DiscordMessageAttachment((1000+i).ToString(),"image"+i+".png","image/png",200,
                "https://cdn.discordapp.com/attachments/10/1000/image"+i+".png")).ToArray(),false,true)
        {
            Mentions=[new("2","Friend")],Reply=new("99","Original author","Quoted text",false),
            Embeds=[new("Title one","Description","https://example.test","https://media.discordapp.net/one.png"),
                new("Title two","Description","https://example.test/two","https://media.discordapp.net/two.png")],
            Reactions=Enumerable.Range(0,12).Select(i=>new DiscordMessageReaction("😀",null,i+1,i==0)).ToArray(),
            Stickers=[new("Sticker","https://cdn.discordapp.com/stickers/500.png"),new("Animated",null),new("Third",null)],
            Poll=new("Question?",Enumerable.Range(0,10).Select(i=>"Choice "+i).ToArray(),false),SystemText="Voice call"
        };
        check(rich.IsValid("10"),"Bounded rich message metadata satisfies the provider contract");
        await Publish(rich);
        using(var range=await Acquire())
        {
            var root=range.Range.Items.Single().Root;var nodes=Nodes(root).ToArray();
            check(nodes.Single(node=>node.Id==root.FocusVisualTargetId).GridCell?.HorizontalAlignment==GridCellAlignment.Start&&nodes.Any(node=>node.Text?.Contains("Quoted text")==true),
                "Received message layout has left alignment and inline reply excerpt");
            check(nodes.Any(node=>node.Text?.Contains("Question?")==true)&&nodes.Any(node=>node.StyleClasses.Contains("discord-message-reaction-own")),
                "Poll and reaction data remain readable inline");
            check(root.FocusPresentation is not null&&Nodes(root.FocusPresentation).Count(node=>node.Kind==ViewNodeKind.Image)==1,
                "Attachment preview realizes one media item and keeps remaining links available");
        }
        var video=rich with{Content="A clip",Attachments=[new("9000","clip.mp4","video/mp4",1000,"https://cdn.discordapp.com/attachments/10/9000/clip.mp4")],
            Embeds=[],Stickers=[],Reactions=[],Poll=null,Reply=null,IsOwn=true,ContentTruncated=false};
        await Publish(video);
        using(var range=await Acquire())
        {
            var root=range.Range.Items.Single().Root;var media=Nodes(root.FocusPresentation!).Single(node=>node.MediaPlayer is not null).MediaPlayer!;
            check(Nodes(root).Single(node=>node.Id==root.FocusVisualTargetId).GridCell?.HorizontalAlignment==GridCellAlignment.End&&!media.Options.AutoPlay&&media.Options.Muted,
                "Own message aligns right and video preview waits for explicit playback");
        }
        await Publish(rich with{Content="||secret https://secret.example||",Reply=new("99","Author","||private quote||",false),
            Attachments=[new("9001","SPOILER_secret.png","image/png",100,"https://cdn.discordapp.com/attachments/10/9001/secret.png")],
            Stickers=[],Reactions=[],Poll=null});
        using(var range=await Acquire())
        {
            var root=range.Range.Items.Single().Root;
            check(root.FocusPresentation is null&&Nodes(root).All(node=>node.Kind!=ViewNodeKind.Image||node.ImageSource?.Contains("secret")!=true)&&
                Nodes(root).All(node=>node.Text?.Contains("secret")!=true&&node.Text?.Contains("private quote")!=true),
                "Hidden spoilers expose no attachment, link card or quote content");
            check(root.ContextActions.Any(action=>action.Label=="Reveal spoilers"),"Spoilers provide explicit context-menu reveal");
        }
        check(session.Calls.Count(call=>call.Method=="messageHistory")==1&&session.Sends==0,"Rich presentation uses supplied data without extra history or send calls");

        async Task<WidgetIndexedCollectionTestLease> Acquire()
        {
            try{return await rows.AcquireAsync("discord.message.history",0,1);}
            catch(ProtocolValidationException error){throw new InvalidDataException(string.Join("; ",error.Errors),error);}
        }

        async Task Publish(DiscordMessage message)
        {
            var revision=scenario.Find("discord.message.history")!.IndexedCollection!.ContentRevision;
            session.Changes.Enqueue(new(++session.MessageRevision,"upsert",message.Id,message));
            await scenario.Widget.OnActionAsync(new("refresh","test"));
            await scenario.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Wait(()=>scenario.Find("discord.message.history")!.IndexedCollection!.ContentRevision>revision);
            rows.PublishSnapshot();
        }
    }
}
