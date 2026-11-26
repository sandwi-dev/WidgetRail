using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteWidgetLaunchResult(WidgetAppLaunchObservation Observation, string? Message = null, PlayniteLaunchConflict? Conflict = null,
    PlayniteWindowBinding? NeedsWindow = null);
