using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class MessagingTests
{
    private static async Task MessagePresentationAsync(Action<bool,string> check)
    {
        const string poster="https://media.discordapp.net/attachments/10/9000/clip.mp4?hm=a%2Bb%2fc&format=jpeg&width=512&height=288";
        check(DiscordMessageValues.AttachmentPosterUrl(poster,"9000"),"Static attachment poster accepts an exact bounded signed proxy rendition");
        foreach(var rejected in new[]{poster.Replace("/9000/","/9001/"),poster.Replace("media.discordapp.net","media.discordapp.net.evil.test"),
            poster.Replace("https:","http:"),poster+"#frame",poster+"&width=1",poster+"&animated=true",poster+"&FIT=cover",
            poster.Replace("format=jpeg","format=gif"),poster.Replace("width=512","width=513"),poster.Replace("height=288","height=0"),
            poster.Replace("height=288","height="),poster.Replace("width=512","width=+1"),poster.Replace("width=512","WIDTH=512"),
            poster.Replace("&height=288","")})
            check(!DiscordMessageValues.AttachmentPosterUrl(rejected,"9000"),"Poster validation rejects mismatched identity or unsafe rendition metadata");
        await ConversationPresentationAsync(check);
        await using var scenario=await Scenario.OpenAsync(new MessagesSession{LiveUpdates=true});
        await scenario.Act("discord.conversation.10");await scenario.WaitForHistory();
        var session=scenario.Session;
        using var rows=WidgetTestHost.CreateIndexedCollectionHost(scenario.Widget,"messages.presentation.rows");
        rows.PublishSnapshot();
        using(var initial=await rows.AcquireAsync("discord.message.history",0,1))
            check(initial.Range.Items.Single().Root.FocusPresentation is null,"Ordinary message text has no redundant rich preview");
        var rich=new DiscordMessage("100","10","2","Friend",null,"**Hello** <:one:501> <:two:502> <:three:503> <:four:504>",1,null,null,
            Enumerable.Range(0,10).Select(i=>new DiscordMessageAttachment((1000+i).ToString(),"image"+i+".png","image/png",200,
                "https://cdn.discordapp.com/attachments/10/1000/image"+i+".png")
                {PosterUrl="https://media.discordapp.net/attachments/10/"+(1000+i)+"/image.png?format=jpeg&width=80&height=60"}).ToArray(),false,true)
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
            var thumbnails=nodes.Where(node=>node.StyleClasses.Contains("discord-message-thumb-image")).ToArray();
            check(thumbnails.Length==2&&thumbnails.All(node=>!node.IsFocusable&&node.ImageIsPublicMedia)&&
                nodes.Count(node=>node.ActionId=="message.media.open")==1,
                "Static thumbnails are bounded to two anonymous images within one gallery action, with no extra focus stops");
            check(nodes.Any(node=>node.Text=="+11")&&nodes.Count(node=>node.ActionId=="message.media.open")==1 && root.IsReadingGroup && root.FocusPresentation is null &&
                nodes.All(node=>!node.Id.EndsWith(".media-title")&&!node.Id.EndsWith(".media-detail")),
                "Attachment tiles show an overflow count without redundant visible title or filenames");
            check(nodes.Single(node=>node.Id==root.FocusVisualTargetId).GridCell?.HorizontalAlignment==GridCellAlignment.Start&&nodes.Any(node=>node.Text?.Contains("Quoted text")==true),
                "Received message layout has left alignment and inline reply excerpt");
            check(nodes.Any(node=>node.Text?.Contains("Question?")==true)&&nodes.Any(node=>node.StyleClasses.Contains("discord-message-reaction-own")),
                "Poll and reaction data remain readable inline");
            var media=await OpenMedia(range);
            check(media?.Gallery is { Count: 13 } && media.Gallery.Select(item => item.Id).Distinct().Count() == 13 &&
                media.Gallery.Count(item => item.Id.StartsWith("attachment.", StringComparison.Ordinal)) == 10,
                "One native gallery declares every attachment, both embed images and the supported sticker");
            check(media?.Gallery?.All(item => item.Source.Kind == MediaPlayerSourceKind.WebImage) == true &&
                nodes.All(node => node.ActionId?.Contains("file-action-", StringComparison.Ordinal) != true),
                "Previewable images use shared image controls without redundant external-open actions");
            check(media!.Gallery!.All(item => item.Source.IsPublicMedia) &&
                nodes.Where(node => node.ImageSource?.StartsWith("https://", StringComparison.Ordinal) == true).All(node => node.ImageIsPublicMedia),
                "Discord remote image declarations retain anonymous same-origin transport through rows and galleries");
            await CloseMedia();
        }
        var video=rich with{Content="A clip",Attachments=[new("9000","clip.mp4","video/mp4",1000,"https://cdn.discordapp.com/attachments/10/9000/clip.mp4"){PosterUrl=poster}],
            Embeds=[],Stickers=[],Reactions=[],Poll=null,Reply=null,IsOwn=true,ContentTruncated=false};
        await Publish(video);
        using(var range=await Acquire())
        {
            var root=range.Range.Items.Single().Root;
            var inline=Nodes(root).Single(node=>node.MediaPlayer?.Presentation==MediaPlayerPresentation.Inline);
            var media=inline.MediaPlayer!;
            check(inline.Text is null && inline.AccessibilityLabel!.Contains("clip.mp4") &&
                Nodes(root).All(node=>node.ActionId!="message.media.open"),
                "A single video exposes a native inline player with accessible naming and no redundant gallery action");
            check(media.Source.IsPublicMedia && media.Source.Location==video.Attachments[0].Url && media.Gallery is null,
                "Single-video playback retains its exact bounded public source in the indexed body");
            check(inline.ImageSource==poster&&inline.ImageIsPublicMedia,"Inline video reuses the verified static poster through checked artwork transport");
            check(Nodes(root).Single(node=>node.Id==root.FocusVisualTargetId).GridCell?.HorizontalAlignment==GridCellAlignment.Start&&!media.Options.AutoPlay&&media.Options.Muted,
                "Own message shares left alignment and inline video waits for explicit playback");
        }
        await Publish(video with { Content = "https://cdn.discordapp.com/attachments/10/9000/clip.mp4", Attachments =
            [video.Attachments[0], new("9002", "animation.gif", "image/gif", 100, "https://cdn.discordapp.com/attachments/10/9002/animation.gif"),
                new("9003", "document.pdf", "application/pdf", 100, "https://cdn.discordapp.com/attachments/10/9003/document.pdf")] });
        using (var range = await Acquire())
        {
            var root=range.Range.Items.Single().Root;
            var media=await OpenMedia(range);
            check(Nodes(root).All(node=>node.MediaPlayer?.Presentation!=MediaPlayerPresentation.Inline) &&
                scenario.Find("discord.message.gallery.stage")?.Kind==ViewNodeKind.Grid && scenario.Find("discord.message.gallery.scroll") is null,
                "Mixed attachments retain one bounded gallery without an inline player or redundant scroller");
            check(media.Gallery is { Count: 2 } && media.Gallery[0].Source.Kind == MediaPlayerSourceKind.WebUrl &&
                media.Gallery[1] is { Source.Kind: MediaPlayerSourceKind.WebImage, Options.AutoPlay: true },
                "Mixed video and GIF attachments use one gallery with per-item playback intent");
            check(Nodes(root).Count(node => node.ActionId?.Contains("file-action-", StringComparison.Ordinal) == true) == 1 &&
                Nodes(root).All(node => node.ActionId?.Contains("inline-link-", StringComparison.Ordinal) != true),
                "Only unsupported documents retain external actions; a previewed media URL is not duplicated as a link action");
            check(Nodes(root).Any(node=>node.Id.EndsWith(".attachment-animation-9002",StringComparison.Ordinal)),"Uploaded GIFs retain passive inline rendering beside their explicit viewer");
            await CloseMedia();
        }
        var animation = new DiscordMessageEmbed("GIF", "", "https://tenor.com/view/fixture", "https://media.tenor.com/fixture.png")
            { VideoUrl = "https://media.tenor.com/fixture.mp4", IsAnimation = true };
        check(animation.IsValid && (animation with { VideoUrl = null }).IsValid &&
            !(animation with { VideoUrl = null, ImageUrl = null }).IsValid &&
            !(animation with { VideoUrl = null, ImageUrl = "https://media.tenor.com.evil.test/fixture.png" }).IsValid &&
            !(animation with { VideoUrl = "https://media.tenor.com.evil.test/fixture.mp4" }).IsValid,
            "Animation metadata requires its approved direct media URL");
        foreach (var host in new[] { "images-ext-1.discordapp.net", "images-ext-2.discordapp.net" })
        {
            var proxy = "https://" + host + "/external/synthetic/https/static.klipy.com/clip.mp4?ex=1&hm=2";
            check((animation with { VideoUrl = proxy, ImageUrl = proxy.Replace("clip.mp4", "poster.webp", StringComparison.Ordinal) }).IsValid &&
                !DiscordMessageValues.MediaUrl(proxy), "Exact Discord external media proxy is admitted for embeds without widening attachment/avatar origins");
        }
        foreach (var rejected in new[] { "https://klipy.com/gifs/wednesday-193", "https://static.klipy.com/clip.mp4",
            "https://images-ext-3.discordapp.net/external/clip.mp4", "https://images-ext-1.discordapp.net.evil.test/clip.mp4",
            "http://images-ext-1.discordapp.net/external/clip.mp4", "https://images-ext-1.discordapp.net:444/external/clip.mp4",
            "https://name:password@images-ext-1.discordapp.net/external/clip.mp4", "https://images-ext-1.discordapp.net/external/clip.mp4#frame" })
            check(!(animation with { VideoUrl = rejected }).IsValid,
                "Embed media rejects unknown origins, provider pages, credentials, unusual ports and fragments");
        await Publish(video with { Content = animation.Url!, Attachments = [], Embeds = [animation] });
        using (var range = await Acquire())
        {
            var root=range.Range.Items.Single().Root;
            check(Nodes(root).All(node => node.Intent is null && node.ActionId != "message.media.open"),
                "Inline GIF-like embeds have no redundant attachment card or external-open action");
            var inline = Nodes(range.Range.Items.Single().Root).Single(node => node.MediaPlayer?.Presentation == MediaPlayerPresentation.Animation);
            check(!inline.IsFocusable && inline.ImageSource == animation.ImageUrl && inline.MediaPlayer!.Source.Location == animation.VideoUrl,
                "GIF-like embeds use the passive native animation and bounded thumbnail poster in the message row");
        }
        var imageAnimation = new DiscordMessageEmbed("Animated image", "", "https://klipy.com/gifs/example",
            "https://images-ext-2.discordapp.net/external/animation") { IsAnimation = true };
        check(imageAnimation.IsValid && !(imageAnimation with { ImageUrl = null }).IsValid,
            "Animated image metadata requires an admitted image or video source");
        await Publish(video with { Content = imageAnimation.Url!, Attachments = [], Embeds = [imageAnimation] });
        using(var range=await Acquire())
        {
            var root=range.Range.Items.Single().Root;
            check(Nodes(root).Any(node=>node.ImageSource==imageAnimation.ImageUrl&&node.ImageIsPublicMedia&&!node.IsFocusable)&&Nodes(root).All(node=>node.Intent is null),
                "Image GIF embeds use bounded passive artwork without a duplicate page-opening action");
            check(Nodes(root).All(node=>node.ActionId!="message.media.open"),
                "Inline image animations have no redundant attachment card");
        }
        var animatedFile = new DiscordMessageAttachment("9005", "loop.webp", "image/webp", 100,
            "https://cdn.discordapp.com/attachments/10/9005/loop.webp") { IsAnimated = true };
        check(animatedFile.HasAnimation && !(animatedFile with { ContentType = "audio/mpeg" }).HasAnimation,
            "Attachment animation flags cannot turn ordinary audio into autoplay media");
        await Publish(video with { Attachments = [animatedFile], Embeds = [] });
        using(var range=await Acquire())
        {
            check(Nodes(range.Range.Items.Single().Root).Any(node=>node.ImageSource==animatedFile.Url),
                "Animated image attachments are recognized beyond the GIF MIME type");
            check(Nodes(range.Range.Items.Single().Root).All(node=>node.ActionId!="message.media.open"),
                "Uploaded inline animations have no redundant attachment card");
        }
        foreach(var isVideo in new[]{false,true})
        {
            var direct="https://cdn.discordapp.com/attachments/10/9004/preview."+(isVideo?"mp4":"png");
            var embed=new DiscordMessageEmbed("Preview","",direct,isVideo?null:direct){VideoUrl=isVideo?direct:null};
            await Publish(video with{Content=direct,Attachments=[],Embeds=[embed],Stickers=[]});
            using var range=await Acquire();
            var root=range.Range.Items.Single().Root;var media=await OpenMedia(range);
            check(media.Gallery?.Single().Source.Location==direct && Nodes(root).All(node=>node.Intent is null),
                "A directly previewable "+(isVideo?"video":"image")+" embed does not also expose an external-open action");
            await CloseMedia();
        }
        const string link="https://example.test/article";
        await Publish(video with{Content=link+" "+link+" "+link+"?page=2",Attachments=[],
            Embeds=[new("Article title","",link,null),new("Duplicate card","",link,null)],Stickers=[]});
        using(var range=await Acquire())
        {
            var root=range.Range.Items.Single().Root;
            var actions=Nodes(root).Where(node=>node.Intent is not null).ToArray();
            check(actions.Length==2 && actions.Count(node=>node.StyleClasses.Contains("discord-message-link-card"))==1 &&
                actions.Any(node=>node.Text==link+"?page=2") && root.FocusPresentation is null,
                "Duplicate embed and text URLs have one card action while a distinct URL is an inline action");
            check(!Nodes(root).Any(node=>node.Kind==ViewNodeKind.Image && node.ImageSource?.Contains("example.test")==true),
                "Ordinary web cards do not fetch rich preview artwork");
        }
        const string selection=link+"#:~:text=selected%20words";
        foreach(var content in new[]{selection,"[Read this passage]("+selection+")"})
        {
            await Publish(video with{Content=content,Attachments=[],Embeds=[new("Article title","",link,null)],Stickers=[]});
            using var range=await Acquire();
            var nodes=Nodes(range.Range.Items.Single().Root).ToArray();
            var actions=nodes.Where(node=>node.Intent is not null).ToArray();
            check(actions.Length==1 && actions[0].StyleClasses.Contains("discord-message-link-card") &&
                actions[0].Intent!.Payload.GetProperty("url").GetString()==selection && nodes.All(node=>node.Text!=selection),
                "Text-selection article shows one card that opens the exact authored destination");
            if(content.StartsWith('['))check(nodes.Any(node=>node.Text=="Read this passage" && node.Intent is null &&
                !node.StyleClasses.Contains("discord-text-link")),"A card-backed markdown label remains readable without a false link affordance");
        }
        var secondSelection=link+"#:~:text=another%20passage";
        foreach(var other in new[]{secondSelection,link})
        {
            await Publish(video with{Content=selection+" "+other,Attachments=[],Embeds=[new("Article title","",link,null)],Stickers=[]});
            using var range=await Acquire();
            var actions=Nodes(range.Range.Items.Single().Root).Where(node=>node.Intent is not null).ToArray();
            check(actions.Length==2 && actions.Select(node=>node.Intent!.Payload.GetProperty("url").GetString()).ToHashSet().SetEquals([selection,other]),
                "Distinct authored article destinations retain their two exact actions");
        }
        await Publish(rich with{Content="||secret https://secret.example||",Reply=new("99","Author","||private quote||",false),
            Attachments=[new("9001","SPOILER_secret.png","image/png",100,"https://cdn.discordapp.com/attachments/10/9001/secret.png")
                {PosterUrl="https://media.discordapp.net/attachments/10/9001/secret.png?format=jpeg&width=80&height=60"}],
            Embeds=[animation],Stickers=[],Reactions=[],Poll=null});
        using(var range=await Acquire())
        {
            var root=range.Range.Items.Single().Root;
            check(root.FocusPresentation is null&&Nodes(root).All(node=>node.Kind!=ViewNodeKind.Image||node.ImageSource?.Contains("secret")!=true)&&
                Nodes(root).All(node=>node.Text?.Contains("secret")!=true&&node.Text?.Contains("private quote")!=true),
                "Hidden spoilers expose no attachment, link card or quote content");
            check(root.ContextActions.Any(action=>action.Label=="Reveal spoilers"),"Spoilers provide explicit context-menu reveal");
        }
        check(session.Calls.Count(call=>call.Method=="messageHistory")==1&&session.Sends==0,"Rich presentation uses supplied data without extra history or send calls");
        await Publish(video with { Attachments = [new("9001", "private.gif", "image/gif", 100,
            "https://cdn.discordapp.com/attachments/10/9001/private.gif") { IsSpoiler = true }] });
        using (var range = await Acquire())
        {
            var root = range.Range.Items.Single().Root;
            check(Nodes(root).All(node => node.ImageSource?.Contains("private.gif") != true && node.ActionId != "message.media.open") &&
                root.ContextActions.Any(action => action.Label == "Reveal spoilers"),
                "Discord attachment spoiler flag suppresses inline demand and gallery access without a filename prefix");
        }
        await Publish(video with { Attachments=[video.Attachments[0],new("9004","still.png","image/png",100,"https://cdn.discordapp.com/attachments/10/9004/still.png")] });
        using(var range=await Acquire())
        {
            await OpenMedia(range);
            var oldModal=scenario.Snapshot();
            session.Inner.State=session.Inner.State with {Account="9"};
            await scenario.Widget.OnActionAsync(new("refresh","test"));
            await scenario.Widget.WaitForStateReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(scenario.Find("discord.message.gallery.media") is null && scenario.Snapshot().Root.Kind!=ViewNodeKind.ModalLayer,
                "Replacing the Discord account retires an open attachment gallery and its media declarations");
            var activeScope=scenario.Snapshot().ActiveInputScopeId;
            await scenario.Widget.OnActionAsync(new(oldModal.Root.ModalDismissal!.ActionId,"discord.message.gallery",InputScopeId:oldModal.ActiveInputScopeId));
            check(scenario.Snapshot().ActiveInputScopeId==activeScope,"A retired gallery close action cannot navigate a replacement account");
        }

        async Task<MediaPlayerDefinition> OpenMedia(WidgetIndexedCollectionTestLease lease)
        {
            var row=lease.Range.Items.Single();
            var open=Nodes(row.Root).Single(node=>node.ActionId=="message.media.open");
            check(lease.RouteInlineAction(row.Key,open.Id,open.ActionId!)==WidgetOperationAdmission.Enqueued,
                "Gallery opening uses admitted indexed child intent");
            await WidgetTestHost.WaitForActionsAsync(scenario.Widget);
            var snapshot=scenario.Snapshot();
            check(snapshot.Root.Kind==ViewNodeKind.ModalLayer && WidgetTestHost.ValidateWinUiPresentation(snapshot).Count==0,
                "Explicit attachment modal uses valid host presentation and input scope");
            rows.PublishSnapshot();
            check(lease.RouteInlineAction(row.Key,open.Id,open.ActionId!) is null,"Background message cannot reopen a gallery while its modal owns input");
            return scenario.Find("discord.message.gallery.media")!.MediaPlayer!;
        }
        async Task CloseMedia()
        {
            var snapshot=scenario.Snapshot();
            await scenario.Widget.OnActionAsync(new(snapshot.Root.ModalDismissal!.ActionId,"discord.message.gallery",InputScopeId:snapshot.ActiveInputScopeId));
            check(scenario.Snapshot().Root.Kind!=ViewNodeKind.ModalLayer && scenario.Snapshot().FocusGroupEntryRequest?.IndexedItem?.ItemKey=="100",
                "Closing the gallery returns through SDK navigation to the exact message occurrence");
            rows.PublishSnapshot();
        }

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
