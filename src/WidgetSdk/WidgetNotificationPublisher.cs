using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// Coalesces optional notification delivery for one stable key and category.
/// Event eligibility and content remain widget policy; host preferences remain
/// authoritative. Failed deliveries are retried only after another publication.
/// </summary>
public sealed class WidgetNotificationPublisher
{
    private readonly object gate = new();
    private readonly string operationKey, notificationKey, categoryId;
    private readonly WidgetOperations operations;
    private readonly Func<WidgetNotificationService> service;
    private readonly TimeSpan debounce;
    private readonly TimeProvider clock;
    private readonly WidgetOperationLifetime lifetime;
    private WidgetNotification? pending;
    private bool needsShow;
    private bool withdrawn;
    private long eventRevision;

    internal WidgetNotificationPublisher(string operationKey, string notificationKey, string? categoryId,
        TimeSpan debounce, WidgetOperationLifetime lifetime, TimeProvider clock,
        WidgetOperations operations, Func<WidgetNotificationService> service)
    {
        StableIdentifier.Validate(operationKey, nameof(operationKey));
        if (!WidgetNotificationContract.ValidKey(notificationKey)) throw new ArgumentException("Invalid notification key.", nameof(notificationKey));
        categoryId ??= WidgetNotificationContract.DefaultCategoryId;
        if (!WidgetNotificationContract.ValidCategoryId(categoryId)) throw new ArgumentException("Invalid notification category.", nameof(categoryId));
        if (debounce < TimeSpan.Zero || debounce > WidgetTimedMutation.MaximumDelay) throw new ArgumentOutOfRangeException(nameof(debounce));
        if (!Enum.IsDefined(lifetime)) throw new ArgumentOutOfRangeException(nameof(lifetime));
        this.operationKey = operationKey;
        this.notificationKey = notificationKey;
        this.categoryId = categoryId;
        this.debounce = debounce;
        this.lifetime = lifetime;
        this.clock = clock;
        this.operations = operations;
        this.service = service;
    }

    /// <summary>
    /// Submit a new event, or update existing content without resurrecting a
    /// dismissed/expired notification. An update can refine a still-pending new
    /// event. The returned handle tracks the attempt, not banner visibility.
    /// </summary>
    public WidgetOperationHandle Publish(WidgetNotification notification, bool updateOnly = false)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!WidgetNotificationContract.IsValid(notification) || notification.Key != notificationKey ||
            (notification.CategoryId ?? WidgetNotificationContract.DefaultCategoryId) != categoryId)
            throw new ArgumentException("Notification must be valid and belong to this publisher's key and category.", nameof(notification));
        lock (gate)
        {
            // Late metadata must not undo an explicit withdrawal, including
            // one still queued behind a delivery that ignores cancellation.
            if (!updateOnly || !withdrawn) pending = notification;
            if (!updateOnly) { withdrawn = false; needsShow = true; eventRevision++; }
        }
        return Deliver();
    }

    /// <summary>Withdraw this publisher's notification, after any already-running delivery has completed.
    /// Pending metadata cannot resurrect it; a subsequent new-event Publish may show it again.
    /// Uses the publisher's existing lifetime and does not change host preferences.</summary>
    public WidgetOperationHandle Dismiss()
    {
        lock (gate) { pending = null; withdrawn = true; needsShow = false; eventRevision++; }
        return Deliver();
    }

    private WidgetOperationHandle Deliver()
    {
        return operations.RunLatest(operationKey, async context =>
        {
            try
            {
                TimeSpan delay;
                lock (gate) delay = withdrawn ? TimeSpan.Zero : debounce;
                await Task.Delay(delay, clock, context.CancellationToken).ConfigureAwait(false);
                context.CancellationToken.ThrowIfCancellationRequested();
                WidgetNotification? content; bool show; long sentRevision;
                lock (gate) { content = pending; show = needsShow; sentRevision = eventRevision; }
                var notifications = service();
                // RunLatest serializes same-key delegates. Cancellation alone
                // would not order Dismiss after a late external Show.
                var result = content is null
                    ? await notifications.DismissAsync(notificationKey, context.CancellationToken).ConfigureAwait(false)
                    : show
                    ? await notifications.ShowAsync(content, context.CancellationToken).ConfigureAwait(false)
                    : await notifications.UpdateAsync(content, context.CancellationToken).ConfigureAwait(false);
                if (result.Accepted)
                    // Metadata may have changed while this event was delivered.
                    // Its accepted receipt still settles Show, but cannot settle
                    // a genuinely newer event with another event revision.
                    lock (gate) { if (eventRevision == sentRevision) needsShow = false; }
            }
            catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or OperationCanceledException)
            {
                // Optional delivery must not stop the widget's primary work.
            }
        }, lifetime);
    }
}

public abstract partial class Widget
{
    /// <summary>
    /// Creates one publisher with a unique operation key. Widget lifetime permits
    /// background events; choose Active when delivery belongs only to visible UI.
    /// Debounce is between zero and WidgetTimedMutation.MaximumDelay.
    /// </summary>
    protected WidgetNotificationPublisher CreateNotificationPublisher(
        string operationKey, string notificationKey, string? categoryId = null,
        TimeSpan? debounce = null, WidgetOperationLifetime lifetime = WidgetOperationLifetime.Widget,
        TimeProvider? timeProvider = null) => new(operationKey, notificationKey, categoryId,
            debounce ?? TimeSpan.FromMilliseconds(250), lifetime, timeProvider ?? TimeProvider.System,
            Operations, () => HostServices.Notifications);
}
