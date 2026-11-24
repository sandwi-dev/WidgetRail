using WidgetRail.WidgetProtocol;

internal static partial class GalleryTests
{
    internal static async Task RecycleLinkedSelectionThroughBroker(IReadOnlyList<CaptureLibraryItem> items,
        Func<CaptureLibraryRecycleRequest, CancellationToken, ValueTask<CaptureLibraryBatchResult>> recycle)
    {
        await using var fixture = new GalleryFixture();
        fixture.Items.Clear();
        fixture.Items.AddRange(items);
        fixture.RecycleLinkedOverride = async (request, token) =>
        {
            var result = await recycle(request, token);
            foreach (var item in result.Results.Where(item => item.ErrorCode is null))
                fixture.Items.RemoveAll(existing => existing.Id == item.Id);
            return result;
        };
        await fixture.Start();
        await fixture.Action("capture-library.selection.toggle");
        await fixture.Action("capture-library.selection.all");
        await fixture.Action("capture-library.batch.recycle");
        await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.batch.confirm") is not null);
        await fixture.Action("capture-library.batch.confirm");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (fixture.LastFeedback != $"{items.Count} captures moved to the Recycle Bin.")
        {
            if (fixture.Node("capture-library.batch.failures") is not null) throw new Exception(fixture.LastFeedback);
            await Task.Delay(20, deadline.Token);
        }
        await fixture.Until(() => !fixture.Busy && fixture.Collection?.IndexedCollection?.Count is null or 0);
    }
    internal static async Task LargeBatchSingleConfirmation()
    {
        foreach (var (count, lostReply, stop) in new[] { (150, false, false), (256, false, false), (305, false, false),
            (305, true, false), (150, false, true) })
        {
            await using var fixture = new GalleryFixture();
            var seed = fixture.Items[0];
            fixture.Items.Clear();
            fixture.Items.AddRange(Enumerable.Range(1, count).Select(i =>
                seed with { Id = i.ToString("x32"), Title = $"Generated {i}", SavedAtUnixMilliseconds = i }));
            var remaining = fixture.Items.Skip(256).Select(item => item.Id).ToArray();
            await fixture.Start();
            await fixture.Action("capture-library.selection.toggle");
            var option = fixture.Node("capture-library.more")!.SelectOptions.Single(item => item.Id.StartsWith("select-"));
            Check(!option.IsDisabled && option.Label == (count > 256 ? "Select first 256 matches" : "Select all matches"), "Incorrect selection boundary");
            await fixture.Action(option.ActionId);
            await fixture.Until(() => fixture.Collection?.IndexedSelection?.Count == Math.Min(256, count));
            fixture.DelayRecycle = true;
            if (lostReply) fixture.LoseRecycleReplyOnAttempt = 2;
            await fixture.Action("capture-library.batch.recycle");
            await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.batch.confirm") is not null);
            if (lostReply) fixture.Items[5] = fixture.Items[5] with { Available = false };
            await fixture.Action("capture-library.batch.confirm");
            await fixture.RecycleEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            await fixture.Action("capture-library.batch.confirm");
            Check(fixture.RecycleAttempts == 1 && fixture.Node("capture-library.batch.progress") is not null &&
                fixture.Node("capture-library.batch.confirm") is null, "A working batch still accepts confirmation");
            Check(ViewSnapshotValidator.Validate(fixture.View()).Count == 0, "Invalid in-progress batch modal");
            if (stop)
            {
                await fixture.Action("capture-library.batch.stop");
                Check(fixture.Node("capture-library.batch.confirmation") is null, "Stop retained an actionable confirmation");
            }
            fixture.RecycleRelease.TrySetResult();
            await fixture.Until(() => !fixture.Busy && fixture.Node("capture-library.batch.confirmation") is null);
            await fixture.Action("capture-library.batch.confirm");
            if (stop)
                Check(fixture.Recycled == 0 && fixture.RecycleAttempts == 1, "Stop sent another chunk or ignored cancellation");
            else if (lostReply)
            {
                Check(fixture.RecycleAttempts == 2 && fixture.Recycled == 31, "An ambiguous batch was dispatched again");
                await fixture.Action("capture-library.batch.failures");
                Check(fixture.Nodes.Count(n => n.Text?.Contains("No completion receipt") == true) == 16 &&
                    fixture.Nodes.Count(n => n.Text?.Contains("Missing or unreadable.") == true) == 1 &&
                    fixture.Nodes.Count(n => n.Text?.Contains("Skipped after") == true) == 256 - 32 &&
                    ViewSnapshotValidator.Validate(fixture.View()).Count == 0,
                    "Confirmed, unconfirmed and untouched items were conflated");
            }
            else Check(fixture.Items.Select(item => item.Id).SequenceEqual(remaining) && fixture.Recycled == Math.Min(256, count),
                "Batch missed selected files or recycled a file beyond the first 256");
        }
    }
    internal static async Task BatchHideOwnedSelectionStaysUnchanged()
    {
        await using var fixture = new GalleryFixture(); await fixture.Start();
        await fixture.Action("capture-library.selection.toggle"); await fixture.Action("capture-library.selection.all");
        await fixture.Until(() => fixture.Collection?.IndexedSelection?.Count == 3);
        await fixture.Action("capture-library.batch.hide");
        await fixture.Until(() => !fixture.Busy && fixture.LastFeedback?.StartsWith("Only captures from linked folders") == true);
        Check(fixture.Items.Count == 3 && fixture.Recycled == 0 && fixture.Unlinked == 0 &&
            fixture.PermissionRequests.Count == 0 && fixture.Collection?.IndexedSelection?.Count == 3 &&
            fixture.Node("capture-library.batch.confirmation") is null,
            "An inapplicable Hide neither asks for permissions nor mutates or clears the selected originals");
    }

