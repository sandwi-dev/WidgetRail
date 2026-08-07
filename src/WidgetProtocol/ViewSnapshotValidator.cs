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
        var scopeShortcuts = new Dictionary<string, Dictionary<(ControllerButton, ControllerEventPhase), string>>(
            StringComparer.Ordinal);
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
            if (!IsDashboardQuickActionButton(quickAction.Button))
                Add($"$.quickActions[{index}].button", "reserved_button", "This button is reserved for dashboard navigation or host behavior.");
            if (!quickActionButtons.Add(quickAction.Button))
                Add($"$.quickActions[{index}].button", "duplicate_button", "A dashboard button can trigger only one quick action.");
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

            CheckString(node.Text, $"{path}.text");
            CheckString(node.AccessibilityLabel, $"{path}.accessibilityLabel");
            CheckString(node.ActionId, $"{path}.actionId");
            CheckString(node.ImageSource, $"{path}.imageSource");

            var isContainer = node.Kind is ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Scroll;
            if (node.Kind is ViewNodeKind.Scroll)
            {
                if (snapshot.ProtocolVersion < ProtocolConstants.ScrollContainerVersion)
                    Add(path, "feature_requires_version",
                        $"Scroll requires protocol version {ProtocolConstants.ScrollContainerVersion} or later.");
                if (node.ScrollAxis is null)
                    Add($"{path}.scrollAxis", "required", "A scroll container requires an axis.");
                else if (!Enum.IsDefined(node.ScrollAxis.Value))
                    Add($"{path}.scrollAxis", "invalid_scroll_axis", "The scroll axis is not supported.");
            }
            else if (node.ScrollAxis is not null)
            {
                Add($"{path}.scrollAxis", "scroll_axis_not_allowed",
                    "Scroll axis applies only to scroll containers.");
            }
            if (node.InputScopeId is not null)
            {
                CheckIdentifier(node.InputScopeId, $"{path}.inputScopeId", "input scope ID");
                if (!isContainer)
                    Add($"{path}.inputScopeId", "input_scope_not_allowed",
                        "Only stack, row, and scroll containers may start an input scope.");
            }
            var startsScope = depth == 1 || (isContainer && node.InputScopeId is not null);
            var scopeKey = startsScope ? path : inheritedScopeKey;
            if (startsScope)
            {
                var publicScopeId = node.InputScopeId ?? node.Id;
                if (!string.IsNullOrWhiteSpace(publicScopeId) && !inputScopes.TryAdd(publicScopeId, scopeKey))
                    Add($"{path}.inputScopeId", "duplicate_input_scope",
                        $"The input scope ID '{publicScopeId}' is already used.");
                scopeShortcuts.TryAdd(scopeKey, []);
            }
            if (!string.IsNullOrWhiteSpace(node.Id) && !ids.TryAdd(node.Id, (node, path, scopeKey)))
                Add($"{path}.id", "duplicate_id", $"The ID '{node.Id}' is already used.");

            if (node.Kind is ViewNodeKind.Button && string.IsNullOrWhiteSpace(node.ActionId))
                Add($"{path}.actionId", "required", "A button requires an action ID.");
            if (node.Kind is ViewNodeKind.Button &&
                string.IsNullOrWhiteSpace(node.Text) && string.IsNullOrWhiteSpace(node.AccessibilityLabel))
                Add(path, "missing_accessible_name", "A button requires visible text or an accessibility label.");
            if (node.Kind is not ViewNodeKind.Button &&
                (node.IsDisabled is not null || node.IsSelected is not null || node.IsBusy is not null))
                Add(path, "interaction_state_not_allowed", "Disabled, selected, and busy states apply only to buttons.");
            if (node.Kind is ViewNodeKind.Progress &&
                (node.Value is null || node.Maximum is null || node.Maximum <= 0 || node.Value < 0 || node.Value > node.Maximum))
                Add(path, "invalid_progress", "Progress requires 0 <= value <= maximum and maximum > 0.");
            if (node.Kind is ViewNodeKind.Image)
            {
                if (!IsSafeImageSource(node.ImageSource))
                    Add($"{path}.imageSource", "invalid_image_source", "An image requires an absolute HTTPS URL without embedded credentials.");
                if (node.ImageFit is null)
                    Add($"{path}.imageFit", "required", "An image requires a fit mode.");
                else if (!Enum.IsDefined(node.ImageFit.Value))
                    Add($"{path}.imageFit", "invalid_image_fit", "The image fit mode is not supported.");
                if (string.IsNullOrWhiteSpace(node.AccessibilityLabel))
                    Add($"{path}.accessibilityLabel", "required", "An image requires an accessibility label.");
            }
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
            var children = node.Children ?? [];
            var shortcuts = node.Shortcuts ?? [];
            if (node.StyleClasses is null)
                Add($"{path}.styleClasses", "required", "Style classes cannot be null.");
            if (node.Kind is not (ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Scroll) && children.Count != 0)
                Add($"{path}.children", "children_not_allowed", $"{node.Kind} cannot contain children.");
            if (node.Kind is not (ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Scroll or ViewNodeKind.Button) && shortcuts.Count != 0)
                Add($"{path}.shortcuts", "shortcuts_not_allowed",
                    "Only input-scope containers and buttons may declare shortcuts.");

            var shortcutButtons = new HashSet<(ControllerButton, ControllerEventPhase)>();
            for (var index = 0; index < shortcuts.Count; index++)
            {
                var shortcut = shortcuts[index];
                CheckIdentifier(shortcut.ActionId, $"{path}.shortcuts[{index}].actionId", "shortcut action ID");
                if (shortcut.Phase != ControllerEventPhase.Pressed)
                    Add($"{path}.shortcuts[{index}].phase", "unsupported_shortcut_phase",
                        "The MVP host emits only Pressed shortcut events.");
                if (!IsOpenWidgetShortcutButton(shortcut.Button))
                    Add($"{path}.shortcuts[{index}].button", "reserved_shortcut_button",
                        "A and D-pad buttons are reserved for activation and focus navigation.");
                if (!shortcutButtons.Add((shortcut.Button, shortcut.Phase)))
                    Add($"{path}.shortcuts[{index}].button", "duplicate_shortcut",
                        "A node cannot declare the same controller button and phase twice.");
                var bindings = scopeShortcuts[scopeKey];
                var signature = (shortcut.Button, shortcut.Phase);
                if (!bindings.TryAdd(signature, node.Id))
                    Add($"{path}.shortcuts[{index}].button", "ambiguous_scope_shortcut",
                        $"Input scope shortcut {shortcut.Button}/{shortcut.Phase} is already bound by '{bindings[signature]}'.");
            }

            for (var index = 0; index < children.Count; index++)
                Visit(children[index], $"{path}.children[{index}]", depth + 1, scopeKey);
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

    private static bool IsSafeImageSource(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        !string.IsNullOrWhiteSpace(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo);
}
