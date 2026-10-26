using System.Diagnostics;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;

namespace WidgetRail.Samples.DiscordPrototype;

// Development-only CDP transport. It never attaches to the user's normal Discord
// client. Only the PID/executable/loopback endpoint recorded by Start-Prototype
// is eligible. No authentication tokens or arbitrary evaluation API leave here.
internal interface IDiscordAdapter : IDisposable
{
    Task<JsonElement> InvokeAsync(string method, object? args, CancellationToken cancellationToken);
}

internal sealed class AdapterClient : IDiscordAdapter
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static string RuntimeFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WidgetRail", "DiscordPrototype", "runtime.json");
    private static readonly string Script = ReadScript();
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(3) };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string owner = Guid.NewGuid().ToString("N");
    private sealed record Runtime(int Port, int Pid, string Executable);

    public async Task<JsonElement> InvokeAsync(string method, object? args, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(18));
        var token = timeout.Token;
        await gate.WaitAsync(token);
        try
        {
            var runtime = JsonSerializer.Deserialize<Runtime>(await File.ReadAllTextAsync(RuntimeFile, token), Json)
                ?? throw new InvalidOperationException("runtime_missing");
            if (runtime.Port is < 1024 or > 65535) throw new InvalidOperationException("runtime_invalid");
            using var process = Process.GetProcessById(runtime.Pid);
            if (!string.Equals(process.MainModule?.FileName, Path.GetFullPath(runtime.Executable), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(runtime.Executable), "vesktop.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("runtime_changed");
            var root = new Uri($"http://127.0.0.1:{runtime.Port}");
            using var response = await http.GetAsync(new Uri(root, "/json/list"), HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            await CopyBoundedAsync(stream, buffer, 256 * 1024, token);
            using var targets = JsonDocument.Parse(buffer.ToArray());
            var candidates = targets.RootElement.EnumerateArray().Where(t =>
                t.GetProperty("type").GetString() == "page" &&
                Uri.TryCreate(t.GetProperty("url").GetString(), UriKind.Absolute, out var uri) &&
                uri.Scheme == "https" && uri.Host == "discord.com").ToArray();
            if (candidates.Length != 1) throw new InvalidOperationException("discord_page_unavailable");
            var ws = new Uri(candidates[0].GetProperty("webSocketDebuggerUrl").GetString()!);
            if (ws.Scheme != "ws" || ws.Host != "127.0.0.1" || ws.Port != runtime.Port || ws.UserInfo.Length != 0)
                throw new InvalidOperationException("endpoint_invalid");
            using var socket = new ClientWebSocket();
            socket.Options.Proxy = null;
            await socket.ConnectAsync(ws, token);
            var envelope = JsonSerializer.Serialize(new { method, args, owner }, Json);
            // Install only once per page lifetime; reload naturally creates a new adapter.
            var expression = $"globalThis.WidgetRailDiscordPrototype?.build===4 ? globalThis.WidgetRailDiscordPrototype.invoke({envelope}) : ({{ok:false,code:globalThis.WidgetRailDiscordPrototype?.build>4?'adapter_newer':'adapter_missing'}})";
            var value = await EvaluateAsync(socket, expression, 1, token);
            if (!value.GetProperty("ok").GetBoolean() && value.GetProperty("code").GetString() == "adapter_missing")
            {
                // No action ran on the missing-adapter branch. Installing and
                // issuing it once is safe; transport failures are never replayed.
                value = await EvaluateAsync(socket, $"(async()=>{{ {Script}\n return await globalThis.WidgetRailDiscordPrototype.invoke({envelope}); }})()", 2, token);
            }
            if (!value.GetProperty("ok").GetBoolean())
                throw new InvalidOperationException(value.GetProperty("code").GetString());
            return value.GetProperty("value").Clone();
        }
        finally { gate.Release(); }
    }

    private static async Task<JsonElement> EvaluateAsync(ClientWebSocket socket, string expression, int requestId, CancellationToken token)
    {
        var request = JsonSerializer.SerializeToUtf8Bytes(new { id = requestId, method = "Runtime.evaluate",
            @params = new { expression, awaitPromise = true, returnByValue = true } });
        await socket.SendAsync(request.AsMemory(), WebSocketMessageType.Text, true, token);
        var chunk = new byte[16 * 1024];
        while (true)
        {
            using var message = new MemoryStream();
            ValueWebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(chunk.AsMemory(), token);
                if (received.MessageType != WebSocketMessageType.Text) throw new IOException("adapter_disconnected");
                if (message.Length + received.Count > 1024 * 1024) throw new IOException("adapter_response_too_large");
                message.Write(chunk, 0, received.Count);
            } while (!received.EndOfMessage);
            using var doc = JsonDocument.Parse(message.ToArray());
            if (!doc.RootElement.TryGetProperty("id", out var id) || id.GetInt32() != requestId) continue;
            if (!doc.RootElement.TryGetProperty("result", out var result) || result.TryGetProperty("exceptionDetails", out _))
                throw new InvalidOperationException("adapter_incompatible");
            return result.GetProperty("result").GetProperty("value").Clone();
        }
    }

    internal static async Task CopyBoundedAsync(Stream input, Stream output, int maximum, CancellationToken token)
    {
        var bytes = new byte[8192];
        int count, total = 0;
        while ((count = await input.ReadAsync(bytes, token)) != 0)
        {
            total += count;
            if (total > maximum) throw new IOException("response_too_large");
            await output.WriteAsync(bytes.AsMemory(0, count), token);
        }
    }

    private static string ReadScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DiscordNativePrototype.adapter.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose() => http.Dispose();
}
