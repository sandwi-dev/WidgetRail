using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI;

internal static partial class Program
{
    static partial void IsValidationLaunch(IReadOnlyList<string> arguments, ref bool fixture)
        => fixture = arguments.Contains("--validate-trimmed-slider");
}

public sealed partial class MainWindow
{
    partial void ConfigureValidation(IReadOnlyList<string> arguments, ref bool handled)
    {
        if (!arguments.Contains("--validate-trimmed-slider")) return;
        handled = true;
        var result = Shell.FrontendArguments.Value(arguments, "--result")
            ?? throw new ArgumentException("Trimmed slider validation requires --result.");
        var panel = new StackPanel();
        RootFrame.Content = panel;
        panel.Loaded += async (_, _) =>
        {
            try
            {
                // A fractional range and nonzero initial volume reproduce the native
                // RangeBase callback used by Audio Mixer without touching a provider.
                for (var pass = 0; pass < 3; pass++)
                {
                    var slider = new WidgetSlider();
                    var changes = 0;
                    slider.ValueChanged += (_, _) => changes++;
                    slider.Maximum = 1;
                    slider.StepFrequency = 0.01;
                    slider.Value = 0.75;
                    panel.Children.Add(slider);
                    await Task.Delay(100);
                    slider.ApplyTemplate();
                    slider.Value = 0;
                    slider.Value = 1;
                    slider.Maximum = 0.5;
                    if (changes < 4 || slider.Value != 0.5)
                        throw new InvalidOperationException("Native range coercion or ValueChanged callback failed.");
                    panel.Children.Clear();
                }
                var button = new WidgetValueButton { Content = "Synthetic value" };
                button.SetAccessibleValue("42", false);
                panel.Children.Add(button);
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
                var provider = peer?.GetPattern(PatternInterface.Value) as IValueProvider;
                if (provider?.Value != "42") throw new InvalidOperationException("Native value-button accessibility projection failed.");
                var artwork = new WidgetArtworkView();
                panel.Children.Add(artwork);
                artwork.Measure(new(160, 90));
                artwork.Arrange(new(0, 0, 160, 90));
                if (FrameworkElementAutomationPeer.CreatePeerForElement(artwork)?.GetAutomationControlType() != AutomationControlType.Image)
                    throw new InvalidOperationException("Native artwork accessibility projection failed.");
                panel.Children.Clear();
                BrowserSurface.FixturePage = url => "<!doctype html><title>" + new Uri(url).AbsolutePath +
                    "</title><a style='position:fixed;inset:0;display:block' href='https://example.com/clicked'>Synthetic guide</a>";
                await using var browser = new BrowserSurface("trim.browser", new("browser.document", 1, "https://example.com/first", "Browser") { InteractionMode = BrowserInteractionMode.ActivateToInteract },
                    () => CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(Path.GetDirectoryName(result)!, "web-profile"), null).AsTask())
                    { Width = 640, Height = 400 };
                // Exercise the same focus owner as the production presenter.
                var browserSlot = new BrowserSlot { Width = 640, Height = 400, IsTabStop = true, Active = true, AcceptsInput = true };
                browserSlot.Host.Children.Add(browser); browserSlot.SetSurface(browser);
                panel.Children.Add(browserSlot);
                browser.SetPresentation(true, true);
                await Until(() => browser.IsReady && !browser.IsNavigationInProgress && browser.CurrentTitle == "/first");
                await browser.EvaluateFixtureAsync("window.hitReady=false;window.events=[];for(const type of ['pointerdown','pointerup','click'])document.addEventListener(type,e=>events.push({type,x:e.clientX,y:e.clientY,target:e.target.tagName}));requestAnimationFrame(()=>requestAnimationFrame(()=>window.hitReady=true));");
                await UntilScript("window.hitReady");
                browser.Enter();
                if (!browser.IsBrowsing) throw new InvalidOperationException("Trimmed browser did not enter direct interaction.");
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed);
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released);
                await Task.Delay(300);
                File.WriteAllText(result + ".pointer.json", string.Join("\n", browser.FixturePointerTrace) + "\n" +
                    await browser.EvaluateFixtureAsync("JSON.stringify({url:location.href,events:window.events,width:innerWidth,height:innerHeight})"));
                await Until(() => browser.CurrentTitle == "/clicked");
                await Until(() => !browser.IsNavigationInProgress);
                browser.HandleButton(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
                await UntilScript("visualViewport.scale > 1.05");
                await Until(() => browser.FixtureZoomIdle);
                browser.HandleButton(ControllerButton.LeftTrigger, ControllerEventPhase.Pressed);
                await UntilScript("Math.abs(visualViewport.scale-1)<.01");
                await Until(() => browser.FixtureZoomIdle);
                await browser.EvaluateFixtureAsync("document.body.innerHTML='<div id=source draggable=true style=\"position:fixed;left:15%;top:35%;width:20%;height:30%\">Drag</div><div id=target style=\"position:fixed;left:60%;top:35%;width:30%;height:30%\">Drop</div>'; document.getElementById('source').onpointerdown=e=>document.title='trim-down'; document.getElementById('source').ondragstart=e=>{e.dataTransfer.setData('text/plain','trim');document.title='trim-drag';}; document.getElementById('target').ondragover=e=>{e.preventDefault();document.title='trim-over';}; document.getElementById('target').ondrop=e=>{e.preventDefault();document.title=e.dataTransfer.getData('text/plain')==='trim'?'trim-drop':'trim-fail';};");
                browser.MovePointer(-.25, 0);
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed);
                await Until(() => browser.CurrentTitle == "trim-down");
                browser.MovePointer(.1, 0);
                await Until(() => browser.CurrentTitle == "trim-drag");
                browser.MovePointer(.35, 0);
                // Release is part of the gesture. Validate the completed drop,
                // without requiring an intermediate drag-over/title notification.
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => browser.CurrentTitle == "trim-drop");
                await browser.EvaluateFixtureAsync("document.body.innerHTML='<input style=\"position:fixed;inset:0;width:100%;height:100%\" value=original oninput=\"document.title=this.value\">';");
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed);
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => browser.FixtureEditor is { IsLoaded: true });
                var automaticContent = (ScrollViewer)browser.FixtureEditor!.Content;
                ((TextBox)((StackPanel)automaticContent.Content).Children[0]).Text = "trim-auto edit";
                browser.HandleButton(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
                await Until(() => !browser.HasDialog && browser.CurrentTitle == "trim-auto edit");
                await browser.EvaluateFixtureAsync("document.body.innerHTML='<input value=original oninput=\"document.title=this.value\">'; document.querySelector('input').focus();");
                browser.HandleButton(ControllerButton.RightStick, ControllerEventPhase.Pressed);
                await Until(() => browser.FixtureEditor is { IsLoaded: true });
                var editContent = (ScrollViewer)browser.FixtureEditor!.Content;
                ((TextBox)((StackPanel)editContent.Content).Children[0]).Text = "trim-safe edit";
                browser.HandleButton(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
                await Until(() => !browser.HasDialog && browser.CurrentTitle == "trim-safe edit");
                browser.HandleButton(ControllerButton.B, ControllerEventPhase.Pressed);
                if (browser.IsBrowsing || browser.IsInteracting) throw new InvalidOperationException("Embedded browser did not leave interaction.");
                // Retire the borrowed native surface before replacing its XAML
                // slot, as BrowserOwner does in production. Removing a child
                // alone does not complete native-controller/focus teardown.
                browser.SetPresentation(false, false);
                browserSlot.Retire();
                await browser.DisposeAsync();
                if (Shell.FrontendArguments.Value(arguments, "--capture-fixtures") is { } captureFixtures)
                {
                    panel.Children.Clear();
                    using var media = new Media.MediaPlayerView(); panel.Children.Add(media);
                    await media.ValidateCapturePlaybackAsync(Path.Combine(captureFixtures, "synthetic.png"), Path.Combine(captureFixtures, "synthetic.mp4"), []);
                }
                panel.Children.Clear();
                var providerReference = new ProviderDocumentReference(new string('a', 32), ProviderDocumentReference.RestrictedHtml);
                var providerResponse = new WidgetRail.WidgetBridge.BridgeProviderDocument(providerReference,
                    (string[])["<script>window.untrustedScript=true</script><a style='position:fixed;inset:0;display:grid;place-items:center' href='https://www.google.com/search?q=fixture'>Original search suggestion</a>"]);
                var providerDocument = new WebBrowserDocument("provider.trim", 1, providerReference.Url, "Search suggestions")
                    { InteractionMode = BrowserInteractionMode.ActivateToInteract, ProviderDocument = providerReference };
                await using var attribution = new BrowserSurface("trim.provider", providerDocument,
                    () => CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(Path.GetDirectoryName(result)!, "provider-profile"), null).AsTask(), providerResponse.Html)
                    { Width = 640, Height = 180, CheckProviderAuthority = _ => Task.CompletedTask };
                var attributionSlot = new BrowserSlot { Width = 640, Height = 180, MinHeight = 120, Active = true, AcceptsInput = true, IsProviderDocument = true, IsTabStop = true };
                attributionSlot.Host.Children.Add(attribution); attributionSlot.SetSurface(attribution); panel.Children.Add(attributionSlot);
                attribution.SetPresentation(true, true);
                await Until(() => attribution.IsReady && !attribution.IsNavigationInProgress && attribution.CurrentTitle == "Document");
                if (await attribution.EvaluateFixtureAsync("typeof window.untrustedScript === 'undefined'") != "true")
                    throw new InvalidOperationException("Provider scripts executed in trimmed Release.");
                string? providerLink = null;
                attribution.OpenExternal = (uri, _) => { providerLink = uri.AbsoluteUri; return Task.FromResult(true); };
                await Until(() => attributionSlot.CanDisplay);
                if (!attributionSlot.Focus(FocusState.Keyboard)) throw new InvalidOperationException("Provider slot could not receive focus.");
                attribution.Enter();
                if (!attribution.IsBrowsing) throw new InvalidOperationException("Provider interaction was not admitted.");
                attribution.HandleButton(ControllerButton.Y, ControllerEventPhase.Pressed);
                await Task.Delay(50);
                if (attribution.HasDialog) throw new InvalidOperationException("Provider document exposed an address editor.");
                await attribution.EvaluateFixtureAsync("new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))");
                await attribution.EvaluateFixtureAsync("window.events=[];for(const type of ['pointerdown','pointerup','click'])document.addEventListener(type,e=>events.push({type,x:e.clientX,y:e.clientY,target:e.target.tagName}));");
                attribution.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed);
                attribution.HandleButton(ControllerButton.A, ControllerEventPhase.Released);
                await Task.Delay(250);
                File.WriteAllLines(result + ".provider-trace.txt", new[] { "url=" + attribution.CurrentUrl, "opened=" + providerLink,
                    "interacting=" + attribution.IsInteracting, "ready=" + attribution.IsReady,
                    "pointer=" + string.Join(";", attribution.FixturePointerTrace),
                    await attribution.EvaluateFixtureAsync("JSON.stringify({width:innerWidth,height:innerHeight,events,anchor:document.querySelector('a').getBoundingClientRect().toJSON()})") });
                await Until(() => providerLink is not null);
                if (providerLink != "https://www.google.com/search?q=fixture" || attribution.CurrentUrl != providerDocument.Url)
                    throw new InvalidOperationException("Provider link changed its destination.");
                attribution.HandleButton(ControllerButton.B, ControllerEventPhase.Pressed);
                attribution.HandleButton(ControllerButton.B, ControllerEventPhase.Released);
                if (attribution.IsInteracting) throw new InvalidOperationException("Provider interaction did not exit.");
                File.WriteAllText(result, "PASS: slider/value-button/artwork projections; lazy browser; mouse click; native zoom in/out; HTML drag-and-drop; automatic A-click keyboard and exact-field R3 dialog with generated JSON; embedded Back; provided capture fixture native playback; restricted provider documents, generated JSON, controller links and Back in trimmed Release.");
                async Task UntilScript(string condition)
                {
                    var deadline = Environment.TickCount64 + 10000;
                    while (await browser.EvaluateFixtureAsync(condition) != "true")
                    {
                        if (Environment.TickCount64 >= deadline) throw new TimeoutException("Trimmed browser: " + condition);
                        await Task.Delay(25);
                    }
                }
            }
            catch (Exception error) { File.WriteAllText(result, "FAIL: " + error); }
            finally { Close(); }
        };
        static async Task Until(Func<bool> ready)
        {
            var deadline = Environment.TickCount64 + 30_000;
            while (!ready())
            {
                if (Environment.TickCount64 >= deadline) throw new TimeoutException("Trimmed browser startup/click timed out.");
                await Task.Delay(25);
            }
        }
    }
}
