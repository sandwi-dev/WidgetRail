using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetWebSessionMicrophoneCapabilities
{
    public static WidgetCapabilityOperation<WebSessionCommand, WebSessionReply> Request { get; } =
        new(WidgetWebSessionCapabilities.CapabilityId,"web-session.microphone");
}

public sealed partial class WidgetWebSession
{
    /// <summary>Ask the host to admit this backend document's microphone from an interactive
    /// widget. The host chooses the current origin/profile and owns the native consent dialog.
    /// Never call from a polling loop. A grant does not imply OS/device access. Cancellation
    /// retires an outstanding prompt; it cannot undo microphone use already authorized.
    /// Losing interactive ownership returns Cancelled. Explicit caller/session cancellation
    /// throws OperationCanceledException; unavailable or incompatible services remain errors.</summary>
    public async ValueTask<WebSessionMicrophoneResult> RequestMicrophoneAsync(CancellationToken cancellationToken = default)
    {
        var result = await RequestPermissionAsync(WebSessionPermissionKind.Microphone, cancellationToken).ConfigureAwait(false);
        return new((WebSessionMicrophoneState)result.State, result.Remembered);
    }
}
