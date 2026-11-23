using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView : ContentControl, IAsyncDisposable
{
    private readonly PresentationSession session;
    private readonly Action<Exception> failed;
    private readonly Dictionary<SelectorItem, (IndexedItem<WidgetIndexedRow> Slot, EventHandler Changed, WidgetIndexedRowView? LayoutOwner)> containers = [];
    private readonly HashSet<Task> retiring = [];
    private WidgetIndexedRows? source;
    private IndexedItemsSource<WidgetIndexedRow>.Retention? measurementRetention;
    private ListViewBase? view;
    private CollectionLayoutKind? layoutKind;
    private ScrollAxis? axis;
    private bool disposed;
    private bool inputActive;
    private bool presentationActive = true;
    private PlatformSettings.AppearanceSettings rowAppearance = PlatformSettings.AppearanceSettings.Default;
    private bool rowAnimationsEnabled = true;
    internal void SetRowAppearance(PlatformSettings.AppearanceSettings value, bool animationsEnabled)
    {
        if (rowAppearance == value && rowAnimationsEnabled == animationsEnabled) return;
        rowAppearance = value; rowAnimationsEnabled = animationsEnabled;
        foreach (var item in containers.Values) item.LayoutOwner?.ApplyAppearance(value, animationsEnabled);
    }
    internal void ApplyRowAppearance(WidgetViewPresenter presenter) => presenter.ApplyInheritedAppearance(rowAppearance, rowAnimationsEnabled);
    private bool CanReceiveInput => presentationActive && inputActive && source?.Presentation.IsCurrent == true && view?.IsEnabled == true;
    internal async Task SetPresentationActiveAsync(bool active)
    {
        presentationActive = active;
        if (!active) CancelPreparedWindow();
        if (!active) CancelViewportRecovery(false);
        QueueViewportObservation(!active);
        if (!active) { readingBackPressed=false; CancelHeldSelection(); CancelContinuity(); CancelNavigation(); CancelPendingActivation(); }
        if (view is not null)
        {
            view.IsHitTestVisible = active && inputActive;
            view.IsTabStop = active && inputActive;
            view.IsItemClickEnabled = active && inputActive && !SelectionActive;
            foreach (var (container, item) in containers) container.IsTabStop = active && inputActive && item.Slot.Value is not null;
        }
        var drains = new List<Task>();
        if (!active && windowPreparationTask is { } preparation) drains.Add(preparation);
        if (source is not null) drains.Add(source.SetPresentationActiveAsync(active));
        if (view is not null) Visit(view);
        await Task.WhenAll(drains);
        void Visit(DependencyObject node)
        {
            if (node is WidgetIndexedRowView row) { drains.Add(row.SetPresentationActiveAsync(active)); return; }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); ++i) Visit(VisualTreeHelper.GetChild(node, i));
        }
    }
    private long sequence;
    private long discoveryForegroundToken;
    private long paddingToken;
    internal ListViewBase NativeView => view ?? throw new InvalidOperationException("Collection is not initialized.");
    internal Action? PresentationChanged { get; set; }
    internal Func<DependencyObject, bool>? PrimaryPreviewRequested { get; set; }
    internal Action<bool>? ScrollOwnerChanging { get; set; }
    internal Func<WidgetIndexedCollectionView, bool>? HasPendingViewportScroll { get; set; }
    internal Func<WidgetIndexedCollectionView, bool>? BoundaryObservationReady { get; set; }
    internal Func<WidgetIndexedCollectionView, bool>? HasReadingPreview { get; set; }
    internal void RefreshAppearanceBinding(WidgetPresentationBinding binding)
    {
        if (source is not null) source.Update(binding, source.Declaration);
        RefreshGroupHeaderStyles();
    }
    internal Func<WidgetPresentationAuthority, CancellationToken, Task<bool>>? EnsureInteractionAsync { get; set; }
    internal Func<WidgetActionRequest, Task>? DispatchIntentAsync { get; set; }
    internal bool Owns(WidgetIndexedRows owner) => ReferenceEquals(owner, source);
    internal WidgetIndexedRow? FocusedRow() => source is not null && FocusedIndex() is { } index
        ? ((IndexedItem<WidgetIndexedRow>)source.Items[index]).Value : null;
    internal IndexedItemsSource<WidgetIndexedRow>.Retention? RetainFocusedRow() => source is not null && FocusedIndex() is { } index ? source.Items.Retain(index) : null;

    internal WidgetIndexedCollectionView(PresentationSession session, Action<Exception> failed)
    {
        this.session = session; this.failed = failed;
        IsTabStop = false;
        // This owner is only a temporary focus parking point during query reset.
        // User-facing focus belongs to the realized row, never this whole region.
        UseSystemFocusVisuals = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        SizeChanged += (_, _) => UpdateGridWidth();
        GettingFocus += (_, args) =>
        {
            if (!ReadingActionsEnabled || args.FocusState == FocusState.Pointer ||
                args.NewFocusedElement is not SelectorItem container || !containers.TryGetValue(container, out var item) ||
                item.Slot.Value is not { Lease.IsCurrent: true, Item.Root.IsReadingGroup: true } ||
                item.LayoutOwner?.ReadingEntryControl is not { } entry) return;
            for (var old = args.OldFocusedElement; old is not null; old = PresentationLayers.LogicalParent(old))
                if (ReferenceEquals(old, container)) return; // Back can still deliberately return to the message itself.
            if (view is not null && FindNativeScroll(view) is { } viewport && container.ActualHeight > viewport.ViewportHeight &&
                !WidgetReadingScrollFocus.IsVisible(entry, viewport)) return;
            args.TrySetNewFocusedElement(entry);
        };
        GotFocus += (_, _) => { ValidatePendingActivation(); RememberItemFocus(); CompleteSelectionNavigation(); RefreshReaderOwnership(); };
        LostFocus += (_, _) => { ValidatePendingActivation(); RefreshReaderOwnership(); };
        // Observe departures from both realized rows and the temporary parking
        // owner so an explicit choice outside can cancel a pending handoff.
        LosingFocus += OnLosingFocus;
        Unloaded += (_, _) => { if (!IsLoaded) {CancelHeldSelection();CancelPendingActivation();} };
        Loaded += (_, _) => AttachViewportObservation();
        LayoutUpdated += (_, _) => { if(source?.Declaration.IndexedCollection?.ObservesBoundaries==true)QueueViewportObservation(); };
        Unloaded += (_, _) => { if (!IsLoaded) QueueViewportObservation(true); };
        GotFocus += (_, _) => PresentationChanged?.Invoke();
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => { CancelHeldSelection(); extendAfterNavigation = null; CancelInputContinuity(true); CancelNavigation(); }), true);
        AddHandler(PointerWheelChangedEvent, new PointerEventHandler((_, args) =>
        {
            if(preferredEntryLookup is not null || pendingIndex is not null && AnchorDirectionalMove)CancelNavigation();
            CancelInputContinuity(true);
            var properties=args.GetCurrentPoint(this).Properties;
            ObserveBoundaryScrollInput(properties.IsHorizontalMouseWheel?-properties.MouseWheelDelta:0,
                properties.IsHorizontalMouseWheel?0:-properties.MouseWheelDelta);
        }), true);
        AddHandler(TappedEvent, new TappedEventHandler(SelectionTapped), true);
        AddHandler(KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            // The presenter already handled directional keys during tunneling;
            // their queued row movement must survive the bubbling event.
            if (args.Handled || Input.GamepadKeyBoundary.Owns(this, args)) return;
            if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Left or
                Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Tab or Windows.System.VirtualKey.Home or
                Windows.System.VirtualKey.End or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown)
                { CancelInputContinuity(true); CancelNavigation(); }
        }), true);
    }

    internal void Apply(WidgetPresentationFrame frame, ViewNode declaration, string scope) => Apply(WidgetPresentationBinding.ForMain(frame), declaration, scope);
    internal void Apply(WidgetPresentationBinding binding, ViewNode declaration, string scope, bool revealTarget = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (revealTarget)
        {
            // An explicit destination in this publication owns the viewport.
            // Recovering an evicted reader here would replace the requested page.
            CancelViewportRecovery(false);
            CancelPreparedWindow();
        }
        else
        {
            if (TryHoldViewportRecovery(binding, declaration, scope)) { CancelPreparedWindow(); return; }
            if (TryPrepareWindow(binding,declaration,scope)) return;
        }
        var entry = CaptureEntry();
        var previousSource = source;
        var layout = declaration.CollectionLayout ?? throw new InvalidDataException("Indexed collection layout is missing.");
        var replaceView = view is null || layoutKind != layout.Kind || axis != declaration.ScrollAxis;
        var replaceSource = source is null || !source.CanUpdate(binding, declaration);
        var rebaseSource = replaceSource && !replaceView && source?.CanRebaseWindow(binding, declaration) == true;
        if(replaceView || replaceSource)CancelHeldSelection();
        var rebasedEntry=replaceSource&&!replaceView?RebaseEntry(entry,binding,declaration):null;
        var userScrollPending = IsViewportScrollPending;
        if (replaceView || replaceSource) ScrollOwnerChanging?.Invoke(rebaseSource);
        var continuity = rebasedEntry is not null?null:replaceSource && !replaceView ? CaptureContinuity(binding, declaration, !userScrollPending) :
            !replaceView && !userScrollPending && !HasReadingOwner && source?.Declaration.IndexedCollection is { EndFollowingEpoch: not null } prior &&
            declaration.IndexedCollection is { } next && (next.ContentRevision > prior.ContentRevision || next.RetainsAppendedPrefix && prior.Count > 0 && next.Count > prior.Count) &&
            presentationActive && inputActive && (pendingContinuity?.FollowEnd == true || ViewportAtEnd())
                ? new Continuity(null, null) { FollowEnd = true } : null;
        if (replaceSource || replaceView || continuity is not null) CancelContinuity();
        // Protect current row focus before either native view or source retires.
        // Default entry and same-request query refresh need this just as much as
        // exact-item requests; WinUI otherwise focuses the empty ListView itself.
        if ((replaceView || replaceSource) && !(rebaseSource && CaptureFocusedItem() is { } focused &&
            declaration.IndexedOccurrenceKeys!.Contains(focused.ItemKey))) ParkFocusForQueryReplacement();
        if (view is null || replaceView)
        {
            CancelNavigation();
            RetireViewRows();
            DetachItems();
            DetachContainers();
            if (view is not null)
            {
                DetachViewportObservation();
                view.UnregisterPropertyChangedCallback(ForegroundProperty, discoveryForegroundToken);
                view.UnregisterPropertyChangedCallback(Control.PaddingProperty, paddingToken);
                view.UnregisterPropertyChangedCallback(Control.FontFamilyProperty, fontFamilyToken);
                view.Footer = null;
                view.ItemsSource = null; view.ItemClick -= Clicked; view.ContainerContentChanging -= ContainerChanged;
                view.SizeChanged -= NativeViewportSizeChanged;
            }
            view = layout.Kind == CollectionLayoutKind.AdaptiveGrid ? new GridView() : new ListView();
            view.UseSystemFocusVisuals = false;
            discoveryForegroundToken = view.RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => UpdateDiscoveryForeground());
            // Native ItemsPresenter places padding inside its scroll viewport.
            // A theme/state can change that inset without changing our outer size.
            // Recompute the same grid capacity used by layout and navigation,
            // independently of semantic source updates and provider retry policy.
            paddingToken = view.RegisterPropertyChangedCallback(Control.PaddingProperty, (_, _) => UpdateGridWidth());
            fontFamilyToken = view.RegisterPropertyChangedCallback(Control.FontFamilyProperty, (_, _) => RefreshInheritedFonts());
            layoutKind = layout.Kind; axis = declaration.ScrollAxis;
            view.SelectionMode = ListViewSelectionMode.None;
            view.IsItemClickEnabled = true;
            view.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var resources = Application.Current.Resources;
            view.ItemTemplate = (DataTemplate)resources[layout.Kind == CollectionLayoutKind.AdaptiveGrid
                ? "WidgetIndexedGridTileTemplate" : "WidgetIndexedRowTemplate"];
            view.ItemsPanel = (ItemsPanelTemplate)resources[layout.Kind == CollectionLayoutKind.AdaptiveGrid
                ? "WidgetIndexedGridPanel" : axis == ScrollAxis.Horizontal ? "WidgetIndexedHorizontalPanel" : "WidgetIndexedVerticalPanel"];
            view.ItemClick += Clicked;
            view.ContainerContentChanging += ContainerChanged;
            view.SizeChanged += NativeViewportSizeChanged;
            view.Loaded += (_, _) => UpdateGridWidth();
            Content = CreateSelectionContent(view, declaration.IndexedSelection is not null);
        }
        if (rebaseSource)
        {
            CancelNavigation();
            if (continuity?.Viewport is not null && declaration.IndexedCollection?.EndFollowingEpoch is not null &&
                view!.ItemsPanelRoot is ItemsStackPanel stack)
            {
                // WinUI declines first-item anchoring at offset 0, so a prepend
                // needs bottom-up tracking. Appending uses first-item tracking:
                // KeepLastItemInView at the old bottom would follow the new tail
                // and skip the appended page while the reader is scrolling.
                continuityPanel = stack;
                continuityScrollMode = stack.ItemsUpdatingScrollMode;
                var oldAnchorIndex = source!.Declaration.IndexedOccurrenceKeys!.ToList().IndexOf(continuity.Viewport.Key);
                stack.ItemsUpdatingScrollMode = continuity.Viewport.Index > oldAnchorIndex
                    ? ItemsUpdatingScrollMode.KeepLastItemInView : ItemsUpdatingScrollMode.KeepItemsInView;
            }
            source!.RebaseWindow(binding, declaration,preparedWindow);
        }
        else if (source is null || replaceSource)
        {
            CancelNavigation();
            DetachItems();
            DetachContainers();
            var replacement = new WidgetIndexedRows(session, binding, declaration, DispatcherQueue) { Failed = failed };
            if ((continuity is not null || rebasedEntry is not null) && source is not null && declaration.IndexedOccurrenceKeys is { } keys)
                replacement.Items.SeedPresentation(source.Items, keys);
            if (source is not null) RetireSource(source);
            source = replacement;
            if (!presentationActive) _ = source.SetPresentationActiveAsync(false);
            source.DiscoveryChanged += UpdateDiscoveryFooter;
            source.StylesChanged += IndexedStylesChanged;
        }
        else
        {
            source.Update(binding, declaration);
        }
        ApplyGroups(declaration.IndexedGroups);
        // ItemsWrapGrid measures its uniform cells from item zero even when its
        // native range excludes that offscreen measurement container. Keep that
        // one logical item current; otherwise deep recreation freezes the whole
        // grid at the placeholder estimate instead of the authored cell height.
        if (layout.Kind == CollectionLayoutKind.AdaptiveGrid && source.Items.Count > 0)
            measurementRetention ??= source.Items.Retain(0);
        AutomationProperties.SetAutomationId(view, "Widget." + declaration.Id + ".Items");
        AutomationProperties.SetName(view, declaration.AccessibilityLabel ?? declaration.Id);
        inputActive = binding.IsCurrent && scope == binding.Scope;
        var active = presentationActive && inputActive;
        UpdateDiscoveryFooter();
        view.IsEnabled = declaration.IsDisabled != true && declaration.IsBusy != true;
        if (!CanReceiveInput) {CancelHeldSelection();CancelNavigation();}
        view.IsItemClickEnabled = active && !SelectionActive;
        foreach (var (container, item) in containers) container.IsTabStop = active && item.Slot.Value is not null;
        foreach (var item in containers.Values) item.LayoutOwner?.RefreshReadingInteraction();
        view.IsTabStop = active;
        view.IsHitTestVisible = active;
        ScrollViewer.SetVerticalScrollMode(view, axis == ScrollAxis.Horizontal ? ScrollMode.Disabled : ScrollMode.Enabled);
        ScrollViewer.SetHorizontalScrollMode(view, axis == ScrollAxis.Horizontal ? ScrollMode.Enabled : ScrollMode.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(view, axis == ScrollAxis.Horizontal ? ScrollBarVisibility.Disabled :
            declaration.ShowScrollbar == false ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(view, axis != ScrollAxis.Horizontal ? ScrollBarVisibility.Disabled :
            declaration.ShowScrollbar == false ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto);
        UpdateGridWidth();
        // Native controls may be rebuilt for presentation changes. Logical entry
        // belongs to the unchanged query, not that discarded control instance.
        if (ReferenceEquals(previousSource, source) && !rebaseSource) RestoreEntry(entry);
        else RestoreEntry(rebasedEntry);
        if (continuity is not null) BeginContinuity(continuity);
        ValidatePendingActivation();
        RefreshSelectionState();
        ContextChanged?.Invoke();
        AttachViewportObservation();
        QueueViewportObservation();
    }

    private void UpdateGridWidth()
    {
        if (view is not null && source?.Declaration.CollectionLayout is { } layout)
        {
            var previousWidth = (view.ItemsPanelRoot as ItemsWrapGrid)?.ItemWidth;
            UpdateGridWidth(view, layout.MinimumColumnWidth ?? 160, layout.MaximumColumns ?? int.MaxValue);
            if (pendingIndex is not null && previousWidth != (view.ItemsPanelRoot as ItemsWrapGrid)?.ItemWidth) QueueNavigation();
        }
    }
    internal static void UpdateGridWidth(ListViewBase view, double minimum, int maximum)
    {
        if (view.ItemsPanelRoot is not ItemsWrapGrid grid) return;
        var scroller = FindNativeScroll(view);
        if (scroller is null || scroller.ViewportWidth <= 0) return;
        // The native viewport owns available space. GridView.ActualWidth can be
        // shrink-wrapped to its items; using it here feeds item padding back into
        // each layout pass and ratchets the desired width until WinUI fails.
        // ItemsPresenter applies the authored padding inside that viewport.
        var inset = view.Padding.Left + view.Padding.Right;
        var available = Math.Max(0, scroller.ViewportWidth - inset);
        if (available <= 0) return;
        var columns = Math.Clamp((int)(available / minimum), 1, maximum);
        // Native layout rounds item sizes to physical pixels. Whole DIP rounding
        // can round six 174-DIP cells up to 174.4 at 125%, forcing a fifth column.
        // Allocate an integral pixel count per cell before converting back to DIPs.
        var scale = view.XamlRoot?.RasterizationScale ?? 1;
        var width = Math.Floor(available * scale / columns) / scale;
        if (grid.MaximumRowsOrColumns != columns) grid.MaximumRowsOrColumns = columns;
        if (!grid.ItemWidth.Equals(width)) grid.ItemWidth = width;
    }
    private static ScrollViewer? FindNativeScroll(DependencyObject element)
    {
        if (element is ScrollViewer scroll) return scroll;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); ++index)
            if (FindNativeScroll(VisualTreeHelper.GetChild(element, index)) is { } found) return found;
        return null;
    }

    private void ContainerChanged(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not SelectorItem container) return;
        if (containers.Remove(container, out var prior)) prior.Slot.StateChanged -= prior.Changed;
        if (args.InRecycleQueue || args.Item is not IndexedItem<WidgetIndexedRow> slot) return;
        EventHandler changed = (_, _) => UpdateContainer(container, slot);
        containers.Add(container, (slot, changed, null));
        slot.StateChanged += changed;
        UpdateContainer(container, slot);
    }
    private void UpdateContainer(SelectorItem container, IndexedItem<WidgetIndexedRow> slot)
    {
        container.IsTabStop = presentationActive && inputActive && slot.Value is not null;
        ConfigureContainerLayout(container, axis ?? ScrollAxis.Vertical,
            slot.Value is null ? source?.Declaration.CollectionLayout?.EstimatedItemExtent ?? 0 : 0);
        // A logical position without a rendered row reserves layout only. WinUI
        // can otherwise focus an empty placeholder during native fallback (for
        // example while old Visible-state rows are disabled during admission).
        // Controller entry continues waiting for its current lease in FinishNavigation.
        // Busy rejects worker actions without removing the user's native focus.
        // Ordinary rows retain authored status. Opt-in selection uses controlled
        // host checkboxes rather than an independently mutable native selection.
        container.IsEnabled = slot.Value is { Item.Root: not { IsDisabled: true } };
        AutomationProperties.SetAutomationId(container, "Widget." + (source?.Declaration.Id ?? "collection") + ".Item." + slot.Index);
        AutomationProperties.SetName(container, slot.Value?.Item.Root.AccessibilityLabel ?? slot.Value?.Item.Root.Text ?? $"Loading item {slot.Index + 1}");
        UpdateSelectionAccessibility(container, slot);
        if (pendingIndex == slot.Index) FinishNavigation(null, null!);
        if (FocusedIndex() == slot.Index) { RememberItemFocus(); PresentationChanged?.Invoke(); }
        ContextChanged?.Invoke();
    }
    internal void RefreshContainerLayout()
    {
        foreach (var container in containers.Keys) NativeComputedStyleAdapter.For(container)?.RefreshBoxLayout();
    }
    private long fontFamilyToken;
    private void RefreshInheritedFonts()
    {
        if (view is null) return;
        foreach (var container in containers.Keys.ToArray())
            NativeComputedStyleAdapter.For(container)?.UpdateInheritedFontFamily(view.FontFamily);
    }
    internal void SuspendForTransition()
    {
        CancelHeldSelection();
        CancelContinuity();
        inputActive = false;
        CancelNavigation();
        CancelPendingActivation();
        if (view is not null) { view.IsTabStop = false; view.IsItemClickEnabled = false; }
        foreach (var container in containers.Keys) container.IsTabStop = false;
    }
    internal static void ConfigureContainerLayout(SelectorItem container, ScrollAxis axis, double placeholderExtent)
    {
        container.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        if (container is GridViewItem)
        {
            // A loading tile has no intrinsic height. Fill the estimated native
            // cell until the loaded row can use its ordinary template alignment.
            if (placeholderExtent > 0) container.VerticalContentAlignment = VerticalAlignment.Stretch;
            else container.ClearValue(Control.VerticalContentAlignmentProperty);
        }
        // Remove native template spacing until the row's single box-style owner
        // supplies authored margins. Do not overwrite that owner on every slot
        // notification: Content/HasValue notifications arrive after row binding.
        var styleOwned = NativeComputedStyleAdapter.For(container) is not null;
        if (!styleOwned) container.Margin = new Thickness(0);
        // The estimate belongs to a loading placeholder on the scrolling axis,
        // not a permanent minimum height. A horizontal poster rail must let the
        // native item fit its portrait rather than stretching its focus box to
        // the entire list viewport.
        if (!styleOwned)
        {
            container.MinHeight = axis == ScrollAxis.Horizontal ? 0 : placeholderExtent;
            container.MinWidth = axis == ScrollAxis.Horizontal ? placeholderExtent : 0;
        }
        container.VerticalAlignment = axis == ScrollAxis.Horizontal ? VerticalAlignment.Top : VerticalAlignment.Stretch;
    }
    private void DetachContainers()
    {
        foreach (var subscription in containers.Values) subscription.Slot.StateChanged -= subscription.Changed;
        containers.Clear();
    }
    private void Clicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is IndexedItem<WidgetIndexedRow> { Value: { } row })
        {
            if (SelectionActive) _ = SelectRowAsync(row, PointerSelectionGesture);
            else if (row.Item.Root.IsReadingGroup) return; // Child controls own pointer activation; the reading row has no primary action.
            else _ = InvokeAsync(row, ControllerButton.A);
        }
    }
    internal bool ActivateFocused() => ActivateDiscoveryFooter() || InvokeFocused(ControllerButton.A);
    internal bool InvokeFocused(ControllerButton button, ControllerEventPhase phase = ControllerEventPhase.Pressed)
    {
        if (button != ControllerButton.A && phase == ControllerEventPhase.Pressed) CancelPendingActivation();
        if (!CanReceiveInput || view?.XamlRoot is null) return false;
        for (var focused = FocusManager.GetFocusedElement(view.XamlRoot) as DependencyObject;
             focused is not null && !ReferenceEquals(focused, view); focused = VisualTreeHelper.GetParent(focused))
            if (focused is SelectorItem container && containers.TryGetValue(container, out var item))
            {
                if (item.Slot.Value is { } row) _ = InvokeAsync(row, button, phase);
                return true;
            }
        return false;
    }
    internal async Task<bool> InvokeFocusedInputAsync(ControllerButton button, ControllerEventPhase phase)
    {
        if(ReturnReadingGroup(button,phase))return true;
        if (button != ControllerButton.A && phase == ControllerEventPhase.Pressed) CancelPendingActivation();
        if (!CanReceiveInput || view?.XamlRoot is null) return false;
        for (var focused = FocusManager.GetFocusedElement(view.XamlRoot) as DependencyObject;
             focused is not null && !ReferenceEquals(focused, view); focused = VisualTreeHelper.GetParent(focused))
            if (focused is SelectorItem container && containers.TryGetValue(container, out var item))
                // Loading/retired targets consume the event. Only a proven absence in
                // the exact displayed declaration can fall through to the host.
                return item.Slot.Value is not { } row || await InvokeAsync(row, button, phase);
        return false;
    }
    private async Task<bool> InvokeAsync(WidgetIndexedRow row, ControllerButton button, ControllerEventPhase phase = ControllerEventPhase.Pressed, bool allowDeferredActivation = true)
    {
        if (disposed || !CanReceiveInput || source is null) return true;
        if (SelectionActive && button == ControllerButton.A)
        { if (phase == ControllerEventPhase.Pressed) await SelectRowAsync(row, IndexedSelectionGesture.Toggle); return true; }
        if(ActivateReadingGroup(row,button,phase))return true;
        if (button == ControllerButton.A && phase == ControllerEventPhase.Pressed)
        {
            if (pendingActivation is not null) return true;
            if (!row.Lease.IsCurrent) { if (allowDeferredActivation) DeferActivation(row); return true; }
        }
        if (!row.Lease.IsCurrent) return true;
        if (button == ControllerButton.A && phase == ControllerEventPhase.Pressed &&
            row.Item.Root is { IsDisabled: not true, IsBusy: not true })
        {
            var anchor = containers.FirstOrDefault(pair => ReferenceEquals(pair.Value.Slot.Value, row)).Key;
            if (anchor is not null && PrimaryPreviewRequested?.Invoke(anchor) == true) return true;
        }
        var capturedSource = source;
        var capturedBinding = capturedSource.Presentation;
        var displayed = capturedBinding.Frame;
        try
        {
            if (!row.Lease.ClaimsInput(displayed, row.Item.Key, button, phase)) return false;
            var inputSequence = ++sequence;
            var timestamp = Environment.TickCount64 * 1000;
            if (EnsureInteractionAsync is { } admit && !await admit(displayed.Authority, CancellationToken.None)) return true;
            if (disposed || !CanReceiveInput || !ReferenceEquals(source, capturedSource) || !capturedBinding.SameInput(capturedSource.Presentation) ||
                !row.Lease.IsCurrent || !row.Lease.ClaimsInput(displayed, row.Item.Key, button, phase)) return true;
            if (button == ControllerButton.A && phase == ControllerEventPhase.Pressed &&
                session.ResolveIndexedIntentAction(row.Lease, displayed, row.Item.Key) is { } intentAction)
            {
                if (DispatchIntentAsync is { } dispatch) await dispatch(new(displayed, intentAction)
                { IndexedLease = row.Lease, IndexedItemKey = row.Item.Key,
                    PinnedSelection = capturedBinding.Selection, PinnedProjection = capturedBinding.Projection });
                return true;
            }
            await row.Lease.AdmitInputAsync(displayed, row.Item.Key, button, phase,
                sequence: inputSequence, monotonicTimestampMicroseconds: timestamp);
            // A null reply also denotes a worker publication racing IPC. It must
            // remain consumed for an input that the displayed declaration owns.
            return true;
        }
        catch (WidgetPresentationSessionException error) when (WidgetInputFailure.IsStale(error)) { session.RequestInputRefresh(displayed); return true; }
        catch (OperationCanceledException) when (disposed || !row.Lease.IsCurrent) { return true; }
        catch (Exception error)
        {
            if (!disposed && ReferenceEquals(source, capturedSource) && capturedBinding.SameInput(source?.Presentation)) failed(error);
            else Diagnostics.FrontendFailureLog.Current.Write("retired-indexed-input", error);
            return true;
        }
    }
    public async ValueTask DisposeAsync()
    {
        CancelHeldSelection();
        if (disposed) return;
        CancelPreparedWindow();
        CancelViewportRecovery(false);
        QueueViewportObservation(true);
        disposed = true;
        DetachViewportObservation();
        LosingFocus -= OnLosingFocus;
        view?.UnregisterPropertyChangedCallback(ForegroundProperty, discoveryForegroundToken);
        view?.UnregisterPropertyChangedCallback(Control.PaddingProperty, paddingToken);
        view?.UnregisterPropertyChangedCallback(Control.FontFamilyProperty, fontFamilyToken);
        CancelContinuity();
        CancelPendingActivation();
        CancelNavigation();
        RetireViewRows();
        DetachItems();
        if (view is not null) { view.Footer = null; view.ItemsSource = null; view.ItemClick -= Clicked; view.ContainerContentChanging -= ContainerChanged; view.SizeChanged -= NativeViewportSizeChanged; }
        DetachContainers(); Content = null;
        if (source is not null) RetireSource(source);
        await Task.WhenAll(retiring.ToArray());
        if (viewportObservationPump is { } observation) await observation;
    }

    private void RetireSource(WidgetIndexedRows previous)
    {
        previous.DiscoveryChanged -= UpdateDiscoveryFooter;
        previous.StylesChanged -= IndexedStylesChanged;
        TrackRetirement(previous.DisposeAsync().AsTask());
    }

    private void IndexedStylesChanged()
    {
        if (!disposed) PresentationChanged?.Invoke();
    }
    private void RetireViewRows()
    {
        if (view is null) return;
        var pending = new Stack<DependencyObject>();
        pending.Push(view);
        var rows = new List<WidgetIndexedRowView>();
        while (pending.TryPop(out var node))
        {
            if (node is WidgetIndexedRowView row) { rows.Add(row); continue; }
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); ++index) pending.Push(VisualTreeHelper.GetChild(node, index));
        }
        foreach (var row in rows) TrackRetirement(row.DisposeAsync().AsTask());
    }
    private void TrackRetirement(Task task)
    {
        retiring.Add(task);
        _ = ObserveAsync();
        async Task ObserveAsync()
        {
            try { await task; }
            catch (Exception error) { failed(error); }
            finally { retiring.Remove(task); }
        }
    }
}
