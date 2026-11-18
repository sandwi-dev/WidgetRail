using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal int PendingCollectionReturnCount => pendingIndexedViewports.Count(pending => pending.ReturnIntent is not null);
    internal string? PendingNativeRestoreForValidation => pendingRestore?.Target.Identity.Id;
    internal string? ScrollFocusOutlineForValidation=>scrollFocusOutline?.Identity.Id;
    internal object ScrollRevealDiagnostics() => new
    {
        lastScrollRevealRequest, pending = pendingScrollReveal?.Request,indexed=pendingIndexedScrollReveal?.Request,indexedStarted=pendingIndexedScrollReveal?.Started,
        activeScope, presentationActive, presentationOnly, HasTransientControl,
        targets = bindings.Values.Where(binding => binding.Identity.Id is "gallery.page-scroll" or "gallery.message.1" or "gallery.message.2")
            .Select(binding => new { binding.Identity, binding.Element.IsLoaded, binding.Element.ActualHeight,
                nativeScroll = NearestScroll(binding.LayoutElement) is { } scroll ? Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(scroll) : null }).ToArray(),
    };

    internal Action<string>? FocusTrace { get; set; }
    internal bool FocusTracePhasesOnly { get; set; }

    partial void TraceNativeFocusArrival(GettingFocusEventArgs args)
    {
        if (FocusTrace is null || presentationOnly) return;
        var previous = args.OldFocusedElement as FrameworkElement;
        var next = args.NewFocusedElement as FrameworkElement;
        FocusTrace(JsonSerializer.Serialize(new
        {
            phase = "native-focus-arrival", frame?.Authority.SnapshotSequence, activeScope, needsEntry, applying,
            input = args.InputDevice.ToString(), state = args.FocusState.ToString(), direction = args.Direction.ToString(),
            completedKeyboard = WidgetTextEntryDialog.IsCompletedFocusOrigin(args.OldFocusedElement),
            previous = previous is null ? null : new { id = AutomationProperties.GetAutomationId(previous), previous.IsLoaded, enabled = (previous as Control)?.IsEnabled,
                identity = previous.Tag as WidgetElementIdentity },
            next = next is null ? null : new { id = AutomationProperties.GetAutomationId(next), next.IsLoaded, enabled = (next as Control)?.IsEnabled,
                identity = next.Tag as WidgetElementIdentity },
            pending = pendingRestore?.Target.Identity.Id, fallback = pendingRestore?.NativeFallback is { } fallback ? AutomationProperties.GetAutomationId(fallback) : null
        }));
    }

    partial void ConfigureCollectionTrace(WidgetIndexedCollectionView collection) =>
        collection.NavigationTrace = phase => TraceFocus(phase);

    partial void TraceFocus(string phase)
    {
        if (FocusTrace is null || presentationOnly) return;
        // Lifecycle probes must not resolve a preview merely to observe it.
        if (FocusTracePhasesOnly) { FocusTrace(phase); return; }
        var focused = FocusedBinding();
        var rememberedBinding = RememberedBinding();
        var currentItem = (focused?.Element as WidgetIndexedCollectionView)?.CaptureFocusedItem();
        var query = rememberedBinding is null ? null : declarations[rememberedBinding.Identity.Id].Node.IndexedCollection;
        var memory = query is null ? null : collectionMemory.GetValueOrDefault(new(rememberedBinding!.Identity, query.SourceId, query.QueryGeneration));
        FocusTrace(JsonSerializer.Serialize(new
        {
            phase, frame?.Authority.SnapshotSequence, activeScope, needsEntry,
            presentationInputEnabled, automaticFocusEnabled, applying,
            preview = FocusPreviewDiagnostics(),
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
