using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryLayoutTests
{
    [TestMethod]
    public async Task NativeHomeRailLoadingGeometryUsesProductionStyles()
    {
        var game = PlayniteLibraryItem.From(Item("loading-app", "loading-game", "Loading geometry game", "Steam", "poster-handle"));
        var captured = await CapturePresentationAsync(State(Snapshot(WidgetPagedResourceStatus.Ready, [game]),
            PlayniteLibraryPrivateState.Empty, PlayniteLibraryRoute.Library, []));
        var snapshot = captured.Snapshot;
        var stage = Nodes(snapshot.Root).Single(node => node.Id == PlayniteLibraryPresentation.HomeRailId + ".stage");
        Assert.AreEqual(ViewNodeKind.Grid, stage.Kind);
        Assert.AreEqual(2, stage.Children.Count);
        var sizing = Nodes(stage.Children[0]).Single(node => node.Kind == ViewNodeKind.Spacer);
        Assert.IsFalse(Nodes(stage.Children[0]).Any(node => node.IsFocusable || node.ActionId is not null));
        Assert.AreEqual(ViewNodeKind.IndexedCollection, stage.Children[1].Kind);
        Assert.AreEqual(0, stage.Children[1].Children.Count, "Reserving geometry must not eagerly publish row trees.");
        var poster = captured.Ranges.SelectMany(range => range.Items).Single().Root;
        var theme = CompileStyles();
        var shelf = Nodes(snapshot.Root).Single(node => node.Id == "playnite-library.home.focus-summary");
        var shelfStyle = theme.Resolve(new("focus-presentation-surface", shelf.Id, shelf.StyleClasses.ToHashSet(StringComparer.Ordinal)));
        Assert.IsNull(shelfStyle.Get("height"), "A viewport-height shelf clips the poster rail when the game summary grows.");
        Assert.IsNull(shelfStyle.Get("max-height"), "Large text and wrapped game titles must participate in the shelf's measured height.");
        var sizeStyle = theme.Resolve(new("spacer", sizing.Id, sizing.StyleClasses.ToHashSet(StringComparer.Ordinal)));
        var posterStyle = theme.Resolve(new("actionSurface", poster.Id, poster.StyleClasses.ToHashSet(StringComparer.Ordinal)));
        foreach (var property in new[] { "width", "min-width", "max-width", "aspect-ratio", "margin", "padding", "border-width" })
            Assert.AreEqual(posterStyle.Get(property), sizeStyle.Get(property),
                $"Loading reservation and live poster must share {property}, including theme/package overrides.");
        ExportFixture("Native-Home-Rail-Loading", snapshot, captured.Ranges);
    }

    [TestMethod]
    public void NativeDetailsNavigationFixtureCoversOptionalTransitions()
    {
        var game = PlayniteLibraryItem.From(Item("navigation", "saved-navigation", "Navigation game", "Steam"));
        PlayniteDetailsExtras[] states =
        [
            new() { Tab = PlayniteDetailsTab.Description },
            new() { Tab = PlayniteDetailsTab.Information },
            new() { Tab = PlayniteDetailsTab.Achievements, AchievementsLoading = true },
            new() { Tab = PlayniteDetailsTab.Achievements, Achievements = new(true, 0, 0, []) { CanRefresh = true } },
            new() { Tab = PlayniteDetailsTab.Activity, Activity = PlayniteActivity.Unavailable },
            new() { Tab = PlayniteDetailsTab.Achievements, Achievements = PlayniteAchievements.Unavailable },
            new() { Tab = PlayniteDetailsTab.Achievements, AchievementsError = "Unavailable" },
            new() { Tab = PlayniteDetailsTab.Achievements, Achievements = new(true, 1, 1,
                [new("Achievement", "Description", true, null, null, null, false)]) },
            new() { Tab = PlayniteDetailsTab.Links },
            new() { Tab = PlayniteDetailsTab.Description },
        ];
        var names = new List<string>();
        for (var index = 0; index < states.Length; index++)
        {
            var modal = PlayniteLibraryDetailsPresentation.Create(new WidgetView(UI.Stack("fixture.parent")), game, true, null, "Ready", null, extras: states[index]);
            var snapshot = new PresentationWidget(modal).RenderSnapshot("native.details.navigation", index + 1);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
            var name = "Native-Details-Navigation-" + index;
            ExportFixture(name, snapshot);
            names.Add(name + ".renderer.json");
        }
        if (Environment.GetEnvironmentVariable("WRAIL_PLAYNITE_LAYOUT_OUTPUT") is { Length: > 0 } directory)
            File.WriteAllText(Path.Combine(directory, "Native-Details-Navigation.json"),
                System.Text.Json.JsonSerializer.Serialize(new { navigationFrames = names }));
    }

    [TestMethod]
    public void NativeDetailsGeometryFixtureUsesProductionPresentationAndTheme()
    {
        var raw = Item("native-layout", "native-layout-game", "Native layout game", "Steam", "poster-handle");
        var game = PlayniteLibraryItem.From(raw with { Presentation = raw.Presentation with
        {
            Metadata = new("fixture", new("fixture", "1", "Synthetic metadata", 0))
            {
                Description = string.Join(" ", Enumerable.Repeat("This long game description should wrap within the modal instead of creating a horizontal scroll extent.", 35)),
                PlaytimeMinutes = 180,
            },
        } });
        var metadata = new PlayniteBridgeGame(game.Value.SavedId, "Native layout game", "Steam", true, true, false,
            "Playing", [], ["Adventure"], ["Windows"], 180, 1)
        { Developers = ["Test developer"], Publishers = ["Test publisher"], Features = ["Controller support"] };
        var modal = PlayniteLibraryDetailsPresentation.Create(new WidgetView(UI.Stack("fixture.parent")), game, true, null, "Ready", null,
            extras: new() { Full = new(game.Value, metadata), Tab = PlayniteDetailsTab.Information });
        var snapshot = new PresentationWidget(modal).RenderSnapshot("native.details.geometry", 1);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        ExportFixture("Native-Details-Geometry", snapshot);
    }
}
