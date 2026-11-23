using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task IndexedInlineVideoAsync(string output, string pipe, string clip)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var session = await PresentationSession.ConnectAsync(pipe,cancellationToken:deadline.Token);
        await session.ListWidgetsAsync(deadline.Token);
        var frame = await session.EstablishPresentationAsync(session.GetTarget("animation-fixture"),WidgetLifecycleState.Interactive,deadline.Token);
        var page = new WidgetViewPresenter { Width=620,Height=360,Session=session };
        var root = new Grid { Width=620,Height=360 }; root.Children.Add(page); host.Children.Add(root);
        PresentationLayers.Attach(root);
        var fullscreen = new MediaFullscreenLayer(); fullscreen.Attach(root);
        // The shell hides the normal widget area after entering fullscreen.
        // Keep that lifecycle boundary in this indexed chat fixture.
        fullscreen.Changed+=()=>
        {
            page.IsEnabled=!fullscreen.IsOpen;
            page.Visibility=fullscreen.IsOpen&&!fullscreen.IsTransitioning?Visibility.Collapsed:Visibility.Visible;
        };
        var theme = new NativePopupTheme(); theme.Attach(page);
        theme.Update(frame.RenderStyles,AppearanceSettings.Default);
        MediaPlayerView.ValidationVideoSource=uri=>uri.AbsoluteUri=="https://example.invalid/animation.mp4"?new Uri(clip):uri;
        Exception? failure=null;
        page.Failed=error=>failure=error;
        var trace=new List<string>();page.FocusTrace=value=>{if(trace.Count<300)trace.Add(value);};
        try
        {
            page.ApplyAppearance(AppearanceSettings.Default,false);page.Apply(frame);
            await Until(()=>Walk(page).OfType<MediaPlayerView>().Any(media=>media.IsLoaded));
            var media=Walk(page).OfType<MediaPlayerView>().Single();media.Width=480;media.Height=260;
            var list=Walk(page).OfType<WidgetIndexedCollectionView>().Single();
            var scroll=Walk(list.NativeView).OfType<ScrollViewer>().First();
            Check(page.FocusHostTarget("animations"),"Native entry focuses the message collection");
            await Until(()=>list.CaptureFocusedItem()?.ItemKey=="gif"&&!list.HasPendingNativeNavigation);
            Check(media.IsInlinePoster&&MediaPlayerView.ResidentPlayers==0,"A real indexed body stays poster-only before activation");
            await Button(ControllerButton.A);
            await Until(()=>list.FocusedReadingAction?.MediaPlayer?.Presentation==MediaPlayerPresentation.Inline);
            Check(!media.IsPlaying&&page.CaptureControllerGuide().Any(hint=>hint.Button==ControllerButton.A&&hint.Label=="Play video"),
                "First A enters the native player and advertises Play without starting it");
            Check(page.CaptureHeldAction(ControllerButton.LeftTrigger) is null,
                "A poster has no phantom held-seek action before playback exists");
            await Button(ControllerButton.A);await Until(()=>media.IsPlaying);
            Check(media.PreviewVideoForValidation.IsLoaded&&MediaPlayerView.ResidentPlayers==1,
                "Second A plays real decoded video through the exact indexed body lease");
            Check(page.CaptureControllerGuide().Any(hint=>hint.Button==ControllerButton.LeftTrigger)&&page.CaptureHeldAction(ControllerButton.LeftTrigger) is not null,
                "Inline controller hints and held-seek routing come from the shared player");
            if (Environment.GetCommandLineArgs().Contains("--inline-video-inspection"))
            {
                await File.WriteAllTextAsync(output+".ready",JsonSerializer.Serialize(new{processId=Environment.ProcessId}));
                var inspectionDeadline=Environment.TickCount64+45000;
                while(!File.Exists(output+".inspected"))
                {if(Environment.TickCount64>inspectionDeadline)throw new TimeoutException("Inline visual inspection was not acknowledged.");await Task.Delay(100,deadline.Token);}
            }
            await Button(ControllerButton.X);await Until(()=>!media.IsPlaying);
            await Button(ControllerButton.X);await Until(()=>media.IsPlaying);
            Check(true,"X reaches the nested player's pause and resume controls");
            await Button(ControllerButton.RightStick);
            await Until(()=>media.IsExpanded&&!fullscreen.IsTransitioning);
            await Task.Delay(400,deadline.Token);
            Check(media.IsPlaying && !media.IsInlinePoster,"Initial fullscreen playback survives hiding the chat underneath");
            fullscreen.Route(ControllerButton.B,ControllerEventPhase.Pressed);fullscreen.Route(ControllerButton.B,ControllerEventPhase.Released);
            await Until(()=>!media.IsExpanded&&!fullscreen.IsTransitioning&&list.FocusedReadingAction?.MediaPlayer is not null);
            await Button(ControllerButton.B);await Until(()=>list.FocusedReadingAction is null);
            await Until(()=>scroll.ViewportHeight>0&&scroll.ScrollableHeight>scroll.ViewportHeight);
            for(var i=0;i<60&&!OffscreenAndPaused();i++){page.ScrollBy(0,45);await Task.Delay(20,deadline.Token);}
            await Until(OffscreenAndPaused);
            trace.Add($"scrolled away: offset={scroll.VerticalOffset}, extent={scroll.ScrollableHeight}, viewport={scroll.ViewportHeight}, playing={media.IsPlaying}");
            for(var i=0;i<60&&!PosterInViewport();i++){page.ScrollBy(0,-45);await Task.Delay(20,deadline.Token);}
            trace.Add($"scrolled back: offset={scroll.VerticalOffset}, playing={media.IsPlaying}");
            await Until(PosterInViewport);
            media=Walk(list).OfType<MediaPlayerView>().Single(view=>view.IsLoaded&&view.IsInlinePoster);
            media.Width=160;media.Height=260;
            if(!page.FocusHostTarget("animations"))throw new InvalidOperationException("Message row did not accept entry after scrolling");
            await Button(ControllerButton.A);
            await Until(()=>list.FocusedReadingAction?.MediaPlayer?.Presentation==MediaPlayerPresentation.Inline);
            await Until(()=>FocusManager.GetFocusedElement(root.XamlRoot) is Button play &&
                AutomationProperties.GetAutomationId(play)=="MediaPlayer.Inline.Play" &&
                play.FocusState==FocusState.Keyboard && play.UseSystemFocusVisuals && VisiblePlay(play));
            Check(true,"Returning to the inline video focuses its visible Play button before the next A");
            await CaptureEditorBitmapAsync(root,output+".returned-poster.png");
            await Button(ControllerButton.A);
            await Until(()=>media.IsPlaying);
            var decoder=media.PlaybackPlayerForValidation as Windows.Media.Playback.MediaPlayer??throw new InvalidOperationException("Missing resumed decoder");
            media.PreviewVolumeForValidation.Value=35;
            var positionBefore=decoder.PlaybackSession.Position;
            await Until(()=>decoder.PlaybackSession.Position>positionBefore+TimeSpan.FromMilliseconds(70));
            await Button(ControllerButton.RightStick);await Until(()=>media.IsExpanded&&!fullscreen.IsTransitioning);
            await Task.Delay(500,deadline.Token);
            Check(media.IsPlaying && !media.IsInlinePoster && ReferenceEquals(decoder,media.PlaybackPlayerForValidation) &&
                page.Visibility==Visibility.Collapsed && decoder.PlaybackSession.NaturalVideoWidth>0 &&
                !media.PreviewMutedForValidation && Math.Abs(media.PreviewVolumeForValidation.Value-35)<.01 &&
                !Walk(fullscreen).OfType<Button>().Any(button=>AutomationProperties.GetAutomationId(button)=="MediaPlayer.Inline.Play" && button.Visibility==Visibility.Visible),
                "Resumed inline video keeps its decoder and audio instead of showing the inline Play poster in fullscreen");
            var positionInFullscreen=decoder.PlaybackSession.Position;
            await Until(()=>decoder.PlaybackSession.Position!=positionInFullscreen);
            if(Environment.GetCommandLineArgs().Contains("--inline-probe-green"))
            {
                await Until(()=>
                {
                    var pixel=OverlayMouseValidationPage.Pixel(media.PreviewVideoForValidation);
                    return (pixel&255)<65 && (pixel>>8&255)>180 && (pixel>>16&255)<65;
                });
                Check(true,"The fullscreen display shows actual green pixels from the resumed generated clip");
            }
            await CaptureEditorBitmapAsync(root,output+".fullscreen.png");
            fullscreen.Route(ControllerButton.B,ControllerEventPhase.Pressed);fullscreen.Route(ControllerButton.B,ControllerEventPhase.Released);
            await Until(()=>!media.IsExpanded&&!fullscreen.IsTransitioning&&list.FocusedReadingAction?.MediaPlayer is not null);
            Check(media.IsPlaying,"Fullscreen Back returns to the exact inline player");
            await Button(ControllerButton.X);await Until(()=>!media.IsPlaying);
            var pausedPosition=decoder.PlaybackSession.Position;
            await Button(ControllerButton.RightStick);await Until(()=>media.IsExpanded&&!fullscreen.IsTransitioning);
            await Task.Delay(350,deadline.Token);
            Check(ReferenceEquals(decoder,media.PlaybackPlayerForValidation) && !media.IsPlaying && !media.IsInlinePoster &&
                Math.Abs((decoder.PlaybackSession.Position-pausedPosition).TotalSeconds)<.1,
                "Entering fullscreen while paused preserves the decoded frame and playhead");
            fullscreen.Route(ControllerButton.B,ControllerEventPhase.Pressed);fullscreen.Route(ControllerButton.B,ControllerEventPhase.Released);
            await Until(()=>!media.IsExpanded&&!fullscreen.IsTransitioning&&list.FocusedReadingAction?.MediaPlayer is not null);
            Check(!media.IsPlaying && !media.IsInlinePoster && !media.PreviewMutedForValidation &&
                Math.Abs((decoder.PlaybackSession.Position-pausedPosition).TotalSeconds)<.1,
                "Leaving fullscreen keeps pause, position and audio settings with the same message action");
            await CaptureEditorBitmapAsync(root,output+".returned.png");
            await Button(ControllerButton.B);await Until(()=>list.FocusedReadingAction is null);
            Check(list.CaptureFocusedItem()?.ItemKey=="gif","Inline Back returns to its message instead of leaving the chat");
            await page.SetPresentationActiveAsync(false);await Until(()=>MediaPlayerView.ResidentPlayers==0);
            Check(!media.IsPlaying,"Suspending the indexed presentation stops inline video and audio");
            await page.SetPresentationActiveAsync(true);page.Apply(frame);await Task.Delay(250);
            Check(MediaPlayerView.ResidentPlayers==0,"Reopening the indexed presentation requires another explicit Play");

            bool OffscreenAndPaused()=>!media.IsPlaying && (!media.IsLoaded ||
                media.TransformToVisual(scroll).TransformBounds(new(0,0,media.ActualWidth,media.ActualHeight)).Bottom<=0);
            bool PosterInViewport()=>Walk(list).OfType<Button>().Any(button=>button.IsLoaded && button.Visibility==Visibility.Visible &&
                AutomationProperties.GetAutomationId(button)=="MediaPlayer.Inline.Play" && VisiblePlay(button));
            bool VisiblePlay(Button play)
            {
                var bounds=play.TransformToVisual(scroll).TransformBounds(new(0,0,play.ActualWidth,play.ActualHeight));
                return bounds.Width>=40 && bounds.Height>=40 && bounds.Left>=-1 && bounds.Top>=-1 &&
                    bounds.Right<=scroll.ActualWidth+1 && bounds.Bottom<=scroll.ActualHeight+1;
            }
        }
        catch(Exception error){failure=error;}
        finally
        {
            await page.DisposeAsync();host.Children.Remove(root);MediaPlayerView.ValidationVideoSource=null;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=failure is null,checks,trace,error=failure?.ToString(),residentPlayers=MediaPlayerView.ResidentPlayers}));
        }
        if(failure is not null)throw failure;
        async Task Button(ControllerButton button){await page.HandleControllerButtonAsync(button);await page.HandleControllerButtonAsync(button,ControllerEventPhase.Released);}
        async Task Until(Func<bool> condition,[System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))]string? description=null)
        {await Wait(()=>failure is not null||condition(),description);if(failure is not null)throw failure;}
        static IEnumerable<DependencyObject> Walk(DependencyObject node)
        {yield return node;for(var index=0;index<VisualTreeHelper.GetChildrenCount(node);index++)foreach(var child in Walk(VisualTreeHelper.GetChild(node,index)))yield return child;}
    }
}
