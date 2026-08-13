using System.Diagnostics;
using GameBarAlternative.WidgetPresentationSession;

namespace GameBarAlternative.AvaloniaPrototype.Integration;

internal sealed class BridgeProcessHost : IAsyncDisposable
{
    private readonly Process process;
    private readonly IPresentationSessionClient session;

    private BridgeProcessHost(Process process, IPresentationSessionClient session)
    {
        this.process = process;
        this.session = session;
    }

    public IPresentationSessionClient Session => session;

    public int ProcessId => process.Id;

    public static async Task<BridgeProcessHost> StartAsync(
        string installationRoot,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(installationRoot);
        var executable = Path.Combine(root, "runtime", "Bridge", "WidgetBridge.exe");
        var catalog = Path.Combine(root, "widget-catalog.json");
        if (!File.Exists(executable) || !File.Exists(catalog))
        {
            throw new FileNotFoundException(
                "The retained WidgetBridge runtime is missing. Build src/OverlayHost Release first.",
                !File.Exists(executable) ? executable : catalog);
        }

        var pipeName = $"gba-avp-{Environment.ProcessId}-{Environment.TickCount64}";
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--host-pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--catalog");
        startInfo.ArgumentList.Add(catalog);
        startInfo.ArgumentList.Add("--accept-timeout-ms");
        startInfo.ArgumentList.Add("10000");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("WidgetBridge could not be started.");
        try
        {
            var session = await GameBarAlternative.WidgetPresentationSession.WidgetPresentationSession.ConnectAsync(
                pipeName,
                new WidgetPresentationSessionOptions
                {
                    ClientName = "AvaloniaOverlayPrototype",
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                },
                cancellationToken).ConfigureAwait(false);
            return new BridgeProcessHost(process, new PresentationSessionClient(session));
        }
        catch
        {
            await StopOwnedProcessAsync(process).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await session.DisposeAsync().ConfigureAwait(false);
        await StopOwnedProcessAsync(process).ConfigureAwait(false);
        process.Dispose();
    }

    private static async Task StopOwnedProcessAsync(Process ownedProcess)
    {
        if (ownedProcess.HasExited) return;
        try
        {
            await ownedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (!ownedProcess.HasExited)
            {
                ownedProcess.Kill(entireProcessTree: true);
                await ownedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }
    }
}
