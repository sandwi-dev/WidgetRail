using WidgetRail.WidgetProtocol;

namespace WidgetRail.FirstPartyWidgets.Settings;

public sealed partial class SettingsWidget
{
    private static readonly CompiledWidgetIntentContract widgetSettingsIntent = CompiledWidgetIntentContract.Create(WidgetIntentContracts.WidgetSettings);
    private static readonly CompiledWidgetIntentContract widgetPermissionsIntent = CompiledWidgetIntentContract.Create(WidgetIntentContracts.WidgetPermissions);
    private static readonly CompiledWidgetIntentContract appUpdatesIntent = CompiledWidgetIntentContract.Create(WidgetIntentContracts.AppUpdates);

    // Intents only navigate. They never grant permissions, enable widgets, or
    // accept a package review on behalf of the user.
    public override async ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request, CancellationToken cancellationToken = default)
    {
        var contract = request.ContractId == widgetSettingsIntent.Id ? widgetSettingsIntent :
            request.ContractId == widgetPermissionsIntent.Id ? widgetPermissionsIntent : request.ContractId == appUpdatesIntent.Id ? appUpdatesIntent : null;
        if (!IsActive || contract is null || !request.IsWellFormed() || request.Version != contract.Version ||
            request.SchemaDigest != contract.SchemaDigest || !contract.Accepts(request.Payload)) return WidgetIntentResult.Rejected;
        var packageId = contract == appUpdatesIntent ? null : request.Payload.GetProperty("packageId").GetString()!;
        await _initializationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsActive) return WidgetIntentResult.Rejected;
            if (contract == appUpdatesIntent)
            {
                Navigate(SettingsPage.AppUpdates);
                await ReadAppUpdatesAsync(new("status"), cancellationToken).ConfigureAwait(false);
            }
            else if (contract == widgetPermissionsIntent)
            {
                await ReloadPermissionsAsync(cancellationToken).ConfigureAwait(false);
                lock (_stateLock)
                {
                    var index = _permissionState.Projection.Packages.ToList().FindIndex(package => package.Id == packageId);
                    if (!SettingsPermissionPolicy.TrySelectPackage(_permissionState, index, out var transition)) return WidgetIntentResult.Rejected;
                    _permissionState = transition.State; _page = transition.Page;
                }
            }
            else
            {
                await ReloadInstalledWidgetsAsync(cancellationToken).ConfigureAwait(false);
                int installed, builtIn;
                lock (_stateLock)
                {
                    if (!_installedState.CatalogValid) return WidgetIntentResult.Rejected;
                    installed = _installedState.Catalog.Widgets.ToList().FindIndex(widget => widget.Id == packageId);
                    builtIn = _installedState.BuiltIns.ToList().FindIndex(widget => widget.Id == packageId);
                }
                if (builtIn >= 0) await SelectBuiltInWidgetAsync(builtIn, cancellationToken).ConfigureAwait(false);
                else if (installed >= 0) await SelectInstalledWidgetAsync(installed, cancellationToken).ConfigureAwait(false);
                else return WidgetIntentResult.Rejected;
            }
            SetOperation("Ready", false, false);
            EnsureSettingsStatusPolling();
            Invalidate();
            return WidgetIntentResult.Accepted;
        }
        finally { _operationGate.Release(); }
    }
}
