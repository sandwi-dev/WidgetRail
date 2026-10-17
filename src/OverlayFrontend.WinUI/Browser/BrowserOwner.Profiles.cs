namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    private readonly HashSet<string> changingProfiles = [];
    internal BrowserSurface[] PermissionTargets(string widgetId) => retired ? [] : entries
        .Where(pair => pair.Key.Widget == widgetId && session.IsWebBrowserSessionCurrent(pair.Value.Authority, pair.Key.Document))
        .Select(pair => pair.Value.Surface.ActiveSurface)
        .Concat(popupPresentation is { IsCurrent: true } popup && popup.WidgetId == widgetId ? [popup.Child] : Array.Empty<BrowserSurface>())
        .Distinct().Where(page => page.IsReady && page.CanCompleteSiteDecision).ToArray();

    private async Task<bool> ChangeProfileAsync(BrowserSurface source, BrowserProfileChange change, CancellationToken token)
    {
        var root = entries.Values.Select(entry => entry.Surface).FirstOrDefault(page => page.Owns(source));
        // A child closes while this operation parks the profile. Its Profile
        // wrapper becomes invalid with that controller, even though the same
        // account remains alive in its parent. Use the surviving root wrapper.
        if (retired || !source.CanCompleteSiteDecision || root?.NativeProfile is not { } profile ||
            root.ProfileName != source.ProfileName || !changingProfiles.Add(source.ProfileName)) return false;
        token = lifetime.Token;
        var parked = entries.Values.Select(entry => entry.Surface).Where(surface => surface.ProfileName == source.ProfileName)
            .Select(surface => (Surface: surface, Url: surface.CurrentUrl)).ToArray();
        try
        {
            // Stop every live document before mutating shared storage. Otherwise
            // another view could keep a microphone alive or rewrite login data.
            foreach (var (surface, _) in parked)
            { await surface.DrainPopupAsync(); await surface.ParkForProfileChangeAsync(token); }
            token.ThrowIfCancellationRequested();
            if (change.ClearData)
            {
                var permissions = await profile.GetNonDefaultPermissionSettingsAsync();
                await profile.ClearBrowsingDataAsync();
                foreach (var permission in permissions)
                    await profile.SetPermissionStateAsync(permission.PermissionKind, permission.PermissionOrigin,
                        Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Default);
            }
            else if (change.Origin is { } origin && BrowserSiteIdentity.Origin(origin) == origin)
                await profile.SetPermissionStateAsync(change.Kind, origin, change.State);
            else return false;
            return true;
        }
        finally
        {
            changingProfiles.Remove(source.ProfileName);
            foreach (var (surface, url) in parked) surface.RestoreAfterProfileChange(url);
            Refresh();
        }
    }
}
