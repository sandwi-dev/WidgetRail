using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task MarketplaceAsync(string directory)
    {
        Exception? failure = null;
        Grid? previewRoot = null;
        try
        {
            var scale = XamlRoot.RasterizationScale;
            App.Window.AppWindow.Resize(new((int)(1080 * scale), (int)(900 * scale)));
            var compactOnly = Environment.GetCommandLineArgs().Contains("--catalogue-compact-only");
            var previewOnly = Environment.GetCommandLineArgs().Contains("--catalogue-preview-only");
            if (previewOnly)
            {
                previewRoot = new Grid { Height = 640 };
                host.Children.Remove(presenter); previewRoot.Children.Add(presenter); host.Children.Add(previewRoot);
                PresentationLayers.Attach(previewRoot);
            }
            var popupTheme = new NativePopupTheme(); popupTheme.Attach(presenter);
            presenter.Height = 640;
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var dispatched = new List<string>();
            presenter.DispatchActionAsync = action => { dispatched.Add(action.Action.ActionId); return Task.CompletedTask; };
            foreach (var width in new[] { 900, 560 })
            {
                presenter.Width = width;
                if (previewRoot is not null) previewRoot.Width = width;
                foreach (var name in previewOnly ? new[] { "browse-preview" } : compactOnly ? new[] { "browse" } : new[] { "browse", "details", "installed", "updates", "review", "completed",
                    "empty", "loading", "stale-error", "requirements-long", "requirements-absent" })
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, name + ".json")));
                    var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(document.RootElement.GetProperty("snapshot").GetRawText())) with { Sequence = ++sequence };
                    var styles = document.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
                    if (document.RootElement.TryGetProperty("shellStyles", out var shellStyles))
                        popupTheme.Update(shellStyles.Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!, AppearanceSettings.Default);
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
                        Check(cards.Max(card => card.ActualHeight) - cards.Min(card => card.ActualHeight) < 1,
                            $"Browse at {width}: all card edges align despite different description and setup lengths");
                        Check(cards.All(card => card.ActualHeight < 180), $"Browse at {width}: compact cards fit more content");
                        var header = Find<FrameworkElement>("Widget.marketplace.header")!;
                        var top = header.TransformToVisual(presenter).TransformPoint(new()).Y;
                        var content = scrolls.OrderByDescending(scroll => scroll.ScrollableHeight).First();
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
                    if (previewOnly)
                    {
                        await Wait(() => presenter.IsFocusPreviewOpen && presenter.FocusPreviewFrame?.Depth.IsRealized == true);
                        Check(presenter.FocusPreviewChrome?.IsHitTestVisible == false && dispatched.Count == 0,
                            $"Catalogue preview at {width}: passive content does not select or install");
                        Check(presenter.FocusPreviewChrome is Border { Padding.Left: 12 }, "Catalogue uses one host padding layer");
                    }
                    await CaptureAsync(Path.Combine(directory, $"{name}-{width}.png"));
                }
                if (!compactOnly && !previewOnly) await ScreenshotFlowAsync(width,options,dispatched);
            }
        }
        catch (Exception error) { failure = error; }
        await File.WriteAllTextAsync(Path.Combine(directory, "native-result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString(),
            scope = "Exported real widget actions plus native dispatch/focus/rendering; screenshot HTTPS identities are replaced with local PNGs before admission. Separate from the full real-Bridge workflow." }));
        if (failure is not null) throw failure;

        async Task ScreenshotFlowAsync(int width,JsonSerializerOptions options,List<string> dispatched)
        {
            const string firstUrl="https://raw.githubusercontent.com/sandwi-dev/WidgetRail/native-fixture/screenshot-1.png";
            const string secondUrl="https://raw.githubusercontent.com/sandwi-dev/WidgetRail/native-fixture/screenshot-2.png";
            var firstPng=await LocalPngAsync(32,18,240,40,30);
            var secondPng=await LocalPngAsync(48,20,20,60,230);
            var frames=new Dictionary<string,(ViewSnapshot Snapshot,Dictionary<string,BridgeNodeRenderStyles> Styles)>();
            foreach(var name in new[]{"browse","first","next","previous","back"})
            {
                using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"screenshots-"+name+".json")));
                var snapshot=SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(document.RootElement.GetProperty("snapshot").GetRawText()));
                var styles=document.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string,BridgeNodeRenderStyles>>(options)!;
                var image=Walk(snapshot.Root).SingleOrDefault(node=>node.Id=="marketplace.screenshot");
                if(name is "first" or "next" or "previous")
                    Check(image?.ImageSource==(name=="next"?secondUrl:firstUrl),$"Screenshot {name} retains the real widget's expected URL identity");
                // This substitution is confined to the validation boundary. Reject
                // every unexpected image rather than accidentally issuing HTTP.
                ViewNode Local(ViewNode node)
                {
                    var source=node.ImageSource;
                    if(source is not null)
                    {
                        if(node.Id!="marketplace.screenshot"||source!=firstUrl&&source!=secondUrl)
                            throw new InvalidDataException("Unexpected image in local Catalogue screenshot fixture.");
                        source=source==firstUrl?firstPng:secondPng;
                    }
                    return node with {ImageSource=source,Children=node.Children.Select(Local).ToArray()};
                }
                frames.Add(name,(snapshot with{Root=Local(snapshot.Root)},styles));
            }
            var cardId=frames["back"].Snapshot.InitialFocusId!;
            var cardAction=Walk(frames["browse"].Snapshot.Root).Single(node=>node.Id==cardId).ActionId!;
            Apply("browse");presenter.Enter(restoreNativeFocus:false);
            await Activate("Widget."+cardId,cardAction);
            Apply("first");await ImageReady(false);
            var imageView=Find<WidgetArtworkView>("Widget.marketplace.screenshot")!;
            await Activate("Widget.marketplace.screenshot.next","marketplace.screenshot.next");
            Apply("next");await ImageReady(true);
            Check(ReferenceEquals(imageView,Find<WidgetArtworkView>("Widget.marketplace.screenshot")),"Next updates the existing native image control");
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),Find<Button>("Widget.marketplace.screenshot.next")),"Next keeps focus on its screenshot control");
            await Activate("Widget.marketplace.screenshot.previous","marketplace.screenshot.previous");
            Apply("previous");await ImageReady(false);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),Find<Button>("Widget.marketplace.screenshot.previous")),"Previous keeps focus on its screenshot control");
            await Activate("Widget.marketplace.back","marketplace.back");
            Apply("back");
            await Wait(()=>FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused&&
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(focused)=="Widget."+cardId);
            Check(InsidePresenter(Find<Button>("Widget."+cardId)!)&&VisibleInScrolls(Find<Button>("Widget."+cardId)!),"Screenshot Back restores the exact visible catalogue card");

            void Apply(string name)
            {
                var frame=frames[name];var snapshot=frame.Snapshot with {Sequence=++sequence};
                var descriptor=new BridgeWidgetDescriptor{Id="marketplace",Name="Catalogue screenshot fixture",InstanceId=snapshot.WidgetInstanceId,
                    RuntimeGeneration="screenshots-"+width,PresentationGeneration="screenshots-"+width,Icon=WidgetGlyph.Connection,PackageContentDigest=""};
                presenter.Apply(new(new(descriptor.Id,descriptor.RuntimeGeneration,descriptor.PresentationGeneration,1,
                    snapshot.WidgetInstanceId,snapshot.Sequence,snapshot.ActiveInputScopeId),descriptor,snapshot,frame.Styles));
                presenter.UpdateLayout();
            }
            async Task Activate(string id,string expected)
            {
                await Wait(()=>Find<Button>(id)is {IsLoaded:true});var button=Find<Button>(id)!;
                button.StartBringIntoView();await Wait(()=>InsidePresenter(button)&&VisibleInScrolls(button));
                Check(button.Focus(FocusState.Keyboard),"Screenshot workflow accepts focus on "+id);
                var before=dispatched.Count;presenter.ActivateFocused();await Wait(()=>dispatched.Count==before+1);
                Check(dispatched[^1]==expected,"Screenshot native action dispatches "+expected);
            }
            async Task ImageReady(bool second)
            {
                await Wait(()=>Find<WidgetArtworkView>("Widget.marketplace.screenshot")?.Source is BitmapImage image&&
                    image.PixelWidth==(second?48:32)&&image.PixelHeight==(second?20:18));
                Check(Find<TextBlock>("Widget.marketplace.screenshot.count")?.Text==(second?"2 / 2":"1 / 2"),"Native screenshot count follows its decoded image");
                var view=Find<WidgetArtworkView>("Widget.marketplace.screenshot")!;
                var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(view);
                var pixels=(await bitmap.GetPixelsAsync()).ToArray();var offset=((bitmap.PixelHeight/2)*bitmap.PixelWidth+bitmap.PixelWidth/2)*4;
                Check(pixels.Length>offset+3&&(second?pixels[offset]>200&&pixels[offset+2]<80:pixels[offset+2]>200&&pixels[offset]<80),
                    second?"Second screenshot paints the local blue PNG":"First screenshot paints the local red PNG");
            }
            static bool VisibleInScrolls(FrameworkElement control)
            {
                for(DependencyObject? parent=VisualTreeHelper.GetParent(control);parent is not null;parent=VisualTreeHelper.GetParent(parent))
                    if(parent is ScrollViewer scroll)
                    {
                        var bounds=control.TransformToVisual(scroll).TransformBounds(new(0,0,control.ActualWidth,control.ActualHeight));
                        var viewport=WidgetScrollViewport.ContentBounds(scroll);
                        if(bounds.Top<viewport.Top-1||bounds.Bottom>viewport.Bottom+1||bounds.Left<viewport.Left-1||bounds.Right>viewport.Right+1)return false;
                    }
                return true;
            }
            static IEnumerable<ViewNode> Walk(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Walk));
        }

        static async Task<string> LocalPngAsync(uint width,uint height,byte red,byte green,byte blue)
        {
            var pixels=new byte[checked((int)(width*height*4))];
            for(var offset=0;offset<pixels.Length;offset+=4){pixels[offset]=blue;pixels[offset+1]=green;pixels[offset+2]=red;pixels[offset+3]=255;}
            using var stream=new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,width,height,96,96,pixels);
            await encoder.FlushAsync();stream.Seek(0);
            using var reader=new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);
            var bytes=new byte[(int)stream.Size];reader.ReadBytes(bytes);
            return "data:image/png;base64,"+Convert.ToBase64String(bytes);
        }

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
            await bitmap.RenderAsync((UIElement?)previewRoot ?? presenter);
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
