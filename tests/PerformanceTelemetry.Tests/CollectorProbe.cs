using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.PerformanceTelemetry;

internal static class CollectorProbe
{
    internal static async Task<int> RunAsync(string executable)
    {
        var name = "wrail-fps-probe-" + Guid.NewGuid().ToString("N");
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
        var start = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--pipe", name, "--parent", Environment.ProcessId.ToString(), "--nonce", nonce }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new Exception("Helper did not launch");
        try
        {
            await pipe.WaitForConnectionAsync(deadline.Token);
            if (!CollectorProtocol.IsClient(pipe, process.Id)) throw new Exception("Wrong helper process");
            var hello = await CollectorProtocol.ReadAsync<CollectorHello>(pipe, deadline.Token);
            if (hello.Version != 1 || hello.Nonce != nonce) throw new Exception("Handshake mismatch");
            await CollectorProtocol.WriteAsync(pipe, new CollectorCommand(false), deadline.Token);
            var idle = await CollectorProtocol.ReadAsync<CollectorReading>(pipe, deadline.Token);
            CollectorProtocol.Validate(idle);
            if (idle.State != "idle") throw new Exception("Helper traced without a collect command");
            CollectorReading reading;
            do
            {
                await CollectorProtocol.WriteAsync(pipe, new CollectorCommand(true), deadline.Token);
                reading = await CollectorProtocol.ReadAsync<CollectorReading>(pipe, deadline.Token);
                CollectorProtocol.Validate(reading);
                await Task.Delay(250, deadline.Token);
            } while (reading.State is "idle" or "collecting");
            // This explicit probe is for the known unelevated account. A real
            // successful trace belongs to the separately authorized FPS check.
            if (reading.State != "permission-required") throw new Exception("Unexpected nonadmin outcome: " + reading.State);
            Console.WriteLine(JsonSerializer.Serialize(new { authenticated = true, idleWithoutDemand = true,
                state = reading.State, measuredProcesses = reading.Processes.Count }));
            pipe.Close();
            await process.WaitForExitAsync(deadline.Token);
            Console.WriteLine("Helper exited after its owner disconnected.");
            return 0;
        }
        finally
        {
            pipe.Close();
            if (!process.HasExited)
            {
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException) { process.Kill(); throw; }
            }
        }
    }
}
