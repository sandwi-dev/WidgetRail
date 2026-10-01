namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Retain informational hints until a change survives a brief quiet period.</summary>
internal sealed class ControllerGuideStability
{
    internal const int SettleMilliseconds = 120;
    internal IReadOnlyList<ControllerGuideHint> Displayed { get; private set; } = [];
    internal IReadOnlyList<ControllerGuideHint> Latest { get; private set; } = [];
    private object? context;
    private bool initialized;
    private long? due;

    internal bool Propose(IReadOnlyList<ControllerGuideHint> hints, object? owner, long now)
    {
        if (!initialized || owner is null || !Equals(context, owner))
        {
            initialized = true; context = owner; due = null;
            Displayed = Latest = hints.ToArray();
            return true;
        }
        var changed = !Latest.SequenceEqual(hints);
        if (changed) Latest = hints.ToArray();
        if (Displayed.SequenceEqual(Latest)) due = null;
        else if (changed || due is null) due = now + SettleMilliseconds;
        return false;
    }

    internal int Remaining(long now) => due is { } deadline ? (int)Math.Max(1, deadline - now) : 0;
    internal bool Commit(long now)
    {
        if (due is not { } deadline || now < deadline) return false;
        Displayed = Latest; due = null; return true;
    }
    internal void Reset() { initialized = false; due = null; }
}
