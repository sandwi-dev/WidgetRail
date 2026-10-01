using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;
using WidgetRail.PlatformSettings;

internal static class BridgeIndexedEndToEndScenarios
{
    internal static async Task ServeThemedValidationAsync(string pipe, string settingsRoot)
    {
        var paths = new PlatformSettingsPaths(Path.GetFullPath(settingsRoot));
        var store = new PlatformSettingsStore(paths);
        await store.UpdateAsync(value => value with { Appearance = value.Appearance with
            { ThemeId = ThemeIdentity.BuiltInNeonCircuit, ThemeVersion = ThemeIdentity.BuiltInNeonCircuitVersion } });
        await using var appearance = new PlatformAppearanceService(paths, new ThemeManager(store, new ThemeCatalog(paths)));
        await appearance.StartAsync();
        var document = WrssParser.Parse("image { width: 48px; height: 48px; } text { font-size: 16px; color: var(--text); }", "native-validation.wrss").Document;
        var configured = new ConfiguredWidget
        {
            Id = "indexed-owned", Name = "Indexed ownership", InstanceId = "indexed-owned.instance",
            PackageId = "dev.indexed", PublisherId = "dev", WorkerExecutable = Environment.ProcessPath!,
            WorkerFingerprint = new('a', 64), CatalogFingerprint = new('a', 64), StylePackage = new([document], []),
        };
        await using var server = new WidgetBridgeServer(pipe, new([configured]), appearance: appearance);
        await server.RunAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    internal static async Task ServeValidationAsync(string pipe)
    {
        var theme = WrssThemeCompiler.Compile([WrssParser.Parse("image { width: 48px; height: 48px; } text { font-size: 16px; }", "native-validation.wrss").Document]);
        if (!theme.IsValid) throw new InvalidOperationException("Native validation styles are invalid.");
        var configured = new ConfiguredWidget
        {
            Id = "indexed-owned", Name = "Indexed ownership", InstanceId = "indexed-owned.instance",
            PackageId = "dev.indexed", PublisherId = "dev", WorkerExecutable = Environment.ProcessPath!,
            WorkerFingerprint = new('a', 64), CatalogFingerprint = new('a', 64),
            CompiledTheme = theme.Theme,
        };
        await using var server = new WidgetBridgeServer(pipe, new([configured]));
        await server.RunAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
    }
    internal static async Task SessionToRealWorker()
    {
        var configured = new ConfiguredWidget
        {
            Id = "indexed-owned", Name = "Indexed ownership", InstanceId = "indexed-owned.instance",
            PackageId = "dev.indexed", PublisherId = "dev", WorkerExecutable = Environment.ProcessPath!,
            WorkerFingerprint = new('a', 64), CatalogFingerprint = new('a', 64),
        };
        var pipe = "indexed-owned-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var server = new WidgetBridgeServer(pipe, new([configured]));
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
        _ = await session.ListWidgetsAsync(deadline.Token);
        var frame = await session.EstablishPresentationAsync(session.GetTarget(configured.Id), WidgetLifecycleState.Interactive, deadline.Token);
        var descriptor = frame.Snapshot.Root.Children.Single(node => node.Id == "items").IndexedCollection!;
        await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "items", descriptor, 7, 2, cancellationToken: deadline.Token);
        Check(lease.Range.Items.Select(item => item.Key).SequenceEqual(["item.7", "item.8"]), "Real worker range lost item identities.");
        Check(await lease.ResolveArtworkAsync("item.7", "cover", deadline.Token) is { ContentType: WidgetArtworkContentType.Png },
            "Row artwork did not cross both process protocols.");

        var displayed = frame;
        frame = await session.RefreshAsync(frame.Authority, deadline.Token);
        Check(lease.IsCurrent && frame.Authority.SnapshotSequence > displayed.Authority.SnapshotSequence,
            "The test did not create receive-thread publication ahead of the displayed frame.");
        Check(lease.ClaimsInput(displayed, "item.7", ControllerButton.A), "The unchanged displayed action lost ownership.");
        await Invoke(ControllerButton.A, "row:0:7:open", displayed);
        await Invoke(ControllerButton.X, "parent");
        Check(lease.IsCurrent, "Unrelated page updates retired row semantics.");
        await Invoke(ControllerButton.Y, "row:0:7:replace");
        Check(!lease.IsCurrent, "Query replacement failed to retire the captured row.");
        await lease.DisposeAsync();
        var next = frame.Snapshot.Root.Children.Single(node => node.Id == "items").IndexedCollection!;
        Check(next.QueryGeneration > descriptor.QueryGeneration, "Worker query did not change.");
        await using var replacement = await session.AcquireIndexedRangeAsync(frame.Authority, "items", next, 7, 2, cancellationToken: deadline.Token);
        Check(replacement.LeaseId != lease.LeaseId && replacement.IsCurrent, "Replacement range did not receive fresh ownership.");
        await session.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!replacement.IsCurrent, "Session shutdown retained remote row ownership.");

