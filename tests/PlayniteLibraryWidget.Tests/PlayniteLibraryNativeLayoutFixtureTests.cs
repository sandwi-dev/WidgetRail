using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Tests.PlayniteLibrary;

public sealed partial class PlayniteLibraryLayoutTests
{
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
