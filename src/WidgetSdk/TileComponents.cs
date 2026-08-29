using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// One safe leading visual for a rich tile. A tile can use either a closed
/// semantic glyph, one absolute credential-free HTTPS image, one bounded
/// inline PNG, or one host-resolved opaque artwork handle; it cannot combine
/// visual sources.
/// </summary>
public sealed class TileArtwork
{
    private TileArtwork(
        WidgetGlyph? glyph,
        string? imageSource,
        WidgetArtworkHandle? artworkHandle,
        ImageFit imageFit,
        string accessibilityLabel)
    {
        Glyph = glyph;
        ImageSource = imageSource;
        ArtworkHandle = artworkHandle;
        ImageFit = imageFit;
        AccessibilityLabel = ValidateLabel(accessibilityLabel, nameof(accessibilityLabel));
    }

    internal WidgetGlyph? Glyph { get; }
    internal string? ImageSource { get; }
    internal WidgetArtworkHandle? ArtworkHandle { get; }
    internal ImageFit ImageFit { get; }
    internal string AccessibilityLabel { get; }

    public static TileArtwork FromGlyph(WidgetGlyph glyph, string accessibilityLabel)
    {
        if (!Enum.IsDefined(glyph)) throw new ArgumentOutOfRangeException(nameof(glyph));
        return new TileArtwork(glyph, null, null, ImageFit.Contain, accessibilityLabel);
    }

    public static TileArtwork FromHttps(
        string source,
        string accessibilityLabel,
        ImageFit fit = ImageFit.Cover)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!Enum.IsDefined(fit)) throw new ArgumentOutOfRangeException(nameof(fit));
        if (source.Length > ProtocolConstants.MaximumStringLength ||
            !Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException(
                "Tile artwork must be an absolute HTTPS URL without embedded credentials.",
                nameof(source));
        return new TileArtwork(null, source, null, fit, accessibilityLabel);
    }

    public static TileArtwork FromInlinePng(
        string pngBase64,
        string accessibilityLabel,
        ImageFit fit = ImageFit.Cover)
    {
        if (!Enum.IsDefined(fit)) throw new ArgumentOutOfRangeException(nameof(fit));
        return new TileArtwork(
            null,
            UI.CanonicalInlinePngSource(pngBase64, nameof(pngBase64)),
            null,
            fit,
            accessibilityLabel);
    }

    public static TileArtwork FromHandle(
        WidgetArtworkHandle handle,
        string accessibilityLabel,
        ImageFit fit = ImageFit.Cover)
    {
        if (!Enum.IsDefined(fit)) throw new ArgumentOutOfRangeException(nameof(fit));
        return new TileArtwork(null, null, handle, fit, accessibilityLabel);
    }

    private static string ValidateLabel(string? value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > ProtocolConstants.MaximumStringLength)
            throw new ArgumentException(
                $"Artwork accessibility text may not exceed {ProtocolConstants.MaximumStringLength} characters.",
                parameterName);
        return value;
    }
}

