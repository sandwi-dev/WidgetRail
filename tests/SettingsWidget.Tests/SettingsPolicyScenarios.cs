using System.Text.Json;
using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SettingsPolicyScenarios
{
    public static Task PresentationIsRepeatable()
    {
        var state = new SettingsPresentationState(
            SettingsPage.Root,
            PlatformSettingsDocument.Default,
            new ThemeCatalogSnapshot([]),
            PlatformDiagnosticsSnapshot.Unavailable(),
            null,
            "Ready",
            SettingsValid: true,
            Busy: false,
            Error: false);

        Require(SettingsPresentation.TryRender(state, out var first),
            "Root presentation was not owned by the snapshot presenter.");
        Require(SettingsPresentation.TryRender(state, out var second),
            "Repeated root presentation was not owned by the snapshot presenter.");
        var firstJson = JsonSerializer.Serialize(first.CreateSnapshot("settings-policy", 1));
        var secondJson = JsonSerializer.Serialize(second.CreateSnapshot("settings-policy", 1));
        Equal(firstJson, secondJson, "Repeated presentation changed semantic output.");
        return Task.CompletedTask;
    }

    public static Task NavigationAndPreferencePoliciesAreClosed()
    {
        Require(SettingsNavigationPolicy.TryResolve(
                "open.appearance", SettingsPage.Root, SettingsPage.Permissions, out var appearance),
            "Appearance navigation was not admitted.");
        Equal(SettingsPage.Appearance, appearance, "Appearance navigation targeted the wrong page.");
        Require(SettingsNavigationPolicy.TryResolve(
                "back", SettingsPage.ThemePicker, SettingsPage.Permissions, out var parent),
            "Back navigation was not admitted.");
        Equal(SettingsPage.Appearance, parent, "Theme Back did not return to Appearance.");
        Require(!SettingsNavigationPolicy.TryResolve(
                "capability.grant", SettingsPage.Root, SettingsPage.Permissions, out _),
            "Ordinary navigation admitted a privileged capability action.");
        Require(!SettingsNavigationPolicy.TryResolve(
                "open.app-library-sources", SettingsPage.Root, SettingsPage.Permissions, out _),
            "Navigation admitted the retired Game Sources page.");

        Require(SettingsPreferencePolicy.TryCreate("widget-switcher.toggle", out var switcher),
            "Widget switcher preference was not admitted.");
        var radial = switcher.Apply(PlatformSettingsDocument.Default);
        Equal(WidgetSwitcherLayout.Radial, radial.Appearance.WidgetSwitcher,
            "Switcher preference did not select radial.");
        Equal(WidgetSwitcherLayout.Rail, switcher.Apply(radial).Appearance.WidgetSwitcher,
            "Switcher preference did not return to rail.");
        Require(SettingsPreferencePolicy.TryCreate("text.increase", out var increase),
            "Text preference was not admitted.");
        var maximum = PlatformSettingsDocument.Default with
        {
            Appearance = PlatformSettingsDocument.Default.Appearance with
            {
                TextScale = AppearanceSettings.MaximumTextScale,
            },
        };
        Equal(AppearanceSettings.MaximumTextScale, increase.Apply(maximum).Appearance.TextScale,
            "Text preference exceeded its bound.");
        Require(!SettingsPreferencePolicy.TryCreate("authority.recovery.retry", out _),
            "Ordinary preferences admitted privileged recovery.");
        Require(!SettingsPreferencePolicy.TryCreate("capability.grant", out _),
            "Ordinary preferences admitted a capability decision.");
        Require(!SettingsPreferencePolicy.TryCreate("app-library.epic.toggle", out _) &&
                !SettingsPreferencePolicy.TryCreate("app-library.gog.toggle", out _),
            "Ordinary preferences admitted a retired game-source action.");
        Require(!SettingsPreferencePolicy.TryCreate("unknown", out _),
            "Ordinary preferences admitted an unknown action.");
        return Task.CompletedTask;
    }

    public static async Task PreferencePersistenceOwnsWrites()
    {
        using var temp = new PolicyTemporaryDirectory();
        var store = new PlatformSettingsStore(new PlatformSettingsPaths(temp.Path));
        await store.ReplaceAsync(PlatformSettingsDocument.Default);
        Require(SettingsPreferencePolicy.TryCreate("opacity.increase", out var increase),
            "Opacity preference was not admitted.");
        var updated = await SettingsPreferencePolicy.PersistAsync(
            store,
            PlatformSettingsDocument.Default,
            currentIsValid: true,
            increase,
            CancellationToken.None);
        Equal(0.69d, updated.Appearance.BackdropOpacity,
            "Valid-state persistence did not update the stored document.");

        var recoveryBase = PlatformSettingsDocument.Default with
        {
            Appearance = PlatformSettingsDocument.Default.Appearance with
            {
                BackdropOpacity = AppearanceSettings.MinimumBackdropOpacity,
            },
        };
        var recovered = await SettingsPreferencePolicy.PersistAsync(
            store,
            recoveryBase,
            currentIsValid: false,
            increase,
            CancellationToken.None);
        Equal(
            AppearanceSettings.MinimumBackdropOpacity + SettingsPreferencePolicy.OpacityStep,
            recovered.Appearance.BackdropOpacity,
            "Invalid-state recovery did not replace from the committed safe document.");
        DocumentEqual(recovered, await store.LoadAsync(),
            "Preference persistence did not commit its returned document.");
    }

    public static Task AuthorityRecoverySelectionIsExact()
    {
        var recoveryId = new string('A', PlatformDiagnosticsSnapshot.RecoveryIdLength);
        var token = new string('B', PlatformDiagnosticsSnapshot.ConfirmationTokenLength);
        var changedToken = new string('C', PlatformDiagnosticsSnapshot.ConfirmationTokenLength);
        var retryable = new PlatformAuthorityRecoveryDiagnostic(
            recoveryId,
            "Zeta package",
            PlatformAuthorityRecoveryState.Pending,
            "recovery_pending",
            true,
            token);
        var unavailable = new PlatformAuthorityRecoveryDiagnostic(
            new string('D', PlatformDiagnosticsSnapshot.RecoveryIdLength),
            "Alpha package",
            PlatformAuthorityRecoveryState.Unavailable,
            "recovery_unavailable",
            false,
            null);
        var diagnostics = PlatformDiagnosticsSnapshot.Unavailable() with
        {
            Revision = 10,
            AuthorityRecoveries = [retryable, unavailable],
        };

        Require(SettingsAuthorityRecoveryPolicy.TrySelect(diagnostics, 1, out var selection),
            "Sorted retryable recovery could not be selected.");
        Equal(recoveryId, selection.RecoveryId, "Selection did not follow deterministic display order.");
        Require(SettingsAuthorityRecoveryPolicy.TryAuthorize(diagnostics, selection, out var request),
            "Exact current recovery selection was refused.");
        Equal(token, request.ConfirmationToken, "Authorized request changed the exact token.");

        var replaced = diagnostics with
        {
            Revision = 11,
            AuthorityRecoveries = [retryable with { ConfirmationToken = changedToken }, unavailable],
        };
        Require(!SettingsAuthorityRecoveryPolicy.TryAuthorize(replaced, selection, out _),
            "A replaced token was authorized without renewed confirmation.");
        Require(SettingsAuthorityRecoveryPolicy.TrySelect(diagnostics, 0, out var disabled),
            "Unavailable recovery could not be reviewed.");
        Require(!SettingsAuthorityRecoveryPolicy.TryAuthorize(diagnostics, disabled, out _),
            "Unavailable recovery was authorized.");

        var pending = SettingsAuthorityRecoveryPolicy.Result(
            new(PlatformAuthorityRecoveryRetryStatus.StillPending, "sharing_violation"),
            diagnostics,
            request);
        Equal(SettingsPage.AuthorityRecovery, pending.Page,
            "Still-pending recovery left its confirmation page.");
        Equal(token, pending.Selection?.ConfirmationToken,
            "Still-pending recovery lost the exact reviewed token.");
        var cancelled = SettingsAuthorityRecoveryPolicy.Cancelled(request);
        Equal(token, cancelled.Selection?.ConfirmationToken,
            "Cancellation changed the exact reviewed token.");
        var recovered = SettingsAuthorityRecoveryPolicy.Result(
            new(PlatformAuthorityRecoveryRetryStatus.Recovered, "recovered"),
            diagnostics with { AuthorityRecoveries = [] },
            request);
        Equal(SettingsPage.Diagnostics, recovered.Page,
            "Recovered authority did not return to diagnostics.");
        Require(recovered.Selection is null, "Recovered authority retained a selection.");

        var state = new SettingsPresentationState(
            SettingsPage.AuthorityRecovery,
            PlatformSettingsDocument.Default,
            new ThemeCatalogSnapshot([]),
            diagnostics,
            recoveryId,
            "Review",
            SettingsValid: true,
            Busy: false,
            Error: false);
        Require(SettingsPresentation.TryRender(state, out var view),
            "Recovery confirmation presentation was unavailable.");
        Require(!JsonSerializer.Serialize(view.CreateSnapshot("settings-policy", 1))
                .Contains(token, StringComparison.Ordinal),
            "Opaque recovery token escaped through presentation metadata.");
        return Task.CompletedTask;
    }

    public static Task InstalledPolicyRemovesStaleSelection()
    {
        var state = SettingsInstalledWidgetState.Empty with
        {
            SelectedInstalledId = "dev.test.removed",
            VersionPage = 4,
        };
        var reconciled = SettingsInstalledWidgetPolicy.Reconcile(
            state,
            new WidgetCatalogSnapshot([]),
            [],
            SettingsPage.InstalledWidgetVersions);
        Equal(SettingsPage.InstalledWidgets, reconciled.Page,
            "Removed selection did not return to the catalog list.");
        Require(reconciled.State.SelectedInstalledId is null,
            "Removed catalog identity survived reconciliation.");
        Equal(0, reconciled.State.VersionPage,
            "Removed catalog identity retained a stale version page.");

        var failed = SettingsInstalledWidgetPolicy.Failure(
            state,
            "Catalog unavailable (test)",
            new WidgetCatalogHealthSnapshot("test_failure", []),
            SettingsPage.InstalledWidgetDetails);
        Equal(SettingsPage.InstalledWidgets, failed.Page,
            "Catalog failure retained a detail route.");
        Require(!failed.State.CatalogValid && failed.State.Catalog.Widgets.Count == 0,
            "Catalog failure retained actionable catalog rows.");
        return Task.CompletedTask;
    }

    public static Task PermissionPolicyBindsAuthorityAndRevocation()
    {
        const string packageId = "dev.test.permissions";
        const string authorityA = "dev.test.publisher\nsha256:a";
        const string authorityB = "dev.test.publisher\nsha256:b";
        const string capabilityId = PlatformCapabilities.NetworkReadV1;
        var packageA = new SettingsPermissionPackage(
            packageId,
            "dev.test.publisher",
            authorityA,
            "Permission sample",
            [new SettingsDeclaredCapability(capabilityId, IsRequired: true)],
            new string('a', 64));
        var grant = new ConsentDocument(
            1,
            7,
            [new ConsentEntry(packageId, authorityA, capabilityId, ConsentDecision.Grant)]);
        var projectionA = new SettingsPermissionProjection(
            [packageA],
            grant,
            CatalogValid: true,
            ConsentValid: true,
            Diagnostic: null,
            UnknownDeclarations: 0,
            HiddenConsentEntries: 0,
            InactiveConsentClassificationAvailable: true,
            UnknownDeclarationDetails: [],
            HiddenConsentDetails: []);
        var initial = SettingsPermissionPolicy.Reconcile(
            SettingsPermissionState.Empty,
            projectionA,
            SettingsPage.Permissions);
        Require(SettingsPermissionPolicy.TrySelectPackage(
                initial.State, 0, out var selected),
            "Current permission package could not be selected.");
        Require(SettingsPermissionPolicy.TrySelectCapability(
                selected.State, 0, out var capability),
            "Current declared capability could not be selected.");
        Equal(ConsentDecision.Grant,
            SettingsPermissionPolicy.FindDecision(
                capability.State.Projection.Consent,
                capability.State.SelectedPackage!,
                capabilityId),
            "Current grant was not projected.");

        var denied = grant with
        {
            Revision = 8,
            Entries = [new ConsentEntry(
                packageId, authorityA, capabilityId, ConsentDecision.Deny)],
        };
        var revoked = SettingsPermissionPolicy.Reconcile(
            capability.State,
            projectionA with { Consent = denied },
            SettingsPage.CapabilityDecision);
        Equal(ConsentDecision.Deny,
            SettingsPermissionPolicy.FindDecision(
                revoked.State.Projection.Consent,
                revoked.State.SelectedPackage!,
                capabilityId),
            "Updated denial did not replace the prior grant.");

        var packageB = packageA with { AuthorityPublisher = authorityB };
        var replaced = SettingsPermissionPolicy.Reconcile(
            revoked.State,
            projectionA with { Packages = [packageB] },
            SettingsPage.CapabilityDecision);
        Equal(SettingsPage.Permissions, replaced.Page,
            "Authority replacement retained a stale capability route.");
        Require(replaced.State.SelectedPackage is null &&
                replaced.State.SelectedCapabilityId is null,
            "Authority replacement retained stale selection authority.");
        return Task.CompletedTask;
    }

    public static Task SectionPresentersAreRepeatableAndBusySafe()
    {
        var headerState = new SettingsPresentationState(
            SettingsPage.InstalledWidgets,
            PlatformSettingsDocument.Default,
            new ThemeCatalogSnapshot([]),
            PlatformDiagnosticsSnapshot.Unavailable(),
            null,
            "Ready",
            SettingsValid: true,
            Busy: true,
            Error: false);
        var header = SettingsPresentation.Header(headerState);
        var failed = SettingsInstalledWidgetPolicy.Failure(
            SettingsInstalledWidgetState.Empty,
            "Catalog unavailable (test)",
            new WidgetCatalogHealthSnapshot(
                "test_failure",
                [new WidgetCatalogRepairCandidate(
                    "dev.test.widget", new Version(1, 0, 0), false, false)]),
            SettingsPage.InstalledWidgets).State;
        var first = SettingsInstalledWidgetPresentation.RenderInstalledWidgets(
            header, busy: true, failed);
        var second = SettingsInstalledWidgetPresentation.RenderInstalledWidgets(
            header, busy: true, failed);
        Equal(SnapshotJson(first), SnapshotJson(second),
            "Installed snapshot presentation changed for the same immutable input.");
        var installed = first.CreateSnapshot("settings-policy", 1);
        Require(Nodes(installed.Root).Single(node =>
                node.Id == "installed.repair.item.0").IsBusy == true,
            "Busy installed recovery remained actionable.");

        var readyFirst = SettingsInstalledWidgetPresentation.RenderInstalledWidgets(
            header, busy: true, SettingsInstalledWidgetState.Empty);
        var readySecond = SettingsInstalledWidgetPresentation.RenderInstalledWidgets(
            header, busy: true, SettingsInstalledWidgetState.Empty);
        Equal(SnapshotJson(readyFirst), SnapshotJson(readySecond),
            "Local-install presentation changed for the same immutable input.");
        var ready = readyFirst.CreateSnapshot("settings-policy", 1);
        var install = Nodes(ready.Root).Single(node =>
            node.Id == "installed.install-local");
        Equal("host.install-local-widget", install.ActionId,
            "Local-install presentation changed the private host action.");
        Require(install.IsBusy == true,
            "Busy Settings state left local package selection actionable.");

        var permissionFirst = SettingsPermissionPresentation.RenderPermissionPackages(
            header,
            busy: false,
            SettingsPermissionState.Empty);
        var permissionSecond = SettingsPermissionPresentation.RenderPermissionPackages(
            header,
            busy: false,
            SettingsPermissionState.Empty);
        Equal(SnapshotJson(permissionFirst), SnapshotJson(permissionSecond),
            "Permission snapshot presentation changed for the same immutable input.");
        return Task.CompletedTask;
    }

    public static async Task CancelledSectionRefreshPreservesCommittedState()
    {
        using var temp = new PolicyTemporaryDirectory();
        var paths = new PlatformSettingsPaths(temp.Path);
        var widget = new SettingsWidget(
            new PlatformSettingsStore(paths),
            new ThemeCatalog(paths));
        await widget.InitializeAsync(CancellationToken.None);
        await widget.SetLifecycleStateAsync(
            WidgetLifecycleState.Visible, CancellationToken.None);
        await widget.InitializationTask;
        await widget.OnActionAsync(new WidgetActionEvent("open.permissions", "test"));
        var before = SnapshotJson(widget.Render());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await widget.OnActionAsync(
                new WidgetActionEvent("refresh", "test"), cancelled.Token);
            throw new InvalidOperationException("Cancelled section refresh completed.");
        }
        catch (OperationCanceledException) when (cancelled.IsCancellationRequested)
        {
        }
        Equal(SettingsPage.Permissions, widget.CurrentPage,
            "Cancelled refresh changed the committed page.");
        Equal(before, SnapshotJson(widget.Render()),
            "Cancelled refresh changed committed section presentation.");
    }

    private static string SnapshotJson(WidgetView view) =>
        JsonSerializer.Serialize(view.CreateSnapshot("settings-policy", 1));

    private static IEnumerable<ViewNode> Nodes(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Nodes(child))
            yield return descendant;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected {expected}; actual {actual}.");
    }

    private static void DocumentEqual(
        PlatformSettingsDocument expected,
        PlatformSettingsDocument actual,
        string message)
    {
        Equal(expected.SchemaVersion, actual.SchemaVersion, message);
        Equal(expected.Appearance.ThemeId, actual.Appearance.ThemeId, message);
        Equal(expected.Appearance.ThemeVersion, actual.Appearance.ThemeVersion, message);
        Equal(expected.Appearance.InterfaceScale, actual.Appearance.InterfaceScale, message);
        Equal(expected.Appearance.TextScale, actual.Appearance.TextScale, message);
        Equal(expected.Appearance.BackdropOpacity, actual.Appearance.BackdropOpacity, message);
        Equal(expected.Appearance.Motion, actual.Appearance.Motion, message);
        Equal(expected.Appearance.Contrast, actual.Appearance.Contrast, message);
        Equal(expected.Appearance.BoldText, actual.Appearance.BoldText, message);
        Equal(expected.Appearance.Transparency, actual.Appearance.Transparency, message);
        Equal(
            expected.Appearance.AnimateWidgetSwitching,
            actual.Appearance.AnimateWidgetSwitching,
            message);
        Equal(
            expected.Appearance.WidgetSurfaceAppearance,
            actual.Appearance.WidgetSurfaceAppearance,
            message);
        Require(
            expected.Appearance.WidgetSurfaceAppearanceOverrides
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(actual.Appearance.WidgetSurfaceAppearanceOverrides.OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal)),
            message);
    }

    private sealed class PolicyTemporaryDirectory : IDisposable
    {
        public PolicyTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"wrail-settings-policy-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
