namespace WidgetRail.OverlayPlatformClient;

/// <summary>Standalone recovery ABI. It has no controller session or initialization methods.</summary>
public interface IControllerIsolationRecoveryNative
{
    uint GetAbiVersion();
    PlatformStatus RecoverControllerIsolation(Span<char> message);
}

public sealed record ControllerIsolationRecoveryResult(PlatformStatus Status, string Message)
{
    public bool Succeeded => Status == PlatformStatus.Ok;
}

/// <summary>
/// Invokes only the native recovery export. The native owner lease, journal
/// validation and conflict policy remain authoritative; no routing is initialized.
/// </summary>
public static class ControllerIsolationRecovery
{
    public static ControllerIsolationRecoveryResult Recover() => Recover(new OverlayPlatformNative());

    public static ControllerIsolationRecoveryResult Recover(IControllerIsolationRecoveryNative native)
    {
        ArgumentNullException.ThrowIfNull(native);
        if (native.GetAbiVersion() != PlatformAbi.Version)
            return new(PlatformStatus.InvalidVersion, "Controller isolation recovery requires the matching native platform library. Repair or update WidgetRail.");
        Span<char> message = stackalloc char[512];
        message.Clear();
        var status = native.RecoverControllerIsolation(message);
        var end = message.IndexOf('\0');
        var text = new string(message[..(end < 0 ? message.Length : end)]);
        if (string.IsNullOrWhiteSpace(text))
            text = status == PlatformStatus.Ok ? "Controller isolation recovery completed."
                : $"Controller isolation recovery could not complete ({status}).";
        return new(status, text);
    }
}
