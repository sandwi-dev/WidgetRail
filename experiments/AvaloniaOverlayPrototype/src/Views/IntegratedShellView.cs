using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameBarAlternative.AvaloniaPrototype.Integration;
using GameBarAlternative.AvaloniaPrototype.Input;
using GameBarAlternative.AvaloniaPrototype.Navigation;
using GameBarAlternative.AvaloniaPrototype.Presentation;
using GameBarAlternative.WidgetPresentationSession;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.AvaloniaPrototype.Views;

public sealed record IntegratedTransitionSample(
    string WidgetId,
    long SnapshotSequence,
    TransitionPhase Phase,
    bool TransparentShellRoot,
    bool OpaqueBlackFallbackAbsent,
    bool AvaloniaSurfaceCoveragePresent,
    int VisualChildCount);

public enum ShellNavigationRegion
{
    Tray,
    Content,
    Modal,
}

public sealed class IntegratedShellView : UserControl, IAsyncDisposable
{
    private readonly WidgetIntegrationCoordinator coordinator;
    private readonly SemanticTreeRenderer renderer;
    private readonly PageTransitionPresenter transitionPresenter = new();
    private readonly Border pageHost;
    private readonly Grid shellGrid;
    private readonly StackPanel trayPanel;
    private readonly ScrollViewer trayScroll;
    private readonly Border trayLayer;
    private readonly Border statusLayer;
    private readonly Border controllerGuideLayer;
    private readonly TextBlock controllerGuideText;
    private readonly Border modalLayer;
    private readonly TextBox modalTextBox;
    private readonly Dictionary<string, Button> trayButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> focusMemory = new(StringComparer.Ordinal);
    private readonly Dictionary<(string WidgetId, bool Compact), string> responsiveFocusMemory = [];
    private readonly List<IntegratedTransitionSample> transitionSamples = [];
    private readonly object admissionGate = new();
    private readonly bool reducedMotion;
    private TaskCompletionSource<string?>? modalCompletion;
    private Task admissionPump = Task.CompletedTask;
    private CancellationTokenSource? activeAdmission;
    private WidgetPresentationFrame? pendingFrame;
    private RenderedPage? admittedPresentation;
    private string? transitioningWidgetId;
    private bool admissionPumpRunning;
    private WidgetPresentationFrame? renderedFrame;
    private bool disposed;
    private bool suppressFocusMemory;
    private int trayRevealGeneration;
    private string? admittedWidgetId;
    private ShellProfile shellProfile = ShellProfile.Standard;
    private ContentResponsiveMode contentMode = ContentResponsiveMode.Standard;
    private ShellNavigationRegion navigationRegion = ShellNavigationRegion.Tray;

