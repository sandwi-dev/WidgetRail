using System.Text.Json;
using WidgetRail.FirstPartyWidgets.Marketplace;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

if (args is ["--export-native", var output]) { await ExportNative(output); return 0; }
var tests = new (string Name,Func<Task> Run)[] {
    ("Catalogue bumpers cycle sections locally and stay out of details and reviews", SectionShortcuts),
    ("Catalogue L3 returns to the search field without editing or losing context", SearchShortcut),
    ("Catalogue local search retains filters and rejects stale suggestions", Search),
    ("Catalogue previews are deferred read-only metadata and retire on navigation", Previews),
    ("Catalog navigation filters updates and restores the selected card", Navigation),
    ("Catalogue screenshot actions preserve image identity and return to the exact card", () => ScreenshotNavigation(null)),
    ("Catalogue setup requirements are visible before download and navigation stays outside scrolling", SetupPresentation),
    ("Catalogue empty loading stale and prerequisite states keep navigation usable", () => ExportEdgeStates(null)),
    ("Reopening checks releases without changing preview identity or the update shortcut", RefreshOnOpening),
    ("Preview sources ignore transport and operation changes but follow changed listing metadata", PreviewSnapshotStability),
    ("Downloads retain focus and installation requires the exact open full-trust review", Review),
    ("Retained older release availability is labelled in browse and details", RetainedAvailability),
    ("Retained newer versions remain available through normal update actions", RetainedUpdate),
    ("Individual updates are reviewed and installed without leaving the Updates page", UpdateFromList),
    ("Update all uses one explicit bounded review and preserves per-item results", BatchReview),
    ("Large catalogues keep update actions within the existing sixteen-package batch", LargeInventoryUpdates),
    ("Marketplace intent resets the page through the ordinary receiver contract", Intent),
    ("Marketplace package and theme declare only narrow intents and no install permission", Package),
};
var failures=0;
foreach(var test in tests) try { await test.Run(); Console.WriteLine("PASS "+test.Name); } catch(Exception error) { failures++;Console.WriteLine("FAIL "+test.Name+": "+error); }
Console.WriteLine($"{tests.Length-failures}/{tests.Length} passed");return failures==0?0:1;
static void Check(bool value,string message) { if(!value)throw new Exception(message); }
static IEnumerable<ViewNode> Walk(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Walk));
static ViewNode? Node(Fixture f,string id)=>Walk(f.View().Root).FirstOrDefault(n=>n.Id==id);
static async Task Until(Func<bool> ready) { using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(6));while(!ready())await Task.Delay(10,deadline.Token); }
static async Task ExportNative(string directory)
{
    Directory.CreateDirectory(directory);
    await using var fixture = new Fixture();
    fixture.Snapshot = fixture.Snapshot with { Items = [
        new(Fixture.Id, "Playnite Library", "Browse and launch your Playnite games from the couch.", [], "2.0.0", "1.0.0", 13421772,
            SetupRequirements: ["Requires Playnite with the separate Playnite Bridge extension.", "Connect using the bridge address and token in widget settings."]),
        new("example.second", "PoE2 Path of Building (Beta)", "Plan builds, manage skills and equipment, and compare trade upgrades using Path of Building.", [], "1.0.0", IsPrerelease: true),
        new("example.spotify", "Spotify", "Browse your library and control music while you play.", [], "1.0.0",
            SetupRequirements: ["Requires your own Spotify developer app and Client ID.", "Spotify Premium is required for playback."]),
        new("example.help", "Game Help", "Get game hints using screenshots and clips.", [], "1.0.0",
            SetupRequirements: ["Requires your own Google Gemini API key; provider usage charges may apply."]),
        new("example.display", "Display Profiles", "Save and restore monitor layouts, resolutions and refresh rates.", [], "1.0.0"),
        new("example.music", "YouTube Music", "Browse music and keep playback controls pinned while playing.", [], "1.0.0") ] };
    await fixture.Start();
    await Write("browse");
    await Until(() => Node(fixture, "marketplace.refresh")?.IsBusy != true);
    using (var previewHost = WidgetTestHost.CreateIndexedCollectionHost(fixture.Widget, "catalogue-visual-preview"))
    {
        var card = Walk(previewHost.CurrentSnapshot.Root).First(node => node.Kind == ViewNodeKind.ActionSurface);
        using var lease = await previewHost.AcquirePreviewAsync("marketplace.previews", card.FocusPreviewKey!);
        var fragment = lease.Range.Items.Single().Root;
        ViewNode Materialize(ViewNode node) => node with {
            FocusPreviewKey = null, FocusPreviewSource = null,
            FocusPresentation = node.Id == card.Id ? fragment : node.FocusPresentation,
            Children = node.Children.Select(Materialize).ToArray(),
        };
        var previewSnapshot = previewHost.CurrentSnapshot with { Root = Materialize(previewHost.CurrentSnapshot.Root), InitialFocusId = card.Id };
        await File.WriteAllBytesAsync(Path.Combine(directory, "browse-preview.snapshot.json"), SnapshotJson.Serialize(previewSnapshot));
    }
    await fixture.Act(Walk(fixture.View().Root).First(n => n.Kind == ViewNodeKind.ActionSurface).ActionId!);
    await Write("details");
    await fixture.Act("marketplace.back");
    await fixture.Act("marketplace.tab.installed"); await Write("installed");
    fixture.Snapshot = fixture.Snapshot with { Revision = 2, Items = Enumerable.Range(0,16).Select(index =>
        new MarketplaceItem("example.batch." + index, "Synthetic widget " + index, "Controller-friendly synthetic add-on", [], "2.0.0", "1.0.0")).ToArray() };
    await fixture.Act("marketplace.refresh");
    await Until(() => Walk(fixture.View().Root).Count(node => node.Kind == ViewNodeKind.ActionSurface) == 16);
    await fixture.Act("marketplace.tab.updates"); await Write("updates");
    await fixture.Act("marketplace.update-all");
    await Until(() => fixture.Commands.Any(command => command.Action == "download-updates"));
    var permissions = Enumerable.Range(0, 8).Select(index => "example.optional.permission." + index).ToArray();
    fixture.Snapshot = fixture.Snapshot with { Revision = 3,
        Operation = new(new string('a',32), "download-updates", "review", null),
        UpdateBatch = new(fixture.Snapshot.Items.Select((item,index) => new MarketplaceUpdateItem(item.Id,item.Name,"2.0.0","review",
            new(new string('b',64),item.Id,item.Name,"2.0.0","1.0.0",index == 0,true,"Compatible",permissions,permissions,permissions,new string('c',64)))).ToArray(),new string('d',64)) };
    await Until(() => Node(fixture,"marketplace.batch.review.open") is not null);
    await fixture.Act("marketplace.batch.review"); await Write("review");
    await fixture.Act("marketplace.batch.install");
    await Until(() => Node(fixture,"marketplace.batch.review.cancel") is null && fixture.Commands.Any(command => command.Action == "install-updates"));
    await Write("completed");
    await ExportEdgeStates(directory);
    await ScreenshotNavigation(directory);
    async Task Write(string name) => await File.WriteAllBytesAsync(Path.Combine(directory, name + ".snapshot.json"), SnapshotJson.Serialize(fixture.View()));
}

