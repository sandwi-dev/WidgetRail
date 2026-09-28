namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>
/// Dispatcher-owned interaction barrier. Shares the shell lifecycle serializer;
/// a newer ownership intent revokes pending work, including away-and-back races.
/// This admits one captured interaction, never queues or replays an input.
/// </summary>
internal sealed class InteractionAdmission(SemaphoreSlim transitions)
{
    private long version;
    private object? acknowledgedOwner;

    internal void Invalidate()
    {
        ++version;
        acknowledgedOwner = null;
    }

    internal async Task<bool> EnsureAsync(object owner, Func<bool> isCurrent,
        Func<CancellationToken, Task> establishInteractive, CancellationToken cancellationToken)
    {
        var capturedVersion = version;
        await transitions.WaitAsync(cancellationToken);
        try
        {
            if (version != capturedVersion || !isCurrent()) return false;
            if (!Equals(acknowledgedOwner, owner))
            {
                await establishInteractive(cancellationToken);
                if (version != capturedVersion || !isCurrent()) return false;
                acknowledgedOwner = owner;
            }
            return true;
        }
        finally { transitions.Release(); }
    }
}
