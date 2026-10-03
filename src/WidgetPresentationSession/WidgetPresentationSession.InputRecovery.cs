using System.Runtime.CompilerServices;

namespace WidgetRail.WidgetPresentationSession;

/// <summary>Expected retirement is not a widget failure. Never classify generic protocol errors here.</summary>
public static class WidgetInputFailure
{
    public static bool IsStale(Exception error) => error is WidgetPresentationSessionException session && session.Code is
        "snapshot_stale" or "input_scope_stale" or "presentation_stale" or "ordinary_input_stale" or
        "stale_controller_input_authority" or "pinned_input_stale" or "stale_pinned_input_authority" or
        "indexed_input_stale" or "indexed_retired" or "stale_indexed_input_authority";
}

public sealed partial class WidgetPresentationSession
{
    private readonly ConditionalWeakTable<WidgetPresentationFrame, object> _recoveredInputFrames = new();
    private readonly HashSet<string> _inputRefreshRequested = new(StringComparer.Ordinal);

    /// <summary>
    /// Recover the current owner after rejecting input, without replaying it. Coalesces
    /// with invalidation refresh and admits at most one recovery per displayed frame.
    /// </summary>
    public void RequestInputRefresh(WidgetPresentationFrame origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        using (_gate.Enter())
        {
            if (_disposed || _terminalFailure is not null || !_publishedInputFrames.TryGetValue(origin, out _) ||
                _states.GetValueOrDefault(origin.Authority.WidgetId)?.LastGood is not { } current ||
                !SameIndexedOwner(origin.Authority, current.Authority) ||
                _recoveredInputFrames.TryGetValue(origin, out _)) return;
            _recoveredInputFrames.Add(origin, PublishedInputFrameMarker);
            _inputRefreshRequested.Add(origin.Authority.WidgetId);
            StartInvalidationRefresh(origin.Authority.WidgetId);
        }
    }
}
