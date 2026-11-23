using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableCatalogueReturnValidation(string resultPath)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            Exception? failure = null;
            try
            {
                if (startup is not null) await startup;
                await WaitFor(() => introductionLoaded && foreground && !switching);
                LeaveIntroduction();
                await SelectAsync("marketplace");
                await WaitFor(() => Cards().Length >= 8 && FindIntroductionTarget(surface!, "marketplace.refresh") is Button { IsEnabled: true });
                var cardId = AutomationProperties.GetAutomationId(Cards()[^2])["Widget.".Length..];
                for (var attempt = 0; attempt < 4; ++attempt)
                {
                    var scroll = (ScrollViewer)FindIntroductionTarget(surface!, "marketplace.content")!;
                    // Establish a settled viewport rather than sampling WinUI's
                    // asynchronous BringIntoView animation halfway through.
                    scroll.ChangeView(null, scroll.ScrollableHeight, null, disableAnimation: true);
                    await WaitFor(() => Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1);
                    Check(surface!.FocusHostTarget(cardId), "Card accepts focus before opening " + attempt);
                    await SettleViewport(scroll);
                    var offset = scroll.VerticalOffset;
                    Check(offset > 100, "List is scrolled down before opening " + attempt);
                    await Press(ControllerButton.A);
                    await WaitFor(() => FindIntroductionTarget(surface!, "marketplace.back") is Button { IsLoaded: true });
                    await Task.Delay(250);
                    // Exercise both the fixed Back button and B while reading
                    // the detail's focusable scroll container.
                    Check(surface.FocusHostTarget(attempt % 2 == 0 ? "marketplace.back" : "marketplace.content"), "Detail focus is usable " + attempt);
                    await Press(attempt % 2 == 0 ? ControllerButton.A : ControllerButton.B);
                    await WaitFor(() => FindIntroductionTarget(surface!, cardId) is Button { IsLoaded: true });
                    await Task.Delay(450);
                    var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                    Check(focused is Button && AutomationProperties.GetAutomationId(focused) == "Widget." + cardId,
                        "Back restores the selected card, not its container " + attempt);
                    var restored = (ScrollViewer)FindIntroductionTarget(surface!, "marketplace.content")!;
                    await SettleViewport(restored);
                    Check(Math.Abs(restored.VerticalOffset - offset) < 2, $"Back restores the list viewport {attempt}: {offset:F2} → {restored.VerticalOffset:F2}, maximum {restored.ScrollableHeight:F2}");
                }
            }
            catch (Exception error) { failure = error; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString(), focus = surface?.FocusDiagnostics() }, new JsonSerializerOptions { WriteIndented = true }));

            Button[] Cards() => Descendants(surface!).OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button).StartsWith("Widget.marketplace.item.", StringComparison.Ordinal)).ToArray();
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
            async Task Press(ControllerButton button)
            {
                await RouteButtonAsync(button, ControllerEventPhase.Pressed);
                await RouteButtonAsync(button, ControllerEventPhase.Released);
            }
            static IEnumerable<DependencyObject> Descendants(DependencyObject root)
            {
                yield return root;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
                    foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
            }
            static async Task WaitFor(Func<bool> predicate)
            {
                var deadline = Environment.TickCount64 + 15000;
                while (!predicate() && Environment.TickCount64 < deadline) await Task.Delay(30);
                if (!predicate()) throw new TimeoutException("Catalogue navigation did not settle.");
            }
            static async Task SettleViewport(ScrollViewer scroll)
            {
                var last = (scroll.VerticalOffset, scroll.ExtentHeight, scroll.ViewportHeight);
                var stableSince = Environment.TickCount64;
                var deadline = stableSince + 6000;
                while (Environment.TickCount64 < deadline)
                {
                    await Task.Delay(50);
                    var current = (scroll.VerticalOffset, scroll.ExtentHeight, scroll.ViewportHeight);
                    if (current != last) { last = current; stableSince = Environment.TickCount64; }
                    else if (Environment.TickCount64 - stableSince >= 500) return;
                }
                throw new TimeoutException("Catalogue viewport kept moving.");
            }
        };
    }
}
