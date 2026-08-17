namespace WidgetRail.WidgetSdk;

/// <summary>
/// Runs bounded, non-overlapping update work for the lifetime of an active
/// widget. The host-provided activation token is the authority for stopping
/// the loop; callers must not replace it with a process-lifetime token.
/// </summary>
public static class WidgetTicker
{
    /// <summary>Maximum public update rate: four ticks per second.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Longest useful periodic interval accepted by this helper.</summary>
    public static readonly TimeSpan MaximumInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Runs <paramref name="tickAsync"/> serially until the activation lifetime
    /// is cancelled. Normal lifetime cancellation completes successfully.
    /// Callback failures are not swallowed and fault the returned task.
    /// </summary>
    public static async Task RunWhileActiveAsync(
        TimeSpan interval,
        Func<CancellationToken, ValueTask> tickAsync,
        CancellationToken activeLifetime,
        bool tickImmediately = false,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(tickAsync);
        if (interval < MinimumInterval || interval > MaximumInterval)
            throw new ArgumentOutOfRangeException(
                nameof(interval), interval,
                $"Periodic widget updates must be between {MinimumInterval.TotalMilliseconds:0} ms " +
                $"and {MaximumInterval.TotalHours:0} hour.");

        if (activeLifetime.IsCancellationRequested) return;

        try
        {
            if (tickImmediately)
                await tickAsync(activeLifetime).ConfigureAwait(false);

            using var timer = new PeriodicTimer(interval, timeProvider ?? TimeProvider.System);
            while (await timer.WaitForNextTickAsync(activeLifetime).ConfigureAwait(false))
                await tickAsync(activeLifetime).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (activeLifetime.IsCancellationRequested)
        {
            // Deactivation is the normal completion path.
        }
    }
}

public abstract partial class Widget
{
    /// <summary>
    /// Starts serial update work tied to the current active lifetime. After a
    /// successful tick, the widget is invalidated unless explicitly disabled.
    /// Store or observe the returned task so callback failures remain visible.
    /// </summary>
    protected Task RunPeriodicUpdatesWhileActiveAsync(
        TimeSpan interval,
        Func<CancellationToken, ValueTask> updateAsync,
        bool tickImmediately = false,
        bool invalidateAfterTick = true)
    {
        if (!IsActive)
            throw new InvalidOperationException(
                "Periodic updates can only be started from an active widget lifecycle.");

        return WidgetTicker.RunWhileActiveAsync(
            interval,
            async activeLifetime =>
            {
                await updateAsync(activeLifetime).ConfigureAwait(false);
                if (invalidateAfterTick && !activeLifetime.IsCancellationRequested)
                    Invalidate();
            },
            ActiveCancellationToken,
            tickImmediately);
    }

    /// <summary>
    /// Invalidates the view periodically while active without performing
    /// additional update work. Useful for clocks and locally interpolated
    /// playback progress.
    /// </summary>
    protected Task InvalidatePeriodicallyWhileActiveAsync(
        TimeSpan interval,
        bool tickImmediately = false) =>
        RunPeriodicUpdatesWhileActiveAsync(
            interval,
            static _ => ValueTask.CompletedTask,
            tickImmediately);
}
