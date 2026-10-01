using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryLayoutTests
{
    [TestMethod]
    public void PackageStylesValidateWithoutHostThemeDocuments()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "styles");
        var package = WrssPackageLoader.Load("default.wrss", new WrssFileSourceProvider(root));
        var result = WrssThemeCompiler.Compile(package);
        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("builtin-cool-slate")]
    [DataRow("builtin-neon-circuit")]
    [DataRow("builtin-redline")]
    [DataRow("builtin-arcade-rush")]
    public void AllScreensInheritSelectedThemeColors(string? themeName)
    {
        var theme = CompileStyles(themeName);
        var surface = Resolve("panel").Get("background")?.Text;
        var text = Resolve("canvas").Get("color")?.Text;
        var raised = Resolve("row", "wrail-toast").Get("background")?.Text;
        var accent = Resolve("text", "wrail-tile__state").Get("color")?.Text;
        var focus = theme.Resolve(new WrssElement("action-surface", null,
            new HashSet<string>(["wrail-action-surface"]),
            new HashSet<WrssPseudoState>([WrssPseudoState.Focused])))
            .Get("outline-color")?.Text;
        Assert.IsNotNull(surface);
        Assert.IsNotNull(text);
        Assert.IsNotNull(raised);
        Assert.IsNotNull(accent);
        Assert.IsNotNull(focus);
        foreach (var style in new[]
                 {
                     "playnite-library-categories-shell",
                     "playnite-library-hidden-shell", "playnite-library-playnite-shell",
                     "playnite-library-empty",
                 })
            Assert.AreEqual(surface, Resolve("stack", style).Get("background")?.Text, style);
        foreach (var style in new[] { "playnite-library-home-actions", "playnite-library-home-summary", "playnite-library-browse-foreground" })
        {
            var panel = Resolve("stack", style);
            // Theme surfaces have .98 alpha; the panel multiplies that by .84,
            // preserving each theme's RGB without dimming text or artwork.
            StringAssert.EndsWith(surface, ", 0.98)");
            Assert.AreEqual(surface.Replace(", 0.98)", ", 0.8232)"), panel.Get("background")?.Text, style);
            Assert.IsNull(panel.Get("opacity"), style);
        }
        Assert.AreEqual(raised, Resolve("action-surface", "playnite-library-category").Get("background")?.Text);
        Assert.AreEqual(raised, Resolve("button", "playnite-library-control").Get("background")?.Text);
        Assert.AreEqual(text, Resolve("text", "playnite-library-summary-title").Get("color")?.Text);
        Assert.AreEqual(text, Resolve("stack", "wrail-modal").Get("color")?.Text);
        Assert.AreEqual(text, Resolve("text", "playnite-library-details-field").Get("color")?.Text);
        Assert.AreEqual(text, Resolve("text", "playnite-library-details-description").Get("color")?.Text);
        Assert.AreEqual(Resolve("text", "playnite-library-details-meta").Get("color")?.Text,
            Resolve("text", "playnite-library-details-label").Get("color")?.Text);
        Assert.AreEqual("13px", Resolve("text", "playnite-library-details-label").Get("font-size")?.Text);
        Assert.AreEqual("20px", Resolve("text", "playnite-library-details-section-title").Get("font-size")?.Text);
        Assert.AreEqual("400", Resolve("text", "playnite-library-details-description").Get("font-weight")?.Text);
        Assert.AreEqual("128", Resolve("text", "playnite-library-details-description").Get("max-lines")?.Text);
        Assert.AreEqual(raised.Replace(", 0.98)", ", 0.8232)"), Resolve("stack", "wrail-modal").Get("background")?.Text);
        Assert.IsNull(Resolve("stack", "wrail-modal").Get("opacity"), "Panel alpha must not dim its children.");
        Assert.AreEqual(Resolve("backdrop").Get("background")?.Text,
            Resolve("modalLayer", "wrail-modal-layer").Get("background")?.Text);
        Assert.AreEqual("24px", Resolve("stack", "wrail-modal").Get("padding")?.Text);
        Assert.AreEqual("column", Resolve("row", "wrail-modal__header").Get("direction")?.Text);
        Assert.AreEqual("900px", Resolve("stack", "wrail-modal").Get("width")?.Text);
        Assert.AreEqual("3", Resolve("text", "playnite-library-details-field").Get("flex-grow")?.Text);
        Assert.AreEqual(accent, Resolve("text", "playnite-library-summary-badge-accent").Get("color")?.Text);
        var focusedControl = theme.Resolve(new WrssElement("button", null,
            new HashSet<string>(["playnite-library-control"]),
            new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
        Assert.AreEqual(focus, focusedControl.Get("outline-color")?.Text);

        WrssResolvedStyle Resolve(string role, params string[] classes) =>
            theme.Resolve(new WrssElement(role, null, classes.ToHashSet(StringComparer.Ordinal)));
    }

    [TestMethod]
    public async Task CategoryRowsAreSingleActionsAndMissingCoversKeepTheirLabels()
    {
        var item = PlayniteLibraryItem.From(Item("app-theme", "saved-theme",
            "A game with a long title and no cover artwork", "Steam"));
        var category = new PlayniteLibraryCategory("category.theme", "Strategy and adventure", [item.Value.SavedId]);
        var organization = PlayniteLibraryPrivateState.Empty with { Categories = [category] };
        var state = State(Snapshot(WidgetPagedResourceStatus.Ready, [item]), organization,
            PlayniteLibraryRoute.Categories, []);
        var snapshot = new PresentationWidget((await CapturePresentationAsync(state)).View)
            .RenderSnapshot("playnite-library.categories-theme", 1);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        var row = Nodes(snapshot.Root).Single(node => node.ActionId == PlayniteLibraryActions.CategoryOpen(category.Id));
        Assert.AreEqual(ViewNodeKind.ActionSurface, row.Kind);
        Assert.IsTrue(row.IsFocusable);
        Assert.AreEqual(1, Nodes(row).Count(node => node.IsFocusable));
        Assert.IsTrue(Nodes(row).Any(node => node.Text == "1 game"));
        var busy = new PresentationWidget((await CapturePresentationAsync(state with { OrganizationBusy = true })).View)
            .RenderSnapshot("playnite-library.categories-busy", 2);
        Assert.IsTrue(Nodes(busy.Root).Single(node => node.Id == row.Id).IsDisabled == true);

        foreach (var route in new[] { PlayniteLibraryRoute.Library, PlayniteLibraryRoute.Browse, PlayniteLibraryRoute.Hidden })
        {
            var routeState = state with
            {
                Route = route, HiddenRows = [item],
                Organization = organization with { ExcludedSavedIds = route == PlayniteLibraryRoute.Hidden ? [item.Value.SavedId] : [] },
            };
            await using var fixture = new IndexedPresentationFixture(routeState, [item]);
            await fixture.StartAsync();
            using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture, "playnite-library.fallback");
            var rendered = host.CurrentSnapshot;
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(rendered).Count);
            using var lease = route == PlayniteLibraryRoute.Hidden ? null : await host.AcquireAsync(
                route == PlayniteLibraryRoute.Library ? PlayniteLibraryPresentation.HomeRailId : PlayniteLibraryPresentation.ScrollId, 0, 1);
            var posterRoot = lease?.Range.Items.Single().Root ?? rendered.Root;
            var title = Nodes(posterRoot).Single(node => node.StyleClasses.Contains("playnite-library-poster-fallback-title"));
            Assert.AreEqual(item.Presentation.DisplayName, title.Text);
            var scrim = Nodes(posterRoot).Single(node => node.StyleClasses.Contains("playnite-library-poster-fallback"));
            Assert.IsNull(CompileStyles().Resolve(new WrssElement("stack", scrim.Id,
                scrim.StyleClasses.ToHashSet())).Get("height"), "Missing-cover labels must have natural height.");
        }
    }

    [TestMethod]
    public async Task CompactSummaryKeepsLaunchFeedbackVisible()
    {
        var item = PlayniteLibraryItem.From(Item("pending-app", "pending-game", "Pending game", "Steam"));
        var state = State(Snapshot(WidgetPagedResourceStatus.Ready, [item]),
            PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Library, []) with
        {
            LaunchingSavedId = item.Value.SavedId,
        };
        await using var fixture = new IndexedPresentationFixture(state, [item]);
        await fixture.StartAsync();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture, "playnite-library.pending-summary");
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(host.CurrentSnapshot).Count);
        using var lease = await host.AcquireAsync(PlayniteLibraryPresentation.HomeRailId, 0, 1);
        var tile = lease.Range.Items.Single().Root;
        Assert.AreEqual(PlayniteLibraryActions.DetailsOpen, tile.ActionId);
        var compactMetadata = Nodes(tile.FocusPresentation!).Single(node =>
            node.StyleClasses.Contains("playnite-library-summary-compact-meta"));
        StringAssert.StartsWith(compactMetadata.Text!, "Pending · ");
    }

    [TestMethod]
    [DataRow((int)PlayniteLibraryRoute.Library)]
    [DataRow((int)PlayniteLibraryRoute.Browse)]
    public async Task IndexedPageAndPosterThemeContractsSurviveBusyAndRetainedStates(int routeValue)
    {
        var route = (PlayniteLibraryRoute)routeValue;
        var item = ArtworkItem("theme-app", "theme-game", "Theme game", "Steam", "tile", "hero");
        foreach (var busy in new[] { false, true })
        foreach (var retained in new[] { false, true })
        {
            var state = State(Snapshot(WidgetPagedResourceStatus.Ready, []),
                PlayniteLibraryPrivateState.Empty, route, []) with
            {
                OrganizationBusy = busy,
                BrowseRetained = retained,
            };
            await using var fixture = new IndexedPresentationFixture(state, [item]);
            await fixture.StartAsync();
            using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture, "playnite-library.indexed-theme");
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(host.CurrentSnapshot).Count);
            var declaration = Nodes(host.CurrentSnapshot.Root).Single(node => node.Kind == ViewNodeKind.IndexedCollection);
            Assert.AreEqual(0, declaration.Children.Count, "The page must never fall back to eager poster rendering.");
            using var lease = await host.AcquireAsync(declaration.Id, 0, 1);
            var poster = lease.Range.Items.Single().Root;
            var disabled = busy || route == PlayniteLibraryRoute.Browse && retained;
            Assert.AreEqual(disabled, poster.IsDisabled == true);
            Assert.AreEqual("hero", poster.FocusBackgroundArtworkHandle);
            Assert.AreEqual(disabled ? (ControllerButton?)null : ControllerButton.X, poster.ContextMenuButton,
                "Busy or retained rows must not advertise actions which cannot be dispatched.");
            Assert.IsTrue(poster.StyleClasses.Contains(route == PlayniteLibraryRoute.Library
                ? "playnite-library-fixed-tile" : "playnite-library-browse-tile"));
            Assert.AreEqual(ImageFit.Cover, Nodes(poster).Single(node => node.Kind == ViewNodeKind.Image).ImageFit);
        }
    }

    [TestMethod]
    public async Task IndexedWarmHomeKeepsMissingArtworkLabelsWithoutEnablingSavedOnlyGames()
    {
        var organization = PlayniteLibraryPrivateState.Empty with
        {
            Items = [new("saved-warm-theme", "Previously seen game", "Steam")],
        };
        var state = State(Snapshot(WidgetPagedResourceStatus.Loading, []), organization,
            PlayniteLibraryRoute.Library, []) with { ContentEntryRequestId = 1 };
        await using var fixture = new IndexedPresentationFixture(state, [], warmDisplayOnly: true);
        await fixture.StartAsync();
        using var host = WidgetTestHost.CreateIndexedCollectionHost(fixture, "playnite-library.warm-theme");
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(host.CurrentSnapshot).Count);
        Assert.IsNull(host.CurrentSnapshot.FocusGroupEntryRequest, "Loading must not request entry into disabled saved-only rows.");
        using var lease = await host.AcquireAsync(PlayniteLibraryPresentation.HomeRailId, 0, 1);
        var poster = lease.Range.Items.Single().Root;
        Assert.IsTrue(poster.IsDisabled == true);
        Assert.IsNull(poster.ContextMenuButton);
        Assert.IsNull(poster.FocusBackgroundArtworkHandle);
        Assert.AreEqual("Previously seen game", Nodes(poster).Single(node =>
            node.StyleClasses.Contains("playnite-library-poster-fallback-title")).Text);
    }

    [TestMethod]
    public async Task SupportingScreensAndNavigationRemainValidAcrossStates()
    {
        var items = Enumerable.Range(0, 18).Select(index => PlayniteLibraryItem.From(Item(
            "app-" + index, "saved-" + index, "Adventure " + index, "Steam"))).ToArray();
        var organization = PlayniteLibraryPrivateState.Empty with
        {
            FavoriteSavedIds = [items[0].Value.SavedId],
            Categories = [new("category.theme", "Strategy and adventure", [items[0].Value.SavedId])],
        };
        foreach (var route in new[] { PlayniteLibraryRoute.Library, PlayniteLibraryRoute.Browse, PlayniteLibraryRoute.Categories, PlayniteLibraryRoute.Hidden })
        {
            foreach (var phase in new[] { WidgetPagedResourceStatus.Ready, WidgetPagedResourceStatus.Loading, WidgetPagedResourceStatus.Error })
            {
                var state = State(Snapshot(phase, phase == WidgetPagedResourceStatus.Ready ? items : [],
                    error: phase == WidgetPagedResourceStatus.Error ? new("unavailable", "Start Playnite and try again.") : null),
                    organization, route, []) with
                {
                    ContentEntryRequestId = 1,
                    HiddenRows = items,
                    Organization = route == PlayniteLibraryRoute.Hidden
                        ? organization with { ExcludedSavedIds = items.Select(item => item.Value.SavedId).ToArray() }
                        : organization,
                };
                var captured = await CapturePresentationAsync(state);
                var snapshot = captured.Snapshot;
                Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count, route + " " + phase);
                if (route is PlayniteLibraryRoute.Library or PlayniteLibraryRoute.Browse)
                {
                    var navigation = Nodes(snapshot.Root).Where(node => node.ActionId is PlayniteLibraryActions.HomeOpen or PlayniteLibraryActions.BrowseOpen).ToArray();
                    Assert.AreEqual(2, navigation.Length);
                    Assert.AreEqual(1, navigation.Count(node => node.IsSelected == true));
                    var theme = CompileStyles();
                    foreach (var tab in navigation)
                    {
                        var tabStyle = theme.Resolve(new WrssElement("button", tab.Id,
                            tab.StyleClasses.ToHashSet(StringComparer.Ordinal)));
                        Assert.AreEqual("128px", tabStyle.Get("width")?.Text,
                            "Home and Library should offer equally wide targets.");
                        Assert.AreEqual("104px", tabStyle.Get("min-width")?.Text,
                            "Compact layouts must retain readable destination labels.");
                        Assert.AreEqual(WidgetTransitionKind.Selection, tab.Transition?.Kind,
                            "Sizing the destinations must preserve coordinated section selection motion.");
                    }
                }
                if (phase == WidgetPagedResourceStatus.Loading)
                    Assert.IsNull(snapshot.FocusGroupEntryRequest, "Do not enter loading placeholders before games arrive.");
                ExportFixture(route + "-" + phase, snapshot, captured.Ranges);
            }
        }
        var longItem = Item("long-app", "long-game", "Long game", "Steam");
        var longGame = PlayniteLibraryItem.From(longItem with
        {
            Presentation = longItem.Presentation with
            {
                DisplayName = "A long adventure title with an extended edition and additional content",
                Metadata = new("fixture", new("fixture", "1", "Synthetic metadata", 0))
                {
                    PlaytimeMinutes = 732,
                    Description = "Explore a distant world, discover new stories, and return to an adventure with a description that spans several lines in the selected-game summary.",
                },
            },
        });
        var longCapture = await CapturePresentationAsync(State(
            Snapshot(WidgetPagedResourceStatus.Ready, [longGame]), organization, PlayniteLibraryRoute.Library, []));
        var longSnapshot = longCapture.Snapshot;
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(longSnapshot).Count);
        ExportFixture("Library-Long", longSnapshot, longCapture.Ranges);

        foreach (var kind in Enum.GetValues<PlayniteBridgeConnectionKind>())
        {
            var view = PlayniteLibraryConnectionPresentation.Render(new(kind, false, string.Empty, true, null));
            var snapshot = new PresentationWidget(view).RenderSnapshot("playnite-library.connection", 1);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count, kind.ToString());
            var header = Nodes(snapshot.Root).Single(node => node.Id == "playnite-library.playnite.header");
            Assert.AreEqual(1, Nodes(header).Count(node => node.IsFocusable), "Only Back belongs in the connection header.");
            ExportFixture("Connection-" + kind, snapshot);
        }
    }

    // Optional local export feeds the production Bridge/native geometry probe.
    // These fixtures contain only synthetic game names and no user data.
    private static void ExportFixture(string name, ViewSnapshot snapshot, IReadOnlyList<IndexedCollectionRange>? ranges = null)
    {
        if (Environment.GetEnvironmentVariable("WRAIL_PLAYNITE_LAYOUT_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".snapshot.json"), SnapshotJson.Serialize(snapshot));
        var theme = CompileStyles();
        if (!Nodes(snapshot.Root).Any(node => node.Kind == ViewNodeKind.IndexedCollection))
        {
            WidgetRail.Tests.RendererFixtureExporter.Write(Path.Combine(directory, name + ".renderer.json"), snapshot, theme);
            return;
        }
        // Preserve real parent/range ownership; never flatten lazy rows for the retired C++ renderer.
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
        using var document = System.Text.Json.JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        File.WriteAllText(Path.Combine(directory, name + ".indexed.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            snapshot = document.RootElement.Clone(),
            renderStyles = WidgetRail.WidgetBridge.BridgeRenderStyleResolver.Resolve(snapshot, theme),
            ranges = (ranges ?? []).Select(range => new { range, renderStyles = WidgetRail.WidgetBridge.BridgeRenderStyleResolver.ResolveRange(range, theme) }),
        }, options));
    }

    [TestMethod]
    public async Task LargeLibraryIndexedFixtureUsesProductionRangesAndStyles()
    {
        var items = Enumerable.Range(0, 150).Select(index => PlayniteLibraryItem.From(Item(
            "scroll-app-" + index, "scroll-game-" + index, "Scrolling library game " + index, "Steam", "fixture.art." + index))).ToArray();
        var captured = await CapturePresentationAsync(State(
            Snapshot(WidgetPagedResourceStatus.Ready, items), PlayniteLibraryPrivateState.Empty,
            PlayniteLibraryRoute.Browse, []));
        var snapshot = captured.Snapshot;
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        Assert.AreEqual(150, Nodes(snapshot.Root).Single(node => node.Kind == ViewNodeKind.IndexedCollection).IndexedCollection!.Count);
        Assert.AreEqual(150, captured.Ranges.Sum(range => range.Items.Count));
        ExportFixture("Library-Scroll", snapshot, captured.Ranges);
    }
}
