using System.IO.Pipes;

namespace GameBarAlternative.PlatformBroker;

internal static class BrokerPipeNames
{
    internal static void Validate(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || pipeName.Length > 200)
            throw new ArgumentException("Broker pipe name is invalid.", nameof(pipeName));
        if (pipeName.Contains('\\') || pipeName.Any(char.IsControl))
            throw new ArgumentException("Broker pipe name is invalid.", nameof(pipeName));
    }

    internal static void ValidateAppContainerSid(string? sid)
    {
        if (sid is null) return;
        if (sid.Length is < 16 or > 184 ||
            !sid.StartsWith("S-1-15-2-", StringComparison.Ordinal) ||
            sid[1..].Any(character => !char.IsAsciiDigit(character) && character != '-'))
            throw new ArgumentException("The isolated broker client SID is invalid.", nameof(sid));
    }

    internal static NamedPipeServerStream CreateServer(string pipeName, string? appContainerSid)
    {
        if (appContainerSid is null)
        {
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance,
                4096,
                4096);
        }

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AppContainer broker pipes require Windows.");
        return CreateIsolatedWindowsServer(pipeName, appContainerSid);
    }

    private static NamedPipeServerStream CreateIsolatedWindowsServer(
        string pipeName,
        string appContainerSid) =>
        WindowsIsolatedPipeFactory.Create(pipeName, appContainerSid);
}
