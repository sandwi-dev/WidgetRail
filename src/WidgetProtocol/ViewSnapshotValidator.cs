using System.Text.RegularExpressions;
using System.Buffers.Binary;

namespace WidgetRail.WidgetProtocol;

public sealed record ProtocolValidationError(string Path, string Code, string Message)
{
    public ProtocolValidationIdentifierContext? IdentifierContext { get; init; }
}

public enum ProtocolValidationIdentifierKind
{
    InitialFocus,
    ReturnFocus,
    ElementReference,
    Action,
    ContextAction,
}

public enum ProtocolValidationIdentifierState
{
    Missing,
    NotFocusable,
    Disabled,
    OutsideActiveScope,
    Duplicate,
    UnknownAction,
    UnsafeValue,
}

public sealed record ProtocolValidationIdentifierContext(
    ProtocolValidationIdentifierKind FieldKind,
    ProtocolValidationIdentifierState State,
    string? Identifier)
{
    public const int MaximumIdentifierLength = 128;

    public static ProtocolValidationIdentifierContext Create(
        ProtocolValidationIdentifierKind fieldKind,
        ProtocolValidationIdentifierState state,
        string? identifier)
    {
        if (identifier is null)
            return new(fieldKind, state, null);
        return IsSafeIdentifier(identifier)
            ? new(fieldKind, state, identifier)
            : new(fieldKind, ProtocolValidationIdentifierState.UnsafeValue, null);
    }

    public static bool IsSafeIdentifier(string value) =>
        value.Length is > 0 and <= MaximumIdentifierLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '.' or '-' or '_');
}

public static class ViewSnapshotValidator
{
    public static IReadOnlyList<ProtocolValidationError> Validate(ViewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Validate(snapshot, ProtocolVersionRequirements.Calculate(snapshot));
    }

    internal static IReadOnlyList<ProtocolValidationError> Validate(
        ViewSnapshot snapshot,
        ProtocolVersionRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(requirements);
        var errors = new List<ProtocolValidationError>();
        var ids = new Dictionary<string, (ViewNode Node, string Path, string ScopeKey)>(StringComparer.Ordinal);
        var inputScopes = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodes = 0;
        var mediaViewportCount = 0;
        string? mediaViewportSurfaceId = null;

        if (snapshot.ProtocolVersion < ProtocolConstants.MinimumSupportedVersion ||
            snapshot.ProtocolVersion > ProtocolConstants.CurrentVersion)
            Add("$.protocolVersion", "unsupported_version",
                $"Expected protocol version {ProtocolConstants.MinimumSupportedVersion}-{ProtocolConstants.CurrentVersion}.");
        foreach (var requirement in requirements.Requirements)
        {
            if (snapshot.ProtocolVersion < requirement.Version)
                Add(requirement.Path, "feature_requires_version", requirement.Message);
        }
        CheckIdentifier(snapshot.WidgetInstanceId, "$.widgetInstanceId", "widget instance ID");
        ValidateSurfaceHints(snapshot.Surface, "$.surface");
        ValidateEmbeddedMedia(snapshot.EmbeddedMedia);
        var pinnedLayouts = snapshot.PinnedLayouts ?? [];
        if (snapshot.PinnedLayouts is null)
            Add("$.pinnedLayouts", "required", "Pinned layouts cannot be null.");
        if (pinnedLayouts.Count > ProtocolConstants.MaximumPinnedPresentationLayoutCount)
            Add("$.pinnedLayouts", "too_many",
                $"A widget may expose at most {ProtocolConstants.MaximumPinnedPresentationLayoutCount} pinned layouts.");
        var pinnedLayoutIds = new HashSet<string>(StringComparer.Ordinal);
        var aggregateNodes = 0;
        var aggregateStrings = 0;
        var aggregateResources = 0;
        var hasPinnedProjection = false;
        Measure(snapshot.Root);
        for (var index = 0; index < pinnedLayouts.Count; index++)
        {
            var layout = pinnedLayouts[index];
            var path = $"$.pinnedLayouts[{index}]";
            if (layout is null)
            {
                Add(path, "required", "A pinned layout cannot be null.");
                continue;
            }
            CheckIdentifier(layout.Id, $"{path}.id", "pinned layout ID");
            if (!string.IsNullOrWhiteSpace(layout.Id) && !pinnedLayoutIds.Add(layout.Id))
                Add($"{path}.id", "duplicate_identifier", "Pinned layout IDs must be unique.");
            CheckString(layout.Name, $"{path}.name");
            if (string.IsNullOrWhiteSpace(layout.Name))
                Add($"{path}.name", "required", "A pinned layout requires a visible name.");
            else if (layout.Name.Length > ProtocolConstants.MaximumPinnedPresentationLayoutNameLength)
                Add($"{path}.name", "too_long",
                    $"A pinned layout name may not exceed {ProtocolConstants.MaximumPinnedPresentationLayoutNameLength} characters.");
            if (layout.Surface is null)
                Add($"{path}.surface", "required", "A pinned layout requires surface sizing hints.");
            else
                ValidateSurfaceHints(layout.Surface, $"{path}.surface");
            aggregateStrings += StringLength(layout.Id) + StringLength(layout.Name) +
                StringLength(layout.ActiveInputScopeId) + StringLength(layout.InitialFocusId);
            if (layout.Root is null)
            {
                if (layout.ActiveInputScopeId is not null)
                    Add($"{path}.activeInputScopeId", "projection_required",
                        "A pinned layout input scope requires a declarative root.");
                if (layout.InitialFocusId is not null)
                    Add($"{path}.initialFocusId", "projection_required",
                        "A pinned layout initial focus requires a declarative root.");
                continue;
            }
            hasPinnedProjection = true;

            var projection = snapshot with
            {
                Root = layout.Root,
                ActiveInputScopeId = layout.ActiveInputScopeId ?? string.Empty,
                InitialFocusId = layout.InitialFocusId,
                QuickActions = [],
                PinnedLayouts = [],
                EmbeddedMedia = null,
            };
            foreach (var projectionError in Validate(projection))
            {
                var projectionPath = projectionError.Path switch
                {
                    "$.root" => $"{path}.root",
                    var value when value.StartsWith("$.root.", StringComparison.Ordinal) =>
                        $"{path}.root{value[6..]}",
                    "$.activeInputScopeId" => $"{path}.activeInputScopeId",
                    "$.initialFocusId" => $"{path}.initialFocusId",
                    _ => $"{path}.root",
                };
                Add(projectionPath, projectionError.Code, projectionError.Message,
                    projectionError.IdentifierContext);
            }
            Measure(layout.Root);
        }
        if (hasPinnedProjection &&
            aggregateNodes > ProtocolConstants.MaximumPinnedPresentationAggregateNodeCount)
            Add("$.pinnedLayouts", "aggregate_tree_too_large",
                $"The full widget and pinned projections may contain at most {ProtocolConstants.MaximumPinnedPresentationAggregateNodeCount} nodes in total.");
        if (hasPinnedProjection &&
            aggregateStrings > ProtocolConstants.MaximumPinnedPresentationAggregateStringLength)
            Add("$.pinnedLayouts", "aggregate_strings_too_large",
                $"The full widget and pinned projections may contain at most {ProtocolConstants.MaximumPinnedPresentationAggregateStringLength} string characters in total.");
        if (hasPinnedProjection &&
            aggregateResources > ProtocolConstants.MaximumPinnedPresentationAggregateResourceCount)
            Add("$.pinnedLayouts", "aggregate_resources_too_large",
                $"The full widget and pinned projections may reference at most {ProtocolConstants.MaximumPinnedPresentationAggregateResourceCount} resources in total.");
        Visit(snapshot.Root, "$.root", 1, "$.root");
        ValidateFocusPresentationOwnership(snapshot.Root, "$.root", consumerDepth: 0);
        if (mediaViewportCount != 0 && snapshot.EmbeddedMedia is null)
            Add("$.root", "media_viewport_without_surface",
                "A MediaViewport requires one current embedded media surface declaration.");
        else if (mediaViewportCount > 1)
            Add("$.root", "duplicate_media_viewport",
                "A presentation may contain exactly one MediaViewport for its embedded media surface.");
        else if (snapshot.EmbeddedMedia?.RetainSessionWhenHidden == true &&
                 mediaViewportCount != 0)
            Add("$.embeddedMedia.retainSessionWhenHidden", "hidden_media_has_viewport",
                "Retained hidden embedded media cannot publish a MediaViewport.");
        else if (snapshot.ProtocolVersion >= ProtocolConstants.MediaViewportVersion &&
                 snapshot.EmbeddedMedia is not null && mediaViewportCount == 0 &&
                 !snapshot.EmbeddedMedia.RetainSessionWhenHidden)
            Add("$.root", "media_viewport_required",
                "Embedded media requires one declarative MediaViewport unless its existing session is explicitly retained hidden.");
        else if (mediaViewportCount == 1 && snapshot.EmbeddedMedia is { } embeddedMedia &&
                 !string.Equals(mediaViewportSurfaceId, embeddedMedia.Id, StringComparison.Ordinal))
            Add("$.root", "media_viewport_surface_mismatch",
                "MediaViewport must reference the current embedded media surface identity.");
        var activeInputScopeId = snapshot.ActiveInputScopeId ?? string.Empty;
        CheckIdentifier(activeInputScopeId, "$.activeInputScopeId", "active input scope ID",
            ProtocolValidationIdentifierKind.ElementReference);
        var hasActiveScope = inputScopes.TryGetValue(activeInputScopeId, out var activeScopeKey);
        if (!hasActiveScope && !string.IsNullOrWhiteSpace(activeInputScopeId))
            AddIdentifier("$.activeInputScopeId", "invalid_active_input_scope",
                $"The active input scope '{activeInputScopeId}' does not exist.",
                ProtocolValidationIdentifierKind.ElementReference,
                ProtocolValidationIdentifierState.Missing, activeInputScopeId);

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
            CheckIdentifier(quickAction.ActionId, $"$.quickActions[{index}].actionId",
                "quick action ID", ProtocolValidationIdentifierKind.Action);
            CheckString(quickAction.Label, $"$.quickActions[{index}].label");
            if (string.IsNullOrWhiteSpace(quickAction.Label))
                Add($"$.quickActions[{index}].label", "required", "A quick action requires a visible label.");
            if (!Enum.IsDefined(quickAction.Button))
                Add($"$.quickActions[{index}].button", "invalid_controller_button",
                    "The controller button is not supported.");
            else if (quickAction.Button == ControllerButton.View)
                Add($"$.quickActions[{index}].button", "host_reserved_view",
                    "View is reserved for host pinned-surface navigation and cannot be a dashboard quick action.");
            else if (!IsDashboardQuickActionButton(quickAction.Button))
                Add($"$.quickActions[{index}].button", "reserved_button", "This button is reserved for dashboard navigation or host behavior.");
            if (!quickActionButtons.Add(quickAction.Button))
                Add($"$.quickActions[{index}].button", "duplicate_button", "A dashboard button can trigger only one quick action.");
            if (!Enum.IsDefined(quickAction.RepeatPolicy))
                Add($"$.quickActions[{index}].repeatPolicy", "invalid_repeat_policy",
                    "The controller action repeat policy is not supported.");
            else if (quickAction.RepeatPolicy == ControllerActionRepeatPolicy.WhileHeld &&
                     !IsRepeatableDiscreteActionButton(quickAction.Button))
                Add($"$.quickActions[{index}].repeatPolicy", "reserved_repeat_button",
                    "This button remains edge-only because the host owns its navigation or shell behavior.");
            if (quickAction.Capability is { } capability)
            {
                CheckCapabilityIdentifier(capability.CapabilityId,
                    $"$.quickActions[{index}].capability.capabilityId", "capability ID");
                CheckIdentifier(capability.OperationId,
                    $"$.quickActions[{index}].capability.operationId", "capability operation ID");
            }
        }

