using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private readonly WidgetNotificationPublisher feedback;

    // Publish at the action result, never from Render or a status subscription.
    // The SDK/host own coalescing, banner placement, expiry and quiet preferences.
    private void UpdateFeedback(Func<State, State> change, bool permissionRequired = false)
    {
        var update = model.Update(state => change(state) with { PermissionRequired = permissionRequired });
        if (update.Current.Message is { } message) PublishFeedback(message);
    }

    private void PublishFeedback(string message) => feedback.Publish(new("capture-result", "Capture Library")
    {
        Body = message,
        Retention = WidgetNotificationRetention.BannerOnly,
        Interaction = WidgetNotificationInteraction.Informational,
        Visibility = WidgetNotificationVisibility.Always,
        DurationMilliseconds = 5000,
    });
}
