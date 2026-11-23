using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task EditSavedAndLinkedCreatesSeparateCopy()
    {
        foreach (var linked in new[] { false, true })
        {
            CaptureLibraryEditRequest? request = null;
            var oldUnpublishedBeforeRelease = false;
            await using var fixture = new GalleryFixture((builder, owner) =>
            {
                owner.OnReleased = value => oldUnpublishedBeforeRelease = Preview(owner)?.Id != value.AttachmentId;
                builder.WithHandler(WidgetCaptureLibraryCapabilities.EditCopy, (value, _) =>
                {
                    request = value;
                    var copy = EditedItem(owner.Items[0]); owner.Items.Add(copy); owner.Notify();
                    return ValueTask.FromResult(new CaptureLibraryEditResult(CaptureLibraryEditOutcome.Saved, copy));
                });
            });
            fixture.Items[0] = fixture.Items[0] with { Revision = 7, MetadataRevision = 9, SourceId = linked ? new string('b', 32) : null };
            var original = fixture.Items[0];
            await fixture.Start(); using var host = await OpenEditingSource(fixture);
            Check(fixture.Node("capture-library.edit-copy") is { IsBusy: not true }, "Saved preview has no Edit copy entry");
            await fixture.Action("capture-library.edit-copy");
            await fixture.Until(() => Preview(fixture)?.Width == 80 && !fixture.Busy);
            Check(request?.Saved == new CaptureLibraryMutationReference(original.Id, 7, 9) && request.Attachment is null,
                "Editing must use the exact saved content and metadata revisions, including linked media");
            Check(ReferenceEquals(original, fixture.Items[0]) && fixture.Items.Count == 4 && fixture.Recycled == 0 && fixture.Unlinked == 0,
                "Editing replaced or removed the original capture");
            Check(fixture.Opened == 2 && fixture.Released == 1 && oldUnpublishedBeforeRelease,
                "Saved copy navigation failed to retire only the old published preview lease");
            Check(fixture.Node("capture-library.edit-title")?.TextEntryValue == "Edited copy" &&
                fixture.LastFeedback == "Edited copy saved.", "Edited copy did not become the selected capture");
        }
    }

    internal static async Task EditCancelAndUnsupportedKeepPreview()
    {
        var requests = 0;
        await using var fixture = new GalleryFixture((builder, _) => builder.WithHandler(WidgetCaptureLibraryCapabilities.EditCopy, (_, _) =>
        {
            if (++requests == 1) return ValueTask.FromResult(new CaptureLibraryEditResult(CaptureLibraryEditOutcome.Cancelled));
            throw new WidgetCapabilityException("capture_library_edit_unsupported_image", "fixture");
        }));
        await fixture.Start(); using var host = await OpenEditingSource(fixture);
        var original = Preview(fixture);
        await fixture.Action("capture-library.edit-copy"); await fixture.Until(() => requests == 1 && !fixture.Busy);
        Check(Preview(fixture) == original && fixture.Released == 0 && fixture.Opened == 1 && fixture.Items.Count == 3,
            "Cancel must retain the current preview without creating or reopening a capture");
        await fixture.Action("capture-library.edit-copy"); await fixture.Until(() => requests == 2 && !fixture.Busy);
        Check(Preview(fixture) == original && fixture.Released == 0 && fixture.LastFeedback?.Contains("PNG") == true &&
            !fixture.LastFeedback!.Contains("capture_library_"), "Unsupported media must retain the preview and explain the format in user-facing language");
    }

    internal static async Task EditLateSaveCannotNavigate()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new GalleryFixture((builder, owner) => builder.WithHandler(WidgetCaptureLibraryCapabilities.EditCopy, async (_, _) =>
        {
            entered.TrySetResult(); await release.Task; // External save can finish after caller cancellation.
            var copy = EditedItem(owner.Items[0]); owner.Items.Add(copy); owner.Notify();
            return new CaptureLibraryEditResult(CaptureLibraryEditOutcome.Saved, copy);
        }));
        try
        {
            await fixture.Start(); using var host = await OpenEditingSource(fixture);
            await fixture.Action("capture-library.edit-copy"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Action("capture-library.cancel");
            await fixture.Action("capture-library.back"); release.TrySetResult();
            await fixture.Until(() => fixture.Collection?.IndexedCollection?.Count == 4 && !fixture.Busy);
            Check(Preview(fixture) is null && fixture.Opened == 1 && fixture.Released == 1,
                "A late committed save reopened its copy or overwrote newer gallery navigation");
        }
        finally { release.TrySetResult(); }
    }

    internal static async Task EditLateOpenReleasesUnclaimedLease()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new List<string>();
        var orphan = new CaptureAttachment(new string('d', 32), "image/png", 80, 60, 12, 0, long.MaxValue) { Origin = CaptureAttachmentOrigin.SavedLibrary };
        await using var fixture = new GalleryFixture((builder, owner) =>
        {
            owner.OnReleased = request => released.Add(request.AttachmentId);
            owner.OpenOverride = async (request, _) =>
            {
                if (request.Id == 4.ToString("x32")) { entered.TrySetResult(); await release.Task; return orphan; }
                return owner.Items.Single(item => item.Id == request.Id).Media with { Id = Guid.NewGuid().ToString("N"), Origin = CaptureAttachmentOrigin.SavedLibrary };
            };
            builder.WithHandler(WidgetCaptureLibraryCapabilities.EditCopy, (_, _) =>
            {
                var copy = EditedItem(owner.Items[0]); owner.Items.Add(copy); owner.Notify();
                return ValueTask.FromResult(new CaptureLibraryEditResult(CaptureLibraryEditOutcome.Saved, copy));
            });
        });
        try
        {
            await fixture.Start(); using var host = await OpenEditingSource(fixture);
            await fixture.Action("capture-library.edit-copy"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Action("capture-library.cancel"); await fixture.Action("capture-library.back"); release.TrySetResult();
            await fixture.Until(() => fixture.Collection is not null && !fixture.Busy);
            Check(Preview(fixture) is null && released.Count == 2 && released.Count(id => id == orphan.Id) == 1,
                "Cancelled result opening leaked its new lease or published it into the newer route");
        }
        finally { release.TrySetResult(); }
    }

    private static CaptureAttachment? Preview(GalleryFixture fixture) => fixture.Node("capture-library.preview")?.MediaPlayer?.Source.Attachment;
    private static CaptureLibraryItem EditedItem(CaptureLibraryItem original) => new(4.ToString("x32"),
        original.Media with { Id = new string('d', 32), Width = 80, Height = 60, Origin = CaptureAttachmentOrigin.SavedLibrary }, 4)
        { Title = "Edited copy", Revision = 11, MetadataRevision = 13 };
    private static async Task<WidgetIndexedCollectionTestHost> OpenEditingSource(GalleryFixture fixture)
    {
        var host = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget, "gallery-edit", 100); fixture.Host = host;
        using (var lease = await host.AcquireAsync("capture-library.entries", 0, 3))
            Check(lease.RouteAction(fixture.Items[0].Id, ControllerButton.A) is not null, "Edit source preview was not admitted");
        await fixture.Until(() => Preview(fixture) is not null && !fixture.Busy);
        return host;
    }
}
