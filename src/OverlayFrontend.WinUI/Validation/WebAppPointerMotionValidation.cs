using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal static class WebAppPointerMotionValidation
{
    internal static async Task RunAsync(BrowserSurface browser, Func<string, Task> body, Func<string, Task> pointAt,
        Action<bool, string> check)
    {
        await body("<style>#submenu{display:none}#b:hover #submenu{display:block}</style><button id=a class=fixed style='left:40px;top:40px'>A</button><div role=button id=b class=fixed style='left:200px;top:40px;background:#456'>Menu<button id=submenu style='position:absolute;top:45px'>Submenu</button></div><button id=c class=fixed style='left:360px;top:40px'>C</button>");
        await pointAt("a");
        var motion = browser.FixturePointerMotionEnabled;
        browser.SampleDpadHold(true);
        browser.MoveFocus(FocusNavigationDirection.Right);
        await Until(() => !browser.SpatialBusyForValidation);
        check(await browser.EvaluateFixtureAsync("getComputedStyle(submenu).display") == "\"none\"", "Held D-pad visits menu without opening its hover submenu");
        if (motion) check(browser.FixturePointerGliding, "D-pad jump starts a compositor glide");
        browser.MoveFocus(FocusNavigationDirection.Right);
        await Until(() => !browser.SpatialBusyForValidation);
        check(await browser.EvaluateFixtureAsync("c.matches(':hover')") == "false", "Repeated D-pad visits do not deliver intermediate hover");
        browser.SampleDpadHold(false);
        await Script("c.matches(':hover')");
        await Until(() => !browser.FixturePointerGliding);
        check(true, "Release delivers final hover and the glide completes");

        await pointAt("a");
        browser.SampleDpadHold(true); browser.MoveFocus(FocusNavigationDirection.Right); browser.SampleDpadHold(false);
        await Until(() => !browser.SpatialBusyForValidation);
        await Script("b.matches(':hover')");
        check(true, "Release before the async jump completes still delivers the final hover");
        browser.SampleDpadHold(true); browser.MoveFocus(FocusNavigationDirection.Down);
        await Until(() => !browser.SpatialBusyForValidation);
        check(await browser.EvaluateFixtureAsync("getComputedStyle(submenu).display!=='none'") == "true", "An existing hover menu stays open while navigating into its children");
        browser.SampleDpadHold(false); await Script("submenu.matches(':hover')");
        check(true, "D-pad can reach and hover a child of an already-open menu");

        await pointAt("a");
        browser.SampleDpadHold(true); browser.MoveFocus(FocusNavigationDirection.Right);
        await Until(() => !browser.SpatialBusyForValidation);
        browser.HandleButton(ControllerButton.A, ControllerEventPhase.Pressed);
        check(!browser.FixturePointerGliding, "A immediately settles the circle before its native press");
        await Task.Delay(40); browser.HandleButton(ControllerButton.A, ControllerEventPhase.Released);
        await Script("clicks.includes('b')");
        browser.SampleDpadHold(false);
        check(true, "A during held D-pad synchronizes and clicks the logical target");

        await pointAt("a");
        browser.SampleDpadHold(true); browser.MoveFocus(FocusNavigationDirection.Right);
        await Until(() => !browser.SpatialBusyForValidation);
        browser.MovePointer(.01, 0); await Script("b.matches(':hover')");
        browser.SampleDpadHold(false);
        check(!browser.FixturePointerGliding, "Free-stick takeover cancels glide and resumes native hover immediately");

        if (motion)
        {
            browser.MovePointer(.01, .01); await Task.Delay(20); browser.MovePointer(.01, .01);
            check(browser.FixturePointerTrailCount is > 0 and <= 7, "Analog movement uses a bounded subtle trail pool");
            await Task.Delay(140);
            check(browser.FixturePointerTrailCount == 0, "Analog trail expires without further input");
        }
        browser.FixturePointerReducedMotion = true;
        await pointAt("a"); browser.MoveFocus(FocusNavigationDirection.Right);
        await Until(() => !browser.SpatialBusyForValidation);
        check(!browser.FixturePointerGliding && browser.FixturePointerTrailCount == 0, "Reduced motion keeps immediate input without glide or trail");
        browser.FixturePointerReducedMotion = false;

        await pointAt("a"); browser.SampleDpadHold(true); browser.MoveFocus(FocusNavigationDirection.Right);
        await Until(() => !browser.SpatialBusyForValidation);
        browser.SetPresentation(false, false); browser.SampleDpadHold(false);
        check(!browser.FixturePointerGliding && browser.FixturePointerTrailCount == 0, "Presentation loss clears pending motion and particles");
        browser.SetPresentation(true, true); browser.Enter();

        async Task Script(string expression)
        {
            var end = Environment.TickCount64 + 3000;
            while (await browser.EvaluateFixtureAsync(expression) != "true")
            { if (Environment.TickCount64 > end) throw new InvalidOperationException("Pointer fixture timed out: " + expression); await Task.Delay(20); }
        }
    }
    private static async Task Until(Func<bool> condition)
    {
        var end = Environment.TickCount64 + 3000;
        while (!condition())
        { if (Environment.TickCount64 > end) throw new InvalidOperationException("Pointer fixture operation timed out"); await Task.Delay(10); }
    }
}
