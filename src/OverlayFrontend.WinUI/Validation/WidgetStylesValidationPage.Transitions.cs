using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeSectionTransitionsAsync()
    {
        var page = new WidgetViewPresenter { Width = 600, Height = 280 };
        host.Children.Add(page);
        Exception? failure = null;
        var invoked = 0;
        page.Failed = error => failure = error;
        page.DispatchActionAsync = _ => { ++invoked; return Task.CompletedTask; };
        page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full, WidgetAnimationSpeed = .5,
            SectionAnimation = WidgetSectionAnimation.Slide }, true);
        try
        {
            Render("a", 0);
            await Wait(() => Entry()?.ActualHeight > 0);
            var first = Entry()!;
            first.Focus(FocusState.Keyboard);
            Render("b", 1);
            Check(page.OutgoingTransitionCount == 1 && !first.IsHitTestVisible && !first.IsTabStop && !ReferenceEquals(first, Entry()),
                "section change retains one native outgoing tree while replacing input bindings immediately");
            first.Command!.Execute(null);
            Check(invoked == 0, "outgoing button token cannot dispatch against the newly committed section");
            var ghost = Descendants(page).OfType<WidgetOutgoingLayer>().Single();
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(ghost);
            Check(peer.GetChildren() is not { Count: > 0 }, "outgoing native tree contributes no automation children");
            await Wait(() => page.TransitionPlayback is not null || failure is not null);
            if (failure is not null) throw failure;
            var selectedButton = Descendants(page).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Widget.transition.tab-b");
            var selectedHost = Descendants(page).OfType<WidgetMotionHost>().Single(item => item.Layer.Children.Contains(selectedButton));
            Check(page.LastTransitionTargetCount == 4 && selectedHost.SelectionSurface?.Background is SolidColorBrush { Color.A: > 0 } &&
                selectedButton.Background is SolidColorBrush { Color.A: 0 } && selectedButton.Translation == System.Numerics.Vector3.Zero,
                "section, header and selection share one batch while selection labels keep their own stationary visual");
            Check(await page.TransitionPlayback! == WidgetMotionOutcome.Completed && page.OutgoingTransitionCount == 0,
                "real declaration section motion completes on native layers and retires outgoing resources");
            var second = Entry()!;
            var completed = page.TransitionPlayback;
            Render("b", 1);
            Check(ReferenceEquals(second, Entry()) && ReferenceEquals(completed, page.TransitionPlayback),
                "same section snapshots preserve native identity and do not replay content motion");
            second.Command!.Execute(null);
            Check(invoked == 1, "incoming native button remains actionable exactly once");
            Render("c", 2);
            await Wait(() => page.TransitionPlayback != completed || failure is not null);
            if (failure is not null) throw failure;
            var interrupted = page.TransitionPlayback;
            Render("d", 3);
            Check(page.OutgoingTransitionCount == 1, "rapid section replacement retains only the latest outgoing tree");
            await Wait(() => page.TransitionPlayback != interrupted || failure is not null);
            if (failure is not null) throw failure;
            await page.TransitionPlayback!;
            Render("e", 4);
            page.Width = 500;
            await Wait(() => page.OutgoingTransitionCount == 0);
            Check(page.OutgoingTransitionCount == 0, "responsive resize settles motion and releases stale outgoing geometry");
            page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
            Render("f", 5);
            Check(page.OutgoingTransitionCount == 0, "reduced motion replaces sections without retaining outgoing controls");
            page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full }, true);
            Render("g", 6);
            await page.DisposeAsync();
            Check(page.OutgoingTransitionCount == 0, "presenter disposal cancels pending section motion and releases outgoing content");
        }
        finally { host.Children.Remove(page); await page.DisposeAsync(); }

        void Render(string key, int order)
        {
            var root = new ViewNode { Id = "transition.root", Kind = ViewNodeKind.Stack, Children = [
                new() { Id = "transition.tabs", Kind = ViewNodeKind.Row, Children = [
                    new() { Id = "transition.tab-a", Kind = ViewNodeKind.Button, Text = "First", ActionId = "first", IsSelected = order % 2 == 0,
                        Transition = new("section", key, order, WidgetTransitionKind.Selection) },
                    new() { Id = "transition.tab-b", Kind = ViewNodeKind.Button, Text = "Second", ActionId = "second", IsSelected = order % 2 != 0,
                        Transition = new("section", key, order, WidgetTransitionKind.Selection) }] },
                new() { Id = "transition.header", Kind = ViewNodeKind.Text, Text = "Section " + key,
                    Transition = new("section", key, order, WidgetTransitionKind.Layout) },
                new() { Id = "transition.content", Kind = ViewNodeKind.Stack, Transition = new("section", key, order), Children = [
                    new() { Id = "transition.entry", Kind = ViewNodeKind.Button, Text = "Current " + key, ActionId = "activate" }] }] };
            var styles = new Dictionary<string, BridgeNodeRenderStyles>();
            foreach (var (id, selected) in new[] { ("transition.tab-a", order % 2 == 0), ("transition.tab-b", order % 2 != 0) })
                styles[id] = Compute($"button {{ width: 120px; height: 44px; color: #ffffff; background: {(selected ? "#224466" : "transparent")}; corner-radius: 6px; }}", id, "button");
            page.Apply(CreateFrame(root, styles));
        }
        Button? Entry() => Descendants(page).OfType<Button>().FirstOrDefault(element =>
            AutomationProperties.GetAutomationId(element) == "Widget.transition.entry" && element.IsHitTestVisible);
        static IEnumerable<DependencyObject> Descendants(DependencyObject owner)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); ++i)
            {
                var child = VisualTreeHelper.GetChild(owner, i); yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }
}
