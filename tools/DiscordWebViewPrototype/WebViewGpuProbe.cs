using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Read-only developer probe. Fresh profile, default installed runtime, no
// credentials, media capture, browser flags, package installation or GPU changes.
internal sealed class WebViewGpuProbe : Form
{
    private readonly string output, script, encodeScript;
    private readonly WebView2 browser = new WebView2();
    private readonly JavaScriptSerializer json = new JavaScriptSerializer();

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 3) { Environment.ExitCode = 2; return; }
        Application.EnableVisualStyles();
        Application.Run(new WebViewGpuProbe(args[0], args[1], args[2]));
    }

    private WebViewGpuProbe(string directory, string readScript, string readEncodeScript)
    {
        output = Path.GetFullPath(directory);
        script = File.ReadAllText(readScript);
        encodeScript = File.ReadAllText(readEncodeScript);
        Text = "WidgetRail WebView2 GPU capability probe";
        ShowInTaskbar = false;
        Opacity = 0;
        Width = 640; Height = 480;
        browser.Dock = DockStyle.Fill;
        Controls.Add(browser);
        Shown += async delegate { await RunAsync(); };
    }

    private async Task RunAsync()
    {
        var result = new Dictionary<string, object>();
        result["scope"] = "Fresh WebView2 profile; installed runtime and default GPU settings; capability inventory only, not active encoder proof.";
        result["capturedUtc"] = DateTime.UtcNow.ToString("o");
        var exited = new TaskCompletionSource<bool>();
        CoreWebView2Environment environment = null;
        try
        {
            // Match production BrowserOwner's only non-default option. No
            // AdditionalBrowserArguments or fixed runtime folder is supplied.
            var options = new CoreWebView2EnvironmentOptions();
            options.AreBrowserExtensionsEnabled = true;
            environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(output, "profile"), options);
            result["runtimeVersion"] = environment.BrowserVersionString;
            environment.BrowserProcessExited += delegate { exited.TrySetResult(true); };
            await browser.EnsureCoreWebView2Async(environment);
            browser.CoreWebView2.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs args)
            { args.State = CoreWebView2PermissionState.Deny; };
            var navigated = new TaskCompletionSource<bool>();
            browser.CoreWebView2.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs args)
            { navigated.TrySetResult(args.IsSuccess); };
            browser.CoreWebView2.Navigate("edge://gpu");
            var navigation = await Task.WhenAny(navigated.Task, Task.Delay(15000));
            if (navigation != navigated.Task || !await navigated.Task) throw new InvalidOperationException("gpu_navigation_unavailable");
            Dictionary<string, object> data = null;
            for (int attempt = 0; attempt != 100; attempt++)
            {
                var raw = await browser.CoreWebView2.ExecuteScriptAsync(script);
                if (raw.Length > 131072) throw new InvalidDataException("gpu_report_oversized");
                data = json.Deserialize<Dictionary<string, object>>(raw);
                if (attempt >= 20 && data != null && data.ContainsKey("ready") && true.Equals(data["ready"]) &&
                    data.ContainsKey("gpuIdentityAvailable") && true.Equals(data["gpuIdentityAvailable"])) break;
                await Task.Delay(100);
            }
            if (data == null || !data.ContainsKey("ready") || !true.Equals(data["ready"]) ||
                !data.ContainsKey("gpuIdentityAvailable") || !true.Equals(data["gpuIdentityAvailable"]))
                throw new InvalidOperationException("gpu_report_unavailable");
            result["gpu"] = data;
            File.WriteAllText(Path.Combine(output, "capabilities.html"), "<!doctype html><meta charset=utf-8><title>Local encoder capabilities</title>");
            browser.CoreWebView2.SetVirtualHostNameToFolderMapping("widgetrail-gpu-probe.test", output, CoreWebView2HostResourceAccessKind.DenyCors);
            navigated = new TaskCompletionSource<bool>();
            browser.CoreWebView2.Navigate("https://widgetrail-gpu-probe.test/capabilities.html");
            navigation = await Task.WhenAny(navigated.Task, Task.Delay(10000));
            if (navigation != navigated.Task || !await navigated.Task) throw new InvalidOperationException("capability_page_unavailable");
            await browser.CoreWebView2.ExecuteScriptAsync(encodeScript);
            Dictionary<string, object> capability = null;
            for (int attempt = 0; attempt != 60; attempt++)
            {
                var raw = await browser.CoreWebView2.ExecuteScriptAsync("globalThis.widgetrailEncodeCapabilities ?? {ready:false}");
                if (raw.Length > 131072) throw new InvalidDataException("capability_report_oversized");
                capability = json.Deserialize<Dictionary<string, object>>(raw);
                if (capability != null && capability.ContainsKey("ready") && true.Equals(capability["ready"])) break;
                await Task.Delay(100);
            }
            result["encodingCapabilities"] = capability;
            if (capability == null || !capability.ContainsKey("ready") || !true.Equals(capability["ready"]))
                throw new InvalidOperationException("capability_report_unavailable");
            result["success"] = true;
        }
        catch (Exception error)
        {
            result["success"] = false;
            // Do not serialize exception messages/stack paths from the runtime.
            result["errorType"] = error.GetType().Name;
            result["errorCode"] = error.HResult;
            Environment.ExitCode = 1;
        }
        finally
        {
            browser.Dispose();
        }
        if (environment != null) await Task.WhenAny(exited.Task, Task.Delay(5000));
        result["browserExitObserved"] = exited.Task.IsCompleted;
        File.WriteAllText(Path.Combine(output, "gpu-capabilities.json"), json.Serialize(result));
        Close();
    }
}
