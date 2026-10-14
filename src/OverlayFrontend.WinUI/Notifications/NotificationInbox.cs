using WidgetRail.WidgetProtocol;
namespace WidgetRail.OverlayFrontend.WinUI.Notifications;

internal enum NotificationPresentation { BannerAndList, ListOnly, Off }

internal sealed record NotificationCallbacks(Func<bool> IsCurrent, Action Shown, Action Clicked, Action Closed, Action Detach);

internal sealed class NotificationEntry(long id, string widget, string profile, string origin, string tag, string title, string body,
    object owner, NotificationCallbacks callbacks)
{
    internal long Id { get; } = id;
    internal WidgetNotification? Content { get; set; }
    internal long ContentRevision { get; private set; }
    internal bool Pending => Content?.Retention != WidgetNotificationRetention.BannerOnly;
    internal bool Interactive => Content?.Interaction != WidgetNotificationInteraction.Informational;
    internal bool ShowWithOverlay => Content?.Visibility == WidgetNotificationVisibility.Always;
    internal DateTimeOffset ReceivedAt { get; } = DateTimeOffset.Now;
    internal string Widget { get; } = widget;
    internal string Profile { get; } = profile;
    internal string Origin { get; } = origin;
    internal string Tag { get; } = tag;
    internal string Title { get; private set; } = title;
    internal string Body { get; private set; } = body;
    internal void UpdateContent(WidgetNotification content)
    { Content = content; Title = content.Title; Body = content.Body; ContentRevision++; }
    internal object? Owner { get; set; } = owner;
    internal NotificationCallbacks? Callbacks { get; set; } = callbacks;
    internal bool WasShown { get; set; }
    internal bool Activating { get; set; }
    internal bool ReplacesPrevious { get; init; }
    internal bool Available => Callbacks?.IsCurrent() == true;
}

/// <summary>Dispatcher-owned pending notifications. Banner expiry never removes an entry.</summary>
internal sealed class NotificationInbox(int capacity = 256) : IDisposable
{
    private readonly int capacityLimit = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly List<NotificationEntry> entries = [];
    private readonly List<NotificationEntry> transient = [];
    private long sequence;
    internal IReadOnlyList<NotificationEntry> Entries => entries;
    internal int OverflowCount { get; private set; }
    internal event Action? Changed;

