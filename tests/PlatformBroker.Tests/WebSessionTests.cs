using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class WebSessionTests
{
    internal static async Task OwnershipAsync()
    {
        var registry = new WebSessionRegistry();
        using var first = registry.CreateOwner("first", new("example.first", "example.publisher", "first"), false);
        using var second = registry.CreateOwner("second", new("example.second", "example.publisher", "second"), true);
        var open = first.ExecuteAsync(new(WebSessionOperation.Open) { Definition = Definition() }, default).AsTask();
        var batch = await registry.TakeAsync(default);
        var work = batch.Work.Single();
        Check(work.Identity.InstanceId == "first" && registry.IsCurrent(work), "Owner identity is bound by host.");
        registry.Complete(new(work.RequestId));
        var reference = (await open).Session!;
        await Reject(() => second.ExecuteAsync(new(WebSessionOperation.Close) { Session = reference }, default).AsTask(), "session_closed");
        await Reject(() => first.ExecuteAsync(new(WebSessionOperation.AuthorizeResource) { Session = reference, ResourceUrl = "http://127.0.0.1:12345/media" }, default).AsTask(), "permission_denied");
        await Reject(() => first.ExecuteAsync(new(WebSessionOperation.Open) { Definition = Definition() }, default).AsTask(), "session_exists");
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { title = "line 1\nline 2 🎮" }));
        registry.Publish(new(reference, [payload]));
        var messages = await first.ExecuteAsync(new(WebSessionOperation.ReadMessages) { Session = reference }, default);
        Check(messages.MessagesBase64.Single() == payload, "Messages preserve bounded UTF8 without broker string coercion.");
        var pending = first.ExecuteAsync(new(WebSessionOperation.PostMessage) { Session = reference, MessageBase64 = payload }, default).AsTask();
        var messageWork = (await registry.TakeAsync(default)).Work.Single();
        first.Dispose();
        await Reject(() => pending, "session_closed");
        Check(!registry.IsCurrent(messageWork), "Worker retirement removes pending authority.");
        registry.Complete(new(messageWork.RequestId));
        await Reject(() => first.ExecuteAsync(new(WebSessionOperation.ReadMessages) { Session = reference }, default).AsTask(), "session_closed");
    }

    internal static async Task CancellationAsync()
    {
        var registry = new WebSessionRegistry();
        using var owner = registry.CreateOwner("widget", new("example.widget", "example.publisher", "widget"), true);
        using var cancellation = new CancellationTokenSource();
        var opening = owner.ExecuteAsync(new(WebSessionOperation.Open) { Definition = Definition() }, cancellation.Token).AsTask();
        var work = (await registry.TakeAsync(default)).Work.Single();
        cancellation.Cancel();
        try { await opening; throw new InvalidOperationException("Opening did not cancel."); }
        catch (OperationCanceledException) { }
        Check(!registry.IsCurrent(work) && !registry.HasBackgroundSession("widget"), "Canceled startup cannot leave a retained session.");
        Check((await registry.TakeAsync(default)).Work.Single().Command.Operation == WebSessionOperation.Close, "Cancellation retires native startup.");
    }

    internal static Task SourcesAsync()
    {
        Check(Definition().IsWellFormed(), "Packaged definition is valid.");
        Check(!(Definition() with { Source = WebSessionSource.Package("../secret", [new() { Path = "../secret", ContentType = "text/html" }]) }).IsWellFormed(), "Package path traversal is rejected.");
        Check(!WebSessionContract.IsNetworkOrigin("https://127.0.0.1") && !WebSessionContract.IsNetworkOrigin("https://localhost") &&
            !WebSessionContract.IsNetworkOrigin("https://example.com/path") && WebSessionContract.IsNetworkOrigin("https://*.example.com"), "Network origin rules are bounded and explicit.");
        Check(WebSessionContract.IsLocalResource("http://127.0.0.1:23456/media") &&
            !WebSessionContract.IsLocalResource("http://localhost:23456/media") && !WebSessionContract.IsLocalResource("file:///secret"), "Local resources require an exact loopback URL.");
        return Task.CompletedTask;
    }

    private static WebSessionDefinition Definition() => new("player", "Synthetic player",
        WebSessionSource.Package("web/index.html", [new() { Path = "web/index.html", ContentType = "text/html" }]));
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static async Task Reject(Func<Task> action, string code)
    {
        try { await action(); }
        catch (BrokerException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected broker rejection: " + code);
    }
}
