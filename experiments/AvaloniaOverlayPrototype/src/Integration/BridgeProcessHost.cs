using System.Diagnostics;
using System.ComponentModel;
using GameBarAlternative.AvaloniaPrototype.Lifecycle;
using GameBarAlternative.WidgetPresentationSession;

namespace GameBarAlternative.AvaloniaPrototype.Integration;

internal sealed class BridgeProcessHost : IAsyncDisposable
{
    private readonly Process process;
    private readonly IPresentationSessionClient session;
    private IReadOnlyList<int>? ownedProcessIds;
    private bool stopped;

    private BridgeProcessHost(Process process, IPresentationSessionClient session)
    {
        this.process = process;
        this.session = session;
    }

    public IPresentationSessionClient Session => session;

    public int ProcessId => process.Id;
    public ProcessTreeShutdownEvidence? LastShutdownEvidence { get; private set; }

    public IReadOnlyList<int> CaptureOwnedProcessTree()
    {
        if (ownedProcessIds is not null) return ownedProcessIds;
        try { ownedProcessIds = OwnedProcessTree.CaptureDescendants(process.Id); }
        catch (Win32Exception) { ownedProcessIds = [process.Id]; }
        return ownedProcessIds;
    }

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
        await StopProcessAsync().ConfigureAwait(false);
    }

    public async Task<ProcessTreeShutdownEvidence> StopProcessAsync()
    {
        if (stopped)
            return LastShutdownEvidence ?? new ProcessTreeShutdownEvidence([], [], true, false, 0);
        stopped = true;
        LastShutdownEvidence = await StopOwnedProcessAsync(process, CaptureOwnedProcessTree()).ConfigureAwait(false);
        process.Dispose();
        return LastShutdownEvidence;
    }

    private static async Task<ProcessTreeShutdownEvidence> StopOwnedProcessAsync(
        Process ownedProcess,
        IReadOnlyList<int>? capturedIds = null)
    {
        var elapsed = Stopwatch.StartNew();
        IReadOnlyList<int> ownedIds = capturedIds ?? [ownedProcess.Id];
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (ownedIds.Any(OwnedProcessTree.IsRunning) && DateTime.UtcNow < deadline)
            await Task.Delay(50).ConfigureAwait(false);

        var remaining = ownedIds.Where(OwnedProcessTree.IsRunning).ToArray();
        var forced = remaining.Length > 0;
        if (forced)
        {
            if (!ownedProcess.HasExited) ownedProcess.Kill(entireProcessTree: true);
            foreach (var processId in remaining.Where(processId => processId != ownedProcess.Id))
            {
                try
                {
                    using var child = Process.GetProcessById(processId);
                    if (!child.HasExited) child.Kill(entireProcessTree: true);
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            var forcedDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            while (ownedIds.Any(OwnedProcessTree.IsRunning) && DateTime.UtcNow < forcedDeadline)
                await Task.Delay(25).ConfigureAwait(false);
        }
        elapsed.Stop();
        var finalRemaining = ownedIds.Where(OwnedProcessTree.IsRunning).ToArray();
        return new ProcessTreeShutdownEvidence(
            ownedIds,
            finalRemaining,
            !forced && finalRemaining.Length == 0,
            forced,
            elapsed.Elapsed.TotalMilliseconds);
    }
}

internal sealed record ProcessTreeShutdownEvidence(
    IReadOnlyList<int> OwnedProcessIds,
    IReadOnlyList<int> RemainingProcessIds,
    bool BoundedNormalShutdownPassed,
    bool ForcedTerminationUsed,
    double DurationMilliseconds);