        if (snapshot.InitialFocusId is { } initial)
        {
            if (!ids.TryGetValue(initial, out var initialTarget))
                AddIdentifier("$.initialFocusId", "invalid_focus_target",
                    "Initial focus must name a focusable node.",
                    ProtocolValidationIdentifierKind.InitialFocus,
                    ProtocolValidationIdentifierState.Missing, initial);
            else if (!initialTarget.Node.IsFocusable)
                AddIdentifier("$.initialFocusId", "invalid_focus_target",
                    "Initial focus must name a focusable node.",
                    ProtocolValidationIdentifierKind.InitialFocus,
                    ProtocolValidationIdentifierState.NotFocusable, initial);
            else if (hasActiveScope && !string.Equals(initialTarget.ScopeKey, activeScopeKey, StringComparison.Ordinal))
                AddIdentifier("$.initialFocusId", "initial_focus_outside_active_scope",
                    "Initial focus must belong to the active input scope.",
                    ProtocolValidationIdentifierKind.InitialFocus,
                    ProtocolValidationIdentifierState.OutsideActiveScope, initial);
        }

        foreach (var (_, entry) in ids)
        {
            if (entry.Node.InitialChildFocusId is { } initialChild)
            {
                CheckIdentifier(initialChild, $"{entry.Path}.initialChildFocusId",
                    "initial child focus ID", ProtocolValidationIdentifierKind.ReturnFocus);
                if (entry.Node.Kind is not (ViewNodeKind.Stack or ViewNodeKind.Row or
                    ViewNodeKind.Scroll or ViewNodeKind.Grid))
                    Add($"{entry.Path}.initialChildFocusId", "focus_group_on_non_container",
                        "Only Stack, Row, Scroll, and Grid containers may opt into remembered-child focus.");
                else if (!ids.TryGetValue(initialChild, out var initialTarget))
                    AddIdentifier($"{entry.Path}.initialChildFocusId", "invalid_initial_child_focus",
                        "Initial child focus must name an always-available focusable descendant.",
                        ProtocolValidationIdentifierKind.ReturnFocus,
                        ProtocolValidationIdentifierState.Missing, initialChild);
                else if (!initialTarget.Node.IsFocusable ||
                         !IsAlwaysAvailableDescendant(entry.Node, initialChild))
                    AddIdentifier($"{entry.Path}.initialChildFocusId", "invalid_initial_child_focus",
                        "Initial child focus must name an always-available focusable descendant.",
                        ProtocolValidationIdentifierKind.ReturnFocus,
                        ProtocolValidationIdentifierState.NotFocusable, initialChild);
                else if (!string.Equals(entry.ScopeKey, initialTarget.ScopeKey, StringComparison.Ordinal))
                    AddIdentifier($"{entry.Path}.initialChildFocusId", "initial_child_focus_outside_scope",
                        "Initial child focus must belong to the container's input scope.",
                        ProtocolValidationIdentifierKind.ReturnFocus,
                        ProtocolValidationIdentifierState.OutsideActiveScope, initialChild);
            }

            var focus = entry.Node.Focus;
            if (focus is null) continue;
            CheckFocus(focus.Up, "up");
            CheckFocus(focus.Down, "down");
            CheckFocus(focus.Left, "left");
            CheckFocus(focus.Right, "right");

            void CheckFocus(string? targetId, string direction)
            {
                if (targetId is null) return;
                if (!ids.TryGetValue(targetId, out var target))
                    AddIdentifier($"{entry.Path}.focus.{direction}", "invalid_focus_target",
                        $"'{targetId}' is not a focusable node or remembered-child focus group.",
                        ProtocolValidationIdentifierKind.ElementReference,
                        ProtocolValidationIdentifierState.Missing, targetId);
                else if (!target.Node.IsFocusable && target.Node.InitialChildFocusId is null)
                    AddIdentifier($"{entry.Path}.focus.{direction}", "invalid_focus_target",
                        $"'{targetId}' is not a focusable node or remembered-child focus group.",
                        ProtocolValidationIdentifierKind.ElementReference,
                        ProtocolValidationIdentifierState.NotFocusable, targetId);
                else if (!string.Equals(entry.ScopeKey, target.ScopeKey, StringComparison.Ordinal))
                    AddIdentifier($"{entry.Path}.focus.{direction}", "cross_input_scope_focus",
                        "Explicit focus neighbors cannot cross input-scope boundaries.",
                        ProtocolValidationIdentifierKind.ElementReference,
                        ProtocolValidationIdentifierState.OutsideActiveScope, targetId);
                else if (target.Node.InitialChildFocusId is not null &&
                         target.Node.VisibleWhen is not (null or ResponsiveVisibility.Always))
                    Add($"{entry.Path}.focus.{direction}", "hidden_focus_group_target",
                        "An explicit focus-group target must be available whenever its source is visible.");
            }
        }

        return errors;

