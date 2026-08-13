using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.FullApplicationWidget;

internal enum ReferenceRoute { Library, Details }

public sealed class FullApplicationReferenceWidget : Widget
{
    internal const int PageSize = 32;
    internal const int MaximumRetainedItems = 96;
    private readonly ReferenceLibrary _library;
    private readonly WidgetCursorResource<ReferenceDocument> _documents;
    private readonly WidgetNavigator<ReferenceRoute> _navigation;
    private string? _selectedId;

    public FullApplicationReferenceWidget() : this(new ReferenceLibrary()) { }

    internal FullApplicationReferenceWidget(ReferenceLibrary library)
    {
        _library = library;
        _navigation = CreateNavigator(
            "full-app.navigation", ReferenceRoute.Library,
            maximumDepth: 1, maximumRoutes: 2);
        _documents = CreateCursorResource<ReferenceDocument>("full-app.documents", new()
        {
            PageSize = PageSize,
            MaximumRetainedItems = MaximumRetainedItems,
            PaginationThreshold = 2,
            LoadPage = library.LoadAsync,
            MapError = _ => new WidgetResourceError(
                "reference_load_failed", "The private library could not be loaded. Try again."),
            Viewports =
            [
                new("full-app.document-list",
                    item => new WidgetCollectionItemKey(item.Id),
                    item => "full-app." + item.Id,
                    "full-app.retry"),
            ],
        });
    }

    internal int PrivateDocumentCount => _library.Count;
    internal WidgetCursorResourceSnapshot<ReferenceDocument> Documents => _documents.Snapshot;
    internal Task WhenDocumentsIdleAsync(CancellationToken cancellationToken = default) =>
        _documents.WhenIdleAsync(cancellationToken);

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        _documents.EnsureLoaded();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken transitionToken)
    {
        _documents.Reset(invalidate: false);
        return ValueTask.CompletedTask;
    }

    public override WidgetView Render()
    {
        var navigation = _navigation.Value;
        var content = navigation.Route == ReferenceRoute.Details
            ? Details()
            : Library();
        var root = _navigation.Scope(navigation,
            UI.Stack("full-app.root",
                UI.Text("Reference Library", "full-app.heading"),
                content).Classes("full-app-root"));
        return new(root,
            navigation.InitialFocusId ?? InitialFocus(),
            ActiveInputScopeId: navigation.InputScopeId,
            Surface: new WidgetSurfaceHints
            {
                Mode = WidgetSurfaceMode.Standard,
                PreferredWidth = 820,
                PreferredHeight = 620,
                MinimumWidth = 360,
                MinimumHeight = 300,
            });
    }

    public override ValueTask OnActionAsync(
        WidgetActionEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (_navigation.TryHandleBack(action)) return ValueTask.CompletedTask;
        if (_documents.TryHandlePagination(action, out _)) return ValueTask.CompletedTask;
        switch (action.ActionId)
        {
            case "full-app.retry":
                _documents.Retry();
                return ValueTask.CompletedTask;
            case "full-app.refresh":
                _documents.Refresh();
                return ValueTask.CompletedTask;
            case "full-app.open":
                var id = action.SourceElementId.StartsWith("full-app.", StringComparison.Ordinal)
                    ? action.SourceElementId["full-app.".Length..]
                    : string.Empty;
                if (_library.Find(id) is null) return ValueTask.CompletedTask;
                _selectedId = id;
                _documents.SelectAnchor(new(id), invalidate: false);
                _navigation.Push(ReferenceRoute.Details, action.SourceElementId);
                return ValueTask.CompletedTask;
            default:
                return ValueTask.CompletedTask;
        }
    }

    private WidgetElement Library()
    {
        var snapshot = _documents.Snapshot;
        if (snapshot.Status is WidgetPagedResourceStatus.NotLoaded or WidgetPagedResourceStatus.Loading)
            return UI.Stack("full-app.loading",
                UI.LoadingIndicator("full-app.loading.indicator", "Loading private library"));
        if (snapshot.Status == WidgetPagedResourceStatus.Error)
            return UI.Stack("full-app.error",
                UI.Text(snapshot.Error?.Message ?? "The private library could not be loaded.",
                    "full-app.error.message"),
                UI.Button("Retry", "full-app.retry", "full-app.retry"));
        var rows = snapshot.Items.Select(item => _documents.PresentItem(item,
            UI.Button($"{item.Title} · {item.Section}", "full-app.open", "full-app." + item.Id)
                .Classes("full-app-document"))).ToArray();
        return UI.Stack("full-app.library",
            UI.Text($"{_library.Count:N0} private records · {snapshot.Items.Count} projected",
                "full-app.summary"),
            UI.Button("Refresh", "full-app.refresh", "full-app.refresh"),
            _documents.Present(UI.VerticalScroll("full-app.document-list", rows)));
    }

    private WidgetElement Details()
    {
        var selected = _selectedId is null ? null : _library.Find(_selectedId);
        return selected is null
            ? UI.Stack("full-app.details",
                UI.Text("The selected record is unavailable.", "full-app.details.missing"),
                UI.Button("Back", _navigation.Value.BackActionId!, "full-app.details.back"))
            : UI.Stack("full-app.details",
                UI.Text(selected.Title, "full-app.details.title"),
                UI.Text(selected.Section, "full-app.details.section"),
                UI.Text(selected.Summary, "full-app.details.summary"),
                UI.Button("Back", _navigation.Value.BackActionId!, "full-app.details.back"));
    }

    private string? InitialFocus()
    {
        if (_navigation.Value.Route == ReferenceRoute.Details) return "full-app.details.back";
        var snapshot = _documents.Snapshot;
        if (snapshot.RequestedFocusId is { } requested) return requested;
        if (snapshot.Status is WidgetPagedResourceStatus.NotLoaded or WidgetPagedResourceStatus.Loading)
            return null;
        if (snapshot.Status == WidgetPagedResourceStatus.Error) return "full-app.retry";
        return snapshot.Items.FirstOrDefault() is { } first
            ? "full-app." + first.Id
            : "full-app.refresh";
    }
}
