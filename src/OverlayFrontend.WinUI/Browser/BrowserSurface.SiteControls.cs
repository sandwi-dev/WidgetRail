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

    private Task ShowSiteControlsAsync() => ShowSiteControlsCoreAsync(Choose, false);

    internal Task ShowSettingsFromHostAsync(BrowserChoiceHandler choose) =>
        ShowSiteControlsCoreAsync(choose, true);

    private async Task ShowSiteControlsCoreAsync(
        BrowserChoiceHandler? choose, bool hostInvoked)
    {
        if (!(hostInvoked ? CanCompleteSiteDecision : CanRequestSiteDecision) || HasDialog || siteControlsPending || choose is null || core is not { } current) return;
        var origin = BrowserSiteIdentity.Origin(sessionStopped ? document.Url : current.Source, requireHttps: false);
        if (origin is null) { ShowStatus("Open a website to manage its settings."); return; }
        siteControlsPending = true;
        var token = (siteDecisions ??= CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)).Token;
        try
        {
            string? returnChoice = null;
            while (CanCompleteSiteDecision && ReferenceEquals(core, current))
            {
                var choices = new List<HostDialogChoice>();
                if (origin.StartsWith("https://", StringComparison.Ordinal))
                {
                    var saved = (await current.Profile.GetNonDefaultPermissionSettingsAsync()).Where(p =>
                        BrowserSiteIdentity.Origin(p.PermissionOrigin) == origin).ToDictionary(p => p.PermissionKind, p => p.PermissionState);
                    choices.AddRange(WebsitePermissions.Select((p, i) => new HostDialogChoice("permission." + i,
                        p.Label, Group: i == 0 ? "Permissions" : null,
                        Value: PermissionSettingLabel(saved.GetValueOrDefault(p.Kind, CoreWebView2PermissionState.Default)))));
                }
                var placement = BrowserPopupPlacement.Ask;
                if (ReadPopupPlacement is { } read && WritePopupPlacement is not null && RequestPopupPin is not null && document.Options.Popups == WidgetProtocol.BrowserPopupMode.Hosted)
                {
                    placement = await read(this, origin, token);
                    choices.Add(new("windows", "New windows", Group: "Behavior", Value: PopupPlacementLabel(placement)));
                }
                choices.Add(new("clear", "Clear website data…", Group: "Data"));
                if (downloads.Count != 0) choices.Add(new("downloads", "Downloads", Value: downloads.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)));
                var description = origin + (document.Options.ProfileKey == "default" ? "" : "\nProfile: " + document.Options.ProfileKey);
                var selection = await choose(this, "Website settings", description, choices, token,
                    preference: new(returnChoice, "Done"));
                token.ThrowIfCancellationRequested();
                if (selection is null || !CanCompleteSiteDecision || !ReferenceEquals(core, current)) return;
                returnChoice = selection;
                if (selection == "downloads") { await ManageDownloadsAsync(token); continue; }
                if (selection == "windows")
                {
                    var selected = await choose(this, "New windows", "Choose where this website opens additional pages.",
                        (HostDialogChoice[])[new("ask", "Ask each time"), new("widget", "Open in widget"), new("pin", "Open pinned")], token,
                        preference: new(placement switch { BrowserPopupPlacement.Pinned => "pin", BrowserPopupPlacement.InWidget => "widget", _ => "ask" }));
                    token.ThrowIfCancellationRequested();
                    if (!CanCompleteSiteDecision || !ReferenceEquals(core, current)) return;
                    if (selected is "ask" or "widget" or "pin" && WritePopupPlacement is { } write)
                        await write(this, origin, selected switch
                        { "pin" => BrowserPopupPlacement.Pinned, "widget" => BrowserPopupPlacement.InWidget, _ => BrowserPopupPlacement.Ask }, token);
                    continue;
                }
                BrowserProfileChange? change = null;
                if (selection.StartsWith("permission.", StringComparison.Ordinal) && int.TryParse(selection[11..], out var index) && index >= 0 && index < WebsitePermissions.Length)
                {
                    var permission = WebsitePermissions[index];
                    // Changing permissions reloads the profile: keep the safe confirmation policy.
                    selection = await choose(this, permission.Label, $"{origin}\n\nChanging this setting reloads every page in this profile and ends active calls. Choosing Ask also clears a temporary decision.",
                        (HostDialogChoice[])[new("ask", "Ask next time"), new("allow", "Allow"), new("block", "Block")], token);
                    if (selection is "ask" or "allow" or "block")
                        change = new(false, origin, permission.Kind, selection == "allow" ? CoreWebView2PermissionState.Allow :
                            selection == "block" ? CoreWebView2PermissionState.Deny : CoreWebView2PermissionState.Default);
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
                    var changed = await apply(this, change, lifetime.Token);
                    if (!retired) ShowStatus(changed ? "Website settings updated." : "Website settings could not be changed. Try again.");
                    return;
                }
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
    private static string PopupPlacementLabel(BrowserPopupPlacement placement) => placement switch
    { BrowserPopupPlacement.Pinned => "Open pinned", BrowserPopupPlacement.InWidget => "Open in widget", _ => "Ask each time" };

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