        void ValidateEmbeddedMedia(EmbeddedMediaSurface? media)
        {
            if (media is null) return;
            const string path = "$.embeddedMedia";
            CheckIdentifier(media.Id, $"{path}.id", "embedded media surface ID");
            CheckString(media.AccessibleName, $"{path}.accessibleName");
            if (string.IsNullOrWhiteSpace(media.AccessibleName))
                Add($"{path}.accessibleName", "required",
                    "An embedded media surface requires an accessible name.");
            if (media.Surface is null)
                Add($"{path}.surface", "required",
                    "An embedded media surface requires bounded sizing hints.");
            else
            {
                ValidateSurfaceHints(media.Surface, $"{path}.surface");
                if (media.Surface.PreferredWidth is null || media.Surface.PreferredHeight is null ||
                    media.Surface.MinimumWidth is null || media.Surface.MinimumHeight is null)
                    Add($"{path}.surface", "complete_bounds_required",
                        "Embedded media requires preferred and minimum width and height.");
            }
            if (!double.IsFinite(media.AspectRatio) || media.AspectRatio is < 0.1 or > 10.0)
                Add($"{path}.aspectRatio", "out_of_range",
                    "Embedded media aspect ratio must be finite and between 0.1 and 10.");

            var resources = media.Resources ?? [];
            if (media.Resources is null)
                Add($"{path}.resources", "required", "Embedded media resources cannot be null.");
            if (resources.Count is < 1 or > ProtocolConstants.MaximumEmbeddedMediaResourceCount)
                Add($"{path}.resources", "resource_count",
                    $"Embedded media requires 1-{ProtocolConstants.MaximumEmbeddedMediaResourceCount} package resources.");
            var resourcePaths = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < resources.Count; index++)
            {
                var resource = resources[index];
                var resourcePath = $"{path}.resources[{index}]";
                if (resource is null)
                {
                    Add(resourcePath, "required", "An embedded media resource cannot be null.");
                    continue;
                }
                if (!IsNormalizedPackageAssetPath(resource.Path))
                    Add($"{resourcePath}.path", "invalid_package_asset_path",
                        "Media resources must use a normalized package-relative path without traversal.");
                else if (!resourcePaths.Add(resource.Path))
                    Add($"{resourcePath}.path", "duplicate_resource",
                        "Embedded media resource paths must be unique.");
                if (!IsEmbeddedMediaContentType(resource.ContentType))
                    Add($"{resourcePath}.contentType", "unsupported_content_type",
                        "The embedded media content type is not in the closed host allowlist.");
            }
            if (!IsNormalizedPackageAssetPath(media.EntryAsset))
                Add($"{path}.entryAsset", "invalid_package_asset_path",
                    "The media entry asset must be a normalized package-relative path.");
            else if (!resourcePaths.Contains(media.EntryAsset))
                Add($"{path}.entryAsset", "entry_not_declared",
                    "The media entry asset must be present in resources.");
            else
            {
                var entry = resources.First(resource => resource?.Path == media.EntryAsset);
                if (!string.Equals(entry.ContentType, "text/html", StringComparison.Ordinal))
                    Add($"{path}.entryAsset", "entry_not_html",
                        "The media entry asset must declare text/html.");
            }

            var commands = media.Commands ?? [];
            if (media.Commands is null)
                Add($"{path}.commands", "required", "Embedded media commands cannot be null.");
            if (commands.Count > ProtocolConstants.MaximumEmbeddedMediaCommandCount)
                Add($"{path}.commands", "too_many",
                    $"Embedded media may declare at most {ProtocolConstants.MaximumEmbeddedMediaCommandCount} commands.");
            var knownCommands = new HashSet<EmbeddedMediaCommand>();
            for (var index = 0; index < commands.Count; index++)
            {
                if (!Enum.IsDefined(commands[index]))
                    Add($"{path}.commands[{index}]", "unsupported_command",
                        "The embedded media command is not supported.");
                else if (!knownCommands.Add(commands[index]))
                    Add($"{path}.commands[{index}]", "duplicate_command",
                        "Embedded media commands must be unique.");
            }
            if (media.MediaSeekStepSeconds is { } seekStep &&
                (!double.IsFinite(seekStep) ||
                 seekStep < ProtocolConstants.MinimumMediaSeekStepSeconds ||
                 seekStep > ProtocolConstants.MaximumMediaSeekStepSeconds))
                Add($"{path}.mediaSeekStepSeconds", "out_of_range",
                    $"Media seek step must be finite and between {ProtocolConstants.MinimumMediaSeekStepSeconds} and {ProtocolConstants.MaximumMediaSeekStepSeconds} seconds.");
            if (!media.CompactPinnedPresentation && !media.OverlayFullscreenCapable &&
                media.MediaSeekStepSeconds is not null)
                Add($"{path}.mediaSeekStepSeconds", "seek_presentation_required",
                    "A media seek step requires compact pinned presentation or overlay fullscreen capability.");
            if (media.CompactPinnedPresentation &&
                (!knownCommands.Contains(EmbeddedMediaCommand.TogglePlayback) ||
                 !knownCommands.Contains(EmbeddedMediaCommand.SeekBackward) ||
                 !knownCommands.Contains(EmbeddedMediaCommand.SeekForward)))
                Add($"{path}.commands", "compact_media_capabilities_required",
                    "Compact pinned media requires toggle-playback and seek-backward/seek-forward capabilities.");

