using System.Buffers.Binary;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.DiscordDesktop.Connection;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
async Task Reject(Func<Task> action, string name)
{
    var rejected = false;
    try { await action(); } catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or ArgumentException) { rejected = true; }
    Check(rejected, name);
}
static JsonElement Json(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
static byte[] Frame(byte[] data)
{
    var bytes = new byte[data.Length + 4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, data.Length); data.CopyTo(bytes, 4); return bytes;
}
static JsonElement Hello(string nonce, string instance) => JsonSerializer.SerializeToElement(new { type = "clientHello", version = 1, clientNonce = nonce, clientInstance = instance });
static JsonElement Proof(string proof) => JsonSerializer.SerializeToElement(new { type = "clientProof", proof });
static string MakeProof(byte[] key, string role, string nonce, JsonElement hello, string instance) =>
    Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"WidgetRail.DiscordDesktop.v1\n{role}\n{nonce}\n{hello.GetProperty("serverNonce").GetString()}\n{hello.GetProperty("connectionId").GetString()}\n{instance}"))).ToLowerInvariant();

// Explicit synthetic interop mode: the parent Node process is the trusted peer,
// a fixed test-only key is used, and no Discord instance/config is consulted.
if (args.Length == 3 && args[0] == "--interop-server")
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    var peer = DiscordProcessIdentity.Capture(int.Parse(args[2]));
    await using var server = new DiscordPipeServer(args[1], peer, Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    Console.WriteLine("LISTENING");
    await using var connection = await server.AcceptAsync(timeout.Token);
    var ping = await connection.ReadObjectAsync(timeout.Token);
    if (ping.GetProperty("type").GetString() != "ping") throw new InvalidDataException("Expected synthetic ping.");
    await connection.WriteObjectAsync(Json("{\"type\":\"pong\"}"), timeout.Token);
    Console.WriteLine("INTEROP PASS");
    return;
}

var value = Json("{\"type\":\"fixture\",\"name\":\"雪 🎮\",\"nested\":{\"value\":3}}");
using (var encoded = new MemoryStream())
{
    await DiscordJsonFrame.WriteAsync(encoded, value);
    Check(BinaryPrimitives.ReadInt32LittleEndian(encoded.ToArray()) == encoded.Length - 4, "wire prefix counts UTF-8 bytes");
    using var fragmented = new FragmentedStream(encoded.ToArray());
    var decoded = await DiscordJsonFrame.ReadAsync(fragmented);
    Check(decoded.GetProperty("name").GetString() == "雪 🎮", "fragmented prefix and multibyte payload round trip");
}
foreach (var text in new[] { "[]", "null", "\"value\"", "{}{}", "{\"a\":1,\"a\":2}", "{\"nested\":{\"a\":1,\"a\":2}}", "{\"a\":1,}", "{/*comment*/\"a\":1}" })
    await Reject(async () => { await DiscordJsonFrame.ReadAsync(new MemoryStream(Frame(Encoding.UTF8.GetBytes(text)))); }, "reject malformed/non-object/ambiguous JSON " + text);
foreach (var bytes in new[] { new byte[] { 0x7B, 0x22, 0x61, 0x22, 0x3A, 0x22, 0xC0, 0xAF, 0x22, 0x7D }, new byte[] { 0x7B, 0x22, 0x61, 0x22, 0x3A, 0x22, 0xF0, 0x9F, 0x22, 0x7D } })
    await Reject(async () => { await DiscordJsonFrame.ReadAsync(new MemoryStream(Frame(bytes))); }, "reject invalid/truncated UTF-8 rather than replace it");
foreach (var size in new[] { 0, -1, DiscordJsonFrame.MaximumPayloadBytes + 1, int.MaxValue })
{
    var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, size);
    await Reject(async () => { await DiscordJsonFrame.ReadAsync(new MemoryStream(header)); }, "reject invalid length before payload allocation " + size);
}
await Reject(async () => { await DiscordJsonFrame.ReadAsync(new MemoryStream([2, 0])); }, "reject incomplete prefix");
await Reject(async () => { await DiscordJsonFrame.ReadAsync(new MemoryStream([5, 0, 0, 0, 123])); }, "reject incomplete payload");
await Reject(async () => { await DiscordJsonFrame.WriteAsync(new MemoryStream(), Json("[]")); }, "writer rejects non-object values");
await Reject(async () => { await DiscordJsonFrame.WriteAsync(new MemoryStream(), JsonSerializer.SerializeToElement(new { data = new string('a', DiscordJsonFrame.MaximumPayloadBytes) })); }, "writer enforces byte bound");
var nested = new string('[', 40) + "0" + new string(']', 40);
await Reject(async () => { await DiscordJsonFrame.ReadAsync(new MemoryStream(Frame(Encoding.UTF8.GetBytes("{\"deep\":" + nested + "}")))); }, "reject excessive JSON depth");
var boundary = string.Concat(Enumerable.Repeat("{\"a\":", 31)) + "{}" + new string('}', 31);
using (var boundaryStream = new MemoryStream())
{
    await DiscordJsonFrame.WriteAsync(boundaryStream, Json(boundary)); boundaryStream.Position = 0;
    _ = await DiscordJsonFrame.ReadAsync(boundaryStream);
    Check(true, "writer and reader agree at 32 nested containers");
}
var beyondBoundary = string.Concat(Enumerable.Repeat("{\"a\":", 32)) + "{}" + new string('}', 32);
await Reject(async () => { await DiscordJsonFrame.WriteAsync(new MemoryStream(), Json(beyondBoundary)); }, "writer rejects 33 containers even with an empty final object");

