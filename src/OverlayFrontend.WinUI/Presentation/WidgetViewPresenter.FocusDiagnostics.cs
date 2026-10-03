using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal object ScrollRevealDiagnostics() => new
    {
        lastScrollRevealRequest, pending = pendingScrollReveal?.Request, activeScope, presentationActive, presentationOnly, HasTransientControl,
        targets = bindings.Values.Where(binding => binding.Identity.Id is "gallery.page-scroll" or "gallery.message.1" or "gallery.message.2")
            .Select(binding => new { binding.Identity, binding.Element.IsLoaded, binding.Element.ActualHeight,
                nativeScroll = NearestScroll(binding.LayoutElement) is { } scroll ? Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(scroll) : null }).ToArray(),
    };

    internal Action<string>? FocusTrace { get; set; }

    partial void ConfigureCollectionTrace(WidgetIndexedCollectionView collection) =>
        collection.NavigationTrace = phase => TraceFocus(phase);

    partial void TraceFocus(string phase)
    {
        if (FocusTrace is null || presentationOnly) return;
        var focused = FocusedBinding();
        var rememberedBinding = RememberedBinding();
        var currentItem = (focused?.Element as WidgetIndexedCollectionView)?.CaptureFocusedItem();
        var query = rememberedBinding is null ? null : declarations[rememberedBinding.Identity.Id].Node.IndexedCollection;
        var memory = query is null ? null : collectionMemory.GetValueOrDefault(new(rememberedBinding!.Identity, query.SourceId, query.QueryGeneration));
        FocusTrace(JsonSerializer.Serialize(new
        {
            phase, frame?.Authority.SnapshotSequence, activeScope, needsEntry,
            presentationInputEnabled, automaticFocusEnabled, applying,
            current = focused?.Identity.Id, currentIndex = currentItem?.Index,
            remembered = rememberedBinding?.Identity.Id, rememberedIndex = memory?.Index,
            pendingEntry = (rememberedBinding?.Element as WidgetIndexedCollectionView)?.IsEntryPending,
        }));
    }

    // Explicit fixture failure evidence: no user text, artwork or action payload.
    internal string FocusDiagnostics() => JsonSerializer.Serialize(new
    {
        frame?.Authority.SnapshotSequence, activeScope,
        initial = effectiveView?.InitialFocusId, needsEntry, focusQueued, applying,
        IsLoaded, closing = HasClosingModal, pendingGroup = pendingGroupEntry?.Group.Id,
        focused = FocusedBinding()?.Identity,
        focusedItem = (FocusedBinding()?.Element as WidgetIndexedCollectionView)?.CaptureFocusedItem(),
        remembered = remembered.GetValueOrDefault(activeScope),
        presentationActive, presentationInputEnabled, automaticFocusEnabled,
        collectionFocus = collectionMemory.Select(pair => new { pair.Key.Identity, pair.Value.Index }).ToArray(),
        eligible = bindings.Values.Where(Eligible).Select(binding => new
        {
            binding.Identity, binding.Element.IsLoaded, binding.Element.ActualWidth, binding.Element.ActualHeight,
            enabled = (binding.Element as Control)?.IsEnabled,
            tab = (binding.Element as Control)?.IsTabStop,
        }).ToArray(),
    });
}
