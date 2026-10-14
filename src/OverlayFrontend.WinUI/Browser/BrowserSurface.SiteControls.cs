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
    internal string PermissionTargetLabel => $"{document.AccessibleName} · {BrowserSiteIdentity.Origin(sessionStopped ? document.Url : core?.Source ?? document.Url)} · {document.Options.ProfileKey}";
    private bool siteControlsPending;
    private bool profileChanging, presentationInputRequested;

    private Task ShowSiteControlsAsync() => ShowSiteControlsCoreAsync(false, Choose, false);

    internal Task ShowPermissionsFromHostAsync(Func<BrowserSurface, string, string, IReadOnlyList<HostDialogChoice>, CancellationToken, Task<string?>> choose) =>
        ShowSiteControlsCoreAsync(true, choose, true);

    private async Task ShowSiteControlsCoreAsync(bool permissionsOnly,
        Func<BrowserSurface, string, string, IReadOnlyList<HostDialogChoice>, CancellationToken, Task<string?>>? choose,
        bool hostInvoked)
    {
        if (!(hostInvoked ? CanCompleteSiteDecision : CanRequestSiteDecision) || HasDialog || siteControlsPending || choose is null || core is not { } current) return;
        var origin = BrowserSiteIdentity.Origin(sessionStopped ? document.Url : current.Source, requireHttps: false);
        if (origin is null) { ShowStatus("Open a website to manage its settings."); return; }
        var secure = origin.StartsWith("https://", StringComparison.Ordinal);
        if (permissionsOnly && !secure) { ShowStatus("Open an HTTPS website to manage its permissions."); return; }
        siteControlsPending = true;
        var token = (siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)).Token;
        try
        {
            HostDialogChoice[] options = [.. (secure ? (HostDialogChoice[])[new("permissions", "Website permissions")] : Array.Empty<HostDialogChoice>()), new("clear", "Clear website data for this profile"),
                .. (ManagePopupPlacement is null || RequestPopupPin is null || document.Options.Popups != WidgetProtocol.BrowserPopupMode.Hosted
                    ? Array.Empty<HostDialogChoice>() : [new("popups", "Pop-out placement")]),
                .. (downloads.Count == 0 ? Array.Empty<HostDialogChoice>() : [new("downloads", $"Downloads ({downloads.Count})")]),
                .. (ChangeRunning is null ? Array.Empty<HostDialogChoice>() : [new("session", sessionStopped ? "Restart webapp session" : "Stop webapp session (ends calls)")])];
            var selection = permissionsOnly ? "permissions" : await choose(this, "Website controls", $"{document.AccessibleName}\n{origin}\nProfile: {document.Options.ProfileKey}", options, token);
            token.ThrowIfCancellationRequested();
            if (!CanCompleteSiteDecision || !ReferenceEquals(core, current)) return;
            if (selection == "downloads") { await ManageDownloadsAsync(token); return; }
            if (selection == "popups" && ManagePopupPlacement is { } manage) { await manage(this, origin, token); return; }
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
                var saved = (await current.Profile.GetNonDefaultPermissionSettingsAsync()).Where(p =>
                    BrowserSiteIdentity.Origin(p.PermissionOrigin) == origin).ToDictionary(p => p.PermissionKind, p => p.PermissionState);
                var permissions = WebsitePermissions;
                var choices = permissions.Select((p, i) => new HostDialogChoice(i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"{p.Label} · {PermissionSettingLabel(saved.GetValueOrDefault(p.Kind, CoreWebView2PermissionState.Default))}")).ToArray();
                selection = await choose(this, "Website permissions", $"{document.AccessibleName}\n{origin}\nProfile: {document.Options.ProfileKey}\n\nChoose a permission to change its saved setting.", choices, token);
                if (int.TryParse(selection, out var index) && index >= 0 && index < permissions.Length)
                {
                    var permission = permissions[index];
                    selection = await choose(this, permission.Label, $"{origin}\n\nChanging this setting reloads every page in this profile and ends active calls. Choosing Ask also clears a temporary decision.",
                        (HostDialogChoice[])[new("ask", "Ask next time"), new("allow", "Allow"), new("block", "Block")], token);
                    if (selection is "ask" or "allow" or "block")
                        change = new(false, origin, permission.Kind, selection == "allow" ? CoreWebView2PermissionState.Allow :
                            selection == "block" ? CoreWebView2PermissionState.Deny : CoreWebView2PermissionState.Default);
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

    private static readonly (CoreWebView2PermissionKind Kind, string Label)[] WebsitePermissions =
    [
        (CoreWebView2PermissionKind.Microphone, "Microphone"),
        (CoreWebView2PermissionKind.Camera, "Camera"),
        (CoreWebView2PermissionKind.Notifications, "Notifications"),
        (CoreWebView2PermissionKind.Geolocation, "Location"),
        (CoreWebView2PermissionKind.ClipboardRead, "Read clipboard"),
        (CoreWebView2PermissionKind.Autoplay, "Automatic media playback"),
        (CoreWebView2PermissionKind.MultipleAutomaticDownloads, "Multiple downloads"),
    ];
    private static string PermissionSettingLabel(CoreWebView2PermissionState state) => state switch
    { CoreWebView2PermissionState.Allow => "Allow", CoreWebView2PermissionState.Deny => "Block", _ => "Ask" };

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
