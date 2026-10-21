using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal async Task<IReadOnlyList<string>> ValidateWidgetNavigationFocusAsync()
    {
        if (owner is not null || startup is not null) throw new InvalidOperationException("Synthetic shell only.");
        var checks = new List<string>();
        var trace = new List<string>();
        var descriptor = new BridgeWidgetDescriptor { Id = "focus.fixture", Name = "Synthetic music", Icon = WidgetGlyph.Music,
            InstanceId = "focus.instance", RuntimeGeneration = "runtime", PresentationGeneration = "presentation", PackageContentDigest = "", NotificationCategories = (WidgetNotificationCategory[])[new("songs", "Song changes", "Brief banners with artwork, artist and duration.", false, Retention: WidgetNotificationRetention.BannerOnly)] };
        long sequence = 0;
        notificationBell.GettingFocus += (_, args) => trace.Add($"bell direction={args.Direction} state={args.FocusState} applying={surface?.IsApplyingPresentation} interactive={interactive}");
        visible = foreground = true;
        Render("library");
        SetInteractive(true);
        surface!.SetAutomaticFocusEnabled(true);
        surface.Enter();
        await Settled("Widget.library.open");
        foreach (var page in new[] { "settings", "library", "settings", "library" })
        {
            ((Control)FocusManager.GetFocusedElement(XamlRoot)).Focus(FocusState.Pointer);
            Render(page);
            await Settled("Widget." + page + ".open");
            if (!interactive) throw new InvalidOperationException("Native page replacement gave shell ownership: " + string.Join("; ", trace));
            checks.Add(page + " replacement keeps native focus and logical widget ownership");
        }
        SetInteractive(false);
        notificationBell.Focus(FocusState.Keyboard);
        await Task.Delay(50);
        if (notificationBell.FocusState == FocusState.Unfocused || interactive) throw new InvalidOperationException("Explicit bell entry was rejected.");
        checks.Add("Intentional shell entry still focuses the bell");
        Render("settings");
        await Task.Delay(100);
        if (notificationBell.FocusState == FocusState.Unfocused) throw new InvalidOperationException("Background snapshot stole deliberate bell focus.");
        checks.Add("Background widget publication preserves deliberate bell focus");
        checks.AddRange(trace);
        checks.AddRange(await ValidateNotificationCategoryUiAsync(descriptor));
        return checks;

        void Render(string page)
        {
            var snapshot = new ViewSnapshot { WidgetInstanceId = descriptor.InstanceId, Sequence = ++sequence,
                ActiveInputScopeId = "music.root", InitialFocusId = page + ".open",
                Root = new() { Id = "music.root", InputScopeId = "music.root", Kind = ViewNodeKind.Stack,
                    Children = (ViewNode[])[new() { Id = page + ".body", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[
                        new() { Id = page + ".open", Kind = ViewNodeKind.Button, Text = page, ActionId = "open" },
                        new() { Id = page + ".other", Kind = ViewNodeKind.Button, Text = "Other control", ActionId = "other" }] }] } };
            var frame = new WidgetPresentationFrame(new(descriptor.Id, "runtime", "presentation", 1, descriptor.InstanceId, sequence, "music.root"),
                descriptor, snapshot, new Dictionary<string, BridgeNodeRenderStyles>());
            if (surface is null) ApplyLayoutFixture(frame, AppearanceSettings.Default, (BridgeWidgetDescriptor[])[descriptor]);
            else surface.Apply(frame);
            ConfigureProductionViewport(new(1400, 900));
            UpdateLayout();
        }
        async Task Settled(string id)
        {
            var deadline = Environment.TickCount64 + 2500;
            while (FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject focused || AutomationProperties.GetAutomationId(focused) != id)
            {
                if (Environment.TickCount64 >= deadline) throw new InvalidOperationException($"Focus did not reach {id}; interactive={interactive}; " + string.Join("; ", trace));
                await Task.Delay(16);
            }
        }
    }
}