// Real widget actions produce these snapshots. The native validation consumer
// substitutes only these exact HTTPS identities with local PNGs before rendering;
// no bridge or production image transport is replaced by this fixture.
static async Task ScreenshotNavigation(string? directory)
{
    const string first="https://raw.githubusercontent.com/sandwi-dev/WidgetRail/native-fixture/screenshot-1.png";
    const string second="https://raw.githubusercontent.com/sandwi-dev/WidgetRail/native-fixture/screenshot-2.png";
    const string package="widgetrail.firstparty.performance-monitor";
    await using var f=new Fixture();
    f.Snapshot=f.Snapshot with {Items=[f.Snapshot.Items[0],new(package,"Performance Monitor","Local screenshot navigation fixture.",[first,second],"1.0.0")]};
    Check(MarketplaceValidation.SnapshotError(f.Snapshot)is null,"Screenshot fixture bypasses catalogue image URL admission");
    await f.Start();await Until(()=>Node(f,"marketplace.refresh") is {IsBusy:not true});
    var card=Walk(f.View().Root).Where(n=>n.Kind==ViewNodeKind.ActionSurface).Last();
    await Write("screenshots-browse");await f.Act(card.ActionId!);
    CheckImage(first,"1 / 2","Performance Monitor, screenshot 1");await Write("screenshots-first");
    Check(f.View().Root.Shortcuts.Any(s=>s.Button==ControllerButton.RightBumper&&s.ActionId=="marketplace.screenshot.next")&&
        f.View().Root.Shortcuts.Any(s=>s.Button==ControllerButton.LeftBumper&&s.ActionId=="marketplace.screenshot.previous"),"Screenshot bumper shortcuts are missing");
    await f.Act("marketplace.screenshot.next");CheckImage(second,"2 / 2","Performance Monitor, screenshot 2");await Write("screenshots-next");
    await f.Act("marketplace.screenshot.previous");CheckImage(first,"1 / 2","Performance Monitor, screenshot 1");await Write("screenshots-previous");
    Check(f.View().Root.Shortcuts.Any(s=>s.Button==ControllerButton.B&&s.ActionId=="marketplace.back"),"Screenshot details lost the declared B Back shortcut");
    await f.Act("marketplace.back");
    Check(f.View().InitialFocusId==card.Id&&Node(f,"marketplace.screenshot")is null,"Screenshot Back lost the exact non-first catalogue card");
    await Write("screenshots-back");
    Check(f.Commands.All(c=>c.Action is "status" or "refresh"),"Screenshot browsing downloaded or changed a package");
    void CheckImage(string source,string count,string label)
    {
        var image=Node(f,"marketplace.screenshot")!;
        Check(image.ImageSource==source&&image.AccessibilityLabel==label&&Node(f,"marketplace.screenshot.count")?.Text==count,
            "Screenshot identity, accessible label and count diverged");
    }
    async Task Write(string name)
    {
        var snapshot=f.View();
        Check(ViewSnapshotValidator.Validate(snapshot).Count==0&&WinUiPresentationContract.Validate(snapshot).Count==0,"Screenshot workflow snapshot invalid");
        if(directory is not null)await File.WriteAllBytesAsync(Path.Combine(directory,name+".snapshot.json"),SnapshotJson.Serialize(snapshot));
    }
}

