using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task ValidateStoppedSurfaceAsync(Action<bool, string> check)
    {
        await SelectAsync("source");
        await ChangeWidgetStoppedAsync("source", stopped: true);
        await Wait(() => !switching && showingStoppedWidget && activeWidget == "source" && interactive);
        check(surface is null && owner!.Session.GetState("source")?.LastGood is null,
            "Stopped widget uses host chrome with no worker snapshot or retained presenter");
        await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Retry));
        check((string)Retry.Content == "Reload", "Stopped surface exposes a focused accessible Reload control");
        await Wait(() => trayGuide.DisplayedHints.Any(hint => hint.Button == ControllerButton.X));
        check(trayGuide.DisplayedHints.Any(hint => hint.Button == ControllerButton.X && hint.Label == "Reload"),
            "Stopped surface maps X to host Reload");
        await Press(ControllerButton.B);
        await Wait(() => RadialOpen);
        await Press(ControllerButton.B);
        await Wait(() => showingStoppedWidget && interactive && !RadialOpen);
        check(surface is null, "Radial return does not resurrect a stopped widget");
        await SelectAsync("settings");
        await SelectAsync("source");
        check(showingStoppedWidget && surface is null, "Switching away and back preserves stopped state");
        SetVisible(false);
        await Task.Delay(150);
        SetVisible(true);
        await Wait(() => !switching && showingStoppedWidget && visible && interactive);
        check(surface is null && owner!.Session.GetTarget("source").Descriptor.IsStopped,
            "Overlay hide and reopen do not restart a stopped widget");
        await Press(ControllerButton.X);
        await Wait(() => !switching && !showingStoppedWidget && surface?.CurrentBinding?.Frame.Descriptor.Id == "source");
        check(!owner!.Session.GetTarget("source").Descriptor.IsStopped,
            "Explicit X Reload clears persistent stop and presents the widget");

        async Task Press(ControllerButton button)
        {
            await RouteButtonAsync(button, ControllerEventPhase.Pressed);
            await RouteButtonAsync(button, ControllerEventPhase.Released);
        }
        async Task Wait(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 12000;
            while (!condition())
            {
                if (Environment.TickCount64 >= deadline) throw new TimeoutException($"Stopped surface did not settle: active={activeWidget}, requested={requestedWidget}, stopped={showingStoppedWidget}, switching={switching}, interactive={interactive}, status={Status.Text}");
                await Task.Delay(20);
            }
        }
    }
}
