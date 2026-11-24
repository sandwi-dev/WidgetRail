using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetBridge;
using WidgetRail.FirstPartyWidgets.CaptureLibrary;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

if(args is ["--linked-recycle-native",var recycleEvidence])
{
    await LinkedRecycleTests.NativeRecycle(recycleEvidence);
    return 0;
}
if(args is ["--thumbnail-edge-proof",var edgeEvidence])
{
    await VideoThumbnailTests.EdgeProof(edgeEvidence);
    return 0;
}
if(args is ["--recording-codecs",var codecSources,var codecEvidence])
{
    await RecordingCodecConsumers.Run(codecSources,codecEvidence);
    return 0;
}

if (args is ["--source-codec", var linkedImage, var linkedVideo, var linkedResult])
{
    await SourceTests.CodecProbe(linkedImage, linkedVideo, linkedResult);
    Console.WriteLine("PASS real linked PNG/MP4 codec, thumbnails and original read lease");
    return 0;
}
if (args is ["--video-thumbnail", var syntheticClip, var output])
{
    syntheticClip = Path.GetFullPath(syntheticClip); output = Path.GetFullPath(output);
    using var source = File.OpenRead(syntheticClip);
    var before = await System.Security.Cryptography.SHA256.HashDataAsync(source);
    var dimensions = await WindowsCaptureImageCodec.CreateThumbnailAsync(syntheticClip, output, default);
    var bytes = await File.ReadAllBytesAsync(output);
    Check(new CaptureLibraryThumbnail(dimensions.Width, dimensions.Height, bytes).IsWellFormed(), "Windows video thumbnail is not a bounded PNG");
    using var original = File.OpenRead(syntheticClip);
    Check(before.SequenceEqual(await System.Security.Cryptography.SHA256.HashDataAsync(original)), "Thumbnail changed its original video");
    Console.WriteLine($"PASS Windows video thumbnail {dimensions.Width}x{dimensions.Height}, {bytes.Length} bytes; original unchanged");
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Hidden gallery filter restores captures without deleting originals", GalleryTests.HiddenFilterRestoresWithoutDeletingOriginal),
    ("Hidden gallery batch restoration keeps empty selection exit available", GalleryTests.HiddenBatchRestoresAndKeepsSelectionExitAvailable),
    ("Hidden captures survive rebuild and restore exact metadata",HiddenCaptureTests.RebuildAndRestore),
    ("Hidden captures continuity proves only favorite changes",HiddenCaptureTests.FavoriteContinuity),
    ("Hidden captures retain canonical choice across interrupted publication",HiddenCaptureTests.InterruptedPublication),
    ("Hidden captures broker operations retain read and manage admission",HiddenCaptureTests.BrokerAdmission),
    ("Hidden captures isolate damaged canonical snapshots from owned media",HiddenCaptureTests.DamagedSnapshotIsolation),
    ("Hidden captures distinguish edits from physical replacement",HiddenCaptureTests.PhysicalIdentity),
    ("Linked folder moves, replacement, recovery paging and offline protection", SourceTests.FolderMoveAndReplacementRecovery),
    ("Hidden captures migrate legacy paths conservatively",HiddenCaptureTests.LegacyMigration),
    ("Hidden captures fence unavailable revoked and cancelled work",HiddenCaptureTests.MissingRevokedAndCancellation),
    ("Hidden captures fail closed on canonical loss and preserve old wire",HiddenCaptureTests.CorruptionAndWire),
    ("Linked recycling preserves removal-only behavior and restores original metadata",LinkedRecycleTests.RemovalAndRecovery),
    ("Linked recycling validates revisions and preserves partial results",LinkedRecycleTests.IdentityAndPartialResults),
    ("Linked recycling requires explicit permission across source owners and honors revocation",LinkedRecycleTests.PermissionAndRevocation),
    ("Linked recycling rejects late replacement and reconciles committed cancellation",LinkedRecycleTests.LateIdentityAndCancellation),
    ("Linked recycling SDK preserves exact revisions and distinct permission",LinkedRecycleTests.SdkRouting),
    ("Linked recycling rejects reparse points and corrupt relative paths",LinkedRecycleTests.PathFences),
    ("Linked recycling native guard rejects before file mutation",LinkedRecycleTests.NativeGuardRejectsBeforeMutation),
    ("Slow bulk recycling preserves 150 receipts across the broker pipe", LinkedRecycleTests.SlowLargeBatchOverPipe),
    ("Linked recycling widget separates original recycle from library removal",GalleryTests.LinkedOriginalRecycleIsSeparateFromRemoval),
    ("Linked recycling widget retains preview after denial or failure",GalleryTests.LinkedRecycleFailureKeepsExactPreview),
    ("Detail action focus survives delayed capture reopening",GalleryTests.DetailFocusSurvivesReopen),
    ("Detail actions retain inline controller bindings and media authority",GalleryTests.DetailActionsAndInlineBindings),
    ("Detail actions preserve busy admission and unavailable-media safety",GalleryTests.DetailBusyAndUnavailableActions),
    ("Capturing stays in gallery after closing saved details",GalleryTests.CaptureKeepsGalleryAfterClosingSavedDetails),
    ("Library capture hands off screenshots and recordings without review", GalleryTests.LibraryCaptureHandsOffWithoutReview),
    ("Screenshot delays honor fresh, restarted and existing profiles", GalleryTests.ScreenshotPreferences),
    ("Approved applications are editable while recording and ignore stale actions", GalleryTests.ApprovalManagementWhileRecording),
    ("Video thumbnail policy repairs legacy cache and preserves screenshot cache",VideoThumbnailTests.CachePolicy),
    ("Recording settings defaults and exact draft qualification",GalleryTests.RecordingAutoAndDraftQualification),
    ("Recording settings reject late probes and external revisions",GalleryTests.RecordingDraftRejectsLateAndExternalChanges),
    ("Sharing selection uses bounded exact grants", GalleryTests.SharingSelectionUsesBoundedExactGrants),
    ("Sharing selection rejects changed and oversized sources", GalleryTests.SharingSelectionRejectsChangedOrOversizedSources),
    ("Sharing retains exact source grants through terminal feedback", GalleryTests.SharingRetainsAuthorityThroughTerminalFeedback),
    ("Sharing rejects stale preparation and unrelated feedback", GalleryTests.SharingRejectsStalePreparationAndFeedback),
    ("Sharing reclaims late results after navigation and cancellation", GalleryTests.SharingCleansLateResultsAfterNavigationAndCancellation),
    ("Named related filters preserve opaque matching and legacy intents", GalleryTests.NamedRelatedFiltersPreserveIdentityAndLegacyIntents),
    ("Default-folder copying supports long linked videos and cancellation", DuplicateTests.LinkedVideoCopyAndCancellation),
    ("Default-folder copies preserve bytes, metadata and related contexts", DuplicateTests.FilesAndContexts),
    ("Default-folder copy SDK and broker retain admission without a picker", DuplicateTests.SdkAndAdmission),
    ("Navigation keeps a coherent preview during delayed opening", GalleryTests.NavigationKeepsCoherentPreviewWhileOpening),
    ("Navigation failure keeps the current capture", GalleryTests.NavigationFailureKeepsCurrentCapture),
    ("Cancelled navigation releases late preview leases", GalleryTests.CancelledNavigationReleasesLatePreview),
    ("Editing saved and linked captures creates a separate copy", GalleryTests.EditSavedAndLinkedCreatesSeparateCopy),
    ("Editing cancellation and unsupported formats preserve the preview", GalleryTests.EditCancelAndUnsupportedKeepPreview),
    ("Late edited save cannot navigate a newer gallery route", GalleryTests.EditLateSaveCannotNavigate),
    ("Late edited preview open releases its unclaimed lease", GalleryTests.EditLateOpenReleasesUnclaimedLease),
    ("Capture edits sessions retain verified input and one completion identity", EditingSessionTests.RetainedSourceAndReceipt),
    ("Capture edits sessions recover a committed copy after receipt publication failure", EditingSessionTests.RecoverCommittedBeforeReceiptReply),
    ("Capture edits sessions support owned temporary captures without saving originals", EditingSessionTests.TemporarySourceAndRetirement),
    ("Capture editing wire commits once and preserves terminal outcomes", EditingWireTests.CompletionAndCancel),
    ("Capture editing wire cancels during progress without lock inversion", EditingWireTests.CancelDuringProgress),
    ("Capture editing wire retains both source and save consent through encoding", EditingWireTests.DualConsent),
    ("Capture editing preparation rejects cancellation-overlapped late results", EditingPreparationTests.CancellationCannotPublishLatePreparation),
    ("Capture edits create a new owned copy and preserve source/context provenance", EditingTests.NewCopyAndContext),
    ("Capture edits release library gate and reject concurrent or stale work", EditingTests.ConcurrentAndStale),
    ("Capture edits cancel on permission retirement/disposal and release originals", EditingTests.CancelRevokeDispose),
    ("Capture edits enforce budgets and recover only owned unpublished staging", EditingTests.BudgetsAndRecovery),
    ("Capture edits native PNG preserves cropped/rotated pixels and alpha", EditingCodecTests.ImagePixels),
    ("Capture edits native MP4 trims retained large originals with audio", EditingCodecTests.VideoRangeAndLargeSource),
    ("Windows capture folder SDK preserves explicit known-location hints", KnownCaptureFolderTests.SdkAndKnownFolderHint),
    ("Windows external folder linking survives replay replacement", GalleryTests.WindowsCapturesActions),
    ("Replay widget host status and session fencing", GalleryTests.ReplayUsesHostStatusAndSessionFences),
    ("Replay widget controller shortcuts preserve session and scope", GalleryTests.ReplayControllerShortcutsFollowSessionAndScope),
    ("Replay widget cancelled and failed starts retain one valid home command", GalleryTests.ReplayUnstartedHomeKeepsOneStart),
    ("Replay widget settings preserve host revision", GalleryTests.ReplaySettingsPreserveHostRevision),
    ("Replay widget stopping remains visible until finalization", GalleryTests.ReplayStoppingRemainsVisible),
    ("Streamlined toolbar preserves capture, filter and batch commands", GalleryTests.StreamlinedToolbarRetainsActions),
    ("Gallery cards preserve composition context controls and accessibility", GalleryTests.CardCompositionAndContext),
    ("Gallery cards favorite through exact item metadata revision", GalleryTests.CardFavoriteUsesExactRevision),
    ("Gallery cards reject stale metadata and preserve batch selection", GalleryTests.CardFavoriteRejectsStaleMetadataAndPreservesSelection),
    ("Gallery clip previews cache identity and withdraw for selection or preference", GalleryTests.ClipPreviewUsesDeclarativeIdentity),
    ("Failed recycle releases and recovers only the unchanged preview", GalleryTests.FailedRecycleReleasesAndRecoversSamePreview),
    ("Gallery batch hide preserves every original", GalleryTests.LinkedRemovalKeepsProviderOriginals),
    ("Gallery batch hide keeps unsupported owned selections", GalleryTests.BatchHideOwnedSelectionStaysUnchanged),
    ("Gallery batch delete recycles both sources and keeps unavailable entries", GalleryTests.BatchDeleteRecyclesBothSourcesAndKeepsUnavailableEntries),
    ("Large batch has one confirmation, honors the selection cap and never retries a lost receipt", GalleryTests.LargeBatchSingleConfirmation),
    ("Gallery batch delete permission denial keeps all sources", GalleryTests.BatchDeletePermissionDenialKeepsAllSources),
    ("Capture folder settings confirm disconnect and preserve originals", GalleryTests.SourceSettingsConnectAndDisconnect),
    ("Saved-video playback retains read authority without temporary copies", PlaybackTests.RetainedOriginalAuthority),
    ("Saved-PNG playback retains read authority without temporary copies", PlaybackTests.RetainedOriginalPngAuthority),
    ("Saved-PNG playback pins original and linked library authority", PlaybackTests.OriginalPngUsesPinnedLibraryAuthority),
    ("Large linked videos use bounded retained playback without raising capture limits", PlaybackTests.LargeLinkedVideo),
    ("Capture contexts preserve associations, origin namespaces and bounded queries", ContextTests.AssociationsAndQueries),
    ("Capture contexts link existing exact-session media with idempotent scoped references", ContextTests.ExistingCapturesBySession),
    ("Caller-owned links support durable dismissal, explicit undo, and stale-action rejection", ContextTests.ReversibleLinks),
    ("Capture subjects reuse inherited destinations across installations, concurrent commits and index rebuild", ContextTests.UniqueSubjectLinks),
    ("Grouped destination unlink and Undo preserve compound references across interruption and rebuild", ContextTests.GroupedDestinationUndo),
    ("Capture runs group exact process instances, rank source hints, and preserve legacy singleton captures", ContextTests.GroupedRuns),
    ("Capture context receipts survive recovery and prevent deleted-media resurrection", ContextTests.RebuildRecoveryAndRetirement),
    ("Capture contexts enforce host origin authority and payload bounds", ContextTests.AuthorityAndBounds),
    ("Related capture intents preserve origin and context identities", GalleryTests.RelatedCapturesIntentAndContextLinks),
    ("Related destination Undo keeps the last link reachable and restores focus", GalleryTests.RelatedDestinationUndoAndFocus),
    ("Closed context discards a late provider result", GalleryTests.ClosedContextDoesNotReopenOnLateResult),
    ("Linked sources preserve originals and enforce read-only ownership", SourceTests.LinkingAndReadOnly),
    ("Folder queries span the index without granting integration folder management", SourceTests.FolderFiltersAndFacets),
    ("Folder selection stays visible and restricted after disconnect", GalleryTests.FolderFilterRemainsExplicitAfterDisconnect),
    ("Capture association offers exact selected revisions without opening media", GalleryTests.AssociationOfferKeepsExactSelection),
    ("Preview sharing retains exact saved identity and rejects changed content", GalleryTests.SharingPreviewUsesExactSavedIdentity),
    ("Gallery L3 requests top controls without resetting collection identity", GalleryTests.QuickControlsKeepsGalleryIdentity),
    ("Linked sources scope grants and reconcile bounded context companions", SourceTests.ScopeAndCompanion),
    ("Linked sources discover live and offline files independently of widgets", SourceTests.OfflineAndLiveDiscovery),
    ("Linked sources picker lifetime and SDK validation", SourceTests.PickerLifetimeAndSdk),
    ("Linked sources broker authority and permission revocation", SourceTests.Authority),
    ("Linked sources file identity and index rebuild preserve removal", SourceTests.IdentityAndIndexRebuild),
    ("Linked sources historical grants never hide active startup sources", SourceTests.HistoricalGrants),
    ("Linked sources preserve legacy client metadata envelopes", SourceTests.LegacyMetadataEnvelope),
    ("Linked sources reviewed updates and exact owner retirement preserve authority", SourceTests.ReviewedOwnerLifecycle),
    ("Gallery exposes cancellation while saving copies", GalleryTests.CopyCanBeCancelledFromGallery),
    ("Cancelled batch reply preserves a newer confirmation", GalleryTests.CancelledBatchReplyDoesNotClearNewConfirmation),
    ("Gallery startup subscription cannot lose a newer snapshot", GalleryTests.InitialSnapshotCannotBeatPostSubscriptionRefresh),
    ("Gallery presentation changes preserve the pending library refresh", GalleryTests.PresentationChangesKeepPendingLibraryRefresh),
    ("Gallery watch read cannot warn the newer explicit preview", GalleryTests.WatchReadCannotWarnNewPreview),
    ("Gallery watch read cannot remap a newer Back request", GalleryTests.WatchReadCannotRemapNewBack),
    ("Gallery metadata edit cannot authorize recycling a changed preview", GalleryTests.MetadataEditDoesNotAuthorizeRecyclingChangedPreviewContent),
    ("Gallery search supersession and live updates retain current results", GalleryTests.SearchAndLiveUpdates),
    ("Gallery selection batch favorite and recycle confirmation", GalleryTests.SelectionBatches),
    ("Gallery selection reconciles host-started held mode", GalleryTests.HeldSelectionReconcilesPresentation),
    ("Explicit refresh detects metadata corruption with unchanged timestamp", QueryTests.ExplicitRefreshRejectsPreservedTimestampCorruption),
    ("Unavailable entry removal never deletes a restored original", QueryTests.UnavailableRemovalNeverDeletesRestoredOriginal),
    ("Legacy capture list and save preserve strict response shapes", QueryTests.LegacyWireShapeRemainsClosed),
    ("Library query ranges, metadata, stale revisions and indexed sorting", QueryTests.QueryAndMetadata),
    ("Library query change events and separate read/manage authority", QueryTests.EventsAndAuthority),
    ("Library query batch metadata, recoverable removal and restoration", QueryTests.BatchesAndRecycle),
    ("Library query large-index filtered ranges avoid metadata rescans", QueryTests.LargeIndex),
    ("Index rebuild preserves titles, favorites, originals and recycled recovery", QueryTests.IndexRebuildPreservesMetadata),
    ("Capture copying enforces consent, interactive admission and revocation", CopyTests.Authority),
    ("Capture copying preserves originals, revisions and existing destination files", CopyTests.Files),
    ("Capture copying cancels staged output without leaving partial files", CopyTests.MidCopyCancellation),
    ("Capture copying scopes picker lifetime and validates SDK results", CopyTests.PickerAndSdk),
    ("Saved originals survive store recreation; repeat saves deduplicate; exports are exact", Persistence),
    ("Corrupt data cannot be opened or exported", Corruption),
    ("Damaged metadata remains visible and deletable", DamagedMetadata),
    ("Disk space checks preserve existing items and permit retry after deletion", Quotas),
    ("Orphaned copies are cleaned without evicting saved originals", Orphans),
    ("Linked storage and export directories are refused without touching their targets", LinkedPaths),
    ("Deleting a leased original is logical immediately and cleans disk after release", LeasedDelete),
    ("Locked small previews reserve their actual bytes rather than the 128 MiB ceiling", PendingByteBudget),
    ("Missing metadata never causes automatic deletion of a saved original", MissingMetadata),
    ("Gallery realizes only requested rows and restores a leased item after preview", Paging),
    ("Cancelled saves leave no published item", Cancellation),
    ("Library imports are owned and require library authority rather than capture authority", Ownership),
    ("Broker separates saved-media reading from recording and destructive management", BrokerAuthority),
    ("Full-resolution request preserves its resolution through the capture queue", OriginalRequest),
    ("Windows codec makes bounded context copies without changing odd-sized 4K originals", Codec),
    ("Capture Library controller flow autosaves and confirms deletion", WidgetFlow),
    ("SDK generic capture reads route saved references through library permission", SdkRouting),
    ("Capture settings persist Auto constraints and reject unavailable recording before capture", SettingsFlow),
    ("Recording requests only its profile and preserves the recommended default", RecordingDiscovery),
    ("Indexed gallery pages avoid rescanning and exceed the old 200-item quota", IndexedGallery),
    ("External renames edits and deletes refresh indexed captures and thumbnails", ExternalChanges),
    ("Opening the capture folder is cancellable and owned by the host handoff", FolderHandoff),
    ("A damaged disposable index rebuilds without changing saved media", IndexRecovery),
    ("High-entropy thumbnails stream within broker bounds and honor library-read revocation", ThumbnailTransport),
    ("Missing library permission offers a scoped Settings intent without prompting", PermissionLink),
    ("Capture action waits for permission then continues without a widget reload", PermissionContinuation),
    ("Capture Library theme compiles with supported logical text alignment", Style),
};
var failures = 0;
if(args is ["--test-prefix",var prefix])
{
    tests=tests.Where(test=>test.Name.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
    if(tests.Length==0) throw new ArgumentException("No CaptureLibrary tests matched the requested prefix.");
}
foreach (var test in tests) try { await test.Run(); Console.WriteLine("PASS " + test.Name); } catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + error); if (error is ProtocolValidationException protocol) Console.WriteLine(JsonSerializer.Serialize(protocol.Errors)); }
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed"); return failures == 0 ? 0 : 1;

