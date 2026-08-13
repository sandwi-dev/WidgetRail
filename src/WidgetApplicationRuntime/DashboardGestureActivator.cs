using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.WidgetRuntime;

internal interface IDashboardGestureActivatingCapabilityClient
{
    void SetDashboardGestureActivator(
        Func<WidgetCapabilityGestureContext, string, string, CancellationToken, ValueTask<bool>>
            activator);
}
