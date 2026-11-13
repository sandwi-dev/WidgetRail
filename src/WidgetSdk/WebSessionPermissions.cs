using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetWebSessionPermissionCapabilities
{
    public static WidgetCapabilityOperation<WebSessionCommand, WebSessionReply> Request { get; } =
        new(WidgetWebSessionCapabilities.CapabilityId, "web-session.permission");
}

public sealed partial class WidgetWebSession
{
    /// <summary>Request a website permission from an interactive backend session.
    /// The host owns consent; this does not reload the page or change OS settings.
    /// A saved block is respected. Never call from a polling loop. Losing interactive
    /// ownership returns Cancelled; explicit caller/session cancellation throws.
    /// A completed grant cannot be undone by cancelling the request afterward.</summary>
    public async ValueTask<WebSessionPermissionResult> RequestPermissionAsync(WebSessionPermissionKind kind, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        DemandActive();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        linked.Token.ThrowIfCancellationRequested();
        WebSessionReply reply;
        try
        {
            // Microphone keeps its original operation so new widgets still work on older hosts.
            var legacy = kind == WebSessionPermissionKind.Microphone;
            var command = legacy ? new WebSessionCommand(WebSessionOperation.RequestMicrophone) { Session = Reference }
                : new(WebSessionOperation.RequestPermission) { Session = Reference, PermissionKind = kind };
            reply = await client.InvokeAsync(legacy ? WidgetWebSessionMicrophoneCapabilities.Request : WidgetWebSessionPermissionCapabilities.Request,
                command, linked.Token).ConfigureAwait(false);
        }
        catch (WidgetCapabilityException error) when (error.ErrorCode is "request_canceled" or "lifecycle_denied")
        {
            linked.Token.ThrowIfCancellationRequested();
            return new(WebSessionPermissionState.Cancelled);
        }
        linked.Token.ThrowIfCancellationRequested();
        if (kind == WebSessionPermissionKind.Microphone)
        {
            if (reply?.Microphone?.IsWellFormed() != true)
                throw new WidgetCapabilityException("malformed_response", "Invalid microphone admission response.");
            return new((WebSessionPermissionState)reply.Microphone.State, reply.Microphone.Remembered);
        }
        if (reply?.Permission?.IsWellFormed() != true)
            throw new WidgetCapabilityException("malformed_response", "Invalid website permission response.");
        return reply.Permission;
    }
}
