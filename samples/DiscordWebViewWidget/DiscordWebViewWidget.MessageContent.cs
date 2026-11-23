using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private static string MessageExcerpt(string content,bool reveal)=>
        string.Concat(DiscordMessageText.Parse(content,revealSpoilers:reveal,contentTruncated:true).Select(part=>part.Text));

    private static IReadOnlyDictionary<int,string> MessageCardTargets(MessageLine line,HashSet<string> mediaUrls,IReadOnlyList<DiscordTextPart> parts)
    {
        var targets=new Dictionary<int,string>();
        if(HideMessageEmbeds(line))return targets;
        var authored=parts.Select(part=>part.Url).OfType<string>().ToArray();
        for(var index=0;index<line.Message.Embeds.Length;index++)
            if(line.Message.Embeds[index] is {Url:{} url,IsAnimation:false} && !mediaUrls.Contains(url) &&
                DiscordMessageLinks.EmbedTarget(url,authored) is {} target && !mediaUrls.Contains(target))targets.Add(index,target);
        return targets;
    }

    private static void AddMessageExtras(MessageLine line,WidgetIndexedItemContext ids,List<WidgetElement> body,
        HashSet<string> linkTargets,IReadOnlyDictionary<int,string> cardTargets)
    {
        var item=line.Message;
        // Spoiler metadata never creates a request or action before explicit reveal.
        if(HideMessageEmbeds(line)&&item.Embeds.Length>0)
            body.Add(UI.Text("[Hidden link preview]",ids.Id("embeds-hidden")).Classes("discord-text-spoiler"));
        else for(var i=0;i<item.Embeds.Length;i++)
        {
            var embed=item.Embeds[i];
            if(embed is {IsAnimation:true,VideoUrl:{} animationUrl})
            {
                var animation=UI.PublicAnimation(animationUrl,ids.Id("embed-animation-"+i),MediaLabel(embed.Title,"GIF"));
                if(embed.ImageUrl is {} poster)animation=animation.WithPosterSource(poster);
                body.Add(animation.Classes("discord-message-image"));
            }
            else if(embed is {IsAnimation:true,ImageUrl:{} animatedImage})
                body.Add(UI.PublicImage(animatedImage,ids.Id("embed-animation-"+i),MediaLabel(embed.Title,"GIF"),ImageFit.Contain).Classes("discord-message-image"));
            else if(cardTargets.TryGetValue(i,out var url) && linkTargets.Add(url))
            {
                var host=new Uri(url).IdnHost;
                var title=MessageExcerpt(embed.Title,line.RevealSpoilers);
                var description=MessageExcerpt(embed.Description,line.RevealSpoilers);
                var content=new List<WidgetElement>{UI.Text(host,ids.Id("embed-host-"+i)).Classes("discord-link-host"),
                    UI.Text(title.Length>0?title:host,ids.Id("embed-title-"+i)).Classes("discord-link-title")};
                if(description.Length>0)content.Add(UI.Text(description,ids.Id("embed-description-"+i)).Classes("discord-link-description"));
                body.Add(UI.ActionSurface(ids.Id("embed-open-"+i),ids.Id("embed-link-"+i),
                    (title.Length>0?title:host)+", "+host,ActionSurfaceOrientation.Vertical,content.ToArray())
                    .OpenIntent(WidgetIntentContracts.Web,JsonSerializer.SerializeToElement(new{url})).Classes("discord-message-link-card"));
            }
            else if(embed.Url is null)
            {
                var copy=MessageExcerpt(embed.Title+(embed.Description.Length>0?"\n"+embed.Description:""),line.RevealSpoilers);
                if(copy.Length>0)body.Add(UI.Text(copy,ids.Id("embed-copy-"+i)).Classes("discord-message-embed"));
            }
        }
        if(item.Stickers.FirstOrDefault() is {} sticker)
            body.Add(sticker.ImageUrl is {} image?UI.PublicImage(image,ids.Id("sticker"),sticker.Name,ImageFit.Contain).Classes("discord-message-sticker"):
                UI.Text("Sticker: "+sticker.Name,ids.Id("sticker")).Classes("discord-secondary"));
        if(item.Stickers.Length>1)body.Add(UI.Text(string.Join(" · ",item.Stickers.Skip(1).Select(sticker=>sticker.Name)),ids.Id("other-stickers")).Classes("discord-secondary"));
        if(item.Poll is {} poll)
        {
            body.Add(UI.Text(poll.Question+"\n"+string.Join("\n",poll.Answers)+"\n"+
                (poll.Closed?"Poll closed":"Vote in Discord"),ids.Id("poll")).Classes("discord-message-embed"));
        }
        if(item.Reactions.Length>0)
            body.Add(UI.RichText(ids.Id("reactions"),item.Reactions.Select((reaction,index)=>
                UI.Text((reaction.EmojiId is null?reaction.Name:":"+reaction.Name+":")+" "+reaction.Count+(index<item.Reactions.Length-1?"   ":""),ids.Id("reaction-"+index))
                    .Classes(reaction.IsOwn?"discord-message-reaction-own":"discord-message-reaction")).ToArray()).Classes("discord-message-reactions"));
    }
    private static bool HideMessageEmbeds(MessageLine line) => !line.RevealSpoilers && line.Message.Content.Contains("||", StringComparison.Ordinal);
    private static string MediaLabel(string value, string fallback)
    {
        var label = new string(value.Select(character => char.IsControl(character) ? ' ' : character).Take(256).ToArray()).Trim();
        return label.Length == 0 ? fallback : label;
    }
}
