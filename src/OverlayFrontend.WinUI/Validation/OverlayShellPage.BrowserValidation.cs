using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableBrowserValidation(string path)
    {
        BrowserSurface.FixturePage = url => "<!doctype html><html><head><title>Fixture " + new Uri(url).AbsolutePath +
            "</title><style>body{margin:0;background:#18334b;color:white;min-height:3000px;font:24px sans-serif}a{position:fixed;left:40%;top:40%;width:20%;height:20%;background:#477;color:white;display:grid;place-items:center}</style></head><body>Browser fixture<a href='https://example.com/clicked'>Open next page</a></body></html>";
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null;
            try
            {
                if (startup is not null) await startup;
                await Until(() => activeWidget == "browser" && interactive && foreground && !switching);
                Check(pinned is null, "Startup does not recreate a pin from legacy saved preferences");
                await ValidateLayoutSessionContentionAsync();
                Check(true, "A contended session read inside native collection layout does not re-enter the dispatcher");
                await Until(() => Slot(surface)?.Surface is { IsReady: true, IsNavigationInProgress: false });
                Check(Slot(surface)!.Surface!.CurrentUrl == WebBrowserDocument.StartPage, "Fresh Browser offers toolbar without loading a website");
                Slot(surface)!.Surface!.NavigateForValidation("https://example.com/first?long=" + new string('a', 1500));
                await Until(() => Slot(surface)?.Surface is { IsReady: true, CurrentTitle: "Fixture /first" });
                var first = Slot(surface)!.Surface!;
                Check(browserOwner!.CreatedCount == 1, "First user navigation lazily creates one native browser");
                await Until(() => first.IsInteracting && first.IsBrowsing);
                Check(true, "Focus mode starts interaction when the initially focused page becomes ready");
                await Until(() => first.FixtureLibraryStore?.History.Count > 0);
                await RouteButtonAsync(ControllerButton.LeftStick, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.LeftStick, ControllerEventPhase.Released);
                Check(first.IsToolbarFocused && !first.IsBrowsing, "LS toggles to toolbar without requiring a held stick");
                await RouteButtonAsync(ControllerButton.Y, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.Y, ControllerEventPhase.Released);
                await Until(() => first.FixtureEditor is { IsLoaded: true });
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => !first.HasDialog);
                Check(first.IsToolbarFocused && AutomationProperties.GetAutomationId((DependencyObject)FocusManager.GetFocusedElement(XamlRoot)) == "Browser.Address",
                    "Cancelling toolbar address editing restores its native focus");
                for (var n = 0; n < 3; n++) first.MoveFocus(FocusNavigationDirection.Right);
                Check(AutomationProperties.GetAutomationId((DependencyObject)FocusManager.GetFocusedElement(XamlRoot)) == "Browser.Bookmark", "Toolbar controller navigation reaches bookmark button");
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => first.FixtureLibraryStore!.Bookmarks.Count == 1);
                first.MoveFocus(FocusNavigationDirection.Right);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => first.FixtureLibrary is { IsLoaded: true });
                Check(true, "Bookmarks opens as a themed controller dialog");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => !first.HasDialog && first.IsToolbarFocused);
                Check(interactive, "Closing bookmarks returns to toolbar without opening the tray");
                Check(AutomationProperties.GetAutomationId((DependencyObject)FocusManager.GetFocusedElement(XamlRoot)) == "Browser.Bookmarks",
                    "Bookmarks close restores its exact toolbar button: " + AutomationProperties.GetAutomationId((DependencyObject)FocusManager.GetFocusedElement(XamlRoot)));
                first.MoveFocus(FocusNavigationDirection.Right);
                Check(AutomationProperties.GetAutomationId((DependencyObject)FocusManager.GetFocusedElement(XamlRoot)) == "Browser.History",
                    "Right from Bookmarks focuses History: " + AutomationProperties.GetAutomationId((DependencyObject)FocusManager.GetFocusedElement(XamlRoot)));
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => first.FixtureLibrary is { IsLoaded: true });
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => !first.HasDialog && first.IsBrowsing);
                Check(true, "A on history reopens a page and restores page interaction");
                if (Environment.GetCommandLineArgs().Contains("--browser-library-only"))
                {
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { pid = Environment.ProcessId, passed = true, checks, error }));
                    return;
                }
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => !interactive && !first.IsInteracting && FocusedTrayWidget()?.Id == "browser");
                Check(true, "Focus-mode B returns directly to the tray");
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => interactive && first.IsBrowsing);
                await first.EvaluateFixtureAsync("new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))");
                Check(first.NavigationCount == 1, "A used to enter the widget never clicks the automatically interacting page");
                var focusSibling = new Microsoft.UI.Xaml.Controls.Button { Content = "Focus fixture", Width = 100, Height = 40 };
                StartupContentStage.Children.Add(focusSibling);
                await Until(() => focusSibling.IsLoaded);
                Check(focusSibling.Focus(FocusState.Keyboard), "Focus-loss fixture moves to a real sibling control");
                await Until(() => !first.IsInteracting);
                Check(true, "Moving focus away stops automatic browser interaction");
                Slot(surface)!.Focus(FocusState.Keyboard);
                StartupContentStage.Children.Remove(focusSibling);
                await Until(() => first.IsInteracting);
                Check(true, "Returning focus resumes browsing without A");
                Check(((Microsoft.UI.Xaml.Controls.WebView2)first.CompositionRoot).Focus(FocusState.Keyboard),
                    "Interactive WebView accepts native keyboard focus");
                await Until(() => first.HasNativeKeyboardFocus);
                await first.EvaluateFixtureAsync("window.idleControllerMoves=0;document.addEventListener('mousemove',()=>window.idleControllerMoves++);");
                for (var poll = 0; poll < 8; poll++) first.MovePointer(0, 0);
                await Task.Delay(120);
                Check(first.HasNativeKeyboardFocus && await first.EvaluateFixtureAsync("window.idleControllerMoves") == "0",
                    "Neutral controller polling neither steals native keyboard focus nor sends mouse movement");
                first.MovePointer(.02, 0);
                await Until(() => !first.HasNativeKeyboardFocus);
                Check(first.IsBrowsing, "Actual controller movement returns ownership to the browser slot");
                var original = surface!.CurrentBinding!.Frame;
                Check(owner!.Session.ResolveWebBrowserDocument(original, "browser.page").NavigationId == 1,
                    "Long address crosses ordinary input and browser authority accepts the published document");
                surface.Apply(original);
                Check(first.NavigationCount == 1, "Compatible presentation does not navigate again");
                await PinAsync("browser", WidgetPinnedProjection.FullWidgetLayoutId);
                await Until(() => ReferenceEquals(Slot(pinned?.Presenter)?.Surface, first) && first.IsLoaded);
                var pinnedMoves = browserOwner.PlacementMoveCount;
                for (var cycle = 0; cycle < 3; ++cycle)
                {
                    await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                    await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                    await Until(() => !interactive);
                    await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                    await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                    await Until(() => interactive && !switching);
                    Check(ReferenceEquals(Slot(pinned?.Presenter)?.Surface, first) && Slot(surface)?.Surface is null &&
                        browserOwner.PlacementMoveCount == pinnedMoves, "Tray-to-widget entry keeps the page in its pin without transfers, cycle " + cycle);
                }
                await Until(() => surface!.CaptureControllerGuide().Any(hint => hint.Button == ControllerButton.View));
                Check(surface!.CaptureControllerGuide().Any(hint => hint.Button == ControllerButton.View) &&
                    !surface.CaptureControllerGuide().Any(hint => hint.Button == ControllerButton.A), "Main placeholder advertises pin interaction, not an unavailable page action");
                await SelectAsync("intent-source");
                await Until(() => ReferenceEquals(Slot(pinned?.Presenter)?.Surface, first) && first.IsLoaded);
                Check(pinned!.Window.IsVisible && !pinned.Window.Interactive && browserOwner.CreatedCount == 1,
                    "Passive pin receives the same native browser across XAML roots");
                Check(!first.IsInteracting, "A visible passive pin does not automatically acquire browser input");
                await Until(() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && AutomationProperties.GetAutomationId(focused) == "Widget.open");
                surface!.MoveFocus(FocusNavigationDirection.Down);
                await Until(() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && AutomationProperties.GetAutomationId(focused) == "Widget.wait");
                var focus = FocusManager.GetFocusedElement(XamlRoot);
                await InvokeAsync(new(surface!.CurrentBinding!.Frame, new("open", "open", ControllerButton.A, InputScopeId: "root")));
                await Until(() => first.CurrentTitle == "Fixture /guide");
                Check(activeWidget == "intent-source" && ReferenceEquals(focus, FocusManager.GetFocusedElement(XamlRoot)),
                    "Web intent navigates an existing pin without taking source focus");
                Check(first.NavigationCount == 2 && browserOwner.CreatedCount == 1, "Pinned navigation preserves its browser controller");
                var rejected = false;
                try { owner.Session.ResolveWebBrowserDocument(original, "browser.page"); }
                catch (WidgetPresentationSessionException) { rejected = true; }
                Check(rejected && owner.Session.IsWebBrowserSessionCurrent(original.Authority, "browser.primary"),
                    "Old navigation authority is rejected while the stable browser session remains current");
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await EnterPinnedAsync();
                await Until(() => PinnedInputActive);
                await Until(() => first.IsInteracting && first.IsBrowsing);
                Check(true, "Entering the pin starts focus-mode browsing without an extra A");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A);
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => first.CurrentTitle == "Fixture /clicked" && !first.IsNavigationInProgress);
                Check(true, "Controller A clicks a page link through host-owned pointer input");
                var frame = owner!.Session.GetState("browser")!.LastGood!;
                pinned.Presenter.ApplyPinned(pinned.Selection!, owner.Session.ResolvePinnedProjection(frame, pinned.LayoutId));
                Check(first.CurrentUrl == "https://example.com/clicked" && first.NavigationCount == 2,
                    "Widget refresh preserves browser-driven navigation and history");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.LeftBumper);
                await Until(() => first.CurrentTitle == "Fixture /guide" && !first.IsNavigationInProgress);
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.RightBumper);
                await Until(() => first.CurrentTitle == "Fixture /clicked" && !first.IsNavigationInProgress);
                Check(true, "Bumpers navigate history directly while interacting");
                first.MovePointer(.15, .12);
                await first.EvaluateFixtureAsync("window.zoomAnchor={x:visualViewport.pageLeft+visualViewport.width*.65,y:visualViewport.pageTop+visualViewport.height*.62};");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.RightTrigger);
                await UntilScript(first, "visualViewport.scale > 1.05");
                await Until(() => first.FixtureZoomIdle);
                Check(true, "Right trigger increases native page scale without moving controller focus");
                Check(await first.EvaluateFixtureAsync("Math.abs(zoomAnchor.x-(visualViewport.pageLeft+visualViewport.width*.65))<3 && Math.abs(zoomAnchor.y-(visualViewport.pageTop+visualViewport.height*.62))<3") == "true",
                    "Native pinch zoom preserves the point beneath an off-center controller pointer");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Repeated);
                await UntilScript(first, "visualViewport.scale > 1.15");
                await Until(() => first.FixtureZoomIdle);
                Check(await first.EvaluateFixtureAsync("Math.abs(zoomAnchor.x-(visualViewport.pageLeft+visualViewport.width*.65))<3 && Math.abs(zoomAnchor.y-(visualViewport.pageTop+visualViewport.height*.62))<3") == "true",
                    "Held-trigger zoom retains the off-center anchor on consecutive steps: " + await first.EvaluateFixtureAsync("JSON.stringify({anchor:zoomAnchor,x:visualViewport.pageLeft+visualViewport.width*.65,y:visualViewport.pageTop+visualViewport.height*.62,scale:visualViewport.scale})"));
                first.MovePointer(-.15, -.12);
                await first.EvaluateFixtureAsync("const a=document.querySelector('a'),v=visualViewport;a.style.left=(v.offsetLeft+v.width*.49)+'px';a.style.top=(v.offsetTop+v.height*.49)+'px';a.style.width=(v.width*.02)+'px';a.style.height=(v.height*.02)+'px';a.onclick=e=>{e.preventDefault();document.title='Zoom target clicked';};");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A);
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => first.CurrentTitle == "Zoom target clicked");
                Check(true, "Controller pointer clicks the visible center target while zoomed");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.LeftTrigger);
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.LeftTrigger, ControllerEventPhase.Repeated);
                await UntilScript(first, "Math.abs(visualViewport.scale-1)<.01");
                await Until(() => first.FixtureZoomIdle);
                Check(true, "Left trigger returns native page scale to 100 percent");
                await first.EvaluateFixtureAsync("document.body.innerHTML='<div style=\"position:fixed;inset:0\">Drag surface</div>';window.dragEvents=[];document.onpointerdown=e=>dragEvents.push('down');document.onpointermove=e=>{if(e.buttons===1)dragEvents.push('move-held')};document.onpointerup=e=>dragEvents.push('up');");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A);
                await UntilScript(first, "dragEvents.includes('down')");
                first.MovePointer(.05, 0);
                await UntilScript(first, "dragEvents.includes('move-held')");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await UntilScript(first, "dragEvents.includes('up')");
                Check(true, "Holding A preserves the mouse button during stick movement and releases on A-up");
                await first.EvaluateFixtureAsync("window.dragEvents=[];");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A);
                await UntilScript(first, "dragEvents.includes('down')");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.Y);
                await Until(() => first.FixtureEditor is { IsLoaded: true });
                await UntilScript(first, "dragEvents.includes('up')");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => !first.HasDialog);
                Check(PinnedInputActive, "Opening a dialog cancels a held pointer without leaking the A release");
                await first.EvaluateFixtureAsync("document.body.innerHTML='<div id=source draggable=true style=\"position:fixed;left:15%;top:35%;width:20%;height:30%\">Drag</div><div id=target style=\"position:fixed;left:60%;top:35%;width:30%;height:30%\">Drop</div>'; document.title='Drag ready'; document.getElementById('source').ondragstart=e=>{e.dataTransfer.setData('text/plain','synthetic');document.title='Drag started';}; document.getElementById('target').ondragover=e=>e.preventDefault(); document.getElementById('target').ondrop=e=>{e.preventDefault();document.title=e.dataTransfer.getData('text/plain')==='synthetic'?'Drop passed':'Drop failed';};");
                await first.EvaluateFixtureAsync("window.dragEvents=[];document.onpointerdown=e=>dragEvents.push('down:'+e.target.id);");
                first.MovePointer(-.3, 0);
                await Task.Delay(60);
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A);
                await UntilScript(first, "dragEvents.includes('down:source')");
                first.MovePointer(.1, 0);
                await Until(() => first.CurrentTitle == "Drag started");
                first.MovePointer(.35, 0);
                await Task.Delay(100);
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => first.CurrentTitle == "Drop passed");
                Check(true, "Holding A can drag an HTML item onto a webpage drop target");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.Y);
                await Until(() => first.FixtureEditor is { IsLoaded: true });
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => !first.HasDialog);
                Check(PinnedInputActive && first.IsBrowsing, "Dialog B cancels and returns directly to the still-interactive pin");
                await first.EvaluateFixtureAsync("document.body.innerHTML='<input id=autoField style=\"position:fixed;inset:0;width:100%;height:100%;box-sizing:border-box\" value=original>';document.getElementById('autoField').oninput=e=>document.title=e.target.value;");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A);
                await UntilScript(first, "document.activeElement.id==='autoField'");
                Check(!first.HasDialog, "Holding A over a text field does not open the keyboard before release");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => first.FixtureEditor is { IsLoaded: true });
                EditValue(first, "Automatic edit");
                await RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Released);
                await Until(() => !first.HasDialog && first.CurrentTitle == "Automatic edit");
                Check(PinnedInputActive && first.IsBrowsing, "Completed controller click opens the existing exact-field keyboard and commits once");
                await first.EvaluateFixtureAsync("document.getElementById('autoField').focus();const b=document.createElement('button');b.style='position:fixed;inset:0;width:100%;height:100%';b.onmousedown=e=>e.preventDefault();b.onclick=()=>document.title='Unrelated button';document.body.append(b);");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A);
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => first.CurrentTitle == "Unrelated button");
                await Task.Delay(150);
                Check(!first.HasDialog, "Clicking elsewhere never opens the keyboard for an older focused field");
                await first.EvaluateFixtureAsync("document.body.innerHTML='<textarea id=dragField style=\"position:fixed;inset:0;width:100%;height:100%\">Select this text</textarea>';");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A);
                await UntilScript(first, "document.activeElement.id==='dragField'");
                first.MovePointer(.04, 0);
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                await Task.Delay(150);
                Check(!first.HasDialog, "Controller drag or text selection does not automatically open a keyboard");
                await first.EvaluateFixtureAsync("document.body.innerHTML = '<input id=field value=original oninput=\"document.title=this.value\">'; document.getElementById('field').focus();");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.RightStick);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                Check(!first.HasDialog && PinnedInputActive, "B can cancel text-field preparation before the dialog opens");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.RightStick);
                await Until(() => first.FixtureEditor is { IsLoaded: true });
                EditValue(first, "Typed safely");
                await RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Released);
                await Until(() => !first.HasDialog && first.CurrentTitle == "Typed safely");
                Check(PinnedInputActive && first.IsBrowsing, "R3 edits the captured page field and RT commits without leaving interaction");
                await first.EvaluateFixtureAsync("document.getElementById('field').focus();");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.RightStick);
                await Until(() => first.FixtureEditor is { IsLoaded: true });
                await first.EvaluateFixtureAsync("document.getElementById('field').replaceWith(document.createElement('input')); document.querySelector('input').focus();");
                EditValue(first, "Wrong field");
                await RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Released);
                await Until(() => !first.HasDialog);
                Check(await first.EvaluateFixtureAsync("document.querySelector('input').value") == "\"\"", "Replacing the selected DOM field rejects a stale text commit");
                await first.EvaluateFixtureAsync("document.querySelector('input').focus();");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.RightStick);
                await Until(() => first.FixtureEditor is { IsLoaded: true });
                await first.EvaluateFixtureAsync("location.href='https://example.com/navigation';");
                await Until(() => !first.HasDialog && first.CurrentTitle == "Fixture /navigation" && !first.IsNavigationInProgress);
                Check(PinnedInputActive, "Page navigation cancels a pending edit without leaving the pin");
                await pinned!.Presenter!.HandleControllerButtonAsync(ControllerButton.Y);
                await Until(() => first.FixtureEditor is { IsLoaded: true });
                EditValue(first, "https://example.com/address");
                await RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.RightTrigger, ControllerEventPhase.Released);
                await Until(() => first.CurrentTitle == "Fixture /address" && !first.IsNavigationInProgress);
                Check(first.NavigationCount == 2 && first.IsBrowsing, "Y edits the native address without reissuing an authored navigation");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => foreground && !PinnedInputActive);
                Check(true, "One B passes through to the host and leaves pinned interaction");
                await SelectAsync("browser");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => !interactive);
                var detaching = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var attach = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                browserOwner.BeforeAttachForValidation = async () => { detaching.TrySetResult(); await attach.Task; };
                var unpin = UnpinAsync(save: true);
                try
                {
                    await detaching.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await Task.Delay(50);
                    Check(!unpin.IsCompleted, "Unpin from Browser tray waits for native transfer before destroying the pin window");
                }
                finally { browserOwner.BeforeAttachForValidation = null; attach.TrySetResult(); }
                await unpin;
                await SelectAsync("browser");
                await Until(() => ReferenceEquals(Slot(surface)?.Surface, first));
                Check(first.CurrentTitle == "Fixture /address" && browserOwner.CreatedCount == 1,
                    "Returning to the main widget preserves the page and controller");
                Slot(surface)!.Focus(FocusState.Keyboard); surface!.ActivateFocused();
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                Check(!interactive, "One B from main browser interaction opens the ordinary tray");
                await SelectAsync("browser");
                first.CloseControllerForValidation();
                surface!.SetPresentationInputEnabled(false);
                surface.SetPresentationInputEnabled(true);
                Check(!first.IsReady, "A closed WebView becomes a local browser failure rather than a shell exception");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                Check(!interactive, "Back remains usable after a native browser failure");
                SetVisible(false); SetVisible(true);
                await SelectAsync("intent-source");
                Check(interactive && activeWidget == "intent-source", "Hide show and switching widgets remain usable after a native browser failure");
                await owner.Session.RestartAsync(owner.Session.GetTarget("browser"));
                await Until(() => browserOwner.ResidentCount == 0);
                Check(true, "Replacing the widget worker retires the old browser document");
                Check(!owner.Session.IsWebBrowserSessionCurrent(original.Authority, "browser.primary"),
                    "Retired worker cannot retain browser authority");
                await SelectAsync("browser-modes");
                await Until(() => Slot(surface)?.Surface is { IsReady: true, IsNavigationInProgress: false });
                await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Slot(surface)));
                var explicitBrowser = Slot(surface)!.Surface!;
                Check(!explicitBrowser.IsInteracting, "Activation mode stays selected without interacting on initial focus");
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
                Check(explicitBrowser.IsInteracting && explicitBrowser.NavigationCount == 1,
                    "Activation-mode A enters without clicking the page");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Repeated);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                Check(interactive && !explicitBrowser.IsInteracting,
                    "Activation-mode B exits interaction and consumes its full gesture without opening the tray");
                surface!.MoveFocus(FocusNavigationDirection.Down);
                Check(AutomationProperties.GetAutomationId((DependencyObject)FocusManager.GetFocusedElement(XamlRoot)) == "Widget.sibling",
                    "After leaving interaction the controller can navigate to sibling controls");
                Slot(surface)!.Focus(FocusState.Keyboard);
                Check(!explicitBrowser.IsInteracting, "Refocusing an activation-mode browser still requires A");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                Check(!interactive, "B on a selected inactive browser follows normal host routing");
                if (Environment.GetCommandLineArgs().Contains("--browser-preview"))
                {
                    await SelectAsync("browser");
                    await Until(() => interactive && !switching && surface?.CurrentBinding is not null);
                    await Until(() => Slot(surface)?.Surface is { IsReady: true, IsNavigationInProgress: false });
                    Slot(surface)!.Surface!.NavigateForValidation("https://example.com/preview");
                    await Until(() => Slot(surface)?.Surface is { IsReady: true, IsNavigationInProgress: false });
                    Slot(surface)!.Focus(FocusState.Keyboard); surface!.ActivateFocused();
                }
            }
            catch (Exception failure) { error = failure.ToString(); }
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { pid = Environment.ProcessId, passed = error is null, checks, error }));
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        };
        static void EditValue(BrowserSurface browser, string value)
        {
            var content = (Microsoft.UI.Xaml.Controls.ScrollViewer)browser.FixtureEditor!.Content;
            var body = (Microsoft.UI.Xaml.Controls.StackPanel)content.Content;
            ((Microsoft.UI.Xaml.Controls.TextBox)body.Children[0]).Text = value;
        }
        static async Task UntilScript(BrowserSurface browser, string condition)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (await browser.EvaluateFixtureAsync(condition) != "true")
            {
                if (Environment.TickCount64 >= deadline) throw new TimeoutException("Browser condition: " + condition);
                await Task.Delay(25);
            }
        }
        static BrowserSlot? Slot(DependencyObject? parent)
        {
            if (parent is null) return null;
            if (parent is BrowserSlot slot) return slot;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (Slot(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
            return null;
        }
        static async Task Until(Func<bool> ready)
        {
            var end = Environment.TickCount64 + 20_000;
            while (!ready()) { if (Environment.TickCount64 >= end) throw new TimeoutException("Browser fixture condition timed out."); await Task.Delay(20); }
        }
    }
}
