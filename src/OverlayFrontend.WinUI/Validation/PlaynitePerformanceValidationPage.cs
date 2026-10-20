using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.OverlayPlatformClient;
using PresentationSession=WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

// Real bridge, application, indexed leases, native GridView and artwork decoder.
// Only the upstream provider is synthetic. No launches or metadata writes.
internal sealed partial class PlaynitePerformanceValidationPage : Page
{
    private const string WidgetId="widgetrail.samples.playnite-library";
    private readonly WidgetViewPresenter presenter=new();
    private readonly CancellationTokenSource lifetime=new();
    private readonly List<object> samples=[];
    private readonly List<double> frames=[];
    private readonly List<double> ticks=[];
    private readonly List<string> navigationChecks=[];
    private readonly string pipe;
    private readonly string output;
    private PresentationSession? session;
    private Exception? failure;
    private long publication;
    private long lastFrame;
    private bool scrolling;
    private string phase="startup";
    private readonly Stopwatch elapsed=Stopwatch.StartNew();

    internal PlaynitePerformanceValidationPage(string pipe,string output)
    {
        this.pipe=pipe;this.output=output;
        if (Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--playnite-cache-length") is { } cacheText)
        {
            var cache = int.Parse(cacheText, System.Globalization.CultureInfo.InvariantCulture);
            if (cache is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(cacheText));
            // Replace the template BEFORE any collection is created. Changing an
            // already-realized panel cannot measure its cold allocation budget.
            Application.Current.Resources["WidgetIndexedGridPanel"] =
                Microsoft.UI.Xaml.Markup.XamlReader.Load(
                    "<ItemsPanelTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                    $"<ItemsWrapGrid Orientation=\"Horizontal\" GroupHeaderPlacement=\"Top\" CacheLength=\"{cache}\" />" +
                    "</ItemsPanelTemplate>");
        }
        var root=new Grid();root.Children.Add(presenter);Content=root;
        PresentationLayers.Attach(root);
        presenter.Failed=error=>failure??=error;
        Loaded+=async(_,_)=>await RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            Directory.CreateDirectory(output);
            var scale=XamlRoot.RasterizationScale;
            App.Window.AppWindow.Resize(new((int)(1360*scale),(int)(940*scale)));
            var connected=await PresentationSession.ConnectAsync(pipe,cancellationToken:lifetime.Token);
            session=connected;
            presenter.Session=session;
            presenter.DispatchActionAsync=request=>connected.SendActionAsync(request.Displayed.Authority,request.Action,lifetime.Token);
            session.PresentationChanged+=Changed;
            var admission=Stopwatch.StartNew();
            while(true)
            {
                var catalog=await session.ListWidgetsAsync(lifetime.Token);
                if(catalog.Widgets.Any(widget=>widget.Id==WidgetId))break;
                if(catalog.IsComplete||admission.Elapsed.TotalSeconds>30)
                    throw new InvalidDataException("Performance widget was not admitted to the isolated catalog.");
                await Task.Delay(200,lifetime.Token);
            }
            await session.EstablishPresentationAsync(session.GetTarget(WidgetId),WidgetLifecycleState.Interactive,lifetime.Token);
            await Until(()=>session.GetState(WidgetId)?.LastGood?.Snapshot is {} snapshot&&Nodes(snapshot.Root).Any(n=>n.ActionId=="playnite-library.browse.open"));
            var frame=session.GetState(WidgetId)!.LastGood!;
            var browse=Nodes(frame.Snapshot.Root).First(n=>n.ActionId=="playnite-library.browse.open");
            await session.SendActionAsync(frame.Authority,new(browse.ActionId!,browse.Id,InputScopeId:frame.Snapshot.ActiveInputScopeId),lifetime.Token);
            await Until(()=>View() is GridView {Items.Count:>0,ActualHeight:>0}&&Descendants(presenter).OfType<WidgetArtworkView>().Any());
            await Task.Delay(3000,lifetime.Token);
            Sample("ready");
            if (Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--playnite-wheel-comparison") is { } wheelMode)
            {
                await CompareWheelAsync(wheelMode);
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--playnite-scroll-direction"))
            {
                await CheckScrollDirectionAsync();
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--playnite-navigation-only"))
            {
                await CheckNavigationAsync();
                return;
            }
            CompositionTarget.Rendering+=Rendering;
            for(var pass=0;pass<3;pass++)
            {
                phase="scroll-"+pass;
                frames.Clear();ticks.Clear();lastFrame=0;scrolling=true;
                var scroll=Descendants(View()!).OfType<ScrollViewer>().First();
                var duration=Stopwatch.StartNew();var previous=Stopwatch.GetTimestamp();var nextSample=0d;
                while(duration.Elapsed.TotalSeconds<20)
                {
                    if(failure is not null)throw failure;
                    var now=Stopwatch.GetTimestamp();
                    ticks.Add(Stopwatch.GetElapsedTime(previous,now).TotalMilliseconds);previous=now;
                    // Continuous fast travel and reversal; never jump to a single endpoint.
                    var fraction=duration.Elapsed.TotalSeconds/20;
                    scroll.ChangeView(null,scroll.ScrollableHeight*(pass%2==0?fraction:1-fraction),null,true);
                    if(duration.Elapsed.TotalSeconds>=nextSample){Sample(phase);nextSample+=1;}
                    await Task.Delay(16,lifetime.Token);
                }
                scrolling=false;
                Sample(phase+"-end");
                await File.WriteAllTextAsync(Path.Combine(output,phase+".json"),JsonSerializer.Serialize(new{frames,ticks}),lifetime.Token);
                await Task.Delay(3000,lifetime.Token);Sample(phase+"-settled");
            }
            var checkNavigation=Environment.GetCommandLineArgs().Contains("--playnite-performance-navigation");
            if(checkNavigation)await CheckNavigationAsync();
            if(Environment.GetCommandLineArgs().Contains("--playnite-performance-heap-pause"))
            {
                Sample("heap-diagnostic");
                await Until(()=>File.Exists(Path.Combine(output,"heap-complete")));
                Sample("after-diagnostic-gc");
            }
            phase="background";
            await presenter.SetPresentationActiveAsync(false);
            await session.SetLifecycleAsync(session.GetTarget(WidgetId),WidgetLifecycleState.Background,lifetime.Token);
            await Task.Delay(5000,lifetime.Token);Sample(phase);
            if(checkNavigation)
            {
                await presenter.SetPresentationActiveAsync(true);
                await session.EstablishPresentationAsync(session.GetTarget(WidgetId),WidgetLifecycleState.Interactive,lifetime.Token);
                await Until(()=>View() is GridView {Items.Count:>0});
                await NativeIndexedFocusProbe.RunAsync(View()!,_=>presenter.MoveFocus(FocusNavigationDirection.Right),
                    steps:1,settleMilliseconds:35,cancellationToken:lifetime.Token);
                await Until(()=>FocusedIndex(View()!)==1);
                navigationChecks.Add("Background/reopen reacquires rows and resumes directional focus");
                Sample("reopened");
            }
            phase="disposed";
            session.PresentationChanged-=Changed;
            await presenter.DisposeAsync();
            await session.DisposeAsync();session=null;
            await Task.Delay(3000,lifetime.Token);Sample(phase);
        }
        catch(Exception error){failure??=error;}
        finally
        {
            scrolling=false;CompositionTarget.Rendering-=Rendering;
            if(session is not null)
            {
                session.PresentationChanged-=Changed;
                await presenter.DisposeAsync();await session.DisposeAsync();session=null;
            }
            await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=failure is null,error=failure?.ToString(),navigationChecks,samples}));
        }
    }
    private void Changed(object? sender,WidgetPresentationChangedEventArgs args)=>DispatcherQueue.TryEnqueue(()=>
    {
        if(args.State.PublicationRevision<=publication)return;
        publication=args.State.PublicationRevision;
        try
        {
            if(args.State.Failure is {} error)throw new InvalidOperationException(error.Message);
            if(args.State.LastGood is {} frame)presenter.Apply(frame);
        }
        catch(Exception error){failure??=error;}
    });
    private void Rendering(object? sender,object args)
    {
        if(!scrolling)return;
        var now=Stopwatch.GetTimestamp();
        if(lastFrame!=0)frames.Add(Stopwatch.GetElapsedTime(lastFrame,now).TotalMilliseconds);
        lastFrame=now;
    }
    private void Sample(string name)
    {
        using var process=Process.GetCurrentProcess();
        var nodes=Descendants(presenter).ToArray();
        var view=nodes.OfType<GridView>().FirstOrDefault();
        var scroll=view is null?null:Descendants(view).OfType<ScrollViewer>().FirstOrDefault();
        var gc=GC.GetGCMemoryInfo();
        var bitmaps=nodes.OfType<WidgetArtworkView>().Select(n=>n.Source).OfType<BitmapImage>().Distinct().ToArray();
        var visibleTiles=scroll is null?[]:nodes.OfType<GridViewItem>().Where(tile=>
        {
            var bounds=tile.TransformToVisual(scroll).TransformBounds(new(0,0,tile.ActualWidth,tile.ActualHeight));
            var viewport=WidgetScrollViewport.ContentBounds(scroll);
            return bounds.Width>0&&bounds.Height>0&&bounds.Right>viewport.Left&&bounds.Left<viewport.Right&&
                bounds.Bottom>viewport.Top&&bounds.Top<viewport.Bottom;
        }).ToArray();
        samples.Add(new{phase=name,seconds=elapsed.Elapsed.TotalSeconds,workingSet=process.WorkingSet64,privateBytes=process.PrivateMemorySize64,
            managedBytes=GC.GetTotalMemory(false),heapSize=gc.HeapSizeBytes,committed=gc.TotalCommittedBytes,fragmented=gc.FragmentedBytes,
            allocated=GC.GetTotalAllocatedBytes(),gen0=GC.CollectionCount(0),gen1=GC.CollectionCount(1),gen2=GC.CollectionCount(2),
            handles=process.HandleCount,count=view?.Items.Count,realized=nodes.Count(n=>n is GridViewItem),
            images=nodes.OfType<WidgetArtworkView>().Count(n=>n.Source is not null),offset=scroll?.VerticalOffset,
            visibleTiles=visibleTiles.Length,visibleTilesWithArtwork=visibleTiles.Count(tile=>Descendants(tile).OfType<WidgetArtworkView>().Any(image=>image.Source is not null)),
            extent=scroll?.ScrollableHeight,decodedResidentBytes=bitmaps.Sum(b=>(long)b.DecodePixelWidth*b.DecodePixelHeight*4),
            collections=nodes.OfType<WidgetIndexedCollectionView>().Select(n=>n.PerformanceStatistics()).ToArray(),
            artwork=NativeArtworkCounters.Snapshot()});
        File.WriteAllText(Path.Combine(output,"progress.json"),JsonSerializer.Serialize(new{phase=name,seconds=elapsed.Elapsed.TotalSeconds,pid=Environment.ProcessId}));
    }
    private ListViewBase? View()=>Descendants(presenter).OfType<ListViewBase>().FirstOrDefault();
    private async Task CheckNavigationAsync()
    {
        Sample("navigation");
        var view=View()!;
        var count=view.Items.Count;
        var columns=(view.ItemsPanelRoot as ItemsWrapGrid)?.MaximumRowsOrColumns??1;
        var steps=Math.Min(40,(count-1)/columns);
        if(steps<1)throw new InvalidOperationException("Navigation checks require at least two rows.");
        await NativeIndexedFocusProbe.RunAsync(view,_=>presenter.MoveFocus(FocusNavigationDirection.Down),
            steps:steps,settleMilliseconds:35,cancellationToken:lifetime.Token);
        await Until(()=>FocusedIndex(view)==steps*columns);
        navigationChecks.Add("Rapid controller movement crosses materialization boundaries without lost steps");
        await NativeIndexedFocusProbe.RunAsync(view,_=>presenter.MoveFocus(FocusNavigationDirection.Up),
            startIndex:steps*columns,steps:steps,settleMilliseconds:35,cancellationToken:lifetime.Token,direction:NavigationDirection.Up);
        await Until(()=>FocusedIndex(view)==0);
        navigationChecks.Add("Reversing controller movement returns to the original item");
        foreach(var query in new[]{"Fixture Game","Fixture Game 00001",""})
        {
            var priorView = View()!;
            var priorItems = priorView.ItemsSource;
            var priorScroll = Descendants(priorView).OfType<ScrollViewer>().First();
            if (priorView.Items.Count > 1)
            {
                presenter.ScrollBy(0, 900);
                await Until(() => priorScroll.VerticalOffset > 20);
            }
            var frame=session!.GetState(WidgetId)!.LastGood!;
            await session.SendActionAsync(frame.Authority,new("playnite-library.search.commit","playnite-library.search",
                InputScopeId:frame.Snapshot.ActiveInputScopeId){CommittedText=query},lifetime.Token);
            await Until(()=>View()?.Items.Count==(query=="Fixture Game 00001"?1:count) &&
                !ReferenceEquals(View()!.ItemsSource, priorItems));
            await Task.Delay(800, lifetime.Token);
            var currentScroll = Descendants(View()!).OfType<ScrollViewer>().First();
            var replacedOffset = currentScroll.VerticalOffset;
            await Until(presenter.SettleScrollFocus);
            await Task.Delay(250, lifetime.Token);
            if (Math.Abs(currentScroll.VerticalOffset-replacedOffset)>1)
                throw new InvalidOperationException("Retired query scrolling moved the replacement collection.");
        }
        navigationChecks.Add("Search replacement and restoration during native motion retire old ranges without delayed scrolling");
    }
    private static int? FocusedIndex(ListViewBase view)
    {
        for(var current=FocusManager.GetFocusedElement(view.XamlRoot) as DependencyObject;current is not null;
            current=VisualTreeHelper.GetParent(current))
            if(current is Control control&&view.IndexFromContainer(control) is var index&&index>=0)return index;
        return null;
    }
    private async Task Until(Func<bool> condition)
    {
        var timer=Stopwatch.StartNew();
        while(!condition())
        {
            if(failure is not null)throw failure;
            if(timer.Elapsed.TotalSeconds>30)throw new TimeoutException("Performance fixture did not become ready.");
            await Task.Delay(25,lifetime.Token);
        }
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
}
