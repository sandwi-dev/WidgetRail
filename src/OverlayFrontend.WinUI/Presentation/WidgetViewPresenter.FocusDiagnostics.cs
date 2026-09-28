using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    // Explicit fixture failure evidence: no user text, artwork or action payload.
    internal string FocusDiagnostics() => JsonSerializer.Serialize(new
    {
        frame?.Authority.SnapshotSequence, frame?.Authority.ActiveInputScopeId,
        initial = frame?.Snapshot.InitialFocusId, needsEntry, focusQueued, applying,
        IsLoaded, closing = HasClosingModal, pendingGroup = pendingGroupEntry?.Group.Id,
        focused = FocusedBinding()?.Identity,
        eligible = bindings.Values.Where(Eligible).Select(binding => new
        {
            binding.Identity, binding.Element.IsLoaded, binding.Element.ActualWidth, binding.Element.ActualHeight,
            enabled = (binding.Element as Control)?.IsEnabled,
            tab = (binding.Element as Control)?.IsTabStop,
        }).ToArray(),
    });
}
