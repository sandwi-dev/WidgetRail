using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetBridge;
using WidgetRail.FirstPartyWidgets.CaptureLibrary;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

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
    ("Saved originals survive store recreation; repeat saves deduplicate; exports are exact", Persistence),
    ("Corrupt data cannot be opened or exported", Corruption),
    ("Damaged metadata remains visible and deletable", DamagedMetadata),
    ("Storage limits preserve existing items and permit retry after deletion", Quotas),
    ("Orphaned copies are cleaned without evicting saved originals", Orphans),
    ("Linked storage and export directories are refused without touching their targets", LinkedPaths),
    ("Deleting a leased original is logical immediately and cleans disk after release", LeasedDelete),
    ("Locked small previews reserve their actual bytes rather than the 128 MiB ceiling", PendingByteBudget),
    ("Missing metadata never causes automatic deletion of a saved original", MissingMetadata),
    ("Controller page changes enter the first new row and expose bumper shortcuts", Paging),
    ("Cancelled saves leave no published item", Cancellation),
    ("Library imports are owned and require library authority rather than capture authority", Ownership),
    ("Broker separates saved-media reading from recording and destructive management", BrokerAuthority),
    ("Full-resolution request preserves its resolution through the capture queue", OriginalRequest),
    ("Windows codec makes bounded context copies without changing odd-sized 4K originals", Codec),
    ("Capture Library controller flow reviews before saving and confirms deletion", WidgetFlow),
    ("SDK generic capture reads route saved references through library permission", SdkRouting),
    ("Capture settings persist, retain unsupported quality, and reject oversized clips before capture", SettingsFlow),
    ("High-entropy thumbnails stream within broker bounds and honor library-read revocation", ThumbnailTransport),
    ("Missing library permission offers a scoped Settings intent without prompting", PermissionLink),
    ("Capture action waits for permission then continues without a widget reload", PermissionContinuation),
    ("Capture Library theme compiles with supported logical text alignment", Style),
};
var failures = 0;
foreach (var test in tests) try { await test.Run(); Console.WriteLine("PASS " + test.Name); } catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + error); }
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed"); return failures == 0 ? 0 : 1;

