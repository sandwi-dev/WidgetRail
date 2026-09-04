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
            if (snapshot.Surface.Appearance != WidgetSurfaceAppearance.Theme)
            {
                Add(
                    "surface-appearance",
                    ProtocolConstants.SurfaceAppearanceVersion,
                    "$.surface.appearance",
                    $"Surface appearance requires protocol version {ProtocolConstants.SurfaceAppearanceVersion} or later.");
            }
        }

        if ((snapshot.PinnedLayouts?.Count ?? 0) != 0)
            Add(
                "pinned-presentation-layouts",
                ProtocolConstants.PinnedPresentationLayoutsVersion,
                "$.pinnedLayouts",
                $"Pinned presentation layouts require protocol version {ProtocolConstants.PinnedPresentationLayoutsVersion} or later.");
        if (snapshot.PinnedLayouts?.Any(layout =>
                layout?.Surface.Appearance != WidgetSurfaceAppearance.Theme) == true)
            Add(
                "surface-appearance",
                ProtocolConstants.SurfaceAppearanceVersion,
                "$.pinnedLayouts",
                $"Surface appearance requires protocol version {ProtocolConstants.SurfaceAppearanceVersion} or later.");
        if (snapshot.PinnedLayouts?.Any(layout => layout?.Root is not null) == true)
            Add(
                "pinned-presentation-projections",
                ProtocolConstants.PinnedPresentationProjectionsVersion,
                "$.pinnedLayouts",
                $"Pinned presentation projections require protocol version {ProtocolConstants.PinnedPresentationProjectionsVersion} or later.");

        if (snapshot.EmbeddedMediaSession is not null)
            Add(
                "embedded-media-session",
                ProtocolConstants.EmbeddedMediaSessionVersion,
                "$.embeddedMediaSession",
                $"Embedded media sessions require protocol version {ProtocolConstants.EmbeddedMediaSessionVersion} or later.");

        Visit(snapshot.Root, "$.root", 1);
        var pinnedLayouts = snapshot.PinnedLayouts ?? [];
        for (var index = 0; index < Math.Min(
                 pinnedLayouts.Count, ProtocolConstants.MaximumPinnedPresentationLayoutCount); index++)
        {
            if (pinnedLayouts[index]?.Root is { } pinnedRoot)
                Visit(pinnedRoot, $"$.pinnedLayouts[{index}].root", 1);
        }

        var quickActions = snapshot.QuickActions ?? [];
        for (var index = 0; index < quickActions.Count; index++)
        {
            if (quickActions[index]?.RepeatPolicy == ControllerActionRepeatPolicy.WhileHeld)
                Add(
                    "held-button-action-repeat",
                    ProtocolConstants.HeldButtonActionRepeatVersion,
                    $"$.quickActions[{index}].repeatPolicy",
                    $"Held-button action repeat requires protocol version {ProtocolConstants.HeldButtonActionRepeatVersion} or later.");
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
            if (node.InitialChildFocusId is not null)
                Add(
                    "remembered-child-focus-group",
                    ProtocolConstants.RememberedChildFocusGroupVersion,
                    $"{path}.initialChildFocusId",
                    $"Remembered-child focus groups require protocol version {ProtocolConstants.RememberedChildFocusGroupVersion} or later.");
            if ((node.ContextActions?.Count ?? 0) != 0)
                Add(
                    "context-actions",
                    ProtocolConstants.ContextActionsVersion,
                    $"{path}.contextActions",
                    $"Context actions require protocol version {ProtocolConstants.ContextActionsVersion} or later.");
            if (node.Kind is not ViewNodeKind.Select &&
                (node.SelectOptions?.Count ?? 0) != 0)
                Add(
                    "anchored-select",
                    ProtocolConstants.AnchoredSelectVersion,
                    path,
                    $"Anchored Select requires protocol version {ProtocolConstants.AnchoredSelectVersion} or later.");
            if (node.ActionSurfacePresentation is not null)
                Add(
                    "poster-tile",
                    ProtocolConstants.PosterTileVersion,
                    $"{path}.actionSurfacePresentation",
                    $"Poster tiles require protocol version {ProtocolConstants.PosterTileVersion} or later.");
            var shortcuts = node.Shortcuts ?? [];
            for (var shortcutIndex = 0; shortcutIndex < shortcuts.Count; shortcutIndex++)
            {
                var shortcut = shortcuts[shortcutIndex];
                if (shortcut?.RepeatPolicy == ControllerActionRepeatPolicy.WhileHeld)
                    Add(
                        "held-button-action-repeat",
                        ProtocolConstants.HeldButtonActionRepeatVersion,
                        $"{path}.shortcuts[{shortcutIndex}].repeatPolicy",
                        $"Held-button action repeat requires protocol version {ProtocolConstants.HeldButtonActionRepeatVersion} or later.");
                if (shortcut?.Label is not null)
                    Add(
                        "controller-shortcut-label",
                        ProtocolConstants.ControllerShortcutLabelVersion,
                        $"{path}.shortcuts[{shortcutIndex}].label",
                        $"Controller shortcut labels require protocol version {ProtocolConstants.ControllerShortcutLabelVersion} or later.");
            }

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
                    if (node.TextEntryInputKind is not null)
                        Add(
                            "sensitive-text-entry",
                            ProtocolConstants.SensitiveTextEntryVersion,
                            $"{path}.textEntryInputKind",
                            $"Sensitive text entry requires protocol version {ProtocolConstants.SensitiveTextEntryVersion} or later.");
                    break;
                case ViewNodeKind.MediaViewport:
                    Add(
                        "media-viewport",
                        ProtocolConstants.MediaViewportVersion,
                        path,
                        $"MediaViewport requires protocol version {ProtocolConstants.MediaViewportVersion} or later.");
                    break;
                case ViewNodeKind.BackgroundSurface:
                    Add(
                        "background-surface",
                        ProtocolConstants.BackgroundSurfaceVersion,
                        path,
                        $"BackgroundSurface requires protocol version {ProtocolConstants.BackgroundSurfaceVersion} or later.");
                    break;
                case ViewNodeKind.FocusPresentationSurface:
                    Add(
                        "focus-associated-presentation",
                        ProtocolConstants.FocusAssociatedPresentationVersion,
                        path,
                        $"Focus-associated presentation requires protocol version {ProtocolConstants.FocusAssociatedPresentationVersion} or later.");
                    break;
                case ViewNodeKind.Select:
                    Add(
                        "anchored-select",
                        ProtocolConstants.AnchoredSelectVersion,
                        path,
                        $"Select requires protocol version {ProtocolConstants.AnchoredSelectVersion} or later.");
                    break;
            }

            if (node.FocusPresentation is not null)
                Add(
                    "focus-associated-presentation",
                    ProtocolConstants.FocusAssociatedPresentationVersion,
                    $"{path}.focusPresentation",
                    $"Focus-associated presentation requires protocol version {ProtocolConstants.FocusAssociatedPresentationVersion} or later.");
            if (node.DefaultFocusPresentation is not null)
                Add(
                    "focus-associated-presentation",
                    ProtocolConstants.FocusAssociatedPresentationVersion,
                    $"{path}.defaultFocusPresentation",
                    $"Focus-associated presentation requires protocol version {ProtocolConstants.FocusAssociatedPresentationVersion} or later.");

            if (node.CollectionItemKey is not null)
                Add(
                    "cursor-collection-item",
                    ProtocolConstants.CursorCollectionVersion,
                    $"{path}.collectionItemKey",
                    $"Cursor collection item keys require protocol version {ProtocolConstants.CursorCollectionVersion} or later.");
            if (node.ArtworkHandle is not null)
                Add(
                    "trusted-encoded-artwork",
                    ProtocolConstants.TrustedEncodedArtworkVersion,
                    $"{path}.artworkHandle",
                    $"Trusted encoded artwork handles require protocol version {ProtocolConstants.TrustedEncodedArtworkVersion} or later.");
            if (node.FocusBackgroundArtworkHandle is not null ||
                node.UsesFocusedDescendantArtwork is true)
                Add(
                    "focused-background-artwork",
                    ProtocolConstants.FocusedBackgroundArtworkVersion,
                    node.FocusBackgroundArtworkHandle is not null
                        ? $"{path}.focusBackgroundArtworkHandle"
                        : $"{path}.usesFocusedDescendantArtwork",
                    $"Focused descendant background artwork requires protocol version {ProtocolConstants.FocusedBackgroundArtworkVersion} or later.");
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
            if (node.Glyph is WidgetGlyph.Rewind or WidgetGlyph.FastForward)
                Add(
                    "semantic-seek-glyph",
                    ProtocolConstants.SemanticSeekGlyphVersion,
                    $"{path}.glyph",
                    $"Rewind and Fast Forward require protocol version {ProtocolConstants.SemanticSeekGlyphVersion} or later.");

            var children = node.Children ?? [];
            if (node.FocusPresentation is not null)
                Visit(node.FocusPresentation, $"{path}.focusPresentation", depth + 1);
            if (node.DefaultFocusPresentation is not null)
                Visit(node.DefaultFocusPresentation, $"{path}.defaultFocusPresentation", depth + 1);
            for (var index = 0; index < children.Count; index++)
                Visit(children[index], $"{path}.children[{index}]", depth + 1);
        }

        void Add(string feature, int version, string path, string message) =>
            requirements.Add(new(feature, version, path, message));
    }
}
