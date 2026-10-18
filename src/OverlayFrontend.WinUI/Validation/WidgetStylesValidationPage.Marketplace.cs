using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task MarketplaceAsync(string directory)
    {
        Exception? failure = null;
        try
        {
            var scale = XamlRoot.RasterizationScale;
            App.Window.AppWindow.Resize(new((int)(1080 * scale), (int)(900 * scale)));
            presenter.Height = 650;
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var dispatched = new List<string>();
            presenter.DispatchActionAsync = action => { dispatched.Add(action.Action.ActionId); return Task.CompletedTask; };
            foreach (var width in new[] { 900, 560 })
            {
                presenter.Width = width;
                foreach (var name in new[] { "browse", "details", "installed", "updates", "review", "completed",
                    "empty", "loading", "stale-error", "requirements-long", "requirements-absent" })
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, name + ".json")));
                    var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(document.RootElement.GetProperty("snapshot").GetRawText())) with { Sequence = ++sequence };
                    var styles = document.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
                    var descriptor = new BridgeWidgetDescriptor { Id = "marketplace", Name = "Marketplace fixture", InstanceId = snapshot.WidgetInstanceId,
                        RuntimeGeneration = name + width, PresentationGeneration = name + width, Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
                    presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                        snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                    presenter.UpdateLayout();
                    await Wait(() => Find<Button>("Widget." + snapshot.InitialFocusId)?.IsLoaded == true);
                    presenter.Enter(restoreNativeFocus: false);
                    await Task.Delay(120);
                    Check(FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused &&
                        Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(focused) == "Widget." + snapshot.InitialFocusId,
                        $"{name} at {width}: initial focus follows the widget declaration");
                    var scrolls = Descendants(presenter).OfType<ScrollViewer>().Where(scroll => scroll.IsLoaded && scroll.ActualHeight > 0).ToArray();
                    Check(scrolls.All(scroll => scroll.ExtentWidth <= scroll.ViewportWidth + 1), $"{name} at {width}: no horizontal content clipping");
                    if (name is "empty" or "loading" or "stale-error" or "requirements-long" or "requirements-absent")
                    {
                        var initial = Find<Button>("Widget." + snapshot.InitialFocusId)!;
                        Check(initial.IsEnabled && initial.IsTabStop && InsidePresenter(initial), $"{name} at {width}: declared focus is visible and reachable");
                        var header = Find<FrameworkElement>("Widget.marketplace.header")!;
                        Check(InsidePresenter(header), $"{name} at {width}: fixed navigation fits inside the widget");
                    }
                    if (name is "empty" or "loading")
                    {
                        var empty = Find<TextBlock>("Widget.marketplace.empty");
                        Check(empty is { ActualHeight: > 0, IsTextTrimmed: false } && InsidePresenter(empty),
                            $"{name} at {width}: state message is visible without clipping");
                        var refresh = Find<Button>("Widget.marketplace.refresh");
                        Check(refresh is { IsEnabled: true, IsTabStop: true } && InsidePresenter(refresh),
                            $"{name} at {width}: refresh remains reachable while state is displayed");
                    }
                    if (name == "stale-error")
                    {
                        Check(Descendants(presenter).OfType<Button>().Count(button =>
                            Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button).StartsWith("Widget.marketplace.item.") && button.ActualHeight > 0) == 2,
                            $"Stale error at {width}: cached cards remain available");
                        Check(Find<TextBlock>("Widget.marketplace.checked") is { ActualHeight: > 0 }, "Stale error keeps its last-check context");
                        var refresh = Find<Button>("Widget.marketplace.refresh")!;
                        refresh.Focus(FocusState.Programmatic);
                        var before = dispatched.Count;
                        presenter.ActivateFocused();
                        await Wait(() => dispatched.Count == before + 1);
                        Check(dispatched[^1] == "marketplace.refresh", "Stale error retry dispatches the normal bounded refresh action");
                        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), refresh), "Retry dispatch does not displace focus");
                        var status = Find<TextBlock>("Widget.marketplace.status")!;
                        var content = scrolls.OrderByDescending(scroll => scroll.ScrollableHeight).First();
                        content.ChangeView(null, content.ScrollableHeight, null, true);
                        await Task.Delay(100);
                        Check(status.ActualHeight > 0 && !status.IsTextTrimmed && InsidePresenter(status),
                            $"Stale error at {width}: full recovery message is reachable by scrolling");
                    }
                    if (name == "requirements-long")
                    {
                        var requirements = Enumerable.Range(0, 4).Select(index => Find<TextBlock>("Widget.marketplace.requirement." + index)).ToArray();
                        Check(requirements.All(text => text is { ActualHeight: > 0, IsTextTrimmed: false } && text.TextWrapping != TextWrapping.NoWrap),
                            $"Long prerequisites at {width}: all requirements wrap without truncation");
                        var content = scrolls.OrderByDescending(scroll => scroll.ScrollableHeight).First();
                        if (width == 560) Check(content.ScrollableHeight > 0, "Constrained prerequisites exercise overflowing content");
                        var opener = FocusManager.GetFocusedElement(XamlRoot);
                        content.ChangeView(null, content.ScrollableHeight, null, true);
                        await Task.Delay(100);
                        Check(InsidePresenter(requirements[^1]!), $"Long prerequisites at {width}: final requirement is reachable");
                        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), opener), "Scrolling prerequisites preserves action focus");
                        Check(InsidePresenter(Find<Button>("Widget.marketplace.back")!) && InsidePresenter(Find<Button>("Widget.marketplace.primary")!),
                            "Long prerequisites keep Back and Download visible");
                    }
                    if (name == "requirements-absent")
                    {
                        Check(Find<FrameworkElement>("Widget.marketplace.requirements") is null, "Absent prerequisites leave no empty panel");
                        Check(InsidePresenter(Find<TextBlock>("Widget.marketplace.description")!), "Description stays visible without prerequisites");
                    }
                    if (name == "browse")
                    {
                        var cards = Descendants(presenter).OfType<Button>().Where(button =>
                            Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button).StartsWith("Widget.marketplace.item.") &&
                            button.ActualHeight > 0).ToArray();
                        Check(cards.Length >= 2, "Browse exposes native interactive cards");
                        if (width == 900) Check(Math.Abs(cards[0].TransformToVisual(presenter).TransformPoint(new()).Y -
                            cards[1].TransformToVisual(presenter).TransformPoint(new()).Y) < 1, "Different-height cards share the same top edge");
                        var header = Find<FrameworkElement>("Widget.marketplace.header")!;
                        var top = header.TransformToVisual(presenter).TransformPoint(new()).Y;
                        var content = scrolls.First(scroll => scroll.ScrollableHeight > 0);
                        content.ChangeView(null, content.ScrollableHeight, null, true);
                        await Task.Delay(100);
                        Check(Math.Abs(header.TransformToVisual(presenter).TransformPoint(new()).Y - top) < 1,
                            "Scrolling cards keeps Catalogue navigation fixed");
                        content.ChangeView(null, 0, null, true);
                        await Task.Delay(100);
                    }
                    if (name == "review")
                    {
                        Check(snapshot.InitialFocusId == "marketplace.batch.review.cancel", "Aggregate review initially focuses Cancel");
                        var cancel = Find<Button>("Widget.marketplace.batch.review.cancel")!;
                        Check(cancel.IsEnabled, "Cancel stays available in the aggregate trust review");
                        var before = dispatched.Count;
                        presenter.ActivateFocused();
                        await Wait(() => dispatched.Count == before + 1);
                        Check(dispatched[^1] == "marketplace.review.cancel", "Native review Cancel dispatches cancellation, never approval");
                        var install = Find<Button>("Widget.marketplace.batch.review.install");
                        Check(install is { IsEnabled: true, IsTabStop: true }, "Aggregate approval remains an explicit reachable control");
                    }
                    await CaptureAsync(Path.Combine(directory, $"{name}-{width}.png"));
                }
            }
        }
        catch (Exception error) { failure = error; }
        await File.WriteAllTextAsync(Path.Combine(directory, "native-result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;

        bool InsidePresenter(FrameworkElement element)
        {
            var origin = element.TransformToVisual(presenter).TransformPoint(new());
            return element.ActualWidth > 0 && element.ActualHeight > 0 && origin.X >= -1 && origin.Y >= -1 &&
                origin.X + element.ActualWidth <= presenter.ActualWidth + 1 && origin.Y + element.ActualHeight <= presenter.ActualHeight + 1;
        }

        async Task CaptureAsync(string path)
        {
            // Synthetic XAML pixels only, including transparent regions.
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(presenter);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync(); stream.Seek(0);
            using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);
        }
    }
}
