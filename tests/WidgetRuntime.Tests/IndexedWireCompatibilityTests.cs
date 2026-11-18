using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;

internal static class IndexedWireCompatibilityTests
{
    public static Task OrdinaryRequestsKeepLegacyShape()
    {
        foreach (var source in new[]
        {
            new IndexedCollectionDescriptor("items", 4, 2, 20),
            new IndexedCollectionDescriptor("items", 4, 2, 20) { CanLocateItems = true },
            new IndexedCollectionDescriptor("items", 4, 2, 20)
                { Discovery = new(3, true, DiscoveredCollectionStatus.Ready, 256) }
        })
        {
            var request = new IndexedCollectionRangeRequest("collection", source, 0, 20, "demand");
            var wire = RuntimeJson.ToElement(request);
            var previous = RuntimeJson.FromElement<BeforeEndFollowingRequest>(wire);
            if (previous.Source.Count != 20 || previous.Source.CanLocateItems != source.CanLocateItems || previous.Source.Discovery != source.Discovery)
                throw new InvalidOperationException("Legacy range descriptor lost ordinary source authority.");
        }
        var optedIn = RuntimeJson.ToElement(new IndexedCollectionRangeRequest("collection",
            new("items", 5, 0, 20) { EndFollowingEpoch = 1, CanRecoverViewport = true }, 0, 20, "demand"));
        if (!optedIn.GetProperty("source").GetProperty("canRecoverViewport").GetBoolean())
            throw new InvalidOperationException("Opt-in recovery metadata was suppressed.");
        try { RuntimeJson.FromElement<BeforeEndFollowingRequest>(optedIn); }
        catch (JsonException) { return Task.CompletedTask; }
        throw new InvalidOperationException("Legacy decoder must remain strict for genuinely new source semantics.");
    }

    // Frozen pre-118 worker wire shape. These must not acquire new descriptor
    // fields alongside the production DTO or the compatibility check vanishes.
    private sealed record BeforeEndFollowingSource(string SourceId, long QueryGeneration, long ContentRevision, int Count)
    {
        public DiscoveredCollectionState? Discovery { get; init; }
        public bool CanLocateItems { get; init; }
    }
    private sealed record BeforeEndFollowingRequest(string CollectionId, BeforeEndFollowingSource Source,
        int StartIndex, int Count, string DemandId, string? PinnedLayoutId = null)
    {
        public IndexedCollectionRequestKind Kind { get; init; }
        public string? PreviewKey { get; init; }
        public string? LocateKey { get; init; }
    }
}
