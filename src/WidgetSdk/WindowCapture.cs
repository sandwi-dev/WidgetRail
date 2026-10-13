using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetCaptureCapabilities
{
    public const string Permission = "system.apps.windows.capture.v1";
    public static WidgetCapabilityOperation<WindowCaptureRequest, WindowCaptureTicket> Request { get; } = new(Permission, "capture.request");
    public static WidgetCapabilityOperation<WindowCaptureRequest, WindowCaptureTicket> OriginalScreenshot { get; } = new(Permission, "capture.request-original");
    public static WidgetCapabilityOperation<WindowCaptureTicket, WindowCaptureStatus> Status { get; } = new(Permission, "capture.status");
    public static WidgetCapabilityOperation<WindowCaptureTicket, WidgetCapabilityAcknowledgement> Cancel { get; } = new(Permission, "capture.cancel");
    public static WidgetCapabilityOperation<CaptureAttachmentRequest, WidgetCapabilityAcknowledgement> Discard { get; } = new(Permission, "capture.discard");
    public static WidgetCapabilityOperation<CaptureReadRequest, CaptureReadChunk> Read { get; } = new(Permission, "capture.read");
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, CaptureCapabilities> Capabilities { get; } = new(Permission, "capture.capabilities");
    public static WidgetCapabilityOperation<CaptureRecordingQuery, CaptureCapabilities> RecordingCapabilities { get; } = new(Permission, "capture.recording-capabilities");
    public static WidgetCapabilityOperation<CaptureSessionRequest, CaptureConfigurationValidation> Validate { get; } = new(Permission, "capture.validate");
    public static WidgetCapabilityOperation<CaptureSessionRequest, WindowCaptureTicket> Start { get; } = new(Permission, "capture.start");
    public static WidgetCapabilityOperation<WindowCaptureTicket, WidgetCapabilityAcknowledgement> Stop { get; } = new(Permission, "capture.stop");
}