/// <summary>
/// A rich content container that is itself the only focus, pointer, pressed,
/// and action target. Descendants are strictly presentational, preventing the
/// split focus geometry produced by a card containing a separate small button.
/// </summary>
public sealed record ActionSurfaceElement : WidgetElement
{
    internal ActionSurfaceElement(
        string id,
        string actionId,
        string accessibilityLabel,
        ActionSurfaceOrientation orientation,
        IReadOnlyList<WidgetElement> children) : base(RequireId(id))
    {
        StableIdentifier.Validate(id, nameof(id));
        StableIdentifier.Validate(actionId, nameof(actionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(accessibilityLabel);
        if (accessibilityLabel.Length > ProtocolConstants.MaximumStringLength)
            throw new ArgumentException(
                $"Accessibility text may not exceed {ProtocolConstants.MaximumStringLength} characters.",
                nameof(accessibilityLabel));
        if (!Enum.IsDefined(orientation))
            throw new ArgumentOutOfRangeException(nameof(orientation));
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count is < 1 or > ProtocolConstants.MaximumActionSurfaceDirectChildren)
            throw new ArgumentException(
                $"An action surface requires 1-{ProtocolConstants.MaximumActionSurfaceDirectChildren} direct children.",
                nameof(children));
        if (children.Any(child => child is null))
            throw new ArgumentException("Action-surface children cannot contain null values.", nameof(children));
        var descendantCount = 0;
        foreach (var child in children)
            ValidatePresentationalNode(
                child.ToProtocolNode(), nameof(children), 1, ref descendantCount);

        ActionId = actionId;
        AccessibilityLabel = accessibilityLabel;
        Orientation = orientation;
        Children = children.ToArray();
        StyleClasses = ["wrail-action-surface"];
    }

    public string ActionId { get; init; }
    public string AccessibilityLabel { get; init; }
    public ActionSurfaceOrientation Orientation { get; init; }
    public IReadOnlyList<WidgetElement> Children { get; init; }
    public bool? IsDisabled { get; init; }
    public bool? IsSelected { get; init; }
    public bool? IsBusy { get; init; }
    /// <inheritdoc cref="ButtonElement.FocusPersistenceId"/>
    public string? FocusPersistenceId { get; init; }
    public FocusNeighbors? FocusNeighbors { get; init; }
    public IReadOnlyList<ControllerShortcut> Shortcuts { get; init; } = [];

    public ActionSurfaceElement FocusUp(string id) => this with
    {
        FocusNeighbors = (FocusNeighbors ?? new()) with { Up = RequireId(id) },
    };
    public ActionSurfaceElement FocusDown(string id) => this with
    {
        FocusNeighbors = (FocusNeighbors ?? new()) with { Down = RequireId(id) },
    };
    public ActionSurfaceElement FocusLeft(string id) => this with
    {
        FocusNeighbors = (FocusNeighbors ?? new()) with { Left = RequireId(id) },
    };
    public ActionSurfaceElement FocusRight(string id) => this with
    {
        FocusNeighbors = (FocusNeighbors ?? new()) with { Right = RequireId(id) },
    };
    public ActionSurfaceElement Disabled(bool disabled = true) => this with
    {
        IsDisabled = disabled ? true : null,
    };
    public ActionSurfaceElement Selected(bool selected = true) => this with
    {
        IsSelected = selected ? true : null,
    };
    public ActionSurfaceElement Busy(bool busy = true) => this with
    {
        IsBusy = busy ? true : null,
    };
    public ActionSurfaceElement PersistFocusAs(string id)
    {
        StableIdentifier.Validate(id, nameof(id));
        return this with { FocusPersistenceId = id };
    }
    public ActionSurfaceElement Shortcut(
        ControllerButton button,
        ControllerEventPhase phase = ControllerEventPhase.Pressed,
        string? actionId = null) => this with
        {
            Shortcuts =
            [
                .. Shortcuts,
                new ControllerShortcut(button, actionId ?? ActionId, phase),
            ],
        };

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.ActionSurface,
        AccessibilityLabel = AccessibilityLabel,
        ActionId = ActionId,
        ActionSurfaceOrientation = Orientation,
        IsDisabled = IsDisabled,
        IsSelected = IsSelected,
        IsBusy = IsBusy,
        FocusPersistenceId = FocusPersistenceId,
        Focus = FocusNeighbors,
        Shortcuts = Shortcuts,
        StyleClasses = StyleClasses,
        Children = Children.Select(child => child.ToProtocolNode()).ToArray(),
    };

    private static void ValidatePresentationalNode(
        ViewNode node,
        string parameterName,
        int relativeDepth,
        ref int descendantCount)
    {
        descendantCount++;
        if (descendantCount > ProtocolConstants.MaximumActionSurfaceDescendants)
            throw new ArgumentException(
                $"An action surface may contain at most {ProtocolConstants.MaximumActionSurfaceDescendants} descendants.",
                parameterName);
        if (relativeDepth > ProtocolConstants.MaximumActionSurfaceRelativeDepth)
            throw new ArgumentException(
                $"Action-surface content may be at most {ProtocolConstants.MaximumActionSurfaceRelativeDepth} levels deep relative to its surface.",
                parameterName);
        if (node.Kind is ViewNodeKind.Button or ViewNodeKind.Slider or
            ViewNodeKind.ActionSurface or ViewNodeKind.Scroll ||
            node.ActionId is not null || node.ValueChangedActionId is not null ||
            node.Focus is not null || node.InputScopeId is not null ||
            node.Shortcuts.Count != 0 || node.IsDisabled is not null ||
            node.IsSelected is not null || node.IsBusy is not null)
            throw new ArgumentException(
                "Action-surface descendants must be presentational and cannot own focus, actions, scopes, scrolling, shortcuts, or interaction state.",
                parameterName);
        foreach (var child in node.Children)
            ValidatePresentationalNode(
                child, parameterName, relativeDepth + 1, ref descendantCount);
    }
}

public static partial class UI
{
    public const int MaximumTileTitleCharacters = 160;
    public const int MaximumTileMetadataCharacters = 512;
    public const int MaximumTileStateCharacters = 120;

