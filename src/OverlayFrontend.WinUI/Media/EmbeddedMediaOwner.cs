using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>
/// Shell lifetime owner for durable media. Presentation trees borrow viewport slots;
/// this owner alone closes documents. Native controllers and pending admissions share
/// the original host's four-session capacity, including parked sessions.
/// </summary>
internal sealed class EmbeddedMediaOwner : IAsyncDisposable
{
    internal const int MaximumResidentSessions = 4;
    private sealed class Entry(string widgetId)
    {
        internal string WidgetId { get; } = widgetId;
        internal CancellationTokenSource Lifetime { get; } = new();
        internal WidgetPresentationEmbeddedMediaDocument? Document;
        internal EmbeddedMediaSurface? Surface;
        internal Task? Resolving;
        internal bool IsResolving;
        internal long AttemptSequence;
    }
    private sealed record Viewport(WidgetPresentationAuthority Authority, string ElementId, string SessionId, WidgetMediaViewport Element);
    private readonly PresentationSession session;
    private readonly DispatcherQueue dispatcher;
    private readonly Grid parking;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Viewport> viewports = new(StringComparer.Ordinal);
    private readonly HashSet<Task> retirements = [];
    private readonly Dictionary<string, string> failures = new(StringComparer.Ordinal);
    private string? activeWidget;
    private bool visible;
    private bool inputEnabled;
    private bool retired;
    private bool reconciling;
    private bool reconcileAgain;
    private Task? disposal;
    internal Action<string, string>? Diagnostic { get; set; }
    internal Action<string>? BackRequested { get; set; }
    internal Action<string>? FailureChanged { get; set; }
    internal string? GetFailure(string widgetId) => failures.GetValueOrDefault(widgetId);
    internal int ResidentCount => entries.Count;
    internal int BrowserCreationCount { get; private set; }
    internal void Refresh() => Reconcile();
    internal bool IsReady(string widgetId) => entries.GetValueOrDefault(widgetId)?.Surface?.IsReady == true;
    internal bool IsParked(string widgetId) => entries.GetValueOrDefault(widgetId)?.Surface is { } surface && ReferenceEquals(surface.Element.Parent, parking);

    internal EmbeddedMediaOwner(PresentationSession session, Grid parking)
    {
        this.session = session;
        this.parking = parking;
        dispatcher = parking.DispatcherQueue;
        parking.Visibility = Visibility.Collapsed;
        parking.IsHitTestVisible = false;
        session.PresentationChanged += PresentationChanged;
        session.CatalogChanged += CatalogChanged;
    }

    internal void SetHostState(string? widgetId, bool isVisible, bool acceptsInput)
    {
        DemandDispatcher();
        if (retired) return;
        activeWidget = widgetId;
        visible = isVisible;
        inputEnabled = isVisible && acceptsInput;
        Reconcile();
    }

    internal void Bind(WidgetPresentationAuthority authority, string elementId, string sessionId, WidgetMediaViewport viewport)
    {
        DemandDispatcher();
        if (retired || viewport.Retired) return;
        if (viewports.TryGetValue(authority.WidgetId, out var old) && !ReferenceEquals(old.Element, viewport))
            old.Element.PlacementChanged = null;
        viewports[authority.WidgetId] = new(authority, elementId, sessionId, viewport);
        viewport.PlacementChanged = Reconcile;
        Reconcile();
    }

    internal void Unbind(WidgetMediaViewport viewport)
    {
        DemandDispatcher();
        foreach (var (id, bound) in viewports.ToArray())
            if (ReferenceEquals(bound.Element, viewport)) viewports.Remove(id);
        viewport.PlacementChanged = null;
        viewport.Retire();
        Reconcile();
    }

    private void PresentationChanged(object? sender, WidgetPresentationChangedEventArgs args) =>
        dispatcher.TryEnqueue(Reconcile);
    private void CatalogChanged(object? sender, WidgetRevisionChangedEventArgs args) =>
        dispatcher.TryEnqueue(Reconcile);

