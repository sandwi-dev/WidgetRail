using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static Task WatchReadCannotWarnNewPreview() => WatchReadEffects(back: false);
    internal static Task WatchReadCannotRemapNewBack() => WatchReadEffects(back: true);
    private static async Task WatchReadEffects(bool back)
    {
        await using var fixture = new GalleryFixture();
        await fixture.Start(); using var host = await OpenEditingSource(fixture);
        var originalId = fixture.Items[0].Id; var nextId = fixture.Items[1].Id;
        var openEntered = Signal(); var openFinish = Signal();
        var oldMetadataEntered = Signal(); var oldMetadataFinish = Signal();
        var backReleaseEntered = Signal(); var backReleaseFinish = Signal();
        var returnMetadataEntered = Signal(); var returnMetadataFinish = Signal();
        var publication = Signal();
        EventHandler<WidgetInvalidatedEventArgs> invalidated = (_, _) => publication.TrySetResult();
        fixture.OpenOverride = async (request, _) =>
        {
            openEntered.TrySetResult(); await openFinish.Task;
            return fixture.Items.Single(item => item.Id == request.Id).Media with
            { Id = Guid.NewGuid().ToString("N"), Origin = CaptureAttachmentOrigin.SavedLibrary };
        };
        fixture.MetadataOverride = async (request, _) =>
        {
            if (request.Id == originalId) { oldMetadataEntered.TrySetResult(); await oldMetadataFinish.Task; }
            else if (back) { returnMetadataEntered.TrySetResult(); await returnMetadataFinish.Task; }
            return fixture.Items.Single(item => item.Id == request.Id);
        };
        try
        {
            // Next is admitted under revision1 before an external change.
            await fixture.Action("capture-library.viewer.next"); await openEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            fixture.Items[0] = fixture.Items[0] with { Revision = fixture.Items[0].Revision + 1 };
            fixture.Items.Add(fixture.Items[0] with { Id = 4.ToString("x32"), Title = "New capture" });
            fixture.Notify(); await oldMetadataEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            openFinish.TrySetResult();
            await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.edit-title")?.TextEntryValue == "Capture 2");
            if (back)
            {
                fixture.ReleaseOverride = async (_, _) =>
                { backReleaseEntered.TrySetResult(); await backReleaseFinish.Task; return new(true); };
                await fixture.Action("capture-library.back"); await backReleaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            }
            fixture.Widget.Invalidated += invalidated;
            oldMetadataFinish.TrySetResult();
            await publication.Task.WaitAsync(TimeSpan.FromSeconds(3));
            fixture.Widget.Invalidated -= invalidated;
            if (back)
            {
                backReleaseFinish.TrySetResult();
                await returnMetadataEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Check(fixture.Collection?.IndexedCollection?.Count == 4,
                    "Ignoring stale preview effects also discarded current library data.");
                Check(fixture.View().FocusGroupEntryRequest?.IndexedItem?.ItemKey != originalId,
                    "An old watch read remapped the new Back request using the previous capture's position.");
            }
            Check(fixture.LastFeedback?.Contains("changed outside", StringComparison.Ordinal) != true,
                "An old watch read attached the previous capture's warning to a newer preview/route.");
            if (back)
            {
                returnMetadataFinish.TrySetResult();
                await fixture.Until(() => !fixture.Busy && fixture.View().FocusGroupEntryRequest?.IndexedItem?.ItemKey == nextId);
            }
            else Check(fixture.Node("capture-library.edit-title")?.TextEntryValue == "Capture 2",
                "Background metadata replaced the explicitly selected next capture.");
        }
        finally
        {
            fixture.Widget.Invalidated -= invalidated;
            openFinish.TrySetResult(); oldMetadataFinish.TrySetResult(); backReleaseFinish.TrySetResult(); returnMetadataFinish.TrySetResult();
        }
        static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
