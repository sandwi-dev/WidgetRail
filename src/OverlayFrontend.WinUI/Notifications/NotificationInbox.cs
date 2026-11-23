using WidgetRail.WidgetProtocol;
namespace WidgetRail.OverlayFrontend.WinUI.Notifications;

internal enum NotificationPresentation { BannerAndList, ListOnly, Off }
internal enum NotificationActivationDisposition { Unavailable, SourceActivated, Handled }

internal sealed record NotificationCallbacks(Func<bool> IsCurrent, Action Shown, Action Clicked, Action Closed, Action Detach);

internal sealed class NotificationEntry(long id, string widget, string profile, string origin, string tag, string title, string body,
    object owner, NotificationCallbacks callbacks)
{
    internal long Id { get; } = id;
    internal WidgetNotification? Content { get; set; }
    internal WebNotificationIntentPayload? NativeIntent { get; set; }
    internal long ContentRevision { get; private set; }
    internal bool Pending => Content?.Retention != WidgetNotificationRetention.BannerOnly;
    internal bool Interactive => Content?.Interaction != WidgetNotificationInteraction.Informational;
    // Native website notifications share this path across browsers, webapps and pop-outs.
    // Ordinary widgets retain their explicit (or SDK-default) visibility policy.
    internal WidgetNotificationVisibility BannerVisibility => Content?.Visibility ?? WidgetNotificationVisibility.Always;
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
internal sealed class NotificationInbox(int capacity = 128) : IDisposable
{
    private readonly int capacityLimit = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly List<NotificationEntry> entries = [];
    private readonly List<NotificationEntry> transient = [];
    private long sequence;
    internal IReadOnlyList<NotificationEntry> Entries => entries;
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
        if (ReferenceEquals(target, transient) && transient.Count >= 32)
        {
            callbacks.Detach(); return null;
        }
        var item = new NotificationEntry(++sequence, widget, profile, origin, Bound(tag, 256), Bound(title, 256), Bound(body, 2048), owner, callbacks) { ReplacesPrevious = replaced, Content = content };
        target.Add(item);
        if (item.Pending)
        {
            if (entries.Count > capacityLimit) Remove(entries[0], notify: true);
            Changed?.Invoke();
        }
        return item;
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
        if (entry is null || entry.Content?.Retention != content.Retention || (entry.Content?.CategoryId ?? WidgetNotificationContract.DefaultCategoryId) != (content.CategoryId ?? WidgetNotificationContract.DefaultCategoryId)) return null;
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
    internal Task<bool> ActivateAsync(long id, Func<NotificationEntry, Task<bool>> activate) =>
        ActivateWithDispositionAsync(id, async entry => await activate(entry)
            ? NotificationActivationDisposition.SourceActivated : NotificationActivationDisposition.Unavailable);

