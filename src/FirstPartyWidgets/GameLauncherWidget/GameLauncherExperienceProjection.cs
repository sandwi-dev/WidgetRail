using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

/// <summary>
/// Partitions the one authoritative launcher view into host-known semantic
/// slots. Slot wrappers own no actions or domain identity; every actionable
/// descendant is the exact element authored by Game Launcher.
/// </summary>
internal static class GameLauncherExperienceProjection
{
    internal static WidgetView Project(
        WidgetView view,
        GameLauncherExperience experience)
    {
        if (view.Root is not StackElement root || root.Children.Count != 4 ||
            root.Children[3] is not StackElement content)
            return view;

        var hero = content.Children.FirstOrDefault(child =>
            child.Id == "game-launcher.hero");
        var rail = content.Children.FirstOrDefault(child =>
            child.Id == GameLauncherPresentation.ScrollId);
        if (hero is null || rail is null) return view;

        var existingHints = content.Children.FirstOrDefault(child =>
            child.Id == "game-launcher.organization.hints");
        var operationChildren = content.Children.Where(child =>
            child.Id != hero.Id && child.Id != rail.Id &&
            child.Id != existingHints?.Id).ToArray();

        var details = UI.Stack("game-launcher.slot.details-panel",
                root.Children[0], hero)
            .InAdvancedPresentationSlot(
                WidgetAdvancedPresentationSlot.DetailsPanel);
        rail = rail.InAdvancedPresentationSlot(
            WidgetAdvancedPresentationSlot.PrimaryCollection);
        var collections = root.Children[2].InAdvancedPresentationSlot(
            WidgetAdvancedPresentationSlot.CollectionNavigation);
        var sources = UI.Stack(
                "game-launcher.slot.source-status", root.Children[1])
            .InAdvancedPresentationSlot(
                WidgetAdvancedPresentationSlot.SourceStatus);
        var operations = UI.Stack("game-launcher.content", operationChildren)
            .Classes("game-launcher-content", "game-launcher-main")
            .InAdvancedPresentationSlot(
                WidgetAdvancedPresentationSlot.OperationStatus);
        var hints = UI.Row("game-launcher.slot.controller-hints",
                existingHints ?? UI.Stack("game-launcher.hints.empty"))
            .Classes("game-launcher-footer")
            .InAdvancedPresentationSlot(
                WidgetAdvancedPresentationSlot.ControllerHints);

        var projectedRoot = UI.Stack(
                $"game-launcher.experience.{GameLauncherExperienceIdentity.Id(experience)}",
                details, rail, collections, sources, operations, hints)
            .Classes("game-launcher-widget");
        return view with
        {
            Root = projectedRoot,
            AdvancedPresentation = new(
                WidgetAdvancedPresentationKind.LauncherExperience,
                Preset(experience)),
        };
    }

    private static WidgetAdvancedPresentationPreset Preset(
        GameLauncherExperience experience) => experience switch
        {
            GameLauncherExperience.CoverWall =>
                WidgetAdvancedPresentationPreset.CoverWall,
            GameLauncherExperience.Carousel =>
                WidgetAdvancedPresentationPreset.Carousel,
            GameLauncherExperience.CompactGrid =>
                WidgetAdvancedPresentationPreset.CompactGrid,
            _ => WidgetAdvancedPresentationPreset.HeroRail,
        };
}
