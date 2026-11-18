using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetSdk;
using WidgetRail.OverlayFrontend.WinUI.Diagnostics;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Shared declaration rendering inside a native item container.</summary>
public sealed partial class WidgetIndexedRowView : ContentControl, IAsyncDisposable
{
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(nameof(Row), typeof(object),
        typeof(WidgetIndexedRowView), new PropertyMetadata(null, (sender, _) => ((WidgetIndexedRowView)sender).ApplyRow()));
    public object? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }
    private WidgetViewPresenter? presenter;
    private Grid? contentRoot;
    private CheckBox? selectionCheck;
    private WidgetIndexedSelectionResources? selectionImageResources;
    internal CheckBox? SelectionToggle => selectionCheck;
    private WidgetIndexedCollectionView? selectionCollection;
    private readonly HashSet<Task> retiring = [];
    private bool disposed;
    private NativeComputedStyleAdapter? containerStyle;
    private SelectorItem? styleContainer;
    private WidgetPresentationIndexedLease? styledLease;
    private WidgetIndexedRow? appliedRow;
    private WidgetPresentationBinding? appliedPresentation;
    private object? appliedStyles;
    internal int ContentApplyCount { get; private set; }
    internal int SkippedContentApplyCount { get; private set; }
    internal Task SetPresentationActiveAsync(bool active)
    {
        containerStyle?.RefreshContextIndicator();
        return active && Row is WidgetIndexedRow { Lease.IsCurrent: false } ? Task.CompletedTask :
            presenter?.SetPresentationActiveAsync(active) ?? Task.CompletedTask;
    }
    public WidgetIndexedRowView()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Loaded += (_, _) => ApplyRow();
        Unloaded += (_, _) =>
        {
            // Native reparenting can deliver an old Unloaded after a new Loaded.
            // Retire only when the row is actually outside the live visual tree.
            if (!IsLoaded) Retire();
        };
    }
    private void ApplyRow()
    {
        var timing = PresentationPipelineTrace.Log.Begin();
        var outcome = PipelineOutcome.Failed;
        try { ApplyRowCore(); outcome = PipelineOutcome.Completed; }
        finally { PresentationPipelineTrace.Log.End(timing.Id, PipelineStage.IndexedRowApply, timing.Started, 1, outcome); }
    }
    private void ApplyRowCore()
    {
        TraceReviewRow("apply");
        if (disposed) return;
        if (Row is not WidgetIndexedRow row) { Retire(); return; }
        if (!ReferenceEquals(styledLease, row.Lease))
        {
            if (styledLease is not null) styledLease.RenderStylesChanged -= StylesChanged;
            styledLease = row.Lease;
            styledLease.RenderStylesChanged += StylesChanged;
        }
        var presentation = row.Owner.Presentation;
        var styles = row.Lease.RenderStyles;
        if (ReferenceEquals(appliedRow, row) && ReferenceEquals(appliedPresentation, presentation) && ReferenceEquals(appliedStyles, styles))
        {
            ++SkippedContentApplyCount;
            _ = presenter!.SetPresentationActiveAsync(row.Owner.IsPresentationActive);
        }
        else
        {
            laidOutRow = null;
            presenter ??= new(presentationOnly: true);
            _ = presenter.SetPresentationActiveAsync(row.Owner.IsPresentationActive);
            presenter.Session = row.Owner.Session;
            presenter.UseIndexedContainerStyles();
            presenter.Failed = row.Owner.Failed;
            presenter.ArtworkGeneration = row.Lease.LeaseId;
            presenter.ArtworkAuthorityCurrent = () => row.Lease.IsCurrent && row.Owner.IsPresentationActive;
            presenter.ResolveArtworkFallbackAsync = async (handle, token) =>
                await row.Owner.ResolveArtworkAsync(row, handle, token, allowFileSource: false) as WidgetEncodedArtwork;
            presenter.ResolveArtworkAsync = async (handle, token) =>
            {
                try { return await row.Owner.ResolveArtworkAsync(row, handle, token); }
                catch (WidgetPresentationSessionException) when (!row.Lease.IsCurrent) { return null; }
            };
            presenter.ApplyFragment(presentation.WithStyles(styles), row.Item.Root, row.Lease.Range.ScopeId);
            appliedRow = row; appliedPresentation = presentation; appliedStyles = styles;
            ++ContentApplyCount;
        }
        if (contentRoot is null)
        {
            contentRoot = new();
            contentRoot.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            contentRoot.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            contentRoot.Children.Add(presenter!); Grid.SetColumn(presenter!, 1); Content = contentRoot;
        }
        SelectorItem? container = null;
        WidgetIndexedCollectionView? collection = null;
        for (var current = VisualTreeHelper.GetParent(this); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is SelectorItem item) container ??= item;
            if (current is WidgetIndexedCollectionView indexed) { collection = indexed; break; }
        }
        if (!ReferenceEquals(container, styleContainer))
        {
            RetireRowLayout();
            containerStyle?.Dispose(); containerStyle = null; styleContainer = container;
            if (container is not null)
            {
                containerStyle = new(container);
                containerStyle.PointerPressAllowed = origin =>
                {
                    // A small nested toggle must stay under the pointer. Scaling
                    // the whole row around its centre can move that hit target
                    // beyond the original press before the button is released.
                    for (var target = origin; target is not null && !ReferenceEquals(target, container);
                        target = VisualTreeHelper.GetParent(target))
                        if (ReferenceEquals(target, selectionCheck)) return false;
                    return true;
                };
                containerStyle.InteractionChanged += (focused, pressed) => presenter?.SetIndexedRootInteraction(focused, pressed);
            }
        }
        if (containerStyle is not null)
        {
            // WinUI's default item style sets its own font family, breaking the
            // collection's inherited typography even when the row declares none.
            containerStyle.InheritedFontFamily = collection?.NativeView.FontFamily;
            containerStyle.ContextHintHost = contentRoot;
            containerStyle.StretchComposedContent = row.Item.Root.Kind == WidgetRail.WidgetProtocol.ViewNodeKind.ActionSurface;
            containerStyle.SetFocusVisualTarget(presenter!.FocusVisualTarget(row.Item.Root), row.Item.Root.FocusVisualTargetId is { } visualId
                ? row.Lease.RenderStyles.GetValueOrDefault(visualId) : null);
            containerStyle.ContextHintButton = WidgetContextIndicator.Resolve(row.Item.Root);
            containerStyle.ContextHintAdmitted = () => !disposed && ReferenceEquals(Row, row) && row.Lease.IsCurrent &&
                collection?.ContextHintsAdmitted == true && row.Owner.IsPresentationActive &&
                row.Owner.Presentation.Scope == row.Lease.Range.ScopeId;
            containerStyle.Update(row.Lease.RenderStyles.GetValueOrDefault(row.Item.Root.Id));
        }
        if (containerStyle is not null)
            presenter!.SetIndexedRootInteraction(containerStyle.Interaction.Focused, containerStyle.Interaction.Pressed);
        selectionCollection = collection;
        RefreshSelection();
        if (container is not null && collection is not null && HasCurrentLayout(row, container))
            collection.ReportRowLayout(container, this, row);
        else AwaitRowLayout();
    }
    internal void RefreshSelection()
    {
        if (contentRoot is null || Row is not WidgetIndexedRow row) return;
        var active = selectionCollection?.SelectionActive == true;
        if (active && selectionCheck is null)
        {
            selectionCheck = new() { MinWidth = 0, MinHeight = 32, Width = 32, Height = 32, IsTabStop = false, CornerRadius = new(4),
                VerticalAlignment = VerticalAlignment.Center, Margin = new(0,0,8,0) };
            // The checkbox owns its click. It must not also activate the containing row.
            selectionCheck.PointerPressed += (_, args) => args.Handled = true;
            selectionCheck.PointerReleased += (_, args) => args.Handled = true;
            selectionCheck.Tapped += (_, args) => args.Handled = true;
            selectionCheck.Click += (_, args) =>
            {
                if (Row is WidgetIndexedRow current && selectionCollection is { SelectionActive: true } owner)
                    _ = owner.ToggleSelectionAsync(current);
            };
            contentRoot.Children.Add(selectionCheck);
        }
        if (selectionCheck is null) return;
        var overlay = selectionCollection?.SelectionOverlaysContent == true;
        Grid.SetColumn(selectionCheck, overlay ? 1 : 0);
        Canvas.SetZIndex(selectionCheck, 1);
        selectionCheck.VerticalAlignment = overlay ? VerticalAlignment.Top : VerticalAlignment.Center;
        selectionCheck.HorizontalAlignment = HorizontalAlignment.Left;
        selectionCheck.Margin = overlay ? new(8) : new(0,0,8,0);
        // Do not copy row paint into the pointer target; preserve native contrast policy.
        selectionCheck.ClearValue(Control.BackgroundProperty);
        if (overlay)
        {
            selectionImageResources ??= new();
            if (!selectionCheck.Resources.MergedDictionaries.Contains(selectionImageResources))
                selectionCheck.Resources.MergedDictionaries.Add(selectionImageResources);
        }
        else if (selectionImageResources is not null)
            selectionCheck.Resources.MergedDictionaries.Remove(selectionImageResources);
        selectionCheck.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        selectionCheck.IsEnabled = active && row.Lease.IsCurrent && row.Item.Root.IsDisabled != true && row.Item.Root.IsBusy != true;
        var index = row.Lease.Range.StartIndex + row.Lease.Range.Items.TakeWhile(item => item.Key != row.Item.Key).Count();
        selectionCheck.IsChecked = active && selectionCollection!.IsSelected(index);
        if (styleContainer is not null) selectionCheck.Foreground = styleContainer.Foreground;
        AutomationProperties.SetAutomationId(selectionCheck, "Widget." + row.Item.Root.Id + ".Selection");
        AutomationProperties.SetName(selectionCheck, "Select " + (row.Item.Root.AccessibilityLabel ?? row.Item.Root.Text ?? "item"));
    }

    private void Retire()
    {
        var timing = PresentationPipelineTrace.Log.Begin();
        var outcome = PipelineOutcome.Failed;
        try { RetireCore(); outcome = PipelineOutcome.Completed; }
        finally { PresentationPipelineTrace.Log.End(timing.Id, PipelineStage.IndexedRowRetire, timing.Started, 1, outcome); }
    }
    private void RetireCore()
    {
        TraceReviewRow("retire");
        RetireRowLayout();
        appliedRow = null; appliedPresentation = null; appliedStyles = null;
        if (styledLease is not null) styledLease.RenderStylesChanged -= StylesChanged;
        styledLease = null;
        containerStyle?.Dispose(); containerStyle = null; styleContainer = null;
        var previous = presenter;
        presenter = null;
        Content = null;
        contentRoot = null; selectionCheck = null; selectionImageResources = null; selectionCollection = null;
        if (previous is not null)
        {
            var task = previous.DisposeAsync().AsTask();
            retiring.Add(task);
            _ = ObserveAsync(task);
        }
        async Task ObserveAsync(Task task)
        {
            try { await task; }
            catch (Exception error) { System.Diagnostics.Trace.TraceError("Indexed row disposal: {0}", error); }
            finally { retiring.Remove(task); }
        }
    }
    partial void TraceReviewRow(string phase);
    private void StylesChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (disposed || !IsLoaded || !ReferenceEquals(sender, styledLease) || styledLease?.IsCurrent != true) return;
        try { ApplyRow(); }
        catch (Exception error) { if (Row is WidgetIndexedRow row) row.Owner.Failed?.Invoke(error); }
    });
    public async ValueTask DisposeAsync()
    {
        disposed = true;
        Retire();
        await Task.WhenAll(retiring.ToArray());
    }
}
