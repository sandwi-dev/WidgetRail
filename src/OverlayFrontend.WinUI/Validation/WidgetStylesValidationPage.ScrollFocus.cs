using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeScrollFocusAsync()
    {
        var priorWidth = presenter.Width;
        var priorHeight = presenter.Height;
        presenter.Width = 500; presenter.Height = 300;
        var rows = Enumerable.Range(0, 24).Select(index => new ViewNode
        {
            Id = "scroll-row-" + index, Kind = ViewNodeKind.Button, Text = "Track " + index, ActionId = "play",
        }).ToArray();
        var scrollNode = new ViewNode { Id = "scroll-focus.viewport", Kind = ViewNodeKind.Scroll,
            ScrollAxis = ScrollAxis.Vertical, Children = rows };
        var root = new ViewNode { Id = "scroll-focus.root", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[scrollNode] };
        var styles = rows.ToDictionary(row => row.Id, row => Compute("button { height: 44px; min-height: 44px; }", row.Id, "button"));
        styles[scrollNode.Id] = Compute("scroll { height: 240px; }", scrollNode.Id, "scroll");
        presenter.Apply(CreateFrame(root, styles));
        await Wait(() => Find<ScrollViewer>("Widget.scroll-focus.viewport")?.ScrollableHeight > 400);
        var scroll = Find<ScrollViewer>("Widget.scroll-focus.viewport")!;
        var first = Find<Button>("Widget.scroll-row-0")!;
        first.Focus(FocusState.Keyboard);
        // Finish initial entry/BringIntoView before simulating a later stick gesture.
        await Task.Delay(100);
        var revealFlashed=false;
        RoutedEventHandler observeReveal=(_,_)=>revealFlashed|=presenter.ScrollFocusOutlineForValidation is not null;
        EventHandler<ScrollViewerViewChangedEventArgs> observeView=(_,_)=>revealFlashed|=presenter.ScrollFocusOutlineForValidation is not null;
        presenter.GotFocus+=observeReveal;scroll.ViewChanged+=observeView;
        for(var index=1;index<=7;index++){presenter.MoveFocus(FocusNavigationDirection.Down);await Task.Delay(35);}
        await Wait(()=>scroll.VerticalOffset>0);
        Check(!revealFlashed&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),Find<Button>("Widget.scroll-row-7")),
            "directional navigation reveals offscreen controls without flashing a container outline");
        presenter.GotFocus-=observeReveal;scroll.ViewChanged-=observeView;
        first.Focus(FocusState.Keyboard);first.StartBringIntoView(new(){AnimationDesired=false});
        await Wait(()=>scroll.VerticalOffset<1);
        presenter.ScrollBy(0, 100);
        presenter.ScrollBy(0, 1);
        await Wait(() => scroll.VerticalOffset >= 100.5);
        Check(Math.Abs(scroll.VerticalOffset - 101) < 1,
            "two controller samples in one dispatch accumulate instead of replacing the pending movement");
        presenter.ScrollBy(0, -60);
        presenter.ScrollBy(0, -41);
        await Wait(() => scroll.VerticalOffset < 1);
        Check(scroll.VerticalOffset < 1,
            "queued reverse movement returns to the boundary without replaying forward movement");
        Check(presenter.ScrollBy(0, 480), "right-stick scrolling selects the focused declaration's native viewport");
        await Wait(() => scroll.VerticalOffset >= 470);
        await Wait(presenter.SettleScrollFocus);
        var settledOffset = scroll.VerticalOffset;
        Check(presenter.SettleScrollFocus(), "right-stick release finds a ready visible focus target");
        var target = FocusManager.GetFocusedElement(XamlRoot) as Button;
        var bounds = target!.TransformToVisual(scroll).TransformBounds(new(0, 0, target.ActualWidth, target.ActualHeight));
        Check(target != first && bounds.Y >= -.5 && bounds.Bottom <= scroll.ViewportHeight + .5,
            "right-stick release focuses a fully visible row instead of its offscreen origin");
        await Task.Delay(50);
        Check(Math.Abs(scroll.VerticalOffset - settledOffset) < 1, "focus settlement preserves the user's viewport offset");
        presenter.MoveFocus(FocusNavigationDirection.Down);
        Check(!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first), "directional navigation resumes from the visible settled row");
        target = FocusManager.GetFocusedElement(XamlRoot) as Button;
        presenter.ScrollBy(0, 1);
        await Task.Delay(30);
        await Wait(presenter.SettleScrollFocus);
        Check(presenter.SettleScrollFocus() && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), target),
            "free scrolling preserves an already-visible focused control");
        presenter.ScrollBy(0, 300);
        presenter.SetAutomaticFocusEnabled(false);
        await Task.Delay(30);
        await Wait(presenter.SettleScrollFocus);
        Check(presenter.SettleScrollFocus() && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), target),
            "revoking widget focus cancels pending scroll settlement");
        presenter.SetAutomaticFocusEnabled(true);
        var readOnly = scrollNode with { Id = "scroll-reading.viewport", Children = Enumerable.Range(0, 24)
            .Select(index => new ViewNode { Id = "paragraph-" + index, Kind = ViewNodeKind.Text, Text = "Preview details " + index }).ToArray() };
        var apply = new ViewNode { Id = "reading.apply", Kind = ViewNodeKind.Button, Text = "Apply", ActionId = "apply" };
        var discard = new ViewNode { Id = "reading.discard", Kind = ViewNodeKind.Button, Text = "Discard", ActionId = "discard" };
        var rightPanel = new ViewNode { Id = "reading.panel", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[apply, discard, readOnly] };
        presenter.Apply(CreateFrame(root with { Kind = ViewNodeKind.Row, Children = (ViewNode[])[scrollNode, rightPanel] },
            new Dictionary<string, BridgeNodeRenderStyles> {
                [scrollNode.Id] = Compute("scroll { width: 220px; height: 240px; }", scrollNode.Id, "scroll"),
                [rightPanel.Id] = Compute("stack { width: 220px; }", rightPanel.Id, "stack"),
                [readOnly.Id] = Compute("scroll { height: 150px; } scroll:focused { outline-width: 2px; outline-color: #aabbcc; outline-offset: -2px; }", readOnly.Id, "scroll") }));
        await Wait(() => Find<ScrollViewer>("Widget.scroll-reading.viewport")?.ScrollableHeight > 100);
        var applyButton = Find<Button>("Widget.reading.apply")!;
        applyButton.Focus(FocusState.Keyboard);
        await Task.Delay(100);
        var leftOffset=Find<ScrollViewer>("Widget.scroll-focus.viewport")!.VerticalOffset;
        Check(presenter.ScrollBy(0, 180), "right-stick scrolling can read a text-only preview beside action buttons");
        await Task.Delay(30);
        Check(Find<ScrollViewer>("Widget.scroll-reading.viewport")!.VerticalOffset>0 &&
            Find<ScrollViewer>("Widget.scroll-focus.viewport")!.VerticalOffset==leftOffset,
            "fixed detail actions scroll their own panel instead of the neighboring list");
        await Wait(presenter.SettleScrollFocus);
        Check(presenter.SettleScrollFocus() && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),Find<ScrollViewer>("Widget.scroll-reading.viewport")),
            "text-only scrolling settles on the container instead of waiting for nonexistent rows");
        Find<ScrollViewer>("Widget.scroll-reading.viewport")!.ChangeView(null,0,null,true);
        await Wait(()=>Find<ScrollViewer>("Widget.scroll-reading.viewport")!.VerticalOffset<.5);
        presenter.MoveFocus(FocusNavigationDirection.Up);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Find<Button>("Widget.reading.discard")),
            "directional navigation returns to the preceding action at the top of a text-only preview");
        var reading=Find<ScrollViewer>("Widget.scroll-reading.viewport")!;
        await Wait(()=>reading.IsTabStop);
        Check(!Find<ScrollViewer>("Widget.scroll-focus.viewport")!.IsTabStop,
            "a list with interactive rows does not add a redundant container stop");
        presenter.MoveFocus(FocusNavigationDirection.Down);
        await Wait(()=>ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),reading));
        Check(!reading.UseSystemFocusVisuals && Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementChildVisual(reading) is not null,
            "reading focus uses the authored theme outline");
        reading.ChangeView(null,0,null,true);await Wait(()=>reading.VerticalOffset<.5);
        presenter.MoveFocus(FocusNavigationDirection.Down);
        await Wait(()=>reading.VerticalOffset>0);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),reading),"Down scrolls the focused reading region without moving to another panel");
        var beforeStick=reading.VerticalOffset;
        presenter.ScrollBy(0,80);await Wait(()=>reading.VerticalOffset>beforeStick);
        await Wait(presenter.SettleScrollFocus);
        Check(presenter.SettleScrollFocus()&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),reading),
            "right-stick scrolling retains the reading-region focus");
        reading.ChangeView(null,0,null,true);await Wait(()=>reading.VerticalOffset<.5);
        presenter.MoveFocus(FocusNavigationDirection.Up);
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),Find<Button>("Widget.reading.discard")),
            "Up at the top of a reading region returns to its preceding action");
        presenter.MoveFocus(FocusNavigationDirection.Down);
        presenter.MoveFocus(FocusNavigationDirection.Left);
        Check(!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),reading),"Left leaves a vertical reading region immediately");
        outside.Focus(FocusState.Keyboard);
        var shortReading=readOnly with{Children=(ViewNode[])[new(){Id="reading.short",Kind=ViewNodeKind.Text,Text="Short text"}]};
        presenter.Apply(CreateFrame(root with{Children=(ViewNode[])[shortReading]},new Dictionary<string,BridgeNodeRenderStyles>{
            [shortReading.Id]=Compute("scroll { height: 150px; }",shortReading.Id,"scroll")}));
        await Wait(()=>Find<ScrollViewer>("Widget.scroll-reading.viewport")!.ScrollableHeight<.5);
        await Task.Delay(50);
        Check(!Find<ScrollViewer>("Widget.scroll-reading.viewport")!.IsTabStop,"fitting read-only content adds no focus stop");
        presenter.Apply(CreateFrame(root with{Children=(ViewNode[])[readOnly]},new Dictionary<string,BridgeNodeRenderStyles>{
            [readOnly.Id]=Compute("scroll { height: 150px; }",readOnly.Id,"scroll")}));
        await Wait(()=>Find<ScrollViewer>("Widget.scroll-reading.viewport")!.IsTabStop);
        reading=Find<ScrollViewer>("Widget.scroll-reading.viewport")!;
        reading.Focus(FocusState.Keyboard);
        presenter.Apply(CreateFrame(root with{Children=(ViewNode[])[shortReading]},new Dictionary<string,BridgeNodeRenderStyles>{
            [shortReading.Id]=Compute("scroll { height: 150px; }",shortReading.Id,"scroll")}));
        await Wait(()=>reading.ScrollableHeight<.5);
        Check(reading.IsTabStop&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),reading),
            "content shrinking preserves a valid focus origin until the user leaves");
        outside.Focus(FocusState.Keyboard);
        await Wait(()=>!reading.IsTabStop);
        Check(!reading.IsTabStop,"a fitted region retires its stop after focus leaves");
        await MixedReadingFocusAsync();
        await AnimatedScrollLifetimeAsync();
        presenter.Width = priorWidth; presenter.Height = priorHeight;
    }

    private async Task MixedReadingFocusAsync()
    {
        foreach(var horizontal in new[]{false,true})
        {
            var first=new ViewNode{Id="mixed.first",Kind=ViewNodeKind.Button,Text="First action",ActionId="first"};
            var last=first with{Id="mixed.last",Text="Last action",ActionId="last"};
            var middle=new ViewNode{Id="mixed.middle",Kind=ViewNodeKind.Spacer};
            var viewport=new ViewNode{Id="mixed.scroll",Kind=ViewNodeKind.Scroll,ScrollAxis=horizontal?ScrollAxis.Horizontal:ScrollAxis.Vertical,
                Children=(ViewNode[])[first,middle,last]};
            var styles=new Dictionary<string,BridgeNodeRenderStyles>{
                [first.Id]=Compute("button { width: 100px; height: 44px; }",first.Id,"button"),
                [last.Id]=Compute("button { width: 100px; height: 44px; }",last.Id,"button"),
                [middle.Id]=Compute("spacer { width: 600px; height: 600px; }",middle.Id,"spacer"),
                [viewport.Id]=Compute("scroll { width: 240px; height: 150px; } scroll:focused { outline-width: 2px; outline-color: #aabbcc; }",viewport.Id,"scroll")};
            presenter.Apply(CreateFrame(new(){Id="mixed.root",Kind=ViewNodeKind.Stack,Children=(ViewNode[])[viewport]},styles));
            var scroll=Find<ScrollViewer>("Widget.mixed.scroll")!;
            await Wait(()=>scroll.IsLoaded&&(horizontal?scroll.ScrollableWidth:scroll.ScrollableHeight)>300);
            Find<Button>("Widget.mixed.first")!.Focus(FocusState.Keyboard);await Task.Delay(80);
            Check(!scroll.IsTabStop,"mixed region uses its visible action instead of a container stop (horizontal="+horizontal+")");
            presenter.ScrollBy(horizontal?300:0,horizontal?0:300);
            await Wait(()=>scroll.IsTabStop);
            Check(presenter.ScrollFocusOutlineForValidation is null&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),Find<Button>("Widget.mixed.first")),
                "free scrolling keeps its original focus without an intermediate viewport outline (horizontal="+horizontal+")");
            presenter.ScrollBy(horizontal?40:0,horizontal?0:40);
            await Wait(()=>(horizontal?scroll.HorizontalOffset:scroll.VerticalOffset)>330);
            Check(presenter.ScrollFocusOutlineForValidation is null&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),Find<Button>("Widget.mixed.first")),
                "continued scrolling does not settle focus between samples (horizontal="+horizontal+")");
            await Wait(presenter.SettleScrollFocus);
            Check(presenter.SettleScrollFocus()&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),scroll),
                "scroll release focuses the container when all actions are outside the viewport (horizontal="+horizontal+")");
            presenter.ScrollBy(horizontal?1000:0,horizontal?0:1000);
            await Wait(()=>(horizontal?scroll.HorizontalOffset:scroll.VerticalOffset)>500);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),scroll),
                "a newly visible option waits for scroll release before taking reading focus (horizontal="+horizontal+")");
            await Wait(presenter.SettleScrollFocus);
            Check(presenter.SettleScrollFocus()&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),Find<Button>("Widget.mixed.last")),
                "scroll release settles onto a newly visible option (horizontal="+horizontal+")");
            await Wait(()=>!scroll.IsTabStop);
            Check(!scroll.IsTabStop,"visible-option settlement removes the extra container stop (horizontal="+horizontal+")");
            var offset=horizontal?scroll.HorizontalOffset:scroll.VerticalOffset;
            await Task.Delay(50);
            Check(Math.Abs((horizontal?scroll.HorizontalOffset:scroll.VerticalOffset)-offset)<1,
                "settling onto the visible option does not rewind the viewport (horizontal="+horizontal+")");
        }
    }
}
