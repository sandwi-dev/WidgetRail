using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private readonly WidgetNotificationPublisher feedback;

    // Emit only at a confirmed action outcome. The host owns positioning,
    // expiry and notification preferences; Render/polling never publishes.
    private void PublishFeedback(string message) => feedback.Publish(new("discord-action-result", "Discord")
    {
        Body=message,
        Retention=WidgetNotificationRetention.BannerOnly,
        Interaction=WidgetNotificationInteraction.Informational,
        Visibility=WidgetNotificationVisibility.Always,
        DurationMilliseconds=5000
    });
}
