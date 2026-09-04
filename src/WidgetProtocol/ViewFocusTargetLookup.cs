namespace WidgetRail.WidgetProtocol;

/// <summary>Protocol-accurate classification of a stable focus target.</summary>
internal enum ViewFocusTargetState
{
    Missing,
    NotFocusable,
    Disabled,
    OutsideActiveScope,
    Duplicate,
    Valid,
}

/// <summary>One focus-target lookup result.</summary>
internal readonly record struct ViewFocusTargetResult(string? Id, ViewFocusTargetState State)
{
    public bool IsEnabled => State == ViewFocusTargetState.Valid;
}

/// <summary>
/// Owns the managed, protocol-level focus-target traversal used for validation
/// and SDK route restoration. Responsive visibility remains declarative: this
/// query deliberately does not guess a host viewport.
/// </summary>
internal static class ViewFocusTargetLookup
{
    public static ViewFocusTargetResult First(
        ViewNode root,
        string? activeInputScopeId,
        bool includeDisabled = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        var entries = new List<Entry>();
        var scopes = new Dictionary<string, string>(StringComparer.Ordinal);
        Visit(root, "$.root", "$.root", isRoot: true);
        var activeScope = !string.IsNullOrWhiteSpace(activeInputScopeId) &&
            scopes.TryGetValue(activeInputScopeId, out var scopeKey)
            ? scopeKey
            : null;
        foreach (var entry in entries)
        {
            if (!entry.IsFocusable || (!includeDisabled && entry.IsDisabled) ||
                (activeScope is not null && !string.Equals(entry.ScopeKey, activeScope, StringComparison.Ordinal)))
                continue;
            return new(entry.Id, entry.IsDisabled ? ViewFocusTargetState.Disabled : ViewFocusTargetState.Valid);
        }
        return new(null, ViewFocusTargetState.Missing);

        void Visit(ViewNode node, string path, string inheritedScopeKey, bool isRoot)
        {
            var isContainer = node.Kind is ViewNodeKind.Stack or ViewNodeKind.Row or
                ViewNodeKind.Scroll or ViewNodeKind.Grid;
            var startsScope = isRoot || (isContainer && node.InputScopeId is not null);
            var scopeKey = startsScope ? path : inheritedScopeKey;
            if (startsScope)
            {
                var scopeId = node.InputScopeId ?? node.Id;
                if (!string.IsNullOrWhiteSpace(scopeId) && !scopes.ContainsKey(scopeId))
                    scopes.Add(scopeId, scopeKey);
            }
            entries.Add(new(node.Id, scopeKey, node.IsFocusable, node.IsDisabled == true));
            var children = node.Children ?? [];
            for (var index = 0; index < children.Count; index++)
                Visit(children[index], $"{path}.children[{index}]", scopeKey, isRoot: false);
        }
    }

    public static ViewFocusTargetResult Resolve(
        ViewNode root,
        string? targetId,
        string? activeInputScopeId)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrWhiteSpace(targetId))
            return new(targetId, ViewFocusTargetState.Missing);

        var entries = new List<Entry>();
        var scopes = new Dictionary<string, string>(StringComparer.Ordinal);
        Visit(root, "$.root", "$.root", isRoot: true);
        var matches = entries.Where(entry => string.Equals(entry.Id, targetId, StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0) return new(targetId, ViewFocusTargetState.Missing);
        if (matches.Length != 1) return new(targetId, ViewFocusTargetState.Duplicate);

        var target = matches[0];
        if (!target.IsFocusable) return new(targetId, ViewFocusTargetState.NotFocusable);
        if (!string.IsNullOrWhiteSpace(activeInputScopeId) &&
            scopes.TryGetValue(activeInputScopeId, out var activeScopeKey) &&
            !string.Equals(target.ScopeKey, activeScopeKey, StringComparison.Ordinal))
            return new(targetId, ViewFocusTargetState.OutsideActiveScope);
        if (target.IsDisabled) return new(targetId, ViewFocusTargetState.Disabled);
        return new(targetId, ViewFocusTargetState.Valid);

        void Visit(ViewNode node, string path, string inheritedScopeKey, bool isRoot)
        {
            var isContainer = node.Kind is ViewNodeKind.Stack or ViewNodeKind.Row or
                ViewNodeKind.Scroll or ViewNodeKind.Grid;
            var startsScope = isRoot || (isContainer && node.InputScopeId is not null);
            var scopeKey = startsScope ? path : inheritedScopeKey;
            if (startsScope)
            {
                var scopeId = node.InputScopeId ?? node.Id;
                if (!string.IsNullOrWhiteSpace(scopeId) && !scopes.ContainsKey(scopeId))
                    scopes.Add(scopeId, scopeKey);
            }

            entries.Add(new(node.Id, scopeKey, node.IsFocusable, node.IsDisabled == true));
            var children = node.Children ?? [];
            for (var index = 0; index < children.Count; index++)
                Visit(children[index], $"{path}.children[{index}]", scopeKey, isRoot: false);
        }
    }

    private readonly record struct Entry(string Id, string ScopeKey, bool IsFocusable, bool IsDisabled);
}
