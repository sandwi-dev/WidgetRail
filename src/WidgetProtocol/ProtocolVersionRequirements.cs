namespace WidgetRail.WidgetProtocol;

internal sealed record ProtocolVersionRequirement(
    string Feature,
    int Version,
    string Path,
    string Message);

internal sealed class ProtocolVersionRequirements
{
    private ProtocolVersionRequirements(List<ProtocolVersionRequirement> requirements)
    {
        Requirements = requirements.AsReadOnly();
        RequiredVersion = requirements.Count == 0
            ? ProtocolConstants.BaselineVersion
            : requirements.Max(requirement => requirement.Version);
    }

    public int RequiredVersion { get; }
    public IReadOnlyList<ProtocolVersionRequirement> Requirements { get; }

    public static ProtocolVersionRequirements Calculate(ViewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var requirements = new List<ProtocolVersionRequirement>();
        var nodes = 0;

        if (snapshot.Surface is not null)
        {
            Add(
                "surface-hints",
                ProtocolConstants.SurfaceHintsVersion,
                "$.surface",
                $"Surface hints require protocol version {ProtocolConstants.SurfaceHintsVersion} or later.");
            if (snapshot.Surface.WidthMode != WidgetSurfaceAxisMode.Preferred ||
                snapshot.Surface.HeightMode != WidgetSurfaceAxisMode.Preferred)
            {
                Add(
                    "surface-axis-sizing",
                    ProtocolConstants.SurfaceAxisSizingVersion,
                    "$.surface",
                    $"Surface axis sizing requires protocol version {ProtocolConstants.SurfaceAxisSizingVersion} or later.");
            }
        }

        Visit(snapshot.Root, "$.root", 1);

        var quickActions = snapshot.QuickActions ?? [];
        for (var index = 0; index < quickActions.Count; index++)
        {
            if (quickActions[index]?.Capability is null) continue;
            Add(
                "dashboard-capability-authority",
                ProtocolConstants.DashboardGestureAuthorityVersion,
                $"$.quickActions[{index}].capability",
                $"Dashboard capability authority requires protocol version {ProtocolConstants.DashboardGestureAuthorityVersion} or later.");
        }

        return new(requirements);

        void Visit(ViewNode? node, string path, int depth)
        {
            if (node is null) return;
            nodes++;
            if (nodes > ProtocolConstants.MaximumNodeCount ||
                depth > ProtocolConstants.MaximumTreeDepth)
                return;

            if (node.VisibleWhen is not null)
                Add(
                    "responsive-visibility",
                    ProtocolConstants.ResponsiveVisibilityVersion,
                    $"{path}.visibleWhen",
                    $"Responsive visibility requires protocol version {ProtocolConstants.ResponsiveVisibilityVersion} or later.");
            if (node.FocusPersistenceId is not null)
                Add(
                    "focus-persistence",
                    ProtocolConstants.FocusPersistenceVersion,
                    $"{path}.focusPersistenceId",
                    $"Focus persistence requires protocol version {ProtocolConstants.FocusPersistenceVersion} or later.");

            switch (node.Kind)
            {
                case ViewNodeKind.LoadingIndicator:
                    Add(
                        "loading-indicator",
                        ProtocolConstants.LoadingIndicatorVersion,
                        path,
                        $"LoadingIndicator requires protocol version {ProtocolConstants.LoadingIndicatorVersion} or later.");
                    break;
                case ViewNodeKind.Scroll:
                    Add(
                        "scroll-container",
                        ProtocolConstants.ScrollContainerVersion,
                        path,
                        $"Scroll requires protocol version {ProtocolConstants.ScrollContainerVersion} or later.");
                    if (node.ScrollNearStartActionId is not null ||
                        node.ScrollNearEndActionId is not null ||
                        node.ScrollPaginationThreshold is not null)
                    {
                        Add(
                            "scroll-pagination",
                            ProtocolConstants.ScrollPaginationVersion,
                            path,
                            $"Scroll pagination requires protocol version {ProtocolConstants.ScrollPaginationVersion} or later.");
                    }
                    if (node.CollectionAnchorKey is not null)
                        Add(
                            "cursor-collection-anchor",
                            ProtocolConstants.CursorCollectionVersion,
                            $"{path}.collectionAnchorKey",
                            $"Cursor collections require protocol version {ProtocolConstants.CursorCollectionVersion} or later.");
                    if (node.VirtualCollectionWindow is not null)
                        Add(
                            "virtual-collection-window",
                            ProtocolConstants.VirtualCollectionWindowVersion,
                            $"{path}.virtualCollectionWindow",
                            $"Virtual collection windows require protocol version {ProtocolConstants.VirtualCollectionWindowVersion} or later.");
                    break;
                case ViewNodeKind.Grid:
                    Add(
                        "responsive-grid",
                        ProtocolConstants.ResponsiveGridVersion,
                        path,
                        $"Grid requires protocol version {ProtocolConstants.ResponsiveGridVersion} or later.");
                    break;
                case ViewNodeKind.ActionSurface:
                    Add(
                        "action-surface",
                        ProtocolConstants.ActionSurfaceVersion,
                        path,
                        $"ActionSurface requires protocol version {ProtocolConstants.ActionSurfaceVersion} or later.");
                    break;
                case ViewNodeKind.Slider:
                    Add(
                        "slider",
                        ProtocolConstants.SliderVersion,
                        path,
                        $"Slider requires protocol version {ProtocolConstants.SliderVersion} or later.");
                    if (node.SliderInteractionMode is not null)
                        Add(
                            "slider-interaction-mode",
                            ProtocolConstants.SliderActivationVersion,
                            $"{path}.sliderInteractionMode",
                            $"Slider interaction modes require protocol version {ProtocolConstants.SliderActivationVersion} or later.");
                    break;
                case ViewNodeKind.TextEntry:
                    Add(
                        "text-entry",
                        ProtocolConstants.TextEntryVersion,
                        path,
                        $"Text entry requires protocol version {ProtocolConstants.TextEntryVersion} or later.");
                    break;
            }

            if (node.CollectionItemKey is not null)
                Add(
                    "cursor-collection-item",
                    ProtocolConstants.CursorCollectionVersion,
                    $"{path}.collectionItemKey",
                    $"Cursor collection item keys require protocol version {ProtocolConstants.CursorCollectionVersion} or later.");
            if (node.ArtworkHandle is not null)
                Add(
                    "artwork-handle",
                    ProtocolConstants.CursorCollectionVersion,
                    $"{path}.artworkHandle",
                    $"Opaque artwork handles require protocol version {ProtocolConstants.CursorCollectionVersion} or later.");
            if (ViewSnapshotValidator.IsValidInlinePng(node.ImageSource))
                Add(
                    "inline-png-image",
                    ProtocolConstants.InlinePngImageVersion,
                    $"{path}.imageSource",
                    $"Inline PNG images require protocol version {ProtocolConstants.InlinePngImageVersion} or later.");
            if (node.Glyph == WidgetGlyph.RepeatOne)
                Add(
                    "repeat-one-glyph",
                    ProtocolConstants.RepeatOneGlyphVersion,
                    $"{path}.glyph",
                    $"Repeat One requires protocol version {ProtocolConstants.RepeatOneGlyphVersion} or later.");

            var children = node.Children ?? [];
            for (var index = 0; index < children.Count; index++)
                Visit(children[index], $"{path}.children[{index}]", depth + 1);
        }

        void Add(string feature, int version, string path, string message) =>
            requirements.Add(new(feature, version, path, message));
    }
}
