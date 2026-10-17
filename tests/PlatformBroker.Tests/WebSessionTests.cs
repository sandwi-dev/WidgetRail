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
        var large=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("\""+new string('x',WebSessionContract.MaximumMessageBytes-2)+"\""));
        registry.Publish(new(reference,[large,large]));
        registry.Publish(new(reference,[large]));
        var firstBatch=await first.ExecuteAsync(new(WebSessionOperation.ReadMessages){Session=reference},default);
        var secondBatch=await first.ExecuteAsync(new(WebSessionOperation.ReadMessages){Session=reference},default);
        Check(firstBatch.MessagesBase64.Count==2&&secondBatch.MessagesBase64.Count==1,"Large messages drain across bounded batches without loss.");
        Check(JsonSerializer.SerializeToUtf8Bytes(firstBatch).Length<200*1024,"Largest reply reserves broker envelope space.");
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
        var adapter=new WebSessionPageAdapter {ScriptAsset="web/actions.js",Origins=["https://example.com"],Responses=[new("https://example.com","/api/items/")]};
        var adapted=new WebSessionDefinition("adapted","Adapted page",WebSessionSource.Remote("https://example.com")){PageAdapter=adapter};
        Check(adapted.IsWellFormed()&&adapter.AllowsPage("https://example.com/results"),"Exact origin adapter is valid.");
        Check(!adapter.AllowsPage("https://example.com.evil.test/")&&!adapter.AllowsPage("http://example.com/")&&!adapter.AllowsPage("https://example.com:444/"),"Adapter origins do not admit suffixes, HTTP, or other ports.");
        Check(!(adapter with {Origins=["https://*.example.com"]}).IsWellFormed(),"Adapters require exact origins, never wildcards.");
        Check(!(adapter with {ScriptAsset="../actions.js"}).IsWellFormed(),"Adapter assets cannot escape the sealed package.");
        Check(!(adapter with {Responses=[new("https://other.example","/api/items/")]}).IsWellFormed(),"Response access must be declared within page origins.");
        Check(!(Definition() with {PageAdapter=adapter}).IsWellFormed(),"Remote adapter does not alter packaged messaging.");
        var rule=adapter.Responses[0];
        Check(rule.Matches("https://example.com/api/items/123?q=abc")&&!rule.Matches("https://example.com/api/items-other/123")&&
            !rule.Matches("https://evil.example/api/items/123"),"Response paths are scoped at a segment boundary.");
        Check(!(rule with {PathPrefix="/api/../"}).IsWellFormed()&&!(rule with {PathPrefix="/api/%2e%2e/"}).IsWellFormed()&&
            !(rule with {MaximumBytes=WebSessionResponseRule.MaximumResponseBytes+1}).IsWellFormed(),"Ambiguous paths and oversized response limits are rejected.");
        Check(Definition().IsWellFormed(), "Packaged definition is valid.");
        Check(!(Definition() with { Source = WebSessionSource.Package("../secret", [new() { Path = "../secret", ContentType = "text/html" }]) }).IsWellFormed(), "Package path traversal is rejected.");
        Check(!WebSessionContract.IsNetworkOrigin("https://127.0.0.1") && !WebSessionContract.IsNetworkOrigin("https://localhost") &&
            !WebSessionContract.IsNetworkOrigin("https://example.com/path") && WebSessionContract.IsNetworkOrigin("https://*.example.com"), "Network origin rules are bounded and explicit.");
        Check(WebSessionContract.IsLocalResource("http://127.0.0.1:23456/media") &&
            !WebSessionContract.IsLocalResource("http://localhost:23456/media") && !WebSessionContract.IsLocalResource("file:///secret"), "Local resources require an exact loopback URL.");
        Check(WebSessionSource.Package("web/index.html", [new() { Path="web/index.html", ContentType="text/html" },
            new() { Path="web/art.webp", ContentType="image/webp" }]).IsWellFormed(), "Transparent WebP artwork is supported in packaged sessions.");
        return Task.CompletedTask;
    }

    internal static async Task AdaptedAsync()
    {
        var registry=new WebSessionRegistry();
        using var owner=registry.CreateOwner("widget",new("example.widget","example.publisher","widget"),false);
        var definition=new WebSessionDefinition("adapted","Adapted page",WebSessionSource.Remote("https://example.com"))
            {PageAdapter=new(){ScriptAsset="web/actions.js",Origins=["https://example.com"]}};
        await Reject(()=>owner.ExecuteAsync(new(WebSessionOperation.Open){Definition=definition},default).AsTask(),"invalid_payload");
        var open=owner.ExecuteAsync(new(WebSessionOperation.OpenAdapted){Definition=definition},default).AsTask();
        var work=(await registry.TakeAsync(default)).Work.Single();registry.Complete(new(work.RequestId));
        var session=(await open).Session!;
        await Reject(()=>owner.ExecuteAsync(new(WebSessionOperation.PostMessage){Session=session,MessageBase64="e30="},default).AsTask(),"messaging_unavailable");
        owner.Dispose();
        await Reject(()=>owner.ExecuteAsync(new(WebSessionOperation.ReadMessages){Session=session},default).AsTask(),"session_closed");
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
