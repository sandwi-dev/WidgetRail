using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.FullApplicationWidget;

internal enum ReferenceRoute { Library, Details }
internal sealed record ReferenceQuery(IReadOnlyList<ReferenceDocument> Items);

public sealed class FullApplicationReferenceWidget : Widget
{
    internal const string CollectionId = "full-app.document-list";
    private readonly object _gate = new();
    private readonly ReferenceLibrary _library;
    private readonly WidgetIndexedCollection<ReferenceQuery, ReferenceDocument> _documents;
    private readonly WidgetNavigator<ReferenceRoute> _navigation;
    private ReferenceQuery _query;
    private ReferenceDocument? _selected;
    private IndexedCollectionFocusTarget? _returnItem;
    private long? _returnRevision;
    private bool _loadError;

    public FullApplicationReferenceWidget() : this(new ReferenceLibrary()) { }

    internal FullApplicationReferenceWidget(ReferenceLibrary library)
    {
        _library = library;
        _query = new(library.Documents);
        _navigation = CreateNavigator("full-app.navigation", ReferenceRoute.Library, maximumDepth: 1, maximumRoutes: 2);
        _documents = CreateIndexedCollection<ReferenceQuery, ReferenceDocument>("full-app.documents", _query, _query.Items.Count, new()
        {
            ReadRange = ReadDocumentsAsync,
            ItemKey = item => new(item.Id),
            RenderItem = (_, item, context) => UI.Button($"{item.Title} · {item.Section}", "full-app.open", context.Id("document"))
                .Classes("full-app-document"),
            OnAction = OpenDocumentAsync,
        });
    }

    internal int PrivateDocumentCount => _library.Count;

    private async ValueTask<IReadOnlyList<ReferenceDocument>> ReadDocumentsAsync(ReferenceQuery query, int start, int count, CancellationToken token)
    {
        try { return await _library.ReadAsync(query.Items, start, count, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            var changed = false;
            lock (_gate)
                if (ReferenceEquals(query, _query) && !_loadError) { _loadError = true; changed = true; }
            if (changed) Invalidate();
            throw new InvalidOperationException("The private library could not be loaded. Try again.");
        }
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        // Retire reads/actions without changing membership or keyed return targets.
        // The source keeps the real provider slot until cancelled work has drained.
        lock (_gate) if (!WidgetLifetimeToken.IsCancellationRequested) RefreshContent();
        return ValueTask.CompletedTask;
    }

    public override WidgetView Render()
    {
        lock (_gate)
        {
            var navigation = _navigation.Value;
            var details = navigation.Route == ReferenceRoute.Details;
            var root = _navigation.Scope(navigation, UI.Stack("full-app.root",
                UI.Text("Reference Library", "full-app.heading"), details ? Details() : Library()).Classes("full-app-root"));
            return new(root, details ? "full-app.details.back" : CollectionId,
                ActiveInputScopeId: navigation.InputScopeId,
                Surface: new WidgetSurfaceHints { Mode = WidgetSurfaceMode.Standard, PreferredWidth = 820,
                    PreferredHeight = 620, MinimumWidth = 360, MinimumHeight = 300 })
            {
                FocusGroupEntryRequest = !details && navigation.Revision == _returnRevision && _returnItem is { } target
                    ? _documents.Enter(CollectionId, navigation.Revision, target) : null,
            };
        }
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action); cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_navigation.TryHandleBack(action))
            {
                _returnRevision = _navigation.Value.Revision;
                Invalidate();
            }
            else if (IsActive && _navigation.Value.Route == ReferenceRoute.Library && action.ActionId is "full-app.refresh" or "full-app.retry")
                RefreshContent();
            // Row opens are accepted only through an indexed lease, never by parsing IDs.
        }
        return ValueTask.CompletedTask;
    }

    private ValueTask OpenDocumentAsync(ReferenceQuery query, ReferenceDocument item, WidgetActionEvent action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!IsActive || !ReferenceEquals(query, _query) || _navigation.Value.Route != ReferenceRoute.Library || action.ActionId != "full-app.open")
                return ValueTask.CompletedTask;
            _selected = item;
            _returnItem = action.FocusedCollectionItem;
            _returnRevision = null;
            _navigation.PushFromAction(ReferenceRoute.Details, action, CollectionId);
        }
        return ValueTask.CompletedTask;
    }

    private void RefreshContent()
    {
        _query = new(_library.Documents);
        _loadError = false;
        // This immutable sample has unchanged count/order/keys. Membership changes
        // in a real app must instead use PublishQuery with the new exact count.
        _documents.UpdateContent(_query);
    }

    private WidgetElement Library()
    {
        var children = new List<WidgetElement>
        {
            UI.Text($"{_library.Count:N0} private records · loaded on demand", "full-app.summary"),
            UI.Button("Refresh", "full-app.refresh", "full-app.refresh"),
        };
        if (_loadError) children.Add(UI.Alert("Library unavailable", "The private library could not be loaded. Try again.",
            AlertTone.Warning, "full-app.error", new ComponentAction("Retry", "full-app.retry", WidgetGlyph.Refresh)));
        children.Add(UI.CollectionList(CollectionId, _documents, 56, "Private documents").Classes("full-app-document-list"));
        return UI.Stack("full-app.library", children.ToArray());
    }

    private WidgetElement Details() => _selected is not { } selected
        ? UI.Stack("full-app.details", UI.Text("The selected record is unavailable.", "full-app.details.missing"),
            UI.Button("Back", _navigation.Value.BackActionId!, "full-app.details.back"))
        : UI.Stack("full-app.details", UI.Text(selected.Title, "full-app.details.title"),
            UI.Text(selected.Section, "full-app.details.section"), UI.Text(selected.Summary, "full-app.details.summary"),
            UI.Button("Back", _navigation.Value.BackActionId!, "full-app.details.back"));
}
