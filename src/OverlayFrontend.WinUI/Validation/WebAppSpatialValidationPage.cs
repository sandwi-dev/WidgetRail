using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WebAppSpatialValidationPage : Page, IAsyncDisposable
{
    private readonly BrowserSurface browser;
    private readonly NativePopupTheme theme = new();
    private const string Html = """
        <!doctype html><title>Spatial fixture</title><style>
        body{margin:0;background:#182030;color:white;font:18px sans-serif}
        button,a,input,[role=button]{box-sizing:border-box;min-width:80px;height:40px}
        .fixed{position:absolute;width:100px;height:40px}
        </style><body><script>
        window.lastMove='';window.clicks=[];
        document.addEventListener('mousemove',e=>{if(e.isTrusted)window.lastMove=e.composedPath().find(n=>n.id)?.id??'';});
        document.addEventListener('click',e=>{if(e.isTrusted)window.clicks.push(e.composedPath().find(n=>n.id)?.id??'');});
        </script></body>
        """;
    internal WebAppSpatialValidationPage(string result, string profile)
    {
        theme.Attach(this); theme.Update(null, AppearanceSettings.Default);
        Task<CoreWebView2Environment>? environment = null;
        BrowserSurface.FixturePage = url => new Uri(url).AbsolutePath == "/frame"
            ? "<!doctype html><style>body{margin:0}button{position:absolute;width:100px;height:40px;left:20px}#inner{top:20px}#inner2{top:90px}</style><button id=inner>Inner</button><button id=inner2>Inner 2</button><script>document.addEventListener('click',e=>parent.postMessage({clicked:e.target.id,trusted:e.isTrusted},'*'));</script>"
            : new Uri(url).AbsolutePath == "/scroll-frame"
            ? "<!doctype html><style>body{margin:0}button{width:100px;height:40px}</style><button style='position:absolute;top:0'>Header</button><div id=scroller style='position:absolute;top:50px;width:280px;height:120px;overflow:auto'><button id=inner>Top</button><div style='height:100px'></div><button id=inner2>Bottom</button><div style='height:100px'></div></div><script>document.addEventListener('click',e=>parent.postMessage({clicked:e.target.id,trusted:e.isTrusted},'*'));</script>"
            : Html;
        browser = new("fixture.spatial", new("spatial", 1, "https://spatial.fixture.invalid/", "Spatial fixture")
        { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp },
            () => environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profile, null).AsTask())
        { IsInteractionFocused = () => true };
        Content = new Grid { Margin = new(24), Children = { browser } };
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); var timings = new List<SpatialMoveResult>(); string? error = null;
            try
            {
                browser.SetPresentation(true, true);
                await Until(() => browser.IsReady && !browser.IsNavigationInProgress && browser.CurrentTitle == "Spatial fixture");
                browser.Enter();
                Check(await browser.EvaluateFixtureAsync("innerWidth>=600&&innerHeight>=300") == "true", "Spatial fixture has a usable CSS viewport at the current display DPI");
                await Body("<button id=source style='position:absolute;left:100px;top:160px;width:240px;height:40px;cursor:default'>Source<span id=decoration style='position:absolute;left:185px;top:8px;width:24px;height:24px;cursor:pointer'>*</span></button><button id=leftTarget class=fixed style='left:100px;top:40px'>Left</button><button id=rightTarget class=fixed style='left:275px;top:40px'>Right</button>");
                await PointAt("source"); await Jump(FocusNavigationDirection.Up, "leftTarget");
                await PointAt("decoration");
                await Jump(FocusNavigationDirection.Up, "leftTarget");
                Check(true, "Decorative pointer-cursor descendants do not change the source geometry of a semantic button");
                await Body("<button id=far class=fixed style='left:40px;top:40px'>Far above</button><div style='position:absolute;top:700px;height:10px;overflow:hidden'>" + string.Concat(Enumerable.Repeat("<div>Offscreen structure</div>", 13000)) + "</div><div style='position:absolute;left:200px;top:200px;display:flex;gap:20px'><button id=near>Neighbor</button><button id=source>Source</button></div>");
                await PointAt("source"); await Jump(FocusNavigationDirection.Left, "near");
                Check(true, "A late-DOM toolbar keeps its nearby left neighbor despite an earlier oversized page subtree");
                await browser.EvaluateFixtureAsync("document.getElementById('near').style.transform='translateY(-60px)'");
                await PointAt("source"); await Jump(FocusNavigationDirection.Left, "near");
                Check(browser.LastSpatialMoveForValidation is { Truncated: true }, "A truncated page still permits a nearby diagonal destination without row overlap");
                await browser.EvaluateFixtureAsync("document.getElementById('near').style.transform='translate(-100px,-100px)'");
                await PointAt("source"); await Jump(FocusNavigationDirection.Up, "near");
                Check(true, "Vertical navigation also permits nearby controls without column overlap");
                await browser.EvaluateFixtureAsync("document.getElementById('near').hidden=true");
                await PointAt("source"); await Jump(FocusNavigationDirection.Left, "far");
                Check(true, "Navigation can leave the toolbar when its closer neighbor is unavailable");
                await Body("<button id=a class=fixed style='left:40px;top:40px'>A</button><button id=b class=fixed style='left:200px;top:40px'>B</button><button id=d class=fixed style='left:200px;top:150px'>D</button>");
                await PointAt("a");
                await Jump(FocusNavigationDirection.Right, "b");
                await Jump(FocusNavigationDirection.Down, "d");
                await Jump(FocusNavigationDirection.Up, "b");
                await Jump(FocusNavigationDirection.Left, "a");
                Check(true, "Grid jumps follow geometric directions and send native hover");
                Check(await browser.EvaluateFixtureAsync("clicks.length") == "0", "Jumps do not click controls");
                await Jump(FocusNavigationDirection.Right, "b");
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed); await Task.Delay(50);
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released); await Task.Delay(120);
                Check(await browser.EvaluateFixtureAsync("clicks.join(',')") == "\"b\"", "A activates the jumped target through native pointer input");

                await Body("<button id=a class=fixed style='left:40px;top:40px'>A</button><button disabled id=disabled class=fixed style='left:160px;top:40px'>Disabled</button><button id=hidden style='display:none'>Hidden</button><div role=button id=custom class=fixed style='left:320px;top:40px;background:#456'>Custom</div><button id=covered class=fixed style='left:200px;top:150px'>Covered</button><div style='position:absolute;left:190px;top:140px;width:120px;height:60px;background:#333;z-index:20'></div>");
                await PointAt("a"); await Jump(FocusNavigationDirection.Right, "custom");
                Check(true, "Disabled, hidden and covered controls are skipped; ARIA controls participate");
                await browser.EvaluateFixtureAsync("custom.remove()");
                await Jump(FocusNavigationDirection.Left, "a");
                Check(true, "Dynamic removal is reflected on the next jump");

                await Body("<button id=a class=fixed style='left:40px;top:40px'>A</button><div inert><button class=fixed style='left:160px;top:40px'>Inert</button></div><div aria-disabled=true><button class=fixed style='left:220px;top:40px'>Disabled group</button></div><input id=check type=checkbox style='opacity:0;width:1px;height:1px;position:absolute'><label id=proxy for=check class=fixed style='left:360px;top:40px;background:#456'>Checkbox</label>");
                await PointAt("a"); await Jump(FocusNavigationDirection.Right, "proxy");
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed); await Task.Delay(50);
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released); await Task.Delay(100);
                Check(await browser.EvaluateFixtureAsync("document.getElementById('check').checked") == "true", "Visible label proxies work; inert and disabled groups do not participate");

                await Body("<div id=card role=button style='position:absolute;left:30px;top:30px;width:480px;height:180px;background:#234'><button id=a class=fixed style='left:10px;top:10px'>A</button><button id=b class=fixed style='left:200px;top:10px'>B</button></div>");
                await PointAt("a"); await Jump(FocusNavigationDirection.Right, "b");
                Check(true, "Interactive ancestor cards do not steal directional navigation from their children");

                await Body("<button id=a class=fixed style='left:40px;top:40px'>A</button><div style='position:absolute;left:0;top:0;width:10px;height:10px;overflow:hidden'><button id=fixed style='position:fixed;left:200px;top:40px;width:100px'>Escaped</button></div>");
                await PointAt("a"); await Jump(FocusNavigationDirection.Right, "fixed");
                Check(true, "Visible fixed controls escaping ancestor overflow remain navigable");

                await Body("<button id=a class=fixed style='left:40px;top:40px'>A</button><div id=shadow style='position:absolute;left:210px;top:40px'></div>");
                await browser.EvaluateFixtureAsync("shadow.attachShadow({mode:'open'}).innerHTML='<button id=shadowButton style=\"width:110px;height:40px\">Shadow</button>'");
                await PointAt("a"); await Jump(FocusNavigationDirection.Right, "shadowButton");
                Check(true, "Open shadow-root controls use a real hit-tested point");

                await Body("<button id=a class=fixed style='left:40px;top:40px'>A</button><button id=b class=fixed style='left:200px;top:40px'>B</button><div style='position:absolute;left:235px;top:40px;width:30px;height:40px;background:#555;z-index:4'></div>");
                await PointAt("a"); await Jump(FocusNavigationDirection.Right, "b");
                Check(true, "Partially occluded controls use an uncovered point instead of their center");

                await Body("<style>#sub{display:none;position:absolute;top:45px;left:0}#menu:hover #sub{display:block}</style><button id=a class=fixed style='left:40px;top:40px'>A</button><div role=button id=menu class=fixed style='left:200px;top:40px;background:#456'>Menu<button id=sub>Submenu</button></div>");
                await PointAt("a"); await Jump(FocusNavigationDirection.Right, "menu");
                await Jump(FocusNavigationDirection.Down, "sub");
                Check(true, "Native hover reveals menus and the next jump finds their new controls");

                await Body("<button id=a class=fixed style='left:40px;top:40px'>A</button><input id=field class=fixed style='left:200px;top:40px' value='synthetic'>");
                await PointAt("a"); await Jump(FocusNavigationDirection.Right, "field");
                Check(browser.FixtureEditor is null, "Jumping to a field does not open the keyboard");
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed); await Task.Delay(50);
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released);
                await Until(() => browser.FixtureEditor is not null);
                var keyboardRequests = browser.SpatialRequestsForValidation;
                browser.MoveFocus(FocusNavigationDirection.Right);
                Check(browser.SpatialRequestsForValidation == keyboardRequests, "Keyboard D-pad retains its own navigation");
                browser.HandleButton(ControllerButton.B, ControllerEventPhase.Pressed);
                browser.HandleButton(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => browser.FixtureEditor is null); await Task.Delay(150);
                Check(true, "A still opens live text entry after a geometric jump");

                await RunRowScrollChecksAsync(Body, PointAt, Check);

                await Body("<div id=scroller style='position:absolute;left:40px;top:40px;width:300px;height:180px;overflow:auto'><button id=top>Top</button><div style='height:240px'></div><button id=bottom>Bottom</button><div style='height:70px'></div></div><button id=outside class=fixed style='left:370px;top:150px'>Other pane</button>");
                await PointAt("top");
                // One bounded scroll may expose only blank space; repeated D-pad input continues it.
                for (var attempt = 0; attempt < 4 && await browser.EvaluateFixtureAsync("lastMove") != "\"bottom\""; attempt++)
                { browser.MoveFocus(FocusNavigationDirection.Down); await Until(() => !browser.SpatialBusyForValidation); await Task.Delay(50); }
                Check(await browser.EvaluateFixtureAsync("lastMove") == "\"bottom\"", "Nested scroll containers reveal the next control");
                Check(await browser.EvaluateFixtureAsync("window.scrollY") == "0", "Nested navigation does not scroll the outer document");
                Check(true, "Scrollable list navigation stays inside the list before moving to another pane");

                await Body("<style>.row:hover{background:#456}</style><button id=a class=fixed style='left:20px;top:40px'>A</button><div role=separator tabindex=0 style='position:absolute;left:140px;top:0;width:4px;height:250px;cursor:ew-resize'></div><div tabindex=0 style='position:absolute;left:150px;top:0;width:100px;height:240px'></div><ol role=list tabindex=0 style='position:absolute;margin:0;padding:0;left:260px;top:0;width:150px;height:240px'><li role=article tabindex=0 class=row style='height:200px'>Hover-only row</li></ol><button id=b class=fixed style='left:450px;top:40px'>B</button>");
                await PointAt("a"); await Jump(FocusNavigationDirection.Right, "b");
                await Jump(FocusNavigationDirection.Left, "a");
                Check(true, "Focusable panels, lists, resize separators and hover-only rows are not pointer targets");

                await Body("<button id=a style='position:absolute;left:40px;top:40px;width:300px;height:40px'>Wide source</button><button id=wrong class=fixed style='left:250px;top:100px'>Behind pointer</button><div id=b tabindex=0 style='position:absolute;left:450px;top:100px;width:100px;height:40px;cursor:pointer'>Custom action</div>");
                await PointAt("a");
                await browser.EvaluateFixtureAsync("window.anchorX=(320-visualViewport.offsetLeft)/visualViewport.width");
                browser.MovePointer(double.Parse(await browser.EvaluateFixtureAsync("anchorX"), System.Globalization.CultureInfo.InvariantCulture) - browser.SpatialPointerForValidation.X, 0);
                await Jump(FocusNavigationDirection.Right, "b");
                Check(true, "Actual landing follows the requested direction; custom pointer actions remain available");

                await Body("<button id=header class=fixed style='left:40px;top:0px'>Header</button><div id=scroller style='position:absolute;left:40px;top:80px;width:300px;height:140px;overflow:auto'><button id=top>Top</button><div style='height:500px'></div><button id=bottom>Bottom</button></div><button id=floating class=fixed style='left:220px;top:150px;z-index:3'>Foreign overlay</button>");
                await PointAt("top");
                browser.MoveFocus(FocusNavigationDirection.Down); await Until(() => !browser.SpatialBusyForValidation);
                Check(await browser.EvaluateFixtureAsync("scroller.scrollTop>0") == "true", "Down scrolls a region even when the next action is not exposed yet");
                var emptyScrollPoint = browser.SpatialPointerForValidation;
                Check(await browser.EvaluateFixtureAsync(FormattableString.Invariant($"scroller.contains(document.elementFromPoint(visualViewport.offsetLeft+{emptyScrollPoint.X}*visualViewport.width,visualViewport.offsetTop+{emptyScrollPoint.Y}*visualViewport.height))")) == "true" && browser.SpatialErrorForValidation is null,
                    "Scroll scope excludes overlapping foreign controls and cannot jump backward into a fixed header");

                await Body("<button id=header class=fixed style='left:40px;top:0px'>Header</button><div id=scroller style='position:absolute;left:40px;top:80px;width:300px;height:140px;overflow:auto'><button id=top>Top</button><div style='height:140px'></div><button id=bottom>Bottom</button><div style='height:200px'></div></div>");
                await browser.EvaluateFixtureAsync("scroller.addEventListener('scroll',()=>{document.getElementById('top')?.remove();},{once:true})");
                await PointAt("top"); await Jump(FocusNavigationDirection.Down, "bottom");
                Check(true, "Scroll retains DOM region ownership when virtualized content removes the starting control");

                await Body("<div id=rtl style='position:absolute;left:40px;top:40px;width:250px;overflow:auto;white-space:nowrap;direction:rtl'>" + string.Concat(Enumerable.Range(0, 6).Select(i => "<button id=r" + i + " style='width:100px'>RTL " + i + "</button>")) + "</div>");
                await PointAt("r0"); await Jump(FocusNavigationDirection.Left, "r1");
                await Jump(FocusNavigationDirection.Left, "r2"); await Jump(FocusNavigationDirection.Left, "r3");
                Check(await browser.EvaluateFixtureAsync("rtl.scrollLeft<0&&window.scrollX===0") == "true", "Horizontal RTL lists scroll in physical D-pad direction");

                foreach (var origin in new[] { "spatial.fixture.invalid", "cross-origin.invalid" })
                {
                    await Body("<button id=a class=fixed style='left:20px;top:60px'>A</button><iframe id=frame src='https://" + origin + "/frame' style='position:absolute;left:220px;top:40px;width:300px;height:180px;border:2px solid white'></iframe>");
                    await Task.Delay(350);
                    await browser.EvaluateFixtureAsync("window.frameClick=null;window.addEventListener('message',e=>window.frameClick=e.data)");
                    await PointAt("a");
                    browser.MoveFocus(FocusNavigationDirection.Right); await Until(() => !browser.SpatialBusyForValidation);
                    Check(browser.LastSpatialMoveForValidation is { Frames: >= 2 }, "Owned iframe geometry is available: " + origin + " " + browser.SpatialErrorForValidation);
                    if (origin == "cross-origin.invalid") Check(browser.LastSpatialMoveForValidation is { Sessions: >= 1 }, "Cross-site iframe uses its own owned CDP session");
                    if (browser.LastSpatialMoveForValidation is { } frameMove) timings.Add(frameMove);
                    browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed); await Task.Delay(50);
                    browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released); await Task.Delay(100);
                    Check(await browser.EvaluateFixtureAsync("frameClick?.clicked==='inner'&&frameClick?.trusted===true") == "true", "Frame jump and native click hit the expected control: " + origin);
                    browser.MoveFocus(FocusNavigationDirection.Down); await Until(() => !browser.SpatialBusyForValidation);
                    browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed); await Task.Delay(50);
                    browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released); await Task.Delay(100);
                    Check(await browser.EvaluateFixtureAsync("frameClick?.clicked==='inner2'") == "true", "Navigation continues within the frame: " + origin);
                }

                await Body("<button id=a class=fixed style='left:20px;top:100px'>A</button><iframe id=frame src='https://cross-origin.invalid/scroll-frame' style='position:absolute;left:220px;top:40px;width:300px;height:180px;border:2px solid white'></iframe>");
                await Task.Delay(350); await PointAt("a");
                browser.MoveFocus(FocusNavigationDirection.Right); await Until(() => !browser.SpatialBusyForValidation);
                browser.MoveFocus(FocusNavigationDirection.Down); await Until(() => !browser.SpatialBusyForValidation);
                Check(browser.LastSpatialMoveForValidation is { Scrolled: true, Sessions: >= 1 }, "Cross-process iframe scroll retains its owned region across rescan");
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed); await Task.Delay(50);
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released); await Task.Delay(100);
                Check(await browser.EvaluateFixtureAsync("frameClick?.clicked==='inner2'&&frameClick?.trusted===true") == "true", "Post-scroll native activation reaches the next control inside the cross-process frame");

                await Body("<button id=a class=fixed style='left:20px;top:60px'>A</button><iframe id=frame src='https://cross-origin.invalid/frame' style='position:absolute;left:220px;top:40px;width:300px;height:180px;border:2px solid white;transform:scale(.8);transform-origin:top left'></iframe>");
                await Task.Delay(300); await PointAt("a");
                browser.MoveFocus(FocusNavigationDirection.Right); await Until(() => !browser.SpatialBusyForValidation);
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed); await Task.Delay(50);
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released); await Task.Delay(100);
                Check(await browser.EvaluateFixtureAsync("frameClick?.clicked==='inner'&&frameClick?.trusted===true") == "true", "Scaled cross-site frame coordinates preserve native activation");

                await Body("<button id=a class=fixed style='left:20px;top:60px'>A</button><iframe src='https://cross-origin.invalid/frame' style='position:absolute;left:220px;top:40px;width:300px;height:180px;border:2px solid white'></iframe><div style='position:absolute;z-index:3;left:220px;top:40px;width:140px;height:150px;background:#444'></div><button id=b class=fixed style='left:490px;top:40px'>B</button>");
                await Task.Delay(300); await PointAt("a"); await Jump(FocusNavigationDirection.Right, "b");
                Check(true, "Parent-frame occlusion skips inaccessible child controls instead of trapping navigation");

                await Body("<button id=a class=fixed style='left:40px;top:40px'>A</button><button id=b class=fixed style='left:200px;top:40px'>B</button>");
                await PointAt("a");
                for (var i = 0; i < 3; i++) browser.HandleButton(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
                await Until(() => browser.FixtureZoomIdle); await Task.Delay(100);
                await PointAt("a"); await Jump(FocusNavigationDirection.Right, "b");
                Check(true, "Pinch-zoom viewport offsets preserve directional landing and native hover");
                for (var i = 0; i < 3; i++) browser.HandleButton(ControllerButton.LeftTrigger, ControllerEventPhase.Pressed);
                await Until(() => browser.FixtureZoomIdle); await Task.Delay(100);

                await Body("<button id=a class=fixed style='left:40px;top:40px'>A</button><button id=b class=fixed style='left:200px;top:40px'>B</button>" + string.Concat(Enumerable.Repeat("<div style='height:1px'></div>", 13000)));
                await PointAt("a"); await Jump(FocusNavigationDirection.Right, "b");
                Check(browser.LastSpatialMoveForValidation is { Truncated: true }, "Dense page scanning has a bounded node/time budget");
                var neutralRequests = browser.SpatialRequestsForValidation;
                for (var i = 0; i < 100; i++) browser.MovePointer(0, 0);
                await Task.Delay(150);
                Check(browser.SpatialRequestsForValidation == neutralRequests, "Neutral polling performs no geometry scans");

                await Body("<canvas id=canvas width=400 height=200 style='display:block'></canvas>");
                var canvasPosition = browser.SpatialPointerForValidation;
                browser.MoveFocus(FocusNavigationDirection.Right); await Until(() => !browser.SpatialBusyForValidation);
                Check(browser.SpatialPointerForValidation == canvasPosition && browser.SpatialErrorForValidation is null, "Canvas-only content falls back quietly without inventing controls");
                browser.MovePointer(.02, .02);
                Check(browser.SpatialPointerForValidation != canvasPosition, "Free pointer remains usable when a page exposes no targets");

                await Body("<button id=a class=fixed style='left:40px;top:40px'>A</button><button id=b class=fixed style='left:200px;top:40px'>B</button>");
                await PointAt("a");
                browser.MoveFocus(FocusNavigationDirection.Right);
                browser.MovePointer(.02, .03);
                var free = browser.SpatialPointerForValidation;
                await Until(() => !browser.SpatialBusyForValidation);
                Check(browser.SpatialPointerForValidation == free, "Free stick movement supersedes an in-flight jump");
                await PointAt("a");
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed);
                var held = browser.SpatialPointerForValidation;
                browser.MoveFocus(FocusNavigationDirection.Right); await Task.Delay(50);
                Check(browser.SpatialPointerForValidation == held, "D-pad does not teleport a held drag");
                browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released); await Task.Delay(80);
                await WebAppPointerMotionValidation.RunAsync(browser, Body, PointAt, Check);
                var repeated = browser.SpatialRequestsForValidation;
                for (var i = 0; i < 20; i++) browser.MoveFocus(FocusNavigationDirection.Right);
                await Until(() => !browser.SpatialBusyForValidation);
                Check(browser.SpatialRequestsForValidation - repeated <= 2, "Repeated input retains at most one pending navigation request");
                browser.MoveFocus(FocusNavigationDirection.Left);
                browser.SetPresentation(false, false);
                await Until(() => !browser.SpatialBusyForValidation);
                Check(true, "Held-direction pressure and presentation loss drain without a stale move");
            }
            catch (Exception failure) { error = failure.ToString() + "\nSpatial: " + browser.SpatialErrorForValidation; }
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, checks, timings, error }));

            void Check(bool ok, string description) { if (!ok) throw new InvalidOperationException(description); checks.Add(description); }
            async Task Body(string html)
            {
                await browser.EvaluateFixtureAsync("document.body.innerHTML=" + JsonSerializer.Serialize(html) + ";window.lastMove='';window.clicks=[];window.scrollTo(0,0)");
                await Task.Delay(60);
            }
            async Task PointAt(string id)
            {
                using var location = JsonDocument.Parse(await browser.EvaluateFixtureAsync("(()=>{let r=document.getElementById(" + JsonSerializer.Serialize(id) + ").getBoundingClientRect();return {x:(r.left+r.width/2-visualViewport.offsetLeft)/visualViewport.width,y:(r.top+r.height/2-visualViewport.offsetTop)/visualViewport.height}})()"));
                var current = browser.SpatialPointerForValidation;
                browser.MovePointer(location.RootElement.GetProperty("x").GetDouble() - current.X, location.RootElement.GetProperty("y").GetDouble() - current.Y);
                await Task.Delay(60);
            }
            async Task Jump(FocusNavigationDirection direction, string expected)
            {
                browser.MoveFocus(direction);
                await Until(() => !browser.SpatialBusyForValidation);
                await Task.Delay(60);
                var targetPointer = browser.SpatialPointerForValidation;
                var targetId = await browser.EvaluateFixtureAsync(FormattableString.Invariant($"document.elementFromPoint({targetPointer.X}*visualViewport.width+visualViewport.offsetLeft,{targetPointer.Y}*visualViewport.height+visualViewport.offsetTop)?.id"));
                Check(await browser.EvaluateFixtureAsync("lastMove") == JsonSerializer.Serialize(expected), "Directional target: " + expected + " actual=" + await browser.EvaluateFixtureAsync("lastMove") + " logicalTarget=" + targetId + " move=" + browser.LastSpatialMoveForValidation);
                if (browser.LastSpatialMoveForValidation is { } move) timings.Add(move);
            }
        };
    }
    private static async Task Until(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!ready()) await Task.Delay(20, timeout.Token);
    }
    public ValueTask DisposeAsync() => browser.DisposeAsync();
}
