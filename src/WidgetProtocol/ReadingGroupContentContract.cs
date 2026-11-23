namespace WidgetRail.WidgetProtocol;

// The host may render these as real child controls. Keeping this predicate in
// the protocol aligns authoring validation and trusted action admission.
internal static class ReadingGroupContentContract
{
    internal static bool IsSavedPreview(MediaPlayerDefinition? media) => media is
        { Presentation: MediaPlayerPresentation.Interactive, Source.Kind: MediaPlayerSourceKind.SavedCapture,
          Gallery: null, Options.AutoPlay: false, Options.IsAnimation: false };
    internal static bool IsInteractivePlayer(MediaPlayerDefinition? media) =>
        media?.Presentation == MediaPlayerPresentation.Inline || IsSavedPreview(media);
    internal static bool IsHorizontalViewport(ViewNode node) =>
        node.Kind == ViewNodeKind.Scroll && node.ScrollAxis == ScrollAxis.Horizontal &&
        node.CollectionLayout is null && node.ScrollNearStartActionId is null && node.ScrollNearEndActionId is null &&
        node.CollectionNavigation is null && node.CollectionAnchorKey is null && node.CollectionLoading is null &&
        node.VirtualCollectionWindow is null && node.CollectionGeneration is null && node.CollectionResetGeneration is null;

    internal static bool IsContent(ViewNode node) =>
        (node.Kind is ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Grid or ViewNodeKind.Text or ViewNodeKind.RichText or
            ViewNodeKind.Image or ViewNodeKind.Icon or ViewNodeKind.Spacer or ViewNodeKind.Progress or ViewNodeKind.LineGraph or
            ViewNodeKind.LoadingIndicator or ViewNodeKind.ControllerGlyph or ViewNodeKind.Button or ViewNodeKind.ActionSurface ||
            node.Kind==ViewNodeKind.MediaPlayer && (node.MediaPlayer?.Presentation == MediaPlayerPresentation.Animation || IsInteractivePlayer(node.MediaPlayer)) ||
            IsHorizontalViewport(node)) &&
        !node.IsReadingGroup && node.FocusPresentation is null && node.DefaultFocusPresentation is null && node.FocusPreview is null &&
        node.FocusPreviewSource is null && node.FocusPreviewKey is null && node.ContextActions?.Count==0 && node.ContextMenuButton is null &&
        node.Disclosure is null && node.Transition is null && node.AnchoredPresentation is null;

    internal static bool IsInlineAction(ViewNode node) =>
        node.Kind is ViewNodeKind.Button or ViewNodeKind.ActionSurface && node.ActionId is not null &&
        IsInlineControl(node);

    internal static bool IsInlineControl(ViewNode node) =>
        (node.Kind is ViewNodeKind.Button or ViewNodeKind.ActionSurface && node.ActionId is not null ||
            node.Kind==ViewNodeKind.MediaPlayer && IsInteractivePlayer(node.MediaPlayer) && node.ActionId is null) &&
        !node.IsReadingGroup && node.InputScopeId is null && node.Focus is null && node.FocusPersistenceId is null &&
        node.Shortcuts?.Count == 0 && node.ContextActions?.Count == 0 && node.ContextMenuButton is null &&
        node.FocusPresentation is null && node.DefaultFocusPresentation is null && node.FocusPreview is null &&
        node.FocusPreviewSource is null && node.FocusPreviewKey is null && node.Disclosure is null;
}
