using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

internal interface IDashboardGestureActivatingCapabilityClient
{
    void SetDashboardGestureActivator(
        Func<WidgetCapabilityGestureContext, string, string, CancellationToken, ValueTask<bool>>
            activator);
}
