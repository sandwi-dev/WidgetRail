using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.IO;
using System.Text.Json;
using GameBarAlternative.AvaloniaPrototype.Diagnostics;
using GameBarAlternative.AvaloniaPrototype.Integration;
using GameBarAlternative.AvaloniaPrototype.Lifecycle;
using GameBarAlternative.AvaloniaPrototype.Navigation;
using GameBarAlternative.AvaloniaPrototype.Presentation;
using GameBarAlternative.AvaloniaPrototype.Platform;
using GameBarAlternative.AvaloniaPrototype.Views;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetPresentationSession;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WidgetStyling;
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
                new[] { typeof(WrapPanel), typeof(UniformGrid), typeof(ScrollViewer), typeof(TextBlock),
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
            var nodes = Flatten(frame.Snapshot.Root).ToDictionary(node => node.Id, StringComparer.Ordinal);
            foreach (var node in nodes.Values)
                _ = ControllerComponentCompiler.Compile(node, ReferenceEquals(node, frame.Snapshot.Root));
            foreach (var semantic in controls.Where(item =>
                         item.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty) is string))
            {
                var nodeId = semantic.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty)!;
                var node = nodes[nodeId];
                var component = ControllerComponentCompiler.Compile(
                    node, ReferenceEquals(node, frame.Snapshot.Root));
                CollectionAssert.Contains(semantic.Classes.ToArray(),
                    "component-" + component.Kind.ToString().ToLowerInvariant());
                Assert.AreEqual(component.NavigationZone,
                    semantic.GetValue(ComponentProperties.NavigationZoneProperty));
            }
            Assert.IsTrue(controls.OfType<TextBlock>().All(item => !item.IsHitTestVisible));
            Assert.IsTrue(controls.OfType<Image>().All(item => !item.IsHitTestVisible));
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Empty_typed_text_uses_accessibility_label_as_visible_readable_fallback()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            using var renderer = Renderer();
            var node = new ViewNode
            {
                Id = "accessible-status",
                Kind = ViewNodeKind.Text,
                Text = string.Empty,
                AccessibilityLabel = "Connection status unavailable",
            };
            var rendered = renderer.Render(Frame("accessible-text.widget", 1, node), isCompact: false);
            var window = new Window { Width = 420, Height = 340, Content = rendered };
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            var text = Assert.IsInstanceOfType<TextBlock>(rendered);
            Assert.AreEqual(node.AccessibilityLabel, text.Text);
            Assert.AreEqual(node.AccessibilityLabel, AutomationProperties.GetName(text));
            Assert.IsTrue(text.IsEffectivelyVisible);
            Assert.IsGreaterThan(0, text.Bounds.Width);
            Assert.IsGreaterThan(0, text.Bounds.Height);
            window.Close();
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
    public async Task Coordinator_leaves_invalidation_refresh_to_session_and_stale_loser_is_not_visible()
    {
        var initial = Frame("invalidation.widget", 1, ButtonTree("open"));
        var fake = new FakePresentationSession(initial)
        {
            RefreshFailure = new InvalidOperationException("An older snapshot completed after the current snapshot."),
        };
        await using var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
        await coordinator.InitializeAsync();

        fake.Invalidate(initial.Authority.WidgetId, revision: 1);
        var latest = Frame(initial.Authority.WidgetId, 2, ButtonTree("latest"));
        fake.Publish(latest);
        await WaitForAsync(() => Equals(coordinator.CurrentFrame?.Authority, latest.Authority));
        await Task.Delay(50);

        Assert.AreEqual(0, fake.RefreshCalls, "The session is the sole invalidation refresh owner.");
        Assert.AreEqual(latest.Authority, coordinator.CurrentFrame?.Authority);
        Assert.IsFalse(coordinator.ViewModel.HasFailure);
        Assert.DoesNotContain("older snapshot", coordinator.ViewModel.StatusText, StringComparison.OrdinalIgnoreCase);
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
            Assert.AreEqual(540, ((ListBox)rendered).MaxHeight);
            Assert.IsInstanceOfType<VirtualizingStackPanel>(((ListBox)rendered).ItemsPanel?.Build());
            var first = rendered.GetVisualDescendants().OfType<Control>()
                .First(control => control.GetValue(SemanticTreeRenderer.CollectionItemKeyProperty) is not null);
            Assert.AreEqual("stable-00000", first.GetValue(SemanticTreeRenderer.CollectionItemKeyProperty));
            Assert.AreEqual(88, first.Height);
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
            var list = shell.ActivePage!.GetVisualDescendants().OfType<ListBox>().Single();
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
        using var renderer = Renderer();
        var semantic = renderer.Render(frame, true);
        CollectionAssert.AreEqual(new[] { "compact" }, VisibleSemanticIds(semantic));
        Assert.IsTrue(renderer.SetCompact(semantic, false));
        CollectionAssert.AreEqual(new[] { "expanded" }, VisibleSemanticIds(semantic));
        Assert.AreEqual(1, renderer.TrackedRenderCount);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Responsive_visibility_moves_hidden_focus_to_visible_scope_then_restores_exact_mode_identity()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var root = new ViewNode
            {
                Id = "responsive-root",
                Kind = ViewNodeKind.Stack,
                Children =
                [
                    new ViewNode
                    {
                        Id = "expanded-action", FocusPersistenceId = "focus-expanded",
                        Kind = ViewNodeKind.Button, Text = "Expanded action", ActionId = "expanded",
                        VisibleWhen = ResponsiveVisibility.ExpandedOnly,
                    },
                    new ViewNode
                    {
                        Id = "compact-action", FocusPersistenceId = "focus-compact",
                        Kind = ViewNodeKind.Button, Text = "Compact action", ActionId = "compact",
                        VisibleWhen = ResponsiveVisibility.CompactOnly,
                    },
                ],
            };
            var fake = new FakePresentationSession(Frame("responsive-focus.widget", 1, root,
                surface: Surface(978, 466, 420, 340)));
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: true);
            var window = new Window { Width = 978, Height = 466, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => shell.AdmittedWidgetId == "responsive-focus.widget");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            var expanded = SemanticControl(shell, "expanded-action");
            Assert.IsTrue(expanded.Focus(NavigationMethod.Directional));
            shell.SetEvidenceViewport(new Avalonia.Size(420, 340));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            var compact = SemanticControl(shell, "compact-action");
            Assert.AreSame(compact, window.FocusManager?.GetFocusedElement());
            Assert.IsTrue(compact.IsEffectivelyVisible);
            Assert.IsFalse(expanded.IsEffectivelyVisible);
            var peer = ControlAutomationPeer.CreatePeerForElement(window);
            Assert.IsNotNull(peer);
            Assert.IsFalse(AutomationPeers(peer).Any(candidate =>
                string.Equals(candidate.GetAutomationId(), AutomationProperties.GetAutomationId(expanded),
                    StringComparison.Ordinal)));
            foreach (var direction in new[]
                     {
                         NavigationDirection.Up, NavigationDirection.Down,
                         NavigationDirection.Left, NavigationDirection.Right,
                     })
            {
                Assert.AreNotSame(expanded,
                    GameBarAlternative.AvaloniaPrototype.Navigation.FocusNavigator.FindTarget(
                        window.FocusManager, compact, direction, [shell.ActivePage!]));
            }

            shell.SetEvidenceViewport(new Avalonia.Size(978, 466));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            Assert.AreSame(expanded, window.FocusManager?.GetFocusedElement());
            Assert.IsTrue(expanded.IsEffectivelyVisible);
            Assert.AreEqual(1, shell.TrackedRenderCount);
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task Generic_shell_allocates_useful_width_readable_controls_and_non_overlapping_regions_at_supported_layouts_and_scales()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var fake = new FakePresentationSession(Frame(
                "responsive.widget",
                9,
                GeometryTree(),
                surface: Surface(980, 700, 320, 240, WidgetSurfaceMode.Wide)));
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: false);
            var window = new Window { Width = 1600, Height = 1000, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => shell.AdmittedWidgetId == "responsive.widget");

            foreach (var size in new[]
                     {
                         new Avalonia.Size(420, 340),
                         new Avalonia.Size(978, 466),
                         new Avalonia.Size(1180, 680),
                         new Avalonia.Size(1440, 810),
                     })
            foreach (var scale in new[] { 1d, 1.25d, 1.5d })
            {
                window.SetRenderScaling(scale);
                var workArea = new PixelRect(
                    0,
                    0,
                    (int)Math.Round(size.Width * scale, MidpointRounding.AwayFromZero),
                    (int)Math.Round(size.Height * scale, MidpointRounding.AwayFromZero));
                shell.SetHostEnvelopeConstraints(new WidgetEnvelopeConstraints(
                    workArea,
                    scale,
                    AccessibilityScale: 1,
                    EnvelopeInsets.PlatformPlacement));
                await WaitForAsync(() =>
                    Math.Abs(shell.PageHostElement.Bounds.Width - shell.CurrentEnvelope.ContentBounds.Width) <= 0.5 &&
                    Math.Abs(shell.PageHostElement.Bounds.Height - shell.CurrentEnvelope.ContentBounds.Height) <= 0.5);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var rendered = window.CaptureRenderedFrame();
                Assert.IsNotNull(rendered);

                var page = BoundsInShell(shell.PageHostElement, shell);
                var guide = BoundsInShell(shell.ControllerGuideElement, shell);
                var tray = BoundsInShell(shell.TrayElement, shell);
                var semantic = shell.ActiveSemanticRoot!;
                var semanticBounds = BoundsInShell(semantic, shell);
                Assert.IsGreaterThanOrEqualTo(shell.CurrentEnvelope.AdmittedContent.Width * 0.98, page.Width,
                    $"The page host must receive useful width at {size} / {scale}x.");
                Assert.IsFalse(Overlaps(page, guide), $"Content and controller guide overlap at {size} / {scale}x.");
                Assert.IsFalse(Overlaps(page, tray), $"Content and tray overlap at {size} / {scale}x.");
                Assert.IsFalse(Overlaps(guide, tray), $"Controller guide and tray overlap at {size} / {scale}x.");
                var availableSemanticWidth = Math.Max(1, page.Width - shell.PageHostElement.Padding.Left -
                    shell.PageHostElement.Padding.Right - 2);
                var horizontalEmptyRatio = 1 - Math.Min(1, semanticBounds.Width / availableSemanticWidth);
                var allocationChain = string.Join(" -> ", semantic.GetVisualAncestors().OfType<Control>()
                    .TakeWhile(item => !ReferenceEquals(item, shell.PageHostElement))
                    .Select(item => $"{item.GetType().Name}:{item.Bounds.Width:F1}/m={item.Margin}"));
                Assert.IsLessThanOrEqualTo(0.08, horizontalEmptyRatio,
                    $"The semantic root left too much unintended horizontal empty area at {size} / {scale}x " +
                    $"(page={page.Width:F1}, semantic={semanticBounds.Width:F1}, padding={shell.PageHostElement.Padding}, " +
                    $"mode={shell.ContentMode}, chain={allocationChain}).");

                foreach (var id in new[] { "long-copy", "primary-action", "volume" })
                {
                    var control = SemanticControl(shell, id);
                    Assert.IsTrue(control.IsEffectivelyVisible, $"{id} is not effectively visible at {size} / {scale}x.");
                    Assert.IsGreaterThanOrEqualTo(id == "long-copy" ? 120 : 44, control.Bounds.Width,
                        $"{id} is not readable at {size} / {scale}x.");
                    Assert.IsGreaterThanOrEqualTo(id == "long-copy" ? 16 : 36, control.Bounds.Height,
                        $"{id} has an unusable height at {size} / {scale}x.");
                }

                var grid = semantic.GetVisualDescendants().OfType<UniformGrid>().Single();
                Assert.IsGreaterThanOrEqualTo(1, grid.Columns);
                Assert.IsLessThanOrEqualTo(4, grid.Columns);
            }
            window.Close();
        });
    }

    [TestMethod]
    public void Widget_envelope_resolver_admits_atomic_pairs_and_clamps_against_work_area_dpi_and_accessibility()
    {
        var workArea = new PixelRect(120, 60, 1920, 1080);
        var authored = new[]
        {
            Surface(520, 520, 320, 360),
            Surface(760, 440, 480, 340),
            Surface(980, 700, 420, 340),
            Surface(1600, 1200, 360, 300),
        };
        var resolved = authored.Select(surface => WidgetEnvelopeResolver.Resolve(
            surface,
            new WidgetEnvelopeConstraints(workArea, 1, 1, EnvelopeInsets.PlatformPlacement)))
            .ToArray();

        Assert.AreEqual(4, resolved
            .Select(item => ($"{item.AdmittedContent.Width:F1}", $"{item.AdmittedContent.Height:F1}"))
            .Distinct()
            .Count(), "Authored widgets must retain materially distinct admitted content envelopes.");
        Assert.IsTrue(resolved.Take(3).All(item => item.PreferredPairAdmitted));
        Assert.IsTrue(resolved.All(item => item.MinimumPairSatisfied));
        foreach (var envelope in resolved)
        {
            Assert.IsTrue(Contains(new Rect(envelope.Window), envelope.ContentBounds));
            Assert.IsTrue(Contains(new Rect(envelope.Window), envelope.GuideBounds));
            Assert.IsTrue(Contains(new Rect(envelope.Window), envelope.TrayBounds));
            Assert.IsFalse(Overlaps(envelope.ContentBounds, envelope.GuideBounds));
            Assert.IsFalse(Overlaps(envelope.ContentBounds, envelope.TrayBounds));
            Assert.IsFalse(Overlaps(envelope.GuideBounds, envelope.TrayBounds));
            Assert.IsFalse(envelope.UsesFullWorkAreaBackdrop);
        }

        var constrained = WidgetEnvelopeResolver.Resolve(
            Surface(1600, 1200, 640, 480),
            new WidgetEnvelopeConstraints(new PixelRect(40, 20, 1280, 720), 1.25, 1.5,
                EnvelopeInsets.PlatformPlacement));
        Assert.IsTrue(constrained.WorkAreaClamped);
        Assert.IsLessThanOrEqualTo(constrained.LogicalWorkArea.Width -
            EnvelopeInsets.PlatformPlacement.Left - EnvelopeInsets.PlatformPlacement.Right,
            constrained.Window.Width);
        Assert.IsLessThanOrEqualTo(constrained.LogicalWorkArea.Height -
            EnvelopeInsets.PlatformPlacement.Top - EnvelopeInsets.PlatformPlacement.Bottom,
            constrained.Window.Height);
        Assert.AreEqual(976, constrained.AdmittedContent.Width, 0.01);
        Assert.AreEqual(400, constrained.AdmittedContent.Height, 0.01);

        var compactFallback = WidgetEnvelopeResolver.Resolve(
            Surface(560, 700, 320, 420, WidgetSurfaceMode.Compact),
            new WidgetEnvelopeConstraints(
                new PixelRect(0, 0, 420, 340),
                1,
                1,
                EnvelopeInsets.PlatformPlacement));
        Assert.AreEqual(372, compactFallback.AdmittedContent.Width, 0.01,
            "The physical height fallback must not unnecessarily collapse usable content width.");
        Assert.AreEqual(164, compactFallback.AdmittedContent.Height, 0.01);
        Assert.IsFalse(compactFallback.MinimumPairSatisfied,
            "The protocol minimum may be missed only because this physical work area cannot contain it plus fixed chrome.");

        var scaledWorkArea = new PixelRect(1920, 40, 2560, 1600);
        var scaledConstraints = new WidgetEnvelopeConstraints(
            scaledWorkArea, 1.25, 1, EnvelopeInsets.PlatformPlacement);
        var scaledCompact = WidgetEnvelopeResolver.Resolve(Surface(520, 520, 320, 360), scaledConstraints);
        var scaledWide = WidgetEnvelopeResolver.Resolve(Surface(980, 700, 420, 340), scaledConstraints);
        Assert.AreEqual(
            AnchoredPixelBounds(scaledCompact.GuideBounds, scaledCompact.Window, scaledWorkArea, 1.25),
            AnchoredPixelBounds(scaledWide.GuideBounds, scaledWide.Window, scaledWorkArea, 1.25));
        Assert.AreEqual(
            AnchoredPixelBounds(scaledCompact.TrayBounds, scaledCompact.Window, scaledWorkArea, 1.25),
            AnchoredPixelBounds(scaledWide.TrayBounds, scaledWide.Window, scaledWorkArea, 1.25));
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Surface_hints_atomically_resize_the_content_union_while_absolute_chrome_stays_anchored()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var compact = Frame("compact.widget", 1, ButtonTree("compact"),
                surface: new WidgetSurfaceHints
                {
                    Mode = WidgetSurfaceMode.Compact,
                    PreferredWidth = 420,
                    PreferredHeight = 340,
                    MinimumWidth = 320,
                    MinimumHeight = 240,
                });
            var wide = Frame("wide.widget", 1, ButtonTree("wide"),
                surface: new WidgetSurfaceHints
                {
                    Mode = WidgetSurfaceMode.Wide,
                    PreferredWidth = 1440,
                    PreferredHeight = 810,
                    MinimumWidth = 640,
                    MinimumHeight = 400,
                });
            var fake = new FakePresentationSession(compact, wide);
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: true);
            var workArea = new PixelRect(100, 50, 1920, 1080);
            shell.SetHostEnvelopeConstraints(new WidgetEnvelopeConstraints(
                workArea, 1, 1, EnvelopeInsets.PlatformPlacement));
            var window = new Window { Width = 1600, Height = 1000, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => shell.AdmittedWidgetId == compact.Authority.WidgetId);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            var compactEnvelope = shell.CurrentEnvelope;
            Assert.AreEqual(compact.Authority, shell.EnvelopeAuthority);
            Assert.AreEqual(new Size(420, 340), compactEnvelope.AdmittedContent);
            Assert.AreEqual(new Size(920, 460), compactEnvelope.Window);
            Assert.AreEqual(compactEnvelope.GuideBounds, BoundsInShell(shell.ControllerGuideElement, shell));
            Assert.AreEqual(compactEnvelope.TrayBounds, BoundsInShell(shell.TrayElement, shell));
            var trayBounds = AnchoredBounds(compactEnvelope.TrayBounds, compactEnvelope.Window, workArea);
            var guideBounds = AnchoredBounds(compactEnvelope.GuideBounds, compactEnvelope.Window, workArea);
            var trayParent = shell.TrayElement.GetVisualParent();
            var guideParent = shell.ControllerGuideElement.GetVisualParent();
            Assert.AreEqual(ContentResponsiveMode.Compact, shell.ContentMode);

            await coordinator.SelectWidgetAsync(wide.Authority.WidgetId);
            await WaitForAsync(() => shell.AdmittedWidgetId == wide.Authority.WidgetId);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            Assert.AreEqual(ContentResponsiveMode.Wide, shell.ContentMode);
            var wideEnvelope = shell.CurrentEnvelope;
            Assert.AreEqual(wide.Authority, shell.EnvelopeAuthority);
            Assert.AreEqual(new Size(1440, 810), wideEnvelope.AdmittedContent);
            Assert.AreEqual(new Size(1440, 930), wideEnvelope.Window);
            Assert.AreEqual(wideEnvelope.GuideBounds, BoundsInShell(shell.ControllerGuideElement, shell));
            Assert.AreEqual(wideEnvelope.TrayBounds, BoundsInShell(shell.TrayElement, shell));
            Assert.AreEqual(trayBounds, AnchoredBounds(wideEnvelope.TrayBounds, wideEnvelope.Window, workArea));
            Assert.AreEqual(guideBounds, AnchoredBounds(wideEnvelope.GuideBounds, wideEnvelope.Window, workArea));
            Assert.IsTrue(wideEnvelope.Window.Width < wideEnvelope.LogicalWorkArea.Width ||
                          wideEnvelope.Window.Height < wideEnvelope.LogicalWorkArea.Height);
            Assert.IsFalse(wideEnvelope.UsesFullWorkAreaBackdrop);
            Assert.AreSame(trayParent, shell.TrayElement.GetVisualParent());
            Assert.AreSame(guideParent, shell.ControllerGuideElement.GetVisualParent());
            Assert.IsFalse(shell.TrayElement.GetVisualAncestors().Any(item => item is TransitioningContentControl));
            Assert.IsFalse(shell.ControllerGuideElement.GetVisualAncestors().Any(item => item is TransitioningContentControl));
            Assert.IsTrue(shell.ActivePage!.GetVisualAncestors()
                .Any(item => ReferenceEquals(item, shell.TransitionPresenter)));
            Assert.IsInstanceOfType<TransitioningContentControl>(shell.TransitionPresenter);
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Typed_component_compiler_keeps_intrinsic_action_rows_glyphs_and_advanced_slots_readable()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var ordinaryRoot = new ViewNode
            {
                Id = "intrinsic-root",
                Kind = ViewNodeKind.Stack,
                InputScopeId = "root-scope",
                Children =
                [
                    new ViewNode
                    {
                        Id = "intrinsic-heading", Kind = ViewNodeKind.Text, Text = "Intrinsic settings heading",
                        StyleClasses = ["page-heading"],
                    },
                    new ViewNode
                    {
                        Id = "intrinsic-grid", Kind = ViewNodeKind.Grid,
                        GridMinimumColumnWidth = 260, GridMaximumColumns = 2,
                        Children = Enumerable.Range(0, 5).Select(index => new ViewNode
                        {
                            Id = $"intrinsic-action-{index}", Kind = ViewNodeKind.Button,
                            Text = $"Action row {index}", ActionId = $"action-{index}",
                            StyleClasses = ["setting-row"],
                        }).Append(new ViewNode
                        {
                            Id = "intrinsic-glyph-action", Kind = ViewNodeKind.Button,
                            Text = "Play", AccessibilityLabel = "Play", Glyph = WidgetGlyph.Play,
                            ActionId = "play", StyleClasses = ["transport-action"],
                        }).ToArray(),
                    },
                ],
            };
            var advancedRoot = AdvancedPresetTree();
            var smallScrollRoot = new ViewNode
            {
                Id = "small-intrinsic-scroll", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical,
                InputScopeId = "root-scope",
                Children = Enumerable.Range(0, 3).Select(index => new ViewNode
                {
                    Id = $"small-scroll-action-{index}", Kind = ViewNodeKind.ActionSurface,
                    ActionId = $"open-{index}",
                    Children =
                    [
                        new ViewNode { Id = $"small-scroll-title-{index}", Kind = ViewNodeKind.Text, Text = $"Connection {index}" },
                        new ViewNode { Id = $"small-scroll-copy-{index}", Kind = ViewNodeKind.Text, Text = "A longer status line remains readable without an arbitrary fixed row height." },
                    ],
                }).ToArray(),
            };
            var ordinary = Frame("intrinsic.widget", 1, ordinaryRoot,
                surface: Surface(880, 520, 520, 360));
            var advanced = Frame(
                "advanced.widget",
                1,
                advancedRoot,
                advanced: new WidgetAdvancedPresentationView(
                    WidgetAdvancedPresentationKind.LauncherExperience,
                    WidgetAdvancedPresentationPreset.CoverWall),
                surface: Surface(980, 700, 420, 340));
            var smallScroll = Frame("small-scroll.widget", 1, smallScrollRoot,
                surface: Surface(560, 700, 320, 420));
            var fake = new FakePresentationSession(ordinary, advanced, smallScroll);
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: true);
            var window = new Window { Width = 978, Height = 466, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => shell.AdmittedWidgetId == ordinary.Authority.WidgetId);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            var heading = (TextBlock)SemanticControl(shell, "intrinsic-heading");
            Assert.IsGreaterThanOrEqualTo(heading.LineHeight - 1, heading.Bounds.Height,
                "Typed body text must retain its intrinsic line height.");
            Assert.IsFalse(heading.Classes.Contains("page-heading"),
                "Authored style-class strings must not become hidden presentation roles.");
            var actions = Enumerable.Range(0, 5)
                .Select(index => BoundsInShell(SemanticControl(shell, $"intrinsic-action-{index}"), shell))
                .ToArray();
            for (var left = 0; left < actions.Length; left++)
            for (var right = left + 1; right < actions.Length; right++)
                Assert.IsFalse(Overlaps(actions[left], actions[right]),
                    $"Intrinsic action rows {left} and {right} must not overlap.");
            Assert.IsTrue(SemanticControl(shell, "intrinsic-glyph-action")
                .GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "▶" && text.Bounds.Width > 0),
                "A compact semantic button with a glyph must retain a meaningful visible icon.");

            await coordinator.SelectWidgetAsync(advanced.Authority.WidgetId);
            await WaitForAsync(() => shell.AdmittedWidgetId == advanced.Authority.WidgetId);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var page = BoundsInShell(shell.PageHostElement, shell);
            var details = BoundsInShell(SemanticControl(shell, "advanced-details"), shell);
            var collection = BoundsInShell(SemanticControl(shell, "advanced-collection"), shell);
            Assert.IsGreaterThanOrEqualTo(page.Width * 0.24, details.Width,
                "The generic advanced details slot must receive a readable allocation.");
            Assert.IsGreaterThanOrEqualTo(page.Width * 0.48, collection.Width,
                "The generic advanced primary collection must receive the dominant allocation.");
            Assert.IsFalse(Overlaps(details, collection));
            var advancedHeading = (TextBlock)SemanticControl(shell, "advanced-heading");
            Assert.IsGreaterThanOrEqualTo(advancedHeading.LineHeight - 1, advancedHeading.Bounds.Height);

            await coordinator.SelectWidgetAsync(smallScroll.Authority.WidgetId);
            await WaitForAsync(() => shell.AdmittedWidgetId == smallScroll.Authority.WidgetId);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var smallRows = Enumerable.Range(0, 3)
                .Select(index => BoundsInShell(SemanticControl(shell, $"small-scroll-action-{index}"), shell))
                .ToArray();
            Assert.IsTrue(smallRows.All(row => row.Height > 60),
                "Ordinary small semantic collections must retain intrinsic multi-line row height.");
            Assert.IsFalse(Overlaps(smallRows[0], smallRows[1]));
            Assert.IsFalse(Overlaps(smallRows[1], smallRows[2]));
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Adaptive_tray_scrolls_long_selected_item_fully_into_view_without_dominating_the_shell()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var names = new[]
            {
                "Settings", "Game Launcher", "Audio Mixer", "Network Controls",
                "Spotify", "Media Sessions", "YouTube Music", "Community Hub",
            };
            var surfaces = new[]
            {
                Surface(880, 520, 520, 360),
                Surface(980, 700, 420, 340),
                Surface(520, 520, 320, 360),
                Surface(560, 700, 320, 420),
                Surface(980, 560, 480, 360),
                Surface(580, 400, 360, 280),
                Surface(760, 440, 480, 340),
                Surface(820, 620, 420, 340),
            };
            var frames = names.Select((name, index) =>
                Frame($"tray.widget.{index}", 1, ButtonTree("open"), displayName: name,
                    surface: surfaces[index])).ToArray();
            var fake = new FakePresentationSession(frames);
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: true);
            var workArea = new PixelRect(100, 50, 1920, 1080);
            shell.SetHostEnvelopeConstraints(new WidgetEnvelopeConstraints(
                workArea, 1, 1, EnvelopeInsets.PlatformPlacement));
            var window = new Window { Width = 1600, Height = 1000, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => shell.AdmittedWidgetId == frames[0].Authority.WidgetId);

            Rect? fixedTray = null;
            Rect? fixedGuide = null;
            var admittedSizes = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < frames.Length; index++)
            {
                await coordinator.SelectWidgetAsync(frames[index].Authority.WidgetId);
                await WaitForAsync(() => shell.EnvelopeAuthority == frames[index].Authority);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                var envelope = shell.CurrentEnvelope;
                var absoluteTray = AnchoredBounds(envelope.TrayBounds, envelope.Window, workArea);
                var absoluteGuide = AnchoredBounds(envelope.GuideBounds, envelope.Window, workArea);
                fixedTray ??= absoluteTray;
                fixedGuide ??= absoluteGuide;
                Assert.AreEqual(fixedTray.Value, absoluteTray,
                    $"Tray moved while switching to {frames[index].Authority.WidgetId}.");
                Assert.AreEqual(fixedGuide.Value, absoluteGuide,
                    $"Controller guide moved while switching to {frames[index].Authority.WidgetId}.");
                admittedSizes.Add($"{envelope.AdmittedContent.Width:F1}x{envelope.AdmittedContent.Height:F1}");
            }
            Assert.IsGreaterThanOrEqualTo(4, admittedSizes.Count,
                "The installed fixtures must retain at least four materially distinct content envelopes.");

            var fixtures = new[]
            {
                new Avalonia.Size(420, 340),
                new Avalonia.Size(978, 466),
                new Avalonia.Size(1180, 680),
                new Avalonia.Size(1440, 810),
            };

            async Task AssertSelectedEdgeAsync(int selectedIndex)
            {
                await coordinator.SelectWidgetAsync(frames[selectedIndex].Authority.WidgetId);
                await WaitForAsync(() => shell.AdmittedWidgetId == frames[selectedIndex].Authority.WidgetId);
                foreach (var fixture in fixtures)
                {
                    shell.SetEvidenceViewport(fixture);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                    var viewport = BoundsInShell(shell.TrayScrollElement, shell);
                    var selectedBounds = BoundsInShell(shell.TrayButtons[selectedIndex], shell);
                    Assert.IsGreaterThanOrEqualTo(viewport.Left - 1, selectedBounds.Left,
                        $"Selected tray item {selectedIndex} lost its leading edge at {fixture}.");
                    Assert.IsLessThanOrEqualTo(viewport.Right + 1, selectedBounds.Right,
                        $"Selected tray item {selectedIndex} remained clipped after layout at {fixture}.");
                }
            }

            await AssertSelectedEdgeAsync(frames.Length - 1);
            await AssertSelectedEdgeAsync(0);

            foreach (var button in shell.TrayButtons)
            {
                var label = button.GetVisualDescendants().OfType<TextBlock>().Last();
                Assert.IsGreaterThanOrEqualTo(label.DesiredSize.Width - 1, label.Bounds.Width,
                    $"Tray label '{label.Text}' must not be truncated inside its button.");
            }

            var guide = BoundsInShell(shell.ControllerGuideElement, shell);
            var tray = BoundsInShell(shell.TrayElement, shell);
            Assert.IsFalse(Overlaps(guide, tray));
            Assert.AreEqual(WidgetEnvelopeResolver.GuideHeightDip + WidgetEnvelopeResolver.TrayHeightDip,
                guide.Height + tray.Height, 0.1,
                "The fixed guide and tray metrics must remain independent from the admitted content envelope.");
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Responsive_evidence_retries_size_publication_until_authority_root_and_controls_are_coherent()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var initial = Frame("responsive-race.widget", 1, ButtonTree("old"));
            var replacement = Frame("responsive-race.widget", 2, new ViewNode
            {
                Id = "replacement-root",
                Kind = ViewNodeKind.Stack,
                InputScopeId = "root-scope",
                Children =
                [
                    new ViewNode
                    {
                        Id = "replacement-action",
                        Kind = ViewNodeKind.Button,
                        Text = "Replacement action",
                        ActionId = "new",
                    },
                ],
            });
            var fake = new FakePresentationSession(initial);
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: true);
            var window = new Window { Width = 978, Height = 466, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => Equals(shell.AdmittedAuthority, initial.Authority));

            var replacementPublished = false;
            shell.SizeChanged += (_, _) =>
            {
                if (replacementPublished) return;
                replacementPublished = true;
                fake.Publish(replacement);
            };
            var capture = await EvidenceScenario.CaptureResponsiveFixtureAsync(
                shell,
                new Avalonia.Size(420, 340),
                initial.Authority.WidgetId,
                TimeSpan.FromSeconds(3));

            Assert.IsTrue(replacementPublished, "The fixture must publish during the size transition.");
            Assert.AreEqual(replacement.Authority, capture.Frame.Authority);
            Assert.AreSame(shell.ActiveSemanticRoot, capture.SemanticRoot);
            CollectionAssert.AreEquivalent(
                new[] { "replacement-action" },
                capture.Controls.Select(control => control.NodeId).ToArray());
            Assert.IsTrue(capture.ExpectedIds.SetEquals(["replacement-action"]));
            Assert.IsEmpty(capture.MissingExpectedIds);
            Assert.IsEmpty(capture.ProbedFocusableIds);
            Assert.IsEmpty(capture.UnreachableFocusableIds);
            window.Close();
        });
    }

    [TestMethod]
    public void Expected_required_ids_treat_every_vertical_scroll_as_virtualized_even_when_small()
    {
        var root = new ViewNode
        {
            Id = "root",
            Kind = ViewNodeKind.Stack,
            Children =
            [
                new ViewNode { Id = "outside", Kind = ViewNodeKind.Text, Text = "Outside" },
                new ViewNode
                {
                    Id = "small-scroll",
                    Kind = ViewNodeKind.Scroll,
                    ScrollAxis = ScrollAxis.Vertical,
                    Children =
                    [
                        new ViewNode { Id = "inside-one", Kind = ViewNodeKind.Button, Text = "One", ActionId = "one" },
                        new ViewNode { Id = "inside-two", Kind = ViewNodeKind.Button, Text = "Two", ActionId = "two" },
                    ],
                },
            ],
        };

        var expected = EvidenceScenario.ExpectedNonVirtualizedRequiredIds(root, compact: false);

        Assert.IsTrue(expected.SetEquals(["outside"]));
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Responsive_evidence_reveals_outer_scroll_focusable_controls_then_restores_scroll_and_focus()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var children = Enumerable.Range(0, 24)
                .Select(index => new ViewNode
                {
                    Id = $"copy-{index}",
                    Kind = ViewNodeKind.Text,
                    Text = $"Supporting status line {index}",
                })
                .Append(new ViewNode
                {
                    Id = "below-fold-action",
                    Kind = ViewNodeKind.Button,
                    Text = "Below-fold action",
                    ActionId = "open",
                })
                .Append(new ViewNode
                {
                    Id = "below-fold-disabled",
                    Kind = ViewNodeKind.Button,
                    Text = "Disabled below-fold action",
                    ActionId = "disabled",
                    IsDisabled = true,
                })
                .ToArray();
            var frame = Frame("outer-scroll.widget", 1, new ViewNode
            {
                Id = "root",
                Kind = ViewNodeKind.Stack,
                InputScopeId = "root-scope",
                Children = children,
            });
            var fake = new FakePresentationSession(frame);
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: true);
            var window = new Window { Width = 420, Height = 340, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => Equals(shell.AdmittedAuthority, frame.Authority));
            var selectedTray = shell.TrayButtons.Single();
            selectedTray.Focus(NavigationMethod.Directional);
            var outer = shell.ActivePage!.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.IsNotNull(outer);
            var originalOffset = outer.Offset;
            var enabledTarget = SemanticControl(shell, "below-fold-action");
            var disabledTarget = SemanticControl(shell, "below-fold-disabled");
            var enabledFocusCount = 0;
            var disabledFocusCount = 0;
            enabledTarget.GotFocus += (_, _) => enabledFocusCount++;
            disabledTarget.GotFocus += (_, _) => disabledFocusCount++;
            var residencyPhases = new List<string>();

            var capture = await EvidenceScenario.CaptureResponsiveFixtureAsync(
                shell,
                new Avalonia.Size(420, 340),
                frame.Authority.WidgetId,
                TimeSpan.FromSeconds(3),
                residencyPhases.Add);

            Assert.IsEmpty(capture.MissingExpectedIds);
            Assert.IsEmpty(capture.UnreachableFocusableIds);
            CollectionAssert.AreEquivalent(
                new[] { "below-fold-action", "below-fold-disabled" },
                capture.ProbedFocusableIds.ToArray());
            var action = capture.Controls.Single(control => control.NodeId == "below-fold-action");
            Assert.IsTrue(action.Contained);
            Assert.IsTrue(action.StandardUiaIdentity);
            var disabled = capture.Controls.Single(control => control.NodeId == "below-fold-disabled");
            Assert.IsTrue(disabled.Contained);
            Assert.IsTrue(disabled.StandardUiaIdentity);
            Assert.AreEqual(1, enabledFocusCount,
                "An enabled below-fold target must receive actual Avalonia focus during its probe.");
            Assert.AreEqual(0, disabledFocusCount,
                "A disabled below-fold target must be proven through visibility/UIA without focus ownership.");
            CollectionAssert.AreEqual(
                new[] { "before-focus-reachability-probe", "after-focus-reachability-probe" },
                residencyPhases);
            Assert.IsGreaterThanOrEqualTo(0.92, capture.Geometry.SemanticWidthUtilization);
            Assert.AreEqual(originalOffset, outer.Offset);
            Assert.AreSame(selectedTray, window.FocusManager?.GetFocusedElement());
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Root_stack_hoists_direct_vertical_scroll_into_one_virtualized_reachable_owner()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var root = new ViewNode
            {
                Id = "hoisted-root", Kind = ViewNodeKind.Stack, InputScopeId = "root-scope",
                Children =
                [
                    new ViewNode
                    {
                        Id = "hoisted-header", Kind = ViewNodeKind.Stack,
                        Children =
                        [
                            new ViewNode { Id = "hoisted-eyebrow", Kind = ViewNodeKind.Text, Text = "CONTROL CENTER" },
                            new ViewNode { Id = "hoisted-title", Kind = ViewNodeKind.Text, Text = "Network Controls" },
                            new ViewNode { Id = "hoisted-status", Kind = ViewNodeKind.Text, Text = "Connected" },
                        ],
                    },
                    new ViewNode
                    {
                        Id = "hoisted-card", Kind = ViewNodeKind.Row,
                        Children = [new ViewNode { Id = "hoisted-card-copy", Kind = ViewNodeKind.Text, Text = "Current connection" }],
                    },
                    new ViewNode { Id = "hoisted-details", Kind = ViewNodeKind.Button, Text = "Connection details", ActionId = "details" },
                    new ViewNode
                    {
                        Id = "hoisted-tabs", Kind = ViewNodeKind.Row,
                        Children =
                        [
                            new ViewNode { Id = "hoisted-wifi", Kind = ViewNodeKind.Button, Text = "Wi-Fi", ActionId = "wifi" },
                            new ViewNode { Id = "hoisted-bluetooth", Kind = ViewNodeKind.Button, Text = "Bluetooth", ActionId = "bluetooth" },
                        ],
                    },
                    new ViewNode
                    {
                        Id = "hoisted-compact-body", Kind = ViewNodeKind.Scroll,
                        ScrollAxis = ScrollAxis.Vertical,
                        AccessibilityLabel = "Compact connections",
                        VisibleWhen = ResponsiveVisibility.CompactOnly,
                        Children =
                        [
                            new ViewNode
                            {
                                Id = "hoisted-compact-item", Kind = ViewNodeKind.Button,
                                Text = "Compact connection", ActionId = "open",
                            },
                        ],
                    },
                    new ViewNode
                    {
                        Id = "hoisted-body", Kind = ViewNodeKind.Scroll,
                        ScrollAxis = ScrollAxis.Vertical,
                        AccessibilityLabel = "Expanded connections",
                        VisibleWhen = ResponsiveVisibility.ExpandedOnly,
                        Children = Enumerable.Range(0, 20).Select(index => new ViewNode
                        {
                            Id = $"hoisted-item-{index}", Kind = ViewNodeKind.Button,
                            Text = $"Connection {index}", ActionId = "open",
                        }).ToArray(),
                    },
                ],
            };
            var frame = Frame("hoisted.widget", 1, root,
                surface: Surface(978, 466, 420, 340));
            var fake = new FakePresentationSession(frame);
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: true);
            var window = new Window { Width = 420, Height = 340, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => Equals(shell.AdmittedAuthority, frame.Authority));

            var capture = await EvidenceScenario.CaptureResponsiveFixtureAsync(
                shell, new Avalonia.Size(420, 340), frame.Authority.WidgetId, TimeSpan.FromSeconds(3));

            Assert.IsTrue(capture.UnreachableFocusableIds.Count == 0,
                $"Unreachable: {string.Join(',', capture.UnreachableFocusableIds)}; " +
                $"probed: {string.Join(',', capture.ProbedFocusableIds)}");
            Assert.IsEmpty(capture.MissingExpectedIds,
                "Controls contained in the coherent seed capture must survive later virtualization probes.");
            foreach (var id in new[] { "hoisted-details", "hoisted-wifi", "hoisted-bluetooth" })
                Assert.IsTrue(capture.Controls.Any(control => control.NodeId == id && control.Contained) ||
                    capture.ProbedFocusableIds.Contains(id),
                    $"{id} must be seed-contained or proven reachable through the one scroll owner.");
            var page = shell.ActivePage!;
            var outer = page.GetVisualDescendants().OfType<ListBox>().Single();
            Assert.HasCount(1, page.GetVisualDescendants().OfType<ListBox>());
            Assert.HasCount(1, page.GetVisualDescendants().OfType<ScrollViewer>());
            var section = ((IEnumerable<ViewNode>)outer.ItemsSource!).First(node =>
                node.Id == "hoisted-compact-body");
            outer.ScrollIntoView(section);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);

            var compactScroll = SemanticControl(shell, "hoisted-compact-body");
            Assert.IsTrue(compactScroll.IsEffectivelyVisible);
            Assert.IsFalse(page.GetVisualDescendants().OfType<Control>().Any(control =>
                (control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty) is
                    "hoisted-body" or "hoisted-item-0") && control.IsEffectivelyVisible));
            var compactAutomationId = AutomationProperties.GetAutomationId(compactScroll);
            StringAssert.StartsWith(compactAutomationId,
                SemanticAutomationIdentity.WidgetNodePrefix.TrimEnd('.'));
            var compactPeer = ControlAutomationPeer.CreatePeerForElement(window);
            Assert.IsNotNull(compactPeer);
            Assert.IsTrue(AutomationPeers(compactPeer).Any(peer =>
                string.Equals(peer.GetAutomationId(), compactAutomationId, StringComparison.Ordinal)));
            outer.ScrollIntoView(((IEnumerable<ViewNode>)outer.ItemsSource!).First(node =>
                node.Id == "hoisted-compact-item"));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            Assert.IsTrue(SemanticControl(shell, "hoisted-compact-item").IsEffectivelyVisible);

            shell.SetEvidenceViewport(new Avalonia.Size(978, 466));
            var expandedSection = ((IEnumerable<ViewNode>)outer.ItemsSource!).First(node =>
                node.Id == "hoisted-body");
            outer.ScrollIntoView(expandedSection);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var expandedScroll = SemanticControl(shell, "hoisted-body");
            Assert.IsTrue(expandedScroll.IsEffectivelyVisible);
            Assert.IsFalse(page.GetVisualDescendants().OfType<Control>().Any(control =>
                (control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty) is
                    "hoisted-compact-body" or "hoisted-compact-item") && control.IsEffectivelyVisible));
            var expandedAutomationId = AutomationProperties.GetAutomationId(expandedScroll);
            StringAssert.StartsWith(expandedAutomationId,
                SemanticAutomationIdentity.WidgetNodePrefix.TrimEnd('.'));
            var expandedPeer = ControlAutomationPeer.CreatePeerForElement(window);
            Assert.IsNotNull(expandedPeer);
            Assert.IsTrue(AutomationPeers(expandedPeer).Any(peer =>
                string.Equals(peer.GetAutomationId(), expandedAutomationId, StringComparison.Ordinal)));
            outer.ScrollIntoView(((IEnumerable<ViewNode>)outer.ItemsSource!).First(node =>
                node.Id == "hoisted-item-0"));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            Assert.IsTrue(SemanticControl(shell, "hoisted-item-0").IsEffectivelyVisible);
            Assert.HasCount(1, page.GetVisualDescendants().OfType<ListBox>());
            Assert.HasCount(1, page.GetVisualDescendants().OfType<ScrollViewer>());
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Generic_shell_ignores_widget_geometry_styles_and_keeps_outer_root_allocation_host_owned()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var root = GeometryTree();
            var rootStyle = new Dictionary<string, BridgeComputedStyleValue>
            {
                ["width"] = Length(100, "vw"),
                ["height"] = Length(100, "vh"),
                ["min-width"] = Length(280, "px"),
                ["max-width"] = Length(560, "px"),
            };
            var childStyle = new Dictionary<string, BridgeComputedStyleValue>
            {
                ["width"] = Length(100, "%"),
            };
            var compactInteractiveStyle = new Dictionary<string, BridgeComputedStyleValue>
            {
                ["width"] = Length(40, "px"),
                ["min-width"] = Length(0, "px"),
                ["background"] = Color("transparent"),
                ["border-color"] = Color("transparent"),
            };
            var styles = new Dictionary<string, BridgeNodeRenderStyles>
            {
                [root.Id] = Style(rootStyle),
                ["layout-row"] = Style(childStyle),
                ["primary-action"] = Style(compactInteractiveStyle),
            };
            var fake = new FakePresentationSession(Frame("styled-responsive.widget", 10, root, styles,
                surface: Surface(978, 466, 420, 340)));
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: true);
            var window = new Window { Width = 978, Height = 466, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => shell.AdmittedWidgetId == "styled-responsive.widget");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            var page = BoundsInShell(shell.PageHostElement, shell);
            var semantic = BoundsInShell(shell.ActiveSemanticRoot!, shell);
            var row = BoundsInShell(SemanticControl(shell, "layout-row"), shell);
            var action = SemanticControl(shell, "primary-action");
            Assert.IsGreaterThanOrEqualTo(page.Width * 0.92, semantic.Width);
            Assert.IsGreaterThanOrEqualTo(semantic.Width * 0.92, row.Width);
            Assert.IsGreaterThan(100, semantic.Height, "Widget height styles must not become outer-shell geometry.");
            Assert.IsGreaterThan(560, semantic.Width,
                "Widget max-width styling must not collapse the generic controller page into a desktop column.");
            Assert.IsGreaterThanOrEqualTo(44, action.Bounds.Width,
                "GBSS min-width:0 must not erase the host interactive readability minimum.");
            Assert.IsTrue(action is Button
            {
                Background: ISolidColorBrush { Color.A: > 0 },
                BorderBrush: ISolidColorBrush { Color.A: > 0 },
            }, "Raw transparent widget styles must not erase the reusable Avalonia component theme.");
            window.Close();
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
        Assert.IsTrue(MainWindow.CanRouteNativeInput(visible: true, shellAvailable: true),
            "The native visible lease must route even when foreground activation is unavailable.");
        Assert.IsFalse(MainWindow.CanRouteNativeInput(visible: false, shellAvailable: true));
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Input_trace_is_live_atomic_and_controller_frames_after_neutral_prime_remain_routable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"avp004-input-{Guid.NewGuid():N}.json");
        try
        {
            var trace = new InputTraceRecorder(path, "manual-tip");
            trace.Record("native-controller-state", true, false,
                detail: "connected=False;primed=False;readPath=GameInputVisibleLease;foregroundExclusive=False");
            var liveFlush = await trace.FlushAsync("manual-tip");
            Assert.IsTrue(liveFlush.Succeeded, liveFlush.Failure);

            using (var liveDocument = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
            {
                Assert.AreEqual("manual-tip", liveDocument.RootElement.GetProperty("SourceCommit").GetString());
                Assert.HasCount(1, liveDocument.RootElement.GetProperty("Entries").EnumerateArray().ToArray(),
                    "A manual-session trace must be readable before normal shutdown.");
            }
            Assert.IsEmpty(TraceTemporaryFiles(path), "Atomic publication must not leave a live partial artifact.");
            Assert.IsTrue(OverlayPlatformClient.IsControllerFrameRoutable(connected: true),
                "The actionable frame after the native neutral-prime frame must remain routable when primed=0.");
            Assert.IsFalse(OverlayPlatformClient.IsControllerFrameRoutable(connected: false));

            var exactFlush = await trace.FlushAsync("exact-commit");
            Assert.IsTrue(exactFlush.Succeeded, exactFlush.Failure);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var root = document.RootElement;
            Assert.IsTrue(root.GetProperty("NativeVisibleLeaseObserved").GetBoolean());
            Assert.IsFalse(root.GetProperty("NativeConnectedVisibleLeaseObserved").GetBoolean());
            Assert.IsFalse(root.GetProperty("NativeRoutedSemanticInputObserved").GetBoolean());
            Assert.IsFalse(root.GetProperty("DeterministicSharedRouterProofObserved").GetBoolean());
            Assert.IsFalse(root.GetProperty("RoutedSemanticInputObserved").GetBoolean());
            Assert.IsEmpty(root.GetProperty("HandledCategories").EnumerateArray().ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Y_shared_route_atomically_records_the_actual_handled_and_unhandled_results()
    {
        var path = Path.Combine(Path.GetTempPath(), $"avp004-y-route-{Guid.NewGuid():N}.json");
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var fake = new FakePresentationSession(Frame("y-route.widget", 1, ButtonTree("y-route")));
                var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
                await using var shell = new IntegratedShellView(coordinator, reducedMotion: true);
                var shellWindow = new Window { Width = 978, Height = 466, Content = shell };
                shellWindow.Show();
                await shell.InitializeAsync();
                await WaitForAsync(() => shell.AdmittedWidgetId == "y-route.widget");
                var focused = SemanticControl(shell, "action");
                Assert.IsTrue(focused.Focus(NavigationMethod.Directional));

                var routeWindow = new MainWindow();
                var trace = new InputTraceRecorder(path, "y-route-tip");
                var unhandled = await routeWindow.HandleYAsync(
                    ControllerEventPhase.Released, focused, shell);
                trace.Record("native-input-routed", true, true, "action",
                    "button=Y;phase=Released", unhandled);
                var unhandledFlush = await trace.FlushAsync("y-route-tip");
                Assert.IsTrue(unhandledFlush.Succeeded, unhandledFlush.Failure);

                using (var unhandledDocument = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
                {
                    var entries = unhandledDocument.RootElement.GetProperty("Entries")
                        .EnumerateArray().ToArray();
                    Assert.HasCount(1, entries);
                    Assert.IsFalse(entries[0].GetProperty("Handled").GetBoolean());
                    Assert.IsFalse(unhandledDocument.RootElement
                        .GetProperty("NativeRoutedSemanticInputObserved").GetBoolean());
                }

                fake.ControllerInputHandled = true;
                var handled = await routeWindow.HandleYAsync(
                    ControllerEventPhase.Released, focused, shell);
                trace.Record("native-input-routed", true, true, "action",
                    "button=Y;phase=Released", handled);
                var handledFlush = await trace.FlushAsync("y-route-tip");
                Assert.IsTrue(handledFlush.Succeeded, handledFlush.Failure);

                using var handledDocument = JsonDocument.Parse(await File.ReadAllTextAsync(path));
                var retained = handledDocument.RootElement.GetProperty("Entries")
                    .EnumerateArray().ToArray();
                Assert.HasCount(2, retained);
                Assert.IsFalse(retained[0].GetProperty("Handled").GetBoolean());
                Assert.IsTrue(retained[1].GetProperty("Handled").GetBoolean());
                Assert.IsTrue(handledDocument.RootElement
                    .GetProperty("NativeRoutedSemanticInputObserved").GetBoolean());
                Assert.IsEmpty(TraceTemporaryFiles(path));
                shellWindow.Close();
                await routeWindow.ShutdownAsync();
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Input_trace_denied_atomic_replace_retains_last_good_and_recovers_every_sequence_once()
    {
        var path = Path.Combine(Path.GetTempPath(), $"avp004-input-denied-{Guid.NewGuid():N}.json");
        try
        {
            var trace = new InputTraceRecorder(path, "denied-replacement-tip");
            trace.Record("native-controller-state", true, true, "tray-alpha",
                "connected=True;primed=True;readPath=GameInputVisibleLease;foregroundExclusive=True");
            var baselineFlush = await trace.FlushAsync("denied-replacement-tip");
            Assert.IsTrue(baselineFlush.Succeeded, baselineFlush.Failure);
            Assert.AreEqual(1L, baselineFlush.PersistedSequence);

            InputTraceFlushResult deniedFlush;
            using (var deniedReplacement = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    trace.Record("native-input-routed", true, true, "tray-alpha",
                        "navigation=Down;phase=Pressed", true);
                    trace.Record("native-input-routed", true, true, "content-beta",
                        "button=Y;phase=Released", false);
                });

                deniedFlush = await trace.FlushAsync("denied-replacement-tip");
                Assert.IsFalse(deniedFlush.Succeeded,
                    "A denied atomic replacement must be reported without escaping through Record or the UI dispatcher.");
                Assert.AreEqual(3L, deniedFlush.RequestedSequence);
                Assert.AreEqual(1L, deniedFlush.PersistedSequence,
                    "A denied replacement must retain the last-good publication boundary.");
                Assert.IsNotNull(deniedFlush.Failure);

                using var lastGoodDocument = JsonDocument.Parse(await File.ReadAllTextAsync(path));
                var lastGoodEntries = lastGoodDocument.RootElement.GetProperty("Entries")
                    .EnumerateArray().ToArray();
                Assert.HasCount(1, lastGoodEntries,
                    "Replacement denial must leave the prior artifact as valid JSON.");
                Assert.AreEqual(1L, lastGoodEntries[0].GetProperty("Sequence").GetInt64());
            }

            var recoveredFlush = await trace.FlushAsync("denied-replacement-tip");
            Assert.IsTrue(recoveredFlush.Succeeded, recoveredFlush.Failure);
            Assert.AreEqual(3L, recoveredFlush.RequestedSequence);
            Assert.AreEqual(3L, recoveredFlush.PersistedSequence);

            using var recoveredDocument = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var recoveredEntries = recoveredDocument.RootElement.GetProperty("Entries")
                .EnumerateArray().ToArray();
            Assert.HasCount(3, recoveredEntries);
            CollectionAssert.AreEqual(
                new long[] { 1, 2, 3 },
                recoveredEntries.Select(entry => entry.GetProperty("Sequence").GetInt64()).ToArray(),
                "Every bounded in-memory entry must be published exactly once and in order after retry.");
            Assert.IsTrue(recoveredEntries[1].GetProperty("Handled").GetBoolean());
            Assert.IsFalse(recoveredEntries[2].GetProperty("Handled").GetBoolean());
            Assert.IsEmpty(TraceTemporaryFiles(path), "Recovery must not leave unique publication temp files.");
        }
        finally
        {
            File.Delete(path);
            foreach (var temporaryPath in TraceTemporaryFiles(path)) File.Delete(temporaryPath);
        }
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Dynamic_shell_keeps_stationary_tray_cycles_enters_content_restores_back_and_records_native_transition_phases()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var frames = new[]
            {
                Frame("alpha.widget", 1, ButtonTree("alpha"),
                    surface: Surface(420, 340, 320, 240, WidgetSurfaceMode.Compact)),
                Frame("beta.widget", 1, ButtonTree("beta"),
                    surface: Surface(980, 700, 420, 340, WidgetSurfaceMode.Wide)),
                Frame("gamma.widget", 1, ButtonTree("gamma"),
                    surface: Surface(520, 520, 320, 360, WidgetSurfaceMode.Compact)),
            };
            var fake = new FakePresentationSession(frames);
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: false);
            var workArea = new PixelRect(100, 50, 1920, 1080);
            shell.SetHostEnvelopeConstraints(new WidgetEnvelopeConstraints(
                workArea, 1, 1, EnvelopeInsets.PlatformPlacement));
            shell.AbsoluteChromeBoundsProvider = () => new IntegratedAbsoluteChromeBounds(
                AnchoredPixelBounds(shell.CurrentEnvelope.GuideBounds, shell.CurrentEnvelope.Window, workArea, 1),
                AnchoredPixelBounds(shell.CurrentEnvelope.TrayBounds, shell.CurrentEnvelope.Window, workArea, 1));
            var window = new Window { Width = 1600, Height = 1000, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => shell.AdmittedWidgetId == "alpha.widget");
            var trayBounds = ((Control)shell.TrayButtons[0].GetVisualParent()!).Bounds;
            var selected = shell.TrayButtons[0];
            selected.Focus(NavigationMethod.Directional);
            Assert.AreEqual(ShellNavigationRegion.Tray, shell.NavigationRegion);
            Assert.IsTrue(shell.TryCycleTray(selected, 1));
            await WaitForAsync(() => shell.AdmittedWidgetId == "beta.widget");
            Assert.AreSame(shell.TrayButtons[1], window.FocusManager?.GetFocusedElement());
            Assert.AreEqual(trayBounds, ((Control)shell.TrayButtons[1].GetVisualParent()!).Bounds);
            Assert.IsTrue(shell.RouteKeyboard(GameBarAlternative.AvaloniaPrototype.Input.SemanticInput.Up,
                shell.TrayButtons[1]), "Tray Up must enter the selected widget through the shared semantic router.");
            var contentFocus = window.FocusManager?.GetFocusedElement() as Control;
            Assert.AreEqual("action", contentFocus?.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty));
            Assert.AreEqual(ShellNavigationRegion.Content, shell.NavigationRegion);
            Assert.AreEqual(ControllerNavigationZone.Component,
                contentFocus?.GetValue(ComponentProperties.NavigationZoneProperty));
            Assert.IsTrue(shell.RouteKeyboard(GameBarAlternative.AvaloniaPrototype.Input.SemanticInput.Down,
                contentFocus), "Down from the final widget control must return to the selected tray item.");
            Assert.AreSame(shell.TrayButtons[1], window.FocusManager?.GetFocusedElement());
            Assert.AreEqual(ShellNavigationRegion.Tray, shell.NavigationRegion);
            CollectionAssert.AreEqual(
                Enum.GetValues<GameBarAlternative.AvaloniaPrototype.Navigation.TransitionPhase>(),
                shell.TransitionSamples.Where(sample => sample.WidgetId == "beta.widget")
                    .TakeLast(3).Select(sample => sample.Phase).ToArray());
            Assert.IsTrue(shell.TransitionSamples.All(sample => sample.OpaqueBlackFallbackAbsent));

            await coordinator.SelectWidgetAsync("gamma.widget");
            await WaitForAsync(() => shell.AdmittedWidgetId == "gamma.widget");
            foreach (var widgetId in new[] { "beta.widget", "gamma.widget" })
            {
                var samples = shell.TransitionSamples.Where(sample => sample.WidgetId == widgetId)
                    .TakeLast(3).ToArray();
                CollectionAssert.AreEqual(
                    Enum.GetValues<GameBarAlternative.AvaloniaPrototype.Navigation.TransitionPhase>(),
                    samples.Select(sample => sample.Phase).ToArray());
                Assert.IsTrue(samples.All(sample => sample.AbsoluteGuideBounds is not null &&
                    sample.AbsoluteTrayBounds is not null));
                Assert.AreEqual(1, samples.Select(sample => sample.AbsoluteGuideBounds).Distinct().Count(),
                    $"Guide drifted during the {widgetId} envelope transition.");
                Assert.AreEqual(1, samples.Select(sample => sample.AbsoluteTrayBounds).Distinct().Count(),
                    $"Tray drifted during the {widgetId} envelope transition.");
            }
            var betaCompletion = shell.TransitionSamples.Last(sample =>
                sample.WidgetId == "beta.widget" && sample.Phase == TransitionPhase.Completion);
            var gammaCompletion = shell.TransitionSamples.Last(sample =>
                sample.WidgetId == "gamma.widget" && sample.Phase == TransitionPhase.Completion);
            Assert.AreEqual(betaCompletion.AbsoluteGuideBounds, gammaCompletion.AbsoluteGuideBounds);
            Assert.AreEqual(betaCompletion.AbsoluteTrayBounds, gammaCompletion.AbsoluteTrayBounds);
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Same_widget_snapshot_supersession_admits_only_latest_exact_authority_and_releases_predecessor()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var initial = Frame("same.widget", 1, ButtonTree("one"));
            var fake = new FakePresentationSession(initial);
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: false);
            var window = new Window { Width = 978, Height = 466, Content = shell };
            window.Show();

            await shell.InitializeAsync();
            fake.Publish(Frame("same.widget", 2, ButtonTree("two")));
            var latest = Frame("same.widget", 3, ButtonTree("three"));
            fake.Publish(latest);

            await WaitForAsync(() => Equals(shell.AdmittedAuthority, latest.Authority));
            Assert.AreEqual(latest.Authority, shell.AdmittedAuthority);
            Assert.AreEqual(latest.Authority, coordinator.CurrentFrame?.Authority);
            Assert.AreEqual(1, shell.TrackedRenderCount);
            CollectionAssert.AreEqual(
                Enum.GetValues<GameBarAlternative.AvaloniaPrototype.Navigation.TransitionPhase>(),
                shell.TransitionSamples.Where(sample => sample.WidgetId == "same.widget")
                    .GroupBy(sample => sample.SnapshotSequence)
                    .Single(group => group.Any(sample => sample.Phase == GameBarAlternative.AvaloniaPrototype.Navigation.TransitionPhase.Completion))
                    .Select(sample => sample.Phase).ToArray());
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Newer_widget_supersedes_inflight_transition_without_stale_admission_or_focus()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var alpha = Frame("alpha.widget", 1, ButtonTree("alpha"));
            var beta = Frame("beta.widget", 1, ButtonTree("beta"));
            var gamma = Frame("gamma.widget", 1, ButtonTree("gamma"));
            var fake = new FakePresentationSession(alpha, beta, gamma);
            var coordinator = new WidgetIntegrationCoordinator(fake, new ImmediateScheduler());
            await using var shell = new IntegratedShellView(coordinator, reducedMotion: false);
            var window = new Window { Width = 978, Height = 466, Content = shell };
            window.Show();
            await shell.InitializeAsync();
            await WaitForAsync(() => Equals(shell.AdmittedAuthority, alpha.Authority));

            await coordinator.SelectWidgetAsync(beta.Authority.WidgetId);
            await coordinator.SelectWidgetAsync(gamma.Authority.WidgetId);

            await WaitForAsync(() => Equals(shell.AdmittedAuthority, gamma.Authority));
            await Task.Delay(200);
            Assert.AreEqual(gamma.Authority, shell.AdmittedAuthority);
            Assert.AreEqual(gamma.Authority, coordinator.CurrentFrame?.Authority);
            Assert.AreEqual(1, shell.TrackedRenderCount);
            Assert.IsFalse(shell.TransitionSamples.Any(sample =>
                sample.WidgetId == beta.Authority.WidgetId &&
                sample.Phase == GameBarAlternative.AvaloniaPrototype.Navigation.TransitionPhase.Completion));
            CollectionAssert.AreEqual(
                Enum.GetValues<GameBarAlternative.AvaloniaPrototype.Navigation.TransitionPhase>(),
                shell.TransitionSamples.Where(sample => sample.WidgetId == gamma.Authority.WidgetId)
                    .TakeLast(3).Select(sample => sample.Phase).ToArray());
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Releasing_superseded_render_cancels_artwork_and_disposes_owned_bitmaps()
    {
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new SemanticTreeRenderer(
            _ => Task.CompletedTask,
            async (_, _, token) =>
            {
                using var registration = token.Register(() => cancellationObserved.TrySetResult());
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return ReadOnlyMemory<byte>.Empty;
            },
            _ => Task.FromResult<string?>(null));
        var pending = renderer.Render(Frame("art.widget", 1, new ViewNode
        {
            Id = "art", Kind = ViewNodeKind.Image, ArtworkHandle = "cover",
        }), false);
        Assert.AreEqual(1, renderer.PendingArtworkRequestCount);
        renderer.Release(pending);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForAsync(() => renderer.PendingArtworkRequestCount == 0);
        Assert.AreEqual(0, renderer.TrackedRenderCount);
        Assert.AreEqual(0, renderer.OwnedArtworkBitmapCount);

        var inline = renderer.Render(Frame("art.widget", 2, new ViewNode
        {
            Id = "inline", Kind = ViewNodeKind.Image,
            ImageSource = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=",
        }), false);
        Assert.AreEqual(1, renderer.OwnedArtworkBitmapCount);
        renderer.Release(inline);
        Assert.AreEqual(0, renderer.OwnedArtworkBitmapCount);
        renderer.Dispose();
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
            Assert.AreEqual(0, window.MinWidth,
                "A fixed window minimum must not override the resolved content-plus-chrome union.");
            Assert.AreEqual(0, window.MinHeight,
                "A fixed window minimum must not override work-area containment.");
            await window.ShutdownAsync();
        });
    }

    [TestMethod]
    public void Prototype_single_instance_guard_rejects_a_second_owner_for_the_same_bounded_name()
    {
        var name = $@"Local\GameBarAlternative.AvaloniaOverlayPrototype.Tests.{Guid.NewGuid():N}";
        Assert.IsTrue(PrototypeInstanceGuard.TryAcquire(out var first, name));
        using (first)
        {
            Assert.IsFalse(PrototypeInstanceGuard.TryAcquire(out var second, name));
            Assert.IsNull(second);
        }
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

    private static Control SemanticControl(IntegratedShellView shell, string id) =>
        shell.ActivePage!.GetVisualDescendants().OfType<Control>()
            .First(control => string.Equals(
                control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty), id, StringComparison.Ordinal));

    private static IEnumerable<AutomationPeer> AutomationPeers(AutomationPeer root)
    {
        yield return root;
        foreach (var child in root.GetChildren())
            foreach (var descendant in AutomationPeers(child)) yield return descendant;
    }

    private static WidgetPresentationFrame Frame(
        string widgetId,
        long sequence,
        ViewNode root,
        IReadOnlyDictionary<string, BridgeNodeRenderStyles>? renderStyles = null,
        string? displayName = null,
        WidgetAdvancedPresentationView? advanced = null,
        WidgetSurfaceHints? surface = null)
    {
        var descriptor = Descriptor(widgetId, displayName);
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
                Surface = surface,
                Root = root,
                AdvancedPresentation = advanced,
            },
            renderStyles ?? new Dictionary<string, BridgeNodeRenderStyles>());
    }

    private static WidgetSurfaceHints Surface(
        double preferredWidth,
        double preferredHeight,
        double minimumWidth,
        double minimumHeight,
        WidgetSurfaceMode mode = WidgetSurfaceMode.Adaptive) => new()
    {
        Mode = mode,
        PreferredWidth = preferredWidth,
        PreferredHeight = preferredHeight,
        MinimumWidth = minimumWidth,
        MinimumHeight = minimumHeight,
    };

    private static BridgeComputedStyleValue Length(double number, string unit) => new()
    {
        Kind = GbssValueKind.Length,
        Text = $"{number}{unit}",
        Number = number,
        Unit = unit,
    };

    private static BridgeComputedStyleValue Color(string text) => new()
    {
        Kind = GbssValueKind.Color,
        Text = text,
    };

    private static BridgeComputedStyleValue Number(double number) => new()
    {
        Kind = GbssValueKind.Number,
        Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Number = number,
    };

    private static BridgeNodeRenderStyles Style(IReadOnlyDictionary<string, BridgeComputedStyleValue> values) => new()
    {
        Base = values,
        Focused = values,
        Pressed = values,
    };

    private static BridgeWidgetDescriptor Descriptor(string id, string? displayName = null) => new()
    {
        Id = id,
        Name = displayName ?? "Generic fixture",
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
            new ViewNode { Id = "scroll", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Horizontal, Children = [new ViewNode { Id = "button", Kind = ViewNodeKind.Button, Text = "Button", ActionId = "button" }] },
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

    private static ViewNode GeometryTree() => new()
    {
        Id = "layout-root", Kind = ViewNodeKind.Stack, InputScopeId = "root-scope",
        Children =
        [
            new ViewNode
            {
                Id = "long-copy", Kind = ViewNodeKind.Text,
                Text = "Network and playback status remain readable across the complete admitted viewport.",
            },
            new ViewNode
            {
                Id = "layout-row", Kind = ViewNodeKind.Row,
                Children =
                [
                    new ViewNode { Id = "row-label", Kind = ViewNodeKind.Text, Text = "Volume" },
                    new ViewNode { Id = "volume", Kind = ViewNodeKind.Slider, Minimum = 0, Maximum = 100, Value = 50, Step = 5, ValueChangedActionId = "volume" },
                    new ViewNode { Id = "primary-action", Kind = ViewNodeKind.Button, Text = "Apply", ActionId = "apply" },
                ],
            },
            new ViewNode
            {
                Id = "layout-grid", Kind = ViewNodeKind.Grid,
                GridMinimumColumnWidth = 180, GridMaximumColumns = 4,
                Children = Enumerable.Range(0, 8).Select(index => new ViewNode
                {
                    Id = $"tile-{index}", Kind = ViewNodeKind.ActionSurface, ActionId = "open",
                    Children = [new ViewNode { Id = $"tile-label-{index}", Kind = ViewNodeKind.Text, Text = $"Item {index}" }],
                }).ToArray(),
            },
        ],
    };

    private static ViewNode AdvancedPresetTree() => new()
    {
        Id = "advanced-root", Kind = ViewNodeKind.Stack, InputScopeId = "root-scope",
        Children =
        [
            new ViewNode
            {
                Id = "advanced-details", Kind = ViewNodeKind.Stack,
                AdvancedPresentationSlot = WidgetAdvancedPresentationSlot.DetailsPanel,
                Children =
                [
                    new ViewNode
                    {
                        Id = "advanced-heading", Kind = ViewNodeKind.Text,
                        Text = "Selected library item", StyleClasses = ["page-heading"],
                    },
                    new ViewNode { Id = "advanced-copy", Kind = ViewNodeKind.Text, Text = "Readable details remain intrinsic." },
                ],
            },
            new ViewNode
            {
                Id = "advanced-collection", Kind = ViewNodeKind.Grid,
                AdvancedPresentationSlot = WidgetAdvancedPresentationSlot.PrimaryCollection,
                GridMinimumColumnWidth = 160, GridMaximumColumns = 3,
                Children = Enumerable.Range(0, 6).Select(index => new ViewNode
                {
                    Id = $"advanced-item-{index}", Kind = ViewNodeKind.ActionSurface,
                    ActionId = "open", Children = [new ViewNode { Id = $"advanced-label-{index}", Kind = ViewNodeKind.Text, Text = $"Item {index}" }],
                }).ToArray(),
            },
            new ViewNode
            {
                Id = "advanced-navigation", Kind = ViewNodeKind.Row,
                AdvancedPresentationSlot = WidgetAdvancedPresentationSlot.CollectionNavigation,
                Children = [new ViewNode { Id = "advanced-tab", Kind = ViewNodeKind.Button, Text = "Library", ActionId = "library" }],
            },
            new ViewNode
            {
                Id = "advanced-source", Kind = ViewNodeKind.Stack,
                AdvancedPresentationSlot = WidgetAdvancedPresentationSlot.SourceStatus,
                Children = [new ViewNode { Id = "advanced-source-copy", Kind = ViewNodeKind.Text, Text = "Source ready" }],
            },
            new ViewNode
            {
                Id = "advanced-operation", Kind = ViewNodeKind.Stack,
                AdvancedPresentationSlot = WidgetAdvancedPresentationSlot.OperationStatus,
                Children = [new ViewNode { Id = "advanced-operation-copy", Kind = ViewNodeKind.Text, Text = "No pending operation" }],
            },
            new ViewNode
            {
                Id = "advanced-hints", Kind = ViewNodeKind.Stack,
                AdvancedPresentationSlot = WidgetAdvancedPresentationSlot.ControllerHints,
                Children = [new ViewNode { Id = "advanced-hint-copy", Kind = ViewNodeKind.Text, Text = "A Open  B Back" }],
            },
        ],
    };

    private static Rect BoundsInShell(Control control, IntegratedShellView shell)
    {
        var origin = control.TranslatePoint(default, shell);
        Assert.IsNotNull(origin);
        return new Rect(origin.Value, control.Bounds.Size);
    }

    private static bool Overlaps(Rect left, Rect right) =>
        left.Left < right.Right && left.Right > right.Left &&
        left.Top < right.Bottom && left.Bottom > right.Top;

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.1 && inner.Top >= outer.Top - 0.1 &&
        inner.Right <= outer.Right + 0.1 && inner.Bottom <= outer.Bottom + 0.1;

    private static Rect AnchoredBounds(Rect local, Size window, PixelRect workArea)
    {
        var windowX = workArea.X + ((workArea.Width - window.Width) / 2);
        var windowY = workArea.Bottom - EnvelopeInsets.PlatformPlacement.Bottom - window.Height;
        return new Rect(windowX + local.X, windowY + local.Y, local.Width, local.Height);
    }

    private static PixelRect AnchoredPixelBounds(
        Rect local,
        Size window,
        PixelRect workArea,
        double scaling)
    {
        var windowWidth = (int)Math.Round(window.Width * scaling, MidpointRounding.AwayFromZero);
        var windowHeight = (int)Math.Round(window.Height * scaling, MidpointRounding.AwayFromZero);
        var windowX = workArea.X + ((workArea.Width - windowWidth) / 2);
        var windowY = workArea.Bottom - (int)Math.Round(
            EnvelopeInsets.PlatformPlacement.Bottom * scaling,
            MidpointRounding.AwayFromZero) - windowHeight;
        return new PixelRect(
            windowX + (int)Math.Round(local.X * scaling, MidpointRounding.AwayFromZero),
            windowY + (int)Math.Round(local.Y * scaling, MidpointRounding.AwayFromZero),
            (int)Math.Round(local.Width * scaling, MidpointRounding.AwayFromZero),
            (int)Math.Round(local.Height * scaling, MidpointRounding.AwayFromZero));
    }

    private static string[] TraceTemporaryFiles(string tracePath) => Directory
        .EnumerateFiles(
            Path.GetDirectoryName(tracePath)!,
            $"{Path.GetFileName(tracePath)}.*.tmp",
            SearchOption.TopDirectoryOnly)
        .ToArray();

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
        public event EventHandler<WidgetPresentationInvalidatedEventArgs>? Invalidated;
        public event EventHandler<WidgetPresentationDiagnosticEventArgs>? DiagnosticPublished { add { } remove { } }
        public int RefreshCalls { get; private set; }
        public Exception? RefreshFailure { get; init; }
        public bool ControllerInputHandled { get; set; }

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
        public Task<WidgetPresentationFrame> RefreshAsync(WidgetPresentationAuthority authority, CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            return RefreshFailure is null ? Task.FromResult(current) : Task.FromException<WidgetPresentationFrame>(RefreshFailure);
        }
        public Task<WidgetLifecycleState> RestartAsync(WidgetPresentationTarget target, CancellationToken cancellationToken = default) => Task.FromResult(WidgetLifecycleState.Interactive);
        public Task<WidgetOperationAdmission> SendActionAsync(WidgetPresentationAuthority authority, WidgetActionEvent action, CancellationToken cancellationToken = default)
        {
            Actions.Add((authority, action));
            return Task.FromResult(WidgetOperationAdmission.Enqueued);
        }
        public Task<bool> SendControllerInputAsync(WidgetPresentationAuthority authority, ControllerInputEvent input, CancellationToken cancellationToken = default)
        {
            ControllerInputs.Add((authority, input));
            return Task.FromResult(ControllerInputHandled);
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

        public void Invalidate(string widgetId, long revision) => Invalidated?.Invoke(
            this,
            new WidgetPresentationInvalidatedEventArgs(new WidgetPresentationInvalidation(widgetId, revision)));
    }
}
