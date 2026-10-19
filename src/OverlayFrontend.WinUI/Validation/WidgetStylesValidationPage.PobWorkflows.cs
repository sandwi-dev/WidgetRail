using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task PobWorkflowsAsync(string directory)
    {
        Exception? failure=null;
        var root=new Grid();var page=new WidgetViewPresenter();root.Children.Add(page);PresentationLayers.Attach(root);
        presenter.Visibility=Visibility.Collapsed;host.Children.Add(root);
        var popupTheme=new NativePopupTheme();popupTheme.Attach(page);
        var catalog=new ThemeCatalog(new PlatformSettingsPaths(directory));
        var package=WrssParser.Parse(File.ReadAllText(Path.Combine(directory,"pob.wrss")),"pob.wrss");
        var scale=XamlRoot.RasterizationScale;
        App.Window.AppWindow.Resize(new((int)(1480*scale),(int)(1100*scale)));
        page.ApplyAppearance(AppearanceSettings.Default with{Motion=MotionPreference.Reduced},false);
        page.Failed=error=>failure??=error;
        long focusSerial=10000;
        var densityMetrics=new List<object>();
        try
        {
            var disclosures=Environment.GetCommandLineArgs().Contains("--pob-disclosures-only");
            var density=Environment.GetCommandLineArgs().Contains("--pob-density-only");
            var headerOnly=Environment.GetCommandLineArgs().Contains("--pob-header-only");
            var editorOnly=Environment.GetCommandLineArgs().Contains("--pob-editor-only");
            foreach(var themeId in new[]{ThemeIdentity.BuiltInMonoInk,ThemeIdentity.BuiltInMineralLight,"widgetrail.builtin.velvet-dusk","high-contrast"})
            foreach(var game in new[]{1,2})
            foreach(var scenario in editorOnly?new[]{"trade-options","item-name"}:headerOnly?new[]{"header"}:density?new[]{"configuration-density"}:disclosures?new[]{"calculation-expanded","calculation-collapsed","configuration-collapsed","configuration-expanded"}:new[]{"search","skill-workspace","build-comparison","search-target-equipment","search-target-gem","search-target-configuration"})
            foreach(var size in new[]{(Width:1320d,Height:900d),(Width:820d,Height:600d)})
            {
                root.Width=page.Width=size.Width;root.Height=page.Height=size.Height;
                var contrast=themeId=="high-contrast";
                WidgetViewPresenter.SetHighContrastStyleOverride(contrast);
                page.ApplyAppearance(AppearanceSettings.Default with{Motion=MotionPreference.Reduced,TextScale=contrast?(headerOnly?1.5:1.25):1},false);
                var theme=ThemeLayerCompiler.Compile(catalog.BuiltInDefault.Package,new((WrssDocument[])[package.Document],[]),catalog.Load(contrast?ThemeIdentity.BuiltInMonoInk:themeId,"1.0.0").Package).Theme!;
                var snapshot=SnapshotJson.Deserialize(File.ReadAllBytes(Path.Combine(directory,$"poe{game}-{scenario}.json")));
                ViewNode[] rows=scenario switch
                {
                    "search"=>JsonSerializer.Deserialize<ViewNode[]>(File.ReadAllBytes(Path.Combine(directory,$"poe{game}-search-rows.json")))!,
                    "build-comparison"=>File.Exists(Path.Combine(directory,$"poe{game}-build-comparison-rows.json"))
                        ?JsonSerializer.Deserialize<ViewNode[]>(File.ReadAllBytes(Path.Combine(directory,$"poe{game}-build-comparison-rows.json")))!
                        :(ViewNode[])[JsonSerializer.Deserialize<ViewNode>(File.ReadAllBytes(Path.Combine(directory,$"poe{game}-build-comparison-row.json")))!],
                    _=>[],
                };
                var collection=scenario=="search"?"pob.unified.results":"pob.build-comparison.rows";
                var disclosureRows=scenario.StartsWith("calculation-",StringComparison.Ordinal)
                    ?JsonSerializer.Deserialize<Dictionary<string,ViewNode[]>>(File.ReadAllBytes(Path.Combine(directory,$"poe{game}-calculation-disclosure-rows.json")))!:new();
                ViewNode Materialize(ViewNode node)=>node.Kind==ViewNodeKind.IndexedCollection&&disclosureRows.TryGetValue(node.Id,out var detailRows)
                    ?new ViewNode{Id=node.Id,Kind=ViewNodeKind.Scroll,ScrollAxis=ScrollAxis.Vertical,StyleClasses=node.StyleClasses,InitialChildFocusId=detailRows.FirstOrDefault()?.Id,Children=detailRows}
                    :node.Kind==ViewNodeKind.IndexedCollection&&node.Id==collection
                    ?new ViewNode{Id=node.Id,Kind=ViewNodeKind.Scroll,ScrollAxis=ScrollAxis.Vertical,StyleClasses=node.StyleClasses,
                        GridCell=node.GridCell,InitialChildFocusId=rows.FirstOrDefault()?.Id,Children=rows}
                    :node with{Children=node.Children.Select(Materialize).ToArray()};
                snapshot=snapshot with{Root=Materialize(snapshot.Root),WidgetInstanceId="pob-workflow-fixture"};
                // The single resident row stands in only for native layout. Real SDK
                // leases, build edits and providers are covered by engine workflow tests.
                if(snapshot.FocusGroupEntryRequest is {} request)
                    snapshot=snapshot with{FocusGroupEntryRequest=request with{RequestId=++focusSerial,IndexedItem=null}};
                var runtime="pob-workflow-"+game+"-"+scenario+"-"+themeId+"-"+size.Width;
                var dispatched=new List<string>();page.DispatchActionAsync=request=>{dispatched.Add(request.Action.ActionId);return Task.CompletedTask;};
                Publish();
                var declared=Flatten(snapshot.Root).ToArray();
                var target=snapshot.FocusGroupEntryRequest is {} focus
                    ?declared.Single(n=>n.Id==focus.GroupId).InitialChildFocusId:snapshot.InitialFocusId;
                if(target is not null)
                {
                    await Wait(()=>Find<Control>("Widget."+target,root) is {IsLoaded:true});
                    page.Enter(restoreNativeFocus:false);
                    await Wait(()=>FocusManager.GetFocusedElement(XamlRoot) is DependencyObject current&&AutomationProperties.GetAutomationId(current)=="Widget."+target);
                    Check(true,$"PoE{game} {scenario} establishes its declared native focus target at {size.Width} DIP");
                }
                if(snapshot.Root.Kind==ViewNodeKind.ModalLayer)
                {
                    var panel=Find<FrameworkElement>("Widget."+snapshot.Root.Children[1].Id,root)!;
                    await Wait(()=>panel.ActualWidth>0&&panel.ActualHeight>0);
                    var bounds=panel.TransformToVisual(page).TransformBounds(new(0,0,panel.ActualWidth,panel.ActualHeight));
                    Check(bounds.Left>=15&&bounds.Top>=15&&bounds.Right<=page.ActualWidth-15&&bounds.Bottom<=page.ActualHeight-15,"Search modal stays inside the widget");
                    Check(Descendants(panel).OfType<ScrollViewer>().All(s=>s.ExtentWidth<=s.ViewportWidth+1),"Search modal avoids horizontal overflow");
                    Check(rows.Length>0&&Find<Button>("Widget."+rows[0].Id,root) is not null,"Exported search results materialize as native actionable rows");
                }
                if(headerOnly)
                {
                    var header=Find<FrameworkElement>("Widget.pob.header",root)!;
                    var identity=Find<FrameworkElement>("Widget.pob.identity",root)!;
                    var tools=Find<FrameworkElement>("Widget.pob.buildtools",root)!;
                    var ib=identity.TransformToVisual(header).TransformBounds(new(0,0,identity.ActualWidth,identity.ActualHeight));
                    var tb=tools.TransformToVisual(header).TransformBounds(new(0,0,tools.ActualWidth,tools.ActualHeight));
                    Check(size.Width<1040?tb.Top>=ib.Bottom:tb.Left>=ib.Right,"Header reflows identity and toolbar without overlapping");
                    foreach(var id in new[]{"pob.loadout","pob.unified-search","pob.menu","pob.undo","pob.redo"})
                    {
                        if(Find<Control>("Widget."+id,root) is not {} control)continue;
                        var bounds=control.TransformToVisual(header).TransformBounds(new(0,0,control.ActualWidth,control.ActualHeight));
                        Check(bounds.Left>=-1&&bounds.Right<=header.ActualWidth+1&&bounds.Top>=-1&&bounds.Bottom<=header.ActualHeight+1,"Toolbar action stays inside header: "+id);
                        Check(control.ActualHeight>=43,"Toolbar action retains controller target height: "+id);
                    }
                    Check(AutomationProperties.GetName(Find<Control>("Widget.pob.undo",root)!)=="Undo","Undo symbol has a native accessible name");
                    Check(Find<FrameworkElement>("Widget.pob.trade-session",root) is not null,"Trade actions occupy a distinct workspace row");
                    if(themeId=="widgetrail.builtin.velvet-dusk"&&game==2)
                        await Capture(Path.Combine(directory,$"native-header-{size.Width}.png"));
                }
                if(editorOnly)
                {
                    if(scenario=="trade-options")
                    {
                        var group=Find<FrameworkElement>("Widget.pob.trade.options",root)!;
                        foreach(var id in new[]{"pob.trade-batch","pob.trade-advanced"})
                        {
                            var control=Find<FrameworkElement>("Widget."+id,root)!;
                            var bounds=control.TransformToVisual(group).TransformBounds(new(0,0,control.ActualWidth,control.ActualHeight));
                            Check(Math.Abs(bounds.Left)<1&&bounds.Width>=group.ActualWidth-1,"Trade option stays left-aligned and stretched: "+id);
                        }
                    }
                    else
                    {
                        var name=Find<Control>("Widget.pob.author-title",root)!;
                        Check(name is not null&&name.IsEnabled&&name.ActualHeight>=43,"Rare item name is a reachable normal-size text-entry control");
                        Check(AutomationProperties.GetName(name!)=="Item name","Item name control has a meaningful accessible label");
                    }
                    if(themeId=="widgetrail.builtin.velvet-dusk"&&game==2&&size.Width==1320)
                        await Capture(Path.Combine(directory,$"native-{scenario}.png"));
                }
                if(scenario=="skill-workspace")
                {
                    Check(Find<FrameworkElement>("Widget.pob.inventory.rows",root) is not null,"Inline skill controls preserve the skill-group rail");
                    Check(declared.Any(n=>n.Id.StartsWith("pob.gem-enabled.",StringComparison.Ordinal)),"Real skill fixture includes inline gem enable controls");
                    var editor=Find<ScrollViewer>("Widget.pob.detail.scroll",root)!;
                    var detail=Find<FrameworkElement>("Widget.pob.detail",root)!;
                    Check(editor.ViewportHeight>=detail.ActualHeight*0.65,"Compact skill editor retains a usable scrolling viewport instead of a fixed oversized header");
                }
                if(density)
                {
                    var options=declared.Where(node=>node.StyleClasses.Contains("pob-setting"))
                        .Select(node=>Find<FrameworkElement>("Widget."+node.Id,root)!).Where(element=>element is not null).ToArray();
                    Check(options.Length>=2,"Configuration density uses actual populated option rows");
                    var first=Descendants(options[0]).OfType<Button>().First();
                    var second=Descendants(options[1]).OfType<Button>().First();
                    var gap=second.TransformToVisual(page).TransformPoint(new()).Y-first.TransformToVisual(page).TransformPoint(new(0,first.ActualHeight)).Y;
                    var visible=options.Count(VisibleInAncestorScrolls);
                    densityMetrics.Add(new{game,themeId,size.Width,size.Height,gap,visible,total=options.Length});
                    if(!Environment.GetCommandLineArgs().Contains("--pob-density-baseline"))
                        Check(gap<=12,"Configuration controls retain targets without redundant empty inter-row padding");
                }
                if(scenario=="build-comparison")
                {
                    Check(rows.Length>0&&Find<Button>("Widget."+rows[0].Id,root) is not null,"Build comparison exposes its real read-only difference rows");
                    Check(Descendants(page).OfType<ScrollViewer>().All(s=>s.ExtentWidth<=s.ViewportWidth+1),"Comparison columns have no horizontal overflow");
                    var columns=rows[0].Children.Single();
                    var firstColumns=Find<Grid>("Widget."+columns.Id,root)!;
                    var owner=Find<Button>("Widget."+rows[0].Id,root)!;
                    Check(firstColumns.ActualWidth>=owner.ActualWidth-owner.Padding.Left-owner.Padding.Right-4,"Rich comparison row fills its controller target rather than collapsing columns");
                    var header=Find<FrameworkElement>("Widget.pob.build-comparison.columns",root)!;
                    var list=Find<ScrollViewer>("Widget.pob.build-comparison.rows",root)!;
                    Check(header.TransformToVisual(page).TransformPoint(new(0,header.ActualHeight)).Y<=list.TransformToVisual(page).TransformPoint(new()).Y+1,"Comparison rows do not overlap their column headings");
                    Check(!declared.Any(n=>n.ActionId is "apply" or "loadout-create"),"Comparison fixture exposes no apply or transfer control");
                }
                if(disclosures)
                {
                    var toggleId=scenario.StartsWith("calculation-",StringComparison.Ordinal)?"pob.calculation.details-toggle":"pob.config.inactive.toggle";
                    var toggle=Find<Button>("Widget."+toggleId,root)!;
                    toggle.StartBringIntoView();
                    await Wait(()=>VisibleInAncestorScrolls(toggle));
                    Check(toggle.Focus(FocusState.Keyboard),"Disclosure toggle accepts native focus at constrained size and text scale");
                    Check(Descendants(page).OfType<ScrollViewer>().All(s=>s.ExtentWidth<=s.ViewportWidth+1),"Disclosure layout has no horizontal overflow");
                    if(scenario.StartsWith("calculation-",StringComparison.Ordinal))
                    {
                        Check(Find<TextBlock>("Widget.pob.calculation.value",root) is {ActualHeight:>0},"Calculation total remains present beside disclosure");
                        Check(Find<TextBlock>("Widget.pob.calculation.context",root) is {ActualHeight:>0},"Calculation context remains present beside disclosure");
                        Check((Find<FrameworkElement>("Widget.pob.calculation.source-list",root)is not null)==scenario.EndsWith("expanded",StringComparison.Ordinal),"Calculation sources follow disclosure state");
                    }
                    var count=dispatched.Count;page.ActivateFocused();await Wait(()=>dispatched.Count==count+1);
                    Check(dispatched[^1]==declared.Single(n=>n.Id==toggleId).ActionId,"Disclosure uses its declared action without activating another control");
                    Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),toggle),"Disclosure activation retains its opener");
                    foreach(var row in disclosureRows.Values.Where(items=>items.Length>0).Select(items=>items[0]))
                    {
                        if(Find<Button>("Widget."+row.Id,root)is not {} button)continue;
                        button.StartBringIntoView();button.Focus(FocusState.Keyboard);
                        await Wait(()=>VisibleInAncestorScrolls(button));
                        Check(true,"Calculation list and source rows can be fully revealed at enlarged text scale");
                    }
                }
                var busyNode=scenario=="search"?rows[0]:scenario=="skill-workspace"
                    ?declared.First(n=>n.Id.StartsWith("pob.gem-enabled.",StringComparison.Ordinal)&&n.Kind==ViewNodeKind.Button)
                    :scenario=="build-comparison"?declared.First(n=>n.ActionId=="build-comparison-run")
                    :declared.FirstOrDefault(n=>n.Id==target&&n.Kind is ViewNodeKind.Button or ViewNodeKind.ActionSurface);
                if(busyNode is not null)
                {
                    var control=Find<Button>("Widget."+busyNode.Id,root)!;control.StartBringIntoView();
                    await Wait(()=>control.IsLoaded&&control.ActualHeight>0);Check(control.Focus(FocusState.Keyboard),"Workflow control accepts native focus before busy update");
                    var stable=snapshot;
                    ViewNode Busy(ViewNode node)=>node with{IsBusy=node.Id==busyNode.Id?true:node.IsBusy,Children=node.Children.Select(Busy).ToArray()};
                    snapshot=snapshot with{Root=Busy(snapshot.Root)};Publish();
                    Check(ReferenceEquals(Find<Button>("Widget."+busyNode.Id,root),control)&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),control)&&control.IsEnabled,
                        "Busy publication retains the same enabled native control and focus");
                    var count=dispatched.Count;page.ActivateFocused();Check(dispatched.Count==count,"Busy control suppresses duplicate activation");
                    snapshot=stable;Publish();
                    Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),control),"Completing busy state does not move focus");
                    await Wait(()=>VisibleInAncestorScrolls(control));
                    Check(true,"Focused workflow control is visible after scrolling and busy publication settle");
                }
                if(size.Width==820&&game==2&&themeId==ThemeIdentity.BuiltInMineralLight&&scenario is "build-comparison" or "configuration-expanded")await Capture(Path.Combine(directory,$"native-{scenario}-{themeId}.png"));
                if(size.Width==1320&&game==2&&themeId=="widgetrail.builtin.velvet-dusk"&&scenario is "skill-workspace" or "build-comparison" or "configuration-expanded" or "configuration-density")
                    await Capture(Path.Combine(directory,$"native-wide-{scenario}.png"));
                if(failure is not null)throw failure;

                void Publish()
                {
                    var current=snapshot with{Sequence=++sequence};var styles=new Dictionary<string,BridgeNodeRenderStyles>();
                    void Visit(ViewNode node)
                    {
                        styles[node.Id]=Compute(theme,node);
                        foreach(var child in node.Children)Visit(child);
                        if(node.FocusPresentation is {} fragment)Visit(fragment);
                        if(node.DefaultFocusPresentation is {} fallback)Visit(fallback);
                    }
                    Visit(current.Root);foreach(var role in new[]{"body","canvas","panel","hint","tray-item"})styles[role]=Compute(theme,role,role,[]);
                    popupTheme.Update(styles,AppearanceSettings.Default);
                    var descriptor=new BridgeWidgetDescriptor{Id="pob",Name="PoB workflow fixture",InstanceId=current.WidgetInstanceId,
                        RuntimeGeneration=runtime,PresentationGeneration=runtime,Icon=WidgetGlyph.Settings,PackageContentDigest=""};
                    page.Apply(new(new(descriptor.Id,descriptor.RuntimeGeneration,descriptor.PresentationGeneration,1,current.WidgetInstanceId,current.Sequence,current.ActiveInputScopeId),descriptor,current,styles));page.UpdateLayout();
                }
            }
        }
        catch(Exception error){failure=error;await Capture(Path.Combine(directory,"native-failure.png"));}
        finally{await page.DisposeAsync();host.Children.Remove(root);}
        await File.WriteAllTextAsync(Path.Combine(directory,"native-result.json"),JsonSerializer.Serialize(new{passed=failure is null,checks,densityMetrics,error=failure?.ToString(),
            scope="Native presentation and focus with materialized exported rows. Real provider routing, catalog caching and read-only engine/build invariants are tested separately."}));
        if(failure is not null)throw failure;
        static IEnumerable<ViewNode> Flatten(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Flatten));
        static bool VisibleInAncestorScrolls(FrameworkElement control)
        {
            for(DependencyObject? parent=VisualTreeHelper.GetParent(control);parent is not null;parent=VisualTreeHelper.GetParent(parent))
                if(parent is ScrollViewer scroll)
                {
                    var bounds=control.TransformToVisual(scroll).TransformBounds(new(0,0,control.ActualWidth,control.ActualHeight));
                    var viewport=WidgetScrollViewport.ContentBounds(scroll);
                    if(bounds.Top < viewport.Top-1 || bounds.Bottom > viewport.Bottom+1 || bounds.Left < viewport.Left-1 || bounds.Right > viewport.Right+1)return false;
                }
            return true;
        }
        async Task Capture(string path)
        {
            var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(root);
            using var stream=new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());await encoder.FlushAsync();stream.Seek(0);
            using var output=File.Create(path);await stream.AsStreamForRead().CopyToAsync(output);
        }
    }
}
