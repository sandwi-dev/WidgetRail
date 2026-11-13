using System.IO.Pipes;
using System.Reflection;
using WidgetRail.WidgetBridge;

internal static class BridgeHandshakeScenarios
{
    internal static async Task CatalogPublicationWaitsForHello()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var name = "handshake-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), name);
        var catalog = new BridgeCatalog([]);
        await using var monitor = new BridgeCatalogMonitor(
            Path.Combine(root, "trusted.json"), Path.Combine(root, "installed"),
            Environment.ProcessPath!, catalog, initialDiagnostics: null,
            installedCatalogPending: true,
            loadCatalog: _ => Task.FromResult(new BridgeCatalogLoadResult(catalog, [])));
        await using var server = new WidgetBridgeServer(name, catalog, catalogMonitor: monitor);
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(deadline.Token);
            // Synchronize with pipe admission without introducing a shipping test hook.
            var channelField = typeof(WidgetBridgeServer).GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!;
            while (channelField.GetValue(server) is null)
                await Task.Delay(1, deadline.Token);
            var channel = new BridgeFrameChannel(pipe, BridgeProtocol.DefaultMaximumMessageBytes);
            var firstFrame = channel.ReadAsync(deadline.Token).AsTask();
            var reload = await monitor.ReloadNowAsync(deadline.Token);
            Check(reload.Published && reload.Current.IsComplete, "Startup catalogue did not change.");
            // No frame is legal until the client sends Hello. Keep the same read pending.
            await Task.WhenAny(firstFrame, Task.Delay(250, deadline.Token));
            Check(!firstFrame.IsCompleted, "Catalogue notification preceded HelloAccepted.");
            await channel.WriteAsync(new() { RequestId = 1, Type = BridgeMessageTypes.Hello,
                Payload = BridgeJson.ToElement(new BridgeHello("handshake-test")) }, deadline.Token);
            var hello = await firstFrame;
            Check(hello.Type == BridgeMessageTypes.HelloAccepted && hello.RequestId == 1,
                "First response was not the correlated acceptance.");
            await channel.WriteAsync(new() { RequestId = 2, Type = BridgeMessageTypes.ListWidgets,
                Payload = BridgeJson.ToElement(new BridgeEmptyPayload()) }, deadline.Token);
            var list = await channel.ReadAsync(deadline.Token);
            Check(list.RequestId == 2 && list.Payload.GetProperty("isComplete").GetBoolean() &&
                list.Payload.GetProperty("revision").GetInt64() == reload.Revision,
                "Catalogue changes during handshake were lost.");
            await channel.WriteAsync(new() { RequestId = 3, Type = BridgeMessageTypes.Stop,
                Payload = BridgeJson.ToElement(new BridgeEmptyPayload()) }, deadline.Token);
            Check((await channel.ReadAsync(deadline.Token)).RequestId == 3, "Stop was not acknowledged.");
            await serving.WaitAsync(deadline.Token);
        }
        finally
        {
            deadline.Cancel();
            pipe.Dispose();
            try { await serving; }
            catch (Exception error) when (error is OperationCanceledException or IOException) { }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