    private void Reconcile()
    {
        DemandDispatcher();
        if (retired) return;
        if (reconciling) { reconcileAgain = true; return; }
        reconciling = true;
        try
        {
            do
            {
                reconcileAgain = false;
                foreach (var id in failures.Keys.ToArray())
                    if (!entries.ContainsKey(id) && session.GetState(id)?.LastGood?.Snapshot.EmbeddedMediaSession is null)
                        SetFailure(id, null);
                foreach (var entry in entries.Values.ToArray())
                {
                    var state = session.GetState(entry.WidgetId);
                    if (state?.Failure is not null || state?.LastGood?.Snapshot.EmbeddedMediaSession is null ||
                        entry.Document is { } document && session.GetEmbeddedMediaState(document) is null)
                    { Retire(entry); continue; }
                    if (entry.Surface is { } surface)
                    {
                        SetFailure(entry.WidgetId, surface.FailureCode);
                        Place(entry, MatchingViewport(entry.WidgetId));
                        surface.Refresh();
                    }
                }
                if (visible && activeWidget is { } active && MatchingViewport(active) is { } target &&
                    session.GetState(active)?.LastGood is { } frame && frame.Snapshot.EmbeddedMediaSession is not null)
                {
                    if (!entries.TryGetValue(active, out var entry))
                    {
                        if (entries.Count + retirements.Count >= MaximumResidentSessions)
                        { SetFailure(active, "media-session-capacity"); continue; }
                        SetFailure(active, null);
                        entries.Add(active, entry = new(active));
                    }
                    if (entry.Document is null && !entry.IsResolving && entry.AttemptSequence != frame.Authority.SnapshotSequence)
                    {
                        entry.AttemptSequence = frame.Authority.SnapshotSequence;
                        entry.IsResolving = true;
                        entry.Resolving = ResolveAsync(entry, frame.Authority);
                    }
                    else if (entry.Surface is not null) Place(entry, target);
                }
            } while (reconcileAgain && !retired);
        }
        finally { reconciling = false; }
    }

    private Viewport? MatchingViewport(string widgetId)
    {
        if (!visible || widgetId != activeWidget || !viewports.TryGetValue(widgetId, out var candidate) ||
            candidate.Element.Retired || !candidate.Element.IsLoaded || !AncestorsVisible(candidate.Element)) return null;
        var frame = session.GetState(widgetId)?.LastGood;
        // Compatible publication must not park/reparent live pixels before the
        // presenter applies it. Epoch authority still retires changed documents;
        // native layout owns placement of this surviving logical viewport.
        if (frame is null || frame.Snapshot.EmbeddedMediaSession?.Id != candidate.SessionId ||
            !SameOwner(candidate.Authority, frame.Authority) || !ContainsViewport(frame.Snapshot.Root, candidate)) return null;
        return candidate;
    }

    private static bool SameOwner(WidgetPresentationAuthority a, WidgetPresentationAuthority b) =>
        a.WidgetId == b.WidgetId && a.WidgetInstanceId == b.WidgetInstanceId && a.RuntimeGeneration == b.RuntimeGeneration &&
        a.PresentationGeneration == b.PresentationGeneration && a.SessionGeneration == b.SessionGeneration;
    private static bool ContainsViewport(ViewNode node, Viewport target) =>
        node.Kind == ViewNodeKind.MediaViewport && node.Id == target.ElementId && node.MediaSessionId == target.SessionId ||
        node.Children.Any(child => ContainsViewport(child, target));
    private bool CanAcceptInput(string widgetId) => MatchingViewport(widgetId) is { } target && inputEnabled && target.Element.AcceptsInput &&
        session.GetState(widgetId)?.LastGood?.Authority.ActiveInputScopeId == target.Authority.ActiveInputScopeId;

    private static bool AncestorsVisible(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }

