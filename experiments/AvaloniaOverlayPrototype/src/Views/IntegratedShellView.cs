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

public sealed class IntegratedShellView : UserControl, IAsyncDisposable
{
    private readonly WidgetIntegrationCoordinator coordinator;
    private readonly SemanticTreeRenderer renderer;
    private readonly PageTransitionPresenter transitionPresenter = new();
    private readonly Border pageHost;
    private readonly StackPanel trayPanel;
    private readonly Border statusLayer;
    private readonly Border controllerGuideLayer;
    private readonly Border modalLayer;
    private readonly TextBox modalTextBox;
    private readonly Dictionary<string, Button> trayButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> focusMemory = new(StringComparer.Ordinal);
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
    private string? admittedWidgetId;

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
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(18),
            Background = Brush.Parse("#EB172230"),
            BorderBrush = Brush.Parse("#7092B7E8"),
            BorderThickness = new Thickness(1),
            Child = transitionPresenter,
        };
        AutomationProperties.SetAutomationId(pageHost, "avp.integrated.content");
        AutomationProperties.SetName(pageHost, "Current widget content");

        trayPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var trayScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = trayPanel,
        };
        var tray = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 1100,
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(18),
            Background = Brush.Parse("#F21A2432"),
            BorderBrush = Brush.Parse("#7898BCE9"),
            BorderThickness = new Thickness(1),
            Child = trayScroll,
        };
        AutomationProperties.SetAutomationId(tray, "avp.integrated.tray");
        AutomationProperties.SetName(tray, "Stationary widget tray");

        var statusView = new IntegratedStatusView { DataContext = coordinator.ViewModel };
        statusLayer = new Border
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12),
            Padding = new Thickness(12, 7),
            CornerRadius = new CornerRadius(8),
            Background = Brush.Parse("#F02B3D55"),
            Child = statusView,
        };
        AutomationProperties.SetAutomationId(statusLayer, "avp.integrated.status");

        controllerGuideLayer = new Border
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 94),
            Padding = new Thickness(14, 8),
            CornerRadius = new CornerRadius(10),
            Background = Brush.Parse("#EA111923"),
            Child = new TextBlock
            {
                Text = "D-pad / stick Navigate   A Open / activate   B Back   Hold Y Restart",
                TextWrapping = TextWrapping.Wrap,
            },
        };
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

        var shellGrid = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("*,Auto") };
        shellGrid.Children.Add(pageHost);
        Grid.SetRow(tray, 1);
        tray.Margin = new Thickness(0, 14, 0, 0);
        shellGrid.Children.Add(tray);
        shellGrid.Children.Add(statusLayer);
        shellGrid.Children.Add(controllerGuideLayer);
        shellGrid.Children.Add(modalLayer);
        Grid.SetRowSpan(statusLayer, 2);
        Grid.SetRowSpan(controllerGuideLayer, 2);
        Grid.SetRowSpan(modalLayer, 2);
        Content = shellGrid;

        DataContext = coordinator.ViewModel;
        coordinator.ViewModel.PropertyChanged += OnViewModelChanged;
        coordinator.FramePublished += OnFramePublished;
        AddHandler(GotFocusEvent, OnDescendantGotFocus, RoutingStrategies.Bubble);
        SizeChanged += OnSizeChanged;
    }

    public WidgetIntegrationCoordinator Coordinator => coordinator;
    public event EventHandler<WidgetPresentationFrame>? FrameAdmitted;
    public PageTransitionPresenter TransitionPresenter => transitionPresenter;
    public Control? ActivePage => transitionPresenter.AdmittedPage;
    public IReadOnlyList<Button> TrayButtons => trayButtons.Values.ToArray();
    public int RealizedSemanticControls => admittedPresentation is null
        ? 0
        : renderer.GetRealizedControlCount(admittedPresentation.SemanticRoot);
    public int OwnedArtworkBitmapCount => renderer.OwnedArtworkBitmapCount;
    public long OwnedDecodedArtworkBytes => renderer.OwnedDecodedArtworkBytes;
    public int PendingArtworkRequestCount => renderer.PendingArtworkRequestCount;
    public int TrackedRenderCount => renderer.TrackedRenderCount;
    public bool IsCompact => Bounds.Width <= 700 || Bounds.Height <= 430;
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
        UpdateLayout();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await coordinator.InitializeAsync(cancellationToken);
        RefreshTray();
    }

    public bool TryCycleTray(Control? focused, int delta)
    {
        if (focused is null || !trayButtons.Values.Contains(focused)) return false;
        var buttons = trayButtons.Values.ToArray();
        var current = Array.IndexOf(buttons, focused);
        var next = (current + delta + buttons.Length) % buttons.Length;
        var target = buttons[next];
        target.Focus(NavigationMethod.Directional);
        if (target.Tag is string widgetId) _ = coordinator.SelectWidgetAsync(widgetId);
        return true;
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
        return target is not null && target.Focus(NavigationMethod.Directional);
    }

    public bool RestoreTrayFocus(Control? focused)
    {
        if (focused is null || ActivePage is null || !IsWithin(focused, ActivePage)) return false;
        RememberFocus(focused);
        return SelectedTrayButton()?.Focus(NavigationMethod.Directional) == true;
    }

    public bool TryMoveSpatial(Control? focused, NavigationDirection direction)
    {
        if (focused is null || ActivePage is null || !IsWithin(focused, ActivePage)) return false;
        RememberFocus(focused);
        return FocusNavigator.Move(focused, direction, [ActivePage]);
    }

    public async Task<bool> RouteControllerButtonAsync(
        ControllerButton button,
        ControllerEventPhase phase,
        Control? focused)
    {
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
        if (input is SemanticInput.Left or SemanticInput.Right &&
            TryCycleTray(focused, input == SemanticInput.Left ? -1 : 1)) return true;
        if (input is SemanticInput.Down or SemanticInput.Activate && TryEnterContent(focused)) return true;
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
            return TryMoveSpatial(focused, input switch
            {
                SemanticInput.Up => NavigationDirection.Up,
                SemanticInput.Down => NavigationDirection.Down,
                SemanticInput.Left => NavigationDirection.Left,
                _ => NavigationDirection.Right,
            });
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
            var button = new Button
            {
                Content = $"{Glyph(widget.Icon)}  {widget.Name}",
                Tag = widget.Id,
                MinWidth = 128,
                Height = 62,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
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
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (renderedFrame is null || admittedPresentation is null || activeAdmission is not null) return;
        var wasCompact = args.PreviousSize.Width <= 700 || args.PreviousSize.Height <= 430;
        if (wasCompact == IsCompact) return;
        RememberCurrentFocus();
        renderer.SetCompact(admittedPresentation.SemanticRoot, IsCompact);
        admittedPresentation.Page.UpdateLayout();
    }

    private void OnDescendantGotFocus(object? sender, FocusChangedEventArgs args)
    {
        if (!suppressFocusMemory) RememberFocus(args.Source as Control);
    }

    private void RememberCurrentFocus() => RememberFocus(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control);

    private void RememberFocus(Control? control)
    {
        if (control is null || ActivePage is null || !IsWithin(control, ActivePage)) return;
        var widgetId = coordinator.ViewModel.SelectedWidgetId;
        var persistenceId = control.GetValue(SemanticTreeRenderer.FocusPersistenceIdProperty);
        if (!string.IsNullOrWhiteSpace(widgetId) && !string.IsNullOrWhiteSpace(persistenceId))
            focusMemory[widgetId] = persistenceId;
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
        modalTextBox.Focus();
        return modalCompletion.Task;
    }

    private void CompleteTextEntry(string? text)
    {
        modalLayer.IsVisible = false;
        modalCompletion?.TrySetResult(text);
        modalCompletion = null;
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
        Control page = frame.Snapshot.Root.Kind == ViewNodeKind.Scroll
            ? semanticRoot
            : new ScrollViewer
            {
                Content = semanticRoot,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
        return new RenderedPage(frame, page, semanticRoot);
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
