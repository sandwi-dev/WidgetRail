using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    internal object NavigationDiagnosticState()
    {
        var slot = navigationRetention?.Slot;
        var scroll = view is null ? null : FindNativeScroll(view);
        return new { pendingIndex, entering, navigationQueued, navigationNeedsScroll, CanReceiveInput,
            current = slot?.Value?.Lease.IsCurrent, key = slot?.Key, failed = slot?.Failed,
            targetLoaded = pendingIndex is { } index && view?.ContainerFromIndex(index) is FrameworkElement { IsLoaded: true },
            offset = scroll?.VerticalOffset, extent = scroll?.ScrollableHeight, viewport = scroll?.ViewportHeight,
            containers = containers.Keys.Select(item => view?.IndexFromContainer(item)).ToArray() };
    }
    private static readonly bool CaptureEntryFocusStacks = Environment.GetCommandLineArgs().Any(argument =>
        argument.StartsWith("--indexed-entry-validation=", StringComparison.Ordinal));
    partial void TraceFocusDeparture(LosingFocusEventArgs args)
    {
        NavigationTrace?.Invoke($"losing-focus pending={pendingIndex} entering={entering} direction={args.Direction} input={args.InputDevice} old={Describe(args.OldFocusedElement)} enabled={(args.OldFocusedElement as Control)?.IsEnabled} new={Describe(args.NewFocusedElement)}");
        if (CaptureEntryFocusStacks && NavigationTrace is not null && Id(args.NewFocusedElement).EndsWith(".Item.0", StringComparison.Ordinal))
            NavigationTrace("focus-zero-stack " + Environment.StackTrace);
    }
    private string Describe(DependencyObject? element)
    {
        if (element is not Microsoft.UI.Xaml.Controls.Primitives.SelectorItem item) return Id(element);
        var known = containers.TryGetValue(item, out var data);
        return $"{Id(item)} native={view?.IndexFromContainer(item)} tracked={(known ? data.Slot.Index : -1)} currentOwner={(known ? source?.Items.IndexOf(data.Slot) : -1)} object={System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item)}";
    }
    private static string Id(DependencyObject? element) => element is null ? "null" : AutomationProperties.GetAutomationId(element);
    internal Action<string>? NavigationTrace { get; set; }
    partial void TraceNavigation(string phase, int index, WidgetIndexedRow row, Control target) =>
        NavigationTrace?.Invoke($"{phase} index={index} entry={pendingEntry?.Index} entering={entering} nativeEnabled={target.IsEnabled} rowDisabled={row.Item.Root.IsDisabled} rowBusy={row.Item.Root.IsBusy} current={row.Lease.IsCurrent}");
}
