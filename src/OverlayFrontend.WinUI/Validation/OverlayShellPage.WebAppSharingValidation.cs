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
            var alreadyExpanded = Environment.GetCommandLineArgs().Contains("--webapp-sharing-already-expanded");
            if (alreadyExpanded)
                check(await ExpandBrowserAsync(browser, lifetime.Token), "User-expanded browser is ready before requesting sharing");
            var rounds = Environment.GetCommandLineArgs().Contains("--webapp-sharing-repeat") ? 3 : 1;
            var lastCommand = 0;
            ushort heldButtons = 0;
            for (var round = 1; round <= rounds; round++)
            {
            // Each preceding round explicitly collapses back to the pin. Re-enter
            // user expansion before asserting its preservation on the next round.
            if (alreadyExpanded && round > 1)
                check(await ExpandBrowserAsync(browser, lifetime.Token), "User-expanded browser is restored before the next sharing request");
            // Exercise audio chooser controls only on the cancelled round. Never
            // start a system/microphone audio stream in this synthetic fixture.
            await browser.EvaluateFixtureAsync(round == 2 ? "window.testAudio=true" : "window.testAudio=false");
            await browser.EvaluateGestureFixtureAsync("""
                window.shareState='pending'; window.sharedStreams ??= []; window.previousStreamCount=sharedStreams.length;
                navigator.mediaDevices.getDisplayMedia({video:true,audio:window.testAudio}).then(async stream=>{
                  window.shared=stream; window.sharedStreams.push(stream);
                  const track=stream.getVideoTracks()[0];window.shareSettings=track.getSettings();
                  if(shareSettings.displaySurface!=='window'){stream.getTracks().forEach(t=>t.stop());window.shareState='wrong-source';return;}
                  const video=document.createElement('video');video.muted=true;
                  // The source is static: subscribe before playback can consume
                  // its first (and potentially only new) decoded frame.
                  video.requestVideoFrameCallback(()=>window.shareState='ready');
                  video.srcObject=stream;
                  // Keep the decoded-frame probe visible after returning to a
                  // small pin. Earlier streams remain live, but their previews
                  // must not push this round's video outside the viewport.
                  window.sharePreview?.remove();window.sharePreview=video;
                  video.style='position:fixed;right:0;top:0;width:160px;height:100px';document.body.append(video);await video.play();
                },e=>window.shareState=e.name);
                """);
            await choose(0);
            try { await WebAppUntil(() => expandedBrowser?.Surface == browser && !fullscreenLayer.IsTransitioning); }
            catch (TimeoutException error)
            {
                throw new InvalidOperationException($"Sharing preparation: expanded={expandedBrowser is not null} transition={fullscreenLayer.IsTransitioning} native={nativeBrowserPickerActive} state={await browser.EvaluateFixtureAsync("window.shareState")} trace={string.Join(";", browser.FixturePermissionTrace)}", error);
            }
            check(browser.CompositionRoot.ActualWidth > 700 && browser.CompositionRoot.ActualHeight > 600,
                "Pinned screen-sharing request expands the existing native viewport before the Windows picker");
            await File.WriteAllTextAsync(result + ".sharing-ready", JsonSerializer.Serialize(new { target = targetName, pid = Environment.ProcessId, targetPid = target.Id, round }));
            var deadline = Environment.TickCount64 + 180_000;
            var cancelRequested = false;
            while (await browser.EvaluateFixtureAsync("window.shareState==='pending'") == "true")
            {
                if (File.Exists(result + ".picker-command"))
                {
                    var fields = (await File.ReadAllTextAsync(result + ".picker-command")).Trim().Split(' ');
                    if (fields.Length >= 2 && int.TryParse(fields[0], out var sequence) && sequence > lastCommand)
                    {
                        lastCommand = sequence;
                        deadline = Environment.TickCount64 + 60_000;
                        cancelRequested = fields[1] is "cancel" or "cancel-click";
                        var nativeReady = NativePickerReadyForValidation?.Invoke() == true;
                        if (fields[1] == "foreground-return")
                        {
                            WinUIEx.HwndExtensions.SetForegroundWindow(target.MainWindowHandle);
                            await Task.Delay(2200);
                            check(nativeBrowserPickerActive && expandedBrowser?.Surface == browser,
                                "Temporary foreign foreground pauses chooser input without cancelling its session or placement");
                            pinned!.Window.Activate();
                        }
                        else if (fields[1] == "hide-reopen")
                        {
                            cancelRequested = true;
                            NativePickerBlockCloseForValidation?.Invoke(true);
                            NativePickerVisibilityForValidation?.Invoke(false);
                            await Task.Delay(600);
                            NativePickerVisibilityForValidation?.Invoke(true);
                            await Task.Delay(600);
                            check(nativeBrowserPickerActive && expandedBrowser?.Surface == browser,
                                "Hide/reopen retains the chooser session and expanded placement until native closure is confirmed");
                            NativePickerBlockCloseForValidation?.Invoke(false);
                        }
                        else if (fields[1] is "move" or "scroll" && fields.Length == 5 && short.TryParse(fields[2], out var x) &&
                            short.TryParse(fields[3], out var y) && int.TryParse(fields[4], out var frames) && frames is >= 1 and <= 60)
                        {
                            for (var frame = 0; frame < frames; frame++)
                            {
                                NativePickerFrameForValidation?.Invoke(new ControllerFrame { Connected = 1, State = fields[1] == "move"
                                    ? new() { Buttons = heldButtons, LeftThumbX = x, LeftThumbY = y } : new() { Buttons = heldButtons, RightThumbX = x, RightThumbY = y } });
                                await Task.Delay(16);
                            }
                        }
                        else if (fields[1] == "press")
                        {
                            heldButtons = 0x1000;
                            NativePickerFrameForValidation?.Invoke(new ControllerFrame { Connected = 1, PressedButtons = heldButtons, State = new() { Buttons = heldButtons } });
                        }
                        else if (fields[1] == "release")
                        {
                            NativePickerFrameForValidation?.Invoke(new ControllerFrame { Connected = 1, ReleasedButtons = heldButtons });
                            heldButtons = 0;
                        }
                        else if (fields[1] is "select" or "cancel-click")
                        {
                            NativePickerFrameForValidation?.Invoke(new ControllerFrame { Connected = 1, PressedButtons = 0x1000, State = new() { Buttons = 0x1000 } });
                            await Task.Delay(50);
                            NativePickerFrameForValidation?.Invoke(new ControllerFrame { Connected = 1, ReleasedButtons = 0x1000 });
                        }
                        else if (fields[1] == "cancel")
                            NativePickerFrameForValidation?.Invoke(new ControllerFrame { Connected = 1, PressedButtons = 0x2000 });
                        else if (fields[1] == "neutral")
                            NativePickerFrameForValidation?.Invoke(new ControllerFrame { Connected = 1 });
                        var position = NativePickerPositionForValidation?.Invoke() ?? default;
                        await File.WriteAllTextAsync(result + ".picker-ack", JsonSerializer.Serialize(new { round, sequence, nativeReady,
                            x = position.X, y = position.Y, circleVisible = position.Visible, window = (long)(NativePickerWindowForValidation?.Invoke() ?? 0), pointer = NativePickerStatusForValidation?.Invoke() }));
                    }
                }
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Native sharing selection did not complete. Synthetic state: " +
                    await browser.EvaluateFixtureAsync("JSON.stringify({state:window.shareState,streams:sharedStreams.map(s=>s.getTracks().map(t=>({state:t.readyState,kind:t.kind}))),videos:[...document.querySelectorAll('video')].map(v=>({ready:v.readyState,paused:v.paused,time:v.currentTime,rect:v.getBoundingClientRect().toJSON()}))})"));
                await Task.Delay(100);
            }
            if (cancelRequested)
                check(await browser.EvaluateFixtureAsync("window.shareState==='NotAllowedError' && sharedStreams.length===previousStreamCount && sharedStreams.every(s=>s.getTracks().every(t=>t.readyState==='live'))") == "true", "Controller cancels the native sharing picker without creating a stream");
            else
            {
                check(await browser.EvaluateFixtureAsync("window.shareState==='ready' && shared.getAudioTracks().length===0") == "true",
                    "Native picker delivers a video frame from the explicitly selected synthetic window");

            }
            if (alreadyExpanded)
            {
                await WebAppUntil(() => !nativeBrowserPickerActive && foreground && expandedBrowser?.Surface == browser && browser.IsBrowsing);
                check(true, "Picker dismissal preserves a browser that was already user-expanded");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
            }
            await WebAppUntil(() => expandedBrowser is null && !nativeBrowserPickerActive && PinnedInputActive && browser.IsBrowsing);
            check(true, alreadyExpanded ? "User can collapse the preserved expanded browser back to its original pin" : "Picker dismissal automatically restores the original pin and interaction without another B press");
            check(await browser.EvaluateFixtureAsync("sharedStreams.every(s=>s.getTracks().every(t=>t.readyState==='live'))") == "true", $"Round {round}: earlier streams survive chooser completion/cancellation");
            }
            await browser.EvaluateFixtureAsync("sharedStreams.forEach(s=>s.getTracks().forEach(t=>t.stop()))");
            check(await browser.EvaluateFixtureAsync("sharedStreams.every(s=>s.getTracks().every(t=>t.readyState==='ended'))") == "true", "Website stops all screen-sharing tracks after repeated requests");
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
