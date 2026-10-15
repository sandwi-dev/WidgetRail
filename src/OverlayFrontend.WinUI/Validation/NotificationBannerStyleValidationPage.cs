using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Notifications;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetPresentationSession;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class NotificationBannerStyleValidationPage : Page, IDisposable
{
    private readonly List<NotificationBannerChrome> specimens = [];
    internal NotificationBannerStyleValidationPage(string resultPath)
    {
        Background = new SolidColorBrush(Color.FromArgb(255, 76, 84, 103));
        var grid = new Grid { Padding = new(24), RowSpacing = 8, ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Content = grid;
        var colors = new[] { Color.FromArgb(255, 39, 34, 49), Color.FromArgb(255, 246, 245, 248),
            Color.FromArgb(255, 39, 34, 49), Microsoft.UI.Colors.Black };
        for (var index = 0; index < 4; index++)
        {
            var high = index == 3;
            var text = index == 1 ? Color.FromArgb(255, 28, 27, 32) : Microsoft.UI.Colors.White;
            var palette = new ShellChromePalette(colors[index], colors[index], text, colors[index], text, text, text, 2, high);
            var card = new NotificationCard((_, _, _) => Task.FromException<WidgetPresentationPackageIcon>(new InvalidOperationException("Synthetic icon")));
            var entry = new NotificationEntry(index, index == 1 ? "Discord" : "WidgetRail", "fixture", index == 1 ? "https://fixture.invalid" : "", "",
                index == 1 ? "New message" : "Update available", index == 1 ? "Synthetic message preview for visual validation." : "A new version is ready. Open Notifications to review it.",
                this, new(() => true, () => { }, () => { }, () => { }, () => { }));
            card.ApplyBannerPalette(palette); card.Update(entry, null, 1);
            var chrome = new NotificationBannerChrome(card) { Width = 480 };
            chrome.Apply(palette, index == 2 ? 55 : 100);
            specimens.Add(chrome);
            var tile = new StackPanel { Children = {
                new TextBlock { Text = new[] { "Dark theme", "Light theme", "55% surface opacity", "High contrast" }[index],
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), Margin = new(30, 0, 0, 0) }, chrome } };
            Grid.SetRow(tile, index / 2); Grid.SetColumn(tile, index % 2); grid.Children.Add(tile);
        }
        Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(500);
                if (specimens.Any(item => !item.Depth.IsRealized || !item.Depth.UsesOuterMask || item.Depth.MaskPadding > NotificationBannerChrome.ShadowGutter))
                    throw new InvalidOperationException("Shadow resources or window gutter are incorrect.");
                if (specimens[3].Depth.NativeShadow?.Opacity != 0) throw new InvalidOperationException("High contrast retained a shadow.");
                var creates = specimens[0].Depth.NativeResourceCreates;
                specimens[0].Width = 481; await Task.Delay(50); specimens[0].Width = 480; await Task.Delay(50);
                if (specimens[0].Depth.NativeResourceCreates != creates) throw new InvalidOperationException("Resize recreated shadow resources.");
                await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = true,
                    checks = new[] { "Four theme/opacity specimens use the shared banner chrome", "Outer-only shadow fits the reserved transparent gutter",
                        "High contrast suppresses shadow", "Resize reuses compositor resources" } }));
            }
            catch (Exception error) { await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = false, error = error.ToString() })); }
        };
    }
    public void Dispose() { foreach (var item in specimens) item.Dispose(); }
}
