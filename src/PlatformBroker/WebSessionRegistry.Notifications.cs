using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class WebSessionRegistry
{
    // Host-only verification; worker capability calls cannot manufacture a native notification entry.
    public bool AllowsNativeNotificationIntent(string widgetId, BrokerWidgetIdentity identity, WebSessionReference reference)
    {
        if (reference?.IsWellFormed() != true) return false;
        lock (gate)
            return sessions.TryGetValue(reference.Id, out var session) && !session.Owner.Retired &&
                session.Owner.WidgetId == widgetId && session.Owner.Identity == identity &&
                owners.TryGetValue(widgetId, out var owner) && ReferenceEquals(owner, session.Owner) &&
                session.Definition.NativeNotificationIntents;
    }
}
