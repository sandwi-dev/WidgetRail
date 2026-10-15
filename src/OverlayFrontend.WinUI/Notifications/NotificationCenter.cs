using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Notifications;

/// <summary>Dispatcher-owned host policy shared by native widgets and WebView notifications.</summary>
internal sealed class NotificationCenter : IDisposable
{
    internal NotificationInbox Inbox { get; } = new();
    internal NotificationPreferences Preferences { get; }
    internal Task Ready { get; }
    internal event Action<NotificationEntry, bool>? BannerRequested;
    private bool ready;
    internal NotificationCenter(string settingsRoot)
    {
        // Preserve the existing per-widget preferences file and its schema.
        Preferences = new(Path.Combine(settingsRoot, "webapp-notifications.json"));
        Ready = LoadAsync();
    }
    private async Task LoadAsync() { await Preferences.LoadAsync(CancellationToken.None); ready = true; }
    internal NotificationPresentation Mode(string widget) => ready ? Preferences.Get(widget) : NotificationPresentation.ListOnly;
    internal void RequestBanner(NotificationEntry entry, bool allowNew) => BannerRequested?.Invoke(entry, allowNew);
    internal void Publish(string widget, object owner, WidgetNotificationCommand command, Func<bool> isCurrent, Action? release = null)
    {
        if (command.Notification is not { } content) { Inbox.WithdrawWidgetKey(widget, command.Key); return; }
        var mode = Mode(widget);
        if (mode == NotificationPresentation.Off) { release?.Invoke(); return; }
        if (content.Retention == WidgetNotificationRetention.BannerOnly && mode != NotificationPresentation.BannerAndList) { release?.Invoke(); return; }
        var callbacks = new NotificationCallbacks(isCurrent, () => { }, () => { }, () => { }, release ?? (() => { }));
        var entry = command.UpdateOnly ? Inbox.UpdateWidget(widget, content) : Inbox.Add(widget, "widget", "", command.Key, content.Title, content.Body, owner,
            callbacks, mode, content);
        if (command.UpdateOnly)
        {
            if (entry is null) release?.Invoke();
            else { entry.Callbacks?.Detach(); entry.Owner = owner; entry.Callbacks = callbacks; }
        }
        if (entry is not null && mode == NotificationPresentation.BannerAndList) RequestBanner(entry, !command.UpdateOnly);
    }
    public void Dispose() { BannerRequested = null; Inbox.Dispose(); }
}
