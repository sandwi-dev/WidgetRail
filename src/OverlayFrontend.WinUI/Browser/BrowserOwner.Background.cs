namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    private readonly HashSet<BrowserSurface> backgroundChanges = [];
    internal event Action? ActivityChanged;
    internal bool NeedsAttention(string widgetId) => !retired && entries.Any(pair => pair.Key.Widget == widgetId && pair.Value.Surface.HasFailedDocument);
    internal bool PlayingAudio(string widgetId) => !retired && entries.Any(pair => pair.Key.Widget == widgetId && pair.Value.Surface.PlayingAudio);
    internal bool HasRunningSession(string widgetId) => entries.Any(pair => pair.Key.Widget == widgetId && (pair.Value.Retained || pair.Value.Surface.BackgroundFaulted));
    internal int BackgroundSessionCount => entries.Values.Count(entry => entry.Retained && entry.Surface.KeepsBackgroundSession);
    internal int FailedBackgroundSessionCount => entries.Values.Count(entry => entry.Surface.BackgroundFaulted);
    internal string BackgroundSessionNames => string.Join(", ", entries.Values.Where(entry => entry.Retained || entry.Surface.BackgroundFaulted).Select(entry => entry.Surface.SessionName));

    private async Task<bool> SynchronizeRetentionAsync((string Widget, string Document) key, Entry entry)
    {
        if (backgroundChanges.Contains(entry.Surface)) return true;
        await entry.SessionGate.WaitAsync(lifetime.Token);
        try
        {
            if (!ReferenceEquals(entries.GetValueOrDefault(key), entry)) return false;
            var wanted = entry.Surface.KeepsBackgroundSession;
            if (wanted == entry.Retained) return true;
            if (wanted) entry.RetentionAttempted = true;
            var retained = await RequestRetentionAsync(entry, key.Document, wanted);
            entry.RetentionAttempted = retained;
            if (wanted && !retained) return false;
            entry.Retained = retained;
            ActivityChanged?.Invoke();
            return true;
        }
        catch (OperationCanceledException) when (retired) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Diagnostics.FrontendFailureLog.Current.Write("browser-retention", error); return false; }
        finally { entry.SessionGate.Release(); }
    }

    internal async Task StopWidgetSessionsAsync(string widgetId)
    {
        foreach (var entry in entries.Where(pair => pair.Key.Widget == widgetId).Select(pair => pair.Value).ToArray())
            await ChangeRunningAsync(entry.Surface, false);
    }

    private async Task<bool> ChangeRunningAsync(BrowserSurface source, bool running)
    {
        var pair = entries.FirstOrDefault(pair => ReferenceEquals(pair.Value.Surface, source));
        if (retired || pair.Value is not { } entry || !backgroundChanges.Add(source)) return false;
        var admitted = false;
        try
        {
            await entry.SessionGate.WaitAsync(lifetime.Token); admitted = true;
            if (!ReferenceEquals(entries.GetValueOrDefault(pair.Key), entry) || !session.IsWebBrowserSessionCurrent(entry.Authority, pair.Key.Document)) return false;
            if (running)
            {
                var wanted = source.RequestsBackgroundSession;
                if (wanted)
                {
                    entry.RetentionAttempted = true;
                    var retained = await RequestRetentionAsync(entry, pair.Key.Document, true);
                    entry.RetentionAttempted = retained;
                    if (!retained) return false;
                }
                entry.Retained = wanted;
                source.RestartStoppedDocument();
            }
            else
            {
                await source.StopDocumentAsync(lifetime.Token);
                if (entry.Retained || entry.RetentionAttempted) await RequestRetentionAsync(entry, pair.Key.Document, false);
                entry.Retained = false;
                entry.RetentionAttempted = false;
            }
            ActivityChanged?.Invoke();
            return true;
        }
        finally
        {
            if (admitted) entry.SessionGate.Release();
            backgroundChanges.Remove(source);
            ActivityChanged?.Invoke();
            Refresh();
        }
    }

    private async Task<bool> RequestRetentionAsync(Entry entry, string document, bool retain)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        return await session.SetWebBrowserRetentionAsync(entry.Authority, document, retain, deadline.Token);
    }

    private async Task RecoverAsync(BrowserSurface source)
    {
        // Reuse the placement actor; never tear down a controller while it is
        // being reparented between the main and pinned XamlRoots.
        while (reconciliation is { IsCompleted: false } pending) await pending;
        if (retired || !source.NeedsRecovery) return;
        var pair = entries.FirstOrDefault(pair => ReferenceEquals(pair.Value.Surface, source));
        if (pair.Value is not { } entry) return;
        await RetireAsync(pair.Key, entry);
        Refresh();
    }
}
