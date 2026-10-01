using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;
using CatalogService = WidgetRail.WidgetCatalog.WidgetCatalog;

internal static partial class FullTrustCommunityScenarios
{
    // Complements the shipped-executable startup check: the production widget
    // and application service run in a separate installed process, with a fake
    // provider that cannot launch a game or contact a Playnite endpoint.
    internal static async Task PlayniteProcessWorkflow()
    {
        using var temporary = new ScenarioDirectory();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = Path.Combine(RepositoryRoot(), "tests", "PlayniteLibraryApplicationFixture",
            "bin", VerificationInstallation.Configuration, "net8.0-windows10.0.19041.0", "win-x64");
        Check(File.Exists(Path.Combine(output, "PlayniteLibraryApplicationFixture.exe")), "Build the process fixture first.");
        var package = CreatePackage(temporary.Path, output, "PlayniteLibraryApplicationFixture.exe",
            "dev.widgetrail.playnite-process-fixture", "1.0.0");
        var installedRoot = Path.Combine(temporary.Path, "installed");
        var catalog = new CatalogService(installedRoot);
        await ThrowsCodeAsync("full_trust_approval_required", () => catalog.InstallAsync(package));
        var installed = await catalog.InstallAsync(package, WidgetPackageTrustApproval.FullTrustCurrentUser);
        await catalog.SetEnabledAsync(installed.Id, true, WidgetPackageTrustApproval.FullTrustCurrentUser);
        var trusted = CreateTrustedCatalog(temporary.Path);
        var load = await BridgeCatalog.LoadWithInstalledAsync(trusted.CatalogPath, installedRoot, trusted.WorkerHostPath);
        Check(load.InstalledCatalogValid && load.Warnings.Count == 0, "Fixture admission failed.");
        var configured = load.Catalog.GetConfigured(installed.Id);
        AssertFullTrust(configured, "PlayniteLibraryApplicationFixture.exe");

        const string variable = "WRAIL_PLAYNITE_PROCESS_FIXTURE_ROOT";
        var previous = Environment.GetEnvironmentVariable(variable);
        File.WriteAllText(Path.Combine(temporary.Path, "fixture-owned"), "isolated fixture");
        Environment.SetEnvironmentVariable(variable, temporary.Path);
        try
        {
            await using var client = Client(configured);
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive, deadline.Token);
            var snapshot = await Wait(snapshot => WorkflowNodes(snapshot.Root).Any(node => node.ActionId == "playnite-library.browse.open"));
            await Action(snapshot, "playnite-library.browse.open");
            snapshot = await Wait(snapshot => TryFind(snapshot.Root, "playnite-library.search") is not null && Collection(snapshot)?.IndexedCollection?.Count == 3);
            var original = Collection(snapshot)!;
            await using var oldLease = await client.AcquireIndexedRangeAsync(Request(original), client.Starts, deadline.Token);
            var oldSnapshot = snapshot;
            await Action(snapshot, "playnite-library.search.commit", "Process Target");
            snapshot = await Wait(snapshot => Collection(snapshot)?.IndexedCollection is { Count: 1 } descriptor &&
                descriptor.QueryGeneration != original.IndexedCollection!.QueryGeneration);

            var staleRejected = false;
            try
            {
                await oldLease.AdmitInputAsync(new(new(oldLease.Lease.LeaseId,
                    oldLease.Lease.Range.Items[0].Key), ControllerButton.A),
                    new(oldLease.Lease.Range.ScopeId, oldSnapshot.Sequence), deadline.Token);
            }
            catch (ArgumentException error) when (error.ParamName == "request") { staleRejected = true; }
            Check(staleRejected, "A replaced search query retained old row input authority.");
            Check(!File.Exists(Path.Combine(temporary.Path, "launches.txt")), "Stale row input reached the provider.");
            snapshot = await client.GetSnapshotAsync(deadline.Token);
            Check(!WorkflowNodes(snapshot.Root).Any(node => node.Kind == ViewNodeKind.ModalLayer), "Stale query opened details.");
            await using var lease = await client.AcquireIndexedRangeAsync(Request(Collection(snapshot)!), client.Starts, deadline.Token);
            var item = lease.Lease.Range.Items.Single();
            Check(WorkflowNodes(item.Root).Any(node => node.Text == "Process Target" || node.AccessibilityLabel?.Contains("Process Target", StringComparison.Ordinal) == true),
                "Search range did not project the matching production poster.");
            Check(await lease.AdmitInputAsync(new(new(lease.Lease.LeaseId, item.Key), ControllerButton.A),
                new(lease.Lease.Range.ScopeId, snapshot.Sequence), deadline.Token) == WidgetOperationAdmission.Enqueued,
                "Current indexed selection was not admitted.");
            snapshot = await Wait(snapshot => TryFind(snapshot.Root, "playnite-library.details.play") is not null &&
                WorkflowNodes(snapshot.Root).Any(node => node.Text == "Cross-process production details"));
            var modalScope = snapshot.ActiveInputScopeId;
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Background, deadline.Token);
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive, deadline.Token);
            snapshot = await Wait(snapshot => snapshot.ActiveInputScopeId == modalScope && TryFind(snapshot.Root, "playnite-library.details.play") is not null);
            Check(client.Starts == 1, "Lifecycle return unnecessarily replaced the application process.");
            await Action(snapshot, "playnite-library.launch");
            var journal = Path.Combine(temporary.Path, "launches.txt");
            string[] launched;
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    // Exclusive read waits for the asynchronous provider append
                    // to close; file existence alone is not completion evidence.
                    using var stream = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.None);
                    using var reader = new StreamReader(stream);
                    launched = reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                    if (launched.Length > 0) break;
                }
                catch (IOException) { }
                await Task.Delay(20, deadline.Token);
            }
            Check(launched.SequenceEqual(["00000000-0000-0000-0000-000000000002"]),
                "Details launch did not reach exactly the selected fake game.");
            await client.StopAsync(deadline.Token);
            Check(!client.IsRunning, "Fixture process did not stop.");

            async Task<ViewSnapshot> Wait(Func<ViewSnapshot, bool> predicate)
            {
                while (true)
                {
                    var current = await client.GetSnapshotAsync(deadline.Token);
                    if (predicate(current)) return current;
                    await Task.Delay(20, deadline.Token);
                }
            }
            async Task Action(ViewSnapshot current, string action, string? text = null)
            {
                var node = WorkflowNodes(current.Root).First(node => node.ActionId == action);
                Check(await client.AdmitActionAsync(new WidgetActionEvent(action, node.Id,
                    InputScopeId: current.ActiveInputScopeId) { CommittedText = text }, deadline.Token) == WidgetOperationAdmission.Enqueued,
                    $"Production action '{action}' was not admitted.");
            }
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
        await catalog.SetEnabledAsync(installed.Id, false);
        Check((await catalog.UninstallAsync(installed.Id)).RemovedVersions.Count == 1, "Fixture package did not uninstall cleanly.");

        static ViewNode? Collection(ViewSnapshot snapshot) => WorkflowNodes(snapshot.Root).FirstOrDefault(node => node.IndexedCollection is not null);
        static IndexedCollectionRangeRequest Request(ViewNode node) => new(node.Id, node.IndexedCollection!, 0,
            (int)Math.Min(3, node.IndexedCollection!.Count), Guid.NewGuid().ToString("N"));
    }

    private static IEnumerable<ViewNode> WorkflowNodes(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in WorkflowNodes(child)) yield return descendant;
    }
}
