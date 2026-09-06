using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetNavigatorTests
{
    internal static async Task Run()
    {
        await RoutesRestoreFocusAndCancelStaleWorkAsync();
        await NestedBackIsScopedAndLifecycleOwnedAsync();
        await SharedRootsSeparatePageAndNestedLifetimesAsync();
        await ActionAwarePushCapturesActualDispatchFocusAsync();
        await ScopeComposesEveryContainerAndRejectsLeafRootsAsync();
        CapacityAndAuthoringConflictsFailClosed();
    }

    private static async Task SharedRootsSeparatePageAndNestedLifetimesAsync()
    {
        var options = new WidgetNavigatorOptions<Route>
        {
            SharedRootScopeId = "navigation.root",
            RootRoutes = [Route.Player, Route.Library],
            RouteScopeIds = new Dictionary<Route, string>
            {
                [Route.Detail] = "navigation.detail",
            },
        };
        using var widget = new NavigationWidget(options: options);
        await WidgetTestHost.InitializeAsync(widget);

        var player = widget.Navigation.Value;
        Equal("navigation.root", player.InputScopeId);
        var playerRoot = widget.Navigation.Scope(
            player, Root("player.root", "player.play"));
        Equal("navigation.root", playerRoot.InputScopeId!);
        False(playerRoot.Shortcuts.Any(IsPressedBack),
            "The initial shared root unexpectedly exposed nested Back.");
        var playerPageLifetime = player.RootRouteCancellationToken;
        var playerRouteLifetime = player.RouteCancellationToken;

        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.Push(Route.Detail, "player.play"));
        True(playerRouteLifetime.IsCancellationRequested,
            "Pushing a nested route did not retire root-route work.");
        False(playerPageLifetime.IsCancellationRequested,
            "Pushing a nested route canceled the owning root page.");
        Equal("navigation.detail", widget.Navigation.Value.InputScopeId);
        Equal(playerPageLifetime, widget.Navigation.Value.RootRouteCancellationToken);

        var detailLifetime = widget.Navigation.Value.RouteCancellationToken;
        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.NavigateRoot(Route.Library, "library.group"));
        True(detailLifetime.IsCancellationRequested,
            "Switching roots did not retire nested route work.");
        True(playerPageLifetime.IsCancellationRequested,
            "Switching roots did not retire the departed page.");
        var library = widget.Navigation.Value;
        Equal("navigation.root", library.InputScopeId);
        True(library.InitialFocusId is null,
            "Group entry also published a competing leaf focus target.");
        Equal(1L, library.FocusGroupEntryRequest!.RequestId);
        Equal("library.group", library.FocusGroupEntryRequest.GroupId);
        False(library.RootRouteCancellationToken.IsCancellationRequested,
            "The successor root page began with a canceled lifetime.");
        var libraryRoot = widget.Navigation.Scope(
            library, Root("library.root", "library.first"));
        Equal("navigation.root", libraryRoot.InputScopeId!);
        False(libraryRoot.Shortcuts.Any(IsPressedBack),
            "The switched shared root unexpectedly exposed nested Back.");

        var nestedBorrow = new WidgetNavigationSnapshot<Route>(
            Route.Detail,
            Route.Library,
            Depth: 1,
            InputScopeId: "navigation.root",
            InitialFocusId: null,
            BackActionId: null,
            Revision: library.Revision,
            RouteCancellationToken: CancellationToken.None);
        Throws<ArgumentException>(() => widget.Navigation.Scope(
            nestedBorrow, Root("detail.root", "detail.play")));

        var foreignRoot = library with { InputScopeId = "foreign.scope" };
        Throws<ArgumentException>(() => widget.Navigation.Scope(
            foreignRoot, Root("foreign.root", "foreign.play")));

        var libraryPageLifetime = library.RootRouteCancellationToken;
        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.NavigateRoot(Route.Player, "player.group"));
        True(libraryPageLifetime.IsCancellationRequested,
            "Leaving the library did not cancel its page lifetime.");
        Equal("navigation.root", widget.Navigation.Value.InputScopeId);
        Equal(2L, widget.Navigation.Value.FocusGroupEntryRequest!.RequestId);
        Equal("player.group", widget.Navigation.Value.FocusGroupEntryRequest.GroupId);

        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.NavigateRoot(Route.Library, "library.group"));
        Equal(3L, widget.Navigation.Value.FocusGroupEntryRequest!.RequestId);
        Equal("library.group", widget.Navigation.Value.FocusGroupEntryRequest.GroupId);

        var request = widget.Navigation.Value.FocusGroupEntryRequest;
        Equal(WidgetNavigationResult.Unchanged,
            widget.Navigation.NavigateRoot(Route.Library, "library.group"));
        Equal(request, widget.Navigation.Value.FocusGroupEntryRequest);

        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.NavigateRoot(Route.Player));
        True(widget.Navigation.Value.InitialFocusId is null,
            "Persistent-header root activation published a focus override.");
        True(widget.Navigation.Value.FocusGroupEntryRequest is null,
            "Persistent-header root activation published a group-entry request.");

        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task ActionAwarePushCapturesActualDispatchFocusAsync()
    {
        using var widget = new NavigationWidget();
        await WidgetTestHost.InitializeAsync(widget);
        var action = new WidgetActionEvent(
            "player.options", "player.root", ControllerButton.Y)
        {
            FocusedElementId = "player.queue",
        };
        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.PushFromAction(Route.Detail, action));
        Equal(WidgetNavigationResult.Changed, widget.Navigation.Back());
        Equal("player.queue", widget.Navigation.Value.InitialFocusId!);

        var missing = new WidgetActionEvent(
            "player.options", "player.root", ControllerButton.Y);
        Throws<ArgumentException>(() =>
            widget.Navigation.PushFromAction(Route.Detail, missing));
        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.PushFromAction(
                Route.Detail, missing, returnFocusOverrideId: "player.play"));
        Equal(WidgetNavigationResult.Changed, widget.Navigation.Back());
        Equal("player.play", widget.Navigation.Value.InitialFocusId!);

        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task RoutesRestoreFocusAndCancelStaleWorkAsync()
    {
        var widget = new NavigationWidget();
        var invalidations = 0;
        widget.Invalidated += (_, _) => invalidations++;
        await WidgetTestHost.InitializeAsync(widget);

        var initial = widget.Navigation.Value;
        Equal(Route.Player, initial.Route);
        Equal(Route.Player, initial.RootRoute);
        Equal(0, initial.Depth);
        False(initial.CanGoBack, "The root route exposed nested Back.");
        True(initial.BackActionId is null, "The root route published a Back action.");

        var root = widget.Navigation.Scope(Root("player.root", "player.play"));
        Equal(initial.InputScopeId, root.InputScopeId!);
        False(root.Shortcuts.Any(IsPressedBack),
            "A navigator Back shortcut leaked onto the root route.");

        var playerLifetime = initial.RouteCancellationToken;
        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.Navigate(Route.Library, "player.play"));
        True(playerLifetime.IsCancellationRequested,
            "Leaving a route did not cancel its route-owned work.");
        Equal(1, invalidations);
        var library = widget.Navigation.Value;
        Equal(Route.Library, library.Route);
        Equal(0, library.Depth);
        False(library.InputScopeId == initial.InputScopeId,
            "Distinct routes shared an input scope.");

        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.Navigate(Route.Player, "library.first"));
        var restored = widget.Navigation.Value;
        Equal(initial.InputScopeId, restored.InputScopeId);
        Equal("player.play", restored.InitialFocusId!);
        Equal(2, invalidations);

        var unchangedLifetime = restored.RouteCancellationToken;
        Equal(WidgetNavigationResult.Unchanged,
            widget.Navigation.Navigate(Route.Player, "player.queue"));
        False(unchangedLifetime.IsCancellationRequested,
            "A no-op route selection canceled valid work.");
        Equal(2, invalidations);

        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task NestedBackIsScopedAndLifecycleOwnedAsync()
    {
        var widget = new NavigationWidget();
        await WidgetTestHost.InitializeAsync(widget);

        var parentLifetime = widget.Navigation.Value.RouteCancellationToken;
        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.Push(Route.Detail, "player.open-detail"));
        True(parentLifetime.IsCancellationRequested,
            "Pushing a nested route left parent work running.");

        var detail = widget.Navigation.Value;
        True(detail.CanGoBack, "A nested route did not publish Back.");
        Equal(1, detail.Depth);
        var nestedRoot = widget.Navigation.Scope(Root("detail.root", "detail.play"));
        Equal(1, nestedRoot.Shortcuts.Count(IsPressedBack));
        Equal(detail.BackActionId!, nestedRoot.Shortcuts.Single(IsPressedBack).ActionId);

        False(widget.Navigation.TryHandleBack(new WidgetActionEvent(
            detail.BackActionId!, "detail.root", ControllerButton.B,
            InputScopeId: "stale.scope")),
            "A Back action from a stale scope changed navigation.");
        False(widget.Navigation.TryHandleBack(new WidgetActionEvent(
            detail.BackActionId!, "detail.root", ControllerButton.B,
            ControllerEventPhase.Released, InputScopeId: detail.InputScopeId)),
            "A released Back action changed navigation.");

        var nestedLifetime = detail.RouteCancellationToken;
        True(widget.Navigation.TryHandleBack(new WidgetActionEvent(
            detail.BackActionId!, "detail.root", ControllerButton.B,
            InputScopeId: detail.InputScopeId), "detail.play"),
            "The current scoped Back action was not handled.");
        True(nestedLifetime.IsCancellationRequested,
            "Popping a route did not cancel nested work.");
        Equal(Route.Player, widget.Navigation.Value.Route);
        Equal("player.open-detail", widget.Navigation.Value.InitialFocusId!);

        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.Push(Route.Player, "player.same-route"));
        var sameRouteNestedRoot = widget.Navigation.Scope(
            Root("player.root", "player.play"));
        Equal(WidgetNavigationResult.Changed, widget.Navigation.Back());
        var reusedAtRoot = widget.Navigation.Scope(sameRouteNestedRoot);
        False(reusedAtRoot.Shortcuts.Any(IsPressedBack),
            "A reused immutable route root retained navigator Back after pop.");

        var finalLifetime = widget.Navigation.Value.RouteCancellationToken;
        await WidgetTestHost.DestroyAsync(widget);
        True(finalLifetime.IsCancellationRequested,
            "Destroying the widget did not cancel current route work.");
        Throws<ObjectDisposedException>(() => widget.Navigation.Back());
    }

    private static async Task ScopeComposesEveryContainerAndRejectsLeafRootsAsync()
    {
        using var widget = new NavigationWidget();
        await WidgetTestHost.InitializeAsync(widget);
        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.Push(Route.Detail, "player.open-detail"));
        var nested = widget.Navigation.Value;
        var roots = new ContainerElement[]
        {
            UI.Stack("scope.stack", UI.Button("Open", "open", "scope.stack.action")),
            UI.Row("scope.row", UI.Button("Open", "open", "scope.row.action")),
            UI.VerticalScroll("scope.scroll", UI.Button("Open", "open", "scope.scroll.action")),
            UI.ResponsiveGrid("scope.grid", 220, 4,
                UI.Button("Open", "open", "scope.grid.action")),
        };

        foreach (var root in roots)
        {
            var scoped = widget.Navigation.Scope(nested, (WidgetElement)root);
            Equal(root.GetType(), scoped.GetType());
            Equal(root.Id, scoped.Id);
            Equal(nested.InputScopeId, scoped.InputScopeId!);
            Equal(1, scoped.Shortcuts.Count(IsPressedBack));
            False(root.Shortcuts.Any(IsPressedBack),
                "Scope composition mutated an immutable authored container.");
        }

        var leaf = Throws<ArgumentException>(() => widget.Navigation.Scope(
            nested, (WidgetElement)UI.Button("Open", "open", "scope.leaf.action")));
        True(leaf.Message.Contains("ContainerElement", StringComparison.Ordinal),
            "Leaf-root rejection did not identify the required container contract.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static void CapacityAndAuthoringConflictsFailClosed()
    {
        using var widget = new NavigationWidget(maximumDepth: 1, maximumRoutes: 2);
        var invalidations = 0;
        widget.Invalidated += (_, _) => invalidations++;

        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.Push(Route.Library, "player.library"));
        var atCapacity = widget.Navigation.Value;
        Equal(WidgetNavigationResult.RejectedCapacity,
            widget.Navigation.Push(Route.Detail, "library.detail"));
        Equal(atCapacity, widget.Navigation.Value);
        Equal(1, invalidations);

        Throws<InvalidOperationException>(() => widget.Navigation.Scope(
            Root("library.root", "library.first").InputScope("foreign.scope")));
        Throws<InvalidOperationException>(() => widget.Navigation.Scope(
            Root("library.root", "library.first")
                .Shortcut(ControllerButton.B, "author.back")));

        Equal(WidgetNavigationResult.Changed,
            widget.Navigation.Navigate(Route.Player, "library.first"));
        Equal(WidgetNavigationResult.RejectedCapacity,
            widget.Navigation.Navigate(Route.Detail, "player.detail"));

        Throws<ArgumentOutOfRangeException>(() => new NavigationWidget(maximumDepth: 0));
        Throws<ArgumentOutOfRangeException>(() => new NavigationWidget(maximumRoutes: 0));
    }

    private static StackElement Root(string id, string focusId) =>
        UI.Stack(id, UI.Button("Open", "open", focusId));

    private static bool IsPressedBack(ControllerShortcut shortcut) =>
        shortcut.Button == ControllerButton.B &&
        shortcut.Phase == ControllerEventPhase.Pressed;

    private enum Route
    {
        Player,
        Library,
        Detail,
    }

    private sealed class NavigationWidget : Widget, IDisposable
    {
        internal NavigationWidget(
            int maximumDepth = WidgetNavigator<Route>.DefaultMaximumDepth,
            int maximumRoutes = WidgetNavigator<Route>.DefaultMaximumRoutes,
            WidgetNavigatorOptions<Route>? options = null) =>
            Navigation = options is null
                ? CreateNavigator(
                    "navigation", Route.Player, maximumDepth, maximumRoutes)
                : CreateNavigatorWithOptions(
                    "navigation", Route.Player, options, maximumDepth, maximumRoutes);

        internal WidgetNavigator<Route> Navigation { get; }

        public override WidgetView Render()
        {
            var state = Navigation.Value;
            var focusId = state.Route switch
            {
                Route.Player => "player.play",
                Route.Library => "library.first",
                _ => "detail.play",
            };
            var rootId = state.Route switch
            {
                Route.Player => "player.root",
                Route.Library => "library.root",
                _ => "detail.root",
            };
            var group = UI.Stack(
                    state.Route == Route.Library ? "library.group" :
                    state.Route == Route.Player ? "player.group" : "detail.group",
                    UI.Button("Open", "open", focusId))
                .RememberChildFocus(focusId);
            return new(
                Navigation.Scope(UI.Stack(rootId, group)),
                state.Revision == 0 ? focusId : state.InitialFocusId,
                ActiveInputScopeId: state.InputScopeId)
            {
                FocusGroupEntryRequest = state.FocusGroupEntryRequest,
            };
        }

        public void Dispose() => Navigation.Dispose();
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void False(bool value, string message) => True(!value, message);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static TException Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
