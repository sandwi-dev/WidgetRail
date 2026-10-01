namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// Publishes one complete guide from a ready UI context. Input eligibility is
/// immediate; visual changes never mix different presentation states.
/// </summary>
internal sealed class ControllerGuidePublication
{
    private readonly ControllerGuideStability stability = new();
    private object? context;
    private bool initialized;
    private bool ready;

    internal IReadOnlyList<ControllerGuideHint> Displayed { get; private set; } = [];
    internal IReadOnlyList<ControllerGuideHint> Latest { get; private set; } = [];

    /// <returns>Whether the composed displayed hints changed.</returns>
    internal bool Update(object context, bool ready, IReadOnlyList<ControllerGuideHint> contextual,
        IReadOnlyList<ControllerGuideHint> host, long now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(contextual);
        ArgumentNullException.ThrowIfNull(host);
        var sameContext = initialized && Equals(this.context, context);
        var wasReady = this.ready;
        var hadPresentation = initialized;
        this.context = context;
        this.ready = ready;
        initialized = true;

        // Input eligibility follows current authority immediately, independently
        // of the informational pixels retained through a presentation handoff.
        Latest = Combine(ready ? contextual : [], host);
        if (!ready)
        {
            // Keep the entire last guide, not old widget actions mixed with new
            // shell actions. Cancel any candidate which has not reached display.
            var retained = hadPresentation ? Displayed : Latest;
            stability.Reset();
            stability.Propose(retained, context, now);
        }
        else
        {
            if (!sameContext || !wasReady) stability.Reset();
            stability.Propose(Latest, context, now);
        }
        return Publish();
    }

    internal int Remaining(long now) => ready ? stability.Remaining(now) : 0;

    internal bool Commit(long now) => ready && stability.Commit(now) && Publish();

    internal void Reset()
    {
        stability.Reset();
        context = null;
        initialized = ready = false;
        Displayed = Latest = [];
    }

    private bool Publish()
    {
        var changed = !Displayed.SequenceEqual(stability.Displayed);
        Displayed = stability.Displayed;
        return changed;
    }

    private static IReadOnlyList<ControllerGuideHint> Combine(IReadOnlyList<ControllerGuideHint> contextual,
        IReadOnlyList<ControllerGuideHint> host)
    {
        var result = new List<ControllerGuideHint>(contextual.Count + host.Count);
        foreach (var hint in contextual.Concat(host))
            if (!result.Any(existing => Collides(existing, hint)))
                result.Add(hint);
        return result.ToArray();
    }

    private static bool Collides(ControllerGuideHint first, ControllerGuideHint second) =>
        first.Prompt == second.Prompt || first.Button is { } button && second.Button == button;
}
