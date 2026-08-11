using System.Text.RegularExpressions;
using System.Buffers.Binary;

namespace GameBarAlternative.WidgetProtocol;

public sealed record ProtocolValidationError(string Path, string Code, string Message);

public static class ViewSnapshotValidator
{
    public static IReadOnlyList<ProtocolValidationError> Validate(ViewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var errors = new List<ProtocolValidationError>();
        var ids = new Dictionary<string, (ViewNode Node, string Path, string ScopeKey)>(StringComparer.Ordinal);
        var inputScopes = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodes = 0;

        if (snapshot.ProtocolVersion < ProtocolConstants.MinimumSupportedVersion ||
            snapshot.ProtocolVersion > ProtocolConstants.CurrentVersion)
            Add("$.protocolVersion", "unsupported_version",
                $"Expected protocol version {ProtocolConstants.MinimumSupportedVersion}-{ProtocolConstants.CurrentVersion}.");
        CheckIdentifier(snapshot.WidgetInstanceId, "$.widgetInstanceId", "widget instance ID");
        ValidateSurfaceHints();
        Visit(snapshot.Root, "$.root", 1, "$.root");
        var activeInputScopeId = snapshot.ActiveInputScopeId ?? string.Empty;
        CheckIdentifier(activeInputScopeId, "$.activeInputScopeId", "active input scope ID");
        var hasActiveScope = inputScopes.TryGetValue(activeInputScopeId, out var activeScopeKey);
        if (!hasActiveScope && !string.IsNullOrWhiteSpace(activeInputScopeId))
            Add("$.activeInputScopeId", "invalid_active_input_scope",
                $"The active input scope '{activeInputScopeId}' does not exist.");

        var quickActions = snapshot.QuickActions ?? [];
        if (snapshot.QuickActions is null)
            Add("$.quickActions", "required", "Quick actions cannot be null.");
        if (quickActions.Count > ProtocolConstants.MaximumQuickActionCount)
            Add("$.quickActions", "too_many", $"A widget may expose at most {ProtocolConstants.MaximumQuickActionCount} dashboard quick actions.");
        var quickActionButtons = new HashSet<ControllerButton>();
        for (var index = 0; index < quickActions.Count; index++)
        {
            var quickAction = quickActions[index];
            if (quickAction is null)
            {
                Add($"$.quickActions[{index}]", "required", "A quick action cannot be null.");
                continue;
            }
            CheckIdentifier(quickAction.ActionId, $"$.quickActions[{index}].actionId", "quick action ID");
            CheckString(quickAction.Label, $"$.quickActions[{index}].label");
            if (string.IsNullOrWhiteSpace(quickAction.Label))
                Add($"$.quickActions[{index}].label", "required", "A quick action requires a visible label.");
            if (!Enum.IsDefined(quickAction.Button))
                Add($"$.quickActions[{index}].button", "invalid_controller_button",
                    "The controller button is not supported.");
            else if (!IsDashboardQuickActionButton(quickAction.Button))
                Add($"$.quickActions[{index}].button", "reserved_button", "This button is reserved for dashboard navigation or host behavior.");
            if (!quickActionButtons.Add(quickAction.Button))
                Add($"$.quickActions[{index}].button", "duplicate_button", "A dashboard button can trigger only one quick action.");
            if (quickAction.Capability is { } capability)
            {
                if (snapshot.ProtocolVersion < ProtocolConstants.DashboardGestureAuthorityVersion)
                    Add($"$.quickActions[{index}].capability", "feature_requires_version",
                        $"Dashboard capability authority requires protocol version {ProtocolConstants.DashboardGestureAuthorityVersion} or later.");
                CheckCapabilityIdentifier(capability.CapabilityId,
                    $"$.quickActions[{index}].capability.capabilityId", "capability ID");
                CheckIdentifier(capability.OperationId,
                    $"$.quickActions[{index}].capability.operationId", "capability operation ID");
            }
        }

        if (snapshot.InitialFocusId is { } initial)
        {
            if (!ids.TryGetValue(initial, out var initialTarget) || !initialTarget.Node.IsFocusable)
                Add("$.initialFocusId", "invalid_focus_target", "Initial focus must name a focusable node.");
            else if (hasActiveScope && !string.Equals(initialTarget.ScopeKey, activeScopeKey, StringComparison.Ordinal))
                Add("$.initialFocusId", "initial_focus_outside_active_scope",
                    "Initial focus must belong to the active input scope.");
        }

        foreach (var (_, entry) in ids)
        {
            var focus = entry.Node.Focus;
            if (focus is null) continue;
            CheckFocus(focus.Up, "up");
            CheckFocus(focus.Down, "down");
            CheckFocus(focus.Left, "left");
            CheckFocus(focus.Right, "right");

            void CheckFocus(string? targetId, string direction)
            {
                if (targetId is null) return;
                if (!ids.TryGetValue(targetId, out var target) || !target.Node.IsFocusable)
                    Add($"{entry.Path}.focus.{direction}", "invalid_focus_target", $"'{targetId}' is not a focusable node.");
                else if (!string.Equals(entry.ScopeKey, target.ScopeKey, StringComparison.Ordinal))
                    Add($"{entry.Path}.focus.{direction}", "cross_input_scope_focus",
                        "Explicit focus neighbors cannot cross input-scope boundaries.");
            }
        }

        return errors;

        void ValidateSurfaceHints()
        {
            if (snapshot.Surface is null) return;
            if (snapshot.ProtocolVersion < ProtocolConstants.SurfaceHintsVersion)
                Add("$.surface", "feature_requires_version",
                    $"Surface hints require protocol version {ProtocolConstants.SurfaceHintsVersion} or later.");
            if (!Enum.IsDefined(snapshot.Surface.Mode))
                Add("$.surface.mode", "invalid_surface_mode", "The surface mode is not supported.");
            CheckPair(snapshot.Surface.PreferredWidth, snapshot.Surface.PreferredHeight,
                "preferred", ProtocolConstants.MinimumSurfaceWidth,
                ProtocolConstants.MaximumSurfaceWidth,
                ProtocolConstants.MinimumSurfaceHeight,
                ProtocolConstants.MaximumSurfaceHeight);
            CheckPair(snapshot.Surface.MinimumWidth, snapshot.Surface.MinimumHeight,
                "minimum", ProtocolConstants.MinimumSurfaceWidth,
                ProtocolConstants.MaximumSurfaceWidth,
                ProtocolConstants.MinimumSurfaceHeight,
                ProtocolConstants.MaximumSurfaceHeight);
            if (snapshot.Surface.PreferredWidth is { } preferredWidth &&
                snapshot.Surface.MinimumWidth is { } minimumWidth &&
                minimumWidth > preferredWidth)
                Add("$.surface.minimumWidth", "surface_minimum_exceeds_preferred",
                    "Minimum width cannot exceed preferred width.");
            if (snapshot.Surface.PreferredHeight is { } preferredHeight &&
                snapshot.Surface.MinimumHeight is { } minimumHeight &&
                minimumHeight > preferredHeight)
                Add("$.surface.minimumHeight", "surface_minimum_exceeds_preferred",
                    "Minimum height cannot exceed preferred height.");
        }

        void CheckPair(
            double? width,
            double? height,
            string label,
            double minimumWidth,
            double maximumWidth,
            double minimumHeight,
            double maximumHeight)
        {
            if (width.HasValue != height.HasValue)
            {
                Add($"$.surface.{label}Width", "incomplete_surface_size",
                    $"The {label} width and height must be supplied together.");
            }
            if (width is { } actualWidth &&
                (!double.IsFinite(actualWidth) || actualWidth < minimumWidth || actualWidth > maximumWidth))
                Add($"$.surface.{label}Width", "invalid_surface_size",
                    $"The {label} width must be finite and between {minimumWidth} and {maximumWidth} DIPs.");
            if (height is { } actualHeight &&
                (!double.IsFinite(actualHeight) || actualHeight < minimumHeight || actualHeight > maximumHeight))
                Add($"$.surface.{label}Height", "invalid_surface_size",
                    $"The {label} height must be finite and between {minimumHeight} and {maximumHeight} DIPs.");
        }

        void Visit(ViewNode? node, string path, int depth, string inheritedScopeKey)
        {
            if (node is null)
            {
                Add(path, "required", "Node is required.");
                return;
            }

            nodes++;
            if (nodes > ProtocolConstants.MaximumNodeCount)
            {
                if (nodes == ProtocolConstants.MaximumNodeCount + 1)
                    Add(path, "tree_too_large", $"A view may contain at most {ProtocolConstants.MaximumNodeCount} nodes.");
                return;
            }

            if (depth > ProtocolConstants.MaximumTreeDepth)
            {
                Add(path, "tree_too_deep", $"A view may be at most {ProtocolConstants.MaximumTreeDepth} levels deep.");
                return;
            }

            CheckIdentifier(node.Id, $"{path}.id", "node ID");
            if (!Enum.IsDefined(node.Kind))
                Add($"{path}.kind", "invalid_node_kind", "The node kind is not supported.");
            if (node.VisibleWhen is { } visibility)
            {
                if (snapshot.ProtocolVersion < ProtocolConstants.ResponsiveVisibilityVersion)
                    Add($"{path}.visibleWhen", "feature_requires_version",
                        $"Responsive visibility requires protocol version {ProtocolConstants.ResponsiveVisibilityVersion} or later.");
                if (!Enum.IsDefined(visibility))
                    Add($"{path}.visibleWhen", "invalid_responsive_visibility",
                        "The responsive visibility mode is not supported.");
                if (depth == 1 && visibility is not ResponsiveVisibility.Always)
                    Add($"{path}.visibleWhen", "conditional_root_not_allowed",
                        "The root must remain visible; apply responsive visibility to one of its descendants.");
            }

            CheckString(node.Text, $"{path}.text");
            CheckString(node.AccessibilityLabel, $"{path}.accessibilityLabel");
            CheckString(node.AccessibilityValue, $"{path}.accessibilityValue");
            CheckString(node.ActionId, $"{path}.actionId");
            CheckString(node.ValueChangedActionId, $"{path}.valueChangedActionId");
            if (node.FocusPersistenceId is not null)
            {
                if (snapshot.ProtocolVersion < ProtocolConstants.FocusPersistenceVersion)
                    Add($"{path}.focusPersistenceId", "feature_requires_version",
                        $"Focus persistence requires protocol version {ProtocolConstants.FocusPersistenceVersion} or later.");
                CheckIdentifier(node.FocusPersistenceId,
                    $"{path}.focusPersistenceId", "focus persistence ID");
                if (!node.IsFocusable)
                    Add($"{path}.focusPersistenceId", "focus_persistence_on_non_focusable_node",
                        "Only focusable nodes may declare focus persistence.");
            }
            if (node.Kind is not (ViewNodeKind.Image or ViewNodeKind.Button) ||
                node.ImageSource is null)
                CheckString(node.ImageSource, $"{path}.imageSource");

            var isContainer = node.Kind is
                ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Scroll or ViewNodeKind.Grid;
            var isActionSurface = node.Kind is ViewNodeKind.ActionSurface;
            if (node.Kind is ViewNodeKind.LoadingIndicator)
            {
                if (snapshot.ProtocolVersion < ProtocolConstants.LoadingIndicatorVersion)
                    Add(path, "feature_requires_version",
                        $"LoadingIndicator requires protocol version {ProtocolConstants.LoadingIndicatorVersion} or later.");
                if (string.IsNullOrWhiteSpace(node.AccessibilityLabel))
                    Add($"{path}.accessibilityLabel", "required",
                        "A loading indicator requires an accessibility label.");
                if (node.IndicatorSize is null)
                    Add($"{path}.indicatorSize", "required",
                        "A loading indicator requires a bounded semantic size.");
                else if (!Enum.IsDefined(node.IndicatorSize.Value))
                    Add($"{path}.indicatorSize", "invalid_loading_indicator_size",
                        "The loading-indicator size is not supported.");
                if (node.Text is not null || node.Value is not null || node.Maximum is not null ||
                    node.ImageSource is not null || node.ImageFit is not null ||
                    node.Glyph is not null || node.Focus is not null)
                    Add(path, "loading_indicator_property_not_allowed",
                        "Loading indicators accept only an ID, accessibility label, and style classes.");
            }
            else if (node.IndicatorSize is not null)
            {
                Add($"{path}.indicatorSize", "loading_indicator_size_not_allowed",
                    "Indicator size applies only to loading indicators.");
            }
            if (node.Kind is ViewNodeKind.Scroll)
            {
                if (snapshot.ProtocolVersion < ProtocolConstants.ScrollContainerVersion)
                    Add(path, "feature_requires_version",
                        $"Scroll requires protocol version {ProtocolConstants.ScrollContainerVersion} or later.");
                if (node.ScrollAxis is null)
                    Add($"{path}.scrollAxis", "required", "A scroll container requires an axis.");
                else if (!Enum.IsDefined(node.ScrollAxis.Value))
                    Add($"{path}.scrollAxis", "invalid_scroll_axis", "The scroll axis is not supported.");
                var hasPagination = node.ScrollNearStartActionId is not null ||
                    node.ScrollNearEndActionId is not null ||
                    node.ScrollPaginationThreshold is not null;
                if (hasPagination)
                {
                    if (snapshot.ProtocolVersion < ProtocolConstants.ScrollPaginationVersion)
                        Add(path, "feature_requires_version",
                            $"Scroll pagination requires protocol version {ProtocolConstants.ScrollPaginationVersion} or later.");
                    if (node.ScrollNearStartActionId is not null)
                        CheckIdentifier(node.ScrollNearStartActionId,
                            $"{path}.scrollNearStartActionId", "scroll near-start action ID");
                    if (node.ScrollNearEndActionId is not null)
                        CheckIdentifier(node.ScrollNearEndActionId,
                            $"{path}.scrollNearEndActionId", "scroll near-end action ID");
                    if (node.ScrollNearStartActionId is null && node.ScrollNearEndActionId is null)
                        Add(path, "scroll_pagination_action_required",
                            "Scroll pagination requires at least one boundary action.");
                    if (node.ScrollPaginationThreshold is not { } threshold ||
                        threshold is < 1 or > ProtocolConstants.MaximumScrollPaginationThreshold)
                        Add($"{path}.scrollPaginationThreshold", "invalid_scroll_pagination_threshold",
                            $"Scroll pagination threshold must be between 1 and {ProtocolConstants.MaximumScrollPaginationThreshold}.");
                }
                if (node.CollectionAnchorKey is not null)
                {
                    if (snapshot.ProtocolVersion < ProtocolConstants.CursorCollectionVersion)
                        Add($"{path}.collectionAnchorKey", "feature_requires_version",
                            $"Cursor collections require protocol version {ProtocolConstants.CursorCollectionVersion} or later.");
                    CheckIdentifier(node.CollectionAnchorKey,
                        $"{path}.collectionAnchorKey", "collection anchor key");
                    var itemKeys = new HashSet<string>(StringComparer.Ordinal);
                    var itemCount = 0;
                    CollectCollectionItems(node, path);
                    if (itemCount > ProtocolConstants.MaximumCursorCollectionItems)
                        Add(path, "too_many_collection_items",
                            $"A cursor collection may serialize at most {ProtocolConstants.MaximumCursorCollectionItems} retained items.");
                    if (!itemKeys.Contains(node.CollectionAnchorKey))
                        Add($"{path}.collectionAnchorKey", "missing_collection_anchor",
                            "The collection anchor must name one retained item key.");

                    void CollectCollectionItems(ViewNode current, string currentPath)
                    {
                        if (!ReferenceEquals(current, node) && current.Kind is ViewNodeKind.Scroll)
                            return;
                        if (current.CollectionItemKey is { } key)
                        {
                            itemCount++;
                            if (!itemKeys.Add(key))
                                Add($"{currentPath}.collectionItemKey", "duplicate_collection_item_key",
                                    $"The collection item key '{key}' is repeated.");
                            return;
                        }
                        var currentChildren = current.Children ?? [];
                        for (var childIndex = 0; childIndex < currentChildren.Count; childIndex++)
                            CollectCollectionItems(currentChildren[childIndex],
                                $"{currentPath}.children[{childIndex}]");
                    }
                }
                else if (ContainsCollectionItem(node, isRoot: true))
                {
                    Add($"{path}.collectionAnchorKey", "collection_anchor_required",
                        "A non-empty keyed cursor collection requires one retained anchor.");
                }

                static bool ContainsCollectionItem(ViewNode current, bool isRoot)
                {
                    if (!isRoot && current.Kind is ViewNodeKind.Scroll) return false;
                    if (current.CollectionItemKey is not null) return true;
                    return (current.Children ?? []).Any(child =>
                        ContainsCollectionItem(child, isRoot: false));
                }
            }
            else if (node.ScrollAxis is not null || node.ScrollNearStartActionId is not null ||
                     node.ScrollNearEndActionId is not null ||
                     node.ScrollPaginationThreshold is not null ||
                     node.CollectionAnchorKey is not null)
            {
                Add(path, "scroll_property_not_allowed",
                    "Scroll properties apply only to scroll containers.");
            }
            if (node.CollectionItemKey is not null)
            {
                if (snapshot.ProtocolVersion < ProtocolConstants.CursorCollectionVersion)
                    Add($"{path}.collectionItemKey", "feature_requires_version",
                        $"Cursor collection item keys require protocol version {ProtocolConstants.CursorCollectionVersion} or later.");
                CheckIdentifier(node.CollectionItemKey,
                    $"{path}.collectionItemKey", "collection item key");
            }
            if (node.Kind is ViewNodeKind.Grid)
            {
                if (snapshot.ProtocolVersion < ProtocolConstants.ResponsiveGridVersion)
                    Add(path, "feature_requires_version",
                        $"Grid requires protocol version {ProtocolConstants.ResponsiveGridVersion} or later.");
                if (node.GridMinimumColumnWidth is not { } minimumColumnWidth ||
                    !double.IsFinite(minimumColumnWidth) ||
                    minimumColumnWidth < ProtocolConstants.MinimumGridColumnWidth ||
                    minimumColumnWidth > ProtocolConstants.MaximumGridColumnWidth)
                    Add($"{path}.gridMinimumColumnWidth", "invalid_grid_minimum_column_width",
                        $"Grid minimum column width must be finite and between {ProtocolConstants.MinimumGridColumnWidth} and {ProtocolConstants.MaximumGridColumnWidth} DIPs.");
                if (node.GridMaximumColumns is { } maximumColumns &&
                    maximumColumns is < 1 or > ProtocolConstants.MaximumGridColumns)
                    Add($"{path}.gridMaximumColumns", "invalid_grid_maximum_columns",
                        $"Grid maximum columns must be between 1 and {ProtocolConstants.MaximumGridColumns}.");
            }
            else if (node.GridMinimumColumnWidth is not null || node.GridMaximumColumns is not null)
            {
                Add(path, "grid_property_not_allowed",
                    "Grid column properties apply only to responsive Grid nodes.");
            }
            if (isActionSurface)
            {
                if (snapshot.ProtocolVersion < ProtocolConstants.ActionSurfaceVersion)
                    Add(path, "feature_requires_version",
                        $"ActionSurface requires protocol version {ProtocolConstants.ActionSurfaceVersion} or later.");
                if (node.ActionSurfaceOrientation is null)
                    Add($"{path}.actionSurfaceOrientation", "required",
                        "An action surface requires a bounded content orientation.");
                else if (!Enum.IsDefined(node.ActionSurfaceOrientation.Value))
                    Add($"{path}.actionSurfaceOrientation", "invalid_action_surface_orientation",
                        "The action-surface orientation is not supported.");
                if (string.IsNullOrWhiteSpace(node.ActionId))
                    Add($"{path}.actionId", "required", "An action surface requires an action ID.");
                if (string.IsNullOrWhiteSpace(node.AccessibilityLabel))
                    Add($"{path}.accessibilityLabel", "required",
                        "An action surface requires an accessibility label independent of its visual content.");
                if (node.Text is not null || node.AccessibilityValue is not null ||
                    node.Value is not null || node.Minimum is not null || node.Maximum is not null ||
                    node.Step is not null || node.ValueChangedActionId is not null ||
                    node.ImageSource is not null || node.ImageFit is not null ||
                    node.Glyph is not null || node.IndicatorSize is not null ||
                    node.InputScopeId is not null || node.ScrollAxis is not null ||
                    node.GridMinimumColumnWidth is not null || node.GridMaximumColumns is not null)
                    Add(path, "action_surface_property_not_allowed",
                        "Action surfaces accept interaction metadata, orientation, style classes, shortcuts, and bounded presentational children only.");
            }
            else if (node.ActionSurfaceOrientation is not null)
            {
                Add($"{path}.actionSurfaceOrientation", "action_surface_orientation_not_allowed",
                    "Action-surface orientation applies only to action surfaces.");
            }
            if (node.InputScopeId is not null)
            {
                CheckIdentifier(node.InputScopeId, $"{path}.inputScopeId", "input scope ID");
                if (!isContainer)
                    Add($"{path}.inputScopeId", "input_scope_not_allowed",
                        "Only stack, row, scroll, and grid containers may start an input scope.");
            }
            var startsScope = depth == 1 || (isContainer && node.InputScopeId is not null);
            var scopeKey = startsScope ? path : inheritedScopeKey;
            if (startsScope)
            {
                var publicScopeId = node.InputScopeId ?? node.Id;
                if (!string.IsNullOrWhiteSpace(publicScopeId) && !inputScopes.TryAdd(publicScopeId, scopeKey))
                    Add($"{path}.inputScopeId", "duplicate_input_scope",
                        $"The input scope ID '{publicScopeId}' is already used.");
            }
            if (!string.IsNullOrWhiteSpace(node.Id) && !ids.TryAdd(node.Id, (node, path, scopeKey)))
                Add($"{path}.id", "duplicate_id", $"The ID '{node.Id}' is already used.");

            if (node.Kind is ViewNodeKind.Button && string.IsNullOrWhiteSpace(node.ActionId))
                Add($"{path}.actionId", "required", "A button requires an action ID.");
            if (node.Kind is ViewNodeKind.Button &&
                string.IsNullOrWhiteSpace(node.Text) && string.IsNullOrWhiteSpace(node.AccessibilityLabel))
                Add(path, "missing_accessible_name", "A button requires visible text or an accessibility label.");
            if (node.Kind is not (ViewNodeKind.Button or ViewNodeKind.Slider or ViewNodeKind.ActionSurface) &&
                (node.IsDisabled is not null || node.IsSelected is not null || node.IsBusy is not null))
                Add(path, "interaction_state_not_allowed",
                    "Interaction states apply only to buttons, sliders, and action surfaces.");
            if (node.Kind is ViewNodeKind.Slider && node.IsSelected is not null)
                Add($"{path}.isSelected", "interaction_state_not_allowed", "Selected state does not apply to sliders.");
            if (node.Kind is ViewNodeKind.Progress &&
                (node.Value is null || node.Maximum is null ||
                 !double.IsFinite(node.Value.Value) || !double.IsFinite(node.Maximum.Value) ||
                 node.Maximum <= 0 || node.Value < 0 || node.Value > node.Maximum))
                Add(path, "invalid_progress", "Progress requires 0 <= value <= maximum and maximum > 0.");
            if (node.Kind is ViewNodeKind.Slider)
            {
                if (snapshot.ProtocolVersion < ProtocolConstants.SliderVersion)
                    Add(path, "feature_requires_version",
                        $"Slider requires protocol version {ProtocolConstants.SliderVersion} or later.");
                var minimum = node.Minimum ?? double.NaN;
                var maximum = node.Maximum ?? double.NaN;
                var value = node.Value ?? double.NaN;
                var step = node.Step ?? double.NaN;
                var hasCompleteFiniteRange =
                    node.Minimum is not null && node.Maximum is not null &&
                    node.Value is not null && node.Step is not null &&
                    double.IsFinite(minimum) && double.IsFinite(maximum) &&
                    double.IsFinite(value) && double.IsFinite(step);
                var range = hasCompleteFiniteRange ? maximum - minimum : double.NaN;
                if (!hasCompleteFiniteRange || !double.IsFinite(range) || range <= 0 ||
                    value < minimum || value > maximum || step <= 0 || step > range)
                    Add(path, "invalid_slider_range",
                        "Slider requires a finite positive range, an in-range value, and 0 < step <= range.");
                CheckIdentifier(node.ValueChangedActionId, $"{path}.valueChangedActionId", "slider value-changed action ID");
                if (node.ActionId is not null)
                    CheckIdentifier(node.ActionId, $"{path}.actionId", "slider activation action ID");
                if (node.SliderInteractionMode is { } interactionMode)
                {
                    if (snapshot.ProtocolVersion < ProtocolConstants.SliderActivationVersion)
                        Add($"{path}.sliderInteractionMode", "feature_requires_version",
                            $"Slider interaction modes require protocol version {ProtocolConstants.SliderActivationVersion} or later.");
                    if (!Enum.IsDefined(interactionMode))
                        Add($"{path}.sliderInteractionMode", "invalid_slider_interaction_mode",
                            "The slider interaction mode is not supported.");
                    if (interactionMode is SliderInteractionMode.ActivateToAdjust &&
                        node.ActionId is not null)
                        Add($"{path}.actionId", "slider_activation_action_conflict",
                            "Activation-first sliders reserve A for entering and leaving adjustment mode.");
                }
                if (string.IsNullOrWhiteSpace(node.AccessibilityLabel))
                    Add($"{path}.accessibilityLabel", "required", "A slider requires an accessibility label.");
                if (string.IsNullOrWhiteSpace(node.AccessibilityValue))
                    Add($"{path}.accessibilityValue", "required", "A slider requires an accessible value.");
                if (node.SliderInteractionMode is not SliderInteractionMode.ActivateToAdjust &&
                    (node.Focus?.Left is not null || node.Focus?.Right is not null))
                    Add($"{path}.focus", "slider_horizontal_focus_not_allowed",
                        "A slider owns Left and Right for value adjustment; use only Up and Down focus neighbors.");
            }
            else
            {
                if (node.Minimum is not null || node.Step is not null ||
                    node.ValueChangedActionId is not null ||
                    node.AccessibilityValue is not null ||
                    node.SliderInteractionMode is not null)
                    Add(path, "slider_property_not_allowed",
                        "Minimum, step, value-change action, accessible value, and interaction mode apply only to sliders.");
            }
            if (node.Kind is not (ViewNodeKind.Button or ViewNodeKind.Slider or ViewNodeKind.ActionSurface) &&
                node.ActionId is not null)
                Add($"{path}.actionId", "action_not_allowed",
                    "Action IDs apply only to buttons, sliders, and action surfaces.");
            var supportsImageSource = node.Kind is ViewNodeKind.Image or ViewNodeKind.Button;
            if (node.ArtworkHandle is not null)
            {
                if (snapshot.ProtocolVersion < ProtocolConstants.CursorCollectionVersion)
                    Add($"{path}.artworkHandle", "feature_requires_version",
                        $"Opaque artwork handles require protocol version {ProtocolConstants.CursorCollectionVersion} or later.");
                if (!supportsImageSource)
                    Add($"{path}.artworkHandle", "artwork_handle_not_allowed",
                        "Opaque artwork handles apply only to image and button nodes.");
                CheckIdentifier(node.ArtworkHandle, $"{path}.artworkHandle", "artwork handle");
                if (node.ImageSource is not null)
                    Add(path, "multiple_artwork_sources",
                        "A node may use an image source or an opaque artwork handle, not both.");
                if (node.ImageFit is null)
                    Add($"{path}.imageFit", "required", "Artwork requires a fit mode.");
                else if (!Enum.IsDefined(node.ImageFit.Value))
                    Add($"{path}.imageFit", "invalid_image_fit", "The image fit mode is not supported.");
                if (node.Kind is ViewNodeKind.Image && string.IsNullOrWhiteSpace(node.AccessibilityLabel))
                    Add($"{path}.accessibilityLabel", "required", "An image requires an accessibility label.");
            }
            if (node.ImageSource is not null && supportsImageSource)
            {
                var imageSource = ValidateImageSource(node.ImageSource);
                if (imageSource == ImageSourceKind.Invalid)
                    Add($"{path}.imageSource", "invalid_image_source",
                        "Artwork requires an absolute HTTPS URL without credentials or a bounded canonical PNG data source.");
                else if (imageSource == ImageSourceKind.InlinePng &&
                    snapshot.ProtocolVersion < ProtocolConstants.InlinePngImageVersion)
                    Add($"{path}.imageSource", "feature_requires_version",
                        $"Inline PNG images require protocol version {ProtocolConstants.InlinePngImageVersion} or later.");
                if (node.ImageFit is null)
                    Add($"{path}.imageFit", "required", "An image requires a fit mode.");
                else if (!Enum.IsDefined(node.ImageFit.Value))
                    Add($"{path}.imageFit", "invalid_image_fit", "The image fit mode is not supported.");
                if (node.Kind is ViewNodeKind.Image &&
                    string.IsNullOrWhiteSpace(node.AccessibilityLabel))
                    Add($"{path}.accessibilityLabel", "required", "An image requires an accessibility label.");
            }
            else if (node.Kind is ViewNodeKind.Image && node.ArtworkHandle is null)
            {
                Add($"{path}.imageSource", "required",
                    "An image requires an image source or opaque artwork handle.");
            }
            else if (!supportsImageSource &&
                (node.ImageSource is not null || node.ImageFit is not null || node.ArtworkHandle is not null))
            {
                Add(path, "image_property_not_allowed",
                    "Image source and fit apply only to images and buttons with leading artwork.");
            }
            else if (node.Kind is ViewNodeKind.Button && node.ImageFit is not null && node.ArtworkHandle is null)
            {
                Add($"{path}.imageFit", "image_fit_without_source",
                    "A button image fit requires a leading image source.");
            }
            if (node.Kind is ViewNodeKind.Button &&
                (node.ImageSource is not null || node.ArtworkHandle is not null) && node.Glyph is not null)
                Add(path, "multiple_leading_visuals",
                    "A button may use either one semantic glyph or one leading image, not both.");
            if (node.Glyph is not null && node.Kind is not (ViewNodeKind.Icon or ViewNodeKind.Button))
                Add($"{path}.glyph", "glyph_not_allowed", "Semantic glyphs apply only to icon and button nodes.");
            if (node.Kind is ViewNodeKind.Icon)
            {
                if (node.Glyph is null)
                    Add($"{path}.glyph", "required", "An icon requires a semantic glyph.");
                else if (!Enum.IsDefined(node.Glyph.Value))
                    Add($"{path}.glyph", "invalid_glyph", "The semantic glyph is not supported.");
                if (string.IsNullOrWhiteSpace(node.AccessibilityLabel))
                    Add($"{path}.accessibilityLabel", "required", "An icon requires an accessibility label.");
            }
            if (node.Kind is ViewNodeKind.Button && node.Glyph is not null && !Enum.IsDefined(node.Glyph.Value))
                Add($"{path}.glyph", "invalid_glyph", "The semantic glyph is not supported.");
            if (node.Glyph == WidgetGlyph.RepeatOne &&
                snapshot.ProtocolVersion < ProtocolConstants.RepeatOneGlyphVersion)
                Add($"{path}.glyph", "feature_requires_version",
                    $"Repeat One requires protocol version {ProtocolConstants.RepeatOneGlyphVersion} or later.");
            var children = node.Children ?? [];
            var shortcuts = node.Shortcuts ?? [];
            var styleClasses = node.StyleClasses ?? [];
            if (node.Children is null)
                Add($"{path}.children", "required", "Children cannot be null.");
            if (node.StyleClasses is null)
                Add($"{path}.styleClasses", "required", "Style classes cannot be null.");
            else
            {
                if (styleClasses.Count > ProtocolConstants.MaximumStyleClassCount)
                    Add($"{path}.styleClasses", "too_many_style_classes",
                        $"A node may declare at most {ProtocolConstants.MaximumStyleClassCount} style classes.");

                var seenStyleClasses = new HashSet<string>(StringComparer.Ordinal);
                var classesToValidate = Math.Min(styleClasses.Count, ProtocolConstants.MaximumStyleClassCount);
                for (var index = 0; index < classesToValidate; index++)
                {
                    var className = styleClasses[index];
                    var classPath = $"{path}.styleClasses[{index}]";
                    if (className is null)
                    {
                        Add(classPath, "required", "A style class cannot be null.");
                        continue;
                    }
                    if (className.Length > ProtocolConstants.MaximumStyleClassLength)
                        Add(classPath, "style_class_too_long",
                            $"A style class may not exceed {ProtocolConstants.MaximumStyleClassLength} characters.");
                    else if (!StyleClassContract.IsValidIdentifier(className))
                        Add(classPath, "invalid_style_class",
                            "A style class must start with an ASCII letter or '_' and then contain only ASCII letters, digits, '_' or '-'.");
                    if (!seenStyleClasses.Add(className))
                        Add(classPath, "duplicate_style_class", $"The style class '{className}' is repeated.");
                }
            }
            if (node.Kind is not (ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Scroll or
                ViewNodeKind.ActionSurface or ViewNodeKind.Grid) &&
                children.Count != 0)
                Add($"{path}.children", "children_not_allowed", $"{node.Kind} cannot contain children.");
            if (node.Kind is not (ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Scroll or ViewNodeKind.Grid or
                ViewNodeKind.Button or ViewNodeKind.ActionSurface) && shortcuts.Count != 0)
                Add($"{path}.shortcuts", "shortcuts_not_allowed",
                    "Only input-scope containers, buttons, and action surfaces may declare shortcuts.");

            if (isActionSurface)
            {
                if (children.Count == 0)
                    Add($"{path}.children", "required",
                        "An action surface requires at least one presentational child.");
                if (children.Count > ProtocolConstants.MaximumActionSurfaceDirectChildren)
                    Add($"{path}.children", "too_many_action_surface_children",
                        $"An action surface may contain at most {ProtocolConstants.MaximumActionSurfaceDirectChildren} direct children.");
                var descendantCount = 0;
                for (var index = 0; index < children.Count; index++)
                    ValidateActionSurfaceContent(
                        children[index], $"{path}.children[{index}]", 1, ref descendantCount);
            }

            var shortcutButtons = new HashSet<(ControllerButton, ControllerEventPhase)>();
            for (var index = 0; index < shortcuts.Count; index++)
            {
                var shortcut = shortcuts[index];
                CheckIdentifier(shortcut.ActionId, $"{path}.shortcuts[{index}].actionId", "shortcut action ID");
                if (!Enum.IsDefined(shortcut.Phase))
                    Add($"{path}.shortcuts[{index}].phase", "invalid_controller_phase",
                        "The controller event phase is not supported.");
                else if (shortcut.Phase != ControllerEventPhase.Pressed)
                    Add($"{path}.shortcuts[{index}].phase", "unsupported_shortcut_phase",
                        "The MVP host emits only Pressed shortcut events.");
                if (!Enum.IsDefined(shortcut.Button))
                    Add($"{path}.shortcuts[{index}].button", "invalid_controller_button",
                        "The controller button is not supported.");
                else if (!IsOpenWidgetShortcutButton(shortcut.Button))
                    Add($"{path}.shortcuts[{index}].button", "reserved_shortcut_button",
                        "A and D-pad buttons are reserved for activation and focus navigation.");
                if (!shortcutButtons.Add((shortcut.Button, shortcut.Phase)))
                    Add($"{path}.shortcuts[{index}].button", "duplicate_shortcut",
                        "A node cannot declare the same controller button and phase twice.");
            }

            for (var index = 0; index < children.Count; index++)
                Visit(children[index], $"{path}.children[{index}]", depth + 1, scopeKey);
        }

        void ValidateActionSurfaceContent(
            ViewNode? child,
            string path,
            int relativeDepth,
            ref int descendantCount)
        {
            if (child is null) return;
            descendantCount++;
            if (descendantCount == ProtocolConstants.MaximumActionSurfaceDescendants + 1)
                Add(path, "action_surface_too_large",
                    $"An action surface may contain at most {ProtocolConstants.MaximumActionSurfaceDescendants} descendants.");
            if (relativeDepth > ProtocolConstants.MaximumActionSurfaceRelativeDepth)
                Add(path, "action_surface_too_deep",
                    $"Action-surface content may be at most {ProtocolConstants.MaximumActionSurfaceRelativeDepth} levels deep relative to its surface.");
            if (child.Kind is ViewNodeKind.Button or ViewNodeKind.Slider or
                ViewNodeKind.ActionSurface or ViewNodeKind.Scroll ||
                child.ActionId is not null || child.ValueChangedActionId is not null ||
                child.FocusPersistenceId is not null || child.Focus is not null ||
                child.InputScopeId is not null ||
                (child.Shortcuts?.Count ?? 0) != 0 ||
                child.IsDisabled is not null || child.IsSelected is not null || child.IsBusy is not null)
            {
                Add(path, "interactive_action_surface_descendant",
                    "Action-surface descendants must be presentational; nested focus, actions, scopes, scrolling, shortcuts, or interaction state are not allowed.");
            }
            foreach (var (descendant, index) in (child.Children ?? []).Select((item, index) => (item, index)))
                ValidateActionSurfaceContent(
                    descendant, $"{path}.children[{index}]", relativeDepth + 1,
                    ref descendantCount);
        }

        void CheckIdentifier(string? value, string path, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                Add(path, "required", $"The {label} is required.");
            else if (value.Length > 128)
                Add(path, "too_long", $"The {label} may not exceed 128 characters.");
            else if (!value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'))
                Add(path, "invalid_identifier", $"The {label} may contain only ASCII letters, digits, '.', '-' and '_'.");
        }

        void CheckCapabilityIdentifier(string? value, string path, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                Add(path, "required", $"The {label} is required.");
            else if (value.Length > ProtocolConstants.MaximumCapabilityIdLength)
                Add(path, "too_long",
                    $"The {label} may not exceed {ProtocolConstants.MaximumCapabilityIdLength} characters.");
            else if (!Regex.IsMatch(value,
                "^[a-z](?:[a-z0-9-]*[a-z0-9])?(?:\\.[a-z](?:[a-z0-9-]*[a-z0-9])?)*(?::[A-Za-z0-9._-]+)?$",
                RegexOptions.CultureInvariant))
                Add(path, "invalid_identifier",
                    $"The {label} must use the bounded manifest capability syntax.");
        }

        void CheckString(string? value, string path)
        {
            if (value?.Length > ProtocolConstants.MaximumStringLength)
                Add(path, "too_long", $"Text may not exceed {ProtocolConstants.MaximumStringLength} characters.");
        }

        void Add(string path, string code, string message) => errors.Add(new(path, code, message));
    }

    private static bool IsDashboardQuickActionButton(ControllerButton button) => button is
        ControllerButton.X or
        ControllerButton.LeftBumper or ControllerButton.RightBumper or
        ControllerButton.LeftTrigger or ControllerButton.RightTrigger or
        ControllerButton.LeftStick or ControllerButton.RightStick or
        ControllerButton.Menu or ControllerButton.View;

    private static bool IsOpenWidgetShortcutButton(ControllerButton button) => button is not (
        ControllerButton.A or
        ControllerButton.DPadUp or ControllerButton.DPadDown or
        ControllerButton.DPadLeft or ControllerButton.DPadRight);

    private enum ImageSourceKind { Invalid, Https, InlinePng }

    private static ImageSourceKind ValidateImageSource(string? source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            !string.IsNullOrWhiteSpace(uri.Host) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            source!.Length <= ProtocolConstants.MaximumStringLength)
            return ImageSourceKind.Https;
        return IsValidInlinePng(source) ? ImageSourceKind.InlinePng : ImageSourceKind.Invalid;
    }