static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
static Task Style()
{
    var parsed=WidgetRail.WidgetStyling.WrssParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixture/capture.wrss")),"capture.wrss");
    Check(parsed.IsValid);
    var compiled=WidgetRail.WidgetStyling.WrssThemeCompiler.Compile([parsed.Document]);
    Check(compiled.IsValid,string.Join("; ",compiled.Diagnostics.Select(d=>d.Message)));
    var modal = compiled.Theme!.Resolve(new("stack", "capture-library.confirm", new HashSet<string> { "wrail-modal" }, new HashSet<WidgetRail.WidgetStyling.WrssPseudoState>())).Properties;
    Check(modal["width"].Number == 560 && modal["height"].Number == 320, "Confirmation geometry must apply to the actual SDK modal class");
    var folders = compiled.Theme.Resolve(new("stack","capture-library-sources",new HashSet<string>{"wrail-modal"},new HashSet<WidgetRail.WidgetStyling.WrssPseudoState>())).Properties;
    Check(folders["width"].Number == 760 && folders["height"].Number == 520,"Folder management must have room for complete folder actions");
    var gallery = compiled.Theme.Resolve(new("scroll", StyleClasses: new HashSet<string> { "capture-library", "capture-library-base" }));
    var preview = compiled.Theme.Resolve(new("stack", StyleClasses: new HashSet<string> { "capture-library", "capture-detail-page" }));
    var card = compiled.Theme.Resolve(new("stack", StyleClasses: new HashSet<string> { "capture-settings-section" }));
    Check(gallery.Get("background")?.Text == "transparent", "Gallery and settings must expose the host acrylic base");
    Check(preview.Get("background")?.Text == "transparent", "Detail root must expose the desktop acrylic without changing the media player fill");
    Check(card.Get("background") is not null && card.Get("background")?.Text != "transparent", "Settings cards must retain their existing fill");
    return Task.CompletedTask;
}
static async Task Reject(Func<Task> action) { try { await action(); } catch (Exception error) when (error is BrokerException or IOException) { return; } throw new Exception("Operation should have been refused"); }
static async Task Persistence()
{
    using var f = new Fixture();
    var saved = await f.Store.SaveAsync(f.Source, default);
    Check((await f.Store.SaveAsync(f.Source, default)).Id == saved.Id);
    var restarted = new CaptureLibraryStore(f.Library, f.Exports);
    Check((await restarted.ListAsync(0, default)).TotalCount == 1);
    await using (var read = await restarted.OpenAsync(saved.Id, CaptureImageResolution.Original, default))
    { Check(read.Media.Width == f.Source.Attachment.Width); Check(read.Stream.Length == f.Bytes.Length); }
    var exported = await restarted.ExportAsync(saved.Id, default);
    Check(File.ReadAllBytes(Path.Combine(f.Exports, exported.FileName)).SequenceEqual(f.Bytes));
    await restarted.DeleteAsync(saved.Id, default); await restarted.DeleteAsync(saved.Id, default);
    Check((await restarted.ListAsync(0, default)).TotalCount == 0);
    Check(!File.Exists(Path.Combine(f.Exports, exported.FileName)), "Deletion left the canonical saved file behind");
}
static async Task Corruption()
{
    using var f = new Fixture(); var saved = await f.Store.SaveAsync(f.Source, default);
    File.WriteAllBytes(f.MediaPath(saved.Id), [0, 1]);
    await Reject(async () => { await using var read = await f.Store.OpenAsync(saved.Id, CaptureImageResolution.Original, default); });
    await Reject(async () => { await f.Store.ExportAsync(saved.Id, default); });
    await f.Store.DeleteAsync(saved.Id, default); Check((await f.Store.ListAsync(0, default)).TotalCount == 0);
}
static async Task DamagedMetadata()
{
    using var f = new Fixture(); var item = await f.Store.SaveAsync(f.Source, default);
    File.WriteAllText(Path.Combine(f.Library, item.Id + ".json"), "not json");
    var page = await f.Store.ListAsync(0, default, refresh: true); Check(page.Items.Single().Available == false && CaptureLibraryLimits.Valid(page));
    await f.Store.DeleteAsync(item.Id, default); Check((await f.Store.ListAsync(0, default)).TotalCount == 0);
}
static async Task Quotas()
{
    using var f = new Fixture(); using var store = new CaptureLibraryStore(f.Library, f.Exports)
    { AvailableFreeBytes=()=>16L*1024*1024+20-Directory.EnumerateFiles(f.Exports).Sum(p=>new FileInfo(p).Length) };
    var first = await store.SaveAsync(f.Source, default);
    Check((await store.SaveAsync(f.Source, default)).Id == first.Id);
    var other = f.Bytes.ToArray(); other[^1] = 1; File.WriteAllBytes(f.Source.Path, other);
    await Reject(async () => { await store.SaveAsync(f.Source, default); });
    Check((await store.ListAsync(0, default)).TotalCount == 1);
    await store.DeleteAsync(first.Id, default);
    using var tooSmall = new CaptureLibraryStore(f.Library, f.Exports) { AvailableFreeBytes=()=>16L*1024*1024+10 };
    await Reject(async () => { await tooSmall.SaveAsync(f.Source, default); });
    Check((await store.ListAsync(0, default)).TotalCount == 0);
    await store.SaveAsync(f.Source, default); Check((await store.ListAsync(0, default)).TotalCount == 1);
}
static async Task Orphans()
{
    using var f = new Fixture(); var saved = await f.Store.SaveAsync(f.Source, default);
    File.WriteAllBytes(Path.Combine(f.Library, new string('A', 32) + ".png"), f.Bytes);
    File.WriteAllText(Path.Combine(f.Library, new string('A', 32) + ".json.pending"), "interrupted transaction marker");
    File.WriteAllBytes(Path.Combine(f.Library, "context-" + new string('B', 32) + ".png"), f.Bytes);
    var page = await f.Store.ListAsync(0, default, refresh: true);
    Check(page.TotalCount == 1 && page.StoredBytes == f.Bytes.Length && File.Exists(f.MediaPath(saved.Id)));
}
static async Task LinkedPaths()
{
    using var f = new Fixture();
    var outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside);
    var marker = Path.Combine(outside, "private.txt"); File.WriteAllText(marker, "unchanged");
    static async Task Junction(string link, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference='Stop'; $null=New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "'");
        using var process = System.Diagnostics.Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync(); await output;
        if (process.ExitCode != 0) throw new Exception("Junction fixture failed: " + await error);
    }
    await Junction(f.Library, outside);
    try { await Reject(async () => { await f.Store.ListAsync(0, default); }); Check(File.ReadAllText(marker) == "unchanged"); }
    finally { Directory.Delete(f.Library); }
    await Junction(f.Exports, outside);
    try { await Reject(async () => { await f.Store.SaveAsync(f.Source, default); }); Check(Directory.GetFiles(outside).Length == 1); }
    finally { Directory.Delete(f.Exports); }
}
static async Task LeasedDelete()
{
    using var f = new Fixture(); var saved = await f.Store.SaveAsync(f.Source, default);
    await using (var read = await f.Store.OpenAsync(saved.Id, CaptureImageResolution.Original, default))
    {
        await f.Store.DeleteAsync(saved.Id, default);
        Check((await f.Store.ListAsync(0, default)).TotalCount == 0, "A pending deletion remained in the list");
        Check(read.Stream.ReadByte() == f.Bytes[0], "Existing reader was broken by delete");
    }
    await f.Store.ListAsync(0, default);
    Check(!File.Exists(f.MediaPath(saved.Id)) && !File.Exists(Path.Combine(f.Library, saved.Id + ".deleted")));
}
static async Task PendingByteBudget()
{
    using var captures = new WindowCaptureRegistry(); var owner = new object(); var readers = new List<FileStream>();
    try
    {
        for (var i = 0; i < 3; i++)
        {
            var ticket = captures.Enqueue(owner, Fixture.Identity, WindowCaptureKind.Screenshot);
            var request = captures.Take()!;
            File.WriteAllBytes(request.OutputPath, [137,80,78,71,13,10,26,10,0,0,0,0]);
            captures.Complete(new(ticket.RequestId, 100, 100, 0));
            var status = captures.Status(owner, ticket.RequestId);
            Check(status.Phase == WindowCapturePhase.Ready, "Small locked previews exhausted the enlarged image budget");
            readers.Add(new FileStream(request.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read));
            captures.Discard(owner, status.Attachment!.Id);
        }
    }
    finally { foreach (var reader in readers) reader.Dispose(); }
    await Task.CompletedTask;
}
static async Task MissingMetadata()
{
    using var f = new Fixture(); var saved = await f.Store.SaveAsync(f.Source, default);
    File.Delete(Path.Combine(f.Library, saved.Id + ".json"));
    var page = await f.Store.ListAsync(0, default, refresh: true);
    Check(page.TotalCount == 1 && !page.Items.Single().Available && File.Exists(f.MediaPath(saved.Id)), "Missing metadata evicted a saved original");
    await f.Store.DeleteAsync(saved.Id, default);
    Check((await f.Store.ListAsync(0, default)).TotalCount == 0 && !File.Exists(f.MediaPath(saved.Id)));
}
static async Task Paging()
{
    var media = new CaptureAttachment(new string('A', 32), "image/png", 100, 100, 1, 0, long.MaxValue);
    var items = Enumerable.Range(1, 2001).Select(i => new CaptureLibraryItem(i.ToString("x32"), media, i)).ToArray();
    var requested = new List<(int Offset,int Count)>();
    var client = new WidgetTestHostServicesBuilder().WithCaptureContinuity().WithPrivateState(new())
        .WithHandler(WidgetCaptureLibraryCapabilities.Query,(request,_)=>{
            requested.Add((request.Offset,request.Count));
            return ValueTask.FromResult(new CaptureLibraryQueryPage(items.Skip(request.Offset).Take(request.Count).ToArray(),request.Offset,items.Length,items.Length,"a"));})
        .WithHandler(WidgetCaptureLibraryCapabilities.Find,(request,_)=>ValueTask.FromResult(new CaptureLibraryPosition(Array.FindIndex(items,item=>item.Id==request.Id),items.Length,"a")))
        .WithResponse(WidgetCaptureLibraryCapabilities.Applications,new CaptureLibraryApplications([],false))
        .WithHandler(WidgetCaptureLibraryCapabilities.Folders,(request,_)=>ValueTask.FromResult(new CaptureLibraryFolders([new("", "WidgetRail captures", 0)],request.Revision ?? "a")))
        .WithHandler(WidgetCaptureLibraryCapabilities.GetMetadata,(request,_)=>ValueTask.FromResult(items.Single(item=>item.Id==request.Id)))
        .WithResponse(WidgetCaptureLibraryCapabilities.Groups,new CaptureLibraryGroups([new("unknown",items.Length)],items.Length,"a"))
        .WithHandler(WidgetCaptureLibraryCapabilities.Open,(_,_)=>ValueTask.FromResult(media with {Origin=CaptureAttachmentOrigin.SavedLibrary}))
        .WithResponse(WidgetCaptureLibraryCapabilities.Release,new WidgetCapabilityAcknowledgement(true)).Build();
    var widget=WidgetTestHost.Attach(new CaptureLibraryWidget(),client);
    ViewSnapshot View()=>widget.RenderSnapshot("library-pages",1);
    IEnumerable<ViewNode> Walk(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Walk));
    async Task Until(Func<bool> ready){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!ready())await Task.Delay(10,timeout.Token);}
    try
    {
        await WidgetTestHost.InitializeAsync(widget);await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
        await Until(()=>Walk(View().Root).Any(n=>n.IndexedCollection?.Count==2001)&&!Walk(View().Root).Any(n=>n.IsBusy==true));
        Check(!Walk(View().Root).Any(n=>n.ArtworkHandle is not null),"Gallery eagerly published thumbnails");
        using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"library-pages",100);
        using var lease=await host.AcquireAsync("capture-library.entries",1000,3);
        Check(lease.Range.Items.Count==3 && lease.Range.Items.SelectMany(i=>Walk(i.Root)).Count(n=>n.ArtworkHandle is not null)==3);
        Check(requested.All(r=>r.Count<=64)&&requested.Any(r=>r.Offset==1000&&r.Count==3),"Gallery did not use bounded indexed ranges");
        Check(lease.RouteAction(items[1001].Id,ControllerButton.A) is not null,"Leased item open rejected");
        await Until(()=>Walk(View().Root).Any(n=>n.Id=="capture-library.preview")&&!Walk(View().Root).Any(n=>n.IsBusy==true));
        await widget.OnActionAsync(new("capture-library.back","capture-library.back"));
        await Until(()=>Walk(View().Root).Any(n=>n.IndexedCollection is not null)&&!Walk(View().Root).Any(n=>n.IsBusy==true));
        Check(View().FocusGroupEntryRequest?.IndexedItem is {Index:1001} returned&&returned.ItemKey==items[1001].Id,"Back did not restore the exact query occurrence");
        Check(ViewSnapshotValidator.Validate(View()).Count==0&&WinUiPresentationContract.Validate(View()).Count==0);
    }
    finally{await WidgetTestHost.DestroyAsync(widget);}
}
static async Task Cancellation()
{
    using var f = new Fixture(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await f.Store.SaveAsync(f.Source, cancelled.Token); throw new Exception("Cancelled save was accepted"); } catch (OperationCanceledException) { }
    Check((await f.Store.ListAsync(0, default)).TotalCount == 0);
}
static async Task Ownership()
{
    using var f = new Fixture(); using var captures = new WindowCaptureRegistry(); var owner = new object(); var other = new object();
    var saved = await f.Store.SaveAsync(f.Source, default);
    await using var opened = await f.Store.OpenAsync(saved.Id, CaptureImageResolution.Original, default);
    var imported = await captures.ImportAsync(owner, Fixture.Identity, opened, default);
    Check(imported.Origin == CaptureAttachmentOrigin.SavedLibrary && imported.Id != saved.Id);
    await Reject(() => Task.FromResult(captures.Read(other, new(imported.Id, 0), PlatformCapabilities.CaptureLibraryReadV1)));
    await Reject(() => Task.FromResult(captures.Read(owner, new(imported.Id, 0))));
    Check(captures.Read(owner, new(imported.Id, 0), PlatformCapabilities.CaptureLibraryReadV1).Bytes.SequenceEqual(f.Bytes));
    captures.Retire(owner, PlatformCapabilities.WindowCaptureV1);
    Check(captures.ResolveOwned(owner, imported).RequiredCapability == PlatformCapabilities.CaptureLibraryReadV1);
    captures.Retire(owner, PlatformCapabilities.CaptureLibraryReadV1);
    await Reject(() => Task.FromResult(captures.ResolveOwned(owner, imported)));
    Check((await f.Store.ListAsync(0, default)).TotalCount == 1, "Revocation deleted retained original");
}
static async Task BrokerAuthority()
{
    using var f = new Fixture(); var saved = await f.Store.SaveAsync(f.Source, default);
    var consent = new ConsentStore(Path.Combine(f.Root, "consent"));
    await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibraryReadV1, ConsentDecision.Grant);
    var simulation = new SimulatedPlatformBrokerBackend();
    await using var backend = new CompositePlatformBrokerBackend(simulation, simulation, captureLibrary: f.Store);
    await using var broker = new PlatformCapabilityBroker(Fixture.Identity, [PlatformCapabilities.CaptureLibraryReadV1, PlatformCapabilities.CaptureLibraryManageV1], consent, backend);
    broker.SetLifecycle(BrokerLifecycleState.Interactive); long sequence = 0;
    Task<JsonElement> Call(string capability, string operation, object payload) => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion, ++sequence, Fixture.Identity, capability, operation, BrokerJson.ToElement(payload)));
    var imported = BrokerJson.ParsePayload<CaptureAttachment>(await Call(PlatformCapabilities.CaptureLibraryReadV1, "capture-library.open", new CaptureLibraryOpenRequest(saved.Id)));
    Check(imported.Origin == CaptureAttachmentOrigin.SavedLibrary);
    await Reject(() => Call(PlatformCapabilities.CaptureLibraryManageV1, "capture-library.delete", new CaptureLibraryReference(saved.Id)));
    await Reject(() => Call(PlatformCapabilities.WindowCaptureV1, PlatformCapabilities.WindowCaptureRequest, new WindowCaptureRequest(WindowCaptureKind.Screenshot)));
    await consent.SetDecisionAsync(Fixture.Identity,PlatformCapabilities.CaptureLibraryManageV1,ConsentDecision.Grant); await broker.RefreshConsentAsync();
    var opening=Call(PlatformCapabilities.CaptureLibraryManageV1,"capture-library.open-folder",new {});
    using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3))) while(CaptureLibraryFolderRequests.Pending is null) await Task.Delay(10,timeout.Token);
    broker.SetLifecycle(BrokerLifecycleState.Background);
    CaptureLibraryFolderRequests.Complete(CaptureLibraryFolderRequests.Pending!.Id,true);
    Check((await opening).GetProperty("acknowledged").GetBoolean(),"Intentional handoff cancelled an authorized folder request");
    await Reject(()=>Call(PlatformCapabilities.CaptureLibraryManageV1,"capture-library.open-folder",new {}));
    broker.SetLifecycle(BrokerLifecycleState.Interactive);
    await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibraryReadV1, ConsentDecision.Deny); await broker.RefreshConsentAsync();
    await Reject(() => Call(PlatformCapabilities.CaptureLibraryReadV1, "capture-library.read", new CaptureReadRequest(imported.Id, 0)));
    await Reject(() => Task.FromResult(WindowCaptureRegistry.Shared.Resolve(Fixture.Identity, imported.Id)));
}
static async Task OriginalRequest()
{
    var legacy = new WindowCaptureRequest(WindowCaptureKind.Screenshot); legacy.Deconstruct(out var legacyKind);
    Check(legacyKind == WindowCaptureKind.Screenshot && !BrokerJson.ToElement(legacy).GetRawText().Contains("resolution", StringComparison.OrdinalIgnoreCase));
    var original = BrokerJson.ParsePayload<WindowCaptureRequest>(BrokerJson.ToElement(new WindowCaptureRequest(WindowCaptureKind.Screenshot, CaptureImageResolution.Original)));
    Check(original.Resolution == CaptureImageResolution.Original);
    using var captures = new WindowCaptureRegistry(); var owner = new object();
    var ticket = captures.Enqueue(owner, Fixture.Identity, WindowCaptureKind.Screenshot, CaptureImageResolution.Original);
    var pending = captures.Take()!; Check(pending.RequestId == ticket.RequestId && pending.Resolution == CaptureImageResolution.Original);
    captures.Cancel(owner, ticket.RequestId);
    Check(new CaptureAttachment(new string('A',32), "image/png", 3841, 2161, 24 * 1024 * 1024, 0, long.MaxValue).IsWellFormed());
    Check(!new CaptureAttachment(new string('A',32), "image/png", 16384, 16384, 1, 0, long.MaxValue).IsWellFormed());
    await Task.CompletedTask;
}
static async Task Codec()
{
    using var f = new Fixture(); var input = Path.Combine(f.Root, "large.png");
    using (var image = new InMemoryRandomAccessStream())
    {
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, image);
        var pixels = new byte[3841 * 2161 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(i % 255); pixels[i + 3] = 255; }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 3841, 2161, 96, 96, pixels); await encoder.FlushAsync(); image.Seek(0);
        using var source = image.AsStreamForRead(); using var output = File.Create(input); await source.CopyToAsync(output);
    }
    var before = await WidgetRail.PackageDistribution.PackageIntegrity.HashFileAsync(input, default);
    var generated = 0;
    var store = new CaptureLibraryStore(f.Library, f.Exports, WindowsCaptureImageCodec.CreateContextAsync,
        async (sourcePath, target, token) => { generated++; return await WindowsCaptureImageCodec.CreateThumbnailAsync(sourcePath, target, token); });
    var attachment = f.Source.Attachment with { Width = 3841, Height = 2161, ByteLength = new FileInfo(input).Length };
    var saved = await store.SaveAsync(new(attachment, input), default);
    await using (var context = await store.OpenAsync(saved.Id, CaptureImageResolution.Context, default))
    { Check(context.Media.Width == 1919 && context.Media.Height == 1080 && context.Media.ByteLength <= CaptureLimits.MaximumContextBytes, $"Unexpected context geometry {context.Media.Width}x{context.Media.Height}"); }
    Check((await WidgetRail.PackageDistribution.PackageIntegrity.HashFileAsync(input, default)).SequenceEqual(before));
    Check(!Directory.EnumerateFiles(f.Library, "context-*.png").Any(), "Temporary image rendition leaked");
    var thumbnail = await store.ThumbnailAsync(saved.Id, default);
    Check(thumbnail.IsWellFormed() && thumbnail.Width == 255 && thumbnail.Height == 144, "Thumbnail is not aspect-preserving and bounded");
    using (var encoded = new InMemoryRandomAccessStream())
    {
        var stream = encoded.AsStreamForWrite(); await stream.WriteAsync(thumbnail.Bytes); await stream.FlushAsync(); encoded.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(encoded);
        using var decoded = await decoder.GetSoftwareBitmapAsync();
        Check(decoded.PixelWidth == thumbnail.Width && decoded.PixelHeight == thumbnail.Height);
    }
    Check(BrokerJson.SerializeResponse(new(1, 1, true, BrokerJson.ToElement(thumbnail), null)).Length < BrokerJson.MaximumResponseBytes);
    Check((await store.ThumbnailAsync(saved.Id, default)).Bytes.SequenceEqual(thumbnail.Bytes) && generated == 1, "Warm thumbnail was regenerated");
    var cache = Path.Combine(f.Library, saved.Id + ".thumbnail");
    File.WriteAllBytes(cache, [1, 2, 3]);
    Check((await store.ThumbnailAsync(saved.Id, default)).IsWellFormed() && generated == 2, "Invalid derivative did not recover");
    Check((await store.ListAsync(0, default)).StoredBytes == attachment.ByteLength, "Thumbnail changed original library quota");
    await store.DeleteAsync(saved.Id, default); Check(!File.Exists(cache), "Thumbnail survived original deletion");
}
static async Task SdkRouting()
{
    var attachment = new CaptureAttachment(new string('A', 32), "image/png", 1, 1, 3, 0, long.MaxValue) { Origin = CaptureAttachmentOrigin.SavedLibrary };
    var client = new WidgetTestHostServicesBuilder().WithResponse(WidgetCaptureLibraryCapabilities.Read, new CaptureReadChunk(0, [1, 2, 3], true))
        .WithResponse(WidgetCaptureLibraryCapabilities.Release, new WidgetCapabilityAcknowledgement(true)).Build();
    var widget = WidgetTestHost.Attach(new Probe(), client);
    Check((await widget.Read(attachment)).SequenceEqual(new byte[] { 1, 2, 3 })); await widget.Release(attachment);
}
static async Task ThumbnailTransport()
{
    using var f = new Fixture(); var path = Path.Combine(f.Root, "noise.png");
    using (var memory = new InMemoryRandomAccessStream())
    {
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory);
        var pixels = new byte[256 * 144 * 4]; new Random(42).NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 256, 144, 96, 96, pixels);
        await encoder.FlushAsync(); memory.Seek(0);
        using var input = memory.AsStreamForRead(); using var output = File.Create(path); await input.CopyToAsync(output);
    }
    var store = new CaptureLibraryStore(f.Library, f.Exports, null, WindowsCaptureImageCodec.CreateThumbnailAsync);
    var item = await store.SaveAsync(new(f.Source.Attachment with { Width = 256, Height = 144, ByteLength = new FileInfo(path).Length }, path), default);
    var consent = new ConsentStore(Path.Combine(f.Root, "consent"));
    await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibraryReadV1, ConsentDecision.Grant);
    var simulation = new SimulatedPlatformBrokerBackend();
    await using var backend = new CompositePlatformBrokerBackend(simulation, simulation, captureLibrary: store);
    await using var broker = new PlatformCapabilityBroker(Fixture.Identity, [PlatformCapabilities.CaptureLibraryReadV1], consent, backend);
    broker.SetLifecycle(BrokerLifecycleState.Interactive); long sequence = 0; var chunks = 0;
    async Task<JsonElement> Call(CaptureLibraryThumbnailRequest request)
    {
        var result = await broker.ExecuteAsync(new(1, ++sequence, Fixture.Identity, PlatformCapabilities.CaptureLibraryReadV1, "capture-library.thumbnail", BrokerJson.ToElement(request)));
        Check(BrokerJson.SerializeResponse(new(1, sequence, true, result, null)).Length <= BrokerJson.MaximumResponseBytes);
        return result;
    }
    var client = new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureLibraryCapabilities.Thumbnail,
        async (request, _) => { chunks++; return BrokerJson.ParsePayload<CaptureLibraryThumbnailChunk>(await Call(request)); }).Build();
    var widget = WidgetTestHost.Attach(new Probe(), client);
    var result = await widget.Thumbnail(item.Id);
    Check(result.IsWellFormed() && result.Bytes.Length > 72 * 1024 && chunks > 1, "Fixture did not exercise bounded chunk transport");
    await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibraryReadV1, ConsentDecision.Deny); await broker.RefreshConsentAsync();
    await Reject(() => Call(new(item.Id)));
    await store.DeleteAsync(item.Id, default);
}
static async Task WidgetFlow()
{
    var id = new string('B', 32); var source = new CaptureAttachment(new string('C',32), "image/png", 3840, 2160, 1000, 0, long.MaxValue)
        { SourceApplication = new("Example Game", "Example Game") };
    var page = new CaptureLibraryPage([], 0, 0, 0); var saves = 0; var deletes = 0;
    var saveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var deleteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var client = new WidgetTestHostServicesBuilder()
        .WithPrivateState(new())
        .WithResponse(WidgetPermissionCapabilities.Request, new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted))
        .WithResponse(WidgetCaptureCapabilities.Validate, new CaptureConfigurationValidation(true))
        .WithCaptureQuery(() => page)
        .WithHandler(WidgetCaptureLibraryCapabilities.List, (_, _) => ValueTask.FromResult(page))
        .WithHandler(WidgetCaptureCapabilities.SaveScreenshot, async (request, token) =>
        {
            Check(request.Screenshot is { MaximumWidth: null, MaximumHeight: null } && request.Recording is null);
            saves++;
            await saveGate.Task.WaitAsync(token);
            page = new([new CaptureLibraryItem(id, source, 1)], 0, 1, 1000);
            return new WidgetCapabilityAcknowledgement(true);
        })
        .WithResponse(WidgetCaptureLibraryCapabilities.Open, source with { Origin = CaptureAttachmentOrigin.SavedLibrary })
        .WithHandler(WidgetCaptureLibraryCapabilities.Recycle, async (request, token) => { deletes++; await deleteGate.Task.WaitAsync(token); page = new([],0,0,0); return new CaptureLibraryBatchResult(request.Items.Select(item=>new CaptureLibraryMutationResult(item.Id)).ToArray()); }).Build();
    var widget = WidgetTestHost.Attach(new CaptureLibraryWidget(), client);
    ViewSnapshot View() => widget.RenderSnapshot("library-test", 1);
    IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    ViewNode? Node(string name) => Walk(View().Root).FirstOrDefault(n => n.Id == name);
    async Task Until(Func<bool> ready)
    {
        var end = Environment.TickCount64 + 5000;
        while (!ready())
        {
            if (Environment.TickCount64 >= end)
                throw new TimeoutException($"Gallery did not settle: saves={saves}, count={page.TotalCount}, " +
                    string.Join(", ", Walk(View().Root).Select(n => n.Id + ":" + (n.IndexedCollection?.Count.ToString() ?? n.Text ?? n.IsBusy?.ToString()))));
            await Task.Delay(10);
        }
    }
    try
    {
        await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        await Until(() => Node("capture-library.empty") is not null && Node("capture-library.photo")?.IsBusy != true);
        Check(View().Surface?.Appearance == WidgetSurfaceAppearance.DesktopAcrylic && View().Root.StyleClasses.Contains("capture-library-base"), "Gallery must expose a desktop acrylic base");
        Check(View().Surface is { PreferredWidth: 1180, PreferredHeight: 700, WidthMode: WidgetSurfaceAxisMode.Preferred, HeightMode: WidgetSurfaceAxisMode.Preferred }, "Material changes must preserve the library, preview and settings extent");
        Check(View().ProtocolVersion >= ProtocolConstants.DesktopAcrylicVersion, "Gallery must declare desktop acrylic protocol support");
        await widget.OnActionAsync(new("capture-library.photo", "capture-library.photo"));
        await Until(() => saves == 1 && Node("capture-library.photo")?.IsBusy == true);
        Check(View().Root.Transition?.Key == "home" && Node("capture-library.save") is null);
        saveGate.TrySetResult();
        await Until(() => Node("capture-library.photo")?.IsBusy != true);
        await widget.OnActionAsync(new("capture-library.refresh","capture-library.refresh"));
        await Until(() => Walk(View().Root).Any(node => node.IndexedCollection?.Count == 1) && Node("capture-library.photo")?.IsBusy != true);
        using var galleryHost = WidgetTestHost.CreateIndexedCollectionHost(widget, "library-test", 100);
        using (var lease = await galleryHost.AcquireAsync("capture-library.entries", 0, 1))
            Check(lease.RouteAction(id, ControllerButton.A) is not null);
        await Until(() => Node("capture-library.delete") is not null && Node("capture-library.delete")?.IsBusy != true);
        Check(View().Surface?.Appearance == WidgetSurfaceAppearance.DesktopAcrylic && View().Root.StyleClasses.Contains("capture-detail-page"), "Saved detail must expose its desktop acrylic base");
        Check(View().Surface is { PreferredWidth: 1180, PreferredHeight: 700, WidthMode: WidgetSurfaceAxisMode.Preferred, HeightMode: WidgetSurfaceAxisMode.Preferred }, "Material changes must preserve the library, preview and settings extent");
        Check(Node("capture-library.detail.info") is { } info && Walk(info).Any(node => node.Id == "capture-library.delete") &&
            Node("capture-library.detail.header") is {} header&&Walk(header).Any(node => node.Id == "capture-library.back") && Node("capture-library.save") is null,
            "Saved detail must keep file actions in the sidebar and library return in the header without an unsaved Save action");
        Export("detail-saved", View());
        await widget.OnActionAsync(new("capture-library.delete", "capture-library.delete"));
        Check(View().InitialFocusId == "capture-library.delete.cancel" && deletes == 0);
        Check(Node("capture-library.confirm")!.Shortcuts.Single(shortcut => shortcut.Button == ControllerButton.B).ActionId == "capture-library.delete.cancel",
            "B dismisses the confirmation without reaching the background Library action");
        Check(Node("capture-library.confirm.close") is null && Node("capture-library.confirm.scroll") is null, "Delete confirmation must have one Cancel action and keep footer outside scrolling content");
        Check(Node("capture-library.confirm.name")?.Text == "Example Game" && Node("capture-library.confirm.metadata")?.Text?.Contains("3840 × 2160") == true,
            "Confirmation identifies the capture being deleted");
        Check(Node("capture-library.delete.confirm")!.StyleClasses.Contains("capture-delete"), "Delete action uses the theme's danger styling");
        Check(ViewSnapshotValidator.Validate(View()).Count == 0 && WinUiPresentationContract.Validate(View()).Count == 0);
        Export("confirmation", View());
        await widget.OnActionAsync(new("capture-library.delete.cancel", "capture-library.delete.cancel"));
        Check(Node("capture-library.confirm") is null && deletes == 0 && Node("capture-library.preview") is not null, "Cancel retains capture and preview");
        await widget.OnActionAsync(new("capture-library.delete", "capture-library.delete"));
        await widget.OnActionAsync(new("capture-library.delete.confirm", "capture-library.delete.confirm"));
        await Until(() => deletes == 1 && Node("capture-library.delete.confirm")?.IsBusy == true);
        Check(Node("capture-library.delete.confirm")?.Text == "Working…");
        Export("deleting", View());
        await widget.OnActionAsync(new("capture-library.delete.confirm", "capture-library.delete.confirm"));
        Check(deletes == 1, "Busy confirmation must not delete twice");
        deleteGate.TrySetResult();
        await Until(() => Node("capture-library.empty") is not null); Check(deletes == 1);
        Check(View().Surface?.Appearance == WidgetSurfaceAppearance.DesktopAcrylic && View().Root.StyleClasses.Contains("capture-library-base"), "Returning to the gallery must restore acrylic");
        Check(View().Surface is { PreferredWidth: 1180, PreferredHeight: 700, WidthMode: WidgetSurfaceAxisMode.Preferred, HeightMode: WidgetSurfaceAxisMode.Preferred }, "Material changes must preserve the library, preview and settings extent");
        Check(View().Root.Transition is { Key: "home", Navigation: WidgetNavigationOperation.Back }, "Deleting returns to the library with Back navigation");
    }
    finally { saveGate.TrySetResult(); deleteGate.TrySetResult(); await WidgetTestHost.DestroyAsync(widget); }
    static void Export(string name, ViewSnapshot snapshot)
    {
        if (Environment.GetEnvironmentVariable("CAPTURE_DELETE_FIXTURES") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".json"), SnapshotJson.Serialize(snapshot));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixture/capture.wrss"), Path.Combine(directory, "capture.wrss"), true);
    }
}
static async Task RecordingDiscovery()
{
    var id = new string('D',32); var starts = 0; var requests = new List<CaptureSessionRequest>();
    var capabilities = new CaptureCapabilities(true, [CaptureImageFormat.Png],
        [new("1080p30-hardware",1920,1080,30,3_000_000,12_000_000,CaptureEncoderMode.Hardware)
        { Encoding=new(CaptureVideoCodec.H264,CaptureVideoPixelFormat.Yuv4208,CaptureVideoRateControl.ConstantBitrate),
          Adapter=new(new string('a',32),"Fixture GPU",new string('b',32)), EditingSupported=true }], [CaptureAudioMode.None], true,120);
    var client = new WidgetTestHostServicesBuilder().WithPrivateState(new())
        .WithCaptureQuery(() => new CaptureLibraryPage([],0,0,0))
        .WithResponse(WidgetPermissionCapabilities.Request, new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted))
        .WithHandler(WidgetCaptureCapabilities.Capabilities, (_, _) => throw new Exception("Recording surveyed the entire catalog"))
        .WithHandler(WidgetCaptureCapabilities.RecordingCapabilities, (_, _) => throw new Exception("Recording should validate its request without a separate profile survey"))
        .WithHandler(WidgetCaptureCapabilities.RecordClip, (request, _) => {
            requests.Add(request);
            Check(capabilities.Validate(request).Supported);
            starts++;
            return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); }).Build();
    var widget = WidgetTestHost.Attach(new CaptureLibraryWidget(), client);
    IEnumerable<ViewNode> Walk(ViewNode n) => new[] {n}.Concat(n.Children.SelectMany(Walk));
    async Task Ready() { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (Walk(widget.RenderSnapshot("recording-discovery",1).Root).Any(n=>n.IsBusy == true)) await Task.Delay(10,timeout.Token); }
    try
    {
        await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive); await Ready();
        for (var i=0;i<2;i++) { await widget.OnActionAsync(new("capture-library.video","capture-library.video")); await Ready(); }
        Check(starts==2 && requests.Count==2 && requests.All(request => request.Recording?.Selection == new CaptureRecordingSelection()));
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}
static async Task IndexedGallery()
{
    using var f=new Fixture();
    for(var i=0;i<240;i++)
    {
        var bytes=f.Bytes.ToArray(); BitConverter.GetBytes(i).CopyTo(bytes,8); await File.WriteAllBytesAsync(f.Source.Path,bytes);
        await f.Store.SaveAsync(f.Source,default);
    }
    await f.Store.ListAsync(0,default,refresh:true);
    var reads=f.Store.MetadataReads; var reconciliations=f.Store.Reconciliations;
    var watch=System.Diagnostics.Stopwatch.StartNew();
    for(var page=0;page<12;page++)
    {
        var result=await f.Store.ListAsync(page*20,default);
        Check(result.TotalCount==240 && result.Items.Count==20 && result.StoredBytes==240*f.Bytes.Length && CaptureLibraryLimits.Valid(result));
    }
    Check(f.Store.MetadataReads==reads && f.Store.Reconciliations==reconciliations,"Paging reread metadata files or enumerated the library");
    Console.WriteLine($"Indexed gallery: 12 pages / 240 captures in {watch.Elapsed.TotalMilliseconds:F1} ms; no directory reconciliation or metadata-file reads.");
    Check(!Directory.EnumerateFiles(f.Library,"*.png").Any() && Directory.EnumerateFiles(f.Exports,"*.png").Count()==240);
    using var restart=new CaptureLibraryStore(f.Library,f.Exports);
    Check((await restart.ListAsync(220,default)).Items.Count==20 && restart.MetadataReads==0,"Persistent index was discarded on restart");
}
static async Task ExternalChanges()
{
    using var f=new Fixture(); var inspections=0; var thumbnails=0;
    using var store=new CaptureLibraryStore(f.Library,f.Exports,null, async (_,path,token)=>
    {
        thumbnails++; var bytes=new byte[24]; f.Bytes.AsSpan(0,8).CopyTo(bytes);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16,4),100);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20,4),100);
        await File.WriteAllBytesAsync(path,bytes,token); return new(100,100);
    },(path,previous,_)=> { inspections++; return Task.FromResult(previous with {Width=200,ByteLength=new FileInfo(path).Length}); });
    var item=await store.SaveAsync(f.Source,default);
    await store.ThumbnailAsync(item.Id,default); await store.ThumbnailAsync(item.Id,default); Check(thumbnails==1);
    var path=f.MediaPath(item.Id); var renamed=Path.Combine(f.Exports,"Renamed capture.png"); File.Move(path,renamed);
    using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5)))
    {
        while((await store.ExportAsync(item.Id,timeout.Token)).FileName!="Renamed capture.png") await Task.Delay(20,timeout.Token);
    }
    var changed=f.Bytes.Concat(new byte[]{1}).ToArray(); await File.WriteAllBytesAsync(renamed,changed);
    await store.ThumbnailAsync(item.Id,default); Check(thumbnails==2 && inspections==1);
    await using(var media=await store.OpenAsync(item.Id,CaptureImageResolution.Original,default)) Check(media.Media.Width==200 && media.Stream.Length==changed.Length);
    File.Delete(renamed);
    var missing=await store.ListAsync(0,default,refresh:true); Check(!missing.Items.Single().Available && missing.StoredBytes==0);
    await store.DeleteAsync(item.Id,default); Check((await store.ListAsync(0,default)).TotalCount==0);
}
static async Task FolderHandoff()
{
    using var f=new Fixture();
    using var cancel=new CancellationTokenSource();
    var first=CaptureLibraryFolderRequests.OpenAsync(Fixture.Identity,f.Exports,cancel.Token);
    var old=CaptureLibraryFolderRequests.Pending!; cancel.Cancel();
    try { await first; throw new Exception("Folder cancellation ignored"); } catch(OperationCanceledException) { }
    Check(CaptureLibraryFolderRequests.Pending is null);
    var next=CaptureLibraryFolderRequests.OpenAsync(Fixture.Identity,f.Exports,default);
    CaptureLibraryFolderRequests.Complete(old.Id,true); Check(!next.IsCompleted);
    CaptureLibraryFolderRequests.Complete(CaptureLibraryFolderRequests.Pending!.Id,true);
    Check(await next && CaptureLibraryFolderRequests.Pending is null);
    PlatformCapabilities.TryGet(PlatformCapabilities.CaptureLibraryManageV1,out var capability);
    Check(capability.Operations.Contains("capture-library.open-folder") && !capability.AllowsBackgroundForOperation("capture-library.open-folder"));
}
static async Task IndexRecovery()
{
    using var f=new Fixture(); var item=await f.Store.SaveAsync(f.Source,default);
    var original=f.MediaPath(item.Id);
    f.Store.Dispose();
    await File.WriteAllTextAsync(Path.Combine(f.Library,"library.sqlite"),"damaged index");
    await File.WriteAllTextAsync(Path.Combine(f.Library,new string('E',32)+".deleted"),"interrupted deletion marker");
    using var rebuilt=new CaptureLibraryStore(f.Library,f.Exports);
    Check((await rebuilt.ListAsync(0,default)).Items.Single().Id==item.Id);
    Check(File.ReadAllBytes(original).SequenceEqual(f.Bytes));
    Check(Directory.EnumerateFiles(f.Library,"library.sqlite.corrupt-*").Count()==1);
}
static async Task SettingsFlow()
{
    string? lastFeedback = null;
    var savedState = new WidgetTestPrivateState(); var starts = 0;
    var capabilities = new CaptureCapabilities(true, [CaptureImageFormat.Png],
        [new("1080p30-hardware", 1920,1080,30,3_000_000,12_000_000,CaptureEncoderMode.Hardware)], [CaptureAudioMode.None, CaptureAudioMode.Application], true, 120);
    var client = new WidgetTestHostServicesBuilder()
        .WithHandler(WidgetNotificationCapabilities.Publish,(request,_)=>
        {lastFeedback=request.Notification?.Body;return ValueTask.FromResult(new WidgetNotificationReceipt(true));})
        .WithCaptureQuery(() => new CaptureLibraryPage([],0,0,0))
        .WithResponse(WidgetPermissionCapabilities.Request, new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted))
        .WithPrivateState(savedState)
        .WithHandler(WidgetCaptureCapabilities.Capabilities, (_, _) => ValueTask.FromResult(capabilities))
        .WithHandler(WidgetCaptureCapabilities.RecordingCapabilities, (_, _) => ValueTask.FromResult(capabilities))
        .WithHandler(WidgetCaptureCapabilities.RecordClip, (request, _) =>
        {
            Check(request.OutputPurpose == CaptureOutputPurpose.LibraryRecording && request.MaximumBytes == CaptureLimits.MaximumLibraryRecordingBytes);
            var validation = capabilities.Validate(request);
            if (!validation.Supported) throw new WidgetCapabilityException(validation.ErrorCode!, "Unavailable");
            starts++;
            return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true));
        }).Build();
    CaptureLibraryWidget widget = WidgetTestHost.Attach(new CaptureLibraryWidget(), client);
    IEnumerable<ViewNode> Walk(ViewNode n) => new[] { n }.Concat(n.Children.SelectMany(Walk));
    ViewSnapshot View() => widget.RenderSnapshot("settings", 1);
    ViewNode? Node(string id) => Walk(View().Root).FirstOrDefault(n => n.Id == id);
    async Task Until(Func<bool> ready) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (!ready()) await Task.Delay(10, timeout.Token); }
    async Task Action(string id) { await widget.OnActionAsync(new(id,id)); await Until(() => !Walk(View().Root).Any(n => n.IsBusy == true)); }
    async Task Start() { await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive); await Until(() => Node("capture-library.empty") is not null && !Walk(View().Root).Any(n => n.IsBusy == true)); }
    try
    {
        await Start(); await Action("capture-library.settings");
        Check(View().Surface?.Appearance == WidgetSurfaceAppearance.DesktopAcrylic && View().Root.StyleClasses.Contains("capture-library-base"), "Settings must expose a desktop acrylic base");
        Check(View().Surface is { PreferredWidth: 1180, PreferredHeight: 700, WidthMode: WidgetSurfaceAxisMode.Preferred, HeightMode: WidgetSurfaceAxisMode.Preferred }, "Material changes must preserve the library, preview and settings extent");
        Check(View().ProtocolVersion >= ProtocolConstants.DesktopAcrylicVersion, "Settings must declare desktop acrylic protocol support");
        await Until(()=>Node("capture-library.recording.manual.resolution") is not null);
        Check(Node("capture-library.recording.manual.resolution")!.SelectOptions.Single(option=>option.IsSelected).Label=="Auto (recommended)");
        Check(ViewSnapshotValidator.Validate(View()).Count == 0 && WinUiPresentationContract.Validate(View()).Count == 0, "Settings snapshot is invalid");
        Check(Node("capture-library.setting.audio")!.SelectOptions.Select(o => o.Id).SequenceEqual(new[] { "audio.None", "audio.Application" }),
            "Unsupported audio was advertised");
        Check(Node("capture-library.setting.audio")!.SelectOptions.Single(o => o.IsSelected).Id == "audio.None", "Audio was enabled by default");
        await Action("capture-library.option.audio.Application");
        await Action("capture-library.option.duration.120");
        Check(savedState.Revision == 2 && Node("capture-library.settings.estimate")?.Text?.Contains("2 GiB") == true,
            "Settings should retain the library recording limit");
        await WidgetTestHost.DestroyAsync(widget);
        capabilities = capabilities with { RecordingProfiles = [], AudioModes = [CaptureAudioMode.None] };
        widget = WidgetTestHost.Attach(new CaptureLibraryWidget(), client); await Start(); await Action("capture-library.settings");
        await Until(()=>Node("capture-library.recording.manual.resolution") is not null);
        Check(savedState.Revision == 2 && Node("capture-library.settings.no-encoder") is not null &&
            Node("capture-library.setting.audio")!.SelectOptions.Single(option=>option.IsSelected).IsDisabled,
            "Restart overwrote unsupported persisted settings");
        var json = savedState.Json!;
        Check(json.Contains("Selection") && json.Contains("120") && json.Contains("Application"), "Recording/audio settings did not persist");
        await Action("capture-library.settings.back"); await Action("capture-library.video");
        Check(starts == 0 && lastFeedback?.Contains("unavailable") == true,
            "Unavailable recording must be rejected before capture and explained through action feedback");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}
