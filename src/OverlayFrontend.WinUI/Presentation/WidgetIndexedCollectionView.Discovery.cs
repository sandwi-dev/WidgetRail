using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private StackPanel? discoveryFooter;
    private TextBlock? discoveryStatus;
    private ProgressRing? discoveryLoading;
    private Button? discoveryRetry;

    private void UpdateDiscoveryFooter()
    {
        if (view is null) return;
        if (source?.Declaration.IndexedCollection?.Discovery is not { } state)
        { view.Footer = null; return; }
        if (discoveryFooter is null)
        {
            discoveryStatus = new() { TextWrapping = TextWrapping.Wrap };
            discoveryLoading = new() { Width = 24, Height = 24 };
            discoveryRetry = new() { Content = "Retry" };
            AutomationProperties.SetName(discoveryLoading, "Loading more results");
            discoveryRetry.Click += (_, _) => RetryDiscovery();
            discoveryFooter = new() { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new(8),
                Children = { discoveryLoading, discoveryStatus, discoveryRetry } };
        }
        AutomationProperties.SetAutomationId(discoveryRetry, "Widget." + source.Declaration.Id + ".RetryContinuation");
        discoveryStatus!.Text = source.ContinuationFailed ? "More results could not be loaded. Retry to continue." :
            source.ContinuationPaused ? "No new results in recent pages. Load more to continue." : state.Status switch
        {
            DiscoveredCollectionStatus.Loading => "Loading more results…",
            DiscoveredCollectionStatus.Failed => state.ErrorMessage ?? "More results could not be loaded.",
            DiscoveredCollectionStatus.LimitReached => "Result limit reached. Refine or restart the search to continue.",
            _ => state.HasMore ? "More results available" : source.Items.Count == 0 ? "No results" : string.Empty,
        };
        discoveryLoading!.IsActive = state.Status == DiscoveredCollectionStatus.Loading;
        discoveryLoading.Visibility = discoveryLoading.IsActive ? Visibility.Visible : Visibility.Collapsed;
        discoveryRetry!.Content = source.ContinuationPaused ? "Load more" : "Retry";
        discoveryRetry.Visibility = source.ContinuationFailed || source.ContinuationPaused || state.Status == DiscoveredCollectionStatus.Failed ? Visibility.Visible : Visibility.Collapsed;
        discoveryRetry.IsEnabled = inputActive;
        UpdateDiscoveryForeground();
        view.Footer = discoveryFooter;
    }

    private void UpdateDiscoveryForeground()
    {
        // The native ListView/Button templates have their own default foreground;
        // use the collection's resolved widget/theme foreground for host chrome.
        var foreground = view?.Foreground ?? Foreground;
        if (discoveryStatus is not null) discoveryStatus.Foreground = foreground;
        if (discoveryRetry is not null) discoveryRetry.Foreground = foreground;
        if (discoveryLoading is not null) discoveryLoading.Foreground = foreground;
    }

    private void RetryDiscovery()
    {
        if (!disposed && CanReceiveInput && source is { } current)
            _ = current.ContinueAsync(retry: true);
    }
    private bool ActivateDiscoveryFooter()
    {
        if (discoveryRetry is not { IsEnabled: true, Visibility: Visibility.Visible } retry || XamlRoot is null ||
            !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), retry)) return false;
        RetryDiscovery();
        return true;
    }
}
