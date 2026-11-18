using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class FileServiceTests
{
    private static readonly WidgetFileReference File = new(new string('a', 32), "large.mp4", "video/mp4", 4L * 1024 * 1024 * 1024, DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeMilliseconds());
    internal static async Task Stream()
    {
        var calls = new List<WidgetFileReadRequest>();
        var released = 0;
        var host = new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetFileCapabilities.Get, (_, _) => ValueTask.FromResult(File))
            .WithHandler(WidgetFileCapabilities.Read, (request, _) =>
            {
                calls.Add(request);
                return ValueTask.FromResult(new WidgetFileReadChunk(request.Offset, Enumerable.Repeat((byte)42, request.Count).ToArray(), request.Offset + request.Count == File.ByteLength));
            })
            .WithHandler(WidgetFileCapabilities.Release, (_, _) => { released++; return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); }).Build();
        using (var stream = await host.Files.OpenReadAsync(File))
        {
            Check(calls.Count == 0 && stream.Length == File.ByteLength, "Opening eagerly read file data");
            var destination = new byte[256 * 1024];
            Check(await stream.ReadAsync(destination) == WidgetFileContract.ChunkBytes, "Read was not bounded");
            stream.Seek(-3, SeekOrigin.End);
            Check(await stream.ReadAsync(destination) == 3 && stream.Position == File.ByteLength, "Long seek failed");
            Check(await stream.ReadAsync(destination) == 0 && calls.Count == 2, "EOF triggered another read");
            Check(calls.All(call => call.Count <= WidgetFileContract.ChunkBytes), "Unbounded broker transfer");
        }
        Check(released == 0, "Disposing stream unexpectedly released composer's reference");
        await host.Files.ReleaseAsync(File);
        Check(released == 1, "Explicit release was not forwarded");
    }

    internal static async Task Malformed()
    {
        foreach (var fault in new[] { "offset", "short", "oversized", "eof" })
        {
            var host = new WidgetTestHostServicesBuilder().WithHandler(WidgetFileCapabilities.Get, (_, _) => ValueTask.FromResult(File))
                .WithHandler(WidgetFileCapabilities.Read, (request, _) => ValueTask.FromResult(new WidgetFileReadChunk(
                    fault == "offset" ? request.Offset + 1 : request.Offset,
                    new byte[fault == "short" ? request.Count - 1 : fault == "oversized" ? request.Count + 1 : request.Count], fault == "eof"))).Build();
            using var stream = await host.Files.OpenReadAsync(File);
            var bytes = Enumerable.Repeat((byte)7, 4).ToArray();
            try { _ = await stream.ReadAsync(bytes); throw new Exception("Malformed chunk accepted"); } catch (InvalidDataException) { }
            Check(stream.Position == 0 && bytes.All(b => b == 7), "Malformed data leaked to caller");
        }
        var changed = new WidgetTestHostServicesBuilder().WithHandler(WidgetFileCapabilities.Get, (_, _) => ValueTask.FromResult(File with { ByteLength = 1 })).Build();
        try { await changed.Files.OpenReadAsync(File); throw new Exception("Changed metadata accepted"); } catch (InvalidDataException) { }
        var identified = File with { ContentIdentity = new string('a', 32) };
        foreach (var identity in new string?[] { null, new string('b', 32) })
        {
            var replaced = new WidgetTestHostServicesBuilder().WithHandler(WidgetFileCapabilities.Get,
                (_, _) => ValueTask.FromResult(File with { ContentIdentity = identity })).Build();
            try { await replaced.Files.OpenReadAsync(identified); throw new Exception("Changed file identity accepted"); }
            catch (InvalidDataException) { }
        }
    }

    internal static async Task Cancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new WidgetTestHostServicesBuilder().WithHandler(WidgetFileCapabilities.Get, (_, _) => ValueTask.FromResult(File))
            .WithHandler(WidgetFileCapabilities.Read, async (_, token) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return new WidgetFileReadChunk(0, [], false); }).Build();
        var stream = await host.Files.OpenReadAsync(File);
        var read = stream.ReadAsync(new byte[10]).AsTask();
        await started.Task;
        stream.Dispose();
        try { await read.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Disposed read completed"); } catch (OperationCanceledException) { }
        Check(!stream.CanRead, "Disposed stream remained readable");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
