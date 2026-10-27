namespace WidgetRail.WidgetProtocol;

/// <summary>Requests the caller's own published layout. No widget, window or focus identity is caller-supplied.</summary>
public sealed record WidgetPinRequest(string LayoutId)
{
    public bool IsWellFormed() => LayoutId is { Length: > 0 and <= 128 } &&
        !LayoutId.StartsWith("host.", StringComparison.Ordinal) &&
        LayoutId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}

/// <summary>Admission only. The host may reject a stale layout or the user may cancel replacement.</summary>
public sealed record WidgetPinReceipt(bool Accepted);
