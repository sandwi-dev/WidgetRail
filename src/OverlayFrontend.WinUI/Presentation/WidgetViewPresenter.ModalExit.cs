using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed class ModalExit(WidgetModalLayer layer)
    {
        internal WidgetModalLayer Layer { get; } = layer;
        internal HashSet<Binding> Retained { get; } = [];
        internal EventHandler<object>? LayoutReady;
    }
    private ModalExit? closingModal;
    internal bool HasClosingModal => closingModal is not null;
    internal Task<WidgetMotionOutcome>? ModalExitPlayback { get; private set; }

    private ModalExit? PrepareModalExit(IReadOnlyDictionary<string, Declaration> plan, bool sameOwner)
    {
        var incomingModal = plan.Values.Any(d => d.Node.Kind == ViewNodeKind.ModalLayer);
        if (!sameOwner || incomingModal) SettleModalExit();
        var options = WidgetMotionOptions.From(appearance, systemAnimationsEnabled);
        if (!presentationActive || !sameOwner || incomingModal || !IsLoaded || motionStage is null || options.Reduced || !options.AnimateDialogs) return null;
        var prior = declarations.Values.FirstOrDefault(d => d.Node.Kind == ViewNodeKind.ModalLayer);
        if (prior is null || bindings[prior.Node.Id].Element is not WidgetModalLayer layer || Bounds(layer) is not { } bounds) return null;
        var parentId = prior.Node.Children[0].Id;
        // A close retains pixels only when the original parent survives this
        // publication. Runtime/page replacement retires everything immediately.
        if (!plan.TryGetValue(parentId, out var parent) || parent.Identity != bindings[parentId].Identity) return null;
        SettleModalExit();
        var exit = new ModalExit(layer);
        exit.Retained.Add(bindings[prior.Node.Id]);
        Collect(prior.Node.Children[1]);
        if (ReferenceEquals(motionStage.Modal, layer)) motionStage.SetModal(null);
        else if (VisualTreeHelper.GetParent(layer) is Panel owner) owner.Children.Remove(layer);
        layer.Width = bounds.Width; layer.Height = bounds.Height; layer.Margin = new();
        Canvas.SetLeft(layer, bounds.X); Canvas.SetTop(layer, bounds.Y);
        layer.IsHitTestVisible = false;
        motionStage.Outgoing.Children.Add(layer);
        closingModal = exit;
        return exit;

        void Collect(ViewNode node)
        {
            var binding = bindings[node.Id]; exit.Retained.Add(binding);
            binding.Element.IsHitTestVisible = false;
            if (binding.Element is Control control) control.IsTabStop = false;
            if (binding.Element is WidgetIndexedCollectionView collection) collection.SuspendForTransition();
            foreach (var child in node.Children) Collect(child);
        }
    }

    private void StartModalExit(ModalExit? exit)
    {
        if (exit is null || !ReferenceEquals(closingModal, exit)) return;
        exit.LayoutReady = (_, _) =>
        {
            LayoutUpdated -= exit.LayoutReady; exit.LayoutReady = null;
            if (!ReferenceEquals(closingModal, exit)) return;
            try
            {
                ModalExitPlayback = exit.Layer.CloseAsync();
                _ = CompleteAsync(ModalExitPlayback);
            }
            catch (Exception error) { SettleModalExit(); ReportFailure(error); }
        };
        LayoutUpdated += exit.LayoutReady;
        InvalidateArrange();

        async Task CompleteAsync(Task<WidgetMotionOutcome> playback)
        {
            try { await playback; }
            catch (Exception error) { ReportFailure(error); }
            finally { if (ReferenceEquals(closingModal, exit)) SettleModalExit(); }
        }
    }
    private void SettleModalExit()
    {
        if (closingModal is not { } exit) return;
        closingModal = null;
        if (exit.LayoutReady is not null) LayoutUpdated -= exit.LayoutReady;
        exit.Layer.Dispose();
        motionStage?.Outgoing.Children.Remove(exit.Layer);
        foreach (var binding in exit.Retained) Retire(binding);
    }
}
