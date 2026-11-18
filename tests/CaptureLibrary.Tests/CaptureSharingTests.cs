using System.Collections.Concurrent;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class GalleryTests
{
    internal static async Task SharingPreviewUsesExactSavedIdentity()
    {
        var files = new ShareFilesFake();
        await using var fixture = new GalleryFixture(files.Configure); await fixture.Start();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget, "gallery-preview-share", 100); fixture.Host = host;
        using var lease = await host.AcquireAsync("capture-library.entries", 0, 3);
        var declaration = Flatten(lease.Range.Items[0].Root.FocusPresentation!).Single(node => node.Intent?.ContractId == WidgetIntentContracts.FileShare).Intent!;
        Check(fixture.Opened == 0, "Declaring preview Share must not open a file");
        var payload = await fixture.Widget.OnPrepareIntentAsync(declaration.PreparationKey!, declaration);
        Check(payload is not null && fixture.Opened == 1 && files.Offers.Count == 1, "Preview Share did not retain exactly one saved original");
        await fixture.Widget.OnIntentCompletedAsync(new("preview", declaration with { Payload = payload!.Value, PreparationKey = null }, WidgetIntentStatus.Accepted));
        Check(fixture.Released == 1 && files.Released.Count == 1 && files.Canceled.Count == 1, "Preview Share did not release sender custody");
        fixture.Items[0] = fixture.Items[0] with { Revision = 12 };
        Check(await fixture.Widget.OnPrepareIntentAsync(declaration.PreparationKey!, declaration) is null && files.Offers.Count == 1,
            "Stale preview original must not share updated content under old row identity");
    }

    internal static async Task SharingSelectionUsesBoundedExactGrants()
    {
        var files = new ShareFilesFake();
        await using var fixture = new GalleryFixture(files.Configure);
        fixture.Items[1] = fixture.Items[1] with { Media = fixture.Items[1].Media with { ContentType = "video/mp4", DurationSeconds = 5, ByteLength = 1200 } };
        await fixture.Start();
        await fixture.Action("capture-library.selection.toggle");
        await fixture.Action("capture-library.selection.all");
        var declared = fixture.Node("capture-library.batch.share")!.Intent!;
        var payload = await fixture.Widget.OnPrepareIntentAsync(declared.PreparationKey!, declared);
        Check(payload is not null && files.Sources.Count == 3 && files.Offers.Single().FileIds.Distinct().Count() == 3,
            "Selected sharing did not prepare one bounded offer with distinct grants");
        Check(fixture.Opened == 3 && fixture.Released == 0 && fixture.Collection!.IndexedSelection!.Count == 3,
            "Sharing must retain independent originals and preserve selection");
        var completed = new WidgetIntentFeedback("batch", declared with { Payload = payload!.Value, PreparationKey = null }, WidgetIntentStatus.Accepted);
        await fixture.Widget.OnIntentCompletedAsync(completed);
        await fixture.Widget.OnIntentCompletedAsync(completed);
        Check(files.Released.Count == 3 && files.Released.Distinct().Count() == 3 && fixture.Released == 3 && files.Canceled.Count == 1,
            "Batch feedback must release every original and grant exactly once");
    }

    internal static async Task SharingSelectionRejectsChangedOrOversizedSources()
    {
        foreach (var scenario in new[] { "limit", "selection", "revision" })
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var files = new ShareFilesFake { HoldFile = scenario == "selection" ? async () => { entered.TrySetResult(); await release.Task; } : null };
            await using var fixture = new GalleryFixture(files.Configure);
            await fixture.Start();
            if (scenario == "limit")
            {
                for (var i = 4; i <= 11; i++) fixture.Items.Add(fixture.Items[0] with { Id = i.ToString("x32") });
                fixture.Notify();
                await fixture.Until(() => fixture.Collection?.IndexedCollection?.Count == 11);
            }
            await fixture.Action("capture-library.selection.toggle");
            await fixture.Action("capture-library.selection.all");
            var button = fixture.Node("capture-library.batch.share")!;
            var declared = button.Intent!;
            if (scenario == "limit") Check(button.IsDisabled == true, "Oversized batch Share is not disabled");
            if (scenario == "revision") fixture.MetadataOverride = (request, _) =>
            {
                var item = fixture.Items.Single(item => item.Id == request.Id);
                return ValueTask.FromResult(request.Id == fixture.Items[1].Id ? item with { Revision = 99 } : item);
            };
            var preparing = fixture.Widget.OnPrepareIntentAsync(declared.PreparationKey!, declared).AsTask();
            try
            {
                if (scenario == "selection")
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await fixture.Action("capture-library.selection.clear");
                    release.TrySetResult();
                }
                Check(await preparing.WaitAsync(TimeSpan.FromSeconds(5)) is null && files.Offers.IsEmpty,
                    "Changed/oversized selected captures produced a share offer");
                Check(fixture.Opened == fixture.Released && files.Sources.Count == files.Released.Count,
                    "Rejected batch preparation leaked original/file authority");
            }
            finally { release.TrySetResult(); }
        }
    }

    internal static async Task SharingRetainsAuthorityThroughTerminalFeedback()
    {
        foreach (var status in new[] { WidgetIntentStatus.Accepted, WidgetIntentStatus.Rejected, WidgetIntentStatus.Cancelled, WidgetIntentStatus.Unavailable })
        {
            var files = new ShareFilesFake();
            await using var fixture = new GalleryFixture(files.Configure);
            await fixture.Start(); using var host = await OpenEditingSource(fixture);
            var preview = Preview(fixture)!;
            var declared = fixture.Node("capture-library.share")!.Intent!;
            var payload = await fixture.Widget.OnPrepareIntentAsync(declared.PreparationKey!, declared);
            Check(payload is not null && payload.Value.GetProperty("offerId").GetString() == files.OfferId, "Share preparation did not return its opaque offer");
            Check(fixture.Opened == 2 && files.Sources.Single().Id != preview.Id && files.Offers.Count == 1,
                "Saved-capture share must hold its own library attachment rather than borrow the displayed preview");
            Check(fixture.Released == 0 && files.Released.IsEmpty && files.Canceled.IsEmpty,
                "Preparation released source authority before recipient acceptance");
            var resolved = declared with { Payload = payload!.Value, PreparationKey = null };
            await fixture.Widget.OnIntentCompletedAsync(new("share.test", resolved, status));
            Check(files.Released.SequenceEqual([files.FileId]) && files.Canceled.SequenceEqual([files.OfferId]) && fixture.Released == 1 && files.NativeCaptureReleases == 0,
                "Terminal feedback failed to release exactly the file offer and independently opened library capture");
            Check(Preview(fixture)?.Id == preview.Id, "Sharing discarded the displayed capture");
            if (status is WidgetIntentStatus.Rejected or WidgetIntentStatus.Unavailable)
                Check(fixture.LastFeedback?.Contains("accepted this capture", StringComparison.Ordinal) == true, "Unavailable share did not provide useful feedback");
            await fixture.Widget.OnIntentCompletedAsync(new("share.test", resolved, status));
            Check(fixture.Released == 1 && files.Released.Count == 1, "Duplicate terminal feedback released the same grants twice");
        }
    }

    internal static async Task SharingRejectsStalePreparationAndFeedback()
    {
        var files = new ShareFilesFake();
        await using var fixture = new GalleryFixture(files.Configure);
        await fixture.Start(); using var host = await OpenEditingSource(fixture);
        var declared = fixture.Node("capture-library.share")!.Intent!;
        Check(await fixture.Widget.OnPrepareIntentAsync("share.stale", declared) is null && fixture.Opened == 1 && files.Sources.IsEmpty,
            "Stale displayed-capture identity opened a fresh source");
        var payload = await fixture.Widget.OnPrepareIntentAsync(declared.PreparationKey!, declared);
        var foreign = declared with { Payload = JsonSerializer.SerializeToElement(new { offerId = new string('f', 32) }), PreparationKey = null };
        await fixture.Widget.OnIntentCompletedAsync(new("share.old", foreign, WidgetIntentStatus.Rejected));
        Check(files.Released.IsEmpty && files.Canceled.IsEmpty && fixture.Released == 0,
            "Foreign feedback released the current offer");
        Check(fixture.LastFeedback?.Contains("accepted this capture", StringComparison.Ordinal) != true,
            "Foreign rejected feedback overwrote the current capture status");
        await fixture.Widget.OnIntentCompletedAsync(new("share.current", declared with { Payload = payload!.Value, PreparationKey = null }, WidgetIntentStatus.Accepted));
    }

    internal static async Task SharingCleansLateResultsAfterNavigationAndCancellation()
    {
        foreach (var phase in new[] { "file", "offer", "cancel" })
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var files = new ShareFilesFake
            {
                HoldFile = phase != "offer" ? async () => { entered.TrySetResult(); await release.Task; } : null,
                HoldOffer = phase == "offer" ? async () => { entered.TrySetResult(); await release.Task; } : null
            };
            await using var fixture = new GalleryFixture(files.Configure);
            await fixture.Start(); using var host = await OpenEditingSource(fixture);
            var declared = fixture.Node("capture-library.share")!.Intent!;
            using var cancellation = new CancellationTokenSource();
            var preparing = fixture.Widget.OnPrepareIntentAsync(declared.PreparationKey!, declared, cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(await fixture.Widget.OnPrepareIntentAsync(declared.PreparationKey!, declared) is null, "Concurrent share preparation was not bounded");
                if (phase == "cancel") cancellation.Cancel();
                else await fixture.Action("capture-library.back");
                release.TrySetResult();
                JsonElement? prepared = null;
                try { prepared = await preparing.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) when (phase == "cancel") { }
                Check(prepared is null, "Late preparation published an offer after cancellation or navigation");
                Check(files.Released.SequenceEqual([files.FileId]) && files.NativeCaptureReleases == 0,
                    "Late selected-file grant leaked or used the wrong capture release capability");
                Check(fixture.Released == (phase == "cancel" ? 1 : 2), "Late preparation did not reclaim its own library capture");
                Check(files.Offers.Count == (phase == "offer" ? 1 : 0) && files.Canceled.Count == (phase == "offer" ? 1 : 0),
                    "Late preparation leaked an offer or issued one after its source was retired");
            }
            finally { release.TrySetResult(); }
        }
    }

    private sealed class ShareFilesFake
    {
        internal string FileId = new('b', 32), OfferId = new('c', 32);
        internal ConcurrentQueue<CaptureAttachment> Sources = new();
        internal ConcurrentQueue<WidgetFileOfferRequest> Offers = new();
        internal ConcurrentQueue<string> Released = new(), Canceled = new();
        internal int NativeCaptureReleases;
        private int fileSequence;
        private readonly ConcurrentDictionary<string, WidgetFileReference> references = new();
        internal Func<Task>? HoldFile, HoldOffer;
        internal void Configure(WidgetTestHostServicesBuilder builder, GalleryFixture _)
        {
            builder.WithHandler(WidgetFileCapabilities.FromCapture, async (request, _) =>
            {
                Sources.Enqueue(request.Attachment); if (HoldFile is not null) await HoldFile();
                var number = Interlocked.Increment(ref fileSequence);
                var file = new WidgetFileReference(number == 1 ? FileId : number.ToString("x32"), request.Name ?? "capture.png", request.Attachment.ContentType, request.Attachment.ByteLength, long.MaxValue);
                references[file.Id] = file; return file;
            }).WithHandler(WidgetFileCapabilities.Offer, async (request, _) =>
            {
                Offers.Enqueue(request); if (HoldOffer is not null) await HoldOffer();
                return new WidgetFileShareOffer(OfferId, request.FileIds.Select(id => references[id]).Select(file => new WidgetFileMetadata(file.Name, file.ContentType, file.ByteLength)).ToArray(), long.MaxValue);
            }).WithHandler(WidgetFileCapabilities.Release, (request, _) =>
            { Released.Enqueue(request.Id); return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); })
            .WithHandler(WidgetFileCapabilities.CancelOffer, (request, _) =>
            { Canceled.Enqueue(request.Id); return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); })
            .WithHandler(WidgetCaptureCapabilities.Discard, (_, _) =>
            { Interlocked.Increment(ref NativeCaptureReleases); return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); });
        }
    }
}
