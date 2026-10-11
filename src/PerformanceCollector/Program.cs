using System.Diagnostics;
using System.IO.Pipes;
using WidgetRail.PerformanceTelemetry;

// No filesystem paths, process commands or arbitrary ETW session names are accepted.
// Elevation is requested by the Bridge only for a user's explicit Enable FPS action.
if (!OperatingSystem.IsWindows() || args.Length != 6 || args[0] != "--pipe" ||
    args[2] != "--parent" || args[4] != "--nonce" || !CollectorProtocol.ValidPipeName(args[1]) ||
    !int.TryParse(args[3], out var parentId) || parentId <= 0 || args[5].Length != 64 || !args[5].All(char.IsAsciiHexDigit)) return 2;

using var lifetime = new CancellationTokenSource();
await using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
try
{
    using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await pipe.ConnectAsync(connect.Token);
    if (!CollectorProtocol.IsServer(pipe, parentId)) return 3;
    using var parent = Process.GetProcessById(parentId);
    await CollectorProtocol.WriteAsync(pipe, new CollectorHello(CollectorProtocol.Version, args[5]), connect.Token);
    var collect = 0;
    var runner = new PresentMonRunner(Path.Combine(AppContext.BaseDirectory, "PresentMon.exe"));
    var worker = runner.RunAsync(() => Volatile.Read(ref collect) != 0, lifetime.Token);
    try
    {
        while (!parent.HasExited)
        {
            using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            heartbeat.CancelAfter(TimeSpan.FromSeconds(10));
            var command = await CollectorProtocol.ReadAsync<CollectorCommand>(pipe, heartbeat.Token);
            Volatile.Write(ref collect, command.Collect ? 1 : 0);
            await CollectorProtocol.WriteAsync(pipe, runner.Read(), heartbeat.Token);
        }
    }
    finally
    {
        lifetime.Cancel();
        try { await worker; } catch (OperationCanceledException) { }
    }
    return 0;
}
catch (Exception error) when (error is IOException or OperationCanceledException or ArgumentException or
    InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
{
    return 4;
}
