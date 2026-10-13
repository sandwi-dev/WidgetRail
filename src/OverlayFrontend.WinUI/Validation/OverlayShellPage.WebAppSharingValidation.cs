using System.Diagnostics;
using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyShellScreenSharingAsync(BrowserSurface browser, string result, string sourceExecutable,
        Func<int, Task> choose, Action<bool, string> check)
    {
        var targetName = "WidgetRail synthetic sharing target " + Environment.ProcessId;
        var start = new ProcessStartInfo(sourceExecutable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Normal };
        start.ArgumentList.Add(targetName); start.ArgumentList.Add(Path.GetFullPath(result + ".target"));
        using var target = Process.Start(start) ?? throw new InvalidOperationException("Synthetic sharing source did not start.");
        try
        {
            await WebAppUntil(() => File.Exists(result + ".target"));
            pinned!.Window.Activate();
            await WebAppUntil(() => PinnedInputActive && browser.IsBrowsing);
            await browser.EvaluateGestureFixtureAsync("""
                window.shareState='pending';
                navigator.mediaDevices.getDisplayMedia({video:true,audio:false}).then(async stream=>{
                  window.shared=stream;
                  const track=stream.getVideoTracks()[0];window.shareSettings=track.getSettings();
                  if(shareSettings.displaySurface!=='window'){stream.getTracks().forEach(t=>t.stop());window.shareState='wrong-source';return;}
                  const video=document.createElement('video');video.muted=true;video.srcObject=stream;
                  video.style='width:160px;height:100px';document.body.append(video);await video.play();
                  video.requestVideoFrameCallback(()=>window.shareState='ready');
                },e=>window.shareState=e.name);
                """);
            await choose(0);
            await WebAppUntil(() => expandedBrowser?.Surface == browser && !fullscreenLayer.IsTransitioning);
            check(browser.CompositionRoot.ActualWidth > 700 && browser.CompositionRoot.ActualHeight > 600,
                "Pinned screen-sharing request expands the existing native viewport before the Windows picker");
            await File.WriteAllTextAsync(result + ".sharing-ready", JsonSerializer.Serialize(new { target = targetName, pid = Environment.ProcessId, targetPid = target.Id }));
            var deadline = Environment.TickCount64 + 180_000;
            var lastCommand = 0;
            var cancelRequested = false;
            while (await browser.EvaluateFixtureAsync("window.shareState==='pending'") == "true")
            {
                if (File.Exists(result + ".picker-command"))
                {
                    var fields = (await File.ReadAllTextAsync(result + ".picker-command")).Trim().Split(' ');
                    if (fields.Length == 2 && int.TryParse(fields[0], out var sequence) && sequence > lastCommand)
                    {
                        lastCommand = sequence;
                        deadline = Environment.TickCount64 + 60_000;
                        cancelRequested = fields[1] == "cancel";
                        var pressed = fields[1] switch { "select" => 0x1000, "cancel" => 0x2000, "previous" => 0x100, "next" => 0x200, _ => 0 };
                        var direction = fields[1] switch { "left" => NavigationDirection.Left, "right" => NavigationDirection.Right,
                            "up" => NavigationDirection.Up, "down" => NavigationDirection.Down, _ => NavigationDirection.None };
                        var nativeReady = NativePickerReadyForValidation?.Invoke() == true;
                        NativePickerFrameForValidation?.Invoke(new ControllerFrame { Connected = 1, PressedButtons = (ushort)pressed,
                            DpadNavigation = new() { Direction = direction, Phase = direction == NavigationDirection.None ? NavigationPhase.None : NavigationPhase.Pressed } });
                        await File.WriteAllTextAsync(result + ".picker-ack", JsonSerializer.Serialize(new { sequence, nativeReady }));
                    }
                }
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Native sharing selection did not complete.");
                await Task.Delay(100);
            }
            if (cancelRequested)
                check(await browser.EvaluateFixtureAsync("window.shareState==='NotAllowedError' && !window.shared") == "true", "Controller B cancels the native sharing picker without creating a stream");
            else
            {
                check(await browser.EvaluateFixtureAsync("window.shareState==='ready' && shared.getAudioTracks().length===0") == "true",
                    "Native picker delivers a video frame from the explicitly selected synthetic window");
                await browser.EvaluateFixtureAsync("shared.getTracks().forEach(t=>t.stop())");
                check(await browser.EvaluateFixtureAsync("shared.getTracks().every(t=>t.readyState==='ended')") == "true", "Website stops its screen-sharing tracks");
            }
            ReturnFromPinnedRequested?.Invoke();
            await WebAppUntil(() => foreground && browser.IsBrowsing);
            await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
            await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
            await WebAppUntil(() => expandedBrowser is null && PinnedInputActive && browser.IsBrowsing);
        }
        finally
        {
            if (!target.HasExited)
            {
                target.CloseMainWindow();
                try { await target.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException) { if (!target.HasExited) target.Kill(entireProcessTree: true); }
            }
        }
    }
}
