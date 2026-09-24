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
                     "playnite-library-home-actions", "playnite-library-home-summary",
                     "playnite-library-browse-foreground", "playnite-library-categories-shell",
                     "playnite-library-hidden-shell", "playnite-library-playnite-shell",
                     "playnite-library-empty",
                 })
            Assert.AreEqual(surface, Resolve("stack", style).Get("background")?.Text, style);
        Assert.AreEqual(raised, Resolve("action-surface", "playnite-library-category").Get("background")?.Text);
        Assert.AreEqual(raised, Resolve("button", "playnite-library-control").Get("background")?.Text);
        Assert.AreEqual(text, Resolve("text", "playnite-library-summary-title").Get("color")?.Text);
        Assert.AreEqual(accent, Resolve("text", "playnite-library-summary-badge-accent").Get("color")?.Text);
        var focusedControl = theme.Resolve(new WrssElement("button", null,
            new HashSet<string>(["playnite-library-control"]),
            new HashSet<WrssPseudoState>([WrssPseudoState.Focused])));
        Assert.AreEqual(focus, focusedControl.Get("outline-color")?.Text);

        WrssResolvedStyle Resolve(string role, params string[] classes) =>
            theme.Resolve(new WrssElement(role, null, classes.ToHashSet(StringComparer.Ordinal)));
    }

    [TestMethod]
    public void CategoryRowsAreSingleActionsAndMissingCoversKeepTheirLabels()
    {
        var item = PlayniteLibraryItem.From(Item("app-theme", "saved-theme",
            "A game with a long title and no cover artwork", "Steam"));
        var category = new PlayniteLibraryCategory("category.theme", "Strategy and adventure", [item.Value.SavedId]);
        var organization = PlayniteLibraryPrivateState.Empty with { Categories = [category] };
        var state = State(Snapshot(WidgetPagedResourceStatus.Ready, [item]), organization,
            PlayniteLibraryRoute.Categories, []);
        var snapshot = new PresentationWidget(PlayniteLibraryPresentation.Render(state))
            .RenderSnapshot("playnite-library.categories-theme", 1);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        var row = Nodes(snapshot.Root).Single(node => node.ActionId == PlayniteLibraryActions.CategoryOpen(category.Id));
        Assert.AreEqual(ViewNodeKind.ActionSurface, row.Kind);
        Assert.IsTrue(row.IsFocusable);
        Assert.AreEqual(1, Nodes(row).Count(node => node.IsFocusable));
        Assert.IsTrue(Nodes(row).Any(node => node.Text == "1 game"));
        var busy = new PresentationWidget(PlayniteLibraryPresentation.Render(state with { OrganizationBusy = true }))
            .RenderSnapshot("playnite-library.categories-busy", 2);
        Assert.IsTrue(Nodes(busy.Root).Single(node => node.Id == row.Id).IsDisabled == true);

        foreach (var route in new[] { PlayniteLibraryRoute.Library, PlayniteLibraryRoute.Browse, PlayniteLibraryRoute.Hidden })
        {
            var view = PlayniteLibraryPresentation.Render(state with
            {
                Route = route, HiddenRows = [item],
                Organization = organization with { ExcludedSavedIds = route == PlayniteLibraryRoute.Hidden ? [item.Value.SavedId] : [] },
            });
            var rendered = new PresentationWidget(view).RenderSnapshot("playnite-library.fallback", 3);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(rendered).Count);
            var title = Nodes(rendered.Root).Single(node => node.StyleClasses.Contains("playnite-library-poster-fallback-title"));
            Assert.AreEqual(item.Presentation.DisplayName, title.Text);
            var scrim = Nodes(rendered.Root).Single(node => node.StyleClasses.Contains("playnite-library-poster-fallback"));
            Assert.IsNull(CompileStyles().Resolve(new WrssElement("stack", scrim.Id,
                scrim.StyleClasses.ToHashSet())).Get("height"), "Missing-cover labels must have natural height.");
        }
    }

    [TestMethod]
    public void CompactSummaryKeepsLaunchFeedbackVisible()
    {
        var item = PlayniteLibraryItem.From(Item("pending-app", "pending-game", "Pending game", "Steam"));
        var state = State(Snapshot(WidgetPagedResourceStatus.Ready, [item]),
            PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Library, []) with
        {
            LaunchingSavedId = item.Value.SavedId,
        };
        var snapshot = new PresentationWidget(PlayniteLibraryPresentation.Render(state))
            .RenderSnapshot("playnite-library.pending-summary", 1);
        var tile = Nodes(snapshot.Root).Single(node => node.ActionId == PlayniteLibraryActions.Launch);
        var compactMetadata = Nodes(tile.FocusPresentation!).Single(node =>
            node.StyleClasses.Contains("playnite-library-summary-compact-meta"));
        StringAssert.StartsWith(compactMetadata.Text!, "Pending · ");
    }

    [TestMethod]
    public void SupportingScreensAndNavigationRemainValidAcrossStates()
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
                var snapshot = new PresentationWidget(PlayniteLibraryPresentation.Render(state))
                    .RenderSnapshot("playnite-library.fixture", 1);
                Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count, route + " " + phase);
                if (route is PlayniteLibraryRoute.Library or PlayniteLibraryRoute.Browse)
                {
                    var navigation = Nodes(snapshot.Root).Single(node => node.Id == "playnite-library.navigation");
                    Assert.AreEqual(2, navigation.Children.Count);
                    Assert.AreEqual(1, navigation.Children.Count(node => node.IsSelected == true));
                }
                if (phase == WidgetPagedResourceStatus.Loading)
                    Assert.IsNull(snapshot.FocusGroupEntryRequest, "Do not enter loading placeholders before games arrive.");
                ExportFixture(route + "-" + phase, snapshot);
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
        var longView = PlayniteLibraryPresentation.Render(State(
            Snapshot(WidgetPagedResourceStatus.Ready, [longGame]), organization, PlayniteLibraryRoute.Library, []));
        var longSnapshot = new PresentationWidget(longView).RenderSnapshot("playnite-library.long-summary", 1);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(longSnapshot).Count);
        ExportFixture("Library-Long", longSnapshot);

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
    private static void ExportFixture(string name, ViewSnapshot snapshot)
    {
        if (Environment.GetEnvironmentVariable("WRAIL_PLAYNITE_LAYOUT_OUTPUT") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".snapshot.json"), SnapshotJson.Serialize(snapshot));
    }
}
