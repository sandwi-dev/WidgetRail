using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private static string MessageExcerpt(string content,bool reveal)=>
        string.Concat(DiscordMessageText.Parse(content,revealSpoilers:reveal,contentTruncated:true).Select(part=>part.Text));

    private static void AddMessageExtras(MessageLine line,WidgetIndexedItemContext ids,List<WidgetElement> body,List<WidgetElement> preview)
    {
        var item=line.Message;
        // Discord embeds can disclose a URL inside a spoiler. Keep the entire
        // supplied embed concealed until explicit reveal, including image fetch.
        var hideEmbeds=!line.RevealSpoilers&&item.Content.Contains("||",StringComparison.Ordinal);
        if(hideEmbeds&&item.Embeds.Length>0)body.Add(UI.Text("[Hidden link preview]",ids.Id("embeds-hidden")).Classes("discord-text-spoiler"));
        else for(var i=0;i<item.Embeds.Length;i++)
        {
            var embed=item.Embeds[i];
            var copy=MessageExcerpt(embed.Title+(embed.Description.Length>0?"\n"+embed.Description:""),line.RevealSpoilers);
            if(copy.Length>0)body.Add(UI.Text(copy,ids.Id("embed-copy-"+i)).Classes("discord-message-embed"));
            if(embed.ImageUrl is {} image)body.Add(UI.Image(image,ids.Id("embed-image-"+i),embed.Title.Length>0?embed.Title:"Link image",ImageFit.Contain)
                .Classes("discord-message-image"));
            if(embed.Url is {} url)preview.Add(UI.Button(embed.Title.Length>0?embed.Title:"Open link",ids.Id("embed-open-"+i),ids.Id("embed-link-"+i))
                .OpenIntent(WidgetIntentContracts.Web,JsonSerializer.SerializeToElement(new{url})).Classes("discord-control"));
        }
        if(item.Stickers.FirstOrDefault() is {} sticker)
            body.Add(sticker.ImageUrl is {} image?UI.Image(image,ids.Id("sticker"),sticker.Name,ImageFit.Contain).Classes("discord-message-sticker"):
                UI.Text("Sticker: "+sticker.Name,ids.Id("sticker")).Classes("discord-secondary"));
        if(item.Stickers.Length>1)body.Add(UI.Text(string.Join(" · ",item.Stickers.Skip(1).Select(sticker=>sticker.Name)),ids.Id("other-stickers")).Classes("discord-secondary"));
        if(item.Poll is {} poll)
        {
            body.Add(UI.Text(poll.Question+"\n"+string.Join("\n",poll.Answers.Take(3))+
                (poll.Answers.Length>3?"\n…":"")+"\n"+(poll.Closed?"Poll closed":"Vote in Discord"),ids.Id("poll")).Classes("discord-message-embed"));
            if(poll.Answers.Length>3)preview.Add(UI.Text(poll.Question+"\n"+string.Join("\n",poll.Answers),ids.Id("poll-details")).Classes("discord-message-text"));
        }
        if(item.Reactions.Length>0)
            body.Add(UI.RichText(ids.Id("reactions"),item.Reactions.Select((reaction,index)=>
                UI.Text((reaction.EmojiId is null?reaction.Name:":"+reaction.Name+":")+" "+reaction.Count+(index<item.Reactions.Length-1?"   ":""),ids.Id("reaction-"+index))
                    .Classes(reaction.IsOwn?"discord-message-reaction-own":"discord-message-reaction")).ToArray()).Classes("discord-message-reactions"));
    }
}
