using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class ShellChromeValidationPage
{
    private async Task CheckGuideMotionAsync(IReadOnlyDictionary<string, BridgeNodeRenderStyles> palette, AppearanceSettings appearance)
    {
        appearance = appearance with { Motion = MotionPreference.Full, SectionAnimation = WidgetSectionAnimation.Slide,
            WidgetAnimationSpeed = .5, Contrast = ContrastPreference.Standard, Transparency = TransparencyPreference.Full };
        UpdateStyles(palette, appearance, true);
        void Present(string context, string label, bool ready = true) => guide.Present(context, ready,
            (ControllerGuideHint[])[new(ControllerPrompt.A, label, ControllerButton.A)], ControllerGuideModel.WithHost([]));
        Present("motion-before", "Before"); Host.UpdateLayout();
        await Until(() => !guide.IsFading);
        var height = guide.ActualHeight;
        var oldButton = ((Panel)guide.BackgroundSurface).Children.OfType<Button>().First();
        var oldInvoke = (IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(oldButton).GetPattern(PatternInterface.Invoke);
        var invoked = 0;
        void Count(ControllerGuideHint _) => ++invoked;
        guide.Invoked += Count;
        try
        {
            var count = guide.FadeCount;
            Present("motion-after", "After"); Host.UpdateLayout();
            Check(guide.IsFading && guide.FadeCount == count + 1 && guide.LastFadeDuration.TotalMilliseconds == 280,
                "committed whole guide starts one compositor crossfade at shared animation speed");
            Check(guide.OutgoingIsPassive && guide.HelpText.Contains("After") && Math.Abs(height - guide.ActualHeight) < .1,
                "outgoing guide is input/accessibility passive while current labels keep a stable height");
            oldInvoke.Invoke();
            Check(invoked == 0, "retired guide control cannot invoke the newly published action");
            var firstPlayback = guide.FadePlayback!;
            Present("motion-latest", "Latest"); Host.UpdateLayout();
            Check(await firstPlayback == WidgetMotionOutcome.Superseded && guide.IsFading && guide.HelpText.Contains("Latest"),
                "interrupted fade replaces its destination immediately without queued intermediate guides");
            await Until(() => !guide.IsFading);
            Check(guide.FadeFailure is null && await guide.FadePlayback! == WidgetMotionOutcome.Completed,
                "latest fade completes and releases its outgoing layer");
            var currentButton = ((Panel)guide.BackgroundSurface).Children.OfType<Button>().First();
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(currentButton).GetPattern(PatternInterface.Invoke)).Invoke();
            Check(invoked == 1, "incoming guide retains normalized pointer activation after animation");
            count = guide.FadeCount;
            Present("motion-latest", "Latest");
            Present("motion-pending", "Pending", false);
            await Task.Delay(160);
            Check(guide.FadeCount == count && !guide.IsFading && guide.HelpText.Contains("Latest"),
                "unchanged and unready guide proposals never start a fade");
            Present("motion-pending", "Pending");
            Check(guide.IsFading, "ready publication starts the fade without another settling delay");
            UpdateStyles(palette, appearance with { Motion = MotionPreference.Reduced }, true);
            Check(!guide.IsFading && guide.HelpText.Contains("Pending"), "reduced motion immediately settles an active fade");
            foreach (var (settings, animations) in new[] {
                (appearance with { Motion = MotionPreference.Reduced }, true),
                (appearance with { Motion = MotionPreference.System }, false),
                (appearance with { SectionAnimation = WidgetSectionAnimation.None }, true),
                (appearance with { Contrast = ContrastPreference.High }, true) })
            {
                UpdateStyles(palette, settings, animations);
                Present(Guid.NewGuid().ToString(), "Immediate");
                Check(!guide.IsFading, "disabled motion or high contrast publishes guide immediately");
            }
            UpdateStyles(palette, appearance, true);
            Present("motion-hide", "Hide");
            guide.Present("hidden", true, [], [], hide: true);
            Check(!guide.IsFading && guide.Opacity == 0, "hiding the guide cancels outgoing animation retention");
        }
        finally { guide.Invoked -= Count; UpdateStyles(palette, appearance with { WidgetAnimationSpeed = 1 }, true); SetGuideState(false, false); }
    }
}
