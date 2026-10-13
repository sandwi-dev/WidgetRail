using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed record BrowserProfileChange(bool ClearData, string? Origin = null,
    CoreWebView2PermissionKind Kind = CoreWebView2PermissionKind.UnknownPermission,
    CoreWebView2PermissionState State = CoreWebView2PermissionState.Default);

internal sealed partial class BrowserSurface
{
    internal Func<BrowserSurface, BrowserProfileChange, CancellationToken, Task<bool>>? ChangeProfile { get; set; }
    internal CoreWebView2Profile? NativeProfile => core?.Profile;
    private bool siteControlsPending;
    private bool profileChanging, presentationInputRequested;

    private async Task ShowSiteControlsAsync()
    {
        if (!CanRequestSiteDecision || HasDialog || siteControlsPending || Choose is not { } choose || core is not { } current) return;
        var origin = BrowserSiteIdentity.Origin(sessionStopped ? document.Url : current.Source);
        if (origin is null) { ShowStatus("Open an HTTPS website to manage its permissions."); return; }
        siteControlsPending = true;
        var token = (siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)).Token;
        try
        {
            HostDialogChoice[] options = [new("permissions", "Website permissions"), new("clear", "Clear website data for this profile"),
                .. (downloads.Count == 0 ? Array.Empty<HostDialogChoice>() : [new("downloads", $"Downloads ({downloads.Count})")]),
                .. (ChangeRunning is null ? Array.Empty<HostDialogChoice>() : [new("session", sessionStopped ? "Restart webapp session" : "Stop webapp session (ends calls)")])];
            var selection = await choose(this, "Website controls", $"{document.AccessibleName}\n{origin}\nProfile: {document.Options.ProfileKey}", options, token);
            token.ThrowIfCancellationRequested();
            if (!CanCompleteSiteDecision || !ReferenceEquals(core, current)) return;
            if (selection == "downloads") { await ManageDownloadsAsync(token); return; }
            BrowserProfileChange? change = null;
            if (selection == "session" && ChangeRunning is { } run)
            {
                if (sessionStopped) await run(this, true);
                else if (await choose(this, "Stop webapp session?", "This ends calls and connections. Saved login data is kept.",
                    (HostDialogChoice[])[new("stop", "Stop session")], token) == "stop") await run(this, false);
                return;
            }
            if (selection == "permissions")
            {
                var permissions = (await current.Profile.GetNonDefaultPermissionSettingsAsync()).Where(p =>
                    BrowserSiteIdentity.Origin(p.PermissionOrigin) == origin).Take(32).ToArray();
                var choices = permissions.Select((p, i) => new HostDialogChoice(i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"{p.PermissionKind}: {(p.PermissionState == CoreWebView2PermissionState.Allow ? "allowed — revoke" : "blocked — ask next time")}")).ToArray();
                selection = await choose(this, "Website permissions", origin + (choices.Length == 0 ? "\nNo saved permission decisions. Temporary grants end when the page closes." : "\nChanging a permission reloads every page in this profile and ends active calls."), choices, token);
                if (int.TryParse(selection, out var index) && index >= 0 && index < permissions.Length)
                {
                    var permission = permissions[index];
                    change = new(false, origin, permission.PermissionKind,
                        permission.PermissionState == CoreWebView2PermissionState.Allow ? CoreWebView2PermissionState.Deny : CoreWebView2PermissionState.Default);
                }
            }
            else if (selection == "clear")
            {
                selection = await choose(this, "Clear website data?",
                    $"{document.AccessibleName} · {document.Options.ProfileKey}\n\nThis signs out all websites and resets their permissions in this profile. All pages in the profile reload, ending active calls. Bookmarks and browsing history are kept.",
                    (HostDialogChoice[])[new("clear", "Clear website data")], token);
                if (selection == "clear") change = new(true);
            }
            token.ThrowIfCancellationRequested();
            if (change is not null && CanCompleteSiteDecision && ReferenceEquals(core, current) && ChangeProfile is { } apply)
            {
                // Navigation intentionally cancels page-scoped prompts. The
                // confirmed profile operation instead belongs to owner lifetime.
                var changed = await apply(this, change, lifetime.Token);
                if (!retired) ShowStatus(changed ? "Website settings updated." : "Website settings could not be changed. Try again.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { siteControlsPending = false; }
    }

    internal async Task<string?> ParkForProfileChangeAsync(CancellationToken token)
    {
        CancelDownloads();
        if (core is not { } current || retired || faulted) return null;
        profileChanging = true;
        SetPresentation(visible, presentationInputRequested);
        ShowStatus("Updating website settings…");
        var url = current.Source;
        if (url == WidgetProtocol.WebBrowserDocument.StartPage) return url;
        var blank = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        { if (sender.Source == WidgetProtocol.WebBrowserDocument.StartPage && args.IsSuccess) blank.TrySetResult(); }
        current.NavigationCompleted += Completed;
        try
        {
            current.Navigate(WidgetProtocol.WebBrowserDocument.StartPage);
            await blank.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            return url;
        }
        finally { current.NavigationCompleted -= Completed; }
    }

    internal void RestoreAfterProfileChange(string? url)
    {
        var pendingNavigation = appliedNavigation < document.NavigationId;
        profileChanging = false;
        SetPresentation(visible, presentationInputRequested);
        if (!retired) ShowStatus(null);
        if (!pendingNavigation && !sessionStopped && !retired && !faulted && core is { } current && WidgetProtocol.WebBrowserDocument.IsWebUrl(url)) current.Navigate(url);
    }
}
