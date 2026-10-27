using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetPinningCapabilities
{
    public const string Service = "presentation.pinning.v1";
    public static WidgetCapabilityOperation<WidgetPinRequest, WidgetPinReceipt> Open { get; } = new(Service,"pin.open");
}

/// <summary>Requests a passive pin from an admitted user action. The host owns replacement,
/// geometry and focus. Await this within the action handler; background work cannot create pins.</summary>
public sealed class WidgetPinningService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetPinningService(IWidgetCapabilityClient client) => this.client = client;
    public ValueTask<WidgetPinReceipt> RequestOpenAsync(PinnedLayoutHandle layout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return RequestOpenAsync(layout.Id,cancellationToken);
    }
    public ValueTask<WidgetPinReceipt> RequestOpenAsync(string layoutId, CancellationToken cancellationToken = default)
    {
        var request=new WidgetPinRequest(layoutId);
        if(!request.IsWellFormed())throw new ArgumentException("A package-authored layout identifier is required.",nameof(layoutId));
        return client.InvokeAsync(WidgetPinningCapabilities.Open,request,cancellationToken);
    }
}
