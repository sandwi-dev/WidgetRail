using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Shared declaration rendering inside a native item container.</summary>
public sealed class WidgetIndexedRowView : ContentControl, IAsyncDisposable
{
    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(nameof(Row), typeof(object),
        typeof(WidgetIndexedRowView), new PropertyMetadata(null, (sender, _) => ((WidgetIndexedRowView)sender).ApplyRow()));
    public object? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }
    private WidgetViewPresenter? presenter;
    private readonly HashSet<Task> retiring = [];
    private bool disposed;
    private NativeComputedStyleAdapter? containerStyle;
    private SelectorItem? styleContainer;
    internal Task SetPresentationActiveAsync(bool active) =>
        active && Row is WidgetIndexedRow { Lease.IsCurrent: false } ? Task.CompletedTask :
        presenter?.SetPresentationActiveAsync(active) ?? Task.CompletedTask;
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
        if (disposed) return;
        if (Row is not WidgetIndexedRow row) { Retire(); return; }
        presenter ??= new(presentationOnly: true);
        _ = presenter.SetPresentationActiveAsync(row.Owner.IsPresentationActive);
        presenter.Session = row.Owner.Session;
        presenter.UseIndexedContainerStyles();
        presenter.Failed = row.Owner.Failed;
        presenter.ArtworkGeneration = row.Lease.LeaseId;
        presenter.ResolveArtworkAsync = async (handle, token) =>
        {
            try { return await row.Owner.ResolveArtworkAsync(row, handle, token); }
            catch (WidgetPresentationSessionException) when (!row.Lease.IsCurrent) { return null; }
        };
        presenter.ApplyFragment(row.Owner.Frame with { RenderStyles = row.Lease.RenderStyles }, row.Item.Root, row.Lease.Range.ScopeId);
        Content = presenter;
        SelectorItem? container = null;
        for (var current = VisualTreeHelper.GetParent(this); current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is SelectorItem item) { container = item; break; }
        if (!ReferenceEquals(container, styleContainer))
        {
            containerStyle?.Dispose(); containerStyle = null; styleContainer = container;
            if (container is not null)
            {
                containerStyle = new(container);
                containerStyle.InteractionChanged += (focused, pressed) => presenter?.SetIndexedRootInteraction(focused, pressed);
            }
        }
        containerStyle?.Update(row.Lease.RenderStyles.GetValueOrDefault(row.Item.Root.Id));
        if (containerStyle is not null)
            presenter.SetIndexedRootInteraction(containerStyle.Interaction.Focused, containerStyle.Interaction.Pressed);
    }
    private void Retire()
    {
        containerStyle?.Dispose(); containerStyle = null; styleContainer = null;
        var previous = presenter;
        presenter = null;
        Content = null;
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
    public async ValueTask DisposeAsync()
    {
        disposed = true;
        Retire();
        await Task.WhenAll(retiring.ToArray());
    }
}