static async Task ExportEdgeStates(string? directory)
{
    await using (var empty = new Fixture())
    {
        empty.Snapshot = empty.Snapshot with { Items = [] };
        await empty.Start();
        await Until(() => Node(empty, "marketplace.refresh") is { IsBusy: not true });
        await empty.Act("marketplace.tab.installed");
        Check(Node(empty, "marketplace.empty")?.Text == "No catalogue widgets installed yet. Find one in Browse.", "Empty installed state lacks useful recovery copy");
        Check(empty.View().InitialFocusId == "marketplace.tab.installed", "Empty state lost its navigation target");
        await Write("empty", empty);
    }
    await using (var loading = new Fixture())
    {
        loading.Snapshot = loading.Snapshot with { Items = [], CheckedAtUtc = null };
        loading.RefreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await loading.Start();
        await Until(() => Node(loading, "marketplace.refresh")?.IsBusy == true);
        Check(Node(loading, "marketplace.refresh")?.IsDisabled != true, "Loading disabled the focused refresh control");
        Check(Node(loading, "marketplace.empty")?.Text == "Loading the catalogue…", "Initial loading state is missing");
        await Write("loading", loading);
        await loading.Act("marketplace.tab.installed");
        Check(loading.View().InitialFocusId == "marketplace.tab.installed", "Loading prevented section navigation");
        loading.RefreshGate.SetResult();
        await Until(() => Node(loading, "marketplace.refresh") is { IsBusy: not true });
    }
    await using (var stale = new Fixture())
    {
        stale.Snapshot = stale.Snapshot with { Operation = new(new string('e', 32), "refresh", "failed", null,
            Message: "Could not refresh releases. Saved availability may be out of date. Try again later.") };
        await stale.Start();
        await Until(() => Node(stale, "marketplace.refresh") is { IsBusy: not true });
        Check(Walk(stale.View().Root).Count(n => n.Kind == ViewNodeKind.ActionSurface) == 2, "Refresh failure discarded cached cards");
        Check(Node(stale, "marketplace.status")?.StyleClasses.Contains("diagnostic-error") == true, "Failed refresh lacks error styling");
        Check(Node(stale, "marketplace.checked") is not null && Node(stale, "marketplace.refresh")?.IsDisabled != true, "Stale content lost last-check context or retry action");
        await Write("stale-error", stale);
    }
    await using (var details = new Fixture())
    {
        var requirement = "Requires a separate desktop application, an active service account and a developer application configured for this widget. Complete the service's setup guide before downloading, then enter the connection information in the widget's own Settings.";
        details.Snapshot = details.Snapshot with { Items = [new(Fixture.Id,
            "Example integration with detailed setup requirements", "A synthetic listing for prerequisite layout validation.", [], "1.0.0",
            SetupRequirements: [requirement, requirement, requirement, requirement])] };
        await details.Start();
        await Until(() => Node(details, "marketplace.refresh") is { IsBusy: not true });
        await details.Act(Walk(details.View().Root).Single(n => n.Kind == ViewNodeKind.ActionSurface).ActionId!);
        Check(Walk(details.View().Root).Count(n => n.Id?.StartsWith("marketplace.requirement.", StringComparison.Ordinal) == true) == 4, "Long prerequisites were dropped");
        await Write("requirements-long", details);
        await details.Act("marketplace.back");
        details.Snapshot = details.Snapshot with { Revision = 2, Items = details.Snapshot.Items.Select(i => i with { SetupRequirements = null }).ToArray() };
        await details.Act("marketplace.refresh");
        await Until(() => !Walk(details.View().Root).Any(n => n.Text?.Contains("Requires a separate desktop") == true) && Node(details, "marketplace.refresh") is { IsBusy: not true });
        await details.Act(Walk(details.View().Root).Single(n => n.Kind == ViewNodeKind.ActionSurface).ActionId!);
        Check(Node(details, "marketplace.requirements") is null, "Absent prerequisites left an empty panel");
        await Write("requirements-absent", details);
    }
    async Task Write(string name, Fixture fixture)
    {
        var snapshot = fixture.View();
        Check(ViewSnapshotValidator.Validate(snapshot).Count == 0 && WinUiPresentationContract.Validate(snapshot).Count == 0, name + " is not a valid native presentation");
        if (directory is not null) await File.WriteAllBytesAsync(Path.Combine(directory, name + ".snapshot.json"), SnapshotJson.Serialize(snapshot));
    }
}

static async Task SetupPresentation()
{
    await using var f = new Fixture();
    f.Snapshot = f.Snapshot with { Items = f.Snapshot.Items.Select(i => i with {
        SetupRequirements = ["Requires a separate service account.", "Configure the service token inside the widget."], IsPrerelease = true }).ToArray() };
    await f.Start();
    var view = f.View();
    Check(view.Root.Kind == ViewNodeKind.Grid, "Catalogue lacks a bounded fixed header");
    Check(!Walk(Node(f, "marketplace.content")!).Any(n => n.Id == "marketplace.tabs" || n.Id == "marketplace.refresh"), "Navigation scrolls away");
    Check(Walk(view.Root).Any(n => n.Text == "Setup required"), "Discovery omitted setup indication");
    Check(Walk(view.Root).Any(n => n.Text == "Preview"), "Preview state omitted");
    await f.Act(Walk(view.Root).First(n => n.Kind == ViewNodeKind.ActionSurface).ActionId!);
    Check(Node(f, "marketplace.requirement.1")?.Text?.Contains("service token") == true, "Details omitted complete setup requirements");
    Check(f.Commands.All(c => c.Action is "status" or "refresh"), "Reading prerequisites downloads or installs a widget");
    Check(ViewSnapshotValidator.Validate(f.View()).Count == 0 && WinUiPresentationContract.Validate(f.View()).Count == 0, "Setup details invalid");
}

