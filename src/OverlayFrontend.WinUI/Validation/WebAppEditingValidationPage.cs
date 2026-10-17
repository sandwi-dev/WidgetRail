using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WebAppEditingValidationPage : Page, IAsyncDisposable
{
    private readonly BrowserSurface browser;
    private readonly NativePopupTheme theme = new();
    private const string Html = """
        <!doctype html><title>Editor fixture</title>
        <style>body{background:#222;color:white;font:20px sans-serif}input,textarea,[contenteditable]{display:block;margin:12px;padding:12px;border:1px solid white}</style>
        <input id=plain value=original><input id=password type=password value=synthetic-existing>
        <textarea id=area>original</textarea>
        <div id=rich contenteditable=true><b id=bold>Bold</b><span id=mention contenteditable=false>@friend</span><span id=tail>tail</span></div>
        <script>
        window.enters=0;window.nativeInputs=0;window.beforeInputs=0;
        document.addEventListener('keydown',e=>{if(e.key==='Enter'){window.enters++;e.preventDefault();}});
        document.addEventListener('input',e=>{if(e.isTrusted)window.nativeInputs++;});
        document.addEventListener('beforeinput',e=>{if(e.isTrusted)window.beforeInputs++;});
        window.originalBold=document.getElementById('bold');window.originalMention=document.getElementById('mention');
        </script>
        """;

    internal WebAppEditingValidationPage(string result, string profileRoot)
    {
        theme.Attach(this); theme.Update(null, AppearanceSettings.Default);
        Task<CoreWebView2Environment>? environment = null;
        BrowserSurface.FixturePage = url => new Uri(url).AbsolutePath == "/field"
            ? "<!doctype html><style>body{margin:0}input{box-sizing:border-box;width:100%;height:80px}</style><input id=inside value=synthetic-frame><script>document.documentElement.dataset.inputs='0';document.addEventListener('input',e=>{if(e.isTrusted)document.documentElement.dataset.inputs=String(Number(document.documentElement.dataset.inputs)+1)});document.addEventListener('mousedown',e=>document.documentElement.dataset.pointer=e.target.tagName+':'+e.clientX+','+e.clientY);</script>"
            : Html;
        browser = new("fixture.editor", new("editor", 1, "https://editor.fixture.invalid/", "Editor fixture")
        { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp },
            () => environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profileRoot, null).AsTask())
        { IsInteractionFocused = () => true };
        if (Environment.GetCommandLineArgs().Contains("--webapp-editor-small"))
        { browser.Width = 400; browser.Height = 320; }
        Content = new Grid { Margin = new(24), Children = { browser } };
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null;
            try
            {
                browser.SetPresentation(true, true);
                await Until(() => browser.IsReady && !browser.IsNavigationInProgress && browser.CurrentTitle == "Editor fixture");
                browser.Enter();
                if (Environment.GetCommandLineArgs().Contains("--webapp-frame-probe") || Environment.GetCommandLineArgs().Contains("--webapp-frame-editing"))
                {
                    var frameUrl = Environment.GetCommandLineArgs().Contains("--webapp-frame-same-origin") ? "https://editor.fixture.invalid/field" : "https://other-editor.invalid/field";
                    await browser.EvaluateFixtureAsync("window.frameReady=false;window.frame=document.createElement('iframe');frame.id='frame';frame.style='display:block;width:300px;height:120px;border:0';frame.onload=()=>frameReady=true;frame.src=" + JsonSerializer.Serialize(frameUrl) + ";document.body.append(frame)");
                    var deadline = Environment.TickCount64 + 10_000;
                    while (await browser.EvaluateFixtureAsync("frameReady") != "true")
                    { if (Environment.TickCount64 > deadline) throw new TimeoutException("Synthetic frame did not load."); await Task.Delay(30); }
                    if (Environment.GetCommandLineArgs().Contains("--webapp-frame-scaled"))
                        await browser.EvaluateFixtureAsync("frame.style.transform='scale(.8)';frame.style.transformOrigin='top left';frame.style.border='4px solid white'");
                    if (Environment.GetCommandLineArgs().Contains("--webapp-frame-editing"))
                    {
                        await browser.ClickFrameFieldForValidationAsync();
                        await Until(() => browser.FixtureEditor is { IsLoaded: true });
                        var frameEditor = browser.FixtureEditor!;
                        frameEditor.HandleKeyboardKey(Windows.System.VirtualKey.A, control: true);
                        frameEditor.InsertForValidation("live cross-origin edit");
                        await frameEditor.WaitForLiveInputAsync();
                        using var state = JsonDocument.Parse(await browser.ReadLiveFrameForValidationAsync());
                        var value = state.RootElement.GetProperty("result").GetProperty("value");
                        if (value.GetProperty("value").GetString() != "live cross-origin edit" || value.GetProperty("inputs").GetString() == "0")
                            throw new InvalidOperationException("Cross-origin native editing failed: " + state.RootElement);
                        await browser.EvaluateFixtureAsync("frameReady=false;frame.src=frame.src+'?again=1'");
                        deadline = Environment.TickCount64 + 10_000;
                        while (await browser.EvaluateFixtureAsync("frameReady") != "true")
                        { if (Environment.TickCount64 > deadline) throw new TimeoutException("Synthetic frame did not reload."); await Task.Delay(30); }
                        frameEditor.InsertForValidation("must not reach replacement");
                        await frameEditor.WaitForLiveInputAsync();
                        browser.HandleButton(ControllerButton.B, ControllerEventPhase.Pressed);
                        browser.HandleButton(ControllerButton.B, ControllerEventPhase.Released);
                        await Until(() => browser.FixtureEditor is null);
                        await browser.ClickFrameFieldForValidationAsync();
                        await Until(() => browser.FixtureEditor is { IsLoaded: true });
                        if (browser.FixtureEditor!.TakeValue() != "synthetic-frame") throw new InvalidOperationException("Old frame edit reached the replacement document.");
                        browser.HandleButton(ControllerButton.B, ControllerEventPhase.Pressed);
                        browser.HandleButton(ControllerButton.B, ControllerEventPhase.Released);
                        await Until(() => browser.FixtureEditor is null);
                        await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = true, kind = "frame-editing", frameUrl, state = value, staleFrameRejected = true, reopened = true }));
                        await DisposeAsync(); return;
                    }
                    var evidence = await browser.ProbeFrameNodeForValidationAsync();
                    await File.WriteAllTextAsync(result, evidence);
                    await DisposeAsync();
                    return;
                }
                var viewportBefore = browser.CompositionRoot.ActualHeight;
                var dialog = await Open("plain");
                await Task.Delay(250);
                if (Environment.GetCommandLineArgs().Contains("--webapp-keyboard-native-probe"))
                {
                    await Task.Delay(500);
                    var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(dialog.XamlRoot) as Control;
                    var q = FindElement(dialog, "Widget.TextEntry.Key.10") as Button;
                    var panel = NativeDialogPresentation.FindPart<FrameworkElement>(dialog, "BackgroundElement");
                    await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { ready = true, focused = focused?.GetType().Name,
                        focusId = focused is null ? null : AutomationProperties.GetAutomationId(focused), qFocus = q?.FocusState.ToString(),
                        dialog.IsFloating, panelWidth = panel?.ActualWidth, panelHeight = panel?.ActualHeight,
                        dialogWidth = dialog.ActualWidth, dialogHeight = dialog.ActualHeight }));
                    return;
                }
                Check(Math.Abs(browser.CompositionRoot.ActualHeight - viewportBefore) < 1,
                    "Floating live keyboard preserves the rendered WebView height");
                Check(dialog.AllKeysVisibleForValidation && dialog.Content is ScrollViewer { ScrollableHeight: 0 },
                    "Every key and action is visible without scrolling the keyboard");
                Check(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(dialog.XamlRoot) is Button { FocusState: FocusState.Keyboard, UseSystemFocusVisuals: true } focusedKey &&
                    AutomationProperties.GetAutomationId(focusedKey) == "Widget.TextEntry.Key.10",
                    "Live keyboard starts with keyboard-visible focus on Q");
                var originalClipboard = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                var clipboardBackup = new Windows.ApplicationModel.DataTransfer.DataPackage();
                foreach (var format in originalClipboard.AvailableFormats) clipboardBackup.SetData(format, await originalClipboard.GetDataAsync(format));
                var clipboardOptions = new Windows.ApplicationModel.DataTransfer.ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false };
                try
                {
                    var fixtureClipboard = new Windows.ApplicationModel.DataTransfer.DataPackage(); fixtureClipboard.SetText("paste-fixture");
                    if (!Windows.ApplicationModel.DataTransfer.Clipboard.SetContentWithOptions(fixtureClipboard, clipboardOptions)) throw new InvalidOperationException("Fixture clipboard unavailable");
                    var pasteKey = (Button)FindElement(dialog, "Widget.TextEntry.Paste")!;
                    pasteKey.Focus(FocusState.Keyboard); dialog.Activate(); await dialog.WaitForLiveInputAsync();
                    Check(await Eval("document.getElementById('plain').value==='originalpaste-fixture' && window.enters===0"), "Live Paste key inserts clipboard text through native browser editing without submitting");
                    Check(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(dialog.XamlRoot), pasteKey), "Live Paste retains controller focus");
                    dialog.HandleKeyboardKey(Windows.System.VirtualKey.A, control: true); dialog.InsertForValidation("original"); await dialog.WaitForLiveInputAsync();
                    ((Button)FindElement(dialog, "Widget.TextEntry.Key.10")!).Focus(FocusState.Keyboard);
                }
                finally
                {
                    if (!Windows.ApplicationModel.DataTransfer.Clipboard.SetContentWithOptions(clipboardBackup, clipboardOptions)) throw new InvalidOperationException("Clipboard restoration failed");
                }
                var initialAnchor = dialog.FieldAnchor!.Value;
                await browser.EvaluateFixtureAsync("document.getElementById('plain').style.marginLeft='100px'");
                await Task.Delay(400);
                Check(dialog.FieldAnchor!.Value.X > initialAnchor.X + 30, "Floating keyboard follows rendered field movement");
                await browser.EvaluateFixtureAsync("document.getElementById('plain').style.marginLeft='12px'");
                dialog.HandleKeyboardKey(Windows.System.VirtualKey.Space);
                await dialog.WaitForLiveInputAsync();
                Check(await Eval("document.getElementById('plain').value==='original '"), "Physical Space inserts one space while Q has focus");
                if (Environment.GetCommandLineArgs().Contains("--webapp-live-layout-review"))
                {
                    await Task.Delay(200);
                    await CaptureDialogAsync(dialog, Path.ChangeExtension(result, ".png"));
                    dialog.RestoreFocus();
                    await Task.Delay(100);
                    var focusSteps = new List<string>();
                    for (var i = 0; i < 5; i++)
                    {
                        dialog.MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Down); await Task.Delay(100);
                        focusSteps.Add(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(dialog.XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : "none");
                    }
                    var submit = FindElement(dialog, "Widget.TextEntry.Submit")!;
                    var footer = submit.TransformToVisual(dialog).TransformBounds(new(0, 0, submit.ActualWidth, submit.ActualHeight));
                    await CaptureDialogAsync(dialog, Path.ChangeExtension(result, ".footer.png"));
                    await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { ready = true, kind = "layout-review",
                        browserHeight = browser.ActualHeight, keyboardHeight = dialog.ActualHeight, viewportHeight = browser.CompositionRoot.ActualHeight,
                        focusSteps, footerVisible = footer.Top >= 0 && footer.Bottom <= dialog.ActualHeight && footer.Left >= 0 && footer.Right <= dialog.ActualWidth }));
                    return;
                }
                dialog.HandleKeyboardKey(Windows.System.VirtualKey.A, control: true);
                dialog.InsertForValidation("changed");
                await dialog.WaitForLiveInputAsync();
                Check(await Eval("document.getElementById('plain').value==='changed' && window.enters===0"), "Live text reaches the field before the keyboard closes, without submitting");
                Check(await Eval("window.nativeInputs>0 && window.beforeInputs>0"), "Browser insertion emits trusted beforeinput/input events");
                await Close(dialog);
                Check(await Eval("document.getElementById('plain').value==='changed'"), "B closes the keyboard and retains live text");
                dialog = await Open("plain");
                Check(dialog.TakeValue() == "changed", "A reopens the same already-focused field without R3");
                dialog.HandleKeyboardKey(Windows.System.VirtualKey.End);
                dialog.HandleKeyboardKey(Windows.System.VirtualKey.Back);
                dialog.InsertForValidation("!");
                await dialog.WaitForLiveInputAsync();
                Check(await Eval("document.getElementById('plain').value==='change!'"), "Caret and backspace edit the browser's own text");
                await Close(dialog);
                dialog = await Open("area");
                dialog.HandleKeyboardKey(Windows.System.VirtualKey.A, control: true);
                dialog.InsertForValidation("first\nsecond");
                await dialog.WaitForLiveInputAsync();
                Check(await Eval("document.getElementById('area').value==='first\\nsecond' && window.enters===0"), "Live multiline insertion preserves newlines without sending Enter");
                await Close(dialog);
                dialog = await Open("password");
                Check(!dialog.TakeValue().Contains("synthetic", StringComparison.Ordinal) && dialog.TakeValue().All(c => c == '●'), "Password preview contains masks only, never the existing secret");
                dialog.HandleKeyboardKey(Windows.System.VirtualKey.A, control: true);
                dialog.InsertForValidation("synthetic-replacement");
                await dialog.WaitForLiveInputAsync();
                Check(await Eval("document.getElementById('password').value==='synthetic-replacement'") && dialog.TakeValue().All(c=>c=='●'), "Live password input reaches the page without echoing secrets into the keyboard");
                await Close(dialog);
                dialog = await Open("tail");
                dialog.HandleKeyboardKey(Windows.System.VirtualKey.End);
                dialog.InsertForValidation(" added\nnext");
                await dialog.WaitForLiveInputAsync();
                Check(await Eval("document.getElementById('rich').innerText.includes('added') && document.getElementById('bold')===originalBold && document.getElementById('mention')===originalMention"), "Live rich insertion preserves existing formatting and mention nodes");
                await Close(dialog);
                dialog = await Open("plain");
                await browser.EvaluateFixtureAsync("document.getElementById('plain').remove()");
                await Until(() => browser.FixtureEditor is null);
                Check(true, "Removing the edited field automatically closes its keyboard");
                await browser.EvaluateFixtureAsync("document.body.insertAdjacentHTML('afterbegin','<input id=plain value=changed>')");
                dialog = await Open("plain");
                dialog.HandleKeyboardKey(Windows.System.VirtualKey.Enter);
                await dialog.WaitForLiveInputAsync();
                Check(await Eval("window.enters===1"), "An explicit Enter reaches the website");
                await browser.EvaluateFixtureAsync("document.getElementById('area').focus()");
                dialog.InsertForValidation("wrong target");
                await dialog.WaitForLiveInputAsync();
                Check(await Eval("!document.getElementById('area').value.includes('wrong target')"), "A changed browser focus cannot redirect queued text to another field");
                await Close(dialog);
                dialog = await Open("plain");
                browser.NavigateForValidation("https://editor.fixture.invalid/next");
                await Until(() => browser.FixtureEditor is null);
                Check(true, "Navigation retires the keyboard and its exact-field input destination");
                await Until(() => !browser.IsNavigationInProgress && browser.IsReady);
                browser.Enter();
                await browser.EvaluateFixtureAsync("window.shadow=document.createElement('div');shadow.id='shadow';document.body.prepend(shadow);shadow.attachShadow({mode:'open'}).innerHTML='<input value=shadow style=height:40px>'");
                dialog = await Open("shadow");
                dialog.HandleKeyboardKey(Windows.System.VirtualKey.A, control: true);
                dialog.InsertForValidation("shadow edit"); await dialog.WaitForLiveInputAsync();
                Check(await Eval("shadow.shadowRoot.querySelector('input').value==='shadow edit'"), "Floating keyboard edits the exact field in an open shadow root");
                await Close(dialog);
                browser.ZoomForEditingValidation(.5);
                await Until(() => browser.FixtureZoomIdle);
                dialog = await Open("plain");
                await Task.Delay(250);
                Check(dialog.FieldAnchor is { Width: > 0, Height: > 0 }, "Zoomed webpage retains a rendered field anchor");
                await Close(dialog);

                async Task<WidgetTextEntryDialog> Open(string id)
                {
                    browser.Enter();
                    await browser.ClickFieldForValidationAsync(id);
                    await Until(() => browser.FixtureEditor is { IsLoaded: true, InitialFocusForValidation: true });
                    return browser.FixtureEditor!;
                }
                async Task Close(WidgetTextEntryDialog editor)
                {
                    browser.HandleButton(ControllerButton.B, ControllerEventPhase.Pressed);
                    await Until(() => browser.FixtureEditor is null);
                    var zoom = await browser.EvaluateFixtureAsync("visualViewport.scale");
                    browser.HandleButton(ControllerButton.B, ControllerEventPhase.Repeated);
                    Check(await browser.EvaluateFixtureAsync("visualViewport.scale") == zoom, "Closing keeps the held controller button owned until release");
                    browser.HandleButton(ControllerButton.B, ControllerEventPhase.Released);
                }
                async Task<bool> Eval(string script) => await browser.EvaluateFixtureAsync(script) == "true";
                void Check(bool passed, string name) { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
            }
            catch (Exception failure) { error = failure + "\nSynthetic pointer trace: " + string.Join(";", browser.FixturePointerTrace); }
            if (error is null && Environment.GetCommandLineArgs().Contains("--review-webapp-editor"))
            {
                try
                {
                await Until(() => browser.IsReady && !browser.IsNavigationInProgress && browser.CurrentTitle == "Editor fixture");
                await browser.EvaluateFixtureAsync("document.getElementById('area').value='first\\nsecond'");
                browser.Enter();
                await browser.ClickFieldForValidationAsync("area");
                await Until(() => browser.FixtureEditor is { IsLoaded: true });
                await Task.Delay(100);
                if (!browser.FixtureEditor!.TakeValue().Contains('\n') && !browser.FixtureEditor.TakeValue().Contains('\r'))
                    throw new InvalidOperationException("Multiline review lost existing lines.");
                await CaptureDialogAsync(browser.FixtureEditor!, Path.ChangeExtension(result, ".png"));
                }
                catch (Exception failure) { error = failure + "\nSynthetic pointer trace: " + string.Join(";", browser.FixturePointerTrace); }
            }
            else await DisposeAsync();
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, checks, error }));
        };
    }
    private static FrameworkElement? FindElement(DependencyObject root, string id)
    {
        if (root is FrameworkElement element && AutomationProperties.GetAutomationId(element) == id) return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindElement(VisualTreeHelper.GetChild(root, i), id) is { } found) return found;
        return null;
    }
    private static async Task Until(Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(12);
        while (!ready()) { if (DateTime.UtcNow > end) throw new TimeoutException("Editor fixture did not reach the requested state."); await Task.Delay(30); }
    }
    private static async Task CaptureDialogAsync(WidgetTextEntryDialog dialog, string path)
    {
        var raster = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await raster.RenderAsync(dialog);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)raster.PixelWidth, (uint)raster.PixelHeight, 96, 96, (await raster.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes);
    }
    public ValueTask DisposeAsync() => browser.DisposeAsync();
}
