using System.Text.Json;
using WidgetRail.WidgetProtocol;

internal static partial class GalleryTests
{
    internal static async Task NamedRelatedFiltersPreserveIdentityAndLegacyIntents()
    {
        await using var fixture = new GalleryFixture(); await fixture.Start();
        foreach (var label in new[] { "Hollow Knight: Silksong", "The Unquenchable Thirst", "<not markup> & more" })
        {
            var request = WidgetIntentRequest.Create(WidgetIntentContracts.CaptureLibraryV2,
                JsonSerializer.SerializeToElement(new { originPackageId = "widgetrail.tests.provider", referenceKind = "example.item", referenceId = "opaque-id", referenceLabel = label }));
            Check(await fixture.Widget.OnIntentAsync(request) == WidgetIntentResult.Accepted, "Named browse intent rejected");
            await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.related-filter.label")?.Text == label);
            Check(fixture.LastQuery?.Reference?.Id == "opaque-id" && fixture.LastQuery.OriginPackageId == "widgetrail.tests.provider", "Display label changed query identity");
            await fixture.Action("capture-library.filter.screenshots");
            Check(fixture.Node("capture-library.related-filter.label")?.Text == label, "Ordinary filter change lost display label");
            await fixture.Action("capture-library.related-filter.clear");
            Check(fixture.Node("capture-library.related-filter.label") is null, "Cleared filter retained label");
        }
        var legacy = WidgetIntentRequest.Create(WidgetIntentContracts.CaptureLibrary,
            JsonSerializer.SerializeToElement(new { originPackageId = "widgetrail.tests.provider", referenceKind = "example.item", referenceId = "other-id" }));
        Check(await fixture.Widget.OnIntentAsync(legacy) == WidgetIntentResult.Accepted, "Existing v1 sender became incompatible");
        await fixture.Until(() => !fixture.Busy);
        Check(fixture.Node("capture-library.related-filter.label")?.Text == "Related captures", "Legacy message reused an unrelated previous label");
    }
}
