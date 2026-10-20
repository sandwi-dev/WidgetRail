using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryLayoutTests
{
    [TestMethod]
    public void NativeDetailsNavigationFixtureCoversOptionalTransitions()
    {
        var game = PlayniteLibraryItem.From(Item("navigation", "saved-navigation", "Navigation game", "Steam"));
        var parent = new WidgetView(UI.Stack("native.parent", UI.Button("Game", "open", "game")));
        PlayniteDetailsExtras[] states =
        [
            new() { Tab = PlayniteDetailsTab.Description },
            new() { Tab = PlayniteDetailsTab.Information },
            new() { Tab = PlayniteDetailsTab.Achievements, AchievementsLoading = true },
            new() { Tab = PlayniteDetailsTab.Achievements, Achievements = new(true, 0, 0, []) },
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
            var modal = PlayniteLibraryDetailsPresentation.Create(game, true, null, "Ready", null, extras: states[index]);
            var snapshot = new PresentationWidget(parent.WithModal(modal)).RenderSnapshot("native.details.navigation", index + 1);
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
        var parent = new WidgetView(UI.Stack("native.parent", UI.Button("Game", "open", "game")));
        var modal = PlayniteLibraryDetailsPresentation.Create(game, true, null, "Ready", null,
            extras: new() { Full = new(game.Value, metadata), Tab = PlayniteDetailsTab.Description });
        var snapshot = new PresentationWidget(parent.WithModal(modal)).RenderSnapshot("native.details.geometry", 1);
        Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
        ExportFixture("Native-Details-Geometry", snapshot);
    }
}
