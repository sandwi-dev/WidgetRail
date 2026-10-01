using System.Text.Json;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

// The real package and real provider, through normal catalog/worker admission.
// This probe exercises read-only navigation/details. It never activates Play,
// Install, connection editing, favorites or any other game mutation.
if (args.Length is < 2 or > 3) throw new ArgumentException("Usage: WinUiPlayniteProbe <shell-options.json> <report.json> [--diagnose-refresh]");
var options = JsonSerializer.Deserialize<Options>(File.ReadAllText(args[0])) ?? throw new InvalidDataException("Missing options.");
const string widgetId = "widgetrail.samples.playnite-library";
if (options.InitialWidgetId != widgetId) throw new InvalidDataException("This probe admits only the Playnite sample.");
// The full-trust sample owns its own private state independently of the bridge
// profile. Use its existing development override for this automated probe.
Environment.SetEnvironmentVariable("WRAIL_PLAYNITE_LIBRARY_DATA_ROOT", options.SettingsRoot);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await using var bridge = await OwnedBridgeProcess.StartAsync(new(options.InstallationRoot, options.SettingsRoot, options.InstalledCatalogRoot), timeout.Token);
var session = bridge.Session;
var reports = new List<object>();
try
{
    while (!(await session.ListWidgetsAsync(timeout.Token)).IsComplete) await Task.Delay(50, timeout.Token);
    await session.EstablishPresentationAsync(session.GetTarget(widgetId), WidgetLifecycleState.Interactive, timeout.Token);
    if (args.Contains("--diagnose-refresh"))
    {
        await Task.Delay(5000, timeout.Token);
        var before = Frame();
        reports.Add(new { label = "Before explicit diagnostic refresh", sequence = before.Authority.SnapshotSequence,
            invalidation = session.GetState(widgetId)?.InvalidationRevision, page = Inventory(before.Snapshot.Root) });
        var after = await session.RefreshAsync(before.Authority, timeout.Token);
        reports.Add(new { label = "After explicit diagnostic refresh", sequence = after.Authority.SnapshotSequence,
            invalidation = session.GetState(widgetId)?.InvalidationRevision, page = Inventory(after.Snapshot.Root) });
    }
    await CollectionReady();
    await Inspect("Home", openDetails: true);
    var current = Frame();
    var library = Nodes(current.Snapshot.Root).First(node => node.ActionId == "playnite-library.browse.open");
    await session.SendActionAsync(current.Authority, new(library.ActionId!, library.Id, InputScopeId: current.Authority.ActiveInputScopeId), timeout.Token);
    await Until(() => Nodes(Frame().Snapshot.Root).Any(node => node.IndexedCollection is not null && node.ScrollAxis != ScrollAxis.Horizontal));
    await CollectionReady();
    await Inspect("Library", openDetails: true);
    Write(new { result = "passed", bridgePid = bridge.ProcessId, pages = reports });
    Console.WriteLine("Real Playnite Home/Library ranges, artwork and details passed. This is transport evidence, not rendering acceptance.");
}
catch (Exception error)
{
    Write(new { result = "failed", exception = error.GetType().Name, code = (error as WidgetPresentationSessionException)?.Code,
        failure = session.GetState(widgetId)?.Failure?.Code, pages = reports,
        currentPage = session.GetState(widgetId)?.LastGood is { } failedFrame ? Inventory(failedFrame.Snapshot.Root) : null });
    Console.Error.WriteLine("Playnite probe failed: " + error.GetType().Name + ". See the sanitized report.");
    Environment.ExitCode = 1;
}