    private async Task ResolveAsync(Entry entry, WidgetPresentationAuthority authority)
    {
        try
        {
            var document = await session.ResolveEmbeddedMediaAsync(authority, entry.Lifetime.Token);
            if (retired || !entries.TryGetValue(entry.WidgetId, out var current) || !ReferenceEquals(current, entry) ||
                session.GetEmbeddedMediaState(document) is null) return;
            entry.Document = document;
            SetFailure(entry.WidgetId, null);
            entry.Surface = new(session, document);
            entry.Surface.InputAuthority = () => CanAcceptInput(entry.WidgetId);
            ++BrowserCreationCount;
            entry.Surface.Diagnostic += code => Diagnostic?.Invoke(entry.WidgetId, code);
            entry.Surface.BackRequested += () => { if (visible && inputEnabled && entry.WidgetId == activeWidget) BackRequested?.Invoke(entry.WidgetId); };
            entry.Surface.StateChanged += Reconcile;
            Place(entry, MatchingViewport(entry.WidgetId));
        }
        catch (OperationCanceledException) when (retired || entry.Lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            if (!retired && !entry.Lifetime.IsCancellationRequested &&
                error is not WidgetPresentationSessionException { Code: "presentation_stale" or "snapshot_stale" or "embedded_media_stale" })
                SetFailure(entry.WidgetId, "media-document-admission-failed");
        }
        finally
        {
            entry.IsResolving = false;
            if (!retired) Reconcile();
        }
    }

    private void Place(Entry entry, Viewport? target)
    {
        if (entry.Surface is not { } surface) return;
        var destination = target?.Element.SurfaceHost ?? parking;
        if (!ReferenceEquals(surface.Element.Parent, destination))
        {
            surface.UpdatePresentation(false, false);
            if (surface.Element.Parent is Panel previous) previous.Children.Remove(surface.Element);
            destination.Children.Add(surface.Element);
        }
        surface.UpdatePresentation(target is not null, target is not null && inputEnabled);
    }

    private void Retire(Entry entry)
    {
        if (!entries.Remove(entry.WidgetId)) return;
        SetFailure(entry.WidgetId, null);
        entry.Lifetime.Cancel();
        if (entry.Surface is { } surface)
        {
            surface.StateChanged -= Reconcile;
            surface.Dispose();
            if (surface.Element.Parent is Panel parent) parent.Children.Remove(surface.Element);
        }
        var retirement = RetireAsync();
        retirements.Add(retirement);
        _ = ObserveAsync();
        async Task RetireAsync()
        {
            try
            {
                if (entry.Resolving is { } pending) await pending;
                if (entry.Surface is { } browser) await browser.DisposeAsync();
            }
            finally { entry.Lifetime.Dispose(); }
        }
        async Task ObserveAsync()
        {
            try { await retirement; }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            { Diagnostic?.Invoke(entry.WidgetId, "media-retirement-failed"); }
            finally { retirements.Remove(retirement); if (!retired) Reconcile(); }
        }
    }

    private void DemandDispatcher()
    {
        if (!dispatcher.HasThreadAccess) throw new InvalidOperationException("Media owner requires its native dispatcher.");
    }

    private void SetFailure(string widgetId, string? code)
    {
        if (failures.GetValueOrDefault(widgetId) == code) return;
        if (code is null) failures.Remove(widgetId);
        else { failures[widgetId] = code; Diagnostic?.Invoke(widgetId, code); }
        FailureChanged?.Invoke(widgetId);
    }

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());
    private async Task DisposeCoreAsync()
    {
        DemandDispatcher();
        retired = true;
        session.PresentationChanged -= PresentationChanged;
        session.CatalogChanged -= CatalogChanged;
        foreach (var viewport in viewports.Values) viewport.Element.PlacementChanged = null;
        viewports.Clear();
        foreach (var entry in entries.Values.ToArray()) Retire(entry);
        await Task.WhenAll(retirements.ToArray());
        failures.Clear();
        parking.Children.Clear();
    }
}
