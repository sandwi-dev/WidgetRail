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
    private long revision;

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
            pending = notification;
            needsShow |= !updateOnly;
            revision++;
        }
        return operations.RunLatest(operationKey, async context =>
        {
            try
            {
                await Task.Delay(debounce, clock, context.CancellationToken).ConfigureAwait(false);
                WidgetNotification content; bool show; long sentRevision;
                lock (gate) { content = pending!; show = needsShow; sentRevision = revision; }
                var notifications = service();
                var result = show
                    ? await notifications.ShowAsync(content, context.CancellationToken).ConfigureAwait(false)
                    : await notifications.UpdateAsync(content, context.CancellationToken).ConfigureAwait(false);
                if (result.Accepted)
                    lock (gate) { if (revision == sentRevision) needsShow = false; }
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
