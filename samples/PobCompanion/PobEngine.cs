using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

public sealed record PobResult(JsonElement State, string Xml);

/// <summary>One serialized engine session. Cancellation retires the process; late results cannot affect another build.</summary>
public sealed class PobEngine(string engineDirectory, string runtimeDirectory, string adapterPath) : IAsyncDisposable
{
    private readonly SemaphoreSlim serial = new(1);
    private Process? process;
    private Task? diagnostics;
    private string unread = "";
    private Decoder decoder = Encoding.UTF8.GetDecoder();
    private string? lastXml;
    public int? ProcessId => process is { HasExited: false } p ? p.Id : null;
    public long WorkingSet { get { if (process is not { HasExited: false } p) return 0; p.Refresh(); return p.WorkingSet64; } }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PobResult> ExecuteAsync(object command, CancellationToken token, string? expectedState = null)
    {
        await serial.WaitAsync(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await EnsureStartedAsync(deadline.Token);
            // A rejected import or interrupted save must not make the next edit
            // operate on a different build from the one visible in the widget.
            if (expectedState is not null && expectedState != lastXml)
                await ExchangeAsync(new { op = "load", xml = expectedState, name = "Working build" }, deadline.Token);
            return await ExchangeAsync(command, deadline.Token);
        }
        catch (PobCommandException) { throw; }
        catch { Retire(); throw; }
        finally { serial.Release(); }
    }

    private async Task<PobResult> ExchangeAsync(object command, CancellationToken token)
    {
        var line = JsonSerializer.Serialize(command, Json);
        if (Encoding.UTF8.GetByteCount(line) > 6 * 1024 * 1024) throw new IOException("Build request is too large.");
        await process!.StandardInput.WriteLineAsync(line.AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
        using var parsed = JsonDocument.Parse(await ReadLineAsync(process.StandardOutput, token), new() { MaxDepth = 64 });
        var root = parsed.RootElement;
        if (!root.GetProperty("ok").GetBoolean()) throw new PobCommandException(root.GetProperty("error").GetString() ?? "PoB could not complete this change.");
        var result = root.GetProperty("result");
        lastXml = result.GetProperty("xml").GetString()!;
        return new(result.GetProperty("state").Clone(), lastXml);
    }

    private async Task EnsureStartedAsync(CancellationToken token)
    {
        if (process is { HasExited: false }) return;
        Retire();
        var start = new ProcessStartInfo(Path.Combine(runtimeDirectory, "luajit.exe"))
        {
            WorkingDirectory = Path.Combine(await EngineBundle.ResolveAsync(engineDirectory, token), "src"), UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(adapterPath);
        start.Environment["LUA_PATH"] = "../runtime/lua/?.lua;../runtime/lua/?/init.lua;;";
        start.Environment["LUA_CPATH"] = runtimeDirectory.Replace('\\', '/') + "/?.dll;;";
        start.Environment.Remove("CI");
        process = Process.Start(start) ?? throw new IOException("Could not start the build engine.");
        var stderr = process.StandardError;
        // Drain without retaining build content or unbounded upstream diagnostics.
        diagnostics = Task.Run(async () => { try { var buffer = new char[4096]; while (await stderr.ReadAsync(buffer) > 0) { } } catch (IOException) { } catch (ObjectDisposedException) { } });
        using var hello = JsonDocument.Parse(await ReadLineAsync(process.StandardOutput, token));
        if (!hello.RootElement.TryGetProperty("ready", out var ready) || !ready.GetBoolean()) throw new IOException("Build engine initialization failed.");
        if (lastXml is not null)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { op = "load", xml = lastXml, name = "Working build" }, Json).AsMemory(), token);
            await process.StandardInput.FlushAsync(token);
            using var restored = JsonDocument.Parse(await ReadLineAsync(process.StandardOutput, token));
            if (!restored.RootElement.GetProperty("ok").GetBoolean()) throw new IOException("The saved engine session could not be restored.");
        }
    }

    private async Task<string> ReadLineAsync(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new byte[8192];
        var characters = new char[8192];
        while (true)
        {
            if (unread.Length == 0)
            {
                // StreamReader.ReadAsync may wait to fill the requested character
                // count. Read the pipe itself so a short readiness/reply line is
                // delivered immediately without waiting for another request.
                var count = await reader.BaseStream.ReadAsync(buffer.AsMemory(), token);
                if (count == 0) throw new IOException("The build engine stopped unexpectedly.");
                var decoded = decoder.GetChars(buffer, 0, count, characters, 0, flush: false);
                unread = new string(characters, 0, decoded);
            }
            var newline = unread.IndexOf('\n');
            var take = newline < 0 ? unread.Length : newline;
            if (text.Length + take > 8 * 1024 * 1024) throw new IOException("Engine response exceeded its bound.");
            text.Append(unread.AsSpan(0, take));
            unread = newline < 0 ? "" : unread[(newline + 1)..];
            if (newline >= 0) return text.ToString();
        }
    }

    private void Retire()
    {
        var old = process; process = null; unread = ""; decoder = Encoding.UTF8.GetDecoder();
        if (old is null) return;
        try { if (!old.HasExited) old.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        finally { old.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        await serial.WaitAsync();
        try { Retire(); } finally { serial.Release(); }
        if (diagnostics is { } drain) await drain.WaitAsync(TimeSpan.FromSeconds(3));
    }
}

public sealed class PobCommandException(string message) : IOException(message);