    internal async Task<bool> ActivateWithDispositionAsync(long id, Func<NotificationEntry, Task<NotificationActivationDisposition>> activate)
    {
        var item = Find(id);
        if (item is null || !item.Interactive || item.Activating || !item.Available || !MarkShown(id)) return false;
        item.Activating = true;
        try
        {
            var disposition = await activate(item);
            if (disposition == NotificationActivationDisposition.Unavailable || !Enum.IsDefined(disposition) ||
                !ReferenceEquals(Find(id), item) || !item.Available) return false;
            if (disposition == NotificationActivationDisposition.SourceActivated) item.Callbacks!.Clicked();
            // Original-source activation delivers Clicked without an immediate
            // Closed. A host handler consumes the activation instead: close the
            // source notification once without dispatching its website click.
            if (ReferenceEquals(Find(id), item)) Remove(item, notify: disposition == NotificationActivationDisposition.Handled);
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
        finally { item.Activating = false; Changed?.Invoke(); }
    }
    // A stale document cannot receive a click. Open its widget normally instead,
    // without dispatching callbacks to a replacement document.
    internal Task<bool> OpenAsync(long id, Func<NotificationEntry, Task<bool>> activate,
        Func<NotificationEntry, Task<bool>> openWidget) => OpenWithDispositionAsync(id, async entry => await activate(entry)
            ? NotificationActivationDisposition.SourceActivated : NotificationActivationDisposition.Unavailable, openWidget);

    internal async Task<bool> OpenWithDispositionAsync(long id, Func<NotificationEntry, Task<NotificationActivationDisposition>> activate,
        Func<NotificationEntry, Task<bool>> openWidget)
    {
        var item = Find(id);
        if (item is null || !item.Interactive || item.Activating) return false;
        if (await ActivateWithDispositionAsync(id, activate)) return true;
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
        Changed?.Invoke();
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
        entry.Callbacks = null; entry.Owner = null; entry.NativeIntent = null;
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

internal sealed class NotificationBannerState(Action<long>? released = null)
{
    internal const int MaximumVisible = 3;
    internal const int MaximumWaiting = 8;
    internal const int MaximumWaitMilliseconds = 15_000;
    internal static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(4);

    internal sealed class Slot(NotificationEntry entry, long queuedAt)
    {
        internal NotificationEntry Entry { get; set; } = entry;
        internal long QueuedAt { get; } = queuedAt;
        internal long ExpiresAt { get; set; }
        internal int Additional { get; set; }
        internal bool SameSource(NotificationEntry other) => Entry.Widget == other.Widget && Entry.Profile == other.Profile;
        internal bool Matches(NotificationEntry other) => Entry.Id == other.Id ||
            other.Tag.Length > 0 && SameSource(other) && Entry.Origin == other.Origin && Entry.Tag == other.Tag;
    }

    private readonly List<Slot> visible = [];
    private readonly List<Slot> waiting = [];
    internal IReadOnlyList<Slot> Visible => visible;
    internal IReadOnlyList<Slot> Waiting => waiting;
    internal long Revision { get; private set; }
    internal long? EntryId => visible.Count > 0 ? visible[0].Entry.Id : null;
    internal long ExpiresAt => visible.Count > 0 ? visible[0].ExpiresAt : 0;

    // A tagged replacement can arrive after its old inbox entry has detached.
    // Match its slot before checking ownership on the normal presentation tick.
    internal bool Offer(NotificationEntry entry, long now, bool allowNew = true)
    {
        Expire(now);
        var match = visible.Find(slot => slot.Matches(entry)) ?? waiting.Find(slot => slot.Matches(entry));
        if (match is not null)
        {
            match.Entry = entry;
            Revision++;
            return true;
        }
        if (!allowNew) return false;
        var burst = waiting.Find(slot => slot.SameSource(entry));
        if (burst is not null && (visible.Count(slot => slot.SameSource(entry)) >= 2 || waiting.Count == MaximumWaiting))
        {
            if (burst.Additional < int.MaxValue) burst.Additional++;
            Revision++;
            return false;
        }
        if (waiting.Count == MaximumWaiting) return false;
        waiting.Add(new(entry, now));
        Revision++;
        return true;
    }

    internal void Advance(long now, Predicate<NotificationEntry> eligible, Func<Slot, bool> fits)
    {
        Expire(now);
        RemoveWhere(visible, slot => !eligible(slot.Entry));
        RemoveWhere(waiting, slot => !eligible(slot.Entry));
        while (visible.Count < MaximumVisible)
        {
            // A source with two visible cards yields to other sources until a slot expires.
            var index = waiting.FindIndex(slot => visible.Count(active => active.SameSource(slot.Entry)) < 2 && fits(slot));
            if (index < 0) break;
            var next = waiting[index];
            waiting.RemoveAt(index);
            next.ExpiresAt = now + (next.Entry.Content?.DurationMilliseconds ?? (long)VisibleDuration.TotalMilliseconds);
            visible.Insert(0, next);
            Revision++;
        }
    }

    internal void RemoveWidget(string widget)
    {
        RemoveWhere(visible, slot => slot.Entry.Widget == widget);
        RemoveWhere(waiting, slot => slot.Entry.Widget == widget);
    }

    internal void Expire(long now)
    {
        RemoveWhere(visible, slot => now >= slot.ExpiresAt);
        RemoveWhere(waiting, slot => now - slot.QueuedAt >= MaximumWaitMilliseconds);
    }

    internal void Hide()
    {
        RemoveWhere(visible, _ => true);
        RemoveWhere(waiting, _ => true);
    }

    private void RemoveWhere(List<Slot> slots, Predicate<Slot> predicate)
    {
        for (var i = slots.Count - 1; i >= 0; i--)
        {
            var slot = slots[i];
            if (!predicate(slot)) continue;
            slots.RemoveAt(i);
            released?.Invoke(slot.Entry.Id);
            Revision++;
        }
    }
}
