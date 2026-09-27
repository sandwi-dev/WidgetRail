using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SystemCompositor = Windows.UI.Composition.Compositor;
using SystemColorBrush = Windows.UI.Composition.CompositionColorBrush;

namespace WidgetRail.OverlayFrontend.WinUI;

/// <summary>
/// Supplies a transparent external backdrop brush. This alone does not establish
/// desktop transparency; HWND hosting and pixel validation remain separate.
/// </summary>
internal sealed class TransparentWindowBackdrop : SystemBackdrop
{
    private readonly Dictionary<ICompositionSupportsSystemBackdrop, TargetResources> targets = [];

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget,
        XamlRoot xamlRoot)
    {
        if (targets.ContainsKey(connectedTarget))
        {
            throw new InvalidOperationException("The backdrop target is already connected.");
        }

        base.OnTargetConnected(connectedTarget, xamlRoot);

        SystemCompositor? compositor = null;
        SystemColorBrush? brush = null;
        try
        {
            // The framework owns the required OS queue and its shutdown.
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().EnsureSystemDispatcherQueue();
            // SystemBackdrop is an OS-composition boundary: its public property
            // requires Windows.UI.Composition, not XAML's Microsoft.UI.Composition.
            // This owner supplies only the external backdrop brush; XAML retains
            // exclusive ownership of the window's content composition tree.
            compositor = new SystemCompositor();
            brush = compositor.CreateColorBrush(Colors.Transparent);
            targets.Add(connectedTarget, new TargetResources(compositor, brush));
            connectedTarget.SystemBackdrop = brush;
        }
        catch
        {
            targets.Remove(connectedTarget);
            brush?.Dispose();
            compositor?.Dispose();
            base.OnTargetDisconnected(connectedTarget);
            throw;
        }
    }

    protected override void OnTargetDisconnected(
        ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        try
        {
            if (targets.Remove(disconnectedTarget, out var resources))
            {
                try
                {
                    // A replacement backdrop may already own the target. Do not
                    // clear its brush while retiring resources from this instance.
                    if (Equals(disconnectedTarget.SystemBackdrop, resources.Brush))
                    {
                        disconnectedTarget.SystemBackdrop = null;
                    }
                }
                finally
                {
                    resources.Dispose();
                }
            }
        }
        finally
        {
            base.OnTargetDisconnected(disconnectedTarget);
        }
    }

    private sealed record TargetResources(SystemCompositor Compositor, SystemColorBrush Brush) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                Brush.Dispose();
            }
            finally
            {
                Compositor.Dispose();
            }
        }
    }
}