static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
static Task Style()
{
    var parsed=WidgetRail.WidgetStyling.WrssParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixture/capture.wrss")),"capture.wrss");
    Check(parsed.IsValid);
    var compiled=WidgetRail.WidgetStyling.WrssThemeCompiler.Compile([parsed.Document]);
    Check(compiled.IsValid,string.Join("; ",compiled.Diagnostics.Select(d=>d.Message)));
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
    Check(File.Exists(Path.Combine(f.Exports, exported.FileName)), "Deletion removed an export");
}
static async Task Corruption()
{
    using var f = new Fixture(); var saved = await f.Store.SaveAsync(f.Source, default);
    File.WriteAllBytes(Path.Combine(f.Library, saved.Id + ".png"), [0, 1]);
    await Reject(async () => { await using var read = await f.Store.OpenAsync(saved.Id, CaptureImageResolution.Original, default); });
    await Reject(async () => { await f.Store.ExportAsync(saved.Id, default); });
    await f.Store.DeleteAsync(saved.Id, default); Check((await f.Store.ListAsync(0, default)).TotalCount == 0);
}
static async Task DamagedMetadata()
{
    using var f = new Fixture(); var item = await f.Store.SaveAsync(f.Source, default);
    File.WriteAllText(Path.Combine(f.Library, item.Id + ".json"), "not json");
    var page = await f.Store.ListAsync(0, default); Check(page.Items.Single().Available == false && CaptureLibraryLimits.Valid(page));
    await f.Store.DeleteAsync(item.Id, default); Check((await f.Store.ListAsync(0, default)).TotalCount == 0);
}
static async Task Quotas()
{
    using var f = new Fixture(); var store = new CaptureLibraryStore(f.Library, f.Exports, null, 1, 20);
    var first = await store.SaveAsync(f.Source, default);
    Check((await store.SaveAsync(f.Source, default)).Id == first.Id);
    var other = f.Bytes.ToArray(); other[^1] = 1; File.WriteAllBytes(f.Source.Path, other);
    await Reject(async () => { await store.SaveAsync(f.Source, default); });
    Check((await store.ListAsync(0, default)).TotalCount == 1);
    await store.DeleteAsync(first.Id, default);
    var tooSmall = new CaptureLibraryStore(f.Library, f.Exports, null, 10, 10);
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
    var page = await f.Store.ListAsync(0, default);
    Check(page.TotalCount == 1 && page.StoredBytes == f.Bytes.Length && File.Exists(Path.Combine(f.Library, saved.Id + ".png")));
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
    var saved = await f.Store.SaveAsync(f.Source, default);
    await Junction(f.Exports, outside);
    try { await Reject(async () => { await f.Store.ExportAsync(saved.Id, default); }); Check(Directory.GetFiles(outside).Length == 1); }
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
    Check(!File.Exists(Path.Combine(f.Library, saved.Id + ".png")) && !File.Exists(Path.Combine(f.Library, saved.Id + ".deleted")));
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
    var page = await f.Store.ListAsync(0, default);
    Check(page.TotalCount == 1 && !page.Items.Single().Available && File.Exists(Path.Combine(f.Library, saved.Id + ".png")), "Missing metadata evicted a saved original");
    await f.Store.DeleteAsync(saved.Id, default);
    Check((await f.Store.ListAsync(0, default)).TotalCount == 0 && !File.Exists(Path.Combine(f.Library, saved.Id + ".png")));
}
static async Task Paging()
{
    var media = new CaptureAttachment(new string('A', 32), "image/png", 100, 100, 1, 0, long.MaxValue);
    var items = Enumerable.Range(1, 21).Select(i => new CaptureLibraryItem(i.ToString("x32"), media, i)).ToArray();
    var client = new WidgetTestHostServicesBuilder().WithPrivateState(new()).WithHandler(WidgetCaptureLibraryCapabilities.List,
        (request, _) => ValueTask.FromResult(new CaptureLibraryPage(items.Skip(request.Offset).Take(20).ToArray(), request.Offset, items.Length, 21))).Build();
    var widget = WidgetTestHost.Attach(new CaptureLibraryWidget(), client);
    ViewSnapshot View() => widget.RenderSnapshot("library-pages", 1);
    IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    async Task Until(Func<bool> ready) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (!ready()) await Task.Delay(10, timeout.Token); }
    try
    {
        await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        await Until(() => View().Root.Shortcuts.Any(s => s.Button == ControllerButton.RightBumper));
        Check(Walk(View().Root).Count(n => n.ArtworkHandle is not null) == 20, "Saved images were not lazy artwork tiles");
        await widget.OnActionAsync(new("capture-library.next", "capture-library.next"));
        await Until(() => View().FocusGroupEntryRequest is not null);
        Check(View().FocusGroupEntryRequest!.GroupId == "capture-library.entries");
        Check(Walk(View().Root).Any(n => n.Id == "capture-library.page-number" && n.Text == "Page 2 of 2"));
        Check(ViewSnapshotValidator.Validate(View()).Count == 0 && WinUiPresentationContract.Validate(View()).Count == 0);
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
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
    var id = new string('B', 32); var source = new CaptureAttachment(new string('C',32), "image/png", 3840, 2160, 1000, 0, long.MaxValue);
    var page = new CaptureLibraryPage([], 0, 0, 0); var saves = 0; var deletes = 0;
    var saveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var client = new WidgetTestHostServicesBuilder()
        .WithPrivateState(new())
        .WithResponse(WidgetPermissionCapabilities.Request, new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted))
        .WithResponse(WidgetCaptureCapabilities.Validate, new CaptureConfigurationValidation(true))
        .WithHandler(WidgetCaptureLibraryCapabilities.List, (_, _) => ValueTask.FromResult(page))
        .WithHandler(WidgetCaptureCapabilities.Start, (request, _) => {
            Check(request.Screenshot is { MaximumWidth: null, MaximumHeight: null } && request.Recording is null, "Library did not request an original screenshot");
            return ValueTask.FromResult(new WindowCaptureTicket(id)); })
        .WithResponse(WidgetCaptureCapabilities.Status, new WindowCaptureStatus(id, WindowCapturePhase.Ready, source))
        .WithResponse(WidgetCaptureCapabilities.Discard, new WidgetCapabilityAcknowledgement(true))
        .WithHandler(WidgetCaptureLibraryCapabilities.Save, async (_, token) => { saves++; await saveGate.Task.WaitAsync(token); var item = new CaptureLibraryItem(id, source, 1); page = new([item],0,1,1000); return item; })
        .WithHandler(WidgetCaptureLibraryCapabilities.Delete, (_, _) => { deletes++; page = new([],0,0,0); return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); }).Build();
    var widget = WidgetTestHost.Attach(new CaptureLibraryWidget(), client);
    ViewSnapshot View() => widget.RenderSnapshot("library-test", 1);
    IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    ViewNode? Node(string name) => Walk(View().Root).FirstOrDefault(n => n.Id == name);
    async Task Until(Func<bool> ready) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (!ready()) await Task.Delay(10, timeout.Token); }
    try
    {
        await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
        await Until(() => Node("capture-library.empty") is not null && Node("capture-library.photo")?.IsBusy != true);
        await widget.OnActionAsync(new("capture-library.photo", "capture-library.photo")); await Until(() => Node("capture-library.save") is not null && Node("capture-library.save")?.IsBusy != true);
        Check(saves == 0, "Capture saved without review");
        Check(View().Root.Transition is { Kind: WidgetTransitionKind.Page, Key: "preview", Navigation: WidgetNavigationOperation.Forward }, "Review must be forward page navigation");
        Check(ViewSnapshotValidator.Validate(View()).Count == 0 && WinUiPresentationContract.Validate(View()).Count == 0);
        await widget.OnActionAsync(new("capture-library.save", "capture-library.save")); await Until(() => saves == 1);
        Check(Node("capture-library.save")?.IsBusy == true && Node("capture-library.save")?.IsDisabled != true);
        saveGate.TrySetResult(); await Until(() => Node("capture-library.delete") is not null && Node("capture-library.delete")?.IsBusy != true);
        Check(View().Root.Transition?.Key == "preview", "Saving must not navigate away from the current preview");
        await widget.OnActionAsync(new("capture-library.delete", "capture-library.delete"));
        Check(View().InitialFocusId == "capture-library.delete.cancel" && deletes == 0);
        await widget.OnActionAsync(new("capture-library.delete.confirm", "capture-library.delete.confirm"));
        await Until(() => Node("capture-library.empty") is not null); Check(deletes == 1);
        Check(View().Root.Transition is { Key: "home", Navigation: WidgetNavigationOperation.Back }, "Deleting returns to the library with Back navigation");
    }
    finally { saveGate.TrySetResult(); await WidgetTestHost.DestroyAsync(widget); }
}
static async Task SettingsFlow()
{
    var savedState = new WidgetTestPrivateState(); var starts = 0;
    var capabilities = new CaptureCapabilities(true, [CaptureImageFormat.Png],
        [new("1080p30-hardware", 1920,1080,30,3_000_000,12_000_000,CaptureEncoderMode.Hardware)], [CaptureAudioMode.None, CaptureAudioMode.Application], true, 120);
    var client = new WidgetTestHostServicesBuilder()
        .WithResponse(WidgetCaptureLibraryCapabilities.List, new CaptureLibraryPage([],0,0,0))
        .WithResponse(WidgetPermissionCapabilities.Request, new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted))
        .WithPrivateState(savedState)
        .WithHandler(WidgetCaptureCapabilities.Capabilities, (_, _) => ValueTask.FromResult(capabilities))
        .WithHandler(WidgetCaptureCapabilities.Validate, (request, _) => ValueTask.FromResult(capabilities.Validate(request)))
        .WithHandler(WidgetCaptureCapabilities.Start, (_, _) => { starts++; throw new Exception("Invalid settings reached capture start"); }).Build();
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
        Check(Node("capture-library.setting.profile") is not null);
        Check(ViewSnapshotValidator.Validate(View()).Count == 0 && WinUiPresentationContract.Validate(View()).Count == 0, "Settings snapshot is invalid");
        Check(Node("capture-library.setting.audio")!.SelectOptions.Select(o => o.Id).SequenceEqual(new[] { "audio.None", "audio.Application" }),
            "Unsupported audio was advertised");
        Check(Node("capture-library.setting.audio")!.SelectOptions.Single(o => o.IsSelected).Id == "audio.None", "Audio was enabled by default");
        await Action("capture-library.option.audio.Application");
        await Action("capture-library.option.duration.120"); await Action("capture-library.option.bitrate.12000000");
        Check(savedState.Revision == 3 && Node("capture-library.settings.validation")?.Text?.Contains("size limit") == true);
        await Action("capture-library.settings.back"); await Action("capture-library.video");
        Check(starts == 0 && Node("capture-library.status")?.Text?.Contains("size limit") == true);
        await WidgetTestHost.DestroyAsync(widget);
        capabilities = capabilities with { RecordingProfiles = [], AudioModes = [CaptureAudioMode.None] };
        widget = WidgetTestHost.Attach(new CaptureLibraryWidget(), client); await Start(); await Action("capture-library.settings");
        Check(savedState.Revision == 3 && Node("capture-library.settings.validation")?.Text?.Contains("saved quality has not been changed") == true,
            "Restart overwrote unsupported persisted settings");
        var json = savedState.Json!;
        Check(json.Contains("12000000") && json.Contains("120") && json.Contains("Application"), "Recording/audio settings did not persist");
    }
    finally { await WidgetTestHost.DestroyAsync(widget); }
}
static async Task PermissionLink()
{
    var client = new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureLibraryCapabilities.List,
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
        .WithResponse(WidgetCaptureLibraryCapabilities.List, new CaptureLibraryPage([],0,0,0))
        .WithHandler(WidgetPermissionCapabilities.Request, (request, _) => { requests++; Check(request.CapabilityIds.SequenceEqual([WidgetCaptureCapabilities.Permission]));
            return ValueTask.FromResult(new WidgetPermissionRequestState(outcome, outcome == WidgetPermissionOutcome.Pending ? id : null)); })
        .WithHandler(WidgetPermissionCapabilities.Status, (_, _) => { polls++; return ValueTask.FromResult(new WidgetPermissionRequestState(outcome,id)); })
        .WithResponse(WidgetCaptureCapabilities.Validate, new CaptureConfigurationValidation(true))
        .WithHandler(WidgetCaptureCapabilities.Start, (_, _) => { starts++; return ValueTask.FromResult(new WindowCaptureTicket(id)); })
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
        await Until(() => Node("capture-library.save") is not null && Node("capture-library.save")?.IsBusy != true);
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
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
