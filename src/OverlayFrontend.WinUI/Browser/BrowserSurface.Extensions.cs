using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    internal bool ExtensionsEligible => !IsProviderContent && !IsHostedPackage && document.Options.Storage == BrowserStorage.Persistent;
    internal ExtensionProfile ExtensionProfile => new(profile, widgetId,
        document.AccessibleName + " · " + document.Options.ProfileKey, document.Options.Storage == BrowserStorage.Private);
    internal Func<BrowserSurface, CancellationToken, Task>? RegisterExtensionProfile { get; set; }
    private string? extensionOptionsId;
    private bool IsAuthorizedExtensionUrl(string url) => extensionOptionsId is not null &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "chrome-extension" && uri.Host == extensionOptionsId && string.IsNullOrEmpty(uri.UserInfo);

    internal async Task OpenExtensionOptionsAsync(ExtensionInstallation install)
    {
        if (!ExtensionsEligible || !CanCompleteSiteDecision || core is not { } current || install.Manifest.OptionsPage is not { } path) return;
        var native = (await current.Profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Id == install.Id && e.IsEnabled);
        if (native is null || !CanCompleteSiteDecision || !ReferenceEquals(core, current)) return;
        var url = "chrome-extension://" + native.Id + "/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        // Use the established additional-page lifetime and Back path. The
        // original website (and any playback/call) stays alive behind options.
        (popupParent ?? this).AdmitNewWindow(this, new NewWindowRequest(url, lifetime.Token, native.Id));
    }
}
