namespace WidgetRail.Samples.PlayniteLibrary;

/// <summary>
/// An exact game and its logical owner captured when an action is accepted for
/// execution. Availability belongs to the collection/query, not a native control.
/// Delayed operations can recheck that owner without re-resolving a control ID.
/// </summary>
internal sealed class PlayniteLibraryGameTarget(
    PlayniteLibraryItem item, Func<bool> isCurrent, Action? selectAnchor = null)
{
    internal PlayniteLibraryItem Item { get; } = item;
    internal bool IsCurrent => isCurrent();
    internal void SelectAnchor() => selectAnchor?.Invoke();
    internal PlayniteLibraryDisplayItem Display => new(Item.Value.SavedId,
        Item.Presentation.DisplayName, Item.Presentation.Source.DisplayName);
}
