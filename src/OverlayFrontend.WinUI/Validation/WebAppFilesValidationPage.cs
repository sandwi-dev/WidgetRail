using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Native Chromium file semantics with fixture-only picker decisions and synthetic files.</summary>
internal sealed partial class WebAppFilesValidationPage : Page, IAsyncDisposable
{
    private readonly BrowserSurface browser;
    internal WebAppFilesValidationPage(string result, string profileRoot, bool nativePickers)
    {
        Task<CoreWebView2Environment>? environment = null;
        BrowserSurface.FixturePage = _ => "<!doctype html><title>Files fixture</title><body><input id='upload' type='file' multiple></body>";
        browser = new("fixture.files", new("files", 1, "https://files.fixture.invalid/", "Files fixture")
        { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp },
            () => environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profileRoot, null).AsTask())
        { IsInteractionFocused = () => true };
        Content = browser;
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null; var pickerCalls = 0; var saveCalls = 0; var mode = "select";
            Directory.CreateDirectory(profileRoot);
            var source = Path.Combine(profileRoot, "fixture-upload.txt");
            var destination = Path.GetFullPath(result + ".download.txt");
            await File.WriteAllTextAsync(source, "local-upload-marker");
            try
            {
                browser.Choose = (_, _, message, choices, _) =>
                {
                    if (!message.Contains("https://files.fixture.invalid", StringComparison.Ordinal)) throw new InvalidOperationException("Missing file origin.");
                    return Task.FromResult<string?>(choices[0].Id);
                };
                browser.ChooseFiles = async (_, request, token) =>
                {
                    if (request.Save)
                    {
                        saveCalls++;
                        File.WriteAllText(result + ".picker", JsonSerializer.Serialize(new { id = "save" + saveCalls, mode, path = destination }));
                        var selected = nativePickers ? await BrowserFilePicker.PickAsync(browser.XamlRoot.ContentIslandEnvironment.AppWindowId, request, token) : mode == "cancel" ? [] : new[] { destination };
                        if (mode == "select") browser.SetPresentation(true, false);
                        return selected;
                    }
                    pickerCalls++;
                    if (!request.Multiple) throw new InvalidOperationException("Multiple-file input lost its selection mode.");
                    if (mode == "stale") await browser.EvaluateFixtureAsync("document.getElementById('upload').outerHTML='<input id=upload type=file multiple>'");
                    File.WriteAllText(result + ".picker", JsonSerializer.Serialize(new { id = "open" + pickerCalls, mode, path = source }));
                    var selectedFiles = nativePickers ? await BrowserFilePicker.PickAsync(browser.XamlRoot.ContentIslandEnvironment.AppWindowId, request, token) : mode == "cancel" ? [] : new[] { source };
                    if (pickerCalls == 1) browser.SetPresentation(true, false);
                    return selectedFiles;
                };
                browser.SetPresentation(true, true);
                await UntilScript("document.title==='Files fixture'"); browser.Enter();
                await Upload();
                await UntilScript("document.getElementById('upload').files.length===1");
                await browser.EvaluateFixtureAsync("document.getElementById('upload').files[0].text().then(s=>window.uploaded=s)");
                await UntilScript("window.uploaded==='local-upload-marker'");
                Check(pickerCalls == 1, "Native file input receives only the synthetic file selected through host picker contract");
                Check(!browser.CanRequestSiteDecision, "Confirmed upload completes while native-picker input handoff is still pending");
                browser.SetPresentation(true, true);
                mode = "cancel"; await Upload();
                await UntilNative(() => pickerCalls == 2 && !browser.HasDialog);
                Check(await Eval("document.getElementById('upload').files.length===1"), "Cancelling file selection preserves the previous selection");
                mode = "stale"; await Upload();
                await UntilNative(() => pickerCalls == 3 && !browser.HasDialog);
                Check(await Eval("document.getElementById('upload').files.length===0"), "Replacement file input cannot inherit a stale selection");
                mode = "select";
                browser.SetPresentation(false, false);
                await Upload();
                await Task.Delay(150);
                Check(pickerCalls == 3, "Hidden page cannot launch an upload picker");
                browser.SetPresentation(true, true);
                await Download();
                await UntilNative(() => saveCalls == 1 && !browser.HasDialog && browser.FixtureDownloadCount == 0 && File.Exists(destination));
                await UntilNative(() => File.ReadAllText(destination) == "local-download-marker");
                Check(!browser.CanRequestSiteDecision, "Native download commits the chosen destination while input handoff is pending");
                Check(true, "Native blob download writes exact synthetic bytes to the host-selected destination");
                browser.SetPresentation(true, true);
                mode = "cancel"; await Download();
                await UntilNative(() => saveCalls == 2 && !browser.HasDialog);
                Check(File.ReadAllText(destination) == "local-download-marker", "Cancelled download preserves the existing destination");
                if (nativePickers)
                {
                    using var cancelPicker = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
                    try
                    {
                        await BrowserFilePicker.PickAsync(browser.XamlRoot.ContentIslandEnvironment.AppWindowId, new(false), cancelPicker.Token).WaitAsync(TimeSpan.FromSeconds(10));
                        throw new InvalidOperationException("Cancelled picker unexpectedly returned a selection.");
                    }
                    catch (OperationCanceledException) when (cancelPicker.IsCancellationRequested)
                    { Check(true, "Cancellation closes the owned native picker and joins its completion"); }
                }
            }
            catch (Exception failure) { error = failure.ToString(); }
            try { await DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (Exception failure) { error ??= failure.ToString(); }
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, nativePickers, checks, pickerCalls, saveCalls, error, downloads = browser.FixtureDownloadTrace, permissions = browser.FixturePermissionTrace }));
            Task Upload() => browser.EvaluateGestureFixtureAsync("document.getElementById('upload').click()");
            Task Download() => browser.EvaluateGestureFixtureAsync("(()=>{const a=document.createElement('a');a.href=URL.createObjectURL(new Blob(['local-download-marker'],{type:'text/plain'}));a.download='fixture.txt';document.body.append(a);a.click();a.remove();})()");
            Task UntilNative(Func<bool> condition) => Until(condition, nativePickers ? 90 : 15);
            async Task<bool> Eval(string script) => browser.IsReady && await browser.EvaluateFixtureAsync(script) == "true";
            async Task UntilScript(string script)
            {
                var end = DateTime.UtcNow.AddSeconds(nativePickers ? 90 : 15);
                while (!await Eval(script)) { if (DateTime.UtcNow > end) throw new TimeoutException("File script timed out: " + script); await Task.Delay(50); }
            }
            void Check(bool passed, string name)
            { if (!passed) throw new InvalidOperationException(name); checks.Add(name); File.WriteAllText(result + ".progress", string.Join('\n', checks)); }
        };
    }
    private static async Task Until(Func<bool> condition, int timeoutSeconds)
    {
        var end = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition()) { if (DateTime.UtcNow > end) throw new TimeoutException("File state timed out."); await Task.Delay(50); }
    }
    public ValueTask DisposeAsync() => browser.DisposeAsync();
}