static async Task Search()
{
    await using var f = new Fixture();
    f.Snapshot = f.Snapshot with { Items = f.Snapshot.Items.Select(item => item.Id == Fixture.Id
        ? item with { SetupRequirements = ["Needs a service token"] } : item).ToArray() };
    await f.Start();
    await Until(() => Node(f, "marketplace.refresh")?.IsBusy != true);
    Check(Node(f, "marketplace.search.row")!.Children.Any(n => n.Id == "marketplace.refresh"), "Check for updates is not beside search");
    var before = f.Commands.Count;
    TextSuggestionQuery Query(string text) => new("marketplace.search", Node(f, "marketplace.search")!.TextEntrySuggestions!.Key, new string('a', 32), 1, text, MaximumValueLength: 128);
    var query = Query("sec");
    var suggestions = await f.Widget.OnSuggestTextAsync(query, default);
    Check(suggestions.Items.Single().Value == "Second widget", "Suggestions do not use loaded catalogue names");
    await f.Act("marketplace.search", "  SERVICE token  ");
    var card = Walk(f.View().Root).Single(n => n.Kind == ViewNodeKind.ActionSurface);
    Check(card.AccessibilityLabel!.StartsWith("First widget"), "Search does not match setup requirements case-insensitively");
    Check((await f.Widget.OnSuggestTextAsync(query, default)).Unavailable, "Old search generation remained available");
    await f.Act(card.ActionId!);
    Check((await f.Widget.OnSuggestTextAsync(query with { Revision = 2 }, default)).Unavailable, "Details allowed a stale search response");
    await f.Act("marketplace.back");
    Check(Node(f, "marketplace.search")!.Text == "SERVICE token" && f.View().InitialFocusId == card.Id, "Back lost search or exact selection");
    await f.Act("marketplace.search", "no such widget");
    Check(Node(f, "marketplace.empty")!.Text!.Contains("No widgets match") && !Walk(f.View().Root).Any(n => n.Kind == ViewNodeKind.ActionSurface), "No-match state is misleading");
    await f.Act("marketplace.search.clear");
    Check(Walk(f.View().Root).Count(n => n.Kind == ViewNodeKind.ActionSurface) == 2, "Clear did not restore catalogue");
    Check(Node(f, "marketplace.search.clear")?.IsDisabled != true, "Clearing removed its own focus eligibility");
    var old = Query("sec");
    await f.Act("marketplace.tab.installed");
    Check((await f.Widget.OnSuggestTextAsync(old, default)).Unavailable, "Section transition accepted old suggestions");
    Check((await f.Widget.OnSuggestTextAsync(Query("sec"), default)).Items.Count == 0, "Installed suggestions include uninstalled widgets");
    Check(f.Commands.Count == before, "Search or suggestions issued a marketplace request");
    var beforeRefresh = Query("first");
    await f.Act(card.ActionId!);
    f.Snapshot = f.Snapshot with { Revision = f.Snapshot.Revision + 1, Items = [f.Snapshot.Items[1]] };
    await f.Act("marketplace.refresh");
    await Until(() => Node(f, "marketplace.refresh")?.IsBusy != true && Node(f, "marketplace.search") is not null);
    Check((await f.Widget.OnSuggestTextAsync(beforeRefresh, default)).Unavailable, "Refresh accepted old suggestions");
    await f.Act("marketplace.tab.browse");
    Check(Walk(f.View().Root).Single(n => n.Kind == ViewNodeKind.ActionSurface).AccessibilityLabel!.StartsWith("Second widget"),
        "Removing the selected listing left the catalogue empty");
    var active = Query("second");
    await WidgetTestHost.SetLifecycleStateAsync(f.Widget, WidgetLifecycleState.Background);
    Check((await f.Widget.OnSuggestTextAsync(active, default)).Unavailable, "Inactive widget still supplies suggestions");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await f.Widget.OnSuggestTextAsync(active, cancelled.Token); throw new Exception("Cancellation was ignored"); }
    catch (OperationCanceledException) { }
}

