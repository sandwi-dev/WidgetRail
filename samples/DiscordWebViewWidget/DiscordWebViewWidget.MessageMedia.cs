using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private static WidgetElement? MessageInlineVideo(MessageLine line, WidgetIndexedItemContext ids,
        IReadOnlyList<MediaPlayerGalleryItem> media)
    {
        // One video plus any other attachment stays in the explicit gallery,
        // including unsupported files that still have their own download action.
        if (media.Count != 1 || line.Message.Attachments is not
            [{ Url: { } url, ContentType: "video/mp4" or "video/webm", HasAnimation: false } file] ||
            media[0].Id != "attachment." + file.Id) return null;
        var player = UI.InlineVideo(MediaPlayerSource.PublicFile(url), ids.Id("inline-video"), MediaLabel(file.FileName,"Video"));
        if (file.PosterUrl is { } poster) player = player.WithPosterSource(poster);
        return UI.Grid(ids.Id("inline-video-frame"),[GridTrack.Star()],[GridTrack.Star()],
            player.Classes("discord-message-inline-video").InGrid()).Classes("discord-message-inline-video-frame");
    }

    private static WidgetElement MessageMediaThumbnails(MessageLine line,WidgetIndexedItemContext ids,
        IReadOnlyList<MediaPlayerGalleryItem> media)
    {
        var shown=media.Take(2).ToArray();
        var thumbnailIds=WidgetIds.Scope(ids.Id("thumbnails"));
        var thumbnails=shown.Select((item,index)=>
        {
            var file=line.Message.Attachments.FirstOrDefault(file=>"attachment."+file.Id==item.Id);
            var video=file?.ContentType is "video/mp4" or "video/webm" ||
                file is null && item.Source.Kind==MediaPlayerSourceKind.WebUrl;
            var elements=new List<WidgetElement>();
            if(file?.PosterUrl is {} poster)
                elements.Add(UI.PublicImage(poster,thumbnailIds.KeyedId("image",item.Id),item.Label,ImageFit.Cover)
                    .Classes("discord-message-thumb-image").InGrid());
            else elements.Add(UI.Icon(video?WidgetGlyph.Play:WidgetGlyph.Attach,thumbnailIds.KeyedId("fallback",item.Id),item.Label)
                .Classes("discord-message-thumb-symbol").InGrid().AlignInGrid(GridCellAlignment.Center,GridCellAlignment.Center));
            if(video&&file?.PosterUrl is not null)
                elements.Add(UI.Icon(WidgetGlyph.Play,thumbnailIds.KeyedId("play",item.Id),"Video")
                    .Classes("discord-message-thumb-play").InGrid().AlignInGrid(GridCellAlignment.Center,GridCellAlignment.Center));
            if(index==shown.Length-1&&media.Count>shown.Length)
                elements.Add(UI.Stack(ids.Id("thumb-more"),UI.Text($"+{media.Count-shown.Length}",ids.Id("thumb-more-count"),
                    $"{media.Count-shown.Length} more attachments")).Classes("discord-message-thumb-more")
                    .InGrid().AlignInGrid(GridCellAlignment.End,GridCellAlignment.End));
            return (WidgetElement)UI.Grid(thumbnailIds.KeyedId("tile",item.Id),[GridTrack.Star()],[GridTrack.Star()],elements.ToArray())
                .Classes("discord-message-thumb").InGrid(column:index);
        }).ToArray();
        return UI.Grid(ids.Id("thumbnails"),[GridTrack.Star()],shown.Select(_=>GridTrack.Star()).ToArray(),thumbnails)
            .Spacing(0,4)
            .Classes("discord-message-thumbnails",shown.Length==1?"discord-message-thumbnails-single":"discord-message-thumbnails-pair");
    }

    private sealed record MessageMediaDialog(WidgetNavigationSnapshot<Route> Visit,MessageSelection Selection,
        string MessageId,IReadOnlyList<MediaPlayerGalleryItem> Items);

    private static List<MediaPlayerGalleryItem> MessageMediaItems(MessageLine line)
    {
        var media=new List<MediaPlayerGalleryItem>();
        foreach(var file in line.Message.Attachments)
        {
            if(!line.RevealSpoilers&&file.HasSpoiler)continue;
            var image=file.ContentType is "image/png" or "image/jpeg" or "image/gif" or "image/webp";
            if(file.Url is not {} url || !(image || file.ContentType is "video/mp4" or "video/webm" or "audio/mpeg" or "audio/ogg" or "audio/mp4"))continue;
            var animation=file.HasAnimation;
            media.Add(new("attachment."+file.Id,file.FileName,image?MediaPlayerSource.PublicImage(url):MediaPlayerSource.PublicFile(url),
                new(AutoPlay:animation,Loop:animation,Muted:true){IsAnimation=animation}));
        }
        if(!HideMessageEmbeds(line))
            for(var index=0;index<line.Message.Embeds.Length;index++)
            {
                var embed=line.Message.Embeds[index];
                if(embed.VideoUrl is {} video)
                    media.Add(new("embed."+index,MediaLabel(embed.Title,embed.IsAnimation?"GIF":"Video"),MediaPlayerSource.PublicFile(video),
                        new(AutoPlay:embed.IsAnimation,Loop:embed.IsAnimation,Muted:true){IsAnimation=embed.IsAnimation}));
                else if(embed.ImageUrl is {} image)
                    media.Add(new("embed."+index,MediaLabel(embed.Title,embed.IsAnimation?"GIF":"Image"),MediaPlayerSource.PublicImage(image),
                        embed.IsAnimation?new(AutoPlay:true,Loop:true,Muted:true){IsAnimation=true}:null));
            }
        for(var index=0;index<line.Message.Stickers.Length;index++)
            if(line.Message.Stickers[index] is {ImageUrl:{} image} sticker)
                media.Add(new("sticker."+index,sticker.Name,MediaPlayerSource.PublicImage(image)));
        return media;
    }

    private void OpenMessageMedia(MessageLine line,WidgetActionEvent action)
    {
        if(navigation.Value.Route!=Route.Conversation || messages.Value.Selection is not {} selected || !MessageSelectionCurrent(selected))return;
        var items=MessageMediaItems(line);
        if(items.Count==0 || navigation.PushFromCollectionAction(Route.MessageMedia,action,messageRows.Descriptor)!=WidgetNavigationResult.Changed)return;
        messages.Update(current=>current with {Media=new(navigation.Value,selected,line.Message.Id,items)});
    }

    private WidgetView ApplyMessageMedia(WidgetView result)
    {
        var route=navigation.Value;
        if(route.Route!=Route.MessageMedia)return result;
        const string id="discord.message.gallery";
        var current=messages.Value.Media;
        var available=current is not null && ReferenceEquals(route,current.Visit) && !route.RouteCancellationToken.IsCancellationRequested &&
            MessageSelectionCurrent(current.Selection) && history.Snapshot.Items.Any(item=>item.Id==current.MessageId);
        var content=available
            ?UI.Grid(id+".stage",[GridTrack.Star()],[GridTrack.Star()],
                UI.MediaGallery(current!.Items,id+".media","Message attachments",new(Muted:true))
                    .Classes("discord-message-gallery-media").InGrid()).Classes("discord-message-gallery-stage")
            :(WidgetElement)UI.ReadingSurface(id+".unavailable","Attachments unavailable",ActionSurfaceOrientation.Vertical,
                UI.Text("These attachments are no longer available.",id+".status"));
        return result.WithModal(new(id,"Attachments",content,available?id+".media":id+".unavailable",route.BackActionId!)
        {ScrollContent=false,ShowHeader=false,OverlayPresentation=new()});
    }
}
