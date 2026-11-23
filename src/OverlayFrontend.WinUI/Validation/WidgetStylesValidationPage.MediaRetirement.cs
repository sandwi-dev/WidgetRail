using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task MediaRetirementAsync(string output,string clip)
    {
        var baseline=MediaPlayerView.ResidentPlayers;
        var video=new MediaPlayerView{Width=480,Height=270};
        host.Children.Add(video);
        var definition=new MediaPlayerDefinition(MediaPlayerSource.LocalFile(clip),new()){Presentation=MediaPlayerPresentation.Inline};
        var source=new HostMediaPlayerSource(definition,new Uri(clip).AbsoluteUri);
        TextBlock? status=null;
        var pending=new TaskCompletionSource<HostMediaPlayerSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current=true;
        var frames=0;var transientErrors=0;Exception? failure=null;
        void Sample(object? sender,object args)
        {
            ++frames;
            if(status?.Visibility==Visibility.Visible && status.Text.Contains("could not be opened",StringComparison.Ordinal))++transientErrors;
        }
        CompositionTarget.Rendering+=Sample;
        try
        {
            await Wait(()=>video.IsLoaded && video.ActualHeight>0);
            status=Descendants(video).OfType<TextBlock>().Single(t=>AutomationProperties.GetAutomationId(t)=="MediaPlayer.Status");
            var reads=0;
            video.SetActive(true,true);
            video.Apply(_=>{reads++;return pending.Task;},definition,()=>current,"delayed-row");
            video.Toggle();await Wait(()=>reads==1);
            current=false;
            pending.SetException(new InvalidOperationException("The generated row lease ended."));
            // The outgoing row can remain in the tree until the new section is painted.
            var retiredAt=frames;await Wait(()=>frames>=retiredAt+5);
            Check(transientErrors==0,"Retired in-flight resolution paints no media failure on outgoing frames");
            Check(MediaPlayerView.ResidentPlayers==baseline,"The superseded acquisition leaves no decoder");

            current=true;
            video.Apply(_=>Task.FromResult(source),definition,()=>current,"delayed-row");
            Check(video.IsInlinePoster,"Rebinding the same declaration to a current row restores its poster");
            video.Toggle();await Wait(()=>video.IsPlaying);
            current=false;
            await Wait(()=>MediaPlayerView.ResidentPlayers==baseline);
            retiredAt=frames;await Wait(()=>frames>=retiredAt+5);
            Check(transientErrors==0 && !video.IsPlaying,"Visible retiring playback releases its decoder without a format error frame");
            CompositionTarget.Rendering-=Sample;

            current=true;
            var badPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"bad-format.mp4");
            await File.WriteAllBytesAsync(badPath,"Generated invalid media"u8.ToArray());
            var invalid=definition with{Source=MediaPlayerSource.LocalFile(badPath)};
            video.Apply(_=>Task.FromResult(new HostMediaPlayerSource(invalid,new Uri(badPath).AbsoluteUri)),invalid,()=>current,"invalid-row");
            video.Toggle();
            await Wait(()=>status.Visibility==Visibility.Visible && status.Text.Contains("could not be opened",StringComparison.Ordinal));
            Check(MediaPlayerView.ResidentPlayers==baseline,"A current undecodable file still presents its error and releases the decoder");
        }
        catch(Exception error){failure=error;}
        finally
        {
            CompositionTarget.Rendering-=Sample;pending.TrySetCanceled();video.Dispose();await video.PlaybackRetirement;host.Children.Remove(video);
            await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=failure is null,frames,transientErrors,checks,error=failure?.ToString()}));
        }
        if(failure is not null)throw failure;
    }
}