WidgetPresentationFrame Frame() => session.GetState(widgetId)?.LastGood ?? throw new InvalidOperationException("No published frame.");
async Task CollectionReady()
{
    WidgetPresentationAuthority? inspected = null;
    while (true)
    {
        timeout.Token.ThrowIfCancellationRequested();
        if (session.GetState(widgetId)?.Failure is { } failed) throw new InvalidOperationException(failed.Code);
        var current = Frame();
        if (current.Authority != inspected && Nodes(current.Snapshot.Root).FirstOrDefault(node => node.IndexedCollection is { Count: > 0 }) is { } collection)
        {
            inspected = current.Authority;
            try
            {
                var rows = await session.ReadIndexedRangeAsync(current.Authority, collection.Id, collection.IndexedCollection!, 0, 1,
                    cancellationToken: timeout.Token);
                // Saved display rows intentionally have a count but no current
                // action authority. Wait for ordinary live publication, not Retry.
                if (rows.Items[0].Root.IsDisabled != true && rows.Items[0].Root.IsBusy != true) return;
            }
            catch (WidgetPresentationSessionException) when (Frame().Authority != current.Authority)
            {
                // Publication replaced this exact read; inspect its successor.
            }
        }
        await Task.Delay(30, timeout.Token);
    }
}
async Task Until(Func<bool> condition)
{
    var lastSequence = -1L;
    while (!condition())
    {
        timeout.Token.ThrowIfCancellationRequested();
        if (session.GetState(widgetId)?.Failure is { } failed) throw new InvalidOperationException(failed.Code);
        if (session.GetState(widgetId)?.LastGood is { } observed && observed.Authority.SnapshotSequence != lastSequence)
        {
            lastSequence = observed.Authority.SnapshotSequence;
            Write(new { result = "pending", pages = reports, sequence = lastSequence,
                invalidation = session.GetState(widgetId)?.InvalidationRevision, currentPage = Inventory(observed.Snapshot.Root) });
        }
        await Task.Delay(30, timeout.Token);
    }
}
async Task Inspect(string label, bool openDetails)
{
    var frame = Frame();
    var collection = Nodes(frame.Snapshot.Root).First(node => node.IndexedCollection is { Count: > 0 });
    await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, collection.Id, collection.IndexedCollection!, 0,
        Math.Min(collection.IndexedCollection!.Count, 3), cancellationToken: timeout.Token);
    var row = lease.Range.Items.First();
    var cover = Nodes(row.Root).FirstOrDefault(node => node.ArtworkHandle is not null);
    var artwork = cover is null ? null : await lease.ResolveArtworkAsync(row.Key, cover.ArtworkHandle!, timeout.Token);
    reports.Add(new { label, count = collection.IndexedCollection.Count, rows = lease.Range.Items.Count,
        artworkBytes = artwork?.Bytes.Length ?? 0,
        page = Inventory(frame.Snapshot.Root), row = Inventory(row.Root),
        styleProperties = lease.RenderStyles.Values.SelectMany(style => style.Base.Keys).Distinct().Order().ToArray(),
        styleUnits = lease.RenderStyles.Values.SelectMany(style => style.Base.Values).Select(value => value.Unit).Where(unit => unit is not null).Distinct().ToArray() });
    if (!openDetails || row.Root.ActionId != "playnite-library.details.open") throw new InvalidOperationException("Expected details-only row activation.");
    await lease.AdmitInputAsync(Frame().Authority, row.Key, ControllerButton.A, cancellationToken: timeout.Token);
    await Until(() => Frame().Snapshot.Root.Kind == ViewNodeKind.ModalLayer);
    reports.Add(new { label = label + " details", page = Inventory(Frame().Snapshot.Root) });
    var modal = Frame();
    await session.SendControllerInputAsync(modal.Authority, new(ControllerButton.B, ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget, modal.Snapshot.InitialFocusId, 1, Environment.TickCount64 * 1000,
        modal.Authority.ActiveInputScopeId, modal.Authority.SnapshotSequence), timeout.Token);
    await Until(() => Frame().Snapshot.Root.Kind != ViewNodeKind.ModalLayer);
}
object Inventory(ViewNode root) => new {
    kinds = Nodes(root).GroupBy(node => node.Kind.ToString()).ToDictionary(group => group.Key, group => group.Count()),
    conditional = Nodes(root).Count(node => node.VisibleWhen is not (null or ResponsiveVisibility.Always)),
    ids = Nodes(root).Select(node => node.Id).ToArray()
};
void Write(object report) => File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
static IEnumerable<ViewNode> Nodes(ViewNode node)
{
    yield return node;
    foreach (var child in node.Children) foreach (var descendant in Nodes(child)) yield return descendant;
}
internal sealed record Options(string InstallationRoot, string SettingsRoot, string InstalledCatalogRoot, string InitialWidgetId);
