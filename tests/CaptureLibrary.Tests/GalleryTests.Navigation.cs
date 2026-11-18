using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task NavigationKeepsCoherentPreviewWhileOpening()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new GalleryFixture();
        await fixture.Start(); using var host = await OpenEditingSource(fixture);
        var original = Preview(fixture);
        var unpublished = false;
        fixture.OnReleased = request => unpublished = Preview(fixture)?.Id != request.AttachmentId;
        fixture.OpenOverride = async (request, _) =>
        {
            entered.TrySetResult(); await release.Task;
            return fixture.Items.Single(item => item.Id == request.Id).Media with { Id = Guid.NewGuid().ToString("N"), Origin = CaptureAttachmentOrigin.SavedLibrary };
        };
        try
        {
            await fixture.Action("capture-library.viewer.next"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(Preview(fixture) == original && fixture.Released == 0 && fixture.Node("capture-library.metadata") is not null &&
                fixture.Node("capture-library.edit-title")?.TextEntryValue == "Boss fight",
                "Opening the next item dismantled or mixed the current detail page");
            await fixture.Action("capture-library.viewer.next");
            Check(fixture.Opened == 2, "Repeated navigation started overlapping opens");
            release.TrySetResult(); await fixture.Until(() => !fixture.Busy);
            Check(Preview(fixture) != original && fixture.Released == 1 && unpublished &&
                fixture.Node("capture-library.edit-title")?.TextEntryValue == "Capture 2", "Navigation did not swap coherently before releasing");
            await fixture.Action("capture-library.viewer.previous"); await fixture.Until(() => !fixture.Busy);
            Check(fixture.Released == 2 && fixture.Node("capture-library.edit-title")?.TextEntryValue == "Boss fight", "Previous lost the ordering or lease cleanup");
            await fixture.Action("capture-library.viewer.previous"); await fixture.Until(() => !fixture.Busy);
            Check(fixture.Opened == 3 && fixture.Released == 2, "Boundary navigation unnecessarily reopened the current capture");
        }
        finally { release.TrySetResult(); }
    }

    internal static async Task NavigationFailureKeepsCurrentCapture()
    {
        await using var fixture = new GalleryFixture();
        await fixture.Start(); using var host = await OpenEditingSource(fixture);
        var original = Preview(fixture);
        fixture.OpenOverride = (_, _) => throw new WidgetCapabilityException("capture_library_not_found", "missing");
        await fixture.Action("capture-library.viewer.next"); await fixture.Until(() => !fixture.Busy);
        Check(Preview(fixture) == original && fixture.Released == 0 && fixture.LastFeedback is not null &&
            fixture.Node("capture-library.edit-title")?.TextEntryValue == "Boss fight", "Failed navigation discarded the usable current capture");
    }

    internal static async Task CancelledNavigationReleasesLatePreview()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new List<string>();
        await using var fixture = new GalleryFixture();
        await fixture.Start(); using var host = await OpenEditingSource(fixture);
        var orphan = fixture.Items[1].Media with { Id = new string('d', 32), Origin = CaptureAttachmentOrigin.SavedLibrary };
        fixture.OnReleased = request => released.Add(request.AttachmentId);
        fixture.OpenOverride = async (_, _) => { entered.TrySetResult(); await release.Task; return orphan; };
        try
        {
            await fixture.Action("capture-library.viewer.next"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Action("capture-library.cancel"); await fixture.Action("capture-library.back"); release.TrySetResult();
            await fixture.Until(() => fixture.Collection is not null && !fixture.Busy && released.Count == 2);
            Check(Preview(fixture) is null && released.Count(id => id == orphan.Id) == 1, "Late open leaked or replaced a newer route");
        }
        finally { release.TrySetResult(); }
    }
}
