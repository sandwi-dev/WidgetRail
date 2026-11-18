using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    internal async Task ValidateRecordingCodecPlaybackAsync(string file,int width,int height,string output,List<string> checks)
    {
        if(width<2||height<2||width>8192||height>8192||(long)width*height>CaptureLimits.MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(width));
        var baseline=ResidentPlayers;
        var description=new MediaPlayerDefinition(MediaPlayerSource.LocalFile(file),new(AutoPlay:true,Muted:true,Loop:true));
        SetActive(true,true);
        Apply(_=>Task.FromResult(new HostMediaPlayerSource(description,new Uri(file).AbsoluteUri,
            Width:width,Height:height,ExpiresAtUnixMilliseconds:long.MaxValue)),description,()=>true,new object());
        await Wait(()=>IsPlaying&&player?.PlaybackSession.NaturalDuration.TotalSeconds>0&&videoStage is not null);
        var current=player!;
        Check(current.PlaybackSession.NaturalVideoWidth==width&&current.PlaybackSession.NaturalVideoHeight==height,
            "Native player reports the exact visible recording dimensions");
        Check(ResidentPlayers==baseline+1,"Playback acquires one native decoder");
        video.Focus(FocusState.Keyboard);videoChrome.Hide();
        await Pixels("playing");
        videoStage!.SetCovered(true);
        await Pixels("covered");
        videoStage.SetCovered(false);
        await Pixels("restored");
        var fullscreen=ToggleExpandedAsync();
        await Wait(()=>IsExpanded&&!changingPlacement&&expandedHost is {IsTransitioning:false});
        Check(ReferenceEquals(player,current)&&IsPlaying,"Fullscreen retains the same playing codec decoder");
        await Pixels("fullscreen");
        Handle(ControllerButton.B,ControllerEventPhase.Pressed);Handle(ControllerButton.B,ControllerEventPhase.Released);
        await fullscreen;
        await Wait(()=>!IsExpanded&&!changingPlacement);
        Check(ReferenceEquals(player,current)&&IsPlaying,"Fullscreen return preserves codec playback");
        SetActive(false,false);await PlaybackRetirement;
        Check(ResidentPlayers==baseline&&player is null,"Retirement releases the codec decoder");

        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks.Add(label);}
        async Task Wait(Func<bool> ready)
        {
            var deadline=Environment.TickCount64+15000;
            while(!ready())
            {
                if(disposed||Environment.TickCount64>deadline)throw new InvalidOperationException("Native codec player did not settle: "+status.Text);
                await Task.Delay(25);
            }
        }
        async Task Pixels(string phase)
        {
            await Frame();await Frame();
            if(videoStage is null)throw new InvalidOperationException("Codec video plane is missing.");
            var bounds=PreviewVideoBoundsForValidation;
            var factor=XamlRoot.RasterizationScale;
            var ancestry=new List<object>();
            for(DependencyObject? item=this;item is UIElement element;item=VisualTreeHelper.GetParent(item))
            {
                var visual=Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
                var frame=element as FrameworkElement;
                ancestry.Add(new{type=element.GetType().Name,name=frame?.Name,visibility=element.Visibility.ToString(),
                    element.Opacity,loaded=frame?.IsLoaded,width=frame?.ActualWidth,height=frame?.ActualHeight,
                    compositionOpacity=visual.Opacity,compositionScale=new{visual.Scale.X,visual.Scale.Y},
                    compositionSize=new{visual.Size.X,visual.Size.Y}});
            }
            var phasePath=Path.Combine(Path.GetDirectoryName(output)!,phase+".phase.json");
            await File.WriteAllTextAsync(phasePath+".pending",JsonSerializer.Serialize(new{phase,processId=Environment.ProcessId,
                window=WinRT.Interop.WindowNative.GetWindowHandle(App.Window).ToInt64(),
                x=bounds.X*factor,y=bounds.Y*factor,width=bounds.Width*factor,height=bounds.Height*factor,
                hostVisible=XamlRoot.IsHostVisible,ancestry}));
            File.Move(phasePath+".pending",phasePath);
            await Wait(()=>File.Exists(phasePath+".captured"));
            using var ack=JsonDocument.Parse(await File.ReadAllTextAsync(phasePath+".captured"));
            Check(ack.RootElement.GetProperty("passed").GetBoolean(),"Actual codec pixels: "+phase);
        }
        static async Task Frame()
        {
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<object>? rendered=null;
            rendered=(_,_)=>ready.TrySetResult();
            CompositionTarget.Rendering+=rendered;
            try{await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));}
            finally{CompositionTarget.Rendering-=rendered;}
        }
    }
}
