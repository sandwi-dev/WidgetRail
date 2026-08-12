using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

/// <summary>
/// Partitions the one authoritative launcher view into host-known semantic
/// slots. Slot wrappers own no actions or domain identity; every actionable
/// descendant is the exact element authored by Game Launcher.
/// </summary>
internal static class GameLauncherExperienceProjection
{
    internal const string MarkerClass = "game-launcher-experience";

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
            .Classes("game-launcher-slot", "game-launcher-slot--details-panel");
        rail = rail.AddClasses("game-launcher-slot", "game-launcher-slot--game-rail");
        var collections = root.Children[2].AddClasses(
            "game-launcher-slot", "game-launcher-slot--collection-tabs");
        var sources = root.Children[1].AddClasses(
            "game-launcher-slot", "game-launcher-slot--source-status");
        var operations = UI.Stack("game-launcher.content", operationChildren)
            .Classes("game-launcher-content", "game-launcher-main",
                "game-launcher-slot", "game-launcher-slot--operation-status");
        var hints = UI.Stack("game-launcher.slot.controller-hints",
                existingHints ?? UI.Stack("game-launcher.hints.empty"))
            .Classes("game-launcher-footer", "game-launcher-slot",
                "game-launcher-slot--controller-hints");

        var projectedRoot = UI.Stack(
                $"game-launcher.experience.{GameLauncherExperienceIdentity.Id(experience)}",
                details, rail, collections, sources, operations, hints)
            .Classes(MarkerClass,
                $"game-launcher-experience--{GameLauncherExperienceIdentity.Id(experience)}");
        return view with { Root = projectedRoot };
    }
}
