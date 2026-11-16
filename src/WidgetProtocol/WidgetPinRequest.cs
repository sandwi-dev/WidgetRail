namespace WidgetRail.WidgetProtocol;

/// <summary>Requests the caller's own published layout. No widget, window or focus identity is caller-supplied.</summary>
public sealed record WidgetPinRequest(string LayoutId)
{
    public bool IsWellFormed() => LayoutId is { Length: > 0 and <= 128 } &&
        !LayoutId.StartsWith("host.", StringComparison.Ordinal) &&
        LayoutId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}

/// <summary>Identifies one selection lifetime of an authored layout on its owning widget.</summary>
public sealed record WidgetPinCloseRequest(string LayoutId,string SelectionId)
{
    public bool IsWellFormed() => new WidgetPinRequest(LayoutId).IsWellFormed() && Guid.TryParseExact(SelectionId,"N",out _);
}

/// <summary>Manages an exact authored selection, or asks the host to choose among
/// the caller's pins, including host-owned media. No foreign widget/window IDs.</summary>
public sealed record WidgetPinManageRequest(string? LayoutId = null, string? SelectionId = null)
{
    public bool IsWellFormed() => LayoutId is null
        ? SelectionId is null
        : SelectionId is not null && new WidgetPinCloseRequest(LayoutId,SelectionId).IsWellFormed();
}

/// <summary>Admission only. The host may reject a stale layout or the user may cancel replacement.</summary>
public sealed record WidgetPinReceipt(bool Accepted);
