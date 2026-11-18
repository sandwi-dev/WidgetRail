using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task WindowsCapturesActions()
    {
        await using var fixture = new GalleryFixture();
        await fixture.Start();
        Check(fixture.Node("capture-library.more")!.SelectOptions.All(option => option.ActionId != "capture-library.save-recent"),
            "Obsolete Game Bar shortcut remains exposed");
        await fixture.Action("capture-library.sources.open");
        await fixture.Until(() => !fixture.Busy);
        await fixture.Action("capture-library.sources.windows");
        await fixture.Until(() => fixture.LastSourceRequest is not null && !fixture.Busy);
        Check(fixture.LastSourceRequest?.KnownLocation == CaptureLibraryKnownLocation.WindowsGameCaptures, "Settings link action did not reach known folder chooser");
        Check(ViewSnapshotValidator.Validate(fixture.View()).Count == 0, "External Windows folder controls are invalid");
    }
}
