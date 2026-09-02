using System.Collections.Immutable;

namespace WidgetRail.WidgetProtocol;

internal readonly record struct ControllerShortcutRepeatMatchRule(
    ControllerEventPhase InputPhase,
    ControllerEventPhase ShortcutPhase,
    ControllerActionRepeatPolicy RepeatPolicy);

internal readonly record struct ControllerShortcutOwnerAvailabilityRule(
    bool IsDisabled,
    bool IsBusy,
    bool IsAvailable);

internal static class ControllerShortcutResolutionContract
{
    internal static IReadOnlyList<ControllerShortcutRepeatMatchRule> RepeatMatchRules { get; } =
    [
        new(
            ControllerEventPhase.Repeated,
            ControllerEventPhase.Pressed,
            ControllerActionRepeatPolicy.WhileHeld),
    ];

    internal static ImmutableArray<ControllerShortcutOwnerAvailabilityRule>
        OwnerAvailabilityRules { get; } =
    [
        new(IsDisabled: false, IsBusy: false, IsAvailable: true),
        new(IsDisabled: false, IsBusy: true, IsAvailable: false),
        new(IsDisabled: true, IsBusy: false, IsAvailable: false),
        new(IsDisabled: true, IsBusy: true, IsAvailable: false),
    ];

    internal static bool Matches(
        ControllerShortcut shortcut,
        ControllerButton button,
        ControllerEventPhase phase) =>
        shortcut.Button == button &&
        (shortcut.Phase == phase || RepeatMatchRules.Contains(
            new ControllerShortcutRepeatMatchRule(
                phase, shortcut.Phase, shortcut.RepeatPolicy)));

    internal static bool OwnerAvailable(ViewNode owner)
    {
        var isDisabled = owner.IsDisabled is true;
        var isBusy = owner.IsBusy is true;
        return OwnerAvailabilityRules.Single(rule =>
            rule.IsDisabled == isDisabled && rule.IsBusy == isBusy).IsAvailable;
    }
}

internal enum ControllerShortcutResolutionStatus
{
    NoMatch,
    Resolved,
    OwnerUnavailable,
    FocusNotFound,
}

internal readonly record struct ControllerShortcutResolution(
    ControllerShortcutResolutionStatus Status,
    ViewNode? Owner = null,
    ControllerShortcut? Shortcut = null);

/// <summary>
/// Resolves one authored shortcut inside one admitted input scope. The nearest
/// declaring node owns both the binding and its disabled/busy availability;
/// the focused leaf never lends or removes an ancestor's action authority.
/// </summary>
internal static class ControllerShortcutResolver
{
    internal static ControllerShortcutResolution Resolve(
        ViewNode scopeRoot,
        string? focusedElementId,
        ControllerButton button,
        ControllerEventPhase phase)
    {
        var path = new List<ViewNode>();
        if (focusedElementId is null)
        {
            path.Add(scopeRoot);
        }
        else if (!FindPath(scopeRoot, focusedElementId, isScopeRoot: true, path))
        {
            return new(ControllerShortcutResolutionStatus.FocusNotFound);
        }

        for (var index = path.Count - 1; index >= 0; index--)
        {
            var owner = path[index];
            var shortcut = owner.Shortcuts.FirstOrDefault(candidate =>
                ControllerShortcutResolutionContract.Matches(candidate, button, phase));
            if (shortcut is null) continue;
            return ControllerShortcutResolutionContract.OwnerAvailable(owner)
                ? new(ControllerShortcutResolutionStatus.Resolved, owner, shortcut)
                : new(ControllerShortcutResolutionStatus.OwnerUnavailable, owner, shortcut);
        }
        return new(ControllerShortcutResolutionStatus.NoMatch);
    }

    private static bool FindPath(
        ViewNode node,
        string targetId,
        bool isScopeRoot,
        List<ViewNode> path)
    {
        if (!isScopeRoot && node.InputScopeId is not null) return false;
        path.Add(node);
        if (string.Equals(node.Id, targetId, StringComparison.Ordinal)) return true;
        foreach (var child in node.Children)
        {
            if (FindPath(child, targetId, isScopeRoot: false, path)) return true;
        }
        path.RemoveAt(path.Count - 1);
        return false;
    }
}
