using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class ProviderDocumentTests
{
    internal static Task RunAsync()
    {
        var registry = new ProviderDocumentRegistry();
        var first = new object(); var second = new object();
        var identity = new BrokerWidgetIdentity("example.help", "example.publisher", "instance");
        const string html = "<div><a href=\"https://www.google.com/search?q=fixture\">Fixture search</a></div>";
        var reference = registry.Register(first, identity, [html]);
        Check(reference.IsWellFormed() && registry.Resolve(identity, reference).Single() == html);
        Reject(() => registry.Resolve(identity with { InstanceId = "other" }, reference));
        registry.Discard(second, reference);
        Check(registry.Resolve(identity, reference).Count == 1);
        registry.Retire(first);
        Reject(() => registry.Resolve(identity, reference));
        Reject(() => registry.Register(first, identity, [html]));
        Reject(() => registry.Register(second, identity, [new string('a', ProviderDocumentRegistry.MaximumHtmlCharacters + 1)]));
        var oldEpoch = registry.Begin(second);
        registry.Revoke(second);
        Reject(() => registry.Register(second, identity, [html], oldEpoch));
        var earliest = registry.Register(second, identity, [html], registry.Begin(second));
        for (var i = 0; i < 24; i++) registry.Register(second, identity, [html]);
        Reject(() => registry.Resolve(identity, earliest));
        registry.Retire(second);
        return Task.CompletedTask;
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Provider document authority regression."); }
    private static void Reject(Action operation)
    {
        try { operation(); } catch (BrokerException) { return; }
        throw new InvalidOperationException("Invalid provider document operation succeeded.");
    }
}
