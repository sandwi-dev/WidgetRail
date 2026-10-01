using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>
/// Explicit development-only external-content gate. Not a widget media provider:
/// it accepts no widget URL, HTML, script, handle, or credential.
/// </summary>
public sealed partial class ExternalSurfacePage : Page
{
    private bool retired;
    private bool started;
    private ContentDialog? dialog;

    public ExternalSurfacePage()
    {
        InitializeComponent();
        Loaded += InitializeSurface;
        Unloaded += (_, _) => Retire();
    }

    private async void InitializeSurface(object sender, RoutedEventArgs e)
    {
        if (started || retired) return;
        started = true;
        try
        {
            await Browser.EnsureCoreWebView2Async();
            if (retired) return;
            var core = Browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            // NavigateToString can expose its internal data:text/html navigation.
            // This fixture accepts no external input and serves only the literal below.
            core.NavigationStarting += (_, args) => args.Cancel = args.Uri != "about:blank" &&
                !args.Uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase);
            core.NavigationCompleted += (_, args) =>
            {
                if (retired) return;
                StatusText.Text = args.IsSuccess ? "External surface ready" : $"Navigation failed: {args.WebErrorStatus}";
                OverlayCommand.IsEnabled = args.IsSuccess;
            };
            core.NavigateToString("""
                <!doctype html><meta charset="utf-8">
                <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'">
                <style>body{margin:0;background:#174a86;color:white;font:24px system-ui}
                #moving{position:absolute;top:48%;width:120px;height:80px;background:#f0b838}
                p{margin:24px;max-width:55%}</style>
                <p>WebView2 animated external content</p><div id="moving"></div>
                <script>let start;function frame(t){start??=t;document.getElementById('moving').style.left=
                (10+60*((t-start)%2000)/2000)+'%';requestAnimationFrame(frame)}requestAnimationFrame(frame)</script>
                """);
        }
        catch (Exception error)
        {
            if (!retired) StatusText.Text = $"External surface failed: {error.GetType().Name}: {error.Message}";
        }
    }

    private async void OpenDialogClicked(object sender, RoutedEventArgs e)
    {
        if (retired || dialog is not null) return;
        var current = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Native surface layering",
            Content = "This WinUI dialog must remain above the animated WebView2 surface.",
            CloseButtonText = "Return to surface",
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(current, "Media.Dialog");
        dialog = current;
        try { await current.ShowAsync(); }
        finally { dialog = null; }
    }

    public void Retire()
    {
        if (retired) return;
        retired = true;
        dialog?.Hide();
        Browser.Close();
    }
}
