using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task InlineVideoAsync(string output, string clip)
    {
        var colorProbe=Environment.GetCommandLineArgs().Contains("--inline-color-frame-probe");
        var timings=new Dictionary<string,double>();
        if(colorProbe)
        {
            var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(Path.GetFullPath(output))!);
            var file=await folder.CreateFileAsync("generated-green.mp4",Windows.Storage.CreationCollisionOption.ReplaceExisting);
            var composition=new Windows.Media.Editing.MediaComposition();
            composition.Clips.Add(Windows.Media.Editing.MediaClip.CreateFromColor(Microsoft.UI.Colors.Lime,TimeSpan.FromSeconds(12)));
            var profile=Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(Windows.Media.MediaProperties.VideoEncodingQuality.Vga);
            if(await composition.RenderToFileAsync(file,Windows.Media.Editing.MediaTrimmingPreference.Precise,profile)!=Windows.Media.Transcoding.TranscodeFailureReason.None)
                throw new InvalidOperationException("Failed to encode the generated video color probe.");
            clip=file.Path;
        }
        var panel = new StackPanel();
        var scroll = new ScrollViewer { Width = 640, Height = 300, Content = panel };
        var video = new MediaPlayerView { Width = 480, Height = 260, AudioPreferenceWidgetId = "inline.fixture" };
        var outside=new Button{Content="Other control"};
        panel.Children.Add(video); panel.Children.Add(new Border { Height = 800 }); host.Children.Add(scroll);host.Children.Add(outside);
        var settings = ShellPreferences.Empty;
        new NativeMediaAudioPreferences(id => settings.MediaAudio.GetValueOrDefault(id),
            (id, value) => settings = settings.RememberAudio(id, value)).Attach(scroll);
        var definition = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(clip), new())
            { Presentation = MediaPlayerPresentation.Inline };
        var baseline = MediaPlayerView.ResidentPlayers;
        var reads = 0;
        var current = true;
        TaskCompletionSource<HostMediaPlayerSource>? held = null;
        Exception? failure = null;
        try
        {
            video.SetActive(true, true); Apply();
            await Wait(() => video.IsLoaded && video.IsInlinePoster && video.ActualHeight > 0);
            await Task.Delay(250);
            Check(reads == 0 && MediaPlayerView.ResidentPlayers == baseline, "Inline poster acquires no source or decoder before Play");
            Check(video.Focus(FocusState.Keyboard), "Native inline player is controller focusable before source acquisition");
            var started=System.Diagnostics.Stopwatch.StartNew();
            Check(video.Handle(ControllerButton.A, ControllerEventPhase.Pressed), "A explicitly starts inline video");
            video.Handle(ControllerButton.A, ControllerEventPhase.Released);
            await Wait(() => video.IsPlaying);
            if(colorProbe)await RecordFrame("cold",started);
            Check(reads == 1 && MediaPlayerView.ResidentPlayers == baseline + 1, "Explicit playback creates exactly one native decoder");
            Check(video.PreviewMutedForValidation, "First inline playback uses the widget's muted default");
            video.PreviewVolumeForValidation.Value = 30;
            Check(!video.PreviewMutedForValidation && settings.MediaAudio["inline.fixture"] == new MediaAudioPreference(false,.3),
                "Inline volume/unmute uses the existing per-widget audio preference");
            Check(video.PreviewVolumeForValidation.Focus(FocusState.Keyboard),"The active transport can remember volume focus");
            Check(outside.Focus(FocusState.Keyboard),"Focus leaves the video before scrolling away");
            await Wait(()=>ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(video.XamlRoot),outside));
            for(var tick=0;tick<30 && video.TransformToVisual(scroll).TransformPoint(new(0,video.ActualHeight)).Y>0;tick++)
            {
                scroll.ChangeView(null,Math.Min(scroll.ScrollableHeight,scroll.VerticalOffset+64),null,true);
                await Task.Delay(32);
            }
            await Wait(() => video.TransformToVisual(scroll).TransformPoint(new(0,video.ActualHeight)).Y<=0 &&
                video.IsInlinePoster && MediaPlayerView.ResidentPlayers == baseline);
            Check(!video.IsPlaying, "Scrolling inline video offscreen stops audio and releases its decoder");
            video.SetPoster(new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(8,8));
            Check(video.HasPosterForValidation,"A late current poster can fill the idle row after playback suspension");
            // Return in scroll ticks, as a held stick does. The late poster can
            // change the native scroll anchor during the first layout update.
            // Start testing entry only once the visible button is fully in view.
            var visiblePlay=(Button)Descendants(video).OfType<Button>().Single(b=>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b)=="MediaPlayer.Inline.Play");
            for(var tick=0;tick<30;tick++)
            {
                var top=visiblePlay.TransformToVisual(scroll).TransformPoint(new()).Y;
                if(top>=0 && top+visiblePlay.ActualHeight<=scroll.ViewportHeight)break;
                scroll.ChangeView(null,Math.Max(0,scroll.VerticalOffset-64),null,true);
                await Task.Delay(32);
            }
            var shownTop=visiblePlay.TransformToVisual(scroll).TransformPoint(new()).Y;
            Check(shownTop>=0 && shownTop+visiblePlay.ActualHeight<=scroll.ViewportHeight,
                "The poster Play button is visible again before controller entry");
            Check(reads == 1 && !video.IsPlaying, "Returning onscreen does not automatically resume video");
            Check(video.Focus(FocusState.Keyboard),"The returned inline poster accepts a second entry");
            await Wait(()=>Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(video.XamlRoot) is Button {FocusState:FocusState.Keyboard, UseSystemFocusVisuals:true} target &&
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(target)=="MediaPlayer.Inline.Play","settled visible poster Play focus");
            if(colorProbe)await CaptureEditorBitmapAsync(video,Path.Combine(Path.GetDirectoryName(output)!,"return-focus.png"));
            Check(true,"Reentering a scrolled poster places actual keyboard focus on its visible Play button");
            started.Restart();video.Toggle(); await Wait(() => video.IsPlaying);
            if(colorProbe)await RecordFrame("return",started);
            Check(reads == 2 && !video.PreviewMutedForValidation, "Explicit resume reacquires video and remembers widget audio");
            video.SetActive(false, false);
            Check(!video.IsPlaying && MediaPlayerView.ResidentPlayers == baseline, "Leaving the chat releases inline playback synchronously");
            video.SetActive(true, true); Apply(); await Task.Delay(250);
            Check(reads == 2 && video.IsInlinePoster, "Returning to a cached chat remains paused and source-free");
            held = new(TaskCreationOptions.RunContinuationsAsynchronously);
            video.Toggle(); await Wait(() => reads == 3);
            video.SetActive(false, false);
            held.SetResult(Source()); await Task.Delay(250);
            Check(MediaPlayerView.ResidentPlayers == baseline && !video.IsPlaying,
                "Late acquisition after departure cannot create a decoder or start audio");
            held = null; video.SetActive(true, true); Apply();
            video.Toggle(); await Wait(() => video.IsPlaying);
            for(var cycle=0;cycle<12;cycle++)
            {
                video.SetActive(false,false);await video.PlaybackRetirement;
                Check(MediaPlayerView.ResidentPlayers==baseline,$"Inline hide cycle {cycle+1} releases its decoder and source");
                video.SetActive(true,true);Apply();video.Toggle();await Wait(()=>video.IsPlaying);
            }
            current = false;
            await Wait(() => MediaPlayerView.ResidentPlayers == baseline);
            Check(!video.IsPlaying, "Retiring source authority stops inline playback");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            held?.TrySetResult(Source());
            video.Dispose(); await video.PlaybackRetirement; host.Children.Remove(scroll);host.Children.Remove(outside);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new { passed=failure is null, checks, reads,timings,
                residentPlayers=MediaPlayerView.ResidentPlayers, error=failure?.ToString() }));
        }
        if (failure is not null) throw failure;
        async Task RecordFrame(string phase,System.Diagnostics.Stopwatch clock)
        {
            await Wait(()=>{var pixel=OverlayMouseValidationPage.Pixel(video.PreviewVideoForValidation);return
                (pixel&255)<65 && (pixel>>8&255)>180 && (pixel>>16&255)<65;},"first generated green video frame");
            timings[phase+"FirstFrameMilliseconds"]=clock.Elapsed.TotalMilliseconds;
        }
        HostMediaPlayerSource Source() => new(definition,new Uri(clip).AbsoluteUri);
        void Apply() => video.Apply(_ => { ++reads; return held?.Task ?? Task.FromResult(Source()); }, definition, () => current, "inline.owner");
    }
}