    public IntegratedShellView(WidgetIntegrationCoordinator coordinator, bool reducedMotion)
    {
        this.coordinator = coordinator;
        this.reducedMotion = reducedMotion;
        if (reducedMotion)
        {
            transitionPresenter.PageTransition = null;
            transitionPresenter.ReducedMotion = true;
        }
        renderer = new SemanticTreeRenderer(
            request => coordinator.DispatchAsync(request),
            ResolveArtworkAsync,
            RequestTextAsync);

        XYFocus.SetNavigationModes(this, XYFocusNavigationModes.Enabled);
        AutomationProperties.SetAutomationId(this, "avp.integrated.shell");
        AutomationProperties.SetName(this, "Game Bar Alternative Avalonia widget shell");

        pageHost = new Border
        {
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(20),
            Background = Brush.Parse("#EB172230"),
            BorderBrush = Brush.Parse("#7092B7E8"),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = transitionPresenter,
        };
        pageHost.Classes.Add("shell-page-host");
        AutomationProperties.SetAutomationId(pageHost, "avp.integrated.content");
        AutomationProperties.SetName(pageHost, "Current widget content");

        trayPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(12, 0),
        };
        trayScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = trayPanel,
        };
        trayLayer = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(18),
            Background = Brush.Parse("#F21A2432"),
            BorderBrush = Brush.Parse("#7898BCE9"),
            BorderThickness = new Thickness(1),
            Child = trayScroll,
        };
        trayLayer.Classes.Add("shell-tray");
        AutomationProperties.SetAutomationId(trayLayer, "avp.integrated.tray");
        AutomationProperties.SetName(trayLayer, "Stationary widget tray");

        var statusView = new IntegratedStatusView { DataContext = coordinator.ViewModel };
        statusLayer = new Border
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0),
            Padding = new Thickness(14, 6),
            CornerRadius = new CornerRadius(10),
            Background = Brush.Parse("#F02B3D55"),
            Child = statusView,
        };
        AutomationProperties.SetAutomationId(statusLayer, "avp.integrated.status");

        controllerGuideText = new TextBlock
        {
            Text = "D-pad / stick  Navigate    A  Open    B  Back    Hold Y  Restart",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false,
        };
        controllerGuideLayer = new Border
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0),
            Padding = new Thickness(12, 3),
            CornerRadius = new CornerRadius(8),
            Background = Brush.Parse("#EA111923"),
            Child = controllerGuideText,
        };
        controllerGuideLayer.Classes.Add("shell-controller-guide");
        AutomationProperties.SetAutomationId(controllerGuideLayer, "avp.integrated.controller-guide");

        modalTextBox = new TextBox { MinWidth = 320, MinHeight = 44 };
        var modalAccept = new Button { Content = "Apply", MinWidth = 110 };
        var modalCancel = new Button { Content = "Cancel", MinWidth = 110 };
        modalAccept.Click += (_, _) => CompleteTextEntry(modalTextBox.Text ?? string.Empty);
        modalCancel.Click += (_, _) => CompleteTextEntry(null);
        var modalButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        modalButtons.Children.Add(modalAccept);
        modalButtons.Children.Add(modalCancel);
        var modalContent = new StackPanel { Spacing = 14 };
        modalContent.Children.Add(new TextBlock { Text = "Enter value", FontSize = 20 });
        modalContent.Children.Add(modalTextBox);
        modalContent.Children.Add(modalButtons);
        modalLayer = new Border
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(16),
            Background = Brush.Parse("#FF202D3D"),
            BorderBrush = Brush.Parse("#FFA7C7F2"),
            BorderThickness = new Thickness(1),
            Child = modalContent,
        };
        AutomationProperties.SetAutomationId(modalLayer, "avp.integrated.modal");

        shellGrid = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("*,Auto,32,76"),
        };
        Grid.SetRow(pageHost, 0);
        shellGrid.Children.Add(pageHost);
        Grid.SetRow(statusLayer, 1);
        shellGrid.Children.Add(statusLayer);
        Grid.SetRow(controllerGuideLayer, 2);
        shellGrid.Children.Add(controllerGuideLayer);
        Grid.SetRow(trayLayer, 3);
        trayLayer.Margin = new Thickness(0);
        shellGrid.Children.Add(trayLayer);
        shellGrid.Children.Add(modalLayer);
        Grid.SetRowSpan(modalLayer, 4);
        Content = shellGrid;

        DataContext = coordinator.ViewModel;
        coordinator.ViewModel.PropertyChanged += OnViewModelChanged;
        coordinator.FramePublished += OnFramePublished;
        AddHandler(GotFocusEvent, OnDescendantGotFocus, RoutingStrategies.Bubble);
        SizeChanged += OnSizeChanged;
        ApplyShellProfile(shellProfile);
    }

    public WidgetIntegrationCoordinator Coordinator => coordinator;
    public event EventHandler<WidgetPresentationFrame>? FrameAdmitted;
    public PageTransitionPresenter TransitionPresenter => transitionPresenter;
    public Control? ActivePage => transitionPresenter.AdmittedPage;
    internal Control? ActiveSemanticRoot => admittedPresentation?.SemanticRoot;
    public IReadOnlyList<Button> TrayButtons => trayButtons.Values.ToArray();
    internal Border PageHostElement => pageHost;
    internal Border ControllerGuideElement => controllerGuideLayer;
    internal Border TrayElement => trayLayer;
    internal ScrollViewer TrayScrollElement => trayScroll;
    public int RealizedSemanticControls => admittedPresentation is null
        ? 0
        : renderer.GetRealizedControlCount(admittedPresentation.SemanticRoot);
    public int OwnedArtworkBitmapCount => renderer.OwnedArtworkBitmapCount;
    public long OwnedDecodedArtworkBytes => renderer.OwnedDecodedArtworkBytes;
    public int PendingArtworkRequestCount => renderer.PendingArtworkRequestCount;
    public int TrackedRenderCount => renderer.TrackedRenderCount;
    public bool IsCompact => contentMode == ContentResponsiveMode.Compact;
    public ContentResponsiveMode ContentMode => contentMode;
    public ShellProfile ShellProfile => shellProfile;
    public ShellNavigationRegion NavigationRegion => navigationRegion;
    public IReadOnlyList<IntegratedTransitionSample> TransitionSamples => transitionSamples.ToArray();
    public string? AdmittedWidgetId => admittedWidgetId;
    public WidgetPresentationAuthority? AdmittedAuthority => admittedPresentation?.Frame.Authority;
    public Task AdmissionIdle => admissionPump;
    internal string? RememberedFocus(string widgetId) => focusMemory.GetValueOrDefault(widgetId);
    internal bool FocusRestorationPending { get; private set; }
    internal string? LastFocusRestorationOutcome { get; private set; }
    internal string? LastRestoredSemanticId { get; private set; }
    internal string? LastRequestedFocus { get; private set; }

    internal void SetEvidenceViewport(Size viewport)
    {
        Width = viewport.Width;
        Height = viewport.Height;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        ApplyShellProfile(ResolveShellProfile(viewport));
        if (ActiveSemanticRoot is { } semanticRoot)
            SemanticTreeRenderer.PrepareSemanticRootForHost(semanticRoot);
        UpdateLayout();
        RevealSelectedTrayItem();
        ScheduleSelectedTrayReveal();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await coordinator.InitializeAsync(cancellationToken);
        RefreshTray();
    }

    public bool TryCycleTray(Control? focused, int delta)
    {
        if (navigationRegion != ShellNavigationRegion.Tray ||
            focused is null || !trayButtons.Values.Contains(focused)) return false;
        var buttons = trayButtons.Values.ToArray();
        var current = Array.IndexOf(buttons, focused);
        var next = (current + delta + buttons.Length) % buttons.Length;
        var target = buttons[next];
        navigationRegion = ShellNavigationRegion.Tray;
        target.Focus(NavigationMethod.Directional);
        if (target.Tag is string widgetId) _ = coordinator.SelectWidgetAsync(widgetId);
        return true;
    }

    public bool EnsureManagedFocus()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        if (focused is not null && IsManagedFocus(focused))
        {
            SynchronizeNavigationRegion(focused);
            return true;
        }
        navigationRegion = ShellNavigationRegion.Tray;
        return SelectedTrayButton()?.Focus(NavigationMethod.Directional) == true;
    }

    public bool TryEnterContent(Control? focused)
    {
        if (focused is null || !trayButtons.Values.Contains(focused) || ActivePage is null) return false;
        var widgetId = coordinator.ViewModel.SelectedWidgetId;
        Control? target = null;
        if (widgetId is not null && focusMemory.TryGetValue(widgetId, out var remembered))
        {
            target = ActivePage.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(control =>
                    string.Equals(control.GetValue(SemanticTreeRenderer.FocusPersistenceIdProperty), remembered,
                        StringComparison.Ordinal) && IsFocusable(control));
            if (target is null)
            {
                _ = RestoreRememberedFocusAsync(widgetId);
                return true;
            }
        }
        target ??= FindInitialFocus();
        if (target is null || !target.Focus(NavigationMethod.Directional)) return false;
        navigationRegion = ShellNavigationRegion.Content;
        target.BringIntoView();
        return true;
    }

    public bool RestoreTrayFocus(Control? focused)
    {
        if (focused is null || ActivePage is null || !IsWithin(focused, ActivePage)) return false;
        RememberFocus(focused);
        var restored = SelectedTrayButton()?.Focus(NavigationMethod.Directional) == true;
        if (restored) navigationRegion = ShellNavigationRegion.Tray;
        return restored;
    }

    public bool TryMoveSpatial(Control? focused, NavigationDirection direction)
    {
        if (focused is null || ActivePage is null || !IsWithin(focused, ActivePage)) return false;
        RememberFocus(focused);
        var zone = focused.GetVisualAncestors().OfType<Control>()
            .FirstOrDefault(control =>
                control.GetValue(ComponentProperties.NavigationZoneProperty) is
                    ControllerNavigationZone.Component or ControllerNavigationZone.Collection);
        var roots = zone is null || ReferenceEquals(zone, ActivePage)
            ? new[] { ActivePage }
            : new[] { zone, ActivePage };
        var moved = FocusNavigator.Move(focused, direction, roots);
        if (moved) navigationRegion = ShellNavigationRegion.Content;
        return moved;
    }

    public async Task<bool> RouteControllerButtonAsync(
        ControllerButton button,
        ControllerEventPhase phase,
        Control? focused)
    {
        SynchronizeNavigationRegion(focused);
        var trayFocused = focused is not null && trayButtons.Values.Contains(focused);
        if (phase == ControllerEventPhase.Released && button is not ControllerButton.Y)
            return trayFocused || await coordinator.SendControllerInputAsync(button, phase, SemanticId(focused));

        if (button == ControllerButton.A && phase == ControllerEventPhase.Pressed)
        {
            if (trayFocused) return TryEnterContent(focused);
            return await coordinator.SendControllerInputAsync(button, phase, SemanticId(focused));
        }
        if (button == ControllerButton.B && phase == ControllerEventPhase.Pressed)
        {
            if (focused is not null && ActivePage is not null && IsWithin(focused, ActivePage))
            {
                var handled = await coordinator.SendControllerInputAsync(button, phase, SemanticId(focused));
                return handled || RestoreTrayFocus(focused);
            }
            return false;
        }
        if (trayFocused && phase == ControllerEventPhase.Pressed)
            return await coordinator.InvokeQuickActionForButtonAsync(button);
        return await coordinator.SendControllerInputAsync(button, phase, SemanticId(focused));
    }

    public bool RouteKeyboard(SemanticInput input, Control? focused)
    {
        SynchronizeNavigationRegion(focused);
        if (navigationRegion == ShellNavigationRegion.Modal) return false;
        if (navigationRegion == ShellNavigationRegion.Tray)
        {
            if (input is SemanticInput.Left or SemanticInput.Right)
                return TryCycleTray(focused, input == SemanticInput.Left ? -1 : 1);
            if (input is SemanticInput.Up or SemanticInput.Activate)
                return TryEnterContent(focused);
            if (input == SemanticInput.Down) return true;
            return false;
        }
        if (focused is Slider slider && input is SemanticInput.Left or SemanticInput.Right)
        {
            slider.Value = Math.Clamp(
                slider.Value + (input == SemanticInput.Left ? -slider.SmallChange : slider.SmallChange),
                slider.Minimum,
                slider.Maximum);
            return true;
        }
        if (input is SemanticInput.Up or SemanticInput.Down or SemanticInput.Left or SemanticInput.Right)
        {
            var moved = TryMoveSpatial(focused, input switch
            {
                SemanticInput.Up => NavigationDirection.Up,
                SemanticInput.Down => NavigationDirection.Down,
                SemanticInput.Left => NavigationDirection.Left,
                _ => NavigationDirection.Right,
            });
            if (!moved && input == SemanticInput.Down) return RestoreTrayFocus(focused);
            return moved || focused is not null && ActivePage is not null && IsWithin(focused, ActivePage);
        }
        if (input == SemanticInput.Activate && focused is Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return true;
        }
        if (input == SemanticInput.Back) return RestoreTrayFocus(focused);
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        coordinator.ViewModel.PropertyChanged -= OnViewModelChanged;
        coordinator.FramePublished -= OnFramePublished;
        lock (admissionGate)
        {
            pendingFrame = null;
            activeAdmission?.Cancel();
        }
        try { await admissionPump.ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        modalCompletion?.TrySetResult(null);
        transitionPresenter.Dispose();
        if (admittedPresentation is not null) renderer.Release(admittedPresentation.SemanticRoot);
        admittedPresentation = null;
        renderer.Dispose();
        activeAdmission?.Dispose();
        activeAdmission = null;
        await coordinator.DisposeAsync();
    }

    private void RefreshTray()
    {
        trayPanel.Children.Clear();
        trayButtons.Clear();
        foreach (var widget in coordinator.ViewModel.Widgets)
        {
            var glyph = new TextBlock
            {
                Text = Glyph(widget.Icon),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            var label = new TextBlock
            {
                Text = widget.Name,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            content.Children.Add(glyph);
            content.Children.Add(label);
            var button = new Button
            {
                Content = content,
                Tag = widget.Id,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            ControllerComponentCompiler.Apply(button,
                new ControllerComponent(ControllerComponentKind.Tile,
                    ControllerNavigationZone.Component, "ControllerTrayButtonTheme"));
            ApplyTrayButtonSizing(button, shellProfile);
            button.Classes.Add("tray-button");
            AutomationProperties.SetAutomationId(button, $"tray.{Encode(widget.Id)}");
            AutomationProperties.SetName(button, $"Open {widget.Name}");
            button.Click += (_, _) => _ = coordinator.SelectWidgetAsync(widget.Id);
            trayPanel.Children.Add(button);
            trayButtons.Add(widget.Id, button);
        }
        SelectedTrayButton()?.Focus(NavigationMethod.Directional);
        UpdateTraySelection();
    }

    private void OnFramePublished(object? sender, WidgetPresentationFrame frame)
    {
        if (disposed) return;
        lock (admissionGate)
        {
            pendingFrame = frame;
            if (activeAdmission is not null &&
                !string.Equals(transitioningWidgetId, frame.Authority.WidgetId, StringComparison.Ordinal))
                activeAdmission.Cancel();
            if (admissionPumpRunning) return;
            admissionPumpRunning = true;
            admissionPump = ProcessAdmissionsAsync();
        }
    }

    private async Task ProcessAdmissionsAsync()
    {
        try
        {
            while (!disposed)
            {
                WidgetPresentationFrame? frame;
                lock (admissionGate)
                {
                    frame = pendingFrame;
                    pendingFrame = null;
                    if (frame is null)
                    {
                        admissionPumpRunning = false;
                        return;
                    }
                }
                await AdmitFrameAsync(frame);
            }
        }
        finally
        {
            lock (admissionGate) admissionPumpRunning = false;
        }
    }

    private async Task AdmitFrameAsync(WidgetPresentationFrame frame)
    {
        if (disposed) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var restoreContentFocus = focused is not null && ActivePage is not null && IsWithin(focused, ActivePage);
        RememberCurrentFocus();
        var rememberedBeforeReplacement = focusMemory.GetValueOrDefault(frame.Authority.WidgetId);
        if (!restoreContentFocus && frame.Authority.WidgetId == renderedFrame?.Authority.WidgetId &&
            rememberedBeforeReplacement is not null)
            restoreContentFocus = true;
        suppressFocusMemory = restoreContentFocus;
        contentMode = ResolveContentMode(frame);
        var candidate = RenderPage(frame);
        var sameWidget = string.Equals(
            admittedPresentation?.Frame.Authority.WidgetId, frame.Authority.WidgetId, StringComparison.Ordinal);
        if (sameWidget)
        {
            transitionPresenter.ReplaceWithoutTransition(candidate.Page);
        }
        else
        {
            using var admission = new CancellationTokenSource();
            lock (admissionGate)
            {
                activeAdmission = admission;
                transitioningWidgetId = frame.Authority.WidgetId;
            }
            var outcome = await transitionPresenter.PresentAsync(
                candidate.Page,
                sample: phase => RecordTransition(frame, phase),
                cancellationToken: admission.Token);
            lock (admissionGate)
            {
                if (ReferenceEquals(activeAdmission, admission)) activeAdmission = null;
                if (string.Equals(transitioningWidgetId, frame.Authority.WidgetId, StringComparison.Ordinal))
                    transitioningWidgetId = null;
            }
            if (outcome != PresentationOutcome.Admitted ||
                !transitionPresenter.IsExactlyAdmitted(candidate.Page))
            {
                renderer.Release(candidate.SemanticRoot);
                suppressFocusMemory = false;
                return;
            }

            // Same-widget refreshes that arrived during the page transition are coalesced and
            // installed without exposing the older authority as the admitted presentation.
            WidgetPresentationFrame? replacement;
            lock (admissionGate)
            {
                replacement = pendingFrame is not null && string.Equals(
                    pendingFrame.Authority.WidgetId, frame.Authority.WidgetId, StringComparison.Ordinal)
                    ? pendingFrame
                    : null;
                if (replacement is not null) pendingFrame = null;
            }
            if (replacement is not null)
            {
                contentMode = ResolveContentMode(replacement);
                var latestCandidate = RenderPage(replacement);
                transitionPresenter.ReplaceWithoutTransition(latestCandidate.Page);
                renderer.Release(candidate.SemanticRoot);
                candidate = latestCandidate;
                frame = replacement;
            }
        }

        if (!transitionPresenter.IsExactlyAdmitted(candidate.Page) ||
            !Equals(coordinator.CurrentFrame?.Authority, frame.Authority))
        {
            renderer.Release(candidate.SemanticRoot);
            suppressFocusMemory = false;
            return;
        }

        var superseded = admittedPresentation;
        admittedPresentation = candidate;
        renderedFrame = frame;
        admittedWidgetId = frame.Authority.WidgetId;
        if (superseded is not null && !ReferenceEquals(superseded.SemanticRoot, candidate.SemanticRoot))
            renderer.Release(superseded.SemanticRoot);
        if (restoreContentFocus)
        {
            FocusRestorationPending = true;
            LastRequestedFocus = rememberedBeforeReplacement;
            LastFocusRestorationOutcome = null;
            var restored = await RestoreRememberedFocusAsync(frame.Authority.WidgetId, rememberedBeforeReplacement);
            LastRestoredSemanticId = (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control)?
                .GetValue(SemanticTreeRenderer.SemanticNodeIdProperty);
            LastFocusRestorationOutcome = restored ? "restored" : "fallback";
            FocusRestorationPending = false;
        }
        suppressFocusMemory = false;
        FrameAdmitted?.Invoke(this, frame);
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        statusLayer.IsVisible = coordinator.ViewModel.IsBusy || coordinator.ViewModel.HasFailure;
        if (args.PropertyName == nameof(IntegratedShellViewModel.SelectedWidgetId))
            UpdateTraySelection();
    }

    private void RecordTransition(WidgetPresentationFrame frame, TransitionPhase phase)
    {
        var background = pageHost.Background as ISolidColorBrush;
        transitionSamples.Add(new IntegratedTransitionSample(
            frame.Authority.WidgetId,
            frame.Authority.SnapshotSequence,
            phase,
            Background is null || Background is ISolidColorBrush { Color.A: 0 },
            background is null || background.Color is not { A: 255, R: 0, G: 0, B: 0 },
            transitionPresenter.Bounds.Width > 0 && transitionPresenter.Bounds.Height > 0,
            transitionPresenter.GetVisualDescendants().Count()));
    }

    private void UpdateTraySelection()
    {
        foreach (var (id, button) in trayButtons)
            button.Classes.Set("selected", string.Equals(id, coordinator.ViewModel.SelectedWidgetId, StringComparison.Ordinal));
        ScheduleSelectedTrayReveal();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        ApplyShellProfile(ResolveShellProfile(args.NewSize));
        if (renderedFrame is null || admittedPresentation is null || activeAdmission is not null) return;
        var previousMode = contentMode;
        contentMode = ResolveContentMode(renderedFrame, args.NewSize);
        if (previousMode == contentMode) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var focusedInPage = focused is not null && ActivePage is not null && IsWithin(focused, ActivePage);
        if (focusedInPage) RememberFocus(focused, previousMode == ContentResponsiveMode.Compact);
        renderer.SetCompact(admittedPresentation.SemanticRoot, IsCompact);
        admittedPresentation.Page.UpdateLayout();
        if (focusedInPage && !IsFocusable(focused!)) RestoreResponsiveFocus(IsCompact);
    }

    private void ApplyShellProfile(ShellProfile profile)
    {
        shellProfile = profile;
        var compact = profile == ShellProfile.Compact;
        shellGrid.Margin = compact ? new Thickness(10) : new Thickness(20);
        pageHost.CornerRadius = compact ? new CornerRadius(14) : new CornerRadius(20);
        trayPanel.Spacing = compact ? 6 : 10;
        trayPanel.Margin = compact ? new Thickness(7, 0) : new Thickness(12, 0);
        trayLayer.Padding = compact ? new Thickness(4) : new Thickness(6);
        controllerGuideLayer.Padding = compact ? new Thickness(8, 2) : new Thickness(12, 3);
        controllerGuideText.FontSize = compact ? 10.5 : 12;
        shellGrid.RowDefinitions[2].Height = new GridLength(compact ? 26 : 32);
        shellGrid.RowDefinitions[3].Height = new GridLength(compact ? 62 : 76);
        foreach (var button in trayButtons.Values) ApplyTrayButtonSizing(button, profile);
        ScheduleSelectedTrayReveal();
    }

    private void ScheduleSelectedTrayReveal()
    {
        var generation = ++trayRevealGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (disposed || generation != trayRevealGeneration) return;
            RevealSelectedTrayItem();
            Dispatcher.UIThread.Post(() =>
            {
                if (disposed || generation != trayRevealGeneration) return;
                RevealSelectedTrayItem();
            }, DispatcherPriority.Render);
        }, DispatcherPriority.Loaded);
    }

    private void RevealSelectedTrayItem()
    {
        trayScroll.UpdateLayout();
        SelectedTrayButton()?.BringIntoView();
    }

    private static void ApplyTrayButtonSizing(Button button, ShellProfile profile)
    {
        var compact = profile == ShellProfile.Compact;
        button.MinWidth = compact ? 104 : 118;
        button.MaxWidth = compact ? 168 : 190;
        button.Height = compact ? 48 : 56;
        button.Padding = compact ? new Thickness(10, 6) : new Thickness(13, 8);
        button.FontSize = compact ? 12 : 13;
    }

    private void OnDescendantGotFocus(object? sender, FocusChangedEventArgs args)
    {
        SynchronizeNavigationRegion(args.Source as Control);
        if (!suppressFocusMemory) RememberFocus(args.Source as Control);
    }

    private void RememberCurrentFocus() => RememberFocus(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control);

    private void RememberFocus(Control? control, bool? compact = null)
    {
        if (control is null || ActivePage is null || !IsWithin(control, ActivePage)) return;
        var widgetId = coordinator.ViewModel.SelectedWidgetId;
        var persistenceId = control.GetValue(SemanticTreeRenderer.FocusPersistenceIdProperty);
        if (!string.IsNullOrWhiteSpace(widgetId) && !string.IsNullOrWhiteSpace(persistenceId))
        {
            focusMemory[widgetId] = persistenceId;
            responsiveFocusMemory[(widgetId, compact ?? IsCompact)] = persistenceId;
        }
    }

    private void RestoreResponsiveFocus(bool compact)
    {
        if (ActivePage is null || coordinator.ViewModel.SelectedWidgetId is not { } widgetId) return;
        Control? target = null;
        if (responsiveFocusMemory.TryGetValue((widgetId, compact), out var remembered))
        {
            target = ActivePage.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(control =>
                    string.Equals(control.GetValue(SemanticTreeRenderer.FocusPersistenceIdProperty), remembered,
                        StringComparison.Ordinal) && IsFocusable(control));
        }
        target ??= FindInitialFocus();
        suppressFocusMemory = true;
        try
        {
            if (target?.Focus(NavigationMethod.Directional) == true) return;
            SelectedTrayButton()?.Focus(NavigationMethod.Directional);
        }
        finally { suppressFocusMemory = false; }
    }

    private Control? FindInitialFocus()
    {
        if (ActivePage is null) return null;
        var initialId = renderedFrame?.Snapshot.InitialFocusId;
        if (!string.IsNullOrWhiteSpace(initialId))
        {
            var declared = ActivePage.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(control =>
                    string.Equals(control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty), initialId,
                        StringComparison.Ordinal) && IsFocusable(control));
            if (declared is not null) return declared;
        }
        return ActivePage.GetVisualDescendants().OfType<Control>().FirstOrDefault(IsFocusable);
    }

    private async Task<bool> RestoreRememberedFocusAsync(string widgetId, string? requestedFocus = null)
    {
        if (ActivePage is null) return false;
        var remembered = requestedFocus ?? focusMemory.GetValueOrDefault(widgetId);
        if (remembered is null) return false;
        var target = ActivePage.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(control =>
                string.Equals(control.GetValue(SemanticTreeRenderer.FocusPersistenceIdProperty), remembered,
                    StringComparison.Ordinal) && IsFocusable(control));
        if (target is not null) return target.Focus(NavigationMethod.Directional);

        var node = renderedFrame is null ? null : Flatten(renderedFrame.Snapshot.Root)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.FocusPersistenceId ?? candidate.Id, remembered, StringComparison.Ordinal));
        if (node is null) return FindInitialFocus()?.Focus(NavigationMethod.Directional) == true;
        var list = ActivePage.GetVisualDescendants().OfType<ListBox>()
            .Prepend(ActivePage as ListBox)
            .OfType<ListBox>()
            .FirstOrDefault(candidate => candidate.ItemsSource?.Cast<object>().Contains(node) == true);
        if (list is null) return FindInitialFocus()?.Focus(NavigationMethod.Directional) == true;
        var itemIndex = list.ItemsSource!.Cast<object>().ToList().IndexOf(node);
        list.SelectedIndex = itemIndex;
        list.UpdateLayout();
        if (list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroller)
        {
            var realizedHeight = list.ContainerFromIndex(0)?.Bounds.Height ?? 44;
            var estimatedOffset = Math.Clamp(itemIndex * Math.Max(1, realizedHeight), 0, scroller.Extent.Height);
            scroller.Offset = new Vector(scroller.Offset.X, estimatedOffset);
        }
        for (var attempt = 0; attempt < 12; attempt++)
        {
            list.ScrollIntoView(node);
            list.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (list.ContainerFromIndex(itemIndex) is Control container)
            {
                target = container.GetVisualDescendants().OfType<Control>()
                    .Prepend(container)
                    .FirstOrDefault(control =>
                        string.Equals(control.GetValue(SemanticTreeRenderer.FocusPersistenceIdProperty), remembered,
                            StringComparison.Ordinal) && IsFocusable(control));
                if (target?.Focus(NavigationMethod.Directional) == true) return true;
            }
            target = ActivePage.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(control =>
                    string.Equals(control.GetValue(SemanticTreeRenderer.FocusPersistenceIdProperty), remembered,
                        StringComparison.Ordinal) && IsFocusable(control));
            if (target?.Focus(NavigationMethod.Directional) == true) return true;
            await Task.Delay(10);
        }
        return FindInitialFocus()?.Focus(NavigationMethod.Directional) == true;
    }

    private Button? SelectedTrayButton() => coordinator.ViewModel.SelectedWidgetId is { } id &&
        trayButtons.TryGetValue(id, out var button) ? button : null;

    private async Task<ReadOnlyMemory<byte>> ResolveArtworkAsync(
        WidgetPresentationAuthority authority,
        string handle,
        CancellationToken cancellationToken)
    {
        return await coordinator.ResolveArtworkAsync(authority, handle, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<string?> RequestTextAsync(ViewNode node)
    {
        modalCompletion?.TrySetResult(null);
        modalCompletion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        modalTextBox.Text = node.TextEntryValue ?? string.Empty;
        modalTextBox.PlaceholderText = node.TextEntryPlaceholder;
        modalTextBox.MaxLength = node.TextEntryMaximumLength ?? 2048;
        modalLayer.IsVisible = true;
        navigationRegion = ShellNavigationRegion.Modal;
        modalTextBox.Focus();
        return modalCompletion.Task;
    }

    private void CompleteTextEntry(string? text)
    {
        modalLayer.IsVisible = false;
        modalCompletion?.TrySetResult(text);
        modalCompletion = null;
        navigationRegion = ShellNavigationRegion.Content;
        RestoreResponsiveFocus(IsCompact);
    }

    private static string? SemanticId(Control? control) =>
        control?.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty);
    private static bool IsWithin(Control child, Control root) =>
        ReferenceEquals(child, root) || child.GetVisualAncestors().Contains(root);
    private static bool IsFocusable(Control control) =>
        control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled;
    private static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Glyph(WidgetGlyph glyph) => glyph switch
    {
        WidgetGlyph.Settings => "⚙", WidgetGlyph.Music => "♪", WidgetGlyph.Volume => "◕",
        WidgetGlyph.Wifi => "⌁", WidgetGlyph.Ethernet => "↔", WidgetGlyph.Connection => "●",
        _ => "◆",
    };

    private RenderedPage RenderPage(WidgetPresentationFrame frame)
    {
        var semanticRoot = renderer.Render(frame, IsCompact);
        var ownsVerticalScroll = renderer.OwnsVerticalScroll(semanticRoot);
        Control body = ownsVerticalScroll
            ? semanticRoot
            : new ScrollViewer
            {
                Content = semanticRoot,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                AllowAutoHide = true,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
        var eyebrow = new TextBlock
        {
            Text = "CURRENT WIDGET",
            IsHitTestVisible = false,
        };
        eyebrow.Classes.Add("shell-eyebrow");
        var title = new TextBlock
        {
            Text = frame.Descriptor.Name,
            IsHitTestVisible = false,
        };
        title.Classes.Add("shell-widget-title");
        var headerText = new StackPanel { Spacing = 1 };
        headerText.Children.Add(eyebrow);
        headerText.Children.Add(title);
        var header = new Border
        {
            Padding = new Thickness(16, 10),
            Background = Brush.Parse("#E51D2A3A"),
            BorderBrush = Brush.Parse("#4F8FB2DB"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = headerText,
        };
        ControllerComponentCompiler.Apply(header,
            new ControllerComponent(ControllerComponentKind.PageHeader,
                ControllerNavigationZone.None));

        var bodyHost = new Border
        {
            Padding = new Thickness(10, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = body,
        };
        var page = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetRow(header, 0);
        page.Children.Add(header);
        Grid.SetRow(bodyHost, 1);
        page.Children.Add(bodyHost);
        ControllerComponentCompiler.Apply(page,
            new ControllerComponent(ControllerComponentKind.Page,
                ControllerNavigationZone.Page));
        return new RenderedPage(frame, page, semanticRoot);
    }

    private ContentResponsiveMode ResolveContentMode(WidgetPresentationFrame frame, Size? shellSize = null)
    {
        var size = shellSize ?? Bounds.Size;
        var margin = shellProfile == ShellProfile.Compact ? 10 : 20;
        var chrome = shellGrid.RowDefinitions[2].Height.Value + shellGrid.RowDefinitions[3].Height.Value;
        var viewport = new Size(
            Math.Max(0, size.Width - margin * 2),
            Math.Max(0, size.Height - margin * 2 - chrome));
        return ShellGeometryPolicy.ResolveContentMode(frame.Snapshot.Surface, viewport);
    }

    private static ShellProfile ResolveShellProfile(Size size) =>
        size.Width < 1050 || size.Height < 620
            ? ShellProfile.Compact
            : size.Width >= 1500 && size.Height >= 820
                ? ShellProfile.Wide
                : ShellProfile.Standard;

    private bool IsManagedFocus(Control focused) =>
        trayButtons.Values.Contains(focused) ||
        ActivePage is not null && IsWithin(focused, ActivePage) ||
        IsWithin(focused, modalLayer);

    private void SynchronizeNavigationRegion(Control? focused)
    {
        if (focused is null) return;
        navigationRegion = IsWithin(focused, modalLayer)
            ? ShellNavigationRegion.Modal
            : trayButtons.Values.Contains(focused)
                ? ShellNavigationRegion.Tray
                : ActivePage is not null && IsWithin(focused, ActivePage)
                    ? ShellNavigationRegion.Content
                    : navigationRegion;
    }

    private sealed record RenderedPage(
        WidgetPresentationFrame Frame,
        Control Page,
        Control SemanticRoot);

    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child)) yield return descendant;
    }
}
