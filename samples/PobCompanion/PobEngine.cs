using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

public sealed record PobResult
{
    public PobResult(JsonElement state, string xml)
    {
        State = state;
        Xml = xml;
        Capabilities = PobEngineCapabilities.Read(state);
        Configuration = PobConfigurationCatalog.Read(state);
    }

    public JsonElement State { get; }
    public string Xml { get; }
    internal PobEngineCapabilities Capabilities { get; }
    internal PobConfigurationCatalog Configuration { get; }

    internal static PobResult Read(JsonElement envelope)
    {
        if (envelope.ValueKind != JsonValueKind.Object || !envelope.TryGetProperty("ok", out var ok) ||
            ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new IOException("PoB returned an invalid response.");
        if (!ok.GetBoolean())
            throw new PobCommandException(envelope.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString()! : "PoB could not complete this change.");
        if (!envelope.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("xml", out var xml) || xml.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(xml.GetString()))
            throw new IOException("PoB returned incomplete build data. The current build has not been replaced.");
        return new(state.Clone(), xml.GetString()!);
    }
}

/// <summary>One serialized engine session. Cancellation retires the process; late results cannot affect another build.</summary>
public sealed class PobEngine(string engineDirectory, string runtimeDirectory, string adapterPath) : IAsyncDisposable
{
    private readonly SemaphoreSlim serial = new(1);
    private Process? process;
    private Task? diagnostics;
    private CancellationTokenSource? diagnosticsCancellation;
    private string unread = "";
    private Decoder decoder = Encoding.UTF8.GetDecoder();
    private string? lastXml;
    internal TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public int? ProcessId => process is { HasExited: false } p ? p.Id : null;
    public long WorkingSet { get { if (process is not { HasExited: false } p) return 0; p.Refresh(); return p.WorkingSet64; } }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<PobResult> ExecuteAsync(object command, CancellationToken token, string? expectedState = null, bool compare = false) =>
        ExecuteCoreAsync(command, token, expectedState, compare, readOnly: false);

    /// <summary>Calculate another build inside the existing serialized engine, then restore the caller's exact baseline.
    /// Interrupted projections retire the process and retain only that baseline for the next operation.</summary>
    internal Task<PobResult> ProjectReadOnlyAsync(object command, CancellationToken token, string expectedState, bool compare = false)
    {
        var request = JsonSerializer.SerializeToElement(command, Json);
        if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("op", out var operation) ||
            operation.ValueKind != JsonValueKind.String || operation.GetString() is not ("load" or "loadout"))
            throw new IOException("Read-only comparison supports build and loadout projections only.");
        return ExecuteCoreAsync(command, token, expectedState, compare, readOnly: true);
    }

    private async Task<PobResult> ExecuteCoreAsync(object command, CancellationToken token, string? expectedState, bool compare, bool readOnly)
    {
        await serial.WaitAsync(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(CommandTimeout);
        var baselineRejected = false;
        try
        {
            var started = await EnsureStartedAsync(deadline.Token);
            // A rejected import or interrupted save must not make the next edit
            // operate on a different build from the one visible in the widget.
            var restore = expectedState ?? (started ? lastXml : null);
            if (restore is not null && (started || restore != lastXml))
            {
                try { await ExchangeAsync(new { op = "load", xml = restore, name = "Working build" }, deadline.Token); }
                catch (PobCommandException error) when (started || readOnly)
                {
                    // Do not keep retrying a failed cached session ahead of a
                    // caller's explicit saved-build recovery attempt.
                    baselineRejected = true;
                    lastXml = null; Retire();
                    throw new IOException("The previous engine session could not be restored. Reopen the saved build to continue.", error);
                }
            }
            if (compare)
            {
                var request = JsonSerializer.SerializeToNode(command, Json)!.AsObject();
                request["compare"] = true; command = request;
            }
            var projected = await ExchangeAsync(command, deadline.Token);
            if (readOnly)
            {
                await ExchangeAsync(new { op = "load", xml = expectedState!, name = "Working build" }, deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                lastXml = expectedState;
            }
            return projected;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            if (readOnly) lastXml = baselineRejected ? null : expectedState;
            Retire();
            throw new IOException("The build engine did not respond in time. Retry the action; completed changes remain saved.");
        }
        catch (PobCommandException) { if (readOnly) { lastXml = baselineRejected ? null : expectedState; Retire(); } throw; }
        catch { if (readOnly) lastXml = baselineRejected ? null : expectedState; Retire(); throw; }
        finally { serial.Release(); }
    }

    private async Task<PobResult> ExchangeAsync(object command, CancellationToken token)
    {
        var line = JsonSerializer.Serialize(command, Json);
        if (Encoding.UTF8.GetByteCount(line) > 6 * 1024 * 1024) throw new IOException("Build request is too large.");
        await process!.StandardInput.WriteLineAsync(line.AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
        using var parsed = JsonDocument.Parse(await ReadLineAsync(process.StandardOutput, token), new() { MaxDepth = 64 });
        var result = PobResult.Read(parsed.RootElement);
        lastXml = result.Xml;
        return result;
    }

    private async Task<bool> EnsureStartedAsync(CancellationToken token)
    {
        if (process is { HasExited: false }) return false;
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
        var drainCancellation=new CancellationTokenSource();
        diagnosticsCancellation=drainCancellation;
        // Drain without retaining build content or unbounded upstream diagnostics.
        diagnostics = Task.Run(async () =>
        {
            try { var buffer = new char[4096]; while (await stderr.ReadAsync(buffer.AsMemory(),drainCancellation.Token) > 0) { } }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) { }
        });
        using var hello = JsonDocument.Parse(await ReadLineAsync(process.StandardOutput, token));
        if (!hello.RootElement.TryGetProperty("ready", out var ready) || !ready.GetBoolean()) throw new IOException("Build engine initialization failed.");
        return true;
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
        var drainCancellation=diagnosticsCancellation;diagnosticsCancellation=null;
        drainCancellation?.Cancel();
        if(drainCancellation is not null)
        {
            if(diagnostics is { } drain)_=drain.ContinueWith(_=>drainCancellation.Dispose(),TaskScheduler.Default);
            else drainCancellation.Dispose();
        }
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