static async Task SectionShortcuts()
{
    await using var f = new Fixture { RefreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    await f.Start();
    var before = f.Commands.Count;
    await f.Act("marketplace.search", "First");
    foreach (var section in new[] { "installed", "updates", "browse" })
        await Move(ControllerButton.RightBumper, section);
    foreach (var section in new[] { "updates", "installed", "browse" })
        await Move(ControllerButton.LeftBumper, section);
    Check(f.Commands.Count == before && !f.RefreshGate.Task.IsCompleted, "Section changes issued requests or waited for refresh");
    var card = Walk(f.View().Root).Single(n => n.Kind == ViewNodeKind.ActionSurface);
    await f.Act(card.ActionId!);
    Check(!f.View().Root.Shortcuts.Any(s => s.Button is ControllerButton.LeftBumper or ControllerButton.RightBumper),
        "Section bumpers escaped into item details without screenshots");
    await f.Act("marketplace.tab.updates");
    Check(Node(f, "marketplace.description") is not null, "A stale section shortcut escaped item details");
    await f.Act("marketplace.back");
    Check(Node(f, "marketplace.tab.browse")?.IsSelected == true && f.View().InitialFocusId == card.Id,
        "Back lost the originating section or card");
    f.RefreshGate.SetResult();
    async Task Move(ControllerButton button, string section)
    {
        var shortcut = f.View().Root.Shortcuts.Single(s => s.Button == button);
        Check(shortcut.ActionId == "marketplace.tab." + section, "Bumper chose the wrong adjacent section");
        await f.Act(shortcut.ActionId);
        Check(Node(f, "marketplace.tab." + section)?.IsSelected == true && Node(f, "marketplace.search")?.TextEntryValue == "First",
            "Bumper lost the selected tab or search query");
        Check(Node(f, "marketplace.section.previous.hint") is not null && Node(f, "marketplace.section.next.hint") is not null,
            "Section bumper hints missing");
        Check(ViewSnapshotValidator.Validate(f.View()).Count == 0 && WinUiPresentationContract.Validate(f.View()).Count == 0,
            "Section navigation produced an invalid view");
    }
}

static async Task SearchShortcut()
{
    await using var f = new Fixture(); await f.Start();
    await Until(() => Node(f, "marketplace.refresh")?.IsBusy != true);
    var before = f.Commands.Count;
    await f.Act("marketplace.search", "First");
    var card = Walk(f.View().Root).Single(n => n.Kind == ViewNodeKind.ActionSurface);
    await f.Act(card.ActionId!);
    var shortcut = f.View().Root.Shortcuts.Single(s => s.Button == ControllerButton.LeftStick);
    Check(shortcut.ActionId == "marketplace.search.focus", "L3 does not return to Search from details");
    await f.Act(shortcut.ActionId);
    var view = f.View();
    Check(Node(f, "marketplace.search")?.TextEntryValue == "First" && Node(f, "marketplace.description") is null, "L3 lost the query or retained details");
    var request = view.FocusGroupEntryRequest!;
    var target = Node(f, request.GroupId)!;
    Check(target.InitialChildFocusId == "marketplace.search" && target.Children.Single().Id == "marketplace.search", "Search target can restore to Clear or Refresh");
    await f.Act(shortcut.ActionId);
    Check(f.View().FocusGroupEntryRequest!.RequestId > request.RequestId, "Repeated L3 did not issue a fresh focus request");
    await f.Act("marketplace.search", "Second");
    Check(f.View().FocusGroupEntryRequest is null, "Old focus request survived a normal action");
    await f.Act("marketplace.tab.updates"); await f.Act(shortcut.ActionId);
    Check(Node(f, "marketplace.tab.updates")?.IsSelected == true && Node(f, "marketplace.search")?.TextEntryValue == "Second",
        "L3 changed tabs or cleared an empty search");
    Check(f.Commands.Count == before, "L3 issued a catalogue request");
}

static async Task Previews()
{
    await using var f = new Fixture();
    f.Snapshot = f.Snapshot with { Items = f.Snapshot.Items.Select(item => item with {
        SetupRequirements = ["Separate service account required", "Configure your token inside the widget"],
        RetainedAvailability = true, IsPrerelease = true }).ToArray() };
    await f.Start(); await Until(() => Node(f, "marketplace.refresh")?.IsBusy != true);
    using var host = WidgetTestHost.CreateIndexedCollectionHost(f.Widget, "catalogue-preview-test");
    var surface = Walk(host.CurrentSnapshot.Root).Single(n => n.Id == "marketplace.previews");
    var card = Walk(host.CurrentSnapshot.Root).First(n => n.Kind == ViewNodeKind.ActionSurface);
    Check(surface.FocusPreview is { EnterButton: null, SizeToContentHeight: true } && surface.FocusPreviewSource is not null,
        "Preview is not deferred and read-only");
    Check(!Walk(host.CurrentSnapshot.Root).Any(n => n.Text?.Contains("Configure your token") == true), "Previews were eagerly constructed");
    var before = f.Commands.Count;
    using var preview = await host.AcquirePreviewAsync(surface.Id, card.FocusPreviewKey!);
    var content = preview.Range.Items.Single().Root;
    FocusPreviewContentContract.Validate(content, false);
    Check(Walk(content).Any(n => n.Text?.Contains("Configure your token") == true) &&
        Walk(content).Any(n => n.Text?.Contains("Previously checked release") == true), "Preview dropped setup or release provenance");
    Check(!Walk(content).Any(n => n.ActionId is not null || n.Kind == ViewNodeKind.Image), "Preview has an action or network artwork");
    await f.Act("marketplace.search", "Second"); host.PublishSnapshot();
    Check(Walk(host.CurrentSnapshot.Root).Single(n => n.Id == surface.Id).FocusPreviewSource != surface.FocusPreviewSource, "Search retained old preview generation");
    await f.Act("marketplace.select." + card.FocusPreviewKey);
    host.PublishSnapshot();
    Check(!Walk(host.CurrentSnapshot.Root).Any(n => n.Id == surface.Id), "Details retained browse preview surface");
    Check(f.Commands.Count == before, "Preview issued a marketplace request");
    Check(f.View().Surface is { PreferredHeight: 640, HeightMode: WidgetSurfaceAxisMode.Preferred }, "Catalogue still fills all available height");
}

static async Task Navigation()
{
    await using var f=new Fixture();await f.Start();
    Check(Node(f,"marketplace.title")?.Text == "WidgetRail Catalogue", "Catalogue heading missing");
    Check(f.Commands.All(c=>c.Action is "status" or "refresh") && f.Commands.Any(c=>c.Action == "refresh"),"Opening did not check releases or unexpectedly mutated packages");
    Check(f.Commands.Where(c => c.Action == "refresh").All(c => !c.ForceRefresh), "Activation bypassed host freshness");
    await f.Act("marketplace.refresh");
    await Until(() => f.Commands.Any(c => c.Action == "refresh" && c.ForceRefresh));
    Check(Walk(f.View().Root).Count(n=>n.Kind==ViewNodeKind.ActionSurface)==2,"Catalog cards missing");
    await f.Act("marketplace.tab.installed");
    Check(Walk(f.View().Root).Count(n=>n.Kind==ViewNodeKind.ActionSurface)==1,"Installed view included an uninstalled widget");
    await f.Act("marketplace.tab.updates");
    Check(Walk(f.View().Root).Count(n=>n.ActionId?.StartsWith("marketplace.update.", StringComparison.Ordinal)==true)==1,"Updates filter included an uninstalled widget");
    await f.Act("marketplace.tab.installed");
    var card=Walk(f.View().Root).Single(n=>n.Kind==ViewNodeKind.ActionSurface);
    await f.Act(card.ActionId!);Check(Node(f,"marketplace.description") is not null,"Details did not open");
    Check(Node(f,"marketplace.manage")?.Intent?.ContractId==WidgetIntentContracts.OpenWidgetSettings,"Installed management does not route to Settings");
    await f.Act("marketplace.back");Check(f.View().InitialFocusId==card.Id,"Back did not restore selected card");
    Check(ViewSnapshotValidator.Validate(f.View()).Count==0 && WinUiPresentationContract.Validate(f.View()).Count==0,"Catalog snapshot invalid");
}
static async Task Review()
{
    await using var f=new Fixture();await f.Start();
    var card=Walk(f.View().Root).First(n=>n.Kind==ViewNodeKind.ActionSurface);
    await f.Act(card.ActionId!);await f.Act("marketplace.download");
    await Until(()=>Node(f,"marketplace.primary")?.IsBusy==true);
    Check(Node(f,"marketplace.primary")?.IsDisabled!=true,"Busy download removed focus");
    var review=new MarketplaceReview(new string('b',64),Fixture.Id,"First widget","2.0.0","1.0.0",true,true,"Compatible",
        ["system.audio.output.read.v1"],[],["system.audio.output.read.v1"],new string('c',64));
    f.Snapshot=f.Snapshot with { Revision=3, Operation=new(new string('a',32),"download","review",Fixture.Id,Review:review) };
    await Until(()=>Node(f,"marketplace.primary")?.Text=="Review installation");
    await f.Act("marketplace.install");Check(!f.Commands.Any(c=>c.Action=="install"),"Hidden review was approved");
    await f.Act("marketplace.review.open");Check(f.View().InitialFocusId=="marketplace.review.cancel","Review does not start at Cancel");
    await f.Act("marketplace.search.focus");
    Check(f.View().InitialFocusId == "marketplace.review.cancel" && f.View().FocusGroupEntryRequest is null, "L3 escaped installation review");
    await f.Act("marketplace.tab.updates");
    Check(f.View().InitialFocusId == "marketplace.review.cancel", "A stale bumper action escaped installation review");
    Check(ViewSnapshotValidator.Validate(f.View()).Count==0,"Review snapshot invalid");
    await f.Act("marketplace.review.cancel");Check(!f.Commands.Any(c=>c.Action=="install"),"Cancelling review installed");
    await f.Act("marketplace.review.open");await f.Act("marketplace.install");
    await Until(()=>f.Commands.Any(c=>c.Action=="install"));
    var install=f.Commands.Single(c=>c.Action=="install");
    Check(install.ReviewToken==review.Token && install.OperationId==new string('a',32) && install.ApproveFullTrust,"Exact review or full-access acknowledgement lost");
    await Until(()=>Node(f,"marketplace.primary")?.Intent is not null);
    Check(Node(f,"marketplace.primary")!.Intent!.Payload.GetProperty("packageId").GetString()==Fixture.Id,"Completion did not offer exact widget management");
}
static async Task RefreshOnOpening()
{
    await using var f = new Fixture { RefreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
    await f.Start();
    Check(!f.RefreshGate.Task.IsCompleted && Node(f, "marketplace.refresh")?.IsBusy == true, "Fixture did not hold the refresh request");
    await f.Act("marketplace.tab.installed");
    Check(Walk(f.View().Root).Count(n => n.Kind == ViewNodeKind.ActionSurface) == 1, "Refresh hid usable installed content");
    var card = Walk(f.View().Root).Single(n => n.Kind == ViewNodeKind.ActionSurface);
    await f.Act(card.ActionId!);
    Check(Node(f, "marketplace.description") is not null, "Refresh blocked navigation to existing details");
    await f.Act("marketplace.back");
    Check(f.View().InitialFocusId == card.Id, "Refresh lost the remembered card on Back");
    f.RefreshGate.SetResult();
    await Until(() => Node(f, "marketplace.refresh")?.IsBusy != true);
    var source = Node(f, "marketplace.previews")!.FocusPreviewSource;
    var suggestions = Node(f, "marketplace.search")!.TextEntrySuggestions;
    var checkedText = Node(f, "marketplace.checked")!.Text;
    await WidgetTestHost.SetLifecycleStateAsync(f.Widget, WidgetLifecycleState.Background);
    f.Commands.Clear();
    f.StatusGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    f.RefreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Snapshot = f.Snapshot with { Revision = f.Snapshot.Revision + 1, CheckedAtUtc = f.Snapshot.CheckedAtUtc!.Value.AddMinutes(1) };
    await WidgetTestHost.SetLifecycleStateAsync(f.Widget, WidgetLifecycleState.Interactive);
    await Until(() => f.Commands.Any(c => c.Action == "status"));
    Check(Node(f, "marketplace.refresh")?.IsBusy == true && f.View().Root.Shortcuts.Any(s => s.Button == ControllerButton.Y),
        "Resume removed the Y guide entry instead of keeping the busy button");
    await f.Act("marketplace.refresh");
    Check(!f.Commands.Any(c => c.Action == "refresh"), "Busy shortcut started a competing refresh");
    f.StatusGate.SetResult();
    await Until(() => Node(f, "marketplace.checked")!.Text != checkedText);
    await Until(() => f.Commands.Any(c => c.Action == "refresh"));
    Check(f.Commands.Where(c => c.Action == "refresh").All(c => !c.ForceRefresh), "Reopening bypassed host freshness");
    Check(Node(f, "marketplace.previews")!.FocusPreviewSource == source && Node(f, "marketplace.search")!.TextEntrySuggestions == suggestions,
        "An unchanged resumed catalogue retired its preview or search source");
    Check(f.View().InitialFocusId == card.Id && Node(f, "marketplace.tab.installed")?.IsSelected == true,
        "Resume lost the selected card or section");
    f.RefreshGate.SetResult();
    await Until(() => Node(f, "marketplace.refresh")?.IsBusy != true);
    Check(f.View().Root.Shortcuts.Any(s => s.Button == ControllerButton.Y) && Node(f, "marketplace.previews")!.FocusPreviewSource == source,
        "Completing the automatic check changed the guide or preview source");
    await f.Act("marketplace.refresh");
    await Until(() => f.Commands.Any(c => c.Action == "refresh" && c.ForceRefresh));
}

static async Task PreviewSnapshotStability()
{
    await using var f = new Fixture(); await f.Start();
    await Until(() => Node(f, "marketplace.refresh")?.IsBusy != true);
    using var host = WidgetTestHost.CreateIndexedCollectionHost(f.Widget, "catalogue-stable-preview");
    var surface = Walk(host.CurrentSnapshot.Root).Single(n => n.Id == "marketplace.previews");
    var card = Walk(host.CurrentSnapshot.Root).First(n => n.Kind == ViewNodeKind.ActionSurface);
    using var preview = await host.AcquirePreviewAsync(surface.Id, card.FocusPreviewKey!);
    for (var index = 0; index < 3; ++index)
    {
        f.Snapshot = f.Snapshot with { Revision = f.Snapshot.Revision + 1,
            Operation = new(new string('a', 32), "refresh", "completed", null, Message: "Check completed " + index) };
        await f.Act("marketplace.status");
        await Until(() => Node(f, "marketplace.status")?.Text == "Check completed " + index && Node(f, "marketplace.refresh")?.IsBusy != true);
        host.PublishSnapshot();
        Check(Node(f, surface.Id)!.FocusPreviewSource == surface.FocusPreviewSource,
            "Status-only changes replaced the preview source");
    }
    f.Snapshot = f.Snapshot with { Revision = f.Snapshot.Revision + 1, Items = f.Snapshot.Items.Select(item =>
        item.Id == Fixture.Id ? item with { Description = "Updated listing", SetupRequirements = ["New prerequisite"] } : item).ToArray() };
    await f.Act("marketplace.status");
    await Until(() => Node(f, surface.Id)!.FocusPreviewSource != surface.FocusPreviewSource);
    host.PublishSnapshot();
    using var changed = await host.AcquirePreviewAsync(surface.Id, card.FocusPreviewKey!);
    Check(Walk(changed.Range.Items.Single().Root).Any(node => node.Text?.Contains("New prerequisite") == true),
        "Real listing changes left the old preview content cached");
}
static async Task LargeInventoryUpdates()
{
    await using var f = new Fixture();
    f.Snapshot = f.Snapshot with { Items = Enumerable.Range(0, 21).Select(index =>
        new MarketplaceItem("example.optional." + index, "Optional widget " + index, "Description", [], "2.0.0", "1.0.0")).ToArray() };
    await f.Start();
    await f.Act("marketplace.tab.updates");
    Check(Node(f, "marketplace.update-all")?.Text == "Update next 16", "Large batch promise exceeds the bounded action");
    Check(ViewSnapshotValidator.Validate(f.View()).Count == 0, "Expanded inventory presentation is invalid");
    await f.Act("marketplace.update-all");
    await Until(() => f.Commands.Any(command => command.Action == "download-updates"));
    var command = f.Commands.Single(command => command.Action == "download-updates");
    Check(command.PackageIds?.Count == 16 && MarketplaceValidation.CommandError(command) is null,
        "Large catalogue emitted an invalid oversized update request");
}

static async Task RetainedAvailability()
{
    await using var f = new Fixture();
    f.Snapshot = f.Snapshot with { Items = f.Snapshot.Items.Select(item => item with { RetainedAvailability = true }).ToArray() };
    await f.Start();
    Check(Walk(f.View().Root).Any(node => node.Text == "Cached release"), "Browse silently presented retained availability as fresh");
    var card = Walk(f.View().Root).First(node => node.Kind == ViewNodeKind.ActionSurface);
    await f.Act(card.ActionId!);
    Check(Node(f,"marketplace.version")?.Text?.Contains("Previously checked release") == true,
        "Details lost retained availability provenance");
}

static async Task RetainedUpdate()
{
    await using var f = new Fixture();
    f.Snapshot = f.Snapshot with { Items = f.Snapshot.Items.Select(item => item.Id == Fixture.Id ? item with { AvailableVersionSaved = true } : item).ToArray() };
    await f.Start();
    await f.Act("marketplace.tab.updates");
    var update = Walk(f.View().Root).Single(node => node.ActionId?.StartsWith("marketplace.update.", StringComparison.Ordinal) == true);
    await f.Act(update.ActionId!);
    await Until(() => f.Commands.Any(command => command.Action == "download-updates"));
    Check(ViewSnapshotValidator.Validate(f.View()).Count == 0, "Retained update presentation is invalid");
}

static async Task UpdateFromList()
{
    await using var f = new Fixture(); await f.Start();
    await f.Act("marketplace.tab.updates");
    var update = Walk(f.View().Root).Single(node => node.ActionId?.StartsWith("marketplace.update.", StringComparison.Ordinal) == true);
    await f.Act(update.ActionId!);
    await Until(() => f.Commands.Any(command => command.Action == "download-updates"));
    Check(Node(f, "marketplace.description") is null, "Update opened a details page");
    var review = new MarketplaceReview(new string('b',64), Fixture.Id, "First widget", "2.0.0", "1.0.0", false, true, "Compatible", [], [], [], new string('c',64));
    f.Snapshot = f.Snapshot with { Revision = 3, Operation = new(new string('a',32), "download-updates", "review", null),
        UpdateBatch = new([new(Fixture.Id, "First widget", "2.0.0", "review", review)], new string('d',64)) };
    await Until(() => Node(f, update.Id)?.Text == "Review update");
    await f.Act("marketplace.batch.install");
    Check(!f.Commands.Any(command => command.Action == "install-updates"), "Update bypassed its explicit review");
    await f.Act("marketplace.batch.review");
    Check(f.View().InitialFocusId == "marketplace.batch.review.cancel", "Update review does not start on Cancel");
    await f.Act("marketplace.batch.install");
    await Until(() => f.Commands.Any(command => command.Action == "install-updates"));
    Check(f.Commands.Single(command => command.Action == "install-updates").ReviewToken == new string('d',64), "Update did not use the exact reviewed token");
    Check(Node(f, "marketplace.description") is null && Node(f, "marketplace.tab.updates")?.IsSelected == true,
        "Installing from Updates left the page");
    Check(ViewSnapshotValidator.Validate(f.View()).Count == 0, "Updates result snapshot is invalid");
}
static async Task BatchReview()
{
    await using var f = new Fixture();
    f.Snapshot = f.Snapshot with { Items = Enumerable.Range(0,16).Select(index =>
        new MarketplaceItem("example.batch."+index, "Widget "+index, "Synthetic widget", [], "2.0.0", "1.0.0")).ToArray() };
    await f.Start(); await f.Act("marketplace.tab.updates"); await f.Act("marketplace.update-all");
    await Until(() => f.Commands.Any(command => command.Action == "download-updates"));
    Check(f.Commands.Single(command => command.Action == "download-updates").PackageIds!.Count == 16, "Update all did not freeze the visible updates");
    var permissions = Enumerable.Range(0,64).Select(index => "example.optional.permission."+index).ToArray();
    var batch = new MarketplaceUpdateBatch(f.Snapshot.Items.Select((item,index) => new MarketplaceUpdateItem(item.Id,item.Name,"2.0.0","review",
        new(new string('b',64),item.Id,item.Name,"2.0.0","1.0.0",index==0,true,"Compatible",permissions,permissions,permissions,new string('c',64)))).ToArray(),new string('d',64));
    f.Snapshot = f.Snapshot with { Revision = 3, UpdateBatch = batch, Operation = new(new string('a',32),"download-updates","review",null) };
    await Until(() => Node(f,"marketplace.batch.review.open") is not null);
    await f.Act("marketplace.batch.install");
    Check(!f.Commands.Any(command => command.Action == "install-updates"), "Hidden aggregate review was approved");
    await f.Act("marketplace.batch.review");
    Check(f.View().InitialFocusId == "marketplace.batch.review.cancel", "Batch review did not focus Cancel");
    var errors = ViewSnapshotValidator.Validate(f.View());
    Check(errors.Count == 0, "Maximum-sized review exceeded snapshot limits: " + string.Join("; ",errors));
    Check(Walk(f.View().Root).Any(node => node.Text?.Contains("Full access") == true), "Aggregate review omitted full access");
    await f.Act("marketplace.review.cancel");
    Check(!f.Commands.Any(command => command.Action == "install-updates"), "Cancelling the review approved packages");
    await f.Act("marketplace.batch.review"); await f.Act("marketplace.batch.install");
    await Until(() => f.Commands.Any(command => command.Action == "install-updates"));
    Check(f.Commands.Single(command => command.Action == "install-updates") is { ApproveFullTrust:true, ReviewToken:not null }, "Aggregate full-access decision was not sent");
    await Until(() => Walk(f.View().Root).Any(node => node.Text?.Contains("Updated") == true));
    Check(Node(f,"marketplace.tab.updates")?.IsSelected == true && Node(f,"marketplace.description") is null, "Results left the update list");
}
static async Task Intent()
{
    await using var f=new Fixture();await f.Start();
    await f.Act(Walk(f.View().Root).First(n=>n.Kind==ViewNodeKind.ActionSurface).ActionId!);
    var request=WidgetIntentRequest.Create(WidgetIntentContracts.Marketplace,JsonSerializer.SerializeToElement(new{}));
    Check(await f.Widget.OnIntentAsync(request)==WidgetIntentResult.Accepted && Node(f,"marketplace.description") is null,"Intent retained stale details page");
    Check(await f.Widget.OnIntentAsync(request with { SchemaDigest=new string('0',64) })==WidgetIntentResult.Rejected,"Conflicting contract accepted");
}
static Task Package()
{
    var manifest=ManifestJson.Deserialize(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixture/manifest.json")));
    Check(WidgetManifestValidator.Validate(manifest).Count==0,"Manifest invalid");
    Check(manifest.Name == "WidgetRail Catalogue" && manifest.Id == "widgetrail.firstparty.marketplace", "Display rename changed the package identity");
    Check(manifest.Permissions.Count==0 && manifest.OptionalPermissions.Count==0,"Widget attempts to self-declare management authority");
    Check(manifest.Intents!.Handles.Single().Id==WidgetIntentContracts.OpenMarketplace,"Marketplace handler missing");
    var style=WrssParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixture/default.wrss")),"default.wrss");
    Check(style.IsValid,"Marketplace WRSS invalid");
    var compiled=WrssThemeCompiler.Compile([style.Document]);
    Check(compiled.IsValid,string.Join("; ",compiled.Diagnostics.Select(d=>d.Message)));
    return Task.CompletedTask;
}
sealed class Fixture : IAsyncDisposable
{
    internal const string Id="example.first";
    internal MarketplaceSnapshot Snapshot=new(1,[new(Id,"First widget","Controller-first example.",[],"2.0.0","1.0.0",1024),new("example.second","Second widget","Another example.",[],"1.0.0")],CheckedAtUtc:DateTimeOffset.UtcNow);
    internal System.Collections.Concurrent.ConcurrentQueue<MarketplaceCommand> Commands=new();
    internal TaskCompletionSource? RefreshGate;
    internal TaskCompletionSource? StatusGate;
    internal MarketplaceWidget Widget;
    internal Fixture()
    {
        var builder=new WidgetTestHostServicesBuilder();
        foreach(var action in new[]{"status","refresh","inventory","download","install","cancel","download-updates","install-updates"})
            builder.WithHandler(WidgetMarketplaceCapabilities.Command(action),async (command,token)=>{
                Commands.Enqueue(command);
                if (command.Action == "status" && StatusGate is not null) await StatusGate.Task.WaitAsync(token);
                if (command.Action == "refresh" && RefreshGate is not null) await RefreshGate.Task.WaitAsync(token);
                if(command.Action=="download")Snapshot=Snapshot with {Revision=2,Operation=new(new string('a',32),"download","running",Id)};
                if (command.Action == "download-updates") Snapshot = Snapshot with { Revision = 2,
                    Operation = new(new string('a',32),"download-updates","running",command.PackageIds![0]),
                    UpdateBatch = new(Snapshot.Items.Where(item => command.PackageIds.Contains(item.Id)).Select(item =>
                        new MarketplaceUpdateItem(item.Id,item.Name,item.AvailableVersion!,"queued")).ToArray()) };
                if (command.Action == "install-updates") Snapshot = Snapshot with { Revision = 4,
                    Items = Snapshot.Items.Select(item => Snapshot.UpdateBatch!.Items.Any(update => update.PackageId == item.Id && update.State == "review")
                        ? item with { InstalledVersion = item.AvailableVersion, AvailableVersionSaved = true } : item).ToArray(),
                    Operation = new(new string('a',32),"install-updates","completed",null),
                    UpdateBatch = Snapshot.UpdateBatch! with { ReviewToken = null, Items = Snapshot.UpdateBatch!.Items.Select(item => item with { State="completed",Review=null }).ToArray() } };
                if(command.Action=="install")Snapshot=Snapshot with {Revision=4,Items=Snapshot.Items.Select(i=>i.Id==Id?i with {InstalledVersion="2.0.0",AvailableVersionSaved=true}:i).ToArray(),
                    Operation=new(new string('a',32),"install","completed",Id,Message:"Widget updated")};
                // Match IPC: even unchanged responses contain fresh list instances.
                return JsonSerializer.Deserialize<MarketplaceSnapshot>(JsonSerializer.Serialize(Snapshot))!;
            });
        Widget=WidgetTestHost.Attach(new MarketplaceWidget(),builder.Build());
    }
    internal ViewSnapshot View()
    {
        try { return Widget.RenderSnapshot("marketplace",1); }
        catch (ProtocolValidationException error) { throw new Exception(string.Join("; ", error.Errors), error); }
    }
    internal async Task Start() { await WidgetTestHost.InitializeAsync(Widget);await WidgetTestHost.SetLifecycleStateAsync(Widget,WidgetLifecycleState.Interactive);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(Commands.Count<2)await Task.Delay(10,deadline.Token); }
    internal ValueTask Act(string action, string? text = null)=>Widget.OnActionAsync(new(action,action) { CommittedText = text });
    public ValueTask DisposeAsync()=>WidgetTestHost.DestroyAsync(Widget);
}
