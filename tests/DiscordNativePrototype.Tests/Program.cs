using System.Text.Json;
using System.Collections.Concurrent;
using WidgetRail.Samples.DiscordPrototype;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
static IEnumerable<ViewNode> Nodes(ViewNode n) => new[] { n }.Concat(n.Children.SelectMany(Nodes));
static async Task Wait(Func<bool> predicate)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    while (!predicate()) await Task.Delay(15, timeout.Token);
}
var adapter = new FakeAdapter();
var widget = new DiscordPrototypeWidget(adapter);
await WidgetTestHost.InitializeAsync(widget);
await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
try
{
    await Wait(() => Nodes(widget.Render().CreateSnapshot("test", 0).Root).Any(n => n.Id == "discord.main.mute"));
    var snapshot = widget.Render().CreateSnapshot("test", 1);
    Check(WidgetTestHost.ValidateWinUiPresentation(snapshot).Count == 0, "connected native declarations and two pins validate");
    Check(snapshot.PinnedLayouts.Count == 2, "call and stream pins use separate layout identities");
    Check(adapter.Commands.Count == 0, "startup performs no call or broadcast actions");
    await widget.OnActionAsync(new("join.999", "test"));
    Check(adapter.Commands.Count == 0, "unknown channel is rejected before transport");
    await widget.OnActionAsync(new("search", "test") { CommittedText = "second" });
    var filtered = Nodes(widget.Render().CreateSnapshot("test", 2).Root).Where(n => n.Id.StartsWith("discord.channel.")).ToArray();
    Check(filtered.Length == 1 && filtered[0].Id == "discord.channel.21", "search filters native channels");
    adapter.CommandGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    await widget.OnActionAsync(new("mute", "test"));
    await Wait(() => adapter.Commands.Count == 1);
    await widget.OnActionAsync(new("mute", "test"));
    Check(adapter.Commands.Count == 1, "duplicate command joins single-flight work");
    Check(Nodes(widget.Render().CreateSnapshot("test", 3).Root).Single(n => n.Id == "discord.main.mute").IsDisabled == true,
        "commands are disabled during pending mutation");
    adapter.CommandGate.SetResult();
    await Wait(() => Nodes(widget.Render().CreateSnapshot("test", 4).Root).Single(n => n.Id == "discord.main.mute").IsDisabled != true);
    adapter.CommandGate = null;
    adapter.FailCommand = true;
    await widget.OnActionAsync(new("deafen", "test"));
    await Wait(() => Nodes(widget.Render().CreateSnapshot("test", 5).Root).Any(n => n.Text?.StartsWith("Could not confirm") == true));
    Check(adapter.Commands.Count == 2, "uncertain external mutation is not retried");
    await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
    Check(adapter.Commands.All(c => c is not "leave" and not "stopShare"), "overlay background never disconnects or stops broadcast");
    adapter.FailRead = true;
    await widget.OnActionAsync(new("refresh", "test"));
    await Wait(() => Nodes(widget.Render().CreateSnapshot("test", 6).Root).Any(n => n.Id == "discord.status"));
    Check(!Nodes(widget.Render().CreateSnapshot("test", 7).Root).Any(n => n.Id == "discord.main.mute"), "read failure removes stale call controls");
}
finally { await WidgetTestHost.DestroyAsync(widget); }
Check(adapter.Disposed, "teardown releases transport");

try
{
    await AdapterClient.CopyBoundedAsync(new MemoryStream(new byte[33]), new MemoryStream(), 32, CancellationToken.None);
    Check(false, "oversized transport response rejected");
}
catch (IOException) { Check(true, "oversized transport response rejected"); }
Console.WriteLine($"Passed: {passed}, Failed: 0, Skipped: 0");

sealed class FakeAdapter : IDiscordAdapter
{
    internal ConcurrentQueue<string> Commands { get; } = new();
    internal TaskCompletionSource? CommandGate;
    internal bool FailCommand, FailRead, Disposed;
    public async Task<JsonElement> InvokeAsync(string method, object? args, CancellationToken token)
    {
        if (method == "snapshot")
        {
            if (FailRead) throw new IOException();
            return JsonSerializer.SerializeToElement(new DiscordState(1, true, "1", new(true, true, true, true, true, true),
                [new("20", "10", "First", "Test"), new("21", "10", "Second", "Test")], "20", "First", false, false,
                [new("1", "Test", false, false, false)], [new("guild:10:20:2", "2", "Other")], false, false, null, "closed"), AdapterClient.Json);
        }
        Commands.Enqueue(method);
        if (CommandGate is { } gate) await gate.Task.WaitAsync(token);
        if (FailCommand) throw new IOException();
        return JsonSerializer.SerializeToElement(new { });
    }
    public void Dispose() => Disposed = true;
}
