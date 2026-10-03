using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetCaptureCapabilities
{
    public const string Permission = "system.apps.windows.capture.v1";
    public static WidgetCapabilityOperation<WindowCaptureRequest, WindowCaptureTicket> Request { get; } = new(Permission, "capture.request");
    public static WidgetCapabilityOperation<WindowCaptureTicket, WindowCaptureStatus> Status { get; } = new(Permission, "capture.status");
    public static WidgetCapabilityOperation<WindowCaptureTicket, WidgetCapabilityAcknowledgement> Cancel { get; } = new(Permission, "capture.cancel");
    public static WidgetCapabilityOperation<CaptureAttachmentRequest, WidgetCapabilityAcknowledgement> Discard { get; } = new(Permission, "capture.discard");
    public static WidgetCapabilityOperation<CaptureReadRequest, CaptureReadChunk> Read { get; } = new(Permission, "capture.read");
}

/// <summary>Bounded host-confirmed captures of the foreground application.</summary>
public sealed class WidgetCaptureService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetCaptureService(IWidgetCapabilityClient client) => this.client = client;
    /// <summary>Capture the foreground application when the host's preparation countdown ends. Does not activate an application.</summary>
    public async ValueTask<WindowCaptureTicket> RequestAsync(WindowCaptureKind kind, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Invalid capture kind.", nameof(kind));
        var result = await client.InvokeAsync(WidgetCaptureCapabilities.Request, new(kind), cancellationToken).ConfigureAwait(false);
        if (!CaptureLimits.ValidToken(result?.RequestId)) throw Malformed();
        return result!;
    }
    public async ValueTask<WindowCaptureStatus> GetStatusAsync(WindowCaptureTicket ticket, CancellationToken cancellationToken = default)
    {
        DemandToken(ticket.RequestId);
        var result = await client.InvokeAsync(WidgetCaptureCapabilities.Status, ticket, cancellationToken).ConfigureAwait(false);
        if (result is null || result.RequestId != ticket.RequestId || !Enum.IsDefined(result.Phase) ||
            result.Attachment is { } attachment && !attachment.IsWellFormed() ||
            (result.Phase == WindowCapturePhase.Ready) != (result.Attachment is not null) || result.ErrorCode?.Length > 80) throw Malformed();
        return result;
    }
    public async ValueTask CancelAsync(WindowCaptureTicket ticket, CancellationToken cancellationToken = default)
    {
        DemandToken(ticket.RequestId);
        var response = await client.InvokeAsync(WidgetCaptureCapabilities.Cancel, ticket, cancellationToken).ConfigureAwait(false);
        if (response?.Acknowledged != true) throw Malformed();
    }
    public async ValueTask DiscardAsync(CaptureAttachment attachment, CancellationToken cancellationToken = default)
    {
        DemandToken(attachment.Id);
        var response = await client.InvokeAsync(WidgetCaptureCapabilities.Discard, new(attachment.Id), cancellationToken).ConfigureAwait(false);
        if (response?.Acknowledged != true) throw Malformed();
    }
    /// <summary>Read only this widget's capture; callers must obtain separate consent before uploading it.</summary>
    public async Task<byte[]> ReadContentAsync(CaptureAttachment attachment, CancellationToken cancellationToken = default)
    {
        if (!attachment.IsWellFormed()) throw new ArgumentException("Invalid capture attachment.", nameof(attachment));
        using var output = new MemoryStream((int)attachment.ByteLength);
        while (output.Length < attachment.ByteLength)
        {
            var chunk = await client.InvokeAsync(WidgetCaptureCapabilities.Read, new(attachment.Id, output.Length), cancellationToken).ConfigureAwait(false);
            if (chunk is null || chunk.Offset != output.Length || chunk.Bytes is not { Length: > 0 and <= CaptureLimits.ChunkBytes } ||
                output.Length + chunk.Bytes.Length > attachment.ByteLength || chunk.EndOfFile != (output.Length + chunk.Bytes.Length == attachment.ByteLength)) throw Malformed();
            output.Write(chunk.Bytes);
        }
        return output.ToArray();
    }
    private static void DemandToken(string token) { if (!CaptureLimits.ValidToken(token)) throw new ArgumentException("Invalid capture reference."); }
    private static WidgetCapabilityException Malformed() => new("malformed_response", "Capture response is invalid.");
}
