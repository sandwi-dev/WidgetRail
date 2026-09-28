using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;
using System.ComponentModel;
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
    private readonly Dictionary<SelectorItem, (IndexedItem<WidgetIndexedRow> Slot, PropertyChangedEventHandler Changed)> containers = [];
    private readonly HashSet<Task> retiring = [];
    private WidgetIndexedRows? source;
    private ListViewBase? view;
    private CollectionLayoutKind? layoutKind;
    private ScrollAxis? axis;
    private bool disposed;
    private bool inputActive;
    private bool presentationActive = true;
    private bool CanReceiveInput => presentationActive && inputActive && source?.Presentation.IsCurrent == true && view?.IsEnabled == true;
    internal async Task SetPresentationActiveAsync(bool active)
    {
        presentationActive = active;
        if (!active) { CancelNavigation(); CancelPendingActivation(); }
        if (view is not null)
        {
            view.IsHitTestVisible = active && inputActive;
            view.IsTabStop = active && inputActive;
            view.IsItemClickEnabled = active && inputActive;
            foreach (var container in containers.Keys) container.IsTabStop = active && inputActive;
        }
        var drains = new List<Task>();
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
    internal ListViewBase NativeView => view ?? throw new InvalidOperationException("Collection is not initialized.");
    internal Action? PresentationChanged { get; set; }
    internal Func<WidgetPresentationAuthority, CancellationToken, Task<bool>>? EnsureInteractionAsync { get; set; }
    internal bool Owns(WidgetIndexedRows owner) => ReferenceEquals(owner, source);
    internal WidgetIndexedRow? FocusedRow() => source is not null && FocusedIndex() is { } index
        ? ((IndexedItem<WidgetIndexedRow>)source.Items[index]).Value : null;
    internal IndexedItemsSource<WidgetIndexedRow>.Retention? RetainFocusedRow() => source is not null && FocusedIndex() is { } index ? source.Items.Retain(index) : null;

    internal WidgetIndexedCollectionView(PresentationSession session, Action<Exception> failed)
    {
        this.session = session; this.failed = failed;
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        SizeChanged += (_, _) => UpdateGridWidth();
        GotFocus += (_, _) => { ValidatePendingActivation(); RememberItemFocus(); };
        LostFocus += (_, _) => ValidatePendingActivation();
        Unloaded += (_, _) => { if (!IsLoaded) CancelPendingActivation(); };
        GotFocus += (_, _) => PresentationChanged?.Invoke();
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => CancelNavigation()), true);
        AddHandler(KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (Input.GamepadKeyBoundary.Owns(this, args)) return;
            if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Left or
                Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Tab or Windows.System.VirtualKey.Home or
                Windows.System.VirtualKey.End or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown)
                CancelNavigation();
        }), true);
    }

    internal void Apply(WidgetPresentationFrame frame, ViewNode declaration, string scope) => Apply(WidgetPresentationBinding.ForMain(frame), declaration, scope);
    internal void Apply(WidgetPresentationBinding binding, ViewNode declaration, string scope)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var entry = CaptureEntry();
        var previousSource = source;
        var layout = declaration.CollectionLayout ?? throw new InvalidDataException("Indexed collection layout is missing.");
        if (view is null || layoutKind != layout.Kind || axis != declaration.ScrollAxis)
        {
            CancelNavigation();
            RetireViewRows();
            DetachItems();
            DetachContainers();
            if (view is not null)
            {
                view.UnregisterPropertyChangedCallback(ForegroundProperty, discoveryForegroundToken);
                view.Footer = null;
                view.ItemsSource = null; view.ItemClick -= Clicked; view.ContainerContentChanging -= ContainerChanged; view.LosingFocus -= OnLosingFocus;
            }
            view = layout.Kind == CollectionLayoutKind.AdaptiveGrid ? new GridView() : new ListView();
            discoveryForegroundToken = view.RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => UpdateDiscoveryForeground());
            layoutKind = layout.Kind; axis = declaration.ScrollAxis;
            view.SelectionMode = ListViewSelectionMode.None;
            view.IsItemClickEnabled = true;
            view.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var resources = Application.Current.Resources;
            view.ItemTemplate = (DataTemplate)resources["WidgetIndexedRowTemplate"];
            view.ItemsPanel = (ItemsPanelTemplate)resources[layout.Kind == CollectionLayoutKind.AdaptiveGrid
                ? "WidgetIndexedGridPanel" : axis == ScrollAxis.Horizontal ? "WidgetIndexedHorizontalPanel" : "WidgetIndexedVerticalPanel"];
            view.ItemClick += Clicked;
            view.ContainerContentChanging += ContainerChanged;
            view.LosingFocus += OnLosingFocus;
            view.Loaded += (_, _) => UpdateGridWidth();
            Content = view;
        }
        if (source is null || !source.CanUpdate(binding, declaration))
        {
            CancelNavigation();
            DetachItems();
            DetachContainers();
            if (source is not null) RetireSource(source);
            source = new(session, binding, declaration, DispatcherQueue) { Failed = failed };
            if (!presentationActive) _ = source.SetPresentationActiveAsync(false);
            source.DiscoveryChanged += UpdateDiscoveryFooter;
        }
        else
        {
            source.Update(binding, declaration);
        }
        ApplyGroups(declaration.IndexedGroups);
        AutomationProperties.SetAutomationId(view, "Widget." + declaration.Id + ".Items");
        AutomationProperties.SetName(view, declaration.AccessibilityLabel ?? declaration.Id);
        inputActive = binding.IsCurrent && scope == binding.Scope;
        var active = presentationActive && inputActive;
        UpdateDiscoveryFooter();
        view.IsEnabled = declaration.IsDisabled != true && declaration.IsBusy != true;
        if (!CanReceiveInput) CancelNavigation();
        view.IsItemClickEnabled = active;
        foreach (var container in containers.Keys) container.IsTabStop = active;
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
        if (ReferenceEquals(previousSource, source)) RestoreEntry(entry);
        ValidatePendingActivation();
        ContextChanged?.Invoke();
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
        if (containers.Remove(container, out var prior)) prior.Slot.PropertyChanged -= prior.Changed;
        if (args.InRecycleQueue || args.Item is not IndexedItem<WidgetIndexedRow> slot) return;
        PropertyChangedEventHandler changed = (_, _) => UpdateContainer(container, slot);
        containers.Add(container, (slot, changed));
        slot.PropertyChanged += changed;
        UpdateContainer(container, slot);
    }
    private void UpdateContainer(SelectorItem container, IndexedItem<WidgetIndexedRow> slot)
    {
        container.IsTabStop = presentationActive && inputActive;
        ConfigureContainerLayout(container, axis ?? ScrollAxis.Vertical,
            slot.Value is null ? source?.Declaration.CollectionLayout?.EstimatedItemExtent ?? 0 : 0);
        container.IsEnabled = slot.Value?.Item.Root is not { IsDisabled: true } and not { IsBusy: true };
        AutomationProperties.SetAutomationId(container, "Widget." + (source?.Declaration.Id ?? "collection") + ".Item." + slot.Index);
        AutomationProperties.SetName(container, slot.Value?.Item.Root.AccessibilityLabel ?? slot.Value?.Item.Root.Text ?? $"Loading item {slot.Index + 1}");
        if (pendingIndex == slot.Index) FinishNavigation(null, null!);
        if (FocusedIndex() == slot.Index) { RememberItemFocus(); PresentationChanged?.Invoke(); }
        ContextChanged?.Invoke();
    }
    internal void RefreshContainerLayout()
    {
        foreach (var container in containers.Keys) NativeComputedStyleAdapter.For(container)?.RefreshBoxLayout();
    }
    internal void SuspendForTransition()
    {
        inputActive = false;
        CancelNavigation();
        CancelPendingActivation();
        if (view is not null) { view.IsTabStop = false; view.IsItemClickEnabled = false; }
        foreach (var container in containers.Keys) container.IsTabStop = false;
    }
    internal static void ConfigureContainerLayout(SelectorItem container, ScrollAxis axis, double placeholderExtent)
    {
        container.HorizontalContentAlignment = HorizontalAlignment.Stretch;
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
        foreach (var subscription in containers.Values) subscription.Slot.PropertyChanged -= subscription.Changed;
        containers.Clear();
    }
    private void Clicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is IndexedItem<WidgetIndexedRow> { Value: { } row }) _ = InvokeAsync(row, ControllerButton.A);
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
        if (button == ControllerButton.A && phase == ControllerEventPhase.Pressed)
        {
            if (pendingActivation is not null) return true;
            if (!row.Lease.IsCurrent) { if (allowDeferredActivation) DeferActivation(row); return true; }
        }
        if (!row.Lease.IsCurrent) return true;
        try
        {
            var capturedSource = source;
            var capturedBinding = capturedSource.Presentation;
            var displayed = capturedBinding.Frame;
            if (!row.Lease.ClaimsInput(displayed, row.Item.Key, button, phase)) return false;
            var inputSequence = ++sequence;
            var timestamp = Environment.TickCount64 * 1000;
            if (EnsureInteractionAsync is { } admit && !await admit(displayed.Authority, CancellationToken.None)) return true;
            if (disposed || !CanReceiveInput || !ReferenceEquals(source, capturedSource) || !capturedBinding.SameInput(capturedSource.Presentation) ||
                !row.Lease.IsCurrent || !row.Lease.ClaimsInput(displayed, row.Item.Key, button, phase)) return true;
            await row.Lease.AdmitInputAsync(displayed, row.Item.Key, button, phase,
                sequence: inputSequence, monotonicTimestampMicroseconds: timestamp);
            // A null reply also denotes a worker publication racing IPC. It must
            // remain consumed for an input that the displayed declaration owns.
            return true;
        }
        catch (WidgetPresentationSessionException error) when (!row.Lease.IsCurrent || error.Code is "snapshot_stale" or "input_scope_stale" or "presentation_stale" or "indexed_input_stale" or "pinned_input_stale" or "stale_pinned_input_authority") { return true; }
        catch (Exception error) { failed(error); return true; }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        view?.UnregisterPropertyChangedCallback(ForegroundProperty, discoveryForegroundToken);
        CancelPendingActivation();
        CancelNavigation();
        RetireViewRows();
        DetachItems();
        if (view is not null) { view.Footer = null; view.ItemsSource = null; view.ItemClick -= Clicked; view.ContainerContentChanging -= ContainerChanged; view.LosingFocus -= OnLosingFocus; }
        DetachContainers(); Content = null;
        if (source is not null) RetireSource(source);
        await Task.WhenAll(retiring.ToArray());
    }

    private void RetireSource(WidgetIndexedRows previous)
    {
        previous.DiscoveryChanged -= UpdateDiscoveryFooter;
        TrackRetirement(previous.DisposeAsync().AsTask());
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
