using System.ComponentModel;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetUi.State;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record SurfaceValue(ViewNode Node, string Scope, WidgetIndexedRow? Row = null);
    private sealed class SurfaceRetention(WidgetIndexedRows owner, IndexedItemsSource<WidgetIndexedRow>.Retention lease,
        PropertyChangedEventHandler changed) : IDisposable
    {
        public WidgetIndexedRows Owner { get; } = owner;
        public IndexedItemsSource<WidgetIndexedRow>.Retention Lease { get; } = lease;
        public void Dispose() { Lease.Slot.PropertyChanged -= changed; Lease.Dispose(); }
    }
    private readonly PresentationSourceCoordinator<SurfaceValue> surfaceSources = new();
    private readonly Dictionary<PresentationSurfaceId, SurfaceRetention> surfaceRetentions = [];
    private bool surfacesQueued;

    private void QueueSurfaceUpdate()
    {
        if (!presentationActive || presentationOnly || disposed || applying || surfacesQueued) return;
        surfacesQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            surfacesQueued = false;
            if (disposed || applying) return;
            try { UpdateSurfaces(); }
            catch (Exception error) { ReportFailure(error); }
        })) surfacesQueued = false;
    }

    private void ClearSurfaceState()
    {
        foreach (var retention in surfaceRetentions.Values) retention.Dispose();
        surfaceRetentions.Clear(); surfaceSources.Clear();
    }

    private void UpdateSurfaces()
    {
        if (!presentationActive || frame is null || presentationOnly) return;
        var surfaces = new List<PresentationSurface<SurfaceValue>>();
        var contributions = new List<PresentationContribution<SurfaceValue>>();
        var surfaceBindings = new Dictionary<PresentationSurfaceId, Binding>();
        foreach (var binding in bindings.Values)
        {
            var node = declarations[binding.Identity.Id].Node;
            if (binding.Element is not WidgetPresentationSurface) continue;
            var kind = node.Kind == ViewNodeKind.BackgroundSurface ? PresentationSurfaceKind.Background : PresentationSurfaceKind.FocusFragment;
            var id = new PresentationSurfaceId(binding.Identity.Scope, node.Id, kind);
            surfaces.Add(new(id, new(kind == PresentationSurfaceKind.Background ? node : node.DefaultFocusPresentation!, binding.Identity.Scope), node.RetainLastPresentation));
            surfaceBindings.Add(id, binding);
        }
        foreach (var id in surfaceRetentions.Keys.ToArray())
        {
            var retention = surfaceRetentions[id];
            if (!surfaceBindings.ContainsKey(id) || !bindings.TryGetValue(retention.Owner.Declaration.Id, out var collection) ||
                collection.Element is not WidgetIndexedCollectionView view || !view.Owns(retention.Owner) ||
                Owner(collection.Identity.Id, id.Kind) != id)
            { retention.Dispose(); surfaceRetentions.Remove(id); }
        }
        PresentationSourceId? focusedSource = null;
        var focused = FocusedBinding();
        if (focused?.Element is WidgetIndexedCollectionView indexed && indexed.FocusedRow() is { } row)
        {
            focusedSource = RowIdentity(row);
            foreach (var kind in Enum.GetValues<PresentationSurfaceKind>())
                if (Owner(focused.Identity.Id, kind) is { } owner && RowContent(row, kind) is not null)
                {
                    if (surfaceRetentions.TryGetValue(owner, out var old) && ReferenceEquals(old.Owner, row.Owner) && old.Lease.Slot.Key == row.Item.Key) continue;
                    var retention = indexed.RetainFocusedRow();
                    if (retention is null) continue;
                    PropertyChangedEventHandler changed = (_, _) => QueueSurfaceUpdate();
                    retention.Slot.PropertyChanged += changed;
                    if (surfaceRetentions.Remove(owner, out old)) old.Dispose();
                    surfaceRetentions.Add(owner, new(row.Owner, retention, changed));
                }
        }
        else if (focused is not null)
            focusedSource = StaticIdentity(focused.Identity);

        foreach (var declaration in declarations.Values)
        {
            if (!declaration.Node.IsFocusable) continue;
            if (Owner(declaration.Node.Id, PresentationSurfaceKind.Background) is { } background && declaration.Node.FocusBackgroundArtworkHandle is { } handle)
                contributions.Add(new(StaticIdentity(declaration.Identity), background,
                    new(new() { Id = declaration.Node.Id, Kind = ViewNodeKind.Image, ArtworkHandle = handle, ImageFit = ImageFit.Cover }, declaration.Identity.Scope)));
            if (Owner(declaration.Node.Id, PresentationSurfaceKind.FocusFragment) is { } fragment && declaration.Node.FocusPresentation is { } content)
                contributions.Add(new(StaticIdentity(declaration.Identity), fragment, new(content, declaration.Identity.Scope)));
        }
        foreach (var (id, retention) in surfaceRetentions)
            if (retention.Lease.Slot.Value is { } retained && RowContent(retained, id.Kind) is { } content)
                contributions.Add(new(RowIdentity(retained), id, new(content, retained.Lease.Range.ScopeId, retained)));
        var authority = frame.Authority;
        var revision = new PresentationRevision<SurfaceValue>(new(authority.RuntimeGeneration, authority.WidgetInstanceId,
            authority.PresentationGeneration + ":" + authority.SessionGeneration), surfaces, contributions);
        var selections = surfaceSources.Resolve(revision, focusedSource);
        foreach (var selection in selections)
        {
            var binding = surfaceBindings[selection.Surface];
            var surface = (WidgetPresentationSurface)binding.Element;
            var value = selection.Content;
            var selectedRow = value.Row;
            if (selectedRow is { Lease.IsCurrent: false }) continue; // Keep pixels until the retained logical slot reacquires authority.
            var generation = selectedRow is null ? ArtworkGeneration : selectedRow.Lease.LeaseId + ":" + selectedRow.Item.Key.Length + ":" + selectedRow.Item.Key;
            var resolve = selectedRow is null ? ResolveArtworkAsync : async (string handle, CancellationToken token) =>
            {
                try { return await selectedRow.Owner.ResolveArtworkAsync(selectedRow, handle, token); }
                catch (WidgetRail.WidgetPresentationSession.WidgetPresentationSessionException) when (!selectedRow.Lease.IsCurrent) { return null; }
            };
            if (selection.Surface.Kind == PresentationSurfaceKind.Background)
            {
                surface.SetArtworkFit(value.Node.ImageFit);
                UpdateArtwork(binding, value.Node, image => surface.SetArtwork(image, value.Node.ImageFit), resolve, generation);
            }
            else if (surface.Fragment is { } fragment)
            {
                fragment.Session = Session;
                fragment.ResolveArtworkAsync = resolve;
                fragment.ArtworkGeneration = generation;
                fragment.Failed = ReportFailure;
                _ = fragment.SetPresentationActiveAsync(true);
                fragment.ApplyFragment(selectedRow is null ? frame : frame with { RenderStyles = selectedRow.Lease.RenderStyles }, value.Node, value.Scope);
            }
            if (selectedRow is null && surfaceRetentions.Remove(selection.Surface, out var unused)) unused.Dispose();
        }

        PresentationSurfaceId? Owner(string sourceId, PresentationSurfaceKind kind)
        {
            for (var declaration = declarations.GetValueOrDefault(sourceId); declaration is not null;
                declaration = declaration.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
            {
                var wanted = kind == PresentationSurfaceKind.Background ? ViewNodeKind.BackgroundSurface : ViewNodeKind.FocusPresentationSurface;
                if (declaration.Node.Kind != wanted) continue;
                // A nested background always owns its subtree, even when it opts
                // out of consuming focus artwork. Never leak into an outer owner.
                if (kind == PresentationSurfaceKind.Background && declaration.Node.UsesFocusedDescendantArtwork != true) return null;
                return new(declaration.Identity.Scope, declaration.Node.Id, kind);
            }
            return null;
        }
    }
    private static PresentationSourceId StaticIdentity(WidgetElementIdentity identity) => new(identity.Scope, identity.Id, identity.Kind.ToString(), identity.ItemPath);
    private static PresentationSourceId RowIdentity(WidgetIndexedRow row) => new(row.Lease.Range.ScopeId, row.Item.Root.Id, row.Item.Root.Kind.ToString(),
        row.Owner.Declaration.Id + ":" + row.Lease.Range.Source.SourceId + ":" + row.Lease.Range.Source.QueryGeneration + ":" + row.Item.Key);
    private static ViewNode? RowContent(WidgetIndexedRow row, PresentationSurfaceKind kind) => kind == PresentationSurfaceKind.FocusFragment
        ? row.Item.Root.FocusPresentation : row.Item.Root.FocusBackgroundArtworkHandle is { } handle
            ? new() { Id = row.Item.Root.Id, Kind = ViewNodeKind.Image, ArtworkHandle = handle, ImageFit = ImageFit.Cover } : null;
}
