using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private CancellationTokenSource? preferredEntryLookup;

    internal bool EnterPreferred(string key, IndexedCollectionFocusTarget? fallback, FocusNavigationDirection direction = FocusNavigationDirection.None)
    {
        CancelContinuity(); CancelNavigation();
        if (disposed || source is not { Declaration.IndexedCollection: { CanLocateItems: true, Count: > 0 } } owner ||
            view is null || !CanReceiveInput) return false;
        if (fallback is { } known && known.ItemKey == key && MatchesQuery(known))
            return BeginEntry(known.Index, known, direction: direction);
        if (!ParkCollectionFocus()) return false;
        entering = true;
        var lookup = preferredEntryLookup = new();
        _ = LocatePreferredEntryAsync(owner, key, fallback, lookup, direction);
        return true;
    }

    private async Task LocatePreferredEntryAsync(Collections.WidgetIndexedRows owner, string key,
        IndexedCollectionFocusTarget? fallback, CancellationTokenSource lookup, FocusNavigationDirection direction)
    {
        var query = owner.Declaration.IndexedCollection!;
        var token = lookup.Token;
        bool Current() => !disposed && CanReceiveInput && ReferenceEquals(source, owner) &&
            ReferenceEquals(preferredEntryLookup, lookup) && !token.IsCancellationRequested &&
            owner.Declaration.IndexedCollection == query && owner.Declaration.IndexedPreferredEntryKey == key && IsFocusParked;
        try
        {
            var index = await owner.LocateAsync(key, token);
            if (!Current()) return;
            // BeginEntry uses the ordinary exact-key realization check. Lookup
            // identifies a position; it never grants row input authority.
            if (index is { } found)
                BeginEntry(found, new(owner.Declaration.Id, query.SourceId, query.QueryGeneration, key, found), direction: direction);
            else Enter(fallback, allowFallback: true, direction: direction);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || !Current()) { }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            if (Current())
            {
                Enter(fallback, allowFallback: true, direction: direction);
                Diagnostics.FrontendFailureLog.Current.Write("indexed-preferred-entry", error);
            }
        }
        finally
        {
            if (ReferenceEquals(preferredEntryLookup, lookup)) CancelNavigation();
            lookup.Dispose();
        }
    }

    private void CancelPreferredEntry()
    {
        var pending = preferredEntryLookup; preferredEntryLookup = null;
        pending?.Cancel(); // Async completion owns disposal after using its token.
    }
}
