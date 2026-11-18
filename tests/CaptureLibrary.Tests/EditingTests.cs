using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class EditingTests
{
    private static readonly CaptureEditRecipe Crop = new(new(0, 0, 0, 50, 60));
    private static readonly BrokerWidgetIdentity Actor = new("test.editor", "test.editor.publisher", "editor.instance");
    private static CaptureLibraryStore Store(Fixture f) => new(f.Library, f.Exports, null, null,
        (path, previous, _) => Task.FromResult(previous with { Width = 50, Height = 60, ByteLength = new FileInfo(path).Length, Properties = null }));
    private static CaptureLibraryMutationReference Reference(CaptureLibraryItem item) => new(item.Id, item.Revision, item.MetadataRevision);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Reject(string code, Func<Task> action)
    {
        try { await action(); } catch (BrokerException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected " + code);
    }
    private static Task Write(CaptureEditInput input, CaptureEditRecipe recipe, Stream output, IProgress<double>? progress, CancellationToken token) =>
        output.WriteAsync(new byte[] { 137,80,78,71,13,10,26,10,1,2,3,4,5 }, token).AsTask();
    private static void Clean(Fixture f)
    {
        Check(!Directory.EnumerateFiles(f.Exports, ".widgetrail-edit-*").Any(), "Edit left temporary output");
        var staging = Path.Combine(f.Exports, ".widgetrail-edits");
        Check(!Directory.Exists(staging) || !Directory.EnumerateFiles(staging).Any(), "Edit left encoded staging media");
        Check(!Directory.Exists(Path.Combine(f.Library, "edits")) || !Directory.EnumerateFiles(Path.Combine(f.Library, "edits"), "*.json").Any(path => CaptureLimits.ValidToken(Path.GetFileNameWithoutExtension(path))), "Edit left an unneeded journal");
    }
    internal static async Task NewCopyAndContext()
    {
        using var f = new Fixture(); using var store = Store(f);
        var context = new CaptureSaveContext("source.event", [new(CaptureContextReferenceRole.Event, "event", "unlock", "Achievement")], 1000);
        var original = (await f.Store.SaveWithContextAsync(f.Source with { CapturedAtUnixMilliseconds = 2000 }, Fixture.Identity, context, default)).Item!;
        var path = f.MediaPath(original.Id); var bytes = File.ReadAllBytes(path); var attributes = File.GetAttributes(path);
        var result = await store.SaveEditedCopyAsync(Reference(original), Crop, Actor, () => true, async (input, recipe, output, progress, token) =>
        {
            Check(input.Path == path && input.Stream is FileStream, "Encoder did not retain original input");
            try { using var ignored = new FileStream(path, FileMode.Open, FileAccess.Write); throw new InvalidOperationException("Original was writable during encode"); } catch (IOException) { }
            await Write(input, recipe, output, progress, token);
        }, null, default);
        Check(result.Id != original.Id && result.SourceId is null && result.Origin?.PackageId == Actor.PackageId && result.CapturedAtUnixMilliseconds == 2000,
            "Edited result lost new ownership or original capture time");
        Check(result.Media is { Width: 50, Height: 60, Origin: CaptureAttachmentOrigin.SavedLibrary, Properties: null }, "Edited dimensions/metadata incorrect");
        var association = (await store.GetContextsAsync(new(result.Id), default)).Items.Single();
        Check(association.Origin.PackageId == Fixture.Identity.PackageId && association.EventAtUnixMilliseconds == 1000 && association.CapturedAtUnixMilliseconds == 2000,
            "Derivation restamped provider provenance");
        Check((await f.Store.FindSavedContextAsync(Fixture.Identity, context, default))!.Item!.Id == original.Id, "Derived context stole original idempotency receipt");
        Check(File.ReadAllBytes(path).SequenceEqual(bytes) && File.GetAttributes(path) == attributes, "Editing changed original bytes/attributes");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(f.Library, result.Id + ".json")));
        Check(metadata.RootElement.GetProperty("Derivation").GetProperty("SourceId").GetString() == original.Id, "Private derivation was not persisted");
        Clean(f);
    }
    internal static async Task ConcurrentAndStale()
    {
        using var f = new Fixture(); using var store = Store(f); using var other = Store(f);
        var original = await f.Store.SaveAsync(f.Source, default); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var edit = store.SaveEditedCopyAsync(Reference(original), Crop, Actor, () => true, async (input, recipe, output, progress, token) =>
        { entered.SetResult(); await release.Task.WaitAsync(token); await Write(input, recipe, output, progress, token); }, null, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check((await store.QueryAsync(new(new()), default).WaitAsync(TimeSpan.FromSeconds(1))).TotalCount == 1, "Encoder held library gate");
        await Reject("capture_library_edit_busy", () => other.SaveEditedCopyAsync(Reference(original), Crop, Actor, () => true, Write, null, default));
        await Reject("capture_library_edit_busy", () => other.RecoverEditsAsync(default));
        await store.UpdateMetadataAsync(new(original.Id, original.MetadataRevision, "Changed while encoding", false), default);
        release.SetResult(); await Reject("capture_library_metadata_conflict", async () => await edit);
        Check((await store.QueryAsync(new(new()), default)).TotalCount == 1, "Stale edit published output"); Clean(f);
    }
    internal static async Task CancelRevokeDispose()
    {
        foreach (var mode in new[] { "cancel", "revoke", "dispose" })
        {
            using var f = new Fixture(); using var store = Store(f); using var cancel = new CancellationTokenSource();
            var original = await f.Store.SaveAsync(f.Source, default); var entered = new TaskCompletionSource(); var authorized = true;
            var edit = store.SaveEditedCopyAsync(Reference(original), Crop, Actor, () => authorized, async (input, recipe, output, progress, token) =>
            { await Write(input, recipe, output, progress, token); entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }, null, cancel.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (mode == "cancel") cancel.Cancel(); else if (mode == "revoke") authorized = false; else store.Dispose();
            try { await edit.WaitAsync(TimeSpan.FromSeconds(3)); throw new InvalidOperationException("Retired edit completed"); }
            catch (OperationCanceledException) when (mode != "revoke") { }
            catch (BrokerException error) when (mode == "revoke" && error.Code == "permission_denied") { }
            Check((await f.Store.QueryAsync(new(new()), default)).TotalCount == 1, "Aborted edit published output");
            using (new FileStream(f.MediaPath(original.Id), FileMode.Open, FileAccess.ReadWrite)) { }
            Clean(f);
        }
    }
    internal static async Task BudgetsAndRecovery()
    {
        using var f = new Fixture(); using var store = Store(f);
        var original = await f.Store.SaveAsync(f.Source, default);
        await Reject("capture_library_edit_invalid_recipe", () => store.SaveEditedCopyAsync(Reference(original), new(new(0, 99, 0, 50, 60)), Actor, () => true, Write, null, default));
        await Reject("capture_library_edit_output_limit", () => store.SaveEditedCopyAsync(Reference(original), Crop, Actor, () => true,
            (input, recipe, output, progress, token) => { output.SetLength((long)CaptureLimits.MaximumAttachmentBytes + 1); return Task.CompletedTask; }, null, default));
        var id = Guid.NewGuid().ToString("N"); var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var directory = Path.Combine(f.Library, "edits"); Directory.CreateDirectory(directory);
        var staging = Path.Combine(f.Exports, ".widgetrail-edits"); Directory.CreateDirectory(staging);
        var temporary = Path.Combine(staging, id + ".png"); await File.WriteAllBytesAsync(temporary, [1]);
        var abandoned = Path.Combine(f.Exports, $"WidgetRail-{DateTimeOffset.FromUnixTimeMilliseconds(time):yyyyMMdd-HHmmss}-{id}.png"); await File.WriteAllBytesAsync(abandoned, [2]);
        await File.WriteAllTextAsync(Path.Combine(directory, id + ".json"), JsonSerializer.Serialize(new { Id = id, ContentType = "image/png", SavedAt = time }));
        await store.RecoverEditsAsync(default);
        Check(!File.Exists(temporary) && !File.Exists(abandoned) && File.Exists(f.MediaPath(original.Id)), "Recovery touched originals or retained unpublished edit"); Clean(f);
    }
}