/// <summary>Bounded host-confirmed captures of the foreground application.</summary>
public sealed class WidgetCaptureService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetCaptureService(IWidgetCapabilityClient client) => this.client = client;
    /// <summary>Read advertised options; this does not open a capture prompt or begin recording.</summary>
    public async ValueTask<CaptureCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var result = await client.InvokeAsync(WidgetCaptureCapabilities.Capabilities, new(), cancellationToken).ConfigureAwait(false);
        if (result is null || !result.IsWellFormed()) throw Malformed();
        return result with { ImageFormats = result.ImageFormats.ToArray(), RecordingProfiles = result.RecordingProfiles.ToArray(), AudioModes = result.AudioModes.ToArray() };
    }
    /// <summary>Discover only profiles supporting these recording settings. Null finds the recommended silent profile. Use GetCapabilitiesAsync for the complete settings catalog.</summary>
    public async ValueTask<CaptureCapabilities> GetRecordingCapabilitiesAsync(RecordingOptions? recording = null, CancellationToken cancellationToken = default)
    {
        var query = new CaptureRecordingQuery(recording);
        if (!query.IsWellFormed()) throw new ArgumentException("Invalid recording options.", nameof(recording));
        var result = await client.InvokeAsync(WidgetCaptureCapabilities.RecordingCapabilities, query, cancellationToken).ConfigureAwait(false);
        if (result is null || !result.IsWellFormed()) throw Malformed();
        return result with { ImageFormats = result.ImageFormats.ToArray(), RecordingProfiles = result.RecordingProfiles.ToArray(), AudioModes = result.AudioModes.ToArray() };
    }
    /// <summary>Check the exact requested combination without recording or silently adjusting it.</summary>
    public async ValueTask<CaptureConfigurationValidation> ValidateAsync(CaptureSessionRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || !request.IsWellFormed()) throw new ArgumentException("Invalid capture options.", nameof(request));
        var result = await client.InvokeAsync(WidgetCaptureCapabilities.Validate, request, cancellationToken).ConfigureAwait(false);
        if (result is null || result.Supported && result.ErrorCode is not null || !result.Supported &&
            (result.ErrorCode is not { Length: > 0 and <= 80 } || result.ErrorCode.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))) throw Malformed();
        return result;
    }
    /// <summary>Begin a host-confirmed screenshot or recording. The host rechecks support at admission.</summary>
    public async ValueTask<WindowCaptureTicket> StartAsync(CaptureSessionRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || !request.IsWellFormed()) throw new ArgumentException("Invalid capture options.", nameof(request));
        var result = await client.InvokeAsync(WidgetCaptureCapabilities.Start, request, cancellationToken).ConfigureAwait(false);
        if (!CaptureLimits.ValidToken(result?.RequestId)) throw Malformed();
        return result!;
    }
    public ValueTask<WindowCaptureTicket> RequestScreenshotAsync(ScreenshotOptions options, CapturePreparation? preparation = null,
        CancellationToken cancellationToken = default) => StartAsync(new() { Screenshot = options, Preparation = preparation ?? new() }, cancellationToken);
    public ValueTask<WindowCaptureTicket> RequestRecordingAsync(RecordingOptions options, CapturePreparation? preparation = null,
        CancellationToken cancellationToken = default) => StartAsync(new() { Recording = options, Preparation = preparation ?? new() }, cancellationToken);
    /// <summary>Finish and retain a configured recording. CancelAsync instead discards it.</summary>
    public async ValueTask StopAsync(WindowCaptureTicket ticket, CancellationToken cancellationToken = default)
    {
        DemandToken(ticket.RequestId);
        var result = await client.InvokeAsync(WidgetCaptureCapabilities.Stop, ticket, cancellationToken).ConfigureAwait(false);
        if (result?.Acknowledged != true) throw Malformed();
    }
    /// <summary>Capture the foreground application when the host's preparation countdown ends. Does not activate an application.</summary>
    public async ValueTask<WindowCaptureTicket> RequestAsync(WindowCaptureKind kind, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Invalid capture kind.", nameof(kind));
        var result = await client.InvokeAsync(WidgetCaptureCapabilities.Request, new(kind), cancellationToken).ConfigureAwait(false);
        if (!CaptureLimits.ValidToken(result?.RequestId)) throw Malformed();
        return result!;
    }
    /// <summary>Requests an original-resolution screenshot, or a bounded context image. Video keeps its existing format.</summary>
    public async ValueTask<WindowCaptureTicket> RequestAsync(WindowCaptureKind kind, CaptureImageResolution resolution, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(resolution) || kind == WindowCaptureKind.Video && resolution != CaptureImageResolution.Context)
            throw new ArgumentException("Invalid capture format.");
        var result = await client.InvokeAsync(resolution == CaptureImageResolution.Original ? WidgetCaptureCapabilities.OriginalScreenshot : WidgetCaptureCapabilities.Request, new(kind, resolution), cancellationToken).ConfigureAwait(false);
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
        var response = await client.InvokeAsync(attachment.Origin == CaptureAttachmentOrigin.SavedLibrary ? WidgetCaptureLibraryCapabilities.Release : WidgetCaptureCapabilities.Discard, new(attachment.Id), cancellationToken).ConfigureAwait(false);
        if (response?.Acknowledged != true) throw Malformed();
    }
    /// <summary>Read only this widget's capture; callers must obtain separate consent before uploading it.</summary>
    public async Task<byte[]> ReadContentAsync(CaptureAttachment attachment, CancellationToken cancellationToken = default)
    {
        if (!attachment.IsWellFormed()) throw new ArgumentException("Invalid capture attachment.", nameof(attachment));
        using var output = new MemoryStream((int)attachment.ByteLength);
        await CopyContentToAsync(attachment, output, cancellationToken).ConfigureAwait(false);
        return output.GetBuffer(); // Exact-sized convenience path for small files.
    }
    /// <summary>Stream owned temporary media in bounded chunks without allocating a whole video in widget memory.</summary>
    public async Task CopyContentToAsync(CaptureAttachment attachment, Stream destination, CancellationToken cancellationToken = default)
    {
        if (attachment is null || !attachment.IsWellFormed()) throw new ArgumentException("Invalid capture attachment.", nameof(attachment));
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("The destination must be writable.", nameof(destination));
        long offset = 0;
        while (offset < attachment.ByteLength)
        {
            var chunk = await client.InvokeAsync(attachment.Origin == CaptureAttachmentOrigin.SavedLibrary ? WidgetCaptureLibraryCapabilities.Read : WidgetCaptureCapabilities.Read, new(attachment.Id, offset), cancellationToken).ConfigureAwait(false);
            if (chunk is null || chunk.Offset != offset || chunk.Bytes is not { Length: > 0 and <= CaptureLimits.ChunkBytes } ||
                offset + chunk.Bytes.Length > attachment.ByteLength || chunk.EndOfFile != (offset + chunk.Bytes.Length == attachment.ByteLength)) throw Malformed();
            await destination.WriteAsync(chunk.Bytes, cancellationToken).ConfigureAwait(false);
            offset += chunk.Bytes.Length;
        }
    }
    private static void DemandToken(string token) { if (!CaptureLimits.ValidToken(token)) throw new ArgumentException("Invalid capture reference."); }
    private static WidgetCapabilityException Malformed() => new("malformed_response", "Capture response is invalid.");
}