            var frameOrigins = media.AllowedFrameOrigins ?? [];
            if (media.AllowedFrameOrigins is null)
                Add($"{path}.allowedFrameOrigins", "required",
                    "Embedded media frame origins cannot be null.");
            if (frameOrigins.Count > ProtocolConstants.MaximumEmbeddedMediaFrameOriginCount)
                Add($"{path}.allowedFrameOrigins", "too_many",
                    $"Embedded media may declare at most {ProtocolConstants.MaximumEmbeddedMediaFrameOriginCount} frame origins.");
            var uniqueOrigins = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < frameOrigins.Count; index++)
            {
                var origin = frameOrigins[index];
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
                    uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.Fragment.Length != 0 ||
                    origin.Contains('*') ||
                    !string.Equals(origin, uri.GetLeftPart(UriPartial.Authority),
                        StringComparison.Ordinal) ||
                    origin.Length > ProtocolConstants.MaximumEmbeddedMediaFrameOriginLength)
                    Add($"{path}.allowedFrameOrigins[{index}]", "invalid_origin",
                        "Embedded media frame origins must be exact bounded HTTPS origins.");
                else if (!uniqueOrigins.Add(origin))
                    Add($"{path}.allowedFrameOrigins[{index}]", "duplicate_origin",
                        "Embedded media frame origins must be unique.");
            }

            var frameFamilies = media.AllowedFrameDomainFamilies ?? [];
            if (media.AllowedFrameDomainFamilies is null)
                Add($"{path}.allowedFrameDomainFamilies", "required",
                    "Embedded media frame domain families cannot be null.");
            if (frameFamilies.Count >
                ProtocolConstants.MaximumEmbeddedMediaFrameDomainFamilyCount)
                Add($"{path}.allowedFrameDomainFamilies", "too_many",
                    $"Embedded media may declare at most {ProtocolConstants.MaximumEmbeddedMediaFrameDomainFamilyCount} frame domain families.");
            if (frameFamilies.Sum(value => value?.Length ?? 0) >
                ProtocolConstants.MaximumEmbeddedMediaFrameDomainFamilyAggregateLength)
                Add($"{path}.allowedFrameDomainFamilies", "aggregate_too_large",
                    "Embedded media frame domain families exceed the aggregate length limit.");
            var uniqueFamilies = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < frameFamilies.Count; index++)
            {
                var family = frameFamilies[index];
                if (family is null ||
                    family.Length > ProtocolConstants.MaximumEmbeddedMediaFrameDomainFamilyLength ||
                    !PublicSuffixDomainAuthority.IsCanonicalRegistrableDomain(family))
                    Add($"{path}.allowedFrameDomainFamilies[{index}]", "invalid_domain_family",
                        "Embedded media frame domain families must be canonical registrable DNS domains and cannot be public suffixes.");
                else if (!uniqueFamilies.Add(family))
                    Add($"{path}.allowedFrameDomainFamilies[{index}]", "duplicate_domain_family",
                        "Embedded media frame domain families must be unique.");
            }

            if (media.PendingCommand is { } pending)
            {
                if (pending.Sequence <= 0 || pending.Sequence > 9_007_199_254_740_991)
                    Add($"{path}.pendingCommand.sequence", "out_of_range",
                        "Embedded media command sequence must be a positive safe integer.");
                if (!Enum.IsDefined(pending.Kind))
                    Add($"{path}.pendingCommand.kind", "unsupported_command",
                        "Embedded media playback command is unsupported.");
                if (string.IsNullOrWhiteSpace(pending.MediaKey) ||
                    pending.MediaKey.Length > ProtocolConstants.MaximumEmbeddedMediaKeyLength ||
                    !pending.MediaKey.All(ch => char.IsAsciiLetterOrDigit(ch) ||
                        ch is '-' or '_' or '.'))
                    Add($"{path}.pendingCommand.mediaKey", "invalid_identifier",
                        "Embedded media keys must be bounded public identifiers.");
                if (pending.PositionSeconds is { } position &&
                    (!double.IsFinite(position) || position < 0 || position > 86_400))
                    Add($"{path}.pendingCommand.positionSeconds", "out_of_range",
                        "Embedded media position must be finite and between 0 and 86400 seconds.");
                if (pending.Volume is { } volume &&
                    (!double.IsFinite(volume) || volume < 0 || volume > 1))
                    Add($"{path}.pendingCommand.volume", "out_of_range",
                        "Embedded media volume must be finite and between 0 and 1.");
                if (pending.PlaybackRate is { } playbackRate &&
                    (!double.IsFinite(playbackRate) ||
                     playbackRate < ProtocolConstants.MinimumEmbeddedMediaPlaybackRate ||
                     playbackRate > ProtocolConstants.MaximumEmbeddedMediaPlaybackRate))
                    Add($"{path}.pendingCommand.playbackRate", "out_of_range",
                        $"Embedded media playback rate must be finite and between {ProtocolConstants.MinimumEmbeddedMediaPlaybackRate} and {ProtocolConstants.MaximumEmbeddedMediaPlaybackRate}.");
                if (pending.Kind == EmbeddedMediaPlaybackCommandKind.Seek &&
                    pending.PositionSeconds is null)
                    Add($"{path}.pendingCommand.positionSeconds", "required",
                        "Seek requires an exact position.");
                if (pending.Kind == EmbeddedMediaPlaybackCommandKind.SetVolume &&
                    pending.Volume is null)
                    Add($"{path}.pendingCommand.volume", "required",
                        "SetVolume requires an exact volume.");
                if (pending.Kind == EmbeddedMediaPlaybackCommandKind.SetPlaybackRate &&
                    pending.PlaybackRate is null)
                    Add($"{path}.pendingCommand.playbackRate", "required",
                        "SetPlaybackRate requires an exact playback rate.");
                if (pending.Kind != EmbeddedMediaPlaybackCommandKind.SetPlaybackRate &&
                    pending.PlaybackRate is not null)
                    Add($"{path}.pendingCommand.playbackRate", "unexpected",
                        "PlaybackRate is valid only for SetPlaybackRate.");
                if (pending.Kind == EmbeddedMediaPlaybackCommandKind.SetMuted &&
                    pending.Muted is null)
                    Add($"{path}.pendingCommand.muted", "required",
                        "SetMuted requires an exact boolean value.");
                if (pending.Kind != EmbeddedMediaPlaybackCommandKind.SetMuted &&
                    pending.Muted is not null)
                    Add($"{path}.pendingCommand.muted", "unexpected",
                        "Muted is valid only for SetMuted.");
                if (pending.Kind == EmbeddedMediaPlaybackCommandKind.SetLoop &&
                    pending.Loop is null)
                    Add($"{path}.pendingCommand.loop", "required",
                        "SetLoop requires an exact boolean value.");
                if (pending.Kind != EmbeddedMediaPlaybackCommandKind.SetLoop &&
                    pending.Loop is not null)
                    Add($"{path}.pendingCommand.loop", "unexpected",
                        "Loop is valid only for SetLoop.");
            }
        }

        void ValidateSurfaceHints(WidgetSurfaceHints? surface, string path)
        {
            if (surface is null) return;
            if (!Enum.IsDefined(surface.Mode))
                Add($"{path}.mode", "invalid_surface_mode", "The surface mode is not supported.");
            if (!Enum.IsDefined(surface.Appearance))
                Add($"{path}.appearance", "invalid_surface_appearance",
                    "The surface appearance is not supported.");
            if (!Enum.IsDefined(surface.WidthMode))
                Add($"{path}.widthMode", "invalid_surface_axis_mode",
                    "The surface width mode is not supported.");
            if (!Enum.IsDefined(surface.HeightMode))
                Add($"{path}.heightMode", "invalid_surface_axis_mode",
                    "The surface height mode is not supported.");
            CheckPair(surface.PreferredWidth, surface.PreferredHeight,
                "preferred", ProtocolConstants.MinimumSurfaceWidth,
                ProtocolConstants.MaximumSurfaceWidth,
                ProtocolConstants.MinimumSurfaceHeight, ProtocolConstants.MaximumSurfaceHeight, path);
            CheckPair(surface.MinimumWidth, surface.MinimumHeight,
                "minimum", ProtocolConstants.MinimumSurfaceWidth,
                ProtocolConstants.MaximumSurfaceWidth,
                ProtocolConstants.MinimumSurfaceHeight, ProtocolConstants.MaximumSurfaceHeight, path);
            if (surface.PreferredWidth is { } preferredWidth &&
                surface.MinimumWidth is { } minimumWidth &&
                minimumWidth > preferredWidth)
                Add($"{path}.minimumWidth", "surface_minimum_exceeds_preferred",
                    "Minimum width cannot exceed preferred width.");
            if (surface.PreferredHeight is { } preferredHeight &&
                surface.MinimumHeight is { } minimumHeight &&
                minimumHeight > preferredHeight)
                Add($"{path}.minimumHeight", "surface_minimum_exceeds_preferred",
                    "Minimum height cannot exceed preferred height.");
        }

        void CheckPair(
            double? width,
            double? height,
            string label,
            double minimumWidth,
            double maximumWidth,
            double minimumHeight,
            double maximumHeight,
            string path)
        {
            if (width.HasValue != height.HasValue)
            {
                Add($"{path}.{label}Width", "incomplete_surface_size",
                    $"The {label} width and height must be supplied together.");
            }
            if (width is { } actualWidth &&
                (!double.IsFinite(actualWidth) || actualWidth < minimumWidth || actualWidth > maximumWidth))
                Add($"{path}.{label}Width", "invalid_surface_size",
                    $"The {label} width must be finite and between {minimumWidth} and {maximumWidth} DIPs.");
            if (height is { } actualHeight &&
                (!double.IsFinite(actualHeight) || actualHeight < minimumHeight || actualHeight > maximumHeight))
                Add($"{path}.{label}Height", "invalid_surface_size",
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

            CheckIdentifier(node.Id, $"{path}.id", "node ID",
                ProtocolValidationIdentifierKind.ElementReference);
            if (!Enum.IsDefined(node.Kind))
                Add($"{path}.kind", "invalid_node_kind", "The node kind is not supported.");
            if (node.VisibleWhen is { } visibility)
            {
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
            CheckString(node.ActionId, $"{path}.actionId",
                ProtocolValidationIdentifierKind.Action);
            CheckString(node.ValueChangedActionId, $"{path}.valueChangedActionId",
                ProtocolValidationIdentifierKind.Action);
            if (node.FocusPersistenceId is not null)
            {
                CheckIdentifier(node.FocusPersistenceId,
                    $"{path}.focusPersistenceId", "focus persistence ID",
                    ProtocolValidationIdentifierKind.ElementReference);
                if (!node.IsFocusable)
                    Add($"{path}.focusPersistenceId", "focus_persistence_on_non_focusable_node",
                        "Only focusable nodes may declare focus persistence.");
            }
            if (node.FocusBackgroundArtworkHandle is not null)
            {
                CheckIdentifier(node.FocusBackgroundArtworkHandle,
                    $"{path}.focusBackgroundArtworkHandle", "focus-background artwork handle");
                if (!node.IsFocusable)
                    Add($"{path}.focusBackgroundArtworkHandle",
                        "focus_background_on_non_focusable_node",
                        "Only focusable nodes may declare focus-background artwork.");
            }
            if (node.UsesFocusedDescendantArtwork is not null &&
                node.Kind is not ViewNodeKind.BackgroundSurface)
                Add($"{path}.usesFocusedDescendantArtwork",
                    "focused_descendant_artwork_not_allowed",
                    "Only BackgroundSurface may consume focused-descendant artwork.");
            if (node.FocusPresentation is not null && !node.IsFocusable)
                Add($"{path}.focusPresentation", "focus_presentation_on_non_focusable_node",
                    "Only focusable nodes may declare an associated presentation fragment.");
            if (node.DefaultFocusPresentation is not null &&
                node.Kind is not ViewNodeKind.FocusPresentationSurface)
                Add($"{path}.defaultFocusPresentation", "focus_presentation_default_not_allowed",
                    "Only FocusPresentationSurface may declare a default presentation fragment.");
            if (node.Kind is not (ViewNodeKind.Image or ViewNodeKind.Button) ||
                node.ImageSource is null)
                CheckString(node.ImageSource, $"{path}.imageSource");

            var isContainer = node.Kind is
                ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Scroll or ViewNodeKind.Grid;
            var isActionSurface = node.Kind is ViewNodeKind.ActionSurface;
            if (node.Kind is ViewNodeKind.MediaViewport)
            {
                mediaViewportCount++;
                mediaViewportSurfaceId ??= node.MediaSurfaceId;
                CheckIdentifier(node.MediaSurfaceId, $"{path}.mediaSurfaceId",
                    "embedded media surface ID");
                if (snapshot.EmbeddedMedia is { } currentMedia &&
                    !string.Equals(node.AccessibilityLabel, currentMedia.AccessibleName,
                        StringComparison.Ordinal))
                    Add($"{path}.accessibilityLabel", "media_viewport_accessible_name_mismatch",
                        "MediaViewport accessibility must use the current embedded media accessible name.");
                if (node.Text is not null || node.AccessibilityValue is not null ||
                    node.ActionId is not null || node.Value is not null ||
                    node.Minimum is not null || node.Maximum is not null || node.Step is not null ||
                    node.ValueChangedActionId is not null || node.Focus is not null ||
                    node.ImageSource is not null || node.ArtworkHandle is not null ||
                    node.FocusBackgroundArtworkHandle is not null ||
                    node.ImageFit is not null || node.Glyph is not null ||
                    node.IndicatorSize is not null || node.InputScopeId is not null ||
                    node.ScrollAxis is not null || node.GridMinimumColumnWidth is not null ||
                    node.GridMaximumColumns is not null || node.IsDisabled is not null ||
                    node.IsSelected is not null || node.IsBusy is not null)
                    Add(path, "media_viewport_property_not_allowed",
                        "MediaViewport accepts only its ID, media surface identity, accessible name, visibility, and style classes.");
            }
            else if (node.MediaSurfaceId is not null)
            {
                Add($"{path}.mediaSurfaceId", "media_surface_id_not_allowed",
                    "Media surface identity applies only to MediaViewport nodes.");
            }
            if (node.Kind is ViewNodeKind.LoadingIndicator)
            {
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
                if (node.ScrollAxis is null)
                    Add($"{path}.scrollAxis", "required", "A scroll container requires an axis.");
                else if (!Enum.IsDefined(node.ScrollAxis.Value))
                    Add($"{path}.scrollAxis", "invalid_scroll_axis", "The scroll axis is not supported.");
                var hasPagination = node.ScrollNearStartActionId is not null ||
                    node.ScrollNearEndActionId is not null ||
                    node.ScrollPaginationThreshold is not null;
                if (hasPagination)
                {
                    if (node.ScrollNearStartActionId is not null)
                        CheckIdentifier(node.ScrollNearStartActionId,
                            $"{path}.scrollNearStartActionId", "scroll near-start action ID",
                            ProtocolValidationIdentifierKind.Action);
                    if (node.ScrollNearEndActionId is not null)
                        CheckIdentifier(node.ScrollNearEndActionId,
                            $"{path}.scrollNearEndActionId", "scroll near-end action ID",
                            ProtocolValidationIdentifierKind.Action);
                    if (node.ScrollNearStartActionId is null && node.ScrollNearEndActionId is null)
                        Add(path, "scroll_pagination_action_required",
                            "Scroll pagination requires at least one boundary action.");
                    if (node.ScrollPaginationThreshold is not { } threshold ||
                        threshold is < 1 or > ProtocolConstants.MaximumScrollPaginationThreshold)
                        Add($"{path}.scrollPaginationThreshold", "invalid_scroll_pagination_threshold",
                            $"Scroll pagination threshold must be between 1 and {ProtocolConstants.MaximumScrollPaginationThreshold}.");
                }
                var itemKeys = new HashSet<string>(StringComparer.Ordinal);
                var itemCount = 0;
                if (node.CollectionAnchorKey is not null || node.VirtualCollectionWindow is not null)
                    CollectCollectionItems(node, path);
                if (node.CollectionAnchorKey is not null)
                {
                    CheckIdentifier(node.CollectionAnchorKey,
                        $"{path}.collectionAnchorKey", "collection anchor key");
                    if (itemCount > ProtocolConstants.MaximumCursorCollectionItems)
                        Add(path, "too_many_collection_items",
                            $"A cursor collection may serialize at most {ProtocolConstants.MaximumCursorCollectionItems} retained items.");
                    if (!itemKeys.Contains(node.CollectionAnchorKey))
                        Add($"{path}.collectionAnchorKey", "missing_collection_anchor",
                            "The collection anchor must name one retained item key.");
                }
                else if (ContainsCollectionItem(node, isRoot: true))
                {
                    Add($"{path}.collectionAnchorKey", "collection_anchor_required",
                        "A non-empty keyed cursor collection requires one retained anchor.");
                }

                if (node.VirtualCollectionWindow is { } window)
                {
                    if (node.CollectionAnchorKey is null || itemCount == 0)
                        Add($"{path}.virtualCollectionWindow", "virtual_collection_items_required",
                            "A virtual collection window requires a non-empty keyed cursor collection.");
                    if (window.RequestGeneration is < 1 or
                        > ProtocolConstants.MaximumVirtualCollectionRequestGeneration)
                        Add($"{path}.virtualCollectionWindow.requestGeneration", "invalid_virtual_collection_generation",
                            $"Virtual collection request generation must be between 1 and {ProtocolConstants.MaximumVirtualCollectionRequestGeneration}.");
                    if (!Enum.IsDefined(window.Change))
                        Add($"{path}.virtualCollectionWindow.change", "invalid_virtual_collection_change",
                            "Virtual collection window change is not supported.");
                    if (window.FirstItemIndex is null &&
                        window.Change is not VirtualCollectionWindowChange.Replace)
                        Add($"{path}.virtualCollectionWindow.change", "virtual_collection_direction_requires_position",
                            "A virtual collection window without a logical position must use replace.");
                    if (!double.IsFinite(window.EstimatedItemExtent) ||
                        window.EstimatedItemExtent < ProtocolConstants.MinimumVirtualCollectionItemExtent ||
                        window.EstimatedItemExtent > ProtocolConstants.MaximumVirtualCollectionItemExtent)
                        Add($"{path}.virtualCollectionWindow.estimatedItemExtent", "invalid_virtual_collection_item_extent",
                            $"Estimated item extent must be finite and between {ProtocolConstants.MinimumVirtualCollectionItemExtent} and {ProtocolConstants.MaximumVirtualCollectionItemExtent} DIPs.");
                    if (window.FirstItemIndex is { } first &&
                        (first < 0 || first > ProtocolConstants.MaximumVirtualCollectionItems ||
                         itemCount > ProtocolConstants.MaximumVirtualCollectionItems - first))
                        Add($"{path}.virtualCollectionWindow.firstItemIndex", "invalid_virtual_collection_first_index",
                            $"Virtual collection first item index and admitted window must remain within {ProtocolConstants.MaximumVirtualCollectionItems} logical items.");
                    if (window.TotalItemCount is { } total)
                    {
                        if (total is < 1 or > ProtocolConstants.MaximumVirtualCollectionItems)
                            Add($"{path}.virtualCollectionWindow.totalItemCount", "invalid_virtual_collection_total",
                                $"Virtual collection total must be between 1 and {ProtocolConstants.MaximumVirtualCollectionItems}.");
                        if (window.FirstItemIndex is not { } knownFirstIndex)
                            Add($"{path}.virtualCollectionWindow.firstItemIndex", "virtual_collection_first_index_required",
                                "A known virtual collection total requires the first admitted item index.");
                        else if (knownFirstIndex > total || itemCount > total - knownFirstIndex)
                            Add($"{path}.virtualCollectionWindow", "virtual_collection_window_out_of_range",
                                "The admitted virtual collection window exceeds its logical total.");
                        if (double.IsFinite(window.EstimatedItemExtent) &&
                            total * window.EstimatedItemExtent > ProtocolConstants.MaximumVirtualCollectionExtent)
                            Add($"{path}.virtualCollectionWindow", "virtual_collection_extent_too_large",
                                $"Estimated virtual collection extent may not exceed {ProtocolConstants.MaximumVirtualCollectionExtent} DIPs.");
                    }
                    if (window.FirstItemIndex == 0 && window.HasBefore)
                        Add($"{path}.virtualCollectionWindow.hasBefore", "inconsistent_virtual_collection_boundary",
                            "The first logical item cannot report preceding content.");
                    if (window.TotalItemCount is { } knownTotal &&
                        window.FirstItemIndex is { } knownFirst &&
                        knownFirst <= knownTotal && itemCount == knownTotal - knownFirst &&
                        window.HasAfter)
                        Add($"{path}.virtualCollectionWindow.hasAfter", "inconsistent_virtual_collection_boundary",
                            "The final logical item cannot report following content.");
                    if (window.HasBefore != (node.ScrollNearStartActionId is not null) ||
                        window.HasAfter != (node.ScrollNearEndActionId is not null))
                        Add($"{path}.virtualCollectionWindow", "virtual_collection_action_mismatch",
                            "Virtual collection availability must match its admitted boundary actions.");
                }

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
                     node.VirtualCollectionWindow is not null ||
                     node.CollectionAnchorKey is not null)
            {
                Add(path, "scroll_property_not_allowed",
                    "Scroll properties apply only to scroll containers.");
            }
            if (node.CollectionItemKey is not null)
            {
                CheckIdentifier(node.CollectionItemKey,
                    $"{path}.collectionItemKey", "collection item key");
            }
            if (node.Kind is ViewNodeKind.Grid)
            {
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
                if (node.ActionSurfaceOrientation is null)
                    Add($"{path}.actionSurfaceOrientation", "required",
                        "An action surface requires a bounded content orientation.");
                else if (!Enum.IsDefined(node.ActionSurfaceOrientation.Value))
                    Add($"{path}.actionSurfaceOrientation", "invalid_action_surface_orientation",
                        "The action-surface orientation is not supported.");
                if (node.ActionSurfacePresentation is { } presentation &&
                    !Enum.IsDefined(presentation))
                    Add($"{path}.actionSurfacePresentation", "invalid_action_surface_presentation",
                        "The action-surface presentation is not supported.");
                if (node.ActionSurfacePresentation == ActionSurfacePresentation.Poster)
                {
                    if (node.ActionSurfaceOrientation != ActionSurfaceOrientation.Vertical)
                        Add($"{path}.actionSurfaceOrientation", "poster_orientation_required",
                            "A poster ActionSurface requires vertical content orientation.");
                    if (node.Children.Count is < 1 or > 2 ||
                        node.Children[^1].Kind != ViewNodeKind.Stack ||
                        (node.Children.Count == 2 &&
                         (node.Children[0].Kind != ViewNodeKind.Image ||
                          node.Children[0].ImageFit != ImageFit.Cover)))
                        Add($"{path}.children", "invalid_poster_structure",
                            "A poster ActionSurface requires one bounded bottom Stack and may precede it with one Cover Image artwork child.");
                }
                if (string.IsNullOrWhiteSpace(node.ActionId))
                    AddIdentifier($"{path}.actionId", "required",
                        "An action surface requires an action ID.",
                        ProtocolValidationIdentifierKind.Action,
                        ProtocolValidationIdentifierState.Missing, null);
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
            else if (node.ActionSurfaceOrientation is not null ||
                     node.ActionSurfacePresentation is not null)
            {
                Add(path, "action_surface_presentation_not_allowed",
                    "Action-surface orientation and presentation apply only to action surfaces.");
            }
            if (node.InputScopeId is not null)
            {
                CheckIdentifier(node.InputScopeId, $"{path}.inputScopeId", "input scope ID",
                    ProtocolValidationIdentifierKind.ElementReference);
                if (!isContainer)
                    Add($"{path}.inputScopeId", "input_scope_not_allowed",
                        "Only stack, row, scroll, and grid containers may start an input scope.");
            }
            if (node.InitialChildFocusId is not null && !isContainer)
                Add($"{path}.initialChildFocusId", "focus_group_on_non_container",
                    "Only Stack, Row, Scroll, and Grid containers may opt into remembered-child focus.");
            var startsScope = depth == 1 || (isContainer && node.InputScopeId is not null);
            var scopeKey = startsScope ? path : inheritedScopeKey;
            if (startsScope)
            {
                var publicScopeId = node.InputScopeId ?? node.Id;
                if (!string.IsNullOrWhiteSpace(publicScopeId) && !inputScopes.TryAdd(publicScopeId, scopeKey))
                    AddIdentifier($"{path}.inputScopeId", "duplicate_input_scope",
                        $"The input scope ID '{publicScopeId}' is already used.",
                        ProtocolValidationIdentifierKind.ElementReference,
                        ProtocolValidationIdentifierState.Duplicate, publicScopeId);
            }
            if (!string.IsNullOrWhiteSpace(node.Id) && !ids.TryAdd(node.Id, (node, path, scopeKey)))
                AddIdentifier($"{path}.id", "duplicate_id", $"The ID '{node.Id}' is already used.",
                    ProtocolValidationIdentifierKind.ElementReference,
                    ProtocolValidationIdentifierState.Duplicate, node.Id);

            if (node.Kind is ViewNodeKind.Button or ViewNodeKind.TextEntry &&
                string.IsNullOrWhiteSpace(node.ActionId))
                AddIdentifier($"{path}.actionId", "required",
                    "An interactive text or button control requires an action ID.",
                    ProtocolValidationIdentifierKind.Action,
                    ProtocolValidationIdentifierState.Missing, null);
            if (node.Kind is ViewNodeKind.Button or ViewNodeKind.TextEntry &&
                string.IsNullOrWhiteSpace(node.Text) && string.IsNullOrWhiteSpace(node.AccessibilityLabel))
                Add(path, "missing_accessible_name", "An interactive text or button control requires visible text or an accessibility label.");
            if (node.Kind is not (ViewNodeKind.Button or ViewNodeKind.Slider or ViewNodeKind.ActionSurface or ViewNodeKind.TextEntry) &&
                (node.IsDisabled is not null || node.IsSelected is not null || node.IsBusy is not null))
                Add(path, "interaction_state_not_allowed",
                    "Interaction states apply only to buttons, sliders, action surfaces, and text entry.");
            if (node.Kind is ViewNodeKind.Slider && node.IsSelected is not null)
                Add($"{path}.isSelected", "interaction_state_not_allowed", "Selected state does not apply to sliders.");
            if (node.Kind is ViewNodeKind.Progress &&
                (node.Value is null || node.Maximum is null ||
                 !double.IsFinite(node.Value.Value) || !double.IsFinite(node.Maximum.Value) ||
                 node.Maximum <= 0 || node.Value < 0 || node.Value > node.Maximum))
                Add(path, "invalid_progress", "Progress requires 0 <= value <= maximum and maximum > 0.");
            if (node.Kind is ViewNodeKind.Slider)
            {
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
                CheckIdentifier(node.ValueChangedActionId, $"{path}.valueChangedActionId",
                    "slider value-changed action ID", ProtocolValidationIdentifierKind.Action);
                if (node.ActionId is not null)
                    CheckIdentifier(node.ActionId, $"{path}.actionId", "slider activation action ID",
                        ProtocolValidationIdentifierKind.Action);
                if (node.SliderInteractionMode is { } interactionMode)
                {
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
                    (node.Kind is not ViewNodeKind.TextEntry && node.AccessibilityValue is not null) ||
                    node.SliderInteractionMode is not null)
                    Add(path, "slider_property_not_allowed",
                        "Minimum, step, value-change action, accessible value, and interaction mode apply only to sliders.");
            }
            if (node.Kind is ViewNodeKind.TextEntry)
            {
                if (node.TextEntryInputKind is { } inputKind && !Enum.IsDefined(inputKind))
                    Add($"{path}.textEntryInputKind", "invalid_text_entry_input_kind",
                        "The text-entry input kind is not supported.");
                if (node.TextEntryMaximumLength is not (>= 1 and <= ProtocolConstants.MaximumTextEntryLength))
                    Add($"{path}.textEntryMaximumLength", "invalid_text_entry_limit",
                        $"Text entry maximum length must be 1-{ProtocolConstants.MaximumTextEntryLength}.");
                if (node.TextEntryValue is null ||
                    node.TextEntryValue.Length > (node.TextEntryMaximumLength ?? 0) ||
                    node.TextEntryValue.Any(char.IsControl))
                    Add($"{path}.textEntryValue", "invalid_text_entry_value",
                        "Text entry requires a bounded control-free current value.");
                if (node.TextEntryInputKind is TextEntryInputKind.Sensitive &&
                    node.TextEntryValue is not "")
                    Add($"{path}.textEntryValue", "sensitive_text_entry_value_not_empty",
                        "Sensitive text entry cannot publish an authored value.");
                if (node.TextEntryInputKind is TextEntryInputKind.Sensitive &&
                    node.AccessibilityValue is not null)
                    Add($"{path}.accessibilityValue", "sensitive_text_entry_accessibility_value_not_allowed",
                        "Sensitive text entry cannot publish an accessibility value.");
                if (node.TextEntryPlaceholder is null ||
                    node.TextEntryPlaceholder.Length > ProtocolConstants.MaximumTextEntryLength ||
                    node.TextEntryPlaceholder.Any(char.IsControl))
                    Add($"{path}.textEntryPlaceholder", "invalid_text_entry_placeholder",
                        "Text entry requires a bounded control-free placeholder.");
            }
            else if (node.TextEntryValue is not null || node.TextEntryPlaceholder is not null ||
                     node.TextEntryMaximumLength is not null || node.TextEntryInputKind is not null)
                Add(path, "text_entry_property_not_allowed",
                    "Text-entry properties apply only to text-entry nodes.");
            if (node.Kind is not (ViewNodeKind.Button or ViewNodeKind.Slider or ViewNodeKind.ActionSurface or ViewNodeKind.TextEntry) &&
                node.ActionId is not null)
                AddIdentifier($"{path}.actionId", "action_not_allowed",
                    "Action IDs apply only to buttons, sliders, action surfaces, and text entry.",
                    ProtocolValidationIdentifierKind.Action,
                    ProtocolValidationIdentifierState.UnknownAction, node.ActionId);
            var contextActions = node.ContextActions ?? [];
            if (node.ContextActions is null)
                Add($"{path}.contextActions", "required", "Context actions cannot be null.");
            else if (node.Kind is not ViewNodeKind.ActionSurface && contextActions.Count != 0)
                Add($"{path}.contextActions", "context_actions_not_allowed",
                    "Only action surfaces may declare contextual actions.");
            else if (contextActions.Count > ProtocolConstants.MaximumContextActionCount)
                Add($"{path}.contextActions", "too_many_context_actions",
                    $"An action surface may expose at most {ProtocolConstants.MaximumContextActionCount} context actions.");
            var contextActionIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < Math.Min(
                     contextActions.Count, ProtocolConstants.MaximumContextActionCount); index++)
            {
                var action = contextActions[index];
                var actionPath = $"{path}.contextActions[{index}]";
                if (action is null)
                {
                    Add(actionPath, "required", "A context action cannot be null.");
                    continue;
                }
                CheckIdentifier(action.ActionId, $"{actionPath}.actionId", "context action ID",
                    ProtocolValidationIdentifierKind.ContextAction);
                if (!string.IsNullOrWhiteSpace(action.ActionId) &&
                    !contextActionIds.Add(action.ActionId))
                    AddIdentifier($"{actionPath}.actionId", "duplicate_context_action",
                        $"The context action ID '{action.ActionId}' is repeated.",
                        ProtocolValidationIdentifierKind.ContextAction,
                        ProtocolValidationIdentifierState.Duplicate, action.ActionId);
                if (string.IsNullOrWhiteSpace(action.Label) ||
                    action.Label.Length > ProtocolConstants.MaximumStringLength ||
                    action.Label.Any(char.IsControl))
                    Add($"{actionPath}.label", "invalid_context_action_label",
                        "A context action requires bounded control-free visible text.");
                if (!Enum.IsDefined(action.Style))
                    Add($"{actionPath}.style", "invalid_context_action_style",
                        "The context action style is not supported.");
            }
            var supportsImageSource = node.Kind is ViewNodeKind.Image or ViewNodeKind.Button or
                ViewNodeKind.BackgroundSurface;
            if (node.ArtworkHandle is not null)
            {
                if (!supportsImageSource)
                    Add($"{path}.artworkHandle", "artwork_handle_not_allowed",
                        "Opaque artwork handles apply only to image, button, and background-surface nodes.");
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
                    "Image source and fit apply only to images, buttons with leading artwork, and background surfaces.");
            }
            else if (node.Kind is ViewNodeKind.Button && node.ImageFit is not null && node.ArtworkHandle is null)
            {
                Add($"{path}.imageFit", "image_fit_without_source",
                    "A button image fit requires a leading image source.");
            }
            else if (node.Kind is ViewNodeKind.BackgroundSurface &&
                node.ImageFit is not null && node.ImageSource is null && node.ArtworkHandle is null)
            {
                Add($"{path}.imageFit", "image_fit_without_source",
                    "A background-surface image fit requires an image source or artwork handle.");
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
                ViewNodeKind.ActionSurface or ViewNodeKind.Grid or ViewNodeKind.BackgroundSurface or
                ViewNodeKind.FocusPresentationSurface) &&
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

            if (node.Kind is ViewNodeKind.BackgroundSurface && children.Count != 1)
                Add($"{path}.children", "background_surface_child_count",
                    "A background surface requires exactly one foreground child.");
            if (node.Kind is ViewNodeKind.FocusPresentationSurface)
            {
                if (children.Count != 1)
                    Add($"{path}.children", "focus_presentation_surface_child_count",
                        "A focus-presentation surface requires exactly one ordinary content child.");
                if (node.DefaultFocusPresentation is null)
                    Add($"{path}.defaultFocusPresentation", "required",
                        "A focus-presentation surface requires one default presentation fragment.");
                if (node.Text is not null || node.AccessibilityLabel is not null ||
                    node.AccessibilityValue is not null || node.ActionId is not null ||
                    (node.ContextActions?.Count ?? 0) != 0 || node.Value is not null ||
                    node.Minimum is not null || node.Maximum is not null || node.Step is not null ||
                    node.ValueChangedActionId is not null || node.ImageSource is not null ||
                    node.ArtworkHandle is not null || node.FocusBackgroundArtworkHandle is not null ||
                    node.MediaSurfaceId is not null || node.ImageFit is not null || node.Glyph is not null ||
                    node.IndicatorSize is not null || node.ActionSurfaceOrientation is not null ||
                    node.ActionSurfacePresentation is not null || node.GridMinimumColumnWidth is not null ||
                    node.GridMaximumColumns is not null || node.IsDisabled is not null ||
                    node.IsSelected is not null || node.IsBusy is not null ||
                    node.FocusPersistenceId is not null || node.Focus is not null ||
                    node.InputScopeId is not null || node.InitialChildFocusId is not null ||
                    node.UsesFocusedDescendantArtwork is not null || node.ScrollAxis is not null ||
                    node.ScrollNearStartActionId is not null || node.ScrollNearEndActionId is not null ||
                    node.ScrollPaginationThreshold is not null || node.VirtualCollectionWindow is not null ||
                    node.CollectionAnchorKey is not null || node.CollectionItemKey is not null ||
                    (node.Shortcuts?.Count ?? 0) != 0)
                    Add(path, "focus_presentation_surface_property_not_allowed",
                        "FocusPresentationSurface accepts only its ID, visibility, styles, default fragment, and one ordinary content child.");
            }

            var shortcutButtons = new HashSet<(ControllerButton, ControllerEventPhase)>();
            for (var index = 0; index < shortcuts.Count; index++)
            {
                var shortcut = shortcuts[index];
                CheckIdentifier(shortcut.ActionId, $"{path}.shortcuts[{index}].actionId",
                    "shortcut action ID", ProtocolValidationIdentifierKind.Action);
                if (!Enum.IsDefined(shortcut.Phase))
                    Add($"{path}.shortcuts[{index}].phase", "invalid_controller_phase",
                        "The controller event phase is not supported.");
                else if (shortcut.Phase != ControllerEventPhase.Pressed)
                    Add($"{path}.shortcuts[{index}].phase", "unsupported_shortcut_phase",
                        "The MVP host emits only Pressed shortcut events.");
                if (!Enum.IsDefined(shortcut.Button))
                    Add($"{path}.shortcuts[{index}].button", "invalid_controller_button",
                        "The controller button is not supported.");
                else if (shortcut.Button == ControllerButton.View)
                    Add($"{path}.shortcuts[{index}].button", "host_reserved_view",
                        "View is reserved for host pinned-surface navigation and cannot be an authored shortcut.");
                else if (!IsOpenWidgetShortcutButton(shortcut.Button))
                    Add($"{path}.shortcuts[{index}].button", "reserved_shortcut_button",
                        "A and D-pad buttons are reserved for activation and focus navigation.");
                if (!shortcutButtons.Add((shortcut.Button, shortcut.Phase)))
                    Add($"{path}.shortcuts[{index}].button", "duplicate_shortcut",
                        "A node cannot declare the same controller button and phase twice.");
                if (!Enum.IsDefined(shortcut.RepeatPolicy))
                    Add($"{path}.shortcuts[{index}].repeatPolicy", "invalid_repeat_policy",
                        "The controller action repeat policy is not supported.");
                else if (shortcut.RepeatPolicy == ControllerActionRepeatPolicy.WhileHeld &&
                         !IsRepeatableDiscreteActionButton(shortcut.Button))
                    Add($"{path}.shortcuts[{index}].repeatPolicy", "reserved_repeat_button",
                        "This button remains edge-only because the host owns its navigation or shell behavior.");
            }

            if (node.FocusPresentation is { } focusPresentation)
            {
                ValidateFocusPresentationContent(
                    focusPresentation, $"{path}.focusPresentation");
                Visit(focusPresentation, $"{path}.focusPresentation", depth + 1, scopeKey);
            }
            if (node.DefaultFocusPresentation is { } defaultFocusPresentation)
            {
                ValidateFocusPresentationContent(
                    defaultFocusPresentation, $"{path}.defaultFocusPresentation");
                Visit(defaultFocusPresentation,
                    $"{path}.defaultFocusPresentation", depth + 1, scopeKey);
            }
            for (var index = 0; index < children.Count; index++)
                Visit(children[index], $"{path}.children[{index}]", depth + 1, scopeKey);
        }

        void ValidateFocusPresentationOwnership(
            ViewNode? node,
            string path,
            int consumerDepth)
        {
            if (node is null) return;
            var nextConsumerDepth = node.Kind is ViewNodeKind.FocusPresentationSurface
                ? consumerDepth + 1
                : consumerDepth;
            if (node.FocusPresentation is not null && consumerDepth == 0)
                Add($"{path}.focusPresentation", "focus_presentation_consumer_required",
                    "An associated presentation requires an enclosing FocusPresentationSurface.");
            var children = node.Children ?? [];
            for (var index = 0; index < children.Count; index++)
                ValidateFocusPresentationOwnership(
                    children[index], $"{path}.children[{index}]", nextConsumerDepth);
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

        void ValidateFocusPresentationContent(ViewNode root, string path)
        {
            var fragmentNodes = 0;
            VisitFragment(root, path, 1);

            void VisitFragment(ViewNode? node, string nodePath, int relativeDepth)
            {
                if (node is null) return;
                fragmentNodes++;
                if (fragmentNodes == ProtocolConstants.MaximumFocusPresentationNodes + 1)
                    Add(nodePath, "focus_presentation_too_large",
                        $"A focus-associated presentation may contain at most {ProtocolConstants.MaximumFocusPresentationNodes} nodes.");
                if (relativeDepth > ProtocolConstants.MaximumFocusPresentationDepth)
                    Add(nodePath, "focus_presentation_too_deep",
                        $"A focus-associated presentation may be at most {ProtocolConstants.MaximumFocusPresentationDepth} levels deep.");
                if (node.Kind is not (ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Grid or
                    ViewNodeKind.Text or ViewNodeKind.Progress or ViewNodeKind.Spacer or
                    ViewNodeKind.Image or ViewNodeKind.Icon or ViewNodeKind.LoadingIndicator) ||
                    node.ActionId is not null || (node.ContextActions?.Count ?? 0) != 0 ||
                    node.TextEntryValue is not null || node.TextEntryPlaceholder is not null ||
                    node.TextEntryMaximumLength is not null || node.TextEntryInputKind is not null ||
                    node.Minimum is not null || node.Step is not null ||
                    node.ValueChangedActionId is not null || node.SliderInteractionMode is not null ||
                    node.FocusBackgroundArtworkHandle is not null || node.MediaSurfaceId is not null ||
                    node.ActionSurfaceOrientation is not null || node.ActionSurfacePresentation is not null ||
                    node.IsDisabled is not null || node.IsSelected is not null || node.IsBusy is not null ||
                    node.FocusPersistenceId is not null || node.Focus is not null ||
                    node.InputScopeId is not null || node.InitialChildFocusId is not null ||
                    node.UsesFocusedDescendantArtwork is not null || node.FocusPresentation is not null ||
                    node.DefaultFocusPresentation is not null || node.ScrollAxis is not null ||
                    node.ScrollNearStartActionId is not null || node.ScrollNearEndActionId is not null ||
                    node.ScrollPaginationThreshold is not null || node.VirtualCollectionWindow is not null ||
                    node.CollectionAnchorKey is not null || node.CollectionItemKey is not null ||
                    (node.Shortcuts?.Count ?? 0) != 0)
                {
                    Add(nodePath, "interactive_focus_presentation_fragment",
                        "Focus-associated presentation fragments may contain only bounded presentational layout, text, progress, image, icon, loading, and spacer nodes.");
                }
                var children = node.Children ?? [];
                for (var index = 0; index < children.Count; index++)
                    VisitFragment(children[index], $"{nodePath}.children[{index}]", relativeDepth + 1);
            }
        }

        void CheckIdentifier(
            string? value,
            string path,
            string label,
            ProtocolValidationIdentifierKind? fieldKind = null)
        {
            if (string.IsNullOrWhiteSpace(value))
                AddIdentifierOrPlain(path, "required", $"The {label} is required.",
                    fieldKind, ProtocolValidationIdentifierState.Missing, null);
            else if (value.Length > 128)
                AddIdentifierOrPlain(path, "too_long",
                    $"The {label} may not exceed 128 characters.", fieldKind,
                    ProtocolValidationIdentifierState.UnsafeValue, value);
            else if (!value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'))
                AddIdentifierOrPlain(path, "invalid_identifier",
                    $"The {label} may contain only ASCII letters, digits, '.', '-' and '_'.",
                    fieldKind, ProtocolValidationIdentifierState.UnsafeValue, value);
        }

        void Measure(ViewNode? root)
        {
            if (root is null) return;
            var pending = new Stack<ViewNode>();
            pending.Push(root);
            while (pending.Count != 0)
            {
                var node = pending.Pop();
                aggregateNodes++;
                aggregateStrings += StringLength(node.Id) + StringLength(node.Text) +
                    StringLength(node.AccessibilityLabel) + StringLength(node.AccessibilityValue) +
                    StringLength(node.ActionId) + StringLength(node.TextEntryValue) +
                    StringLength(node.TextEntryPlaceholder) + StringLength(node.ValueChangedActionId) +
                    StringLength(node.ImageSource) + StringLength(node.ArtworkHandle) +
                    StringLength(node.FocusBackgroundArtworkHandle) +
                    StringLength(node.MediaSurfaceId) +
                    StringLength(node.FocusPersistenceId) + StringLength(node.InputScopeId) +
                    StringLength(node.InitialChildFocusId) +
                    StringLength(node.ScrollNearStartActionId) + StringLength(node.ScrollNearEndActionId) +
                    StringLength(node.CollectionAnchorKey) + StringLength(node.CollectionItemKey) +
                    StringLength(node.Focus?.Up) + StringLength(node.Focus?.Down) +
                    StringLength(node.Focus?.Left) + StringLength(node.Focus?.Right) +
                    (node.StyleClasses?.Sum(StringLength) ?? 0) +
                    (node.Shortcuts?.Sum(shortcut => StringLength(shortcut?.ActionId)) ?? 0);
                if (node.ImageSource is not null || node.ArtworkHandle is not null ||
                    node.FocusBackgroundArtworkHandle is not null)
                    aggregateResources++;
                if (node.Children is not null)
                    foreach (var child in node.Children)
                        if (child is not null) pending.Push(child);
                if (node.FocusPresentation is not null) pending.Push(node.FocusPresentation);
                if (node.DefaultFocusPresentation is not null)
                    pending.Push(node.DefaultFocusPresentation);
            }
        }

        static int StringLength(string? value) => value?.Length ?? 0;

        static bool IsAlwaysAvailableDescendant(ViewNode container, string targetId)
        {
            foreach (var child in container.Children ?? [])
            {
                if (child is null) continue;
                if (child.VisibleWhen is not (null or ResponsiveVisibility.Always)) continue;
                if (string.Equals(child.Id, targetId, StringComparison.Ordinal)) return true;
                if (IsAlwaysAvailableDescendant(child, targetId)) return true;
            }
            return false;
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

        void CheckString(
            string? value,
            string path,
            ProtocolValidationIdentifierKind? fieldKind = null)
        {
            if (value?.Length > ProtocolConstants.MaximumStringLength)
                AddIdentifierOrPlain(path, "too_long",
                    $"Text may not exceed {ProtocolConstants.MaximumStringLength} characters.",
                    fieldKind, ProtocolValidationIdentifierState.UnsafeValue, value);
        }

        void Add(string path, string code, string message,
            ProtocolValidationIdentifierContext? identifierContext = null) =>
            errors.Add(new(path, code, message) { IdentifierContext = identifierContext });

        void AddIdentifier(
            string path,
            string code,
            string message,
            ProtocolValidationIdentifierKind fieldKind,
            ProtocolValidationIdentifierState state,
            string? identifier) => Add(path, code, message,
                ProtocolValidationIdentifierContext.Create(fieldKind, state, identifier));

        void AddIdentifierOrPlain(
            string path,
            string code,
            string message,
            ProtocolValidationIdentifierKind? fieldKind,
            ProtocolValidationIdentifierState state,
            string? identifier)
        {
            if (fieldKind is { } typedField)
                AddIdentifier(path, code, message, typedField, state, identifier);
            else
                Add(path, code, message);
        }
    }

    private static bool IsNormalizedPackageAssetPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > ProtocolConstants.MaximumEmbeddedMediaResourcePathLength ||
            value.StartsWith("/", StringComparison.Ordinal) || value.Contains('\\') ||
            value.Any(char.IsControl)) return false;
        var segments = value.Split('/');
        return segments.All(segment => segment.Length != 0 && segment is not "." and not ".." &&
            segment.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'));
    }

    private static bool IsEmbeddedMediaContentType(string? value) => value is
        "text/html" or "text/css" or "text/javascript" or "application/javascript" or
        "image/png" or "image/jpeg" or "image/webp" or
        "audio/wav" or "audio/mpeg" or "audio/ogg" or "video/mp4";

    private static bool IsDashboardQuickActionButton(ControllerButton button) => button is
        ControllerButton.X or
        ControllerButton.LeftBumper or ControllerButton.RightBumper or
        ControllerButton.LeftTrigger or ControllerButton.RightTrigger or
        ControllerButton.LeftStick or ControllerButton.RightStick or
        ControllerButton.Menu;

    private static bool IsOpenWidgetShortcutButton(ControllerButton button) => button is not (
        ControllerButton.A or
        ControllerButton.View or
        ControllerButton.DPadUp or ControllerButton.DPadDown or
        ControllerButton.DPadLeft or ControllerButton.DPadRight);

    private static bool IsRepeatableDiscreteActionButton(ControllerButton button) => button is
        ControllerButton.X or ControllerButton.Y or
        ControllerButton.LeftBumper or ControllerButton.RightBumper or
        ControllerButton.LeftTrigger or ControllerButton.RightTrigger or
        ControllerButton.LeftStick or ControllerButton.RightStick;

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

    internal static bool IsValidInlinePng(string? source)
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
