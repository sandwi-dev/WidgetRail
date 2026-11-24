using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Volatile, addressed summaries. Subscribe before the first read so a concurrent update cannot be missed.</summary>
public static class WidgetSummaryCapabilities
{
    public const string ReadPermission = "widgets.summary.read.v1";
    public const string PublishPermission = "widgets.summary.publish.v1";
    public static WidgetCapabilityOperation<ReadWidgetSummary, WidgetSummaryValue> ReadFrom(string provider) =>
        new(WidgetSummaryScope.ForProvider(provider), "summary.read");
    public static WidgetCapabilityEvent<WidgetCapabilityAcknowledgement> ChangedFrom(string provider) =>
        new(WidgetSummaryScope.ForProvider(provider), "summary.changed");
    private const string ProviderService = "widgets.summary.provider.v1";
    public static WidgetCapabilityOperation<ReadWidgetSummary, WidgetSummaryValue> Read { get; } = new(ReadPermission, "summary.read");
    public static WidgetCapabilityOperation<PublishWidgetSummary, WidgetCapabilityAcknowledgement> Publish { get; } = new(ProviderService, "summary.publish");
    public static WidgetCapabilityEvent<WidgetCapabilityAcknowledgement> Changed { get; } = new(ReadPermission, "summary.changed");
    public static WidgetCapabilityOperation<CheckWidgetSummaryReader, WidgetCapabilityAcknowledgement> CheckReader { get; } = new(ProviderService, "summary.check-reader");
    public static WidgetCapabilityOperation<WidgetSummaryMicrophone, WebSessionMicrophoneResult> RequestMicrophone { get; } = new(ProviderService, "summary.microphone");
    public static WidgetCapabilityOperation<WidgetSummaryPin, WidgetPinReceipt> SelectPin { get; } = new(ProviderService, "summary.select-pin");
}