    private static bool IsValidInlinePng(string? source)
    {
        const string prefix = "data:image/png;base64,";
        if (string.IsNullOrEmpty(source) ||
            source.Length > ProtocolConstants.MaximumInlinePngSourceLength ||
            !source.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        byte[] png;
        try
        {
            png = Convert.FromBase64String(source[prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (png.Length is < 45 or > ProtocolConstants.MaximumInlinePngBytes ||
            !source.AsSpan(prefix.Length).SequenceEqual(Convert.ToBase64String(png)) ||
            !png.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(8, 4)) != 13 ||
            !png.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return false;
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        if (width is < 1 or > ProtocolConstants.MaximumInlinePngDimension ||
            height is < 1 or > ProtocolConstants.MaximumInlinePngDimension ||
            png[24] != 8 || png[25] != 6 || png[26] != 0 || png[27] != 0 ||
            png[28] != 0)
            return false;
        var offset = 8;
        var sawHeader = false;
        var sawImageData = false;
        while (offset <= png.Length - 12)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (length < 0 || length > png.Length - offset - 12) return false;
            var type = png.AsSpan(offset + 4, 4);
            if (!sawHeader)
            {
                if (!type.SequenceEqual("IHDR"u8) || length != 13) return false;
                sawHeader = true;
            }
            else if (type.SequenceEqual("IHDR"u8))
            {
                return false;
            }
            if (type.SequenceEqual("IDAT"u8)) sawImageData = true;
            offset += 12 + length;
            if (type.SequenceEqual("IEND"u8))
                return length == 0 && sawImageData && offset == png.Length;
        }
        return false;
    }
}