        async Task Invoke(ControllerButton button, string expected, WidgetPresentationFrame? displayedOrigin = null)
        {
            var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<WidgetPresentationInvalidatedEventArgs> handler = (_, _) => invalidated.TrySetResult();
            session.Invalidated += handler;
            try
            {
                Check(await lease.AdmitInputAsync(displayedOrigin ?? frame, "item.7", button, cancellationToken: deadline.Token) == WidgetOperationAdmission.Enqueued,
                    "Indexed input did not enter the existing worker queue.");
                await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(3));
                // The session refreshes invalidations itself. Wait for that
                // publication; the previously displayed authority may already
                // have been superseded by the receive pump.
                var until = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                while (true)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    frame = session.GetState(configured.Id)!.LastGood!;
                    if (frame.Snapshot.Root.Children.Single(node => node.Id == "status").Text == expected) break;
                    if (DateTime.UtcNow >= until) throw new TimeoutException($"Indexed action {button} was not published.");
                    await Task.Delay(10, deadline.Token);
                }
                Check(frame.Snapshot.Root.Children.Single(node => node.Id == "status").Text == expected,
                    $"Indexed action {button} expected {expected}; observed {frame.Snapshot.Root.Children.Single(node => node.Id == "status").Text}.");
            }
            finally { session.Invalidated -= handler; }
        }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    internal static async Task RealWorkerModalDataAndInputScopes()
    {
        var configured = new ConfiguredWidget
        {
            Id = "indexed-owned", Name = "Indexed modal", InstanceId = "indexed-owned.instance",
            PackageId = "dev.indexed", PublisherId = "dev", WorkerExecutable = Environment.ProcessPath!,
            WorkerFingerprint = new('a', 64), CatalogFingerprint = new('a', 64),
        };
        var pipe = "indexed-modal-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var server = new WidgetBridgeServer(pipe, new([configured]));
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
        await session.ListWidgetsAsync(deadline.Token);
        var frame = await session.EstablishPresentationAsync(session.GetTarget(configured.Id), WidgetLifecycleState.Interactive, deadline.Token);
        await Change(() => session.SendActionAsync(frame.Authority, new("modal-mode", "root", InputScopeId: "root"), deadline.Token));
        var descriptor = Find(frame.Snapshot.Root, "items").IndexedCollection!;
        await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "items", descriptor, 75, 1, cancellationToken: deadline.Token);
        await Change(() => lease.AdmitInputAsync(frame.Authority, "item.75", ControllerButton.A, cancellationToken: deadline.Token));
        Check(frame.Snapshot.Root.Kind == ViewNodeKind.ModalLayer && frame.Authority.ActiveInputScopeId.StartsWith("details.75.", StringComparison.Ordinal),
            "Captured deep row did not open its own modal scope.");
        Check(lease.IsCurrent && await lease.ResolveArtworkAsync("item.75", "background", deadline.Token) is not null,
            "Modal scope retired unchanged parent data or artwork authority.");
        try
        {
            await lease.AdmitInputAsync(frame.Authority, "item.75", ControllerButton.A, cancellationToken: deadline.Token);
            throw new InvalidOperationException("Inactive parent row action was admitted.");
        }
        catch (WidgetPresentationSessionException error) when (error.Code == "input_scope_stale") { }
        await Change(() => session.SendActionAsync(frame.Authority,
            new("modal-play", "modal-play", InputScopeId: frame.Authority.ActiveInputScopeId), deadline.Token));
        Check(Find(frame.Snapshot.Root, "status").Text == "modal-play", "Modal command did not reach worker.");
        await Change(() => session.SendControllerInputAsync(frame.Authority, new(ControllerButton.B, ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget, "modal-play", 1, 1, frame.Authority.ActiveInputScopeId,
            frame.Authority.SnapshotSequence), deadline.Token));
        Check(frame.Snapshot.Root.Kind != ViewNodeKind.ModalLayer && Find(frame.Snapshot.Root, "status").Text == "modal-dismiss" && lease.IsCurrent,
            "Modal B did not dismiss exactly its scope while preserving the parent.");
        await Change(() => lease.AdmitInputAsync(frame.Authority, "item.75", ControllerButton.A, cancellationToken: deadline.Token));
        await Change(() => session.SendActionAsync(frame.Authority,
            new("modal-replace", "modal-replace", InputScopeId: frame.Authority.ActiveInputScopeId), deadline.Token));
        Check(!lease.IsCurrent && frame.Snapshot.Root.Kind == ViewNodeKind.ModalLayer,
            "Parent query replacement must retire old row data without closing the modal.");
        await session.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(5));

        async Task Change(Func<Task> change)
        {
            var priorSequence = frame.Authority.SnapshotSequence;
            var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<WidgetPresentationInvalidatedEventArgs> handler = (_, _) => invalidated.TrySetResult();
            session.Invalidated += handler;
            try
            {
                await change();
                await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(4));
                // Let the session's own invalidation consumer publish. Issuing a
                // second explicit Refresh races it and manufactures stale test
                // authority unrelated to modal data/input semantics.
                var stable = 0;
                while (stable < 2)
                {
                    await Task.Delay(20, deadline.Token);
                    var latest = session.GetState(configured.Id)!.LastGood!;
                    stable = latest.Authority.SnapshotSequence > priorSequence && latest.Authority == frame.Authority ? stable + 1 : 0;
                    frame = latest;
                }
            }
            finally { session.Invalidated -= handler; }
        }
        static ViewNode Find(ViewNode root, string id) => FindOptional(root, id) ?? throw new InvalidOperationException("Missing " + id);
        static ViewNode? FindOptional(ViewNode root, string id) => root.Id == id ? root :
            root.Children.Select(child => FindOptional(child, id)).FirstOrDefault(node => node is not null);
    }
}

