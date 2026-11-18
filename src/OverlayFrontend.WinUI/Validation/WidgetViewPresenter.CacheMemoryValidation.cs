namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private static readonly string? ArtworkReviewPath = Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--navigation-review-output");
    private static int artworkReviewEvents;
    partial void TraceArtworkReview(string id, bool hadPixels, bool hasPixels)
    {
        if (ArtworkReviewPath is null || ++artworkReviewEvents > 6000) return;
        File.AppendAllText(ArtworkReviewPath + ".artwork", System.Text.Json.JsonSerializer.Serialize(new { time = Environment.TickCount64,
            id, hadPixels, hasPixels, ArtworkGeneration, current = ArtworkAuthorityCurrent?.Invoke() }) + Environment.NewLine);
    }
    internal object NavigationReviewForValidation() => new
    {
        memory = NavigationMemoryForValidation(),
        collections = bindings.Values.Where(binding => binding.Element is WidgetIndexedCollectionView).Select(binding => new
        {
            id = binding.Identity.Id,
            query = declarations[binding.Identity.Id].Node.IndexedCollection,
            native = ((WidgetIndexedCollectionView)binding.Element).NavigationDiagnosticState(),
            statistics = ((WidgetIndexedCollectionView)binding.Element).PerformanceStatistics(),
        }).ToArray(),
    };
    internal static object[] NavigationCacheInventoryForValidation() => cachedPages.Select(page => (object)new
    {
        widget = page.Owner.frame?.Authority.WidgetId,
        group = page.Transition.GroupId,
        key = page.Transition.Key,
        bindings = page.Bindings.Count,
    }).ToArray();

    internal static async Task<int> EvictNavigationCachesForValidationAsync()
    {
        var pages = cachedPages.ToArray();
        var owners = pages.Select(page => page.Owner).Distinct().ToArray();
        foreach (var page in pages) page.Owner.EvictCachedPage(page);
        await Task.WhenAll(owners.SelectMany(owner => owner.retirements.ToArray()));
        return pages.Length;
    }
}
