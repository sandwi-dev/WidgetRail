using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Alternating hidden-page trials on synthetic media, not a production profiler.</summary>
internal static class WebAppMemoryValidation
{
    internal static async Task RunAsync(BrowserSurface browser, string streamExpression, string result, Action<bool, string> check)
    {
        var core = ((WebView2)browser.CompositionRoot).CoreWebView2;
        var phases = new List<object>();
        await browser.EvaluateGestureFixtureAsync("window.memoryStream=" + streamExpression + ";" + """
            window.memoryReady=false;
            (async()=>{
              if(!window.p1){
                window.p1=new RTCPeerConnection({iceServers:[]});window.p2=new RTCPeerConnection({iceServers:[]});
                memoryStream.getTracks().forEach(t=>p1.addTrack(t,memoryStream));
                const gathered=pc=>pc.iceGatheringState==='complete'?Promise.resolve():new Promise(r=>pc.addEventListener('icegatheringstatechange',()=>{if(pc.iceGatheringState==='complete')r();}));
                await p1.setLocalDescription(await p1.createOffer());await gathered(p1);
                await p2.setRemoteDescription(p1.localDescription);await p2.setLocalDescription(await p2.createAnswer());await gathered(p2);
                await p1.setRemoteDescription(p2.localDescription);
              }
              window.readMemoryMedia=async()=>{
                const m={bytes:0,frames:0,receivedBytes:0,decoded:0,lost:0,concealed:0,
                  live:memoryStream.getTracks().every(t=>t.readyState==='live'),connected:p1.connectionState==='connected'&&p2.connectionState==='connected',hidden:document.hidden};
                for(const s of (await p1.getStats()).values())if(s.type==='outbound-rtp'){m.bytes+=s.bytesSent||0;m.frames+=s.framesEncoded||0;}
                for(const s of (await p2.getStats()).values())if(s.type==='inbound-rtp'){m.receivedBytes+=s.bytesReceived||0;m.decoded+=s.framesDecoded||0;m.lost+=s.packetsLost||0;m.concealed+=s.concealedSamples||0;}
                return m;
              };
              window.memoryReady=true;
            })();
            """);
        var until = Environment.TickCount64 + 20_000;
        while (await browser.EvaluateFixtureAsync("window.memoryReady && p1.connectionState==='connected' && p2.connectionState==='connected'") != "true")
        { if (Environment.TickCount64 > until) throw new TimeoutException("Synthetic memory loopback did not connect."); await Task.Delay(50); }
        browser.ManualMemoryTargetForValidation = true;
        browser.SetPresentation(false, false);
        try
        {
            foreach (var level in new[] { CoreWebView2MemoryUsageTargetLevel.Normal, CoreWebView2MemoryUsageTargetLevel.Low,
                CoreWebView2MemoryUsageTargetLevel.Normal, CoreWebView2MemoryUsageTargetLevel.Low })
            {
                core.MemoryUsageTargetLevel = level;
                await Task.Delay(1500);
                var samples = new List<object>();
                var start = await Media();
                for (var i = 0; i < 10; i++)
                {
                    await Task.Delay(1000);
                    var timer = Stopwatch.StartNew();
                    var media = await Media();
                    var responseMs = timer.Elapsed.TotalMilliseconds;
                    long workingSet = 0, privateBytes = 0; double cpuMs = 0;
                    foreach (var info in core.Environment.GetProcessInfos())
                    {
                        try
                        {
                            using var process = Process.GetProcessById(info.ProcessId);
                            workingSet += process.WorkingSet64; privateBytes += process.PrivateMemorySize64;
                            cpuMs += process.TotalProcessorTime.TotalMilliseconds;
                        }
                        catch (ArgumentException) { } // A utility process may exit between enumeration and sampling.
                    }
                    samples.Add(new { workingSet, privateBytes, cpuMs, responseMs, media });
                }
                var end = await Media();
                check(core.MemoryUsageTargetLevel == level && !core.IsSuspended && end.GetProperty("hidden").GetBoolean(),
                    $"Hidden {level} trial applies its target without suspending scripts");
                check(end.GetProperty("live").GetBoolean() && end.GetProperty("connected").GetBoolean() &&
                    end.GetProperty("bytes").GetInt64() > start.GetProperty("bytes").GetInt64() &&
                    end.GetProperty("decoded").GetInt64() > start.GetProperty("decoded").GetInt64(),
                    $"Hidden {level} trial preserves live tracks, connection and decoded video progress");
                phases.Add(new { level = level.ToString(), start, end, samples });
                await File.WriteAllTextAsync(result + ".memory.json", JsonSerializer.Serialize(new { phases }));
            }
        }
        finally { core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal; browser.ManualMemoryTargetForValidation = false; }
        var resume = Stopwatch.StartNew();
        browser.SetPresentation(true, true);
        var resumed = await Media();
        check(resumed.GetProperty("live").GetBoolean() && resumed.GetProperty("connected").GetBoolean() &&
            core.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Normal, "Returning restores Normal with the same live connection");
        await File.WriteAllTextAsync(result + ".memory.json", JsonSerializer.Serialize(new { phases, returnResponseMs = resume.Elapsed.TotalMilliseconds }));

        async Task<JsonElement> Media()
        {
            // ExecuteScript does not await promises. CDP's awaitPromise does,
            // and measures a real renderer round-trip in both trial conditions.
            using var response = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                "{\"expression\":\"readMemoryMedia()\",\"awaitPromise\":true,\"returnByValue\":true}").AsTask().WaitAsync(TimeSpan.FromSeconds(8)));
            return response.RootElement.GetProperty("result").GetProperty("value").Clone();
        }
    }

    internal static async Task VerifyPolicyAsync(BrowserSurface browser, Action<bool, string> check)
    {
        var core = ((WebView2)browser.CompositionRoot).CoreWebView2;
        var navigation = browser.NavigationCount;
        browser.SetPresentation(false, false);
        await Task.Delay(1000);
        check(core.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Normal, "Brief hides retain Normal memory target");
        browser.SetPresentation(true, true);
        await Task.Delay(BrowserSurface.HiddenMemoryDelay + TimeSpan.FromSeconds(1));
        check(core.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Normal, "Showing cancels pending memory reduction");
        browser.SetPresentation(false, false);
        // Layout/placement reconciliation must not keep postponing the deadline.
        for (var i = 0; i < 9; i++) { await Task.Delay(2000); browser.SetPresentation(false, false); }
        check(core.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Low && !core.IsSuspended,
            "Sustained hiding applies Low despite repeated placement updates, without suspension");
        check(await browser.EvaluateFixtureAsync("memoryStream.getTracks().every(t=>t.readyState==='live') && p1.connectionState==='connected'") == "true",
            "Automatic memory policy preserves the synthetic media session");
        browser.SetPresentation(true, true);
        check(core.MemoryUsageTargetLevel == CoreWebView2MemoryUsageTargetLevel.Normal && browser.NavigationCount == navigation,
            "Showing immediately restores Normal without reloading the page");
    }
}