var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
var nonce = new string('a', 64); var instance = new string('d', 32);
var serverVector = Convert.ToHexString(DiscordAuthentication.Proof(key, "server", nonce, new string('b', 64), new string('c', 32), instance)).ToLowerInvariant();
Check(serverVector == "30b2252aeb84f9a45ac0fff8954e0809cbc316b535ae619c9ba51edf00418558", "HMAC transcript matches independent Node vector");

if (OperatingSystem.IsWindows())
{
    var self = DiscordProcessIdentity.Capture(Environment.ProcessId);
    Check(self.ProcessId == Environment.ProcessId && self.CreationFileTime > 0 && Path.IsPathFullyQualified(self.ExecutablePath), "OS identity reads exact PID, creation FILETIME and executable");
    async Task<(DiscordPipeServer Server, NamedPipeClientStream Client, Task<DiscordAuthenticatedConnection> Accept)> Pair(DiscordProcessIdentity? identity = null, byte[]? serverKey = null)
    {
        var server = new DiscordPipeServer(DiscordPipeServer.CreatePipeName(), identity ?? self, serverKey ?? key);
        var accepting = server.AcceptAsync();
        var client = new NamedPipeClientStream(".", server.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await client.ConnectAsync(2000); return (server, client, accepting); }
        catch { client.Dispose(); await server.DisposeAsync(); throw; }
    }
    async Task<JsonElement> Begin(NamedPipeClientStream client)
    {
        await DiscordJsonFrame.WriteAsync(client, Hello(nonce, instance));
        return await DiscordJsonFrame.ReadAsync(client);
    }
    async Task Complete(NamedPipeClientStream client, JsonElement hello, string? proof = null)
    {
        await DiscordJsonFrame.WriteAsync(client, Proof(proof ?? MakeProof(key, "client", nonce, hello, instance)));
    }

    var copiedKey = key.ToArray();
    var success = await Pair(serverKey: copiedKey);
    CryptographicOperations.ZeroMemory(copiedKey);
    await using (success.Server)
    await using (success.Client)
    {
        var hello = await Begin(success.Client);
        Check(hello.GetProperty("serverProof").GetString() == MakeProof(key, "server", nonce, hello, instance), "server proof authenticates cloned owner secret");
        Check(!success.Accept.IsCompleted, "no authenticated session before client proof");
        await Complete(success.Client, hello);
        var ready = await DiscordJsonFrame.ReadAsync(success.Client);
        await using var connection = await success.Accept;
        Check(ready.GetProperty("type").GetString() == "ready" && connection.ConnectionId == ready.GetProperty("connectionId").GetString() && connection.ClientInstance == instance, "ready is confirmed before exposing the authenticated channel");
        await DiscordJsonFrame.WriteAsync(success.Client, Json("{\"type\":\"fixture-data\"}"));
        Check((await connection.ReadObjectAsync()).GetProperty("type").GetString() == "fixture-data", "authenticated channel carries bounded objects");
        await connection.WriteObjectAsync(Json("{\"type\":\"fixture-reply\"}"));
        Check((await DiscordJsonFrame.ReadAsync(success.Client)).GetProperty("type").GetString() == "fixture-reply", "authenticated response uses the same framing");
        await Reject(async () => { await success.Server.AcceptAsync(); }, "one endpoint never accepts another handshake");
        var pending = connection.ReadObjectAsync().AsTask();
        await Reject(async () => { await connection.ReadObjectAsync(); }, "concurrent reads cannot queue without a bound");
        await connection.DisposeAsync();
        await Reject(async () => { await pending; }, "disposing authenticated channel interrupts pending read");
        var secretCopy = (byte[])typeof(DiscordPipeServer).GetField("secret", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(success.Server)!;
        Check(secretCopy.All(item => item == 0), "server-owned pairing key is wiped on disposal");
    }

    var badHellos = new[] {
        $"{{\"type\":\"clientHello\",\"version\":2,\"clientNonce\":\"{nonce}\",\"clientInstance\":\"{instance}\"}}",
        $"{{\"type\":\"clientHello\",\"version\":\"1\",\"clientNonce\":\"{nonce}\",\"clientInstance\":\"{instance}\"}}",
        $"{{\"type\":\"clientHello\",\"version\":1,\"clientNonce\":\"{nonce}\",\"clientInstance\":\"{instance}\",\"state\":{{}}}}",
        $"{{\"type\":\"clientHello\",\"version\":1,\"clientNonce\":\"{nonce.ToUpperInvariant()}\",\"clientInstance\":\"{instance}\"}}",
        "{\"type\":\"snapshot\"}", "{\"type\":\"clientHello\"}"
    };
    foreach (var bad in badHellos)
    {
        var pair = await Pair(); await using var server = pair.Server; await using var client = pair.Client;
        await DiscordJsonFrame.WriteAsync(client, Json(bad));
        await Reject(async () => { await pair.Accept; }, "strict hello rejects wrong version/type/extras/nonce/early-state");
    }
    foreach (var bad in new[] { "{\"type\":\"snapshot\"}", $"{{\"type\":\"clientProof\",\"proof\":\"{new string('0',64)}\"}}", $"{{\"type\":\"clientProof\",\"proof\":\"{new string('a',64)}\",\"extra\":1}}" })
    {
        var pair = await Pair(); await using var server = pair.Server; await using var client = pair.Client;
        _ = await Begin(client); await DiscordJsonFrame.WriteAsync(client, Json(bad));
        await Reject(async () => { await pair.Accept; }, "no ready for missing, forged or ambiguous proof");
    }
    string? oldProof = null;
    for (var iteration = 0; iteration < 2; iteration++)
    {
        var pair = await Pair(); await using var server = pair.Server; await using var client = pair.Client;
        var hello = await Begin(client);
        var fresh = MakeProof(key, "client", nonce, hello, instance);
        await Complete(client, hello, oldProof ?? fresh);
        if (oldProof is null)
        {
            _ = await DiscordJsonFrame.ReadAsync(client); await using var connection = await pair.Accept; oldProof = fresh;
        }
        else { Check(fresh != oldProof, "new server challenge changes proof with identical client nonce"); await Reject(async () => { await pair.Accept; }, "prior handshake proof cannot be replayed"); }
    }
    foreach (var wrong in new[] {
        new DiscordProcessIdentity(self.ProcessId + 1, self.CreationFileTime, self.ExecutablePath),
        new DiscordProcessIdentity(self.ProcessId, self.CreationFileTime + 1, self.ExecutablePath),
        new DiscordProcessIdentity(self.ProcessId, self.CreationFileTime, Path.Combine(Path.GetTempPath(), "different.exe")) })
    {
        var pair = await Pair(identity: wrong); await using var server = pair.Server; await using var client = pair.Client;
        await Reject(async () => { await pair.Accept; }, "OS process identity mismatch rejects before handshake");
    }
    await using (var server = new DiscordPipeServer(DiscordPipeServer.CreatePipeName(), self, key))
    {
        await Reject(() => { _ = new DiscordPipeServer(server.PipeName, self, key); return Task.CompletedTask; }, "FirstPipeInstance prevents a second listener with the same name");
        using var cancelled = new CancellationTokenSource(); var accepting = server.AcceptAsync(cancelled.Token); cancelled.Cancel();
        await Reject(async () => { await accepting; }, "cancelled connection wait closes endpoint");
    }
    var timeoutPair = await Pair();
    await using (timeoutPair.Server)
    await using (timeoutPair.Client)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await Reject(async () => { await timeoutPair.Accept.WaitAsync(TimeSpan.FromSeconds(8)); }, "silent peer cannot hold handshake beyond five seconds");
        Check(elapsed.Elapsed < TimeSpan.FromSeconds(7), "handshake deadline is bounded");
    }
    var cancellationPair = await Pair();
    await using (cancellationPair.Client)
    {
        _ = await Begin(cancellationPair.Client); await cancellationPair.Server.DisposeAsync();
        await Reject(async () => { await cancellationPair.Accept; }, "dispose interrupts client-proof wait");
    }
}
else Console.WriteLine("SKIP Windows named-pipe/OS-identity checks on non-Windows");
Console.WriteLine($"Passed: {passed}, Failed: 0, Skipped: {(OperatingSystem.IsWindows() ? 0 : 1)}");

sealed class FragmentedStream(byte[] data) : MemoryStream(data)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], token);
}
