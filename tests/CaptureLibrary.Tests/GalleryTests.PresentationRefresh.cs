using WidgetRail.WidgetProtocol;

internal static partial class GalleryTests
{
    internal static async Task PresentationChangesKeepPendingLibraryRefresh()
    {
        foreach (var previewPreference in new[] { false, true })
        {
            await using var fixture = new GalleryFixture();
            await fixture.Start();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.GroupsOverride = async (request, _) =>
            {
                entered.TrySetResult();
                await finish.Task;
                return new([new("unknown", 4)], 4, request.Revision!);
            };
            fixture.Items.Add(fixture.Items[0] with { Id = 4.ToString("x32"), Title = "New capture" });
            fixture.Notify();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                if (previewPreference)
                {
                    await fixture.Action("capture-library.settings"); await fixture.Action("capture-library.section.Gallery.open");
                    await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.settings.previews") is not null);
                    await fixture.Action("capture-library.option.previews.off");
                    await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.settings.previews")!.SelectOptions
                        .Any(option => option.ActionId == "capture-library.option.previews.off" && option.IsSelected));
                    await fixture.Action("capture-library.settings.back");
                }
                else await fixture.Action("capture-library.selection.toggle");
                finish.TrySetResult();
                await fixture.Until(() => fixture.Collection?.IndexedCollection?.Count == 4);
                Check(fixture.Node("capture-library.gallery.preview") is null,
                    "Publishing the current library revision overwrote newer selection/preview presentation policy.");
                Check(previewPreference || fixture.Collection?.IndexedSelection?.IsActive == true,
                    "Publishing the refreshed count ended the user's selection mode.");
            }
            finally { finish.TrySetResult(); }
        }
    }
}
