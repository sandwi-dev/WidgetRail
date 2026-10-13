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
        BrowserSurface.FixturePage = _ => Html;
        browser = new("fixture.editor", new("editor", 1, "https://editor.fixture.invalid/", "Editor fixture")
        { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp },
            () => environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profileRoot, null).AsTask())
        { IsInteractionFocused = () => true };
        Content = new Grid { Margin = new(24), Children = { browser } };
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null;
            try
            {
                browser.SetPresentation(true, true);
                await Until(() => browser.IsReady && !browser.IsNavigationInProgress && browser.CurrentTitle == "Editor fixture");
                browser.Enter();
                await Edit("document.getElementById('plain').focus()", "changed");
                Check(await Eval("document.getElementById('plain').value==='changed' && window.enters===0"), "Apply edits a plain field without submitting");
                Check(await Eval("window.nativeInputs>0 && window.beforeInputs>0"), "Browser insertion emits trusted editor input events");
                await Edit("document.getElementById('area').focus()", "first\nsecond");
                Check(await Eval("document.getElementById('area').value==='first\\nsecond' && window.enters===0"), "Multiline textarea edits do not send Enter");
                await Edit("document.getElementById('password').focus()", "synthetic-replacement", password: true);
                Check(await Eval("document.getElementById('password').value==='synthetic-replacement'"), "Protected password replacement reaches the selected field");
                await Edit("document.getElementById('rich').focus();let r=document.createRange();r.selectNodeContents(document.getElementById('tail'));r.collapse(false);let s=getSelection();s.removeAllRanges();s.addRange(r)", " added\nnext");
                Check(await Eval("document.getElementById('rich').innerText.includes('added') && document.getElementById('bold')===originalBold && document.getElementById('mention')===originalMention"), "Rich insertion preserves formatting and mention nodes");
                await Edit("document.getElementById('plain').focus()", "cancelled", cancel: true);
                Check(await Eval("document.getElementById('plain').value==='changed'"), "Cancel leaves the page unchanged");
                await Edit("document.getElementById('plain').focus()", "stale", stale: true);
                Check(await Eval("document.getElementById('plain').value==='newer'"), "A newer page edit is not overwritten");
                await Edit("document.getElementById('plain').focus()", "submitted", submit: true);
                Check(await Eval("document.getElementById('plain').value==='submitted' && window.enters===1"), "Only Apply plus Enter submits an explicit page key");

                async Task Edit(string select, string value, bool password = false, bool cancel = false, bool stale = false, bool submit = false)
                {
                    await browser.EvaluateFixtureAsync(select);
                    browser.Enter();
                    browser.HandleButton(ControllerButton.RightStick, ControllerEventPhase.Pressed);
                    browser.HandleButton(ControllerButton.RightStick, ControllerEventPhase.Released);
                    await Until(() => browser.FixtureEditor is { IsLoaded: true });
                    var dialog = browser.FixtureEditor!;
                    if (select.Contains("'area'", StringComparison.Ordinal) && await browser.EvaluateFixtureAsync("document.getElementById('area').value.includes('\\n')") == "true")
                        Check(dialog.TakeValue().Contains('\n') || dialog.TakeValue().Contains('\r'), "Existing multiline text survives keyboard initialization");
                    if (password) Check(dialog.IsSensitive && dialog.TakeValue().Length == 0, "Password dialog never imports an existing password");
                    var editor = FindElement(dialog, "Widget.TextEntry.Editor");
                    if (editor is PasswordBox secret) secret.Password = value;
                    else ((TextBox)editor!).Text = value;
                    if (stale) await browser.EvaluateFixtureAsync("document.getElementById('plain').value='newer'");
                    if (submit)
                    {
                        var button = FindElement(dialog, "Widget.TextEntry.Submit") as Button ?? throw new InvalidOperationException("Submit control missing.");
                        button.Focus(FocusState.Keyboard); dialog.Activate();
                    }
                    else browser.HandleButton(cancel ? ControllerButton.B : ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
                    await Until(() => browser.FixtureEditor is null);
                    if (!submit)
                    {
                        var zoom = await browser.EvaluateFixtureAsync("visualViewport.scale");
                        browser.HandleButton(cancel ? ControllerButton.B : ControllerButton.RightTrigger, ControllerEventPhase.Repeated);
                        await Until(() => browser.FixtureZoomIdle);
                        Check(await browser.EvaluateFixtureAsync("visualViewport.scale") == zoom, "Closing keyboard retains held-button ownership until release");
                        browser.HandleButton(cancel ? ControllerButton.B : ControllerButton.RightTrigger, ControllerEventPhase.Released);
                    }
                    // Text commit is asynchronous after the native dialog closes.
                    await Task.Delay(120);
                }
                async Task<bool> Eval(string script) => await browser.EvaluateFixtureAsync(script) == "true";
                void Check(bool passed, string name) { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
            }
            catch (Exception failure) { error = failure.ToString(); }
            if (error is null && Environment.GetCommandLineArgs().Contains("--review-webapp-editor"))
            {
                await browser.EvaluateFixtureAsync("document.getElementById('area').focus()");
                browser.Enter();
                browser.HandleButton(ControllerButton.RightStick, ControllerEventPhase.Pressed);
                browser.HandleButton(ControllerButton.RightStick, ControllerEventPhase.Released);
                await Until(() => browser.FixtureEditor is { IsLoaded: true });
                await Task.Delay(100);
                if (!browser.FixtureEditor!.TakeValue().Contains('\n') && !browser.FixtureEditor.TakeValue().Contains('\r'))
                    throw new InvalidOperationException("Multiline review lost existing lines.");
                await CaptureDialogAsync(browser.FixtureEditor!, Path.ChangeExtension(result, ".png"));
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
