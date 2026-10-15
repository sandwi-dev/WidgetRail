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
                foreach (var name in new[] { "browse", "installed", "updates", "review", "completed" })
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