static async Task PermissionLink()
{
    var client = new WidgetTestHostServicesBuilder().WithCaptureContinuity().WithPrivateState(new()).WithHandler(WidgetCaptureLibraryCapabilities.Query,
        (_, _) => throw new WidgetCapabilityException("permission_denied", "Denied")).Build();
    var widget = WidgetTestHost.Attach(new CaptureLibraryWidget(), client);
    IEnumerable<ViewNode> Walk(ViewNode n) => new[] { n }.Concat(n.Children.SelectMany(Walk));
    try
    {
        await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ViewSnapshot view;
        while (true)
        {
            view = widget.RenderSnapshot("permissions",1);
            if (Walk(view.Root).Any(n => n.Id == "capture-library.permissions")) break;
            await Task.Delay(10, timeout.Token);
        }
        var intent = Walk(view.Root).Single(n => n.Id == "capture-library.permissions").Intent!;
        Check(intent.ContractId == WidgetIntentContracts.OpenWidgetPermissions && intent.Payload.GetProperty("packageId").GetString() == "widgetrail.firstparty.capture-library");
        Check(intent.Presentation == WidgetIntentPresentation.OpenWidget && ViewSnapshotValidator.Validate(view).Count == 0);
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}
static async Task PermissionContinuation()
{
    var requests = 0; var starts = 0; var polls = 0; var outcome = WidgetPermissionOutcome.Denied;
    var id = new string('a',32); var media = new CaptureAttachment(new string('b',32),"image/png",64,48,128,0,long.MaxValue);
    var client = new WidgetTestHostServicesBuilder().WithPrivateState(new())
        .WithCaptureQuery(() => new CaptureLibraryPage([],0,0,0))
        .WithHandler(WidgetPermissionCapabilities.Request, (request, _) => { requests++; Check(request.CapabilityIds.SequenceEqual([WidgetCaptureCapabilities.Permission, WidgetCaptureLibraryCapabilities.SavePermission]));
            return ValueTask.FromResult(new WidgetPermissionRequestState(outcome, outcome == WidgetPermissionOutcome.Pending ? id : null)); })
        .WithHandler(WidgetPermissionCapabilities.Status, (_, _) => { polls++; return ValueTask.FromResult(new WidgetPermissionRequestState(outcome,id)); })
        .WithResponse(WidgetCaptureCapabilities.Validate, new CaptureConfigurationValidation(true))
        .WithHandler(WidgetCaptureCapabilities.SaveScreenshot, (_, _) => { starts++; return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); })
        .WithResponse(WidgetCaptureCapabilities.Status, new WindowCaptureStatus(id,WindowCapturePhase.Ready,media)).Build();
    var widget = WidgetTestHost.Attach(new CaptureLibraryWidget(),client);
    IEnumerable<ViewNode> Walk(ViewNode n) => new[] { n }.Concat(n.Children.SelectMany(Walk));
    ViewNode? Node(string id) => Walk(widget.RenderSnapshot("permission-flow",1).Root).FirstOrDefault(n => n.Id == id);
    async Task Until(Func<bool> ready) { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (!ready()) await Task.Delay(10,deadline.Token); }
    try
    {
        await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
        await Until(() => Node("capture-library.empty") is not null && Node("capture-library.photo")?.IsBusy != true);
        Check(requests == 0, "Activation prompted for access");
        await widget.OnActionAsync(new("capture-library.photo","capture-library.photo"));
        await Until(() => requests == 1 && Node("capture-library.photo")?.IsBusy != true);
        Check(starts == 0 && Node("capture-library.permissions") is not null, "Denied capture ran anyway or hid recovery");
        outcome = WidgetPermissionOutcome.Pending;
        await widget.OnActionAsync(new("capture-library.photo","capture-library.photo"));
        await Until(() => polls > 0);
        Check(starts == 0 && Node("capture-library.photo")?.IsBusy == true, "Capture ran before consent");
        outcome = WidgetPermissionOutcome.Granted;
        await Until(() => starts == 1 && Node("capture-library.photo")?.IsBusy != true);
        Check(starts == 1 && requests == 2, "Approved action failed to continue exactly once");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}
sealed class Probe : Widget
{
    public override WidgetView Render() => new(UI.Text("test", "test"));
    internal Task<byte[]> Read(CaptureAttachment media) => HostServices.Capture.ReadContentAsync(media);
    internal ValueTask Release(CaptureAttachment media) => HostServices.Capture.DiscardAsync(media);
    internal ValueTask<CaptureLibraryThumbnail> Thumbnail(string id) => HostServices.CaptureLibrary.GetThumbnailAsync(id);
}
sealed class Fixture : IDisposable
{
    internal static BrokerWidgetIdentity Identity { get; } = new("test.library", "test.publisher", "library.instance");
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "wrail-library-test-" + Guid.NewGuid().ToString("N"));
    internal string Library => Path.Combine(Root, "library"); internal string Exports => Path.Combine(Root, "exports");
    internal CaptureLibraryStore Store { get; }
    internal byte[] Bytes { get; } = [137,80,78,71,13,10,26,10,0,0,0,0];
    internal HostCaptureAttachment Source { get; }
    internal Fixture()
    {
        Directory.CreateDirectory(Root); var path = Path.Combine(Root,"source.png"); File.WriteAllBytes(path, Bytes);
        Source = new(new(Guid.NewGuid().ToString("N"), "image/png", 100, 100, Bytes.Length, 0, DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeMilliseconds()),path);
        Store = new(Library, Exports);
    }
    internal string MediaPath(string id) => Directory.Exists(Exports) ? Directory.EnumerateFiles(Exports, "*"+id+".*").FirstOrDefault() ?? Path.Combine(Library,id+".png") : Path.Combine(Library,id+".png");
    public void Dispose() { Store.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}

static class CaptureQueryFixture
{
    public static WidgetTestHostServicesBuilder WithCaptureContinuity(this WidgetTestHostServicesBuilder builder) => builder
        .WithHandler(WidgetCaptureLibraryCapabilities.QueryContinuity, async (request, token) =>
        {
            var page = await builder.Build().CaptureLibrary.QueryAsync(request.Query, request.Offset, request.Count, request.Revision, token);
            // Small fixture snapshots can prove their whole ordering directly.
            // Production uses the broker's indexed sequence, not this test hash.
            var ordering = request.Query.FavoritesOnly || page.Offset != 0 || page.Items.Count != page.TotalCount ? page.Revision :
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                    page.Items.Select(item => item with { IsFavorite = false, MetadataRevision = 0 })))).ToLowerInvariant();
            return new CaptureLibraryContinuityPage(page, ordering);
        });

    public static WidgetTestHostServicesBuilder WithCaptureQuery(this WidgetTestHostServicesBuilder builder,Func<CaptureLibraryPage> read) => builder
        .WithCaptureContinuity()
        .WithHandler(WidgetCaptureLibraryCapabilities.GetMetadata,(request,_)=>ValueTask.FromResult(read().Items.Single(item=>item.Id==request.Id)))
        .WithResponse(WidgetCaptureLibraryCapabilities.Applications,new CaptureLibraryApplications([],false))
        .WithHandler(WidgetCaptureLibraryCapabilities.Folders,(request,_)=>ValueTask.FromResult(new CaptureLibraryFolders([new("", "WidgetRail captures", 0)],request.Revision ?? "a")))
        .WithHandler(WidgetCaptureLibraryCapabilities.Groups,(_,_)=>{var page=read();return ValueTask.FromResult(new CaptureLibraryGroups(page.TotalCount==0?[]:[new("unknown",page.TotalCount)],page.TotalCount,"a"));})
        .WithHandler(WidgetCaptureLibraryCapabilities.Query,(request,_)=>
        {
            var page=read();
            return ValueTask.FromResult(new CaptureLibraryQueryPage(page.Items.Skip(request.Offset).Take(request.Count).ToArray(),request.Offset,page.TotalCount,page.StoredBytes,"a"));
        })
        .WithHandler(WidgetCaptureLibraryCapabilities.Find,(request,_)=>
        {
            var page=read();var index=page.Items.ToList().FindIndex(item=>item.Id==request.Id);
            return ValueTask.FromResult(new CaptureLibraryPosition(index<0?null:index,page.TotalCount,"a"));
        });
}
