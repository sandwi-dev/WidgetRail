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
            var step="startup";string? error=null;int first=0,last=0,drops=0,secondFirst=0,secondLast=0,thirdFirst=0,thirdLast=0,thirdReplacements=0,pinChoiceChecks=0;
            async Task ConfirmStreamChoices(params string[] expected)
            {
                await WebAppUntil(()=>owner?.Session.GetState("widgetrail.samples.discord-controls")?.LastGood is { } frame&&
                    GetPinChoices(frame.Descriptor).Where(choice=>choice.LayoutId.StartsWith("discord.stream",StringComparison.Ordinal))
                        .Select(choice=>choice.LayoutId).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)));
                pinChoiceChecks++;
            }
            try
            {
                if(startup is not null)await startup;
                await SelectAsync("widgetrail.samples.discord-controls",true);App.Window!.Activate();
                step="focus Watch";
                await WebAppUntil(()=>MainFocusEnabled&&surface?.FocusHostTarget("discord.stream.2")==true);
                await WebAppUntil(()=>FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused &&
                    AutomationProperties.GetAutomationId(focused)=="Widget.discord.stream.2");
                await ConfirmStreamChoices();
                step="Watch opens passive stream pin";
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await WebAppUntil(()=>pinned?.LayoutId=="discord.stream" && Slots(pinned.Presenter).Any(slot=>slot.Surface?.IsBackendVideo==true));
                var view=Slots(pinned!.Presenter).Single(slot=>slot.Surface?.IsBackendVideo==true).Surface!;
                step="direct video playing at full size";
                await WebAppUntilScript(view,"document.querySelector('video')?.videoWidth===1280 && document.querySelector('video').videoHeight===720");
                await WebAppUntilScript(view,"getComputedStyle(document.documentElement).backgroundColor==='rgba(0, 0, 0, 0)' && getComputedStyle(document.body).backgroundColor==='rgba(0, 0, 0, 0)'");
                await WebAppUntil(()=>pinned!.Window.SurfaceBackground.Background is Microsoft.UI.Xaml.Media.SolidColorBrush {Color.A:0});
                var viewport=view.Children.OfType<Microsoft.UI.Xaml.Controls.Panel>().Single(panel=>
                    panel.Children.OfType<FrameworkElement>().Any(child=>AutomationProperties.GetAutomationId(child)=="Browser.WebView"));
                if(viewport.Background is not null)throw new InvalidOperationException("Native video viewport still paints a fill.");
                // Theme callbacks run again on reparent and palette changes.
                var contrastBrush=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Magenta);
                view.ApplyTheme(contrastBrush,contrastBrush,contrastBrush,contrastBrush,contrastBrush,16,
                    Microsoft.UI.Text.FontWeights.Normal,null,12);
                if(viewport.Background is not null)throw new InvalidOperationException("Theme update restored the video viewport fill.");
                var loadingBar = view.Children.OfType<Microsoft.UI.Xaml.Controls.ProgressBar>().Single();
                if(pinned!.Window.NativeInputEnabled || view.CompositionRoot is not Microsoft.UI.Xaml.Controls.WebView2 {IsTabStop:false,IsHitTestVisible:false} || view.CompositionRoot.Opacity!=1)
                    throw new InvalidOperationException("Passive receiver must disable native input without fading its content.");
                var videoTop = view.CompositionRoot.TransformToVisual(view).TransformPoint(new(0,0)).Y;
                if (loadingBar.Visibility != Visibility.Collapsed || loadingBar.IsIndeterminate || Math.Abs(videoTop) > 0.01)
                    throw new InvalidOperationException($"Playing video retained navigation chrome: bar={loadingBar.Visibility}, animated={loadingBar.IsIndeterminate}, top={videoTop}.");
                var firstPin=pins.Single(pin=>pin.LayoutId=="discord.stream");
                await ConfirmStreamChoices("discord.stream");
                await WebAppUntil(()=>BrowserHostFillsSlot(Slots(firstPin.Presenter).Single(slot=>slot.Surface==view)));
                step="second independent screen";
                await WebAppUntil(()=>surface?.FocusHostTarget("discord.stream.3")==true);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await WebAppUntil(()=>pins.Any(pin=>pin.LayoutId=="discord.stream.2" &&
                    Slots(pin.Presenter).Any(slot=>slot.Surface?.IsBackendVideo==true)));
                var secondPin=pins.Single(pin=>pin.LayoutId=="discord.stream.2");
                var secondView=Slots(secondPin.Presenter).Single(slot=>slot.Surface?.IsBackendVideo==true).Surface!;
                await WebAppUntil(()=>BrowserHostFillsSlot(Slots(secondPin.Presenter).Single(slot=>slot.Surface==secondView)));
                await WebAppUntilScript(secondView,"document.querySelector('video')?.videoWidth===640 && document.querySelector('video').videoHeight===360");
                if(!firstPin.Window.IsVisible || firstPin.Window.Interactive || secondPin.Window.Interactive)
                    throw new InvalidOperationException("Opening a second stream changed the first pin or took input.");
                step="third independent screen";
                await WebAppUntil(()=>surface?.FocusHostTarget("discord.stream.4")==true);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await WebAppUntil(()=>pins.Any(pin=>pin.LayoutId=="discord.stream.3" &&
                    Slots(pin.Presenter).Any(slot=>slot.Surface?.IsBackendVideo==true)));
                var thirdPin=pins.Single(pin=>pin.LayoutId=="discord.stream.3");
                var thirdView=Slots(thirdPin.Presenter).Single(slot=>slot.Surface?.IsBackendVideo==true).Surface!;
                await WebAppUntil(()=>BrowserHostFillsSlot(Slots(thirdPin.Presenter).Single(slot=>slot.Surface==thirdView)));
                await WebAppUntilScript(thirdView,"document.querySelector('video')?.videoWidth===800 && document.querySelector('video').videoHeight===600");
                if(!firstPin.Window.IsVisible||!secondPin.Window.IsVisible||thirdPin.Window.Interactive)
                    throw new InvalidOperationException("Opening the third stream displaced an existing pin or took input.");
                await ConfirmStreamChoices("discord.stream","discord.stream.2","discord.stream.3");
                // Show Discord's own view beside the pin; verify the same track
                // object at both video elements, rather than relying on codec guesses.
                step="open original Discord page";
                await WebAppUntil(()=>surface?.FocusHostTarget("discord.account.open")==true);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await WebAppUntil(()=>Slots(surface).Any(slot=>slot.Surface?.IsBackendSession==true));
                var source=Slots(surface).Single(slot=>slot.Surface?.IsBackendSession==true).Surface!;
                await WebAppUntilScript(view,"document.querySelector('video').srcObject.getVideoTracks()[0]===opener.fixtureOriginal.srcObject.getVideoTracks()[0]");
                await WebAppUntilScript(secondView,"document.querySelector('video').srcObject.getVideoTracks()[0]===opener.fixtureSecond.srcObject.getVideoTracks()[0]");
                await WebAppUntilScript(thirdView,"document.querySelector('video').srcObject.getVideoTracks()[0]===opener.fixtureThird.srcObject.getVideoTracks()[0]");
                step="measure native side-by-side playback";
                const string quality="document.querySelector('video').getVideoPlaybackQuality()";
                first=int.Parse(await view.EvaluateFixtureAsync(quality+".totalVideoFrames"));
                secondFirst=int.Parse(await secondView.EvaluateFixtureAsync(quality+".totalVideoFrames"));
                thirdFirst=int.Parse(await thirdView.EvaluateFixtureAsync(quality+".totalVideoFrames"));
                var dropsBefore=int.Parse(await view.EvaluateFixtureAsync(quality+".droppedVideoFrames"));
                await Task.Delay(3000);
                last=int.Parse(await view.EvaluateFixtureAsync(quality+".totalVideoFrames"));
                secondLast=int.Parse(await secondView.EvaluateFixtureAsync(quality+".totalVideoFrames"));
                thirdLast=int.Parse(await thirdView.EvaluateFixtureAsync(quality+".totalVideoFrames"));
                if(secondLast-secondFirst<60||thirdLast-thirdFirst<60)throw new InvalidOperationException("Second or third stream cadence regressed.");
                drops=int.Parse(await view.EvaluateFixtureAsync(quality+".droppedVideoFrames"))-dropsBefore;
                if(last-first<60||drops>(last-first)*.05)throw new InvalidOperationException("Passive pin cadence regression.");
                if(Environment.GetCommandLineArgs().Contains("--direct-video-capture"))
                {
                    // A known color behind the receiver distinguishes real alpha
                    // from screenshot encoders flattening transparent pixels.
                    firstPin.Window.SurfaceBackground.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Lime);
                    await Task.Delay(200,lifetime.Token);
                    var paints=new List<object>();
                    void CapturePaint(DependencyObject node)
                    {
                        if(node is FrameworkElement element&&element.Visibility==Visibility.Visible&&element.ActualWidth>0&&element.ActualHeight>0)
                        {
                            var brush=node switch {
                                Microsoft.UI.Xaml.Controls.Panel panel=>panel.Background,
                                Microsoft.UI.Xaml.Controls.Border border=>border.Background,
                                Microsoft.UI.Xaml.Controls.Control control=>control.Background,_=>null};
                            if(brush is Microsoft.UI.Xaml.Media.SolidColorBrush {Color.A:>0} solid)
                                paints.Add(new{type=element.GetType().Name,id=AutomationProperties.GetAutomationId(element),color=solid.Color.ToString(),
                                    bounds=element.TransformToVisual(firstPin.Window.AutomationRoot).TransformBounds(new(0,0,element.ActualWidth,element.ActualHeight))});
                        }
                        for(var index=0;index<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);index++)
                            CapturePaint(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node,index));
                    }
                    CapturePaint(firstPin.Window.AutomationRoot);
                    await File.WriteAllTextAsync(output+".paint.json",JsonSerializer.Serialize(paints));
                    await File.WriteAllTextAsync(output+".capture.json",JsonSerializer.Serialize(new{hwnd=(long)firstPin.Window.Handle,bounds=firstPin.Window.Bounds}));
                    await WebAppUntil(()=>File.Exists(output+".captured"));
                    ApplyPinnedAppearance(firstPin);
                    App.Window!.Activate();
                    await WebAppUntil(()=>MainFocusEnabled);
                }
                step="provider stream end closes only its native pin";
                await source.EvaluateFixtureAsync("fixtureEndFirstStream();true");
                await WebAppUntil(()=>!pins.Contains(firstPin));
                await ConfirmStreamChoices("discord.stream.2","discord.stream.3");
                var surviving=int.Parse(await secondView.EvaluateFixtureAsync(quality+".totalVideoFrames"));
                var thirdSurviving=int.Parse(await thirdView.EvaluateFixtureAsync(quality+".totalVideoFrames"));
                await Task.Delay(500,lifetime.Token);
                if(int.Parse(await secondView.EvaluateFixtureAsync(quality+".totalVideoFrames"))<=surviving || !secondPin.Window.IsVisible||
                    int.Parse(await thirdView.EvaluateFixtureAsync(quality+".totalVideoFrames"))<=thirdSurviving||!thirdPin.Window.IsVisible)
                    throw new InvalidOperationException("Ending the first stream stopped one of its sibling pins.");
                step="restart sender and Watch and pin again";
                await source.EvaluateFixtureAsync("fixtureRestartFirstStream();true");
                // Account page's ordinary Back route returns to the call surface.
                await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Released);
                await WebAppUntil(()=>MainFocusEnabled&&surface?.FocusHostTarget("discord.stream.2")==true);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await WebAppUntil(()=>pins.Any(pin=>pin.LayoutId=="discord.stream"&&
                    Slots(pin.Presenter).Any(slot=>slot.Surface?.IsBackendVideo==true)));
                var restarted=pins.Single(pin=>pin.LayoutId=="discord.stream");
                if(ReferenceEquals(restarted,firstPin)||!pins.Contains(secondPin)||!pins.Contains(thirdPin))throw new InvalidOperationException("Restart reused a retired pin or removed a sibling.");
                var restartedView=Slots(restarted.Presenter).Single(slot=>slot.Surface?.IsBackendVideo==true).Surface!;
                await WebAppUntilScript(restartedView,"document.querySelector('video')?.videoWidth===1280");
                step="Stop watching closes only the corresponding pin";
                await WebAppUntil(()=>surface?.FocusHostTarget("discord.streams.stop")==true);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await WebAppUntil(()=>!pins.Contains(restarted));
                if(!secondPin.Window.IsVisible||!thirdPin.Window.IsVisible)throw new InvalidOperationException("Stop watching closed a sibling pin.");
                step="Watch again after Stop watching starts a fresh provider subscription";
                await WebAppUntil(()=>MainFocusEnabled&&surface?.FocusHostTarget("discord.stream.2")==true);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await WebAppUntil(()=>pins.Any(pin=>pin.LayoutId=="discord.stream"&&Slots(pin.Presenter).Any(slot=>slot.Surface?.IsBackendVideo==true)));
                var watchedAgain=pins.Single(pin=>pin.LayoutId=="discord.stream");
                var watchedAgainView=Slots(watchedAgain.Presenter).Single(slot=>slot.Surface?.IsBackendVideo==true).Surface!;
                await WebAppUntilScript(watchedAgainView,"document.querySelector('video')?.getVideoPlaybackQuality().totalVideoFrames>0");
                await WebAppUntilScript(source,"fixtureWatched.has('guild:10:20:2')");
                if(!secondPin.IsCurrent||!thirdPin.IsCurrent)throw new InvalidOperationException("Rewatch retired another stream.");
                for(var cycle=0;cycle<3;cycle++)
                {
                    step="third stream stop and immediate rewatch cycle "+(cycle+1);
                    await WebAppUntil(()=>surface?.FocusHostTarget("discord.streams.stop.3")==true);
                    await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                    await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                    await WebAppUntil(()=>!pins.Contains(thirdPin));
                    await ConfirmStreamChoices("discord.stream","discord.stream.2");
                    await WebAppUntil(()=>surface?.FocusHostTarget("discord.stream.4")==true);
                    await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
                    await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                    await WebAppUntil(()=>pins.Any(pin=>pin.LayoutId=="discord.stream.3"&&Slots(pin.Presenter).Any(slot=>slot.Surface?.IsBackendVideo==true)));
                    var replacement=pins.Single(pin=>pin.LayoutId=="discord.stream.3");
                    if(ReferenceEquals(replacement,thirdPin)||!watchedAgain.IsCurrent||!secondPin.IsCurrent)
                        throw new InvalidOperationException("Third-stream replacement reused an old window or retired a sibling.");
                    thirdPin=replacement;
                    thirdView=Slots(thirdPin.Presenter).Single(slot=>slot.Surface?.IsBackendVideo==true).Surface!;
                    await WebAppUntilScript(thirdView,"document.querySelector('video')?.getVideoPlaybackQuality().totalVideoFrames>0");
                    await WebAppUntilScript(source,"fixtureWatched.size===3 && fixtureCachedViewers.size===3");
                    await ConfirmStreamChoices("discord.stream","discord.stream.2","discord.stream.3");
                    thirdReplacements++;
                }
                await UnpinAsync(save:false,watchedAgain);
                await UnpinAsync(save:false,secondPin);
                await UnpinAsync(save:false,thirdPin);
                await WebAppUntil(()=>!source.HasActiveVideoOutput);
                await ConfirmStreamChoices();
                await WebAppUntilScript(source,"fixtureWatched.size===0 && fixtureCachedViewers.size===3");
                await WebAppUntilScript(source,"fixtureOriginal.srcObject.getVideoTracks()[0].readyState==='live' && !fixtureOriginal.paused");
            }
            catch(Exception failure){error=failure.ToString();}
            finally{
                BrowserSurface.FixturePage=null;
                await File.WriteAllTextAsync(output+".tmp",JsonSerializer.Serialize(new{passed=error is null,step,first,last,drops,secondFirst,secondLast,thirdFirst,thirdLast,thirdReplacements,pinChoiceChecks,error}));
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
        let rtc={context:'stream',streamKey:'guild:10:20:2',_destroyed:false,isOwner:false,streamContext:{ownerId:'2',channelId:'20'},_connection:connection,_goLiveQualityManager:manager};
        const canvas2=document.createElement('canvas');canvas2.width=640;canvas2.height=360;
        const draw2=canvas2.getContext('2d');let frame2=0;
        function paint2(){draw2.fillStyle='#322250';draw2.fillRect(0,0,640,360);draw2.fillStyle='yellow';draw2.fillRect((frame2++*4)%600,100,40,40);}
        paint2();setInterval(paint2,33);
        const track2=canvas2.captureStream(30).getVideoTracks()[0];window.fixtureSecond=document.createElement('video');
        fixtureSecond.muted=true;fixtureSecond.autoplay=true;document.body.append(fixtureSecond);
        fixtureSecond.srcObject=new MediaStream([track2]);fixtureSecond.play();
        const connection2={context:'stream',streamUserId:'3',peer:{getReceivers:()=>[{track:track2}]}};
        const manager2={incomingVideoEnabled:true,getUserID:()=>'3',onIncomingVideoEnabled(v){this.incomingVideoEnabled=v;}};
        let rtc2={context:'stream',streamKey:'guild:10:20:3',_destroyed:false,isOwner:false,streamContext:{ownerId:'3',channelId:'20'},
          _connection:connection2,_goLiveQualityManager:manager2};
        const canvas3=document.createElement('canvas');canvas3.width=800;canvas3.height=600;
        const draw3=canvas3.getContext('2d');let frame3=0;
        function paint3(){draw3.fillStyle='#133c25';draw3.fillRect(0,0,800,600);draw3.fillStyle='cyan';draw3.fillRect(100,(frame3++*5)%550,70,50);}
        paint3();setInterval(paint3,33);
        const track3=canvas3.captureStream(30).getVideoTracks()[0];window.fixtureThird=document.createElement('video');
        fixtureThird.muted=true;fixtureThird.autoplay=true;document.body.append(fixtureThird);
        fixtureThird.srcObject=new MediaStream([track3]);fixtureThird.play();
        const connection3={context:'stream',streamUserId:'4',peer:{getReceivers:()=>[{track:track3}]}};
        const manager3={incomingVideoEnabled:true,getUserID:()=>'4',onIncomingVideoEnabled(v){this.incomingVideoEnabled=v;}};
        let rtc3={context:'stream',streamKey:'guild:10:20:4',_destroyed:false,isOwner:false,streamContext:{ownerId:'4',channelId:'20'},
          _connection:connection3,_goLiveQualityManager:manager3};
        const engine={connections:[connection,connection2,connection3]};
        let firstStreaming=true;window.fixtureEndFirstStream=()=>{firstStreaming=false;};
        window.fixtureRestartFirstStream=()=>{firstStreaming=true;};
        window.fixtureWatched=new Set();window.fixtureCachedViewers=new Set();const fixtureEvents=[];
        function startWatch(stream,options){
          const streamKey='guild:'+stream.guildId+':'+stream.channelId+':'+stream.ownerId;
          fixtureEvents.push("STREAM_WATCH",streamKey);
          if(!options?.forceMultiple)fixtureWatched.clear();
          fixtureWatched.add(streamKey);fixtureCachedViewers.add(streamKey);
          if(stream.ownerId==='2'&&rtc._destroyed)rtc={...rtc,_destroyed:false};
          if(stream.ownerId==='3'&&rtc2._destroyed)rtc2={...rtc2,_destroyed:false};
          if(stream.ownerId==='4'&&rtc3._destroyed)rtc3={...rtc3,_destroyed:false};
        }
        function closeWatch(streamKey,feedback){
          fixtureEvents.push("STREAM_STOP",streamKey);
          const closing=streamKey==='guild:10:20:4'?rtc3:streamKey==='guild:10:20:3'?rtc2:rtc;
          setTimeout(()=>{fixtureWatched.delete(streamKey);closing._destroyed=true;},500);
        }
        const stores={UserStore:{getCurrentUser:()=>({id:'1'}),getUser:()=>({username:'Generated sender'})},
          SelectedChannelStore:{getVoiceChannelId:()=>'20'},ChannelStore:{getChannel:()=>({id:'20',guild_id:'10',name:'Generated call'})},
          VoiceStateStore:{getVoiceStatesForChannel:()=>({peer:{userId:'2',selfStream:firstStreaming},second:{userId:'3',selfStream:true},third:{userId:'4',selfStream:true}})},
          ApplicationStreamingStore:{getViewerIds:key=>fixtureCachedViewers.has(key)?['1']:[],
            getActiveStreamForStreamKey:key=>fixtureWatched.has(key)?{guildId:'10',channelId:'20',ownerId:key.split(':').at(-1),state:'ACTIVE'}:null},StreamRTCConnectionStore:{getRTCConnection:key=>key==='guild:10:20:4'?rtc3:key==='guild:10:20:3'?rtc2:rtc},
          MediaEngineStore:{getMediaEngine:()=>engine},RTCConnectionStore:{getState:()=>'RTC_CONNECTED',getChannelId:()=>'20'}};
        const modules={};for(const [name,store] of Object.entries(stores))modules[name]={exports:{...store,getName:()=>name}};
        modules.visibility={exports:{isIncomingVideoEnabled:()=>true,lastIncomingVideoEnabledChangeTime:()=>0}};
        modules.watch={exports:{startWatch,closeWatch}};
        window.webpackChunkdiscord_app=[];
        webpackChunkdiscord_app.push=function(entry){entry[2]({c:modules});return Array.prototype.push.call(this,entry);};
        </script>
        """;
}
