using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WebSessionCaptureMediaValidationPage
{
    private static async Task ExerciseTrackConstraintsAsync(string output,List<string> checks,List<object> samples)
    {
        // No interactive window is needed for this generated-pixel contract test.
        App.Window?.AppWindow.Hide();
        BrowserSurface? browser=null;
        using var backing=new WebSessionBackgroundHost();
        try
        {
            await backing.Ready.WaitAsync(TimeSpan.FromSeconds(10));
            const string origin="https://track-constraints.fixture.invalid";
            BrowserSurface.FixturePage=_=>"<!doctype html><title>Generated track constraints</title><body>Synthetic pixels only</body>";
            var environment=CoreWebView2Environment.CreateWithOptionsAsync(null,Path.Combine(output,"profile"),new()).AsTask();
            var definition=new WebSessionDefinition("constraints","Generated capture constraints",WebSessionSource.Remote(origin+"/first"))
            {NativeCapture=true,PageAdapter=new(){ScriptAsset="fixture.js",Origins=new[]{origin},Mode=WebSessionPageAdapterMode.SessionBackend}};
            browser=new BrowserSurface("fixture.constraints",new("constraints",1,definition.Source.Location,definition.AccessibleName)
                {Options=definition.Browser,InteractionMode=BrowserInteractionMode.ActivateToInteract},()=>environment);
            var script=Encoding.UTF8.GetBytes("host.onRequest(()=>({ready:true}));");
            browser.ConfigureHostedSession(definition,new[]{new BridgeWebSessionResource("fixture.js","text/javascript",Convert.ToHexString(SHA256.HashData(script)),Convert.ToBase64String(script))});
            backing.Root.Children.Add(browser);browser.PlacementHost=backing.Root;backing.Root.UpdateLayout();
            await browser.StartHostedSessionAsync(default);
            await WaitForCaptureDocumentAsync(browser,definition.Source.Location);
            var experiment=Path.Combine(output,"track-experiment.js");
            if(File.Exists(experiment))
            {
                await browser.EvaluateFixtureAsync(await File.ReadAllTextAsync(experiment));
                string observed="null";
                await Until(async()=>
                {
                    observed=await browser.EvaluateFixtureAsync("globalThis.trackExperiment??null");
                    using var value=JsonDocument.Parse(observed);
                    return value.RootElement.ValueKind==JsonValueKind.Object&&value.RootElement.TryGetProperty("done",out var done)&&done.ValueKind==JsonValueKind.True;
                });
                using var result=JsonDocument.Parse(observed);
                samples.Add(result.RootElement.Clone());
                checks.Add("Observed native generated-track constraint behavior; this is diagnostic evidence, not a transport pass");
                return;
            }
            var epoch=0;
            foreach(var quality in new[]{"720p30","1440p60"})
            {
                ++epoch;
                var expectedHeight=quality=="1440p60"?1440:720;
                var expectedWidth=quality=="1440p60"?2560:1280;
                var expectedFps=quality=="1440p60"?60:30;
                using(var started=JsonDocument.Parse(await browser.EvaluateFixtureAsync($"WidgetRailNativeMedia.start({epoch},false,{JsonSerializer.Serialize(quality)})")))
                    Check(started.RootElement.ValueKind==JsonValueKind.True,"Real native-media runtime starts "+quality);
                await browser.EvaluateFixtureAsync("globalThis.constraintProbe={phase:'waiting'};navigator.mediaDevices.getDisplayMedia({video:true}).then(s=>{globalThis.constraintStream=s;globalThis.constraintTrack=s.getVideoTracks()[0];globalThis.constraintProbe={phase:'ready',constraints:constraintTrack.getConstraints(),settings:constraintTrack.getSettings()};},e=>globalThis.constraintProbe={phase:'error',error:e.name+':'+e.message+'; constraint='+e.constraint});");
                browser.PostGeneratedCaptureFrameForValidation(640,360,epoch,1);
                using(var initial=await Read("ready"))
                {
                    samples.Add(new{quality,phase="initial",result=initial.RootElement.Clone()});
                    var constraints=initial.RootElement.GetProperty("constraints");
                    Check(Bound(constraints,"width")==expectedWidth&&Bound(constraints,"height")==expectedHeight&&Bound(constraints,"frameRate")==expectedFps,
                        quality+": actual track constraints expose the selected ceilings to browser consumers");
                    var settings=initial.RootElement.GetProperty("settings");
                    Check(settings.GetProperty("width").GetInt32()==640&&settings.GetProperty("height").GetInt32()==360,
                        quality+": actual source settings remain 640x360 without upscaling");
                }
                await browser.EvaluateFixtureAsync("globalThis.constraintProbe={phase:'waiting'};constraintTrack.applyConstraints({width:{max:320},height:{max:180},frameRate:{max:15}}).then(()=>{globalThis.constraintReader=new MediaStreamTrackProcessor({track:constraintTrack}).readable.getReader();constraintProbe={phase:'downscaled',constraints:constraintTrack.getConstraints()};},e=>constraintProbe={phase:'error',error:e.name+':'+e.message});");
                using(var changed=await Read("downscaled"))
                {
                    samples.Add(new{quality,phase="requested-downscale",result=changed.RootElement.Clone()});
                    var constraints=changed.RootElement.GetProperty("constraints");
                    Check(Bound(constraints,"width")==320&&Bound(constraints,"height")==180&&Bound(constraints,"frameRate")==15,
                        quality+": later consumer applyConstraints remains effective and observable");
                }
                await browser.EvaluateFixtureAsync("constraintProbe={phase:'waiting'};(async()=>{for(;;){const r=await constraintReader.read();if(r.done)throw Error('ended');const f=r.value;try{if(f.timestamp<80000)continue;constraintProbe={phase:'frame',width:f.displayWidth,height:f.displayHeight,settings:constraintTrack.getSettings()};return;}finally{f.close();}}})().catch(e=>constraintProbe={phase:'error',error:e.message});");
                browser.PostGeneratedCaptureFrameForValidation(640,360,epoch,2);
                using(var frame=await Read("frame"))
                {
                    samples.Add(new{quality,phase="received-downscale",result=frame.RootElement.Clone()});
                    Check(frame.RootElement.GetProperty("width").GetInt32()<=320&&frame.RootElement.GetProperty("height").GetInt32()<=180,
                        quality+": real browser track delivery obeys subsequent downscale bounds");
                }
                await browser.EvaluateFixtureAsync($"constraintReader.cancel();WidgetRailNativeMedia.stop({epoch});");
            }

            async Task<JsonDocument> Read(string phase)
            {
                string json="null";
                await Until(async()=>
                {
                    json=await browser.EvaluateFixtureAsync("globalThis.constraintProbe");
                    using var result=JsonDocument.Parse(json);
                    if(result.RootElement.ValueKind!=JsonValueKind.Object)return false;
                    var current=result.RootElement.GetProperty("phase").GetString();
                    if(current=="error")throw new InvalidOperationException(result.RootElement.GetProperty("error").GetString());
                    return current==phase;
                });
                return JsonDocument.Parse(json);
            }
        }
        finally{if(browser is not null)await browser.DisposeAsync();}
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks.Add(label);}
    }

    private static double? Bound(JsonElement constraints,string name)
    {
        if(!constraints.TryGetProperty(name,out var value))return null;
        if(value.ValueKind==JsonValueKind.Number)return value.GetDouble();
        return value.ValueKind==JsonValueKind.Object&&value.TryGetProperty("max",out var max)&&max.ValueKind==JsonValueKind.Number?max.GetDouble():null;
    }
}
