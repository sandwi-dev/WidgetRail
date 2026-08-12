using System.Diagnostics;

namespace GameBarAlternative.WindowsAppLibraryProvider;

internal sealed class WindowsGogLauncher : IWindowsGogLauncher
{
    internal static string DefaultClientPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "GOG Galaxy", "GalaxyClient.exe");

    private readonly string _clientPath;

    internal WindowsGogLauncher() : this(DefaultClientPath) { }

    internal WindowsGogLauncher(string clientPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientPath);
        _clientPath = Path.GetFullPath(clientPath);
    }

    public void Launch(
        string productId,
        string installLocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var process = Process.Start(CreateStartInfo(
            _clientPath, productId, installLocation));
        if (process is null)
            throw new InvalidOperationException(
                "Windows did not accept the GOG Galaxy launch request.");
    }

    internal static ProcessStartInfo CreateStartInfo(
        string clientPath,
        string productId,
        string installLocation)
    {
        if (!Path.IsPathFullyQualified(clientPath) || !File.Exists(clientPath) ||
            productId.Length is <= 0 or > 20 || !productId.All(char.IsAsciiDigit) ||
            !Path.IsPathFullyQualified(installLocation) ||
            !Directory.Exists(installLocation))
            throw new ArgumentException("Validated GOG launch evidence is required.");
        var start = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(clientPath),
            UseShellExecute = false,
        };
        start.ArgumentList.Add("/command=runGame");
        start.ArgumentList.Add($"/gameId={productId}");
        start.ArgumentList.Add($"/path={Path.GetFullPath(installLocation)}");
        return start;
    }
}