internal sealed class IndexedOwnedBridgeProbeWidget : Widget
{
    private readonly WidgetIndexedCollection<int, int> source;
    private string status = "initial";
    private bool grid;
    private bool grouped;
    private int headerRevision;
    private bool repartitioned;
    private bool surfaces;
    private bool modalMode;
    private int? modalItem;
    private long modalOpening;
    private int modalRevision;
    private readonly TaskCompletionSource releaseBackground = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string backgroundState = "idle";
    private TaskCompletionSource? activationReadGate;
    private long calls;
    private long focusRequest;
    private FocusGroupEntryRequest? entry;
    internal IndexedOwnedBridgeProbeWidget()
    {
        source = CreateIndexedCollection<int, int>("owned", 0, 100, new()
        {
            ReadRange = async (query, start, count, token) =>
            {
                if (query == 500) throw new InvalidOperationException("Native theme fixture provider failure.");
                if ((query is >= 300 and <= 303 or 97) && Volatile.Read(ref activationReadGate) is { } gate) await gate.Task.WaitAsync(token);
                await Task.Delay(query == 99 ? 500 : 80, token);
                return Enumerable.Range(start, count).ToArray();
            },
            ItemKey = item => new("item." + item),
            RenderItem = (query, item, context) =>
            {
                WidgetElement tile = query == 98 && item < 2
                    ? UI.Button($"Disabled {item}", "open", context.Id("row")).Disabled()
                    : UI.ActionSurface(query == 302 ? "alternate" : "open", context.Id("row"), $"Item {item}",
                        ActionSurfaceOrientation.Horizontal, UI.Artwork(new("cover"), context.Id("cover"), "Cover"),
                        UI.Text($"Item {item}", context.Id("title"))).Shortcut(ControllerButton.Y, actionId: "replace");
                if (query >= 400) tile = ((ActionSurfaceElement)tile).ContextMenuShortcut(ControllerButton.X)
                    .ContextAction("context-play", "Play").ContextAction("context-disabled", "Unavailable", disabled: true)
                    .ContextAction("context-remove", "Remove", WidgetContextActionStyle.Danger);
                if (query == 303) tile = ((ActionSurfaceElement)tile).Disabled();
                if (query is 304 or 305) tile = ((ActionSurfaceElement)tile).Selected();
                if (query == 305) tile = ((ActionSurfaceElement)tile).Busy();
                return query >= 100 ? tile.FocusBackground(new("background")).PresentOnFocus(UI.Text($"Summary {query}:{item}", context.Id("summary"))) : tile;
            },
            OnAction = (query, item, action, _) =>
            {
                Interlocked.Increment(ref calls);
                Volatile.Write(ref status, $"row:{query}:{item}:{action.ActionId}");
                if (action.ActionId == "replace") source!.PublishQuery(query + 1, 100);
                if (modalMode && action.ActionId == "open") { modalItem = item; ++modalOpening; modalRevision = 0; entry = null; }
                Invalidate();
                return ValueTask.CompletedTask;
            },
            ResolveArtwork = async (query, item, handle, _) =>
            {
                // Adjacent rows deliberately share the same handle and range lease,
                // but decode to different pixel widths. A late provider ignores cancellation.
                if (query == 100 && item == 2 && handle.Value == "background")
                {
                    Volatile.Write(ref backgroundState, "pending"); Invalidate();
                    await releaseBackground.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Volatile.Write(ref backgroundState, "completed"); Invalidate();
                }
                // A contrasting yellow/dark checker makes missing or misplaced row
                // images visible even when the parent background is the blue swatch.
                // Background widths still distinguish adjacent retained item identities.
                var png = handle.Value != "background"
                    ? "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAIUlEQVR4nGP4f83hPwiriJmAMTqfgaACXBIwPmEFg8ANADROkCEUK5WhAAAAAElFTkSuQmCC"
                    : item % 3 == 0
                    ? "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY9CYduI/AAT2AoYAkFFrAAAAAElFTkSuQmCC"
                    : item % 3 == 1
                    ? "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAPSURBVBhXYwiYduI/CAMAFd8FW0f/X3EAAAAASUVORK5CYII="
                    : "iVBORw0KGgoAAAANSUhEUgAAAAMAAAABCAYAAAAb4BS0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAPSURBVBhXY6iYduI/DAMANJwIgAW7QakAAAAASUVORK5CYII=";
                return new(WidgetArtworkContentType.Png, Convert.FromBase64String(png));
            },
        });
    }
    private WidgetView RenderParent() => new(UI.Stack("root", UI.Text(Volatile.Read(ref status), "status"),
        UI.Text($"Calls: {Interlocked.Read(ref calls)}", "calls"), UI.Text(Volatile.Read(ref backgroundState), "background-state"),
        UI.Row("toolbar", UI.Button("Parent", "parent", "parent").FocusDown("items"), UI.Button("Refresh content", "content", "content"), UI.Button("Toggle grid", "grid", "grid"), UI.Button("Toggle groups", "groups", "groups"), UI.Button("Surfaces", "surfaces", "surfaces")),
        Collection())
        .Shortcut(ControllerButton.LeftBumper, actionId: "focus-exact")
        .Shortcut(ControllerButton.RightBumper, actionId: "focus-default")
        .Shortcut(ControllerButton.LeftTrigger, actionId: "focus-wrong")
        .Shortcut(ControllerButton.RightTrigger, actionId: "focus-stale")
        .Shortcut(ControllerButton.Menu, actionId: "focus-delayed")
        .Shortcut(ControllerButton.RightStick, actionId: "focus-disabled")
        .Shortcut(ControllerButton.LeftStick, actionId: "group-label")
        .Shortcut(ControllerButton.X, actionId: "group-partition")
        .Shortcut(ControllerButton.B, actionId: "focus-clear"), "items") { FocusGroupEntryRequest = entry };
    public override WidgetView Render()
    {
        var parent = RenderParent();
        if (modalItem is not { } item) return parent;
        return parent.WithModal(new($"details.{item}.{modalOpening}", $"Item {item} details revision {modalRevision}",
            UI.Stack("modal.content",
                UI.Button("Play", "modal-play", "modal-play"),
                UI.Button("Update details", "modal-update", "modal-update"),
                UI.Button("Replace parent query", "modal-replace", "modal-replace"),
                UI.Text($"Item {item}; revision {modalRevision}", "modal-status")),
            "modal-play", "modal-dismiss"));
    }
    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref calls);
        Volatile.Write(ref status, action.ActionId);
        if (action.ActionId == "parent" && surfaces) releaseBackground.TrySetResult();
        if (action.ActionId == "content") source.UpdateContent(surfaces ? 101 : 1);
        if (action.ActionId == "focus-placeholder")
        {
            activationReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            source.PublishQuery(97, 34);
            entry = source.Enter("items", ++focusRequest, source.FocusTarget("items", new("item.33"), 33));
        }
        if (action.ActionId == "focus-placeholder-end")
        {
            activationReadGate?.TrySetResult(); activationReadGate = null;
            source.PublishQuery(0, 100); entry = null;
        }
        if (action.ActionId == "activation-mode")
        {
            activationReadGate?.TrySetResult(); activationReadGate = null;
            modalMode = grid = grouped = false; surfaces = true; modalItem = null;
            source.PublishQuery(300, 100);
            entry = source.Enter("items", ++focusRequest, source.FocusTarget("items", new("item.75"), 75));
        }
        if (action.ActionId is "focus-default-replace" or "focus-default-republish")
        {
            activationReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            source.PublishQuery(97, 2);
            if (action.ActionId == "focus-default-replace") entry = source.Enter("items", ++focusRequest);
        }
        if (action.ActionId is "activation-refresh" or "activation-changed" or "activation-disabled")
        {
            activationReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            source.UpdateContent(action.ActionId == "activation-changed" ? 302 : action.ActionId == "activation-disabled" ? 303 : 301);
        }
        if (action.ActionId == "activation-release") activationReadGate?.TrySetResult();
        if (action.ActionId is "activation-selected" or "activation-busy" or "activation-ready")
        {
            activationReadGate?.TrySetResult(); activationReadGate = null;
            source.UpdateContent(action.ActionId == "activation-selected" ? 304 : action.ActionId == "activation-busy" ? 305 : 306);
        }
        if (action.ActionId == "activation-replace") { source.PublishQuery(300, 100); entry = source.Enter("items", ++focusRequest, source.FocusTarget("items", new("item.75"), 75)); }
        if (action.ActionId == "activation-modal") { modalItem = 75; ++modalOpening; }
        if (action.ActionId == "context-mode")
        {
            modalMode = true; surfaces = true; grid = grouped = false; modalItem = null;
            source.PublishQuery(400, 100);
            entry = source.Enter("items", ++focusRequest, source.FocusTarget("items", new("item.75"), 75));
        }
        if (action.ActionId == "context-refresh") source.UpdateContent(401);
        if (action.ActionId == "context-replace") source.PublishQuery(402, 100);
        if (action.ActionId == "modal-mode")
        {
            modalMode = true; surfaces = true; grid = grouped = false; modalItem = null;
            source.PublishQuery(200, 100);
            entry = source.Enter("items", ++focusRequest, source.FocusTarget("items", new("item.75"), 75));
        }
        if (action.ActionId == "modal-dismiss") modalItem = null;
        if (action.ActionId == "modal-update") { ++modalRevision; source.UpdateContent(201); }
        if (action.ActionId == "modal-replace") source.PublishQuery(210, 100);
        if (action.ActionId == "grid") grid = !grid;
        if (action.ActionId == "groups") { grouped = !grouped; grid = true; source.UpdateContent(0); }
        if (action.ActionId == "surfaces") { surfaces = !surfaces; source.PublishQuery(surfaces ? 100 : 0, 100); }
        if (action.ActionId == "failure-mode") { surfaces = true; source.PublishQuery(500, 100); }
        if (action.ActionId == "group-label") { ++headerRevision; }
        if (action.ActionId == "group-partition") { grouped = true; repartitioned = !repartitioned; }
        if (action.ActionId == "focus-exact") entry = source.Enter("items", ++focusRequest, source.FocusTarget("items", new("item.75"), 75));
        if (action.ActionId == "focus-default") entry = source.Enter("items", ++focusRequest);
        if (action.ActionId == "focus-wrong") entry = source.Enter("items", ++focusRequest, source.FocusTarget("items", new("wrong-key"), 80));
        if (action.ActionId == "focus-stale")
        {
            entry = source.Enter("items", ++focusRequest, source.FocusTarget("items", new("item.75"), 75));
            source.PublishQuery(3, 100);
        }
        if (action.ActionId == "focus-clear") entry = null;
        if (action.ActionId == "focus-delayed")
        {
            source.PublishQuery(99, 100);
            entry = source.Enter("items", ++focusRequest, source.FocusTarget("items", new("item.75"), 75));
        }
        if (action.ActionId == "focus-disabled")
        {
            source.PublishQuery(98, 100);
            entry = source.Enter("items", ++focusRequest);
        }
        Invalidate(); return ValueTask.CompletedTask;
    }
    private WidgetElement Collection()
    {
        var element = grid ? UI.CollectionGrid("items", source, 180, 100, "Items", 5) : UI.CollectionList("items", source, 64, "Items");
        if (surfaces) return UI.BackgroundSurface(
            UI.FocusPresentationSurface(element.Shortcut(ControllerButton.X, "parent"), UI.Text("Default summary", "default-summary"), "summary-surface") with { RetainLastPresentation = true },
            "background-surface").UseFocusedDescendantArtwork();
        if (grouped && repartitioned) return element.Grouped(new("first", $"Section A {headerRevision}", 40), new("second", $"Section B {headerRevision}", 60)).Shortcut(ControllerButton.X, "parent");
        return (grouped ? element.Grouped(new("first", $"Section A {headerRevision}", 5), new("empty", "Empty section", 0),
            new("second", $"Section B {headerRevision}", 7), new("third", $"Section C {headerRevision}", 88)) : element).Shortcut(ControllerButton.X, "parent");
    }
}