    internal NotificationEntry? Add(string widget, string profile, string origin, string tag, string title, string body,
        object owner, NotificationCallbacks callbacks, NotificationPresentation mode, WidgetNotification? content = null)
    {
        if (mode == NotificationPresentation.Off) { callbacks.Detach(); return null; }
        tag = tag.Length == 0 ? "" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tag)));
        var replaced = false;
        if (tag.Length != 0)
        {
            var previous = entries.Concat(transient).FirstOrDefault(entry => entry.Widget == widget && entry.Profile == profile && entry.Origin == origin && entry.Tag == tag);
            if (previous is not null) { replaced = true; Remove(previous, notify: true); }
        }
        var target = content?.Retention == WidgetNotificationRetention.BannerOnly ? transient : entries;
        if (target.Count >= (ReferenceEquals(target, transient) ? 32 : capacityLimit))
        {
            // Preserve existing pending items; reject new handles and expose a
            // visible overflow count rather than silently evicting older items.
            if (ReferenceEquals(target, entries)) OverflowCount = Math.Min(int.MaxValue - 1, OverflowCount) + 1;
            callbacks.Detach(); Changed?.Invoke(); return null;
        }
        var item = new NotificationEntry(++sequence, widget, profile, origin, Bound(tag, 256), Bound(title, 256), Bound(body, 2048), owner, callbacks) { ReplacesPrevious = replaced, Content = content };
        target.Add(item); if (item.Pending) Changed?.Invoke(); return item;
    }
    internal NotificationEntry? Find(long id) => entries.Concat(transient).FirstOrDefault(entry => entry.Id == id);
    internal void WithdrawWidgetKey(string widget, string key)
    {
        var tag = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
        foreach (var item in entries.Concat(transient).Where(e => e.Widget == widget && e.Profile == "widget" && e.Tag == tag).ToArray()) Withdraw(item.Id);
    }
    internal NotificationEntry? UpdateWidget(string widget, WidgetNotification content)
    {
        var tag = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content.Key)));
        var entry = entries.Concat(transient).FirstOrDefault(e => e.Widget == widget && e.Profile == "widget" && e.Tag == tag && e.Available);
        if (entry is null || entry.Content?.Retention != content.Retention) return null;
        entry.UpdateContent(content);
        if (entry.Pending) Changed?.Invoke();
        return entry;
    }
    internal void ReleaseBanner(long id) { if (Find(id) is { Pending: false } entry) Remove(entry, notify: false); }
    internal bool MarkShown(long id)
    {
        var item = Find(id);
        if (item is null || !item.Available) return false;
        if (item.WasShown) return true;
        try { item.Callbacks!.Shown(); item.WasShown = true; return true; }
        catch (Exception error) when (error is not OutOfMemoryException) { Invalidate(item); Changed?.Invoke(); return false; }
    }
    internal async Task<bool> ActivateAsync(long id, Func<NotificationEntry, Task<bool>> activate)
    {
        var item = Find(id);
        if (item is null || !item.Interactive || item.Activating || !item.Available || !MarkShown(id)) return false;
        item.Activating = true;
        try
        {
            if (!await activate(item) || !ReferenceEquals(Find(id), item) || !item.Available) return false;
            item.Callbacks!.Clicked();
            // Activation is not an explicit dismissal. Do not immediately send
            // ReportClosed after ReportClicked: the renderer must first deliver
            // the click, and the website can close its notification itself.
            if (ReferenceEquals(Find(id), item)) Remove(item, notify: false);
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
        finally { item.Activating = false; Changed?.Invoke(); }
    }
    // A stale document cannot receive a click. Open its widget normally instead,
    // without dispatching callbacks to a replacement document.
    internal async Task<bool> OpenAsync(long id, Func<NotificationEntry, Task<bool>> activate,
        Func<NotificationEntry, Task<bool>> openWidget)
    {
        var item = Find(id);
        if (item is null || !item.Interactive || item.Activating) return false;
        if (await ActivateAsync(id, activate)) return true;
        if (!ReferenceEquals(Find(id), item) || item.Activating) return false;
        item.Activating = true;
        try
        {
            if (!await openWidget(item)) return false;
            if (ReferenceEquals(Find(id), item)) { Remove(item, notify: false); Changed?.Invoke(); }
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
        finally { item.Activating = false; }
    }
    internal void Dismiss(long id) { if (Find(id) is { } item) { Remove(item, notify: true); Changed?.Invoke(); } }
    internal void Withdraw(long id) { if (Find(id) is { } item) { Remove(item, notify: false); Changed?.Invoke(); } }
    internal void Clear()
    {
        foreach (var entry in entries.ToArray()) Remove(entry, notify: true);
        OverflowCount = 0; Changed?.Invoke();
    }
    internal void InvalidateOwner(object owner)
    {
        var changed = false;
        foreach (var entry in entries.Concat(transient).Where(entry => ReferenceEquals(entry.Owner, owner))) { Invalidate(entry); changed = true; }
        if (changed) Changed?.Invoke();
    }
    private static void Invalidate(NotificationEntry entry)
    {
        var callbacks = entry.Callbacks;
        entry.Callbacks = null; entry.Owner = null;
        try { callbacks?.Detach(); } catch (Exception error) when (error is not OutOfMemoryException) { }
    }
    private void Remove(NotificationEntry entry, bool notify)
    {
        if (!entries.Remove(entry) && !transient.Remove(entry)) return;
        try { if (notify && entry.WasShown && entry.Available) entry.Callbacks!.Closed(); }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        finally { Invalidate(entry); }
    }
    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..(char.IsHighSurrogate(value[maximum - 1]) ? maximum - 1 : maximum)];
    public void Dispose() { Clear(); foreach (var item in transient.ToArray()) Remove(item, notify: false); Changed = null; }
}

internal sealed class NotificationBannerState
{
    internal static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(4);
    internal static readonly TimeSpan QuietInterval = TimeSpan.FromSeconds(8);
    internal long? EntryId { get; private set; }
    internal string? Widget { get; private set; }
    internal long ExpiresAt { get; private set; }
    private long nextAllowed;
    internal bool Offer(NotificationEntry entry, long now, bool allowNew = true)
    {
        Expire(now);
        if (EntryId is not null)
        {
            if (Widget != entry.Widget) return false;
            EntryId = entry.Id; return true; // Same burst, same expiry.
        }
        if (!allowNew || now < nextAllowed) return false;
        EntryId = entry.Id; Widget = entry.Widget;
        ExpiresAt = now + (entry.Content?.DurationMilliseconds ?? (long)VisibleDuration.TotalMilliseconds);
        nextAllowed = ExpiresAt + (long)QuietInterval.TotalMilliseconds;
        return true;
    }
    internal void Expire(long now) { if (EntryId is not null && now >= ExpiresAt) Hide(); }
    internal void Hide() { EntryId = null; Widget = null; }
}
