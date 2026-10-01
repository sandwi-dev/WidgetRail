using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.OverlayPlatformClient;
using Windows.Foundation;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Real bridge and worker rows rendered by the shared native presenter.</summary>
internal sealed partial class IndexedWidgetValidationPage : Page, IAsyncDisposable
{
    private WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "Connecting", MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly string pipe;
    private readonly CancellationTokenSource lifetime = new();
    private PresentationSession? session;
    private Task? startup;
    private long publication;
    private string? failure;
    private string navigation = "pending";
    private string logicalFocus = "not-run";
    private string groupedFocus = "not-run";
    private string surfaces = "not-run";
    private string inputRoute = "not-run";
    private string modalResult = "not-run";
    private string activationResult = "not-run";
    private string contextResult = "not-run";
    private bool retired;

    public IndexedWidgetValidationPage(string pipe)
    {
        this.pipe = pipe;
        AutomationProperties.SetAutomationId(status, "IndexedWidget.Status");
        var layout = new Grid { RowSpacing = 8 };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        Grid.SetRow(presenter, 1);
        var probe = new Button { Content = "Surfaces" };
        AutomationProperties.SetAutomationId(probe, "IndexedWidget.SurfaceProbe");
        probe.Click += (_, _) => _ = ProbeSurfacesAsync();
        var diagnostics = new Grid();
        diagnostics.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        diagnostics.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        diagnostics.Children.Add(status);
        var inputProbe = new Button { Content = "Input" };
        AutomationProperties.SetAutomationId(inputProbe, "IndexedWidget.InputProbe");
        inputProbe.Click += (_, _) => _ = ProbeInputRouteAsync();
        var modalProbe = new Button { Content = "Modal" };
        AutomationProperties.SetAutomationId(modalProbe, "IndexedWidget.ModalProbe");
        modalProbe.Click += (_, _) => _ = ProbeModalAsync();
        var activationProbe = new Button { Content = "Activation" };
        AutomationProperties.SetAutomationId(activationProbe, "IndexedWidget.ActivationProbe");
        activationProbe.Click += (_, _) => _ = ProbeActivationAsync();
        var contextProbe = new Button { Content = "Context" };
        AutomationProperties.SetAutomationId(contextProbe, "IndexedWidget.ContextProbe");
        contextProbe.Click += (_, _) => _ = ProbeContextAsync();
        var scrollProbe = new Button { Content = "Scroll focus" };
        AutomationProperties.SetAutomationId(scrollProbe, "IndexedWidget.ScrollFocusProbe");
        scrollProbe.Click += (_, _) => _ = ProbeScrollFocusAsync();
        var probes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { probe, inputProbe, modalProbe, activationProbe, contextProbe, scrollProbe } };
        diagnostics.Children.Add(probes); Grid.SetColumn(probes, 1);
        layout.Children.Add(diagnostics); layout.Children.Add(presenter);
        var memoryProbe = new Button { Content = "Memory" };
        AutomationProperties.SetAutomationId(memoryProbe, "IndexedWidget.MementoProbe");
        memoryProbe.Click += (_, _) => _ = ProbeMementoAsync();
        probes.Children.Add(memoryProbe);
        Content = layout;
        presenter.Failed = error => { failure = error.ToString(); Observe(); };
        presenter.DispatchActionAsync = async request =>
        {
            try { if (session is not null) await session.SendActionAsync(request.Displayed, request.Action, lifetime.Token); }
            catch (Exception error) { failure = error.ToString(); Observe(); }
        };
        Loaded += (_, _) => startup ??= StartAsync();
        KeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case VirtualKey.F17: _ = ProbeScrollFocusAsync(); break;
                case VirtualKey.F14: _ = ProbeSurfacesAsync(); break;
                case VirtualKey.F15: _ = ProbeSuspensionAsync(); break;
                case VirtualKey.F16: _ = ProbeMementoAsync(); break;
                case VirtualKey.F1: _ = ProbeGroupedFocusAsync(); break;
                case VirtualKey.F2: _ = ProbeLogicalFocusAsync(); break;
                case VirtualKey.F5: if (View() is { } list) list.ScrollIntoView(list.Items[70], ScrollIntoViewAlignment.Leading); break;
                case VirtualKey.F4: _ = ProbeNavigationAsync(burst: true); break;
                case VirtualKey.F3:
                    presenter.MoveFocus(FocusNavigationDirection.Down);
                    Descendants(presenter).OfType<Button>().First(button => AutomationProperties.GetAutomationId(button) == "Widget.parent").Focus(FocusState.Keyboard);
                    break;
                case VirtualKey.F6: Observe(); break;
                case VirtualKey.F7: presenter.ActivateFocused(); break;
                case VirtualKey.F8: _ = presenter.HandleControllerButtonAsync(ControllerButton.X, origin: ControllerInputOrigin.AccessibilityAutomation); break;
                case VirtualKey.F9: presenter.MoveFocus(FocusNavigationDirection.Down); break;
                case VirtualKey.F10: _ = ProbeNavigationAsync(); break;
                case VirtualKey.F11: _ = RefreshContentAsync(); break;
                case VirtualKey.F12: _ = ProbeNavigationAsync(reverse: true); break;
                default: return;
            }
            args.Handled = true;
        };
    }

    private async Task StartAsync()
    {
        try
        {
            session = await PresentationSession.ConnectAsync(pipe, cancellationToken: lifetime.Token);
            presenter.Session = session;
            session.PresentationChanged += Changed;
            await session.ListWidgetsAsync(lifetime.Token);
            await session.EstablishPresentationAsync(session.GetTarget("indexed-owned"), WidgetLifecycleState.Interactive, lifetime.Token);
            if (Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--indexed-entry-validation") is { } entryResult)
                await ValidateEntryReplacementAsync(entryResult);
            if (Environment.GetCommandLineArgs().Contains("--indexed-state-validation"))
            {
                activationResult = "pending"; Observe();
                var result = await IndexedActivationValidation.RunAccessibilityAsync(presenter, session, lifetime.Token);
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "indexed-accessibility-result.json"), JsonSerializer.Serialize(result));
                activationResult = result.ResultCode + ":" + result.Checks.Count; Observe();
            }
            var styleSettings = Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--style-validation-settings");
            if (styleSettings is not null) await ProbeStylesAsync(styleSettings);
        }
        catch (Exception error) { failure = error.ToString(); Observe(); }
    }
    private void Changed(object? sender, WidgetPresentationChangedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (retired || args.State.PublicationRevision <= publication) return;
            publication = args.State.PublicationRevision;
            try
            {
                if (args.State.Failure is { } error) throw new InvalidOperationException(error.Message);
                if (args.State.LastGood is { } frame) presenter.Apply(frame);
            }
            catch (Exception error) { failure = error.ToString(); }
            Observe();
        });
    }
    private ListViewBase? View() => Descendants(presenter).OfType<ListViewBase>().FirstOrDefault();
    private async Task RefreshContentAsync()
    {
        try
        {
            if (session?.GetState("indexed-owned")?.LastGood is { } frame)
                await session.SendActionAsync(frame.Authority, new("content", "content", InputScopeId: "root"), lifetime.Token);
        }
        catch (Exception error) { failure = error.ToString(); Observe(); }
    }
    private async Task ProbeNavigationAsync(bool reverse = false, bool burst = false)
    {
        try
        {
            if (View() is not { } view) return;
            navigation = "pending";
            var samples = await NativeIndexedFocusProbe.RunAsync(view, frame => presenter.MoveFocus(
                    frame.DpadNavigation.Direction == NavigationDirection.Up ? FocusNavigationDirection.Up : FocusNavigationDirection.Down),
                startIndex: reverse ? 70 : 0, steps: reverse ? 20 : view is GridView ? 12 : 60,
                settleMilliseconds: burst ? 0 : 35, cancellationToken: lifetime.Token,
                direction: reverse ? NavigationDirection.Up : NavigationDirection.Down);
            navigation = $"last:{samples[^1].FocusedIndex};stalled:{samples.Zip(samples.Skip(1)).Count(pair => pair.First.FocusedIndex == pair.Second.FocusedIndex)}";
        }
        catch (Exception error) { navigation = "failed:" + error.Message; }
        Observe();
    }

    private async Task ProbeLogicalFocusAsync()
    {
        logicalFocus = "pending";
        try
        {
            Parent();
            await Send("focus-placeholder");
            await Until(() => View() is { } placeholderView && placeholderView.Items.Count == 34 &&
                placeholderView.ContainerFromIndex(33) is Control { IsLoaded: true });
            var placeholderView = View()!;
            var placeholder = (Control)placeholderView.ContainerFromIndex(33);
            Check(((IndexedItem<WidgetIndexedRow>)placeholderView.Items[33]).Value is null,
                "gated final partial-grid row was unexpectedly loaded");
            Check(!placeholder.IsEnabled && !placeholder.IsTabStop && !placeholder.Focus(FocusState.Keyboard),
                "empty final grid placeholder admitted native focus");
            Check(FocusId() == "Widget.parent", "empty final grid placeholder moved focus from its prior control");
            await Task.Delay(160, lifetime.Token);
            Check(!presenter.IsGuidePresentationReady(requireFocus: false), "elapsed guide debounce admitted unprepared visible rows");
            var pendingCollection = Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single();
            var transform = pendingCollection.RenderTransform;
            try
            {
                pendingCollection.RenderTransform = new TranslateTransform { Y = presenter.ActualHeight + 1000 };
                Check(presenter.IsGuidePresentationReady(requireFocus: false), "offscreen unrealized collection blocked the guide");
            }
            finally { pendingCollection.RenderTransform = transform; }
            Check(!presenter.IsGuidePresentationReady(requireFocus: false), "returning pending collection to viewport did not restore guide readiness gate");
            await Send("activation-release");
            await Until(() => FocusId() == "Widget.items.Item.33");
            await Until(() => presenter.IsGuidePresentationReady(requireFocus: true));
            Check(placeholder.IsEnabled && placeholder.IsTabStop &&
                Descendants(placeholder).OfType<TextBlock>().Any(text => text.Text == "Item 33"),
                "final grid row did not become focusable with its rendered payload");
            Parent();
            await Send("focus-placeholder-end");
            await Send("focus-exact");
            await Until(() => FocusId() == "Widget.items.Item.75");
            // A native focus attempt while the switcher owns input must not
            // replace the remembered row when the widget becomes interactive.
            await Until(() => View()?.ContainerFromIndex(74) is Control { IsLoaded: true, IsEnabled: true });
            presenter.SetPresentationInputEnabled(false);
            presenter.SetAutomaticFocusEnabled(false);
            Check(((Control)View()!.ContainerFromIndex(74)).Focus(FocusState.Programmatic),
                "fixture could not establish native fallback on another row");
            await Task.Delay(100, lifetime.Token);
            Check(FocusId() == "Widget.items.Item.75", "inactive native focus attempt replaced the remembered row");
            presenter.SetPresentationInputEnabled(true);
            presenter.SetAutomaticFocusEnabled(true);
            presenter.Enter(restoreNativeFocus: true);
            await Until(() => FocusId() == "Widget.items.Item.75");
            await Task.Delay(200, lifetime.Token);
            Check(FocusId() == "Widget.items.Item.75", "native fallback overwrote remembered collection item on entry");
            Parent();
            await Send("content", "content");
            await Task.Delay(300, lifetime.Token);
            Check(FocusId() == "Widget.parent", "consumed request replayed on content update");
            await Send("focus-default");
            await Until(() => FocusId() == "Widget.items.Item.75");
            Parent();
            presenter.MoveFocus(FocusNavigationDirection.Down);
            await Until(() => FocusId() == "Widget.items.Item.75");
            Parent();
            await Send("focus-wrong");
            await Task.Delay(500, lifetime.Token);
            Check(FocusId() == "Widget.parent", "wrong key moved focus");
            await Send("focus-stale");
            await Task.Delay(500, lifetime.Token);
            Check(FocusId() == "Widget.parent", "stale query moved focus");
            await Send("focus-default");
            await Until(() => FocusId() == "Widget.items.Item.0");
            Parent();
            await Send("focus-delayed");
            await Until(() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single().IsEntryPending);
            presenter.MoveFocus(FocusNavigationDirection.Left);
            Parent();
            await Task.Delay(500, lifetime.Token);
            Check(FocusId() == "Widget.parent", "superseded request stole focus");
            await Send("focus-delayed");
            await Until(() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single().IsEntryPending);
            await Send("focus-clear");
            Parent();
            await Task.Delay(300, lifetime.Token);
            Check(FocusId() == "Widget.parent", "withdrawn request stole focus");
            await Send("focus-disabled");
            await Until(() => FocusId() == "Widget.items.Item.2");
            Parent();
            await Send("focus-delayed");
            await Until(() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single().IsEntryPending);
            await Send("grid", "grid");
            await Until(() => FocusId() == "Widget.items.Item.75");
            await Send("grid", "grid");
            Parent();
            await Send("focus-delayed");
            await Until(() => Descendants(presenter).OfType<WidgetIndexedCollectionView>().Single().IsEntryPending);
            await Send("group-partition");
            await Until(() => FocusId() == "Widget.items.Item.75");
            await Send("groups", "groups");
            await Send("group-partition");
            await Send("groups", "groups");
            logicalFocus = "passed:22";
        }
        catch (Exception error) { logicalFocus = "failed:" + error.Message; }
        Observe();

        string FocusId() => XamlRoot is null || FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject current
            ? "" : AutomationProperties.GetAutomationId(current);
        void Parent() => Descendants(presenter).OfType<Button>().First(button => AutomationProperties.GetAutomationId(button) == "Widget.parent").Focus(FocusState.Keyboard);
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Until(Func<bool> ready,
            [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(ready))] string? waitingFor = null)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (!ready())
            {
                if (Environment.TickCount64 > deadline)
                {
                    var view = View();
                    var source = view?.ItemsSource as IndexedItemsSource<WidgetIndexedRow>;
                    var tail = source is { Count: > 33 } ? (IndexedItem<WidgetIndexedRow>)source[33] : null;
                    var container = view?.ContainerFromIndex(33) as Control;
                    throw new TimeoutException("Logical focus did not settle: " + JsonSerializer.Serialize(new
                    {
                        waitingFor, focus = FocusId(), source?.CompletedLoads, source?.FailedLoads,
                        source?.ResidentSlots, tail?.Key, tail?.Failed, hasValue = tail?.Value is not null,
                        leaseCurrent = tail?.Value?.Lease.IsCurrent, container?.IsEnabled, container?.IsTabStop,
                        entryPending = Descendants(presenter).OfType<WidgetIndexedCollectionView>().FirstOrDefault()?.IsEntryPending,
                        presenter = presenter.FocusDiagnostics(),
                    }));
                }
                await Task.Delay(20, lifetime.Token);
            }
        }
        async Task Send(string action, string owner = "root")
        {
            var frame = session!.GetState("indexed-owned")!.LastGood!;
            var previous = publication;
            await session.SendActionAsync(frame.Authority, new(action, owner, InputScopeId: "root"), lifetime.Token);
            await Until(() => publication > previous);
        }
    }

    private async Task ProbeGroupedFocusAsync()
    {
        groupedFocus = "pending";
        Observe();
        try
        {
            if (View() is not GridView view || view.ItemsPanelRoot is not ItemsWrapGrid { MaximumRowsOrColumns: 3 })
                throw new InvalidOperationException("Grouped probe requires its three-column grid fixture.");
            foreach (var (start, direction, expected) in new[]
            {
                (2, NavigationDirection.Down, 4), // final partial row in first group
                (4, NavigationDirection.Down, 6), // same column across empty group
                (6, NavigationDirection.Up, 4),
                (5, NavigationDirection.Up, 3),
                (11, NavigationDirection.Down, 12),
                (12, NavigationDirection.Up, 11),
            })
            {
                var samples = await NativeIndexedFocusProbe.RunAsync(view,
                    frame => presenter.MoveFocus(frame.DpadNavigation.Direction == NavigationDirection.Up
                        ? FocusNavigationDirection.Up : FocusNavigationDirection.Down), start, 1, 150, lifetime.Token, direction);
                if (samples[^1].FocusedIndex != expected) throw new InvalidOperationException($"Grouped move from {start}: expected {expected}, got {samples[^1].FocusedIndex}.");
            }
            var previousSource = view.ItemsSource;
            var scroll = Descendants(view).OfType<ScrollViewer>().First();
            var offset = scroll.VerticalOffset;
            var focused = FocusManager.GetFocusedElement(XamlRoot);
            var frame = session!.GetState("indexed-owned")!.LastGood!;
            var revision = publication;
            await session.SendActionAsync(frame.Authority, new("group-label", "root", InputScopeId: "root"), lifetime.Token);
            var deadline = Environment.TickCount64 + 5000;
            while (publication == revision)
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Header change was not published.");
                await Task.Delay(20, lifetime.Token);
            }
            await Task.Delay(150, lifetime.Token);
            if (!ReferenceEquals(previousSource, view.ItemsSource) || !ReferenceEquals(focused, FocusManager.GetFocusedElement(XamlRoot)) ||
                Math.Abs(offset - scroll.VerticalOffset) > 1) throw new InvalidOperationException("Header text update replaced focus/source or moved viewport.");
            groupedFocus = "passed:7";
        }
        catch (Exception error) { groupedFocus = "failed:" + error.Message; }
        Observe();
    }

    private async Task ProbeSurfacesAsync()
    {
        if (surfaces == "pending") return;
        surfaces = "pending"; Observe();
        try
        {
            var view = View() ?? throw new InvalidOperationException("Missing list.");
            await FocusItem(0);
            await Until(() => Summary() == "Summary 100:0" && Artwork()?.PixelWidth == 1);
            var first = ((IndexedItem<WidgetIndexedRow>)view.Items[0]).Value!;
            var second = ((IndexedItem<WidgetIndexedRow>)view.Items[1]).Value!;
            Check(ReferenceEquals(first.Lease, second.Lease), "artwork fixture must share the range lease");
            await FocusItem(1);
            await Until(() => Summary() == "Summary 100:1" && Artwork()?.PixelWidth == 2);
            await FocusItem(2);
            await Until(() => BackgroundState() == "pending");
            await FocusItem(1);
            await Until(() => Summary() == "Summary 100:1" && Artwork()?.PixelWidth == 2);
            var selected = Artwork();
            var frame = session!.GetState("indexed-owned")!.LastGood!;
            await session.SendActionAsync(frame.Authority, new("parent", "parent", InputScopeId: "root"), lifetime.Token);
            await Until(() => BackgroundState() == "completed");
            await Task.Delay(150, lifetime.Token);
            var actualFocus = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            Check(ReferenceEquals(selected, Artwork()) && Artwork()?.PixelWidth == 2,
                $"late same-page artwork replaced selected row: sameSource={ReferenceEquals(selected, Artwork())}; " +
                $"pixels={Artwork()?.PixelWidth}; summary={Summary()}; " +
                $"focus={(actualFocus is null ? "" : AutomationProperties.GetAutomationId(actualFocus))}; publication={publication}");
            await FocusItem(0);
            await Until(() => Summary() == "Summary 100:0" && Artwork()?.PixelWidth == 1);
            Descendants(presenter).OfType<Button>().First(button => AutomationProperties.GetAutomationId(button) == "Widget.parent").Focus(FocusState.Keyboard);
            view.ScrollIntoView(view.Items[80], ScrollIntoViewAlignment.Leading);
            await Until(() => view.ContainerFromIndex(80) is Control &&
                (view.ContainerFromIndex(0) is not FrameworkElement first || first.TransformToVisual(view).TransformPoint(default).Y + first.ActualHeight <= 0));
            await Task.Delay(250, lifetime.Token);
            Check(Summary() == "Summary 100:0" && HasArtwork(), "recycling lost retained presentation");
            await RefreshContentAsync();
            await Until(() => Summary() == "Summary 101:0");
            await FocusItem(80);
            await Until(() => Summary() == "Summary 101:80" && HasArtwork());
            Check(Descendants(presenter).OfType<WidgetPresentationSurface>().All(surface => !surface.IsTabStop), "surface became an input target");
            surfaces = "passed:8";
        }
        catch (Exception error) { surfaces = "failed:" + error.Message; }
        Observe();
        string? Summary() => Descendants(presenter).OfType<TextBlock>().Select(text => text.Text).FirstOrDefault(text => text.StartsWith("Summary ", StringComparison.Ordinal));
        bool HasArtwork() => Descendants(presenter).OfType<WidgetPresentationSurface>().Any(surface => surface.ArtworkSource is not null);
        BitmapImage? Artwork() => Descendants(presenter).OfType<WidgetPresentationSurface>().Select(surface => surface.ArtworkSource).OfType<BitmapImage>().FirstOrDefault();
        string? BackgroundState() => FindNode(session?.GetState("indexed-owned")?.LastGood?.Snapshot.Root, "background-state")?.Text;
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task FocusItem(int index)
        {
            var view = View()!;
            view.ScrollIntoView(view.Items[index], ScrollIntoViewAlignment.Leading);
            await Until(() => view.ContainerFromIndex(index) is Control { IsLoaded: true });
            ((Control)view.ContainerFromIndex(index)).Focus(FocusState.Keyboard);
        }
        async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (!condition())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Surface probe did not settle; summary=" + Summary());
                await Task.Delay(20, lifetime.Token);
            }
        }
    }

    private async Task ProbeInputRouteAsync()
    {
        if (inputRoute == "pending") return;
        inputRoute = "pending"; Observe();
        try
        {
            var parent = Descendants(presenter).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Widget.parent");
            parent.Focus(FocusState.Keyboard);
            var before = FindNode(session!.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "calls")?.Text;
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftBumper, ControllerEventPhase.Released,
                ControllerInputOrigin.AccessibilityAutomation, lifetime.Token);
            await Task.Delay(100, lifetime.Token);
            Check(FindNode(session.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "calls")?.Text == before, "release invoked a press-only shortcut");
            Check(await presenter.HandleControllerButtonAsync(ControllerButton.LeftBumper, origin: ControllerInputOrigin.AccessibilityAutomation,
                cancellationToken: lifetime.Token), "ordinary shortcut was not admitted");
            await Until(() => FocusId() == "Widget.items.Item.75");
            await presenter.HandleControllerButtonAsync(ControllerButton.X, origin: ControllerInputOrigin.AccessibilityAutomation,
                cancellationToken: lifetime.Token);
            await Until(() => FindNode(session.GetState("indexed-owned")!.LastGood!.Snapshot.Root, "status")?.Text == "parent");
            Check(FocusId() == "Widget.items.Item.75", "indexed ancestor shortcut moved focus");
            inputRoute = "passed:3";
        }
        catch (Exception error) { inputRoute = "failed:" + error.Message; }
        Observe();
        string? FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : null;
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 5000;
            while (!condition()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Input route did not settle"); await Task.Delay(20, lifetime.Token); }
        }
    }

    private static ViewNode? FindNode(ViewNode? root, string id)
    {
        if (root is null || root.Id == id) return root;
        foreach (var child in root.Children) if (FindNode(child, id) is { } found) return found;
        return null;
    }
    private async Task ProbeModalAsync()
    {
        if (modalResult == "pending" || session is null) return;
        modalResult = "pending"; Observe();
        var result = await IndexedModalValidation.RunAsync(presenter, session, lifetime.Token);
        modalResult = result.ResultCode == "passed" ? $"passed:{result.Checks.Count}" : "failed:" + result.Error;
        Observe();
    }
    private async Task ProbeActivationAsync()
    {
        if (activationResult == "pending" || session is null) return;
        activationResult = "pending"; Observe();
        var result = await IndexedActivationValidation.RunAsync(presenter, session, lifetime.Token);
        activationResult = result.ResultCode == "passed" ? $"passed:{result.Checks.Count}" : "failed:" + result.Error;
        Observe();
    }
    private async Task ProbeContextAsync()
    {
        if (contextResult == "pending" || session is null) return;
        contextResult = "pending"; Observe();
        var result = await IndexedContextMenuValidation.RunAsync(presenter, session, lifetime.Token);
        contextResult = result.ResultCode == "passed" ? $"passed:{result.Checks.Count}" : "failed:" + result.Error;
        Observe();
    }
    private void Observe()
    {
        if (retired) return;
        var nodes = Descendants(presenter).ToArray();
        var view = nodes.OfType<ListViewBase>().FirstOrDefault();
        var scroll = view is null ? null : Descendants(view).OfType<ScrollViewer>().FirstOrDefault();
        var focus = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as FrameworkElement;
        var bounds = focus is null || scroll is null ? default : focus.TransformToVisual(scroll).TransformBounds(new Rect(0, 0, focus.ActualWidth, focus.ActualHeight));
        var frame = session?.GetState("indexed-owned")?.LastGood;
        status.Text = JsonSerializer.Serialize(new
        {
            failure, publication, count = view?.Items.Count, control = view?.GetType().Name,
            realized = nodes.Count(node => node is ListViewItem or GridViewItem), images = nodes.OfType<WidgetArtworkView>().Count(image => image.Source is not null),
            rowContentApplies = nodes.OfType<WidgetIndexedRowView>().Sum(row => row.ContentApplyCount),
            skippedRowApplies = nodes.OfType<WidgetIndexedRowView>().Sum(row => row.SkippedContentApplyCount),
            imageWidth = nodes.OfType<WidgetArtworkView>().Select(image => image.ActualWidth).DefaultIfEmpty().Max(),
            focus = focus is null ? null : AutomationProperties.GetAutomationId(focus), y = bounds.Y,
            offset = scroll?.VerticalOffset, viewport = scroll?.ViewportHeight, height = view?.ActualHeight,
            status = FindNode(frame?.Snapshot.Root, "status")?.Text,
            calls = FindNode(frame?.Snapshot.Root, "calls")?.Text,
            columns = view?.ItemsPanelRoot is ItemsWrapGrid wrap ? wrap.MaximumRowsOrColumns : 1,
            revision = FindNode(frame?.Snapshot.Root, "items")?.IndexedCollection?.ContentRevision,
            navigation, logicalFocus, groupedFocus, surfaces, inputRoute, modalResult, activationResult, contextResult, scrollFocus,
            groupCount = FindNode(frame?.Snapshot.Root, "items")?.IndexedGroups?.Count ?? 0,
        });
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    public async ValueTask DisposeAsync()
    {
        retired = true; lifetime.Cancel();
        if (startup is not null) await startup;
        if (session is not null) session.PresentationChanged -= Changed;
        try { await presenter.DisposeAsync(); }
        finally
        {
            try { if (session is not null) await session.DisposeAsync(); }
            finally { lifetime.Dispose(); }
        }
    }
}
