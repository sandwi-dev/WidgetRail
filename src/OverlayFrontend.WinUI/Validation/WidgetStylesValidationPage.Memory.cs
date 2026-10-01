using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativePresentationMemoryAsync()
    {
        var rows = Enumerable.Range(0, 30).Select(index => new ViewNode { Id = "ordinary." + index,
            Kind = ViewNodeKind.Button, Text = "Row " + index, ActionId = "row" }).ToArray();
        var frame = CreateFrame(new() { Id = "memory-shell", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[
            new() { Id = "memory", Kind = ViewNodeKind.Scroll, ScrollAxis = ScrollAxis.Vertical,
                Children = (ViewNode[])[new() { Id = "memory-group", Kind = ViewNodeKind.Stack, InitialChildFocusId = "ordinary.0", Children = rows }] }] },
            new Dictionary<string, BridgeNodeRenderStyles> { ["memory"] = Compute("#memory { height: 180px; padding: 14px; }", "memory", "scroll") });
        var first = new WidgetViewPresenter();
        var second = new WidgetViewPresenter();
        host.Children.Insert(2, first);
        try
        {
            first.Apply(frame);
            await Wait(() => FindIn<Button>(first, "Widget.ordinary.20") is { ActualWidth: > 0 });
            var oldButton = FindIn<Button>(first, "Widget.ordinary.20")!;
            oldButton.Focus(FocusState.Keyboard);
            var oldScroll = FindIn<ScrollViewer>(first, "Widget.memory")!;
            await Wait(() => oldScroll.VerticalOffset > 400);
            await Task.Delay(80);
            var memory = first.CapturePresentationState()!;
            var offset = oldScroll.VerticalOffset;
            Check(memory.Groups.Count == 1 && memory.ScrollViewports.Count == 1,
                "evicted presentation memory captures ordinary group focus and semantic scroll anchor");
            await first.SetPresentationActiveAsync(false);
            host.Children.Remove(first); await first.DisposeAsync();
            await second.SetPresentationActiveAsync(false);
            second.SetAutomaticFocusEnabled(false); second.Apply(frame);
            Check(second.RestorePresentationState(memory), "same owner imports ordinary presentation memory");
            host.Children.Insert(2, second);
            await second.SetPresentationActiveAsync(true);
            await Wait(() => !second.HasPendingMemoryRestore);
            Check(Math.Abs(FindIn<ScrollViewer>(second, "Widget.memory")!.VerticalOffset - offset) < 2,
                "preview restores scroll before focus entry can mask a wrong offset");
            second.SetAutomaticFocusEnabled(true); second.Enter();
            await Wait(() => !second.HasPendingMemoryRestore && FocusManager.GetFocusedElement(XamlRoot) is FrameworkElement focused &&
                AutomationProperties.GetAutomationId(focused) == "Widget.ordinary.20");
            Check(!ReferenceEquals(oldButton, FindIn<Button>(second, "Widget.ordinary.20")),
                "ordinary focus restores to a newly created native control");
            Check(Math.Abs(FindIn<ScrollViewer>(second, "Widget.memory")!.VerticalOffset - offset) < 2,
                "ordinary native scroll restores its measured anchor after view eviction");
        }
        finally
        {
            host.Children.Remove(first); host.Children.Remove(second);
            await first.DisposeAsync(); await second.DisposeAsync();
        }
        static T? FindIn<T>(DependencyObject root, string id) where T : FrameworkElement
        {
            if (root is T element && AutomationProperties.GetAutomationId(element) == id) return element;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
                if (FindIn<T>(VisualTreeHelper.GetChild(root, i), id) is { } found) return found;
            return null;
        }
    }
}
