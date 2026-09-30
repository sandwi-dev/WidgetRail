using System.Runtime.CompilerServices;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    private sealed record ArtworkDeclarations(WidgetPresentationFrame Frame, Dictionary<string, long> SinceSequence);
    private readonly Dictionary<string, ArtworkDeclarations> _ordinaryArtworkDeclarations = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<WidgetPresentationAuthority, object> _publishedArtworkAuthorities = new();
    private static readonly object PublishedArtworkAuthority = new();

    // Artwork completion belongs to a continuously declared handle, not to the
    // action/input snapshot. Check every publication so removal followed by an
    // identical declaration cannot revive an in-flight request.
    private void ReconcileOrdinaryArtworkLocked(string widgetId)
    {
        var frame = _states.GetValueOrDefault(widgetId)?.LastGood;
        if (frame is null) _ordinaryArtworkDeclarations.Remove(widgetId);
        else
        {
            _publishedArtworkAuthorities.GetValue(frame.Authority, _ => PublishedArtworkAuthority);
            var previous = _ordinaryArtworkDeclarations.GetValueOrDefault(widgetId);
            if (previous is null || !ReferenceEquals(previous.Frame.Snapshot, frame.Snapshot) ||
                !SameArtworkOwner(previous.Frame.Authority, frame.Authority))
            {
                var declarations = new Dictionary<string, long>(StringComparer.Ordinal);
                AddArtworkDeclarations(frame.Snapshot.Root, declarations, frame.Authority.SnapshotSequence);
                if (previous is not null && SameArtworkOwner(previous.Frame.Authority, frame.Authority))
                    foreach (var handle in declarations.Keys.ToArray())
                        if (previous.SinceSequence.TryGetValue(handle, out var since)) declarations[handle] = since;
                _ordinaryArtworkDeclarations[widgetId] = new(frame, declarations);
            }
        }
        foreach (var pair in _artwork.ToArray())
        {
            if (pair.Value.PinnedSelection is not null || pair.Value.Authority.WidgetId != widgetId) continue;
            try { DemandOrdinaryArtworkLocked(pair.Value.Authority, pair.Value.ArtworkHandle); }
            catch (WidgetPresentationSessionException error)
            {
                _artwork.Remove(pair.Key);
                pair.Value.Completion.TrySetException(error);
            }
        }
    }

    private void DemandOrdinaryArtworkLocked(WidgetPresentationAuthority origin, string handle)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ThrowIfTerminalLocked();
        if (!_publishedArtworkAuthorities.TryGetValue(origin, out _) ||
            !_ordinaryArtworkDeclarations.TryGetValue(origin.WidgetId, out var declarations) ||
            !SameArtworkOwner(origin, declarations.Frame.Authority) ||
            !declarations.SinceSequence.TryGetValue(handle, out var since) || origin.SnapshotSequence < since ||
            origin.SnapshotSequence > declarations.Frame.Authority.SnapshotSequence)
            throw new WidgetPresentationSessionException("stale_artwork_authority",
                "The artwork handle or its presentation owner retired.");
        _ = ValidateAuthority(_states[origin.WidgetId].LastGood!.Authority);
    }

    private static bool SameArtworkOwner(WidgetPresentationAuthority first, WidgetPresentationAuthority second) =>
        first.WidgetId == second.WidgetId && first.WidgetInstanceId == second.WidgetInstanceId &&
        first.RuntimeGeneration == second.RuntimeGeneration && first.PresentationGeneration == second.PresentationGeneration &&
        first.SessionGeneration == second.SessionGeneration && first.WorkerRun == second.WorkerRun;

    private static void AddArtworkDeclarations(ViewNode node, Dictionary<string, long> declarations, long sequence)
    {
        if (node.ArtworkHandle is { } artwork) declarations[artwork] = sequence;
        if (node.FocusBackgroundArtworkHandle is { } background) declarations[background] = sequence;
        foreach (var child in node.Children) AddArtworkDeclarations(child, declarations, sequence);
        if (node.FocusPresentation is { } focus) AddArtworkDeclarations(focus, declarations, sequence);
        if (node.DefaultFocusPresentation is { } fallback) AddArtworkDeclarations(fallback, declarations, sequence);
    }
}