    /// <summary>
    /// Creates one rich focus/action surface from presentational children.
    /// This is the low-level escape hatch for custom rich controls; nested
    /// buttons, sliders, action surfaces, scroll regions, and input scopes are
    /// rejected eagerly.
    /// </summary>
    public static ActionSurfaceElement ActionSurface(
        string action,
        string id,
        string accessibilityLabel,
        ActionSurfaceOrientation orientation,
        params WidgetElement[] children)
    {
        ArgumentNullException.ThrowIfNull(children);
        return new ActionSurfaceElement(
            id, action, accessibilityLabel, orientation, children.ToArray());
    }

    /// <summary>
    /// Creates a controller-first tile with separate title, subtitle, metadata,
    /// and visible state lines inside one full-tile focus target.
    /// </summary>
    public static ActionSurfaceElement Tile(
        string title,
        string stateLabel,
        string action,
        string id,
        string? subtitle = null,
        string? metadata = null,
        TileArtwork? artwork = null,
        string? accessibilityLabel = null,
        ActionSurfaceOrientation orientation = ActionSurfaceOrientation.Horizontal) =>
        BuildTile(
            title, stateLabel, action, id, subtitle, metadata,
            artwork, accessibilityLabel, orientation);

    private static ActionSurfaceElement BuildTile(
        string title,
        string stateLabel,
        string action,
        string id,
        string? subtitle,
        string? metadata,
        TileArtwork? artwork,
        string? accessibilityLabel,
        ActionSurfaceOrientation orientation)
    {
        ValidateTileText(title, nameof(title), MaximumTileTitleCharacters);
        ValidateTileText(stateLabel, nameof(stateLabel), MaximumTileStateCharacters);
        ValidateOptionalTileText(subtitle, nameof(subtitle));
        ValidateOptionalTileText(metadata, nameof(metadata));
        StableIdentifier.Validate(id, nameof(id));
        foreach (var suffix in new[] { "artwork", "content", "title", "subtitle", "metadata", "state" })
            _ = StableIdentifier.Child(id, suffix);

        var copy = new List<WidgetElement>
        {
            new TextElement(StableIdentifier.Child(id, "title"), title, title)
            {
                StyleClasses = ["wrail-tile__title"],
            },
        };
        if (subtitle is not null)
            copy.Add(new TextElement(StableIdentifier.Child(id, "subtitle"), subtitle, subtitle)
            {
                StyleClasses = ["wrail-tile__subtitle"],
            });
        if (metadata is not null)
            copy.Add(new TextElement(StableIdentifier.Child(id, "metadata"), metadata, metadata)
            {
                StyleClasses = ["wrail-tile__metadata"],
            });
        copy.Add(new TextElement(
            StableIdentifier.Child(id, "state"), stateLabel, $"State: {stateLabel}")
        {
            StyleClasses = ["wrail-tile__state"],
        });

        var children = new List<WidgetElement>();
        if (artwork is not null)
        {
            WidgetElement leading = artwork.Glyph is { } glyph
                ? new IconElement(
                    StableIdentifier.Child(id, "artwork"), glyph, artwork.AccessibilityLabel)
                : artwork.ArtworkHandle is { } handle
                    ? UI.Artwork(
                        handle, StableIdentifier.Child(id, "artwork"),
                        artwork.AccessibilityLabel, artwork.ImageFit)
                : new ImageElement(
                    StableIdentifier.Child(id, "artwork"), artwork.ImageSource!,
                    artwork.AccessibilityLabel, artwork.ImageFit);
            children.Add(leading with
            {
                StyleClasses = ["wrail-tile__artwork"],
            });
        }
        children.Add(new StackElement(StableIdentifier.Child(id, "content"), copy)
        {
            StyleClasses = ["wrail-tile__content"],
        });

        var spoken = accessibilityLabel ?? string.Join(", ",
            new[] { title, subtitle, metadata, stateLabel }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        ValidateTileText(spoken, nameof(accessibilityLabel), ProtocolConstants.MaximumStringLength);
        return new ActionSurfaceElement(id, action, spoken, orientation, children)
        {
            StyleClasses = ["wrail-action-surface", "wrail-tile"],
        };
    }

    private static void ValidateTileText(string? value, string parameterName, int maximumCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumCharacters)
            throw new ArgumentException(
                $"Tile text may not exceed {maximumCharacters} characters.", parameterName);
    }

    private static void ValidateOptionalTileText(string? value, string parameterName)
    {
        if (value is null) return;
        ValidateTileText(value, parameterName, MaximumTileMetadataCharacters);
    }
}
