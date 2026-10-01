using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Consumes standalone recovery requests before any normal startup policy.</summary>
internal static class ControllerRecoveryCommand
{
    internal const string Flag = "--controller-isolation-recover-only";
    internal const string Title = "WidgetRail Controller Isolation";

    internal static bool TryRun(IReadOnlyList<string> commandLine, IReadOnlyList<string> activation,
        Func<ControllerIsolationRecoveryResult> recover,
        Action<ControllerIsolationRecoveryResult> present, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(activation);
        // Packaged launch may mirror the same vector in both channels. Preserve
        // duplicates within either vector; normal argument de-duplication would
        // accidentally authorize a malformed standalone recovery request.
        var arguments = commandLine.SequenceEqual(activation, StringComparer.Ordinal)
            ? commandLine : commandLine.Concat(activation).ToArray();
        return TryRun(arguments, recover, present, out exitCode);
    }

    internal static bool TryRun(IReadOnlyList<string> arguments,
        Func<ControllerIsolationRecoveryResult> recover,
        Action<ControllerIsolationRecoveryResult> present, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(recover);
        ArgumentNullException.ThrowIfNull(present);
        exitCode = 0;
        if (!arguments.Any(argument => string.Equals(argument, Flag, StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith(Flag + "=", StringComparison.OrdinalIgnoreCase))) return false;
        ControllerIsolationRecoveryResult result;
        if (arguments.Count != 1 || !string.Equals(arguments[0], Flag, StringComparison.OrdinalIgnoreCase))
            result = new(PlatformStatus.InvalidArgument, "Recovery-only must be used alone; it never starts controller routing.");
        else
        {
            try { result = recover(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                var detail = error.Message.Length <= 512 ? error.Message : error.Message[..512];
                result = new(PlatformStatus.ControllerIsolationUnavailable, "Controller isolation recovery could not run. " + detail);
            }
        }
        exitCode = result.Succeeded ? 0 : 1;
        present(result);
        return true;
    }
}
