using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameBarAlternative.AvaloniaPrototype.Integration;
using GameBarAlternative.AvaloniaPrototype.Presentation;
using GameBarAlternative.AvaloniaPrototype.Platform;
using GameBarAlternative.AvaloniaPrototype.Views;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetPresentationSession;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameBarAlternative.AvaloniaPrototype.Tests;

[TestClass]
public sealed class IntegrationAdapterTests
{
    [TestMethod]
    [Timeout(10_000)]
    public async Task Generic_renderer_maps_every_current_node_kind_to_standard_Avalonia_controls_and_UIA()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var requests = new List<SemanticActionRequest>();
            var renderer = new SemanticTreeRenderer(
                request => { requests.Add(request); return Task.CompletedTask; },
                (_, _, _) => Task.FromResult<ReadOnlyMemory<byte>>(Array.Empty<byte>()),
                _ => Task.FromResult<string?>("committed"));
            var frame = Frame("generic.widget", 4, AllKindsTree());
            var control = renderer.Render(frame, isCompact: false);
            var controls = control.GetVisualDescendants().OfType<Control>().Prepend(control).ToArray();

            CollectionAssert.IsSubsetOf(
                new[] { typeof(StackPanel), typeof(WrapPanel), typeof(ListBox), typeof(TextBlock),
                    typeof(Button), typeof(ProgressBar), typeof(Slider), typeof(Border), typeof(Image) },
                controls.Select(item => item.GetType()).Distinct().ToArray());
            foreach (var semantic in controls.Where(item =>
                         item.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty) is not null))
            {
                StringAssert.StartsWith(AutomationProperties.GetAutomationId(semantic),
                    SemanticAutomationIdentity.WidgetNodePrefix.TrimEnd('.'));
                Assert.IsFalse(string.IsNullOrWhiteSpace(AutomationProperties.GetName(semantic)));
            }
            Assert.AreEqual(Enum.GetValues<ViewNodeKind>().Length,
                Flatten(frame.Snapshot.Root).Select(node => node.Kind).Distinct().Count());
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Coordinator_publishes_on_UI_scheduler_and_dispatches_only_latest_exact_node_action_scope()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var first = Frame("generic.widget", 1, ButtonTree("open"));
            var fake = new FakePresentationSession(first);
            var scheduler = new AvaloniaUiScheduler();
            await using var coordinator = new WidgetIntegrationCoordinator(fake, scheduler);
            await coordinator.InitializeAsync();

            await coordinator.DispatchAsync(new SemanticActionRequest("action", "open"));
            Assert.AreEqual(1, fake.Actions.Count);
            Assert.AreEqual(first.Authority, fake.Actions[0].Authority);
            Assert.AreEqual(first.Authority.ActiveInputScopeId, fake.Actions[0].Action.InputScopeId);

            var replacement = Frame("generic.widget", 2, ButtonTree("launch"));
            var publication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            coordinator.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(IntegratedShellViewModel.CurrentFrame) &&
                    coordinator.CurrentFrame?.Authority.SnapshotSequence == 2)
                    publication.TrySetResult();
            };
            await Task.Run(() => fake.Publish(replacement));
            await publication.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsGreaterThan(0, scheduler.MarshalledInvocationCount);
            Assert.AreEqual(2L, coordinator.CurrentFrame!.Authority.SnapshotSequence);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                coordinator.DispatchAsync(new SemanticActionRequest("action", "open")));
            await coordinator.DispatchAsync(new SemanticActionRequest("action", "launch"));
            Assert.AreEqual(replacement.Authority, fake.Actions[^1].Authority);
            Assert.AreEqual("launch", fake.Actions[^1].Action.ActionId);
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Ten_thousand_item_scroll_is_virtualized_and_preserves_semantic_item_identity()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var items = Enumerable.Range(0, 10_000).Select(index => new ViewNode
            {
                Id = $"item-{index:D5}",
                CollectionItemKey = $"stable-{index:D5}",
                FocusPersistenceId = $"focus-{index:D5}",
                Kind = ViewNodeKind.ActionSurface,
                ActionId = "open",
                AccessibilityLabel = $"Item {index}",
                Children = [new ViewNode { Id = $"label-{index:D5}", Kind = ViewNodeKind.Text, Text = $"Item {index}" }],
            }).ToArray();
            var root = new ViewNode
            {
                Id = "library",
                Kind = ViewNodeKind.Scroll,
                ScrollAxis = ScrollAxis.Vertical,
                CollectionAnchorKey = "stable-00000",
                Children = items,
            };
            var renderer = Renderer();
            var rendered = renderer.Render(Frame("large.application", 8, root), false);
            var window = new Window { Width = 978, Height = 466, Content = rendered };
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            Assert.IsGreaterThan(2, renderer.RealizedControlCount);
            Assert.IsLessThan(100, renderer.RealizedControlCount,
                "The virtualized collection must not realize a page-shaped control tree per item.");
            var first = rendered.GetVisualDescendants().OfType<Control>()
                .First(control => control.GetValue(SemanticTreeRenderer.CollectionItemKeyProperty) is not null);
            Assert.AreEqual("stable-00000", first.GetValue(SemanticTreeRenderer.CollectionItemKeyProperty));
            var automationId = AutomationProperties.GetAutomationId(first);
            StringAssert.Contains(automationId, "widget.node");
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Snapshot_reorder_recycles_then_restores_exact_collection_focus_and_automation_identity()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var initialItems = CollectionItems(200).ToList();
            var first = Frame("reorder.widget", 1, new ViewNode
            {
                Id = "library", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical,
                Children = initialItems,
            });
            var fake = new FakePresentationSession(first);
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: true);
            var window = new Window { Width = 978, Height = 466, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => shell.AdmittedWidgetId == "reorder.widget");
            var list = (ListBox)shell.ActivePage!;
            var focusedNode = initialItems[150];
            list.ScrollIntoView(focusedNode);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            var focused = shell.ActivePage!.GetVisualDescendants().OfType<Control>()
                .First(control => control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty) == focusedNode.Id);
            Assert.IsTrue(focused.Focus(NavigationMethod.Directional));
            var automationBefore = AutomationProperties.GetAutomationId(focused);
            Assert.AreEqual(focusedNode.FocusPersistenceId, shell.RememberedFocus("reorder.widget"));

            var reordered = CollectionItems(5, "inserted").Concat(initialItems).ToArray();
            fake.Publish(Frame("reorder.widget", 2, new ViewNode
            {
                Id = "library", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical,
                Children = reordered,
            }));
            await WaitForAsync(() => coordinator.CurrentFrame?.Authority.SnapshotSequence == 2);
            await WaitForAsync(() => !shell.FocusRestorationPending && shell.LastFocusRestorationOutcome is not null);
            Assert.AreEqual(focusedNode.Id, shell.LastRestoredSemanticId,
                $"Requested: {shell.LastRequestedFocus}; restoration outcome: {shell.LastFocusRestorationOutcome}; available realized IDs: {string.Join(',', shell.ActivePage!.GetVisualDescendants().OfType<Control>().Select(control => control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty)).Where(id => id is not null))}");
            var restored = shell.ActivePage!.GetVisualDescendants().OfType<Control>()
                .First(control => control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty) == focusedNode.Id);
            Assert.AreEqual(focusedNode.CollectionItemKey,
                restored.GetValue(SemanticTreeRenderer.CollectionItemKeyProperty));
            Assert.AreEqual(automationBefore, AutomationProperties.GetAutomationId(restored));
            window.Close();
        });
    }

    [TestMethod]
    public void Responsive_visibility_uses_current_logical_surface_instead_of_scaled_duplicate_rectangles()
    {
        var root = new ViewNode
        {
            Id = "responsive",
            Kind = ViewNodeKind.Stack,
            Children =
            [
                new ViewNode { Id = "compact", Kind = ViewNodeKind.Button, Text = "Compact", ActionId = "compact", VisibleWhen = ResponsiveVisibility.CompactOnly },
                new ViewNode { Id = "expanded", Kind = ViewNodeKind.Button, Text = "Expanded", ActionId = "expanded", VisibleWhen = ResponsiveVisibility.ExpandedOnly },
            ],
        };
        var frame = Frame("responsive.widget", 3, root);
        var compact = Renderer().Render(frame, true);
        var expanded = Renderer().Render(frame, false);
        CollectionAssert.AreEqual(new[] { "compact" }, VisibleSemanticIds(compact));
        CollectionAssert.AreEqual(new[] { "expanded" }, VisibleSemanticIds(expanded));
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task Generic_renderer_is_contained_or_scrollable_at_compact_standard_wide_and_real_Avalonia_scales()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            foreach (var size in new[] { new Avalonia.Size(420, 340), new Avalonia.Size(978, 466), new Avalonia.Size(1440, 810) })
            foreach (var scale in new[] { 1d, 1.25d, 1.5d })
            {
                var semantic = Renderer().Render(Frame("responsive.widget", 9, AllKindsTree()), size.Width <= 700);
                var scroll = new ScrollViewer { Content = semantic };
                var window = new Window { Width = size.Width, Height = size.Height, Content = scroll };
                window.SetRenderScaling(scale);
                window.Show();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var rendered = window.CaptureRenderedFrame();
                Assert.IsNotNull(rendered);
                Assert.AreEqual((int)Math.Ceiling(size.Width * scale), rendered.PixelSize.Width);
                Assert.AreEqual((int)Math.Ceiling(size.Height * scale), rendered.PixelSize.Height);
                Assert.IsTrue(semantic.GetVisualDescendants().OfType<Button>().Any());
                Assert.IsTrue(semantic.GetVisualDescendants().OfType<Slider>().Any());
                window.Close();
            }
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Lifecycle_switch_hide_show_and_controller_dispatch_preserve_exact_current_authority()
    {
        var first = Frame("first.widget", 1, ButtonTree("first"));
        var second = Frame("second.widget", 7, ButtonTree("second"));
        var fake = new FakePresentationSession(first, second);
        await using var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
        await coordinator.InitializeAsync();
        await coordinator.SelectWidgetAsync(second.Authority.WidgetId);
        await coordinator.SetVisibleAsync(false);
        await coordinator.SetVisibleAsync(true);
        await coordinator.SendControllerInputAsync(
            ControllerButton.A, ControllerEventPhase.Pressed, "action");

        CollectionAssert.Contains(fake.Lifecycles,
            (first.Authority.WidgetId, WidgetLifecycleState.Background));
        CollectionAssert.Contains(fake.Lifecycles,
            (second.Authority.WidgetId, WidgetLifecycleState.Background));
        Assert.AreEqual(second.Authority, fake.ControllerInputs.Single().Authority);
        Assert.AreEqual(second.Authority.SnapshotSequence,
            fake.ControllerInputs.Single().Input.SnapshotSequence);
        Assert.AreEqual(second.Authority.ActiveInputScopeId,
            fake.ControllerInputs.Single().Input.ActiveInputScopeId);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Dynamic_shell_keeps_stationary_tray_cycles_enters_content_restores_back_and_records_native_transition_phases()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var frames = new[]
            {
                Frame("alpha.widget", 1, ButtonTree("alpha")),
                Frame("beta.widget", 1, ButtonTree("beta")),
                Frame("gamma.widget", 1, ButtonTree("gamma")),
            };
            var fake = new FakePresentationSession(frames);
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: false);
            var window = new Window { Width = 978, Height = 466, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => shell.AdmittedWidgetId == "alpha.widget");
            var trayBounds = ((Control)shell.TrayButtons[0].GetVisualParent()!).Bounds;
            var selected = shell.TrayButtons[0];
            selected.Focus(NavigationMethod.Directional);
            Assert.IsTrue(shell.TryCycleTray(selected, 1));
            await WaitForAsync(() => shell.AdmittedWidgetId == "beta.widget");
            Assert.AreSame(shell.TrayButtons[1], window.FocusManager?.GetFocusedElement());
            Assert.AreEqual(trayBounds, ((Control)shell.TrayButtons[1].GetVisualParent()!).Bounds);
            Assert.IsTrue(shell.TryEnterContent(shell.TrayButtons[1]));
            var contentFocus = window.FocusManager?.GetFocusedElement() as Control;
            Assert.AreEqual("action", contentFocus?.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty));
            Assert.IsTrue(shell.RestoreTrayFocus(contentFocus));
            Assert.AreSame(shell.TrayButtons[1], window.FocusManager?.GetFocusedElement());
            CollectionAssert.AreEqual(
                Enum.GetValues<GameBarAlternative.AvaloniaPrototype.Navigation.TransitionPhase>(),
                shell.TransitionSamples.Where(sample => sample.WidgetId == "beta.widget")
                    .TakeLast(3).Select(sample => sample.Phase).ToArray());
            Assert.IsTrue(shell.TransitionSamples.All(sample => sample.OpaqueBlackFallbackAbsent));
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Slider_change_dispatches_one_quantized_absolute_action()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var requests = new List<SemanticActionRequest>();
            var renderer = new SemanticTreeRenderer(
                request => { requests.Add(request); return Task.CompletedTask; },
                (_, _, _) => Task.FromResult<ReadOnlyMemory<byte>>(Array.Empty<byte>()),
                _ => Task.FromResult<string?>(null));
            var root = new ViewNode
            {
                Id = "root", Kind = ViewNodeKind.Stack,
                Children = [new ViewNode { Id = "volume", Kind = ViewNodeKind.Slider, Minimum = 0, Maximum = 100, Value = 50, Step = 5, ValueChangedActionId = "set-volume" }],
            };
            var rendered = renderer.Render(Frame("slider.widget", 1, root), false);
            var window = new Window { Width = 500, Height = 300, Content = rendered };
            window.Show();
            var slider = rendered.GetVisualDescendants().OfType<Slider>().Single();
            slider.Value = 57;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.HasCount(1, requests);
            Assert.AreEqual(new SemanticActionRequest("volume", "set-volume", 55), requests[0]);
            window.Close();
        });
    }

    [TestMethod]
    public void Automation_identity_is_collision_safe_bounded_and_preserves_exact_widget_node_and_collection_tuple()
    {
        var nodes = new[]
        {
            new ViewNode { Id = "a/b", CollectionItemKey = "x?y", Kind = ViewNodeKind.Button },
            new ViewNode { Id = "a?b", CollectionItemKey = "x/y", Kind = ViewNodeKind.Button },
            new ViewNode { Id = "é", CollectionItemKey = "e\u0301", Kind = ViewNodeKind.Button },
        };
        var ids = nodes.Select(node => SemanticAutomationIdentity.ForWidgetNode("widget/one", node)).ToArray();
        Assert.AreEqual(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(ids.All(id => id.Length <= SemanticAutomationIdentity.MaximumWidgetAutomationIdLength));
    }

    [TestMethod]
    public void Managed_native_ABI_matches_accepted_v1_layout_and_candidate_has_no_Vortice_reference()
    {
        var sizes = OverlayPlatformClient.ManagedAbiLayoutSizes;
        Assert.AreEqual(32, sizes["PlatformEvent"]);
        Assert.AreEqual(12, sizes["RawControllerState"]);
        Assert.AreEqual(8, sizes["NavigationEvent"]);
        Assert.AreEqual(76, sizes["ControllerFrame"]);
        Assert.AreEqual(48, sizes["PlacementInput"]);
        Assert.AreEqual(24, sizes["Placement"]);
        Assert.AreEqual(32, sizes["CreateOptions"]);
        Assert.IsFalse(typeof(MainWindow).Assembly.GetReferencedAssemblies()
            .Any(assembly => assembly.Name?.Contains("Vortice", StringComparison.OrdinalIgnoreCase) == true));
    }

    [TestMethod]
    public async Task Window_contract_is_one_transparent_borderless_topmost_Avalonia_shell()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = new MainWindow();
            Assert.AreEqual(WindowDecorations.None, window.WindowDecorations);
            Assert.IsTrue(window.Topmost);
            Assert.IsFalse(window.ShowInTaskbar);
            CollectionAssert.Contains(window.TransparencyLevelHint.ToArray(), WindowTransparencyLevel.Transparent);
            Assert.AreEqual(0, ((ISolidColorBrush)window.Background!).Color.A);
            Assert.AreEqual(420, window.MinWidth);
            Assert.AreEqual(340, window.MinHeight);
            await window.ShutdownAsync();
        });
    }

    private static SemanticTreeRenderer Renderer() => new(
        _ => Task.CompletedTask,
        (_, _, _) => Task.FromResult<ReadOnlyMemory<byte>>(Array.Empty<byte>()),
        _ => Task.FromResult<string?>(null));

    private static string[] VisibleSemanticIds(Control root) => root.GetVisualDescendants().OfType<Control>()
        .Prepend(root)
        .Where(control => control.IsVisible && control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty) is not null)
        .Select(control => control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty)!)
        .Where(id => id is "compact" or "expanded")
        .ToArray();

    private static WidgetPresentationFrame Frame(string widgetId, long sequence, ViewNode root)
    {
        var descriptor = Descriptor(widgetId);
        var authority = new WidgetPresentationAuthority(
            widgetId, descriptor.RuntimeGeneration, descriptor.PresentationGeneration,
            1, descriptor.InstanceId, sequence, "root-scope");
        return new WidgetPresentationFrame(
            authority,
            descriptor,
            new ViewSnapshot
            {
                Sequence = sequence,
                WidgetInstanceId = descriptor.InstanceId,
                ActiveInputScopeId = "root-scope",
                InitialFocusId = Flatten(root).FirstOrDefault(node => node.IsFocusable)?.Id,
                Root = root,
            },
            new Dictionary<string, BridgeNodeRenderStyles>());
    }

    private static BridgeWidgetDescriptor Descriptor(string id) => new()
    {
        Id = id,
        Name = "Generic fixture",
        InstanceId = $"{id}.instance",
        RuntimeGeneration = "11111111111111111111111111111111",
        PresentationGeneration = "22222222222222222222222222222222",
        Icon = WidgetGlyph.Connection,
    };

    private static ViewNode ButtonTree(string action) => new()
    {
        Id = "root",
        Kind = ViewNodeKind.Stack,
        InputScopeId = "root-scope",
        Children = [new ViewNode { Id = "action", Kind = ViewNodeKind.Button, Text = "Action", ActionId = action }],
    };

    private static ViewNode AllKindsTree() => new()
    {
        Id = "stack", Kind = ViewNodeKind.Stack, InputScopeId = "root-scope",
        Children =
        [
            new ViewNode { Id = "row", Kind = ViewNodeKind.Row, Children = [new ViewNode { Id = "text", Kind = ViewNodeKind.Text, Text = "Text" }] },
            new ViewNode { Id = "scroll", Kind = ViewNodeKind.Scroll, Children = [new ViewNode { Id = "button", Kind = ViewNodeKind.Button, Text = "Button", ActionId = "button" }] },
            new ViewNode { Id = "progress", Kind = ViewNodeKind.Progress, Value = 50, Minimum = 0, Maximum = 100 },
            new ViewNode { Id = "slider", Kind = ViewNodeKind.Slider, Value = 25, Minimum = 0, Maximum = 100, Step = 5, ValueChangedActionId = "change" },
            new ViewNode { Id = "spacer", Kind = ViewNodeKind.Spacer },
            new ViewNode { Id = "image", Kind = ViewNodeKind.Image, AccessibilityLabel = "Image" },
            new ViewNode { Id = "icon", Kind = ViewNodeKind.Icon, Glyph = WidgetGlyph.Check },
            new ViewNode { Id = "loading", Kind = ViewNodeKind.LoadingIndicator },
            new ViewNode { Id = "surface", Kind = ViewNodeKind.ActionSurface, ActionId = "surface", Children = [new ViewNode { Id = "surface-text", Kind = ViewNodeKind.Text, Text = "Surface" }] },
            new ViewNode { Id = "grid", Kind = ViewNodeKind.Grid, Children = [new ViewNode { Id = "grid-text", Kind = ViewNodeKind.Text, Text = "Grid" }] },
            new ViewNode { Id = "entry", Kind = ViewNodeKind.TextEntry, ActionId = "commit", TextEntryPlaceholder = "Text" },
        ],
    };

    private static IEnumerable<ViewNode> CollectionItems(int count, string prefix = "item") =>
        Enumerable.Range(0, count).Select(index => new ViewNode
        {
            Id = $"{prefix}-{index:D5}",
            CollectionItemKey = $"{prefix}-key-{index:D5}",
            FocusPersistenceId = $"{prefix}-focus-{index:D5}",
            Kind = ViewNodeKind.ActionSurface,
            ActionId = "open",
            AccessibilityLabel = $"{prefix} {index}",
            Children = [new ViewNode { Id = $"{prefix}-label-{index:D5}", Kind = ViewNodeKind.Text, Text = $"{prefix} {index}" }],
        });

    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child)) yield return descendant;
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) Assert.Fail("Timed out waiting for shell publication.");
            await Task.Delay(20);
        }
    }

    private sealed class ImmediateScheduler : IPresentationScheduler
    {
        public bool CheckAccess() => true;
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    }

    private sealed class FakePresentationSession(params WidgetPresentationFrame[] frames) : IPresentationSessionClient
    {
        private readonly Dictionary<string, WidgetPresentationFrame> byId = frames.ToDictionary(
            frame => frame.Authority.WidgetId, StringComparer.Ordinal);
        private WidgetPresentationFrame current = frames[0];
        public List<(WidgetPresentationAuthority Authority, WidgetActionEvent Action)> Actions { get; } = [];
        public List<(string WidgetId, WidgetLifecycleState State)> Lifecycles { get; } = [];
        public List<(WidgetPresentationAuthority Authority, ControllerInputEvent Input)> ControllerInputs { get; } = [];
        public event EventHandler<WidgetPresentationChangedEventArgs>? PresentationChanged;
        public event EventHandler<WidgetPresentationInvalidatedEventArgs>? Invalidated { add { } remove { } }
        public event EventHandler<WidgetPresentationDiagnosticEventArgs>? DiagnosticPublished { add { } remove { } }

        public Task<WidgetPresentationCatalog> ListWidgetsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new WidgetPresentationCatalog(1, byId.Values.Select(frame => frame.Descriptor).ToArray()));
        public WidgetPresentationTarget GetTarget(string widgetId) => new(1, byId[widgetId].Descriptor);
        public Task<WidgetPresentationFrame> EstablishPresentationAsync(WidgetPresentationTarget target, WidgetLifecycleState state, CancellationToken cancellationToken = default)
        {
            current = byId[target.Descriptor.Id];
            Lifecycles.Add((target.Descriptor.Id, state));
            return Task.FromResult(current);
        }
        public Task SetLifecycleAsync(WidgetPresentationTarget target, WidgetLifecycleState state, CancellationToken cancellationToken = default)
        {
            Lifecycles.Add((target.Descriptor.Id, state));
            return Task.CompletedTask;
        }
        public Task<WidgetPresentationFrame> RefreshAsync(WidgetPresentationAuthority authority, CancellationToken cancellationToken = default) => Task.FromResult(current);
        public Task<WidgetLifecycleState> RestartAsync(WidgetPresentationTarget target, CancellationToken cancellationToken = default) => Task.FromResult(WidgetLifecycleState.Interactive);
        public Task<WidgetOperationAdmission> SendActionAsync(WidgetPresentationAuthority authority, WidgetActionEvent action, CancellationToken cancellationToken = default)
        {
            Actions.Add((authority, action));
            return Task.FromResult(WidgetOperationAdmission.Enqueued);
        }
        public Task<bool> SendControllerInputAsync(WidgetPresentationAuthority authority, ControllerInputEvent input, CancellationToken cancellationToken = default)
        {
            ControllerInputs.Add((authority, input));
            return Task.FromResult(false);
        }
        public Task<WidgetOperationAdmission> InvokeQuickActionAsync(WidgetPresentationTarget target, string quickActionId, long sequence, long monotonicTimestampMicroseconds, CancellationToken cancellationToken = default) => Task.FromResult(WidgetOperationAdmission.Enqueued);
        public Task<WidgetPresentationArtwork> ResolveArtworkAsync(WidgetPresentationAuthority authority, string artworkHandle, CancellationToken cancellationToken = default) => Task.FromResult(new WidgetPresentationArtwork(authority, artworkHandle, Array.Empty<byte>()));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Publish(WidgetPresentationFrame replacement)
        {
            current = replacement;
            byId[replacement.Authority.WidgetId] = replacement;
            PresentationChanged?.Invoke(this, new WidgetPresentationChangedEventArgs(
                new WidgetPresentationState(replacement.Authority.WidgetId, replacement, null, 0)));
        }
    }
}
