using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Settings;

public sealed partial class SettingsWidget
{
    private static readonly CompiledWidgetIntentContract widgetSettingsIntent = CompiledWidgetIntentContract.Create(WidgetIntentContracts.WidgetSettings);
    private static readonly CompiledWidgetIntentContract widgetPermissionsIntent = CompiledWidgetIntentContract.Create(WidgetIntentContracts.WidgetPermissions);
    private static readonly CompiledWidgetIntentContract appUpdatesIntent = CompiledWidgetIntentContract.Create(WidgetIntentContracts.AppUpdates);
    private static readonly CompiledWidgetIntentContract appearanceIntent = CompiledWidgetIntentContract.Create(WidgetIntentContracts.Appearance);
    private const string IntentNavigationKey = "settings.intent-navigation";
    private sealed record IntentNavigation(WidgetIntentRequest Request, string? Error = null);
    private IntentNavigation? intentNavigation;
    internal Task IntentNavigationTask { get; private set; } = Task.CompletedTask;

    // Accept navigation data promptly; inspections belong to the destination's
    // loading state, not to the caller's bounded intent acknowledgement.
    public override ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contract = request.ContractId == widgetSettingsIntent.Id ? widgetSettingsIntent :
            request.ContractId == widgetPermissionsIntent.Id ? widgetPermissionsIntent : request.ContractId == appUpdatesIntent.Id ? appUpdatesIntent :
            request.ContractId == appearanceIntent.Id ? appearanceIntent : null;
        if (!IsActive || contract is null || !request.IsWellFormed() || request.Version != contract.Version ||
            request.SchemaDigest != contract.SchemaDigest || !contract.Accepts(request.Payload)) return ValueTask.FromResult(WidgetIntentResult.Rejected);
        var navigation = new IntentNavigation(request);
        lock (_stateLock) { intentNavigation = navigation; _page = SettingsPage.IntentDestination; }
        var operation = Operations.RunLatest(IntentNavigationKey, context => ResolveIntentDestinationAsync(navigation, context));
        IntentNavigationTask = operation.Completion;
        if (!operation.IsAccepted)
        {
            lock (_stateLock) { if (ReferenceEquals(intentNavigation, navigation)) { intentNavigation = null; _page = SettingsPage.Root; } }
            return ValueTask.FromResult(WidgetIntentResult.Rejected);
        }
        PublishSettingsChanged();
        return ValueTask.FromResult(WidgetIntentResult.Accepted);
    }

    private async ValueTask ResolveIntentDestinationAsync(IntentNavigation navigation, WidgetOperationContext context)
    {
        await Task.Yield();
        var token = context.CancellationToken;
        bool Current()
        {
            lock (_stateLock) return context.IsCurrent && ReferenceEquals(intentNavigation, navigation) && IsActive;
        }
        try
        {
            await _initializationTask.WaitAsync(token).ConfigureAwait(false);
            var request = navigation.Request;
            var updates = request.ContractId == appUpdatesIntent.Id;
            var appearance = request.ContractId == appearanceIntent.Id;
            var permissions = request.ContractId == widgetPermissionsIntent.Id;
            var packageId = updates || appearance ? null : request.Payload.GetProperty("packageId").GetString()!;
            await _operationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!Current()) return;
                if (appearance)
                {
                    lock (_stateLock) { if (!Current()) return; _page = SettingsPage.Appearance; }
                }
                else if (updates)
                {
                    lock (_stateLock) { if (!Current()) return; _page = SettingsPage.AppUpdates; }
                }
                else if (permissions)
                {
                    await ReloadPermissionsAsync(token).ConfigureAwait(false);
                    lock (_stateLock)
                    {
                        if (!Current()) return;
                        var index = _permissionState.Projection.Packages.ToList().FindIndex(package => package.Id == packageId);
                        if (!SettingsPermissionPolicy.TrySelectPackage(_permissionState, index, out var transition))
                        { intentNavigation = navigation with { Error = "This widget is no longer installed or available." }; return; }
                        _permissionState = transition.State; _page = transition.Page;
                    }
                }
                else
                {
                    await ReloadInstalledWidgetsAsync(token).ConfigureAwait(false);
                    lock (_stateLock)
                    {
                        if (!Current()) return;
                        if (!_installedState.CatalogValid) throw new IOException("Installed catalog is unavailable.");
                        var installed = _installedState.Catalog.Widgets.ToList().FindIndex(widget => widget.Id == packageId);
                        var builtIn = _installedState.BuiltIns.ToList().FindIndex(widget => widget.Id == packageId);
                        var selected = builtIn >= 0
                            ? SettingsInstalledWidgetPolicy.TrySelectBuiltIn(_installedState, builtIn, out var transition)
                            : SettingsInstalledWidgetPolicy.TrySelectInstalled(_installedState, installed, out transition);
                        if (!selected) { intentNavigation = navigation with { Error = "This widget is no longer installed or available." }; return; }
                        _installedState = transition.State; _page = transition.Page;
                    }
                }
                SetOperation("Ready", false, false);
                PublishSettingsChanged();
            }
            finally { _operationGate.Release(); }
            // Do not hold the ordinary action gate during optional inspection.
            if (!Current()) return;
            if (updates) await ReadAppUpdatesAsync(new("status"), token).ConfigureAwait(false);
            else if (!permissions && !appearance)
            {
                await InspectSelectedWidgetLocalDataAsync(token).ConfigureAwait(false);
                if (Current()) await InspectSelectedWidgetUninstallAsync(token).ConfigureAwait(false);
            }
            EnsureSettingsStatusPolling();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformDiagnosticsException)
        {
            lock (_stateLock)
            {
                if (!Current()) return;
                if (_page == SettingsPage.IntentDestination)
                    intentNavigation = navigation with { Error = "This page could not load. Try again or return to Settings." };
                else { _error = true; StatusMessage = "Some page details could not load. Refresh to try again."; }
            }
        }
        finally { if (IsActive) PublishSettingsChanged(); }
    }

    private void CancelIntentNavigation()
    {
        lock (_stateLock)
        {
            intentNavigation = null;
            if (_page == SettingsPage.IntentDestination) _page = SettingsPage.Root;
        }
        Operations.Cancel(IntentNavigationKey);
    }

    private WidgetView RenderIntentDestination(SettingsPresentationState state)
    {
        IntentNavigation? pending;
        lock (_stateLock) pending = intentNavigation;
        var title = pending?.Request.ContractId == appUpdatesIntent.Id ? "App updates" :
            pending?.Request.ContractId == widgetPermissionsIntent.Id ? "Widget permissions" : "Widget settings";
        var items = new List<WidgetElement> { UI.Text(title, "intent.destination.title").Classes("page-heading") };
        if (pending?.Error is { } error)
        {
            items.Add(UI.Text(error, "intent.destination.error").Classes("diagnostic-error"));
            items.Add(UI.Button("Try again", "intent.retry", "intent.retry"));
        }
        else
        {
            items.Add(UI.LoadingIndicator("intent.destination.spinner", "Opening " + title));
            items.Add(UI.Text("Opening " + title.ToLowerInvariant() + "…", "intent.destination.status").Classes("page-help"));
        }
        items.Add(UI.Button("Back", "back", "intent.destination.back"));
        return SettingsPresentation.View(SettingsPresentation.Header(state), SettingsPresentation.PageScope("intent.destination", items.ToArray()),
            "intent.destination.back", "intent.destination");
    }
}
