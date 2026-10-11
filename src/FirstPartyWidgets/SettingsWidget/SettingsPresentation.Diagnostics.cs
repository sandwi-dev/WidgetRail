using System.Globalization;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal static partial class SettingsPresentation
{
    private static WidgetView RenderDiagnostics(
        StackElement header,
        SettingsPresentationState state)
    {
        var diagnostics = state.Diagnostics;
        var health = DiagnosticHealth.From(diagnostics);
        var invalidThemes = state.Themes.Themes.Count(theme => !theme.IsValid);
        var runningWorkers = diagnostics.Workers.Count(worker => worker.IsRunning);
        var failedWorkers = diagnostics.Workers.Count(worker => worker.LastFailureCode is not null);
        var areas = new[]
        {
            diagnostics.Bridge,
            diagnostics.Catalog,
            diagnostics.Appearance,
            diagnostics.Providers,
            diagnostics.Consent,
            diagnostics.Overlay,
            diagnostics.Guide,
        };
        var children = new List<WidgetElement>
        {
            UI.Text("Diagnostics", "diagnostics.heading", "Settings diagnostics")
                .Classes("page-heading"),
            UI.Text(health.NeedsAttention || !state.SettingsValid || invalidThemes != 0
                    ? "Some items need attention" : "No current issues reported",
                "diagnostics.health", "Overall health").Classes(health.NeedsAttention || !state.SettingsValid || invalidThemes != 0
                    ? "diagnostic-error" : "diagnostic-ok"),
            UI.Text("Latest recorded failure for each widget in this session. A recovered failure does not mean the widget is still broken.",
                "diagnostics.summary.help").Classes("page-help"),
            UI.Button("Export support report", "diagnostics.export", "diagnostics.export")
                .Busy(state.Busy).Classes("setting-row"),
            UI.Text("Saves app and system versions, service status, widget identifiers and failure timestamps to Documents → WidgetRail → Support. " +
                "No raw logs, credentials, chat, captures or browsing history. Nothing is uploaded.",
                "diagnostics.export.help").Classes("page-help"),
        };
        if (state.SupportExportFile is { } saved)
            children.Add(UI.Text($"Last report: {saved}", "diagnostics.export.saved").Classes("diagnostic-ok"));
        if (health.StoppedFailures != 0 || diagnostics.Catalog.State != PlatformDiagnosticState.Healthy)
            children.Add(UI.Button("Manage widgets", "open.installed-widgets", "diagnostics.manage-widgets")
                .Busy(state.Busy).Classes("setting-row"));
        if (health.ControllerProblem)
            children.Add(UI.Button("Review controller access", "open.controller-help", "diagnostics.controllers")
                .Busy(state.Busy).Classes("setting-row"));
        children.Add(UI.Button(state.DiagnosticDetails ? "Hide service details" : "Show service details",
            "diagnostics.details.toggle", "diagnostics.details").Busy(state.Busy).Classes("setting-row"));
        if (state.DiagnosticDetails)
        {
            children.Add(UI.Text(state.SettingsValid ? "Settings file: valid" : "Settings file: invalid; defaults shown",
                "diagnostics.settings").Classes(state.SettingsValid ? "diagnostic-ok" : "diagnostic-error"));
            children.Add(UI.Text($"Theme packages: {state.Themes.Themes.Count} total, {invalidThemes} invalid",
                "diagnostics.themes").Classes(invalidThemes == 0 ? "diagnostic-ok" : "diagnostic-error"));
            children.Add(UI.CodeText($"Schema: {state.Settings.SchemaVersion}; runtime snapshot {diagnostics.Revision}",
                "diagnostics.schema").AddClasses("diagnostic-line"));
            children.AddRange(areas.Select(area => UI.Text(
                $"{DiagnosticPrefix(area.State)} {area.Label}: {area.Summary}",
                $"diagnostics.area.{area.Id}", $"{area.Label}: {area.State}; {area.Summary}").Classes(
                    area.State == PlatformDiagnosticState.Healthy ? "diagnostic-ok" :
                    area.State == PlatformDiagnosticState.Unavailable && area.Id is "overlay" or "guide"
                        ? "diagnostic-line" : "diagnostic-error")));
            children.Add(UI.Text("Unavailable overlay/Guide telemetry means this build does not report it; it does not prove an input failure.",
                "diagnostics.telemetry.help").Classes("page-help"));
        }
        children.Add(UI.Text(
            $"Widgets: {runningWorkers}/{diagnostics.Workers.Count} running; " +
            $"{failedWorkers} with a recorded failure",
            "diagnostics.workers", "Widget worker status").Classes(
                health.StoppedFailures == 0 ? "diagnostic-line" : "diagnostic-error"));
        if (health.StoppedFailures != 0)
            children.Add(UI.Text("To reload a widget, return to the tray, select it and hold Y (Triangle). Reloading may reset its current page.",
                "diagnostics.reload.help").Classes("page-help"));
        foreach (var worker in diagnostics.Workers
                     .Where(worker => worker.LastFailureCode is not null)
                     .OrderByDescending(worker => worker.LastFailureAtUtc)
                     .Take(16))
        {
            children.Add(UI.Text(
                $"{worker.WidgetName} · " +
                (worker.LastFailureAtUtc is { } when ? when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "Time unavailable") +
                $" · {SupportBundle.FailureDescription(worker.LastFailureCode!)}" +
                (state.DiagnosticDetails ? $" ({worker.LastFailureCode})" : "") + "; " +
                (worker.IsRunning
                    ? "running after the recorded failure"
                    : worker.CanRestart ? "will restart on demand" : "restart limit reached"),
                $"diagnostics.worker.{worker.WidgetId}",
                $"{worker.WidgetName} worker failure").Classes(worker.IsRunning ? "diagnostic-line" : "diagnostic-error"));
        }
        if (failedWorkers > 16)
            children.Add(UI.Text($"Showing the 16 most recent of {failedWorkers} widget failures. Export a support report for all recorded failures.",
                "diagnostics.failures.more").Classes("page-help"));
        var recoveryOrder = SettingsAuthorityRecoveryPolicy.OrderedIndexes(diagnostics);
        children.Add(UI.Text(
            recoveryOrder.Length == 0
                ? "Content authority recovery: no pending records"
                : $"Content authority recovery: {recoveryOrder.Length} " +
                  (recoveryOrder.Length == 1
                      ? "record requires review"
                      : "records require review"),
            "diagnostics.authority.summary",
            recoveryOrder.Length == 0
                ? "No content authority recovery records are pending"
                : $"{recoveryOrder.Length} content authority recovery " +
                  (recoveryOrder.Length == 1
                      ? "record requires review"
                      : "records require review"))
            .Classes(recoveryOrder.Length == 0 ? "diagnostic-ok" : "diagnostic-error"));
        for (var displayIndex = 0; displayIndex < recoveryOrder.Length; displayIndex++)
        {
            var recovery = diagnostics.AuthorityRecoveries[recoveryOrder[displayIndex]];
            children.Add(UI.Button(
                    $"Review recovery · {recovery.DisplayName} · {recovery.State}",
                    $"authority.recovery.select.{displayIndex}",
                    $"diagnostics.authority.item.{displayIndex}")
                .Busy(state.Busy)
                .Classes(recovery.CanRetry ? "danger-button" : "secondary-button") with
                {
                    AccessibilityLabel =
                        $"Review content authority recovery {recovery.DisplayName}, " +
                        $"state {recovery.State}, status {recovery.StatusCode}, " +
                        (recovery.CanRetry ? "retry available" : "retry unavailable"),
                });
        }
        children.Add(UI.Button("Refresh diagnostics", "refresh", "diagnostics.refresh")
            .Icon(WidgetGlyph.Refresh, "Refresh diagnostics")
            .Busy(state.Busy).Classes("primary-button"));
        children.Add(UI.Button("Back", "back", "diagnostics.back")
            .Classes("secondary-button"));
        LinkVertical(children);
        return View(header,
            PageScope("diagnostics.page", children.ToArray()),
            recoveryOrder.Length == 0
                ? "diagnostics.refresh"
                : "diagnostics.authority.item.0",
            "diagnostics.page");
    }

}
