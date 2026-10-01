using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

internal sealed partial class PlatformInputPump
{
    private readonly ControllerControlPreference controllerPreference = new();
    private bool controllerRecoveryBlocked;
    private Task controllerInitialization = Task.CompletedTask;
    private bool controllerInitializationStarted;

    internal void InitializeControllerSettings(Func<Task<ControllerSettings>> load)
    {
        if (controllerInitializationStarted) throw new InvalidOperationException("Controller startup settings already initialized.");
        controllerInitializationStarted = true;
        controllerInitialization = LoadAsync();
        async Task LoadAsync()
        {
            try
            {
                var preference = await load();
                if (!closed) ApplyControllerControlPreference(preference);
            }
            catch (Exception error)
            {
                if (!closed) diagnostics.Write($"Controller startup preference unavailable: {error.GetType().Name}");
            }
        }
    }

    internal async Task<ControllerControlStatus> ReadControllerControlAsync(CancellationToken token)
    {
        if (closed || session is null) return ControllerControlStatus.Unavailable;
        // Persisted control/shortcut intent applies on resident startup, even
        // while the lazy widget service is still unopened. Its reply must not
        // race a newer broker preference back into an older revision.
        await controllerInitialization.WaitAsync(token);
        if (closed || session is null) return ControllerControlStatus.Unavailable;
        // This static native query only inspects prerequisites. Keep driver/service
        // discovery off the UI thread and outside the session's input lifetime lock.
        var prerequisites = await Task.Run(nativeBackend.ControllerPrerequisites, token);
        if (closed || session is null || token.IsCancellationRequested) return ControllerControlStatus.Unavailable;
        var state = session.ControllerControlState;
        return new(state switch
        {
            PlatformControllerControlState.Off => ControllerControlState.Off,
            PlatformControllerControlState.Starting => ControllerControlState.Starting,
            PlatformControllerControlState.Active => ControllerControlState.Active,
            PlatformControllerControlState.WaitingForController => ControllerControlState.WaitingForController,
            PlatformControllerControlState.RecoveryRequired => ControllerControlState.RecoveryRequired,
            PlatformControllerControlState.Failed => ControllerControlState.Failed,
            _ => ControllerControlState.Unavailable,
        }, (prerequisites & 1) != 0, (prerequisites & 2) != 0, (prerequisites & 4) != 0);
    }

    internal void ApplyControllerControlPreference(ControllerSettings settings) => Guard(() =>
    {
        if (session is null) return;
        var wasRecoveryBlocked = controllerRecoveryBlocked;
        var result = controllerPreference.Apply(settings, session.SetExclusiveControl);
        controllerRecoveryBlocked = session.ControllerControlState == PlatformControllerControlState.RecoveryRequired;
        if (result is not null || wasRecoveryBlocked != controllerRecoveryBlocked)
        {
            inputOwnership.Reset();
            shortcutConsumed = false;
            if (controllerRecoveryBlocked) navigationTimer.Stop();
            else
            {
                session.SetWindowState(visible, IsForegroundProcess());
                if (visible) { session.PrimeController(IsForegroundProcess(), Now); navigationTimer.Start(); }
            }
            UpdateGuidePolling();
            diagnostics.Write($"Exclusive control requested={settings.ExclusiveControl} revision={settings.Revision} accepted={result} state={session.ControllerControlState}");
        }
        if (!controllerRecoveryBlocked) ApplyControllerSettings(settings);
    });
}
