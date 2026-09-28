using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Uses the actual pump, page navigation and window hide/show handlers.</summary>
internal sealed class ControllerReplayScenario : IDisposable
{
    private readonly DispatcherQueueTimer timer;
    private readonly Window window;
    private readonly ControllerValidationPage page;
    private readonly ReplayNativePlatform backend;
    private int step;
    private long deadline;

    public ControllerReplayScenario(Window window, ControllerValidationPage page, ReplayNativePlatform backend)
    {
        this.window = window; this.page = page; this.backend = backend;
        timer = window.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += (_, _) => Tick();
    }
    public void Start() { deadline = Environment.TickCount64 + 5000; timer.Start(); }
    private void Tick()
    {
        if (Environment.TickCount64 > deadline)
        {
            page.SetReplayStatus($"FAIL at step {step}; focus={page.FocusedId}; actions={page.ActionCount}");
            timer.Stop(); return;
        }
        switch (step)
        {
            case 0 when page.FocusedId == "Controller.First": backend.Move(NavigationDirection.Right); break;
            case 1 when page.FocusedId == "Controller.Second": backend.Activate(); break;
            case 2 when page.ActionCount == 1: backend.Toggle(); break;
            case 3 when !window.AppWindow.IsVisible: backend.Toggle(); break;
            case 4 when window.AppWindow.IsVisible && page.FocusedId == "Controller.Second": backend.Activate(); break;
            case 5 when page.ActionCount == 2:
                page.SetReplayStatus("PASS: cold focus, native-frame navigation, single action, hide/show, retained focus");
                timer.Stop(); return;
            default: return;
        }
        ++step;
        page.SetReplayStatus($"Replay step {step}");
        deadline = Environment.TickCount64 + 5000;
    }
    public void Dispose() => timer.Stop();
}
