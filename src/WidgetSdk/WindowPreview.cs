using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>View-only live content. The enclosing action surface owns input.</summary>
public sealed record WindowPreviewElement : WidgetElement
{
    internal WindowPreviewElement(string windowId, string id, string accessibilityLabel,
        ImageFit fit, double aspectRatio) : base(RequireId(id))
    {
        if (string.IsNullOrWhiteSpace(windowId) || windowId.Length > 128 ||
            windowId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            throw new ArgumentException("Use a window ID returned by TaskSwitcher.GetWindowsAsync.", nameof(windowId));
        ArgumentException.ThrowIfNullOrWhiteSpace(accessibilityLabel);
        if (!Enum.IsDefined(fit)) throw new ArgumentOutOfRangeException(nameof(fit));
        if (!double.IsFinite(aspectRatio) || aspectRatio < 0.25 || aspectRatio > 4)
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        WindowId = windowId;
        AccessibilityLabel = accessibilityLabel;
        Fit = fit;
        AspectRatio = aspectRatio;
        RequiredStyleClasses = ["wrail-window-preview"];
    }

    public string WindowId { get; init; }
    public string AccessibilityLabel { get; init; }
    public ImageFit Fit { get; init; }
    public double AspectRatio { get; init; }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.WindowPreview,
        WindowId = WindowId,
        PreviewAspectRatio = AspectRatio,
        ImageFit = Fit,
        AccessibilityLabel = AccessibilityLabel,
        StyleClasses = StyleClasses,
    };
}

public static partial class UI
{
    /// <summary>
    /// Embeds a live application window. Requires system.apps.windows.preview.v1.
    /// Unavailable, protected, minimized or resource-limited sources show a fallback.
    /// No pixels or native window handles are exposed to the widget.
    /// </summary>
    public static WindowPreviewElement WindowPreview(string windowId, string id,
        string accessibilityLabel, ImageFit fit = ImageFit.Contain,
        double aspectRatio = 16.0 / 9.0) =>
        new(windowId, id, accessibilityLabel, fit, aspectRatio);

    /// <summary>Creates a poster with reusable view-only content and separate themed copy.</summary>
    public static ActionSurfaceElement PosterTile(string title, string stateLabel,
        string action, string id, WidgetElement content, string? subtitle = null,
        string? accessibilityLabel = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content is not (WindowPreviewElement or ImageElement))
            throw new ArgumentException("Poster content must be an image or window preview.", nameof(content));
        var tile = BuildTile(title, stateLabel, action, id, subtitle, null, null,
            accessibilityLabel, ActionSurfaceOrientation.Vertical);
        return new ActionSurfaceElement(id, action, tile.AccessibilityLabel,
            ActionSurfaceOrientation.Vertical, [content, .. tile.Children])
        {
            RequiredStyleClasses = ["wrail-action-surface", "wrail-tile", "wrail-preview-poster"],
        };
    }
}
