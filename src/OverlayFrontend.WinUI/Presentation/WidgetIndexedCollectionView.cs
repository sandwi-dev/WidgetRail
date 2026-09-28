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
    private bool CanReceiveInput => inputActive && view?.IsEnabled == true;
    private long sequence;
    internal ListViewBase NativeView => view ?? throw new InvalidOperationException("Collection is not initialized.");
    internal Action? PresentationChanged { get; set; }
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
            if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Left or
                Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Tab or Windows.System.VirtualKey.Home or
                Windows.System.VirtualKey.End or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown)
                CancelNavigation();
        }), true);
    }

    internal void Apply(WidgetPresentationFrame frame, ViewNode declaration, string scope)
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
            if (view is not null) { view.ItemsSource = null; view.ItemClick -= Clicked; view.ContainerContentChanging -= ContainerChanged; view.LosingFocus -= OnLosingFocus; }
            view = layout.Kind == CollectionLayoutKind.AdaptiveGrid ? new GridView() : new ListView();
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
        if (source is null || !source.CanUpdate(frame, declaration))
        {
            CancelNavigation();
            DetachItems();
            DetachContainers();
            if (source is not null) RetireSource(source);
            source = new(session, frame, declaration, DispatcherQueue) { Failed = failed };
        }
        else
        {
            source.Update(frame, declaration);
        }
        ApplyGroups(declaration.IndexedGroups);
        AutomationProperties.SetAutomationId(view, "Widget." + declaration.Id + ".Items");
        AutomationProperties.SetName(view, declaration.AccessibilityLabel ?? declaration.Id);
        var active = scope == frame.Authority.ActiveInputScopeId;
        inputActive = active;
        view.IsEnabled = declaration.IsDisabled != true && declaration.IsBusy != true;
        if (!CanReceiveInput) CancelNavigation();
        view.IsItemClickEnabled = active;
        foreach (var container in containers.Keys) container.IsTabStop = active;
        view.IsTabStop = active;
        view.IsHitTestVisible = active;
        ScrollViewer.SetVerticalScrollMode(view, axis == ScrollAxis.Horizontal ? ScrollMode.Disabled : ScrollMode.Enabled);
        ScrollViewer.SetHorizontalScrollMode(view, axis == ScrollAxis.Horizontal ? ScrollMode.Enabled : ScrollMode.Disabled);
        UpdateGridWidth();
        // Native controls may be rebuilt for presentation changes. Logical entry
        // belongs to the unchanged query, not that discarded control instance.
        if (ReferenceEquals(previousSource, source)) RestoreEntry(entry);
        ValidatePendingActivation();
    }

    private void UpdateGridWidth()
    {
        if (view?.ItemsPanelRoot is not ItemsWrapGrid grid || source?.Declaration.CollectionLayout is not { } layout || view.ActualWidth <= 0) return;
        var minimum = layout.MinimumColumnWidth ?? 160;
        var columns = Math.Clamp((int)(view.ActualWidth / minimum), 1, layout.MaximumColumns ?? int.MaxValue);
        grid.MaximumRowsOrColumns = columns;
        grid.ItemWidth = Math.Floor(view.ActualWidth / columns);
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
        container.IsTabStop = inputActive;
        container.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        container.MinHeight = source?.Declaration.CollectionLayout?.EstimatedItemExtent ?? 0;
        container.IsEnabled = slot.Value?.Item.Root is not { IsDisabled: true } and not { IsBusy: true };
        AutomationProperties.SetAutomationId(container, "Widget." + (source?.Declaration.Id ?? "collection") + ".Item." + slot.Index);
        AutomationProperties.SetName(container, slot.Value?.Item.Root.AccessibilityLabel ?? slot.Value?.Item.Root.Text ?? $"Loading item {slot.Index + 1}");
        if (pendingIndex == slot.Index) FinishNavigation(null, null!);
        if (FocusedIndex() == slot.Index) { RememberItemFocus(); PresentationChanged?.Invoke(); }
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
    internal bool ActivateFocused() => InvokeFocused(ControllerButton.A);
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
    private async Task InvokeAsync(WidgetIndexedRow row, ControllerButton button, ControllerEventPhase phase = ControllerEventPhase.Pressed, bool allowDeferredActivation = true)
    {
        if (disposed || !CanReceiveInput || source is null) return;
        if (button == ControllerButton.A && phase == ControllerEventPhase.Pressed)
        {
            if (pendingActivation is not null) return;
            if (!row.Lease.IsCurrent) { if (allowDeferredActivation) DeferActivation(row); return; }
        }
        if (!row.Lease.IsCurrent) return;
        try { await row.Lease.AdmitInputAsync(source.Frame.Authority, row.Item.Key, button, phase,
            sequence: ++sequence, monotonicTimestampMicroseconds: Environment.TickCount64 * 1000); }
        catch (WidgetPresentationSessionException) when (!row.Lease.IsCurrent) { }
        catch (Exception error) { failed(error); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        CancelPendingActivation();
        CancelNavigation();
        RetireViewRows();
        DetachItems();
        if (view is not null) { view.ItemsSource = null; view.ItemClick -= Clicked; view.ContainerContentChanging -= ContainerChanged; view.LosingFocus -= OnLosingFocus; }
        DetachContainers(); Content = null;
        if (source is not null) RetireSource(source);
        await Task.WhenAll(retiring.ToArray());
    }

    private void RetireSource(WidgetIndexedRows previous)
    {
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
