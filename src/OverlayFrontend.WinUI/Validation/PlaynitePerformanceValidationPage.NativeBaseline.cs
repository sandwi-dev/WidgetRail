using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PlaynitePerformanceValidationPage
{
    // A diagnostic control baseline, not a substitute for the production consumer
    // checks: no Bridge, widget renderer, incremental publication or artwork.
    private async Task RunNativeGridBaselineAsync()
    {
        var view = new GridView
        {
            ItemsSource = Enumerable.Range(0,183).Select(index => $"Fixture Game {index:D5}").ToArray(),
            ItemsPanel = (ItemsPanelTemplate)XamlReader.Load("""
                <ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <ItemsWrapGrid Orientation="Horizontal" CacheLength="4" />
                </ItemsPanelTemplate>
                """),
            ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <Border Width="170" Height="240" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}">
                        <TextBlock Text="{Binding}" Margin="8" VerticalAlignment="Bottom" />
                    </Border>
                </DataTemplate>
                """)
        };
        Content = view;
        await Until(() => view.ActualHeight > 0 && Descendants(view).OfType<ScrollViewer>().Any());
        await Task.Delay(3000,lifetime.Token);
        await Until(() => foreground.IsOverlayForeground);
        var scroll = Descendants(view).OfType<ScrollViewer>().First();
        CompositionTarget.Rendering += Rendering;
        for (var pass = 0; pass < 3; pass++)
        {
            phase = "scroll-"+pass;
            frames.Clear(); ticks.Clear(); timerWaits.Clear(); continuationWaits.Clear(); lastFrame=0; scrolling=true;
            var duration=Stopwatch.StartNew(); var previous=Stopwatch.GetTimestamp();
            while (duration.Elapsed.TotalSeconds < 20)
            {
                if (!foreground.IsOverlayForeground) throw new InvalidOperationException("Native baseline lost foreground.");
                var now=Stopwatch.GetTimestamp();
                ticks.Add(Stopwatch.GetElapsedTime(previous,now).TotalMilliseconds); previous=now;
                var fraction=duration.Elapsed.TotalSeconds/20;
                scroll.ChangeView(null,scroll.ScrollableHeight*(pass%2==0?fraction:1-fraction),null,true);
                var delayStarted=Stopwatch.GetTimestamp();
                var timerReady=await MotionTickAsync(lifetime.Token);
                timerWaits.Add(Stopwatch.GetElapsedTime(delayStarted,timerReady).TotalMilliseconds);
                continuationWaits.Add(Stopwatch.GetElapsedTime(timerReady).TotalMilliseconds);
            }
            scrolling=false;
            samples.Add(new {phase, nativeBaseline=true, isForeground=foreground.IsOverlayForeground,
                allocated=GC.GetTotalAllocatedBytes(),gen2=GC.CollectionCount(2),offset=scroll.VerticalOffset});
            await File.WriteAllTextAsync(Path.Combine(output,phase+".json"),
                JsonSerializer.Serialize(new {frames,ticks,timerWaits,continuationWaits}),lifetime.Token);
            await Task.Delay(3000,lifetime.Token);
        }
        await presenter.DisposeAsync();
    }
}
