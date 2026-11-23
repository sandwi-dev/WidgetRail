using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Non-activating, fresh-profile host for the generated-media negotiation check.
internal sealed class WebViewNegotiationProbe : Form
{
    private readonly string output;
    private readonly WebView2 browser = new WebView2();
    private readonly JavaScriptSerializer json = new JavaScriptSerializer();
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get { var value = base.CreateParams; value.ExStyle |= 0x08000000; return value; }
    }
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1) { Environment.ExitCode = 2; return; }
        Application.EnableVisualStyles();
        Application.Run(new WebViewNegotiationProbe(args[0]));
    }
    private WebViewNegotiationProbe(string directory)
    {
        output = Path.GetFullPath(directory);
        Text = "WidgetRail generated WebRTC negotiation probe";
        ShowInTaskbar = false; Opacity = 0; Width = 660; Height = 400;
        browser.Dock = DockStyle.Fill; Controls.Add(browser);
        Shown += async delegate { await RunAsync(); };
    }
    private async Task RunAsync()
    {
        var result = new Dictionary<string, object>();
        result["capturedUtc"] = DateTime.UtcNow.ToString("o");
        var exited = new TaskCompletionSource<bool>();
        CoreWebView2Environment environment = null;
        try
        {
            var options = new CoreWebView2EnvironmentOptions(); options.AreBrowserExtensionsEnabled = true;
            environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(output, "profile"), options);
            result["runtimeVersion"] = environment.BrowserVersionString;
            environment.BrowserProcessExited += delegate { exited.TrySetResult(true); };
            await browser.EnsureCoreWebView2Async(environment);
            browser.CoreWebView2.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs args)
            { args.State = CoreWebView2PermissionState.Deny; };
            browser.CoreWebView2.SetVirtualHostNameToFolderMapping("widgetrail-negotiation-probe.test", output, CoreWebView2HostResourceAccessKind.DenyCors);
            var navigated = new TaskCompletionSource<bool>();
            browser.CoreWebView2.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs args)
            { navigated.TrySetResult(args.IsSuccess); };
            browser.CoreWebView2.Navigate("https://widgetrail-negotiation-probe.test/probe.html");
            var navigation = await Task.WhenAny(navigated.Task, Task.Delay(10000));
            if (navigation != navigated.Task || !await navigated.Task) throw new InvalidOperationException();
            Dictionary<string, object> evidence = null;
            var deadline = DateTime.UtcNow.AddSeconds(40);
            while (DateTime.UtcNow < deadline)
            {
                var raw = await browser.CoreWebView2.ExecuteScriptAsync("globalThis.negotiationProbeResult ?? {ready:false}");
                if (raw.Length > 65536) throw new InvalidDataException();
                evidence = json.Deserialize<Dictionary<string, object>>(raw);
                if (evidence != null && evidence.ContainsKey("ready") && true.Equals(evidence["ready"])) break;
                await Task.Delay(200);
            }
            result["evidence"] = evidence;
            bool passed = evidence != null && evidence.ContainsKey("ready") && true.Equals(evidence["ready"]) &&
                evidence.ContainsKey("passed") && true.Equals(evidence["passed"]);
            result["passed"] = passed;
            if (!passed) Environment.ExitCode = 1;
        }
        catch (Exception error)
        {
            result["passed"] = false; result["errorType"] = error.GetType().Name; result["errorCode"] = error.HResult;
            Environment.ExitCode = 1;
        }
        finally { browser.Dispose(); }
        if (environment != null) await Task.WhenAny(exited.Task, Task.Delay(5000));
        result["browserExitObserved"] = exited.Task.IsCompleted;
        File.WriteAllText(Path.Combine(output, "result.json"), json.Serialize(result));
        Close();
    }
}
