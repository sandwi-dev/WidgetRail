using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    public WidgetIndexedRowView()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Loaded += (_, _) => ApplyRow();
        Unloaded += (_, _) => Retire();
    }
    private void ApplyRow()
    {
        if (disposed) return;
        if (Row is not WidgetIndexedRow row) { Retire(); return; }
        presenter ??= new(presentationOnly: true);
        presenter.Failed = row.Owner.Failed;
        presenter.ArtworkGeneration = row.Lease.LeaseId;
        presenter.ResolveArtworkAsync = async (handle, token) =>
        {
            try { return await row.Owner.ResolveArtworkAsync(row, handle, token); }
            catch (WidgetPresentationSessionException) when (!row.Lease.IsCurrent) { return null; }
        };
        presenter.ApplyFragment(row.Owner.Frame with { RenderStyles = row.Lease.RenderStyles }, row.Item.Root, row.Lease.Range.ScopeId);
        Content = presenter;
    }
    private void Retire()
    {
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
