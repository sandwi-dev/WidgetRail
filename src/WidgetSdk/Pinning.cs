using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetPinningCapabilities
{
    public const string Service = "presentation.pinning.v1";
    public static WidgetCapabilityOperation<WidgetPinCloseRequest, WidgetPinReceipt> Close { get; } = new(Service,"pin.close");
    public static WidgetCapabilityOperation<WidgetPinRequest, WidgetPinReceipt> Open { get; } = new(Service,"pin.open");
    public static WidgetCapabilityOperation<WidgetPinManageRequest, WidgetPinReceipt> Manage { get; } = new(Service,"pin.manage");
}

/// <summary>Requests a pin without taking foreground from an admitted user action. The host owns replacement,
/// geometry and focus. The layout's Interaction policy controls later content input.
/// Await this within the action handler; background work cannot create pins.</summary>
public sealed class WidgetPinningService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetPinningService(IWidgetCapabilityClient client) => this.client = client;
    /// <summary>Opens host management for the caller's pins. The host selects an
    /// exact current pin, including compact media, without exposing window IDs.</summary>
    public ValueTask<WidgetPinReceipt> RequestManageAsync(CancellationToken cancellationToken = default) =>
        client.InvokeAsync(WidgetPinningCapabilities.Manage,new WidgetPinManageRequest(),cancellationToken);

    /// <summary>Opens host management for the selection captured by this user action.</summary>
    public ValueTask<WidgetPinReceipt> RequestManageAsync(WidgetPinCloseRequest selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if(!selection.IsWellFormed())throw new ArgumentException("Invalid pin selection.",nameof(selection));
        return client.InvokeAsync(WidgetPinningCapabilities.Manage,
            new WidgetPinManageRequest(selection.LayoutId,selection.SelectionId),cancellationToken);
    }
    /// <summary>Close the selection captured when the user-facing action was authored.</summary>
    public ValueTask<WidgetPinReceipt> RequestCloseAsync(WidgetPinCloseRequest selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!selection.IsWellFormed()) throw new ArgumentException("Invalid pin selection.", nameof(selection));
        return client.InvokeAsync(WidgetPinningCapabilities.Close,selection,cancellationToken);
    }

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
