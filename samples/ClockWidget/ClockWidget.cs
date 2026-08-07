using System.Globalization;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.ClockWidget;

public sealed class ClockWidget(TimeProvider? timeProvider = null) : Widget
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public override WidgetView Render()
    {
        var now = _timeProvider.GetLocalNow();
        return new WidgetView(
            UI.Stack("clock-root",
                UI.Text(now.ToString("h:mm tt", CultureInfo.InvariantCulture), "clock-time", "Current time")
                    .Classes("clock-time"),
                UI.Text(now.ToString("dddd, MMMM d", CultureInfo.InvariantCulture), "clock-date", "Current date")
                    .Classes("clock-date"),
                UI.Row("clock-actions",
                    UI.Button("Refresh", "refresh", "refresh")
                        .Shortcut(ControllerButton.X)
                        .Classes("primary-action"))),
            InitialFocusId: "refresh");
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (action.ActionId == "refresh")
            Invalidate();
        return ValueTask.CompletedTask;
    }
}
