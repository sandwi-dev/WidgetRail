using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Runs the installed shipping package, its real worker/Bridge and the native
    // passive pin. Only the provider document is synthetic, in a fresh profile.
    internal void EnableDiscordDirectVideoValidation(string output)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(output))!;
        if (Path.GetFullPath(options.SettingsRoot) != Path.Combine(directory,"profile") ||
            string.Equals(options.SettingsRoot, WidgetRail.PlatformSettings.PlatformSettingsPaths.CreateDefault().RootDirectory,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Direct video validation requires an isolated profile.");
        BrowserSurface.FixturePage = url => url=="https://discord.com/app" ? SyntheticVideoProvider : "<!doctype html><title>Blocked fixture request</title>";
        bool started=false;
        Loaded+=async (_,_)=>{
            if(started)return;started=true;
            var step="startup";string? error=null;int first=0,last=0,drops=0;
            try
            {
                if(startup is not null)await startup;
                await SelectAsync("widgetrail.samples.discord-controls",true);App.Window!.Activate();
                step="focus Watch";
                await WebAppUntil(()=>MainFocusEnabled&&surface?.FocusHostTarget("discord.stream.2")==true);
                await WebAppUntil(()=>FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused &&
                    AutomationProperties.GetAutomationId(focused)=="Widget.discord.stream.2");
                step="Watch opens passive stream pin";
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await WebAppUntil(()=>pinned?.LayoutId=="discord.stream" && Slots(pinned.Presenter).Any(slot=>slot.Surface?.IsBackendVideo==true));
                var view=Slots(pinned!.Presenter).Single(slot=>slot.Surface?.IsBackendVideo==true).Surface!;
                step="direct video playing at full size";
                await WebAppUntilScript(view,"document.querySelector('video')?.videoWidth===1280 && document.querySelector('video').videoHeight===720");
                var loadingBar = view.Children.OfType<Microsoft.UI.Xaml.Controls.ProgressBar>().Single();
                var videoTop = view.CompositionRoot.TransformToVisual(view).TransformPoint(new(0,0)).Y;
                if (loadingBar.Visibility != Visibility.Collapsed || loadingBar.IsIndeterminate || Math.Abs(videoTop) > 0.01)
                    throw new InvalidOperationException($"Playing video retained navigation chrome: bar={loadingBar.Visibility}, animated={loadingBar.IsIndeterminate}, top={videoTop}.");
                // Show Discord's own view beside the pin; verify the same track
                // object at both video elements, rather than relying on codec guesses.
                step="open original Discord page";
                await WebAppUntil(()=>surface?.FocusHostTarget("discord.account.open")==true);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await WebAppUntil(()=>Slots(surface).Any(slot=>slot.Surface?.IsBackendSession==true));
                var source=Slots(surface).Single(slot=>slot.Surface?.IsBackendSession==true).Surface!;
                await WebAppUntilScript(view,"document.querySelector('video').srcObject.getVideoTracks()[0]===opener.fixtureOriginal.srcObject.getVideoTracks()[0]");
                step="measure native side-by-side playback";
                const string quality="document.querySelector('video').getVideoPlaybackQuality()";
                first=int.Parse(await view.EvaluateFixtureAsync(quality+".totalVideoFrames"));
                var dropsBefore=int.Parse(await view.EvaluateFixtureAsync(quality+".droppedVideoFrames"));
                await Task.Delay(3000);
                last=int.Parse(await view.EvaluateFixtureAsync(quality+".totalVideoFrames"));
                drops=int.Parse(await view.EvaluateFixtureAsync(quality+".droppedVideoFrames"))-dropsBefore;
                if(last-first<60||drops>(last-first)*.05)throw new InvalidOperationException("Passive pin cadence regression.");
                step="unpin preserves original playback";
                await UnpinAsync(save:false);
                await WebAppUntil(()=>!source.HasActiveVideoOutput);
                await WebAppUntilScript(source,"fixtureOriginal.srcObject.getVideoTracks()[0].readyState==='live' && !fixtureOriginal.paused");
            }
            catch(Exception failure){error=failure.ToString();}
            finally{
                BrowserSurface.FixturePage=null;
                await File.WriteAllTextAsync(output+".tmp",JsonSerializer.Serialize(new{passed=error is null,step,first,last,drops,error}));
                File.Move(output+".tmp",output,true);
            }
        };
    }

    private const string SyntheticVideoProvider="""
        <!doctype html><title>Generated Discord stream fixture</title><video muted autoplay id="original"></video><script>
        const canvas=document.createElement('canvas');canvas.width=1280;canvas.height=720;
        const draw=canvas.getContext('2d');let n=0;function paint(){
          draw.fillStyle='#182438';draw.fillRect(0,0,1280,720);draw.fillStyle='white';draw.font='18px sans-serif';
          for(let j=0;j<25;j++)draw.fillText('Moving synthetic screen • 0123456789 '.repeat(5),(n%80)-80,j*27+24);n++;
        } paint();setInterval(paint,33);
        const track=canvas.captureStream(30).getVideoTracks()[0];window.fixtureOriginal=document.querySelector('video');
        fixtureOriginal.srcObject=new MediaStream([track]);fixtureOriginal.play();
        const connection={context:'stream',streamUserId:'2',peer:{getReceivers:()=>[{track}]}};
        const manager={incomingVideoEnabled:true,getUserID:()=>'2',onIncomingVideoEnabled(v){this.incomingVideoEnabled=v;}};
        const rtc={context:'stream',streamKey:'guild:10:20:2',_destroyed:false,isOwner:false,streamContext:{ownerId:'2',channelId:'20'},_connection:connection,_goLiveQualityManager:manager};
        const engine={connections:[connection]};
        const stores={UserStore:{getCurrentUser:()=>({id:'1'}),getUser:()=>({username:'Generated sender'})},
          SelectedChannelStore:{getVoiceChannelId:()=>'20'},ChannelStore:{getChannel:()=>({id:'20',guild_id:'10',name:'Generated call'})},
          VoiceStateStore:{getVoiceStatesForChannel:()=>({peer:{userId:'2',selfStream:true}})},
          ApplicationStreamingStore:{getViewerIds:()=>['1']},StreamRTCConnectionStore:{getRTCConnection:()=>rtc},
          MediaEngineStore:{getMediaEngine:()=>engine},RTCConnectionStore:{getState:()=>'RTC_CONNECTED',getChannelId:()=>'20'}};
        const modules={};for(const [name,store] of Object.entries(stores))modules[name]={exports:{...store,getName:()=>name}};
        modules.visibility={exports:{isIncomingVideoEnabled:()=>true,lastIncomingVideoEnabledChangeTime:()=>0}};
        window.webpackChunkdiscord_app=[];
        webpackChunkdiscord_app.push=function(entry){entry[2]({c:modules});return Array.prototype.push.call(this,entry);};
        </script>
        """;
}
