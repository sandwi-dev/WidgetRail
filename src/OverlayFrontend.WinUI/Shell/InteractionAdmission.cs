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
        cancellationToken.ThrowIfCancellationRequested();
        var capturedVersion = version;
        if (!isCurrent()) return false;
        // The current widget's acknowledged lifetime is independent of another
        // widget's awaited retirement. Never replay an input after that cleanup.
        if (Equals(acknowledgedOwner, owner)) return true;
        await transitions.WaitAsync(cancellationToken);
        try
        {
            if (version != capturedVersion || !isCurrent()) return false;
            return await EstablishSerializedAsync(owner, isCurrent, establishInteractive, cancellationToken);
        }
        finally { transitions.Release(); }
    }

    /// <summary>
    /// The caller already owns the lifecycle serializer. Prepare a real worker
    /// acknowledgment before publishing input ownership, retaining all epoch and
    /// current-intent checks used by ordinary interaction admission.
    /// </summary>
    internal async Task<bool> EstablishSerializedAsync(object owner, Func<bool> isCurrent,
        Func<CancellationToken, Task> establishInteractive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capturedVersion = version;
        if (!isCurrent()) return false;
        if (Equals(acknowledgedOwner, owner)) return true;
        await establishInteractive(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (version != capturedVersion || !isCurrent()) return false;
        acknowledgedOwner = owner;
        return true;
    }
}