    internal static async Task BatchDeleteRecyclesBothSourcesAndKeepsUnavailableEntries()
    {
        await using var fixture = new GalleryFixture();
        fixture.Items[0] = fixture.Items[0] with { SourceId = new string('b', 32) };
        await fixture.Start();
        await SelectAll();
        var row = fixture.Node("capture-library.selection.actions")!;
        Check(row.Kind == ViewNodeKind.Scroll && row.ScrollAxis == ScrollAxis.Horizontal && row.GridMinimumColumnWidth is null,
            "Share and text actions must use content widths instead of equal oversized grid columns");
        Check(fixture.Node("capture-library.batch.hide")?.Text == "Hide…" && fixture.Node("capture-library.batch.recycle")?.Text == "Delete…",
            "Hide and Delete must be distinct actions");
        if (Environment.GetEnvironmentVariable("CAPTURE_BATCH_FIXTURES") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "selection.json"), SnapshotJson.Serialize(fixture.View()));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixture/capture.wrss"), Path.Combine(directory, "capture.wrss"), true);
        }
        await fixture.Action("capture-library.batch.recycle");
        await fixture.Until(() => fixture.Node("capture-library.batch.confirmation") is not null && !fixture.Busy);
        Check(fixture.Node("capture-library.batch.help")?.Text?.Contains("including files in linked folders") == true,
            "Delete must disclose linked-original recycling before requesting permission");
        await fixture.Action("capture-library.batch.cancel");
        Check(fixture.Recycled == 0 && fixture.Collection?.IndexedSelection?.Count == 3,
            "Cancel preserves both the files and selection");
        await fixture.Action("capture-library.batch.recycle");
        await fixture.Until(() => fixture.Node("capture-library.batch.confirmation") is not null && !fixture.Busy);
        fixture.Items[2] = fixture.Items[2] with { Available = false };
        await fixture.Action("capture-library.batch.confirm");
        await fixture.Until(() => !fixture.Busy && fixture.Recycled == 2);
        Check(fixture.Unlinked == 0 && fixture.Items.Count == 1 && !fixture.Items[0].Available,
            "Delete must recycle both available originals without hiding or removing unavailable entries");
        Check(fixture.PermissionRequests.Any(ids => ids.Contains("media.capture-library.recycle-linked.v1")),
            "Linked originals require the existing explicit recycle permission");
        Check(fixture.Node("capture-library.batch.failures") is not null,
            "An unavailable original is reported as a partial result");

        async Task SelectAll()
        {
            await fixture.Action("capture-library.selection.toggle");
            await fixture.Action("capture-library.selection.all");
            await fixture.Until(() => fixture.Collection?.IndexedSelection?.Count == 3);
        }
    }

    internal static async Task BatchDeletePermissionDenialKeepsAllSources()
    {
        await using var fixture = new GalleryFixture { DeniedPermission = "media.capture-library.recycle-linked.v1" };
        fixture.Items[0] = fixture.Items[0] with { SourceId = new string('b', 32) };
        await fixture.Start();
        await fixture.Action("capture-library.selection.toggle"); await fixture.Action("capture-library.selection.all");
        await fixture.Until(() => fixture.Collection?.IndexedSelection?.Count == 3);
        await fixture.Action("capture-library.batch.recycle");
        await fixture.Until(() => fixture.Node("capture-library.batch.confirmation") is not null && !fixture.Busy);
        await fixture.Action("capture-library.batch.confirm");
        await fixture.Until(() => !fixture.Busy && fixture.PermissionRequests.Any(ids => ids.Contains("media.capture-library.recycle-linked.v1")));
        Check(fixture.Items.Count == 3 && fixture.Recycled == 0 && fixture.Unlinked == 0 && fixture.Collection?.IndexedSelection?.Count == 3,
            "Obtain all permissions before mutating any source; denial must retain the selection");
    }
}
