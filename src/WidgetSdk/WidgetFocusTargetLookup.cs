using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Provider-neutral classification of an authored stable focus target.</summary>
public enum WidgetFocusTargetState
{
    Missing,
    NotFocusable,
    Disabled,
    OutsideActiveScope,
    Duplicate,
    Valid,
}

/// <summary>One protocol-accurate focus-target lookup result.</summary>
public readonly record struct WidgetFocusTargetResult(string? Id, WidgetFocusTargetState State)
{
    public bool IsEnabled => State == WidgetFocusTargetState.Valid;
}

/// <summary>
/// Resolves authored element IDs through the same serialized protocol tree and
/// focus-scope rules used by snapshot validation. Responsive conditions remain
/// declarative because viewport ownership belongs to the host.
/// </summary>
public static class WidgetFocusTargetLookup
{
    public static WidgetFocusTargetResult Resolve(
        WidgetElement root,
        string? targetId,
        string? activeInputScopeId = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var scopeId = activeInputScopeId ?? RootScopeId(root);
        var result = WidgetRail.WidgetProtocol.ViewFocusTargetLookup.Resolve(
            root.ToProtocolNode(), targetId, scopeId);
        return new(result.Id, Map(result.State));
    }

    public static WidgetFocusTargetResult First(
        WidgetElement root,
        string? activeInputScopeId = null,
        bool includeDisabled = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        var scopeId = activeInputScopeId ?? RootScopeId(root);
        var result = WidgetRail.WidgetProtocol.ViewFocusTargetLookup.First(
            root.ToProtocolNode(), scopeId, includeDisabled);
        return new(result.Id, Map(result.State));
    }

    private static WidgetFocusTargetState Map(ViewFocusTargetState state) => state switch
    {
        ViewFocusTargetState.Missing => WidgetFocusTargetState.Missing,
        ViewFocusTargetState.NotFocusable => WidgetFocusTargetState.NotFocusable,
        ViewFocusTargetState.Disabled => WidgetFocusTargetState.Disabled,
        ViewFocusTargetState.OutsideActiveScope => WidgetFocusTargetState.OutsideActiveScope,
        ViewFocusTargetState.Duplicate => WidgetFocusTargetState.Duplicate,
        ViewFocusTargetState.Valid => WidgetFocusTargetState.Valid,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static string RootScopeId(WidgetElement root) => root switch
    {
        ResponsiveBranchElement branch => RootScopeId(branch.Child),
        ContainerElement container => container.InputScopeId ?? container.Id,
        _ => root.Id,
    };
}
