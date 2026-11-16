using System.Text.Json;
using System.Text.Json.Nodes;
using WidgetRail.Playnite;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed class PlayniteSetupService : IPlayniteSetupService
{
    private readonly IPlayniteSetupPlatform platform;
    private readonly IPlayniteSetupConnection connection;
    private readonly IPlayniteCredentialStore credentials;
    private readonly PlayniteSetupDiscoveryService discovery;
    private readonly PlayniteSetupPackage package;
    private readonly PlayniteLocalConnectionStore connections;
    private readonly SemaphoreSlim gate = new(1, 1);

    internal PlayniteSetupService(string stateRoot, string packageDirectory, IPlayniteSetupPlatform? platform = null,
        IPlayniteSetupConnection? connection = null, IPlayniteCredentialStore? credentials = null,
        PlayniteLocalConnectionStore? localConnections = null)
    {
        this.platform = platform ?? new PlayniteSetupPlatform();
        this.connection = connection ?? new PlayniteSetupConnection();
        connections = localConnections ?? new(stateRoot);
        this.credentials = credentials ?? new PlayniteProfileCredentialStore(connections);
        discovery = new(this.platform, stateRoot);
        package = new(packageDirectory);
    }

    public ValueTask<PlayniteSetupDiscovery> DiscoverAsync(string? programPath, string? dataPath, CancellationToken token) =>
        discovery.DiscoverAsync(programPath, dataPath, token);

    public async ValueTask<PlayniteSetupOffer> CheckSetupAsync(CancellationToken token)
    {
        var found = await DiscoverAsync(null, null, token).ConfigureAwait(false);
        var running = await platform.ProcessesAsync(token).ConfigureAwait(false);
        var selected = running.Count == 1
            ? found.Installations.SingleOrDefault(item => item.Running && PlayniteSetupPlatform.Matches(running[0], item))
            : running.Count == 0 && found.Message is null && found.Installations.Count == 1 ? found.Installations[0] : null;
        if (selected is null) return new(found, null, true);
        var plan = await InspectCoreAsync(selected, token, verifyLibrary: false).ConfigureAwait(false);
        var savedToken = plan.Action == PlayniteSetupAction.Connect ? await credentials.ReadAsync(token).ConfigureAwait(false) : null;
        var binding = connections.Read();
        var ready = plan.Action == PlayniteSetupAction.Connect && savedToken is not null && binding is not null &&
            string.Equals(binding.DataDirectory, selected.DataDirectory, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(binding.ProgramDirectory, selected.ProgramDirectory, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(savedToken, ReadToken(selected.DataDirectory), StringComparison.Ordinal);
        return new(found, plan, !ready);
    }

    public ValueTask<PlayniteSetupPlan> InspectAsync(PlayniteSetupInstallation installation, CancellationToken token) =>
        InspectCoreAsync(installation, token, verifyLibrary: true);

    private async ValueTask<PlayniteSetupPlan> InspectCoreAsync(PlayniteSetupInstallation installation, CancellationToken token, bool verifyLibrary)
    {
        var validated = PlayniteSetupDiscoveryService.Candidate(installation.ProgramDirectory, installation.DataDirectory, installation.Running, installation.Fullscreen);
        if (validated.Id != installation.Id) throw new PlayniteSetupException("The selected installation changed. Find Playnite again.");
        var processes = await platform.ProcessesAsync(token).ConfigureAwait(false);
        if (processes.Any(process => !PlayniteSetupPlatform.Matches(process, installation)))
            return Plan(PlayniteSetupAction.Manual, "Another Playnite profile is running", "Close the other Playnite instance before setting up this profile.", null, null, null);
        installation = installation with { Running = processes.Count != 0, Fullscreen = processes.FirstOrDefault()?.Fullscreen ?? installation.Fullscreen };
        var extension = FindExtension(installation);
        var recovery = RecoveryTarget(installation);
        if (recovery is not null) return Plan(PlayniteSetupAction.Repair, "Finish interrupted setup",
            "Playnite will close briefly while its previous integration is restored and setup is retried.", null, null, recovery);
        if (extension is null)
        {
            var existingToken = ReadToken(installation.DataDirectory);
            if (installation.Running && existingToken is not null)
            {
                var existingHealth = await connection.CheckAsync(installation, existingToken, token, verifyLibrary).ConfigureAwait(false);
                if (existingHealth == PlayniteSetupConnectionKind.Ready)
                    return Plan(PlayniteSetupAction.Connect, "Ready to connect", "Use the existing integration managed by this Playnite profile. No files need to be installed.", null, null, null);
                if (existingHealth != PlayniteSetupConnectionKind.Offline)
                    return Plan(PlayniteSetupAction.Manual, "Externally managed integration detected", "The running integration could not be verified and its files are outside the detected extension folders. Use manual setup to preserve it.", null, null, null);
            }
            var defaultTarget = Path.Combine(installation.DataDirectory, "Extensions", PlayniteSetupFiles.ExtensionId);
            if (Directory.Exists(defaultTarget) && Directory.EnumerateFileSystemEntries(defaultTarget).Any())
                return Plan(PlayniteSetupAction.Manual, "Integration folder needs attention", "The expected integration folder contains an unrecognized installation. Use manual setup so its files are preserved.", null, null, defaultTarget);
            package.Validate();
            return Plan(PlayniteSetupAction.Install, "Connect your Playnite library",
                "Install WidgetRail for Playnite, start Playnite, and connect automatically. " +
                (installation.Running ? "Playnite will restart once. " : "") + "Your library and integration settings are preserved.", null, null,
                Path.Combine(installation.DataDirectory, "Extensions", PlayniteSetupFiles.ExtensionId));
        }
        var manifest = PlayniteSetupFiles.ReadManifest(Path.Combine(extension, "extension.yaml"));
        var version = manifest.GetValueOrDefault("Version");
        if (manifest.GetValueOrDefault("Module") != IntegrationIdentity.ModuleName)
            return Plan(PlayniteSetupAction.Manual, "Custom integration detected", "This integration uses an unfamiliar entry point. Use manual setup; its files will not be replaced.", version, null, extension);
        var digest = PlayniteSetupPackage.InstalledDigest(extension);
        // A healthy older build still needs an update offer. This check precedes
        // the health fast path which used to hide available Bridge upgrades.
        if (NeedsPackageUpdate(version, digest, extension))
        {
            package.Validate();
            return Plan(PlayniteSetupAction.Repair, "Update WidgetRail for Playnite",
                "Install the bundled local integration while preserving its settings. Playnite will restart if running. The original Bridge is left unchanged.", version, digest, extension);
        }
        if (Disabled(installation.DataDirectory))
            return Plan(PlayniteSetupAction.Enable, "Enable WidgetRail for Playnite", "Enable the installed integration and reconnect. Playnite will restart if it is running.", version, digest, extension);
        var localToken = ReadToken(installation.DataDirectory);
        if (installation.Running && localToken is not null)
        {
            var health = await connection.CheckAsync(installation, localToken, token, verifyLibrary).ConfigureAwait(false);
            if (health == PlayniteSetupConnectionKind.Ready)
                return Plan(PlayniteSetupAction.Connect, "Ready to connect", "Use this installation's existing integration and save the connection securely. No restart is needed.", version, digest, extension);
            if (health is PlayniteSetupConnectionKind.WrongProfile or PlayniteSetupConnectionKind.Authentication)
                return Plan(PlayniteSetupAction.Manual, "The local service belongs to another connection", "Check the selected Playnite profile and reconnect. Existing settings were not changed.", version, digest, extension);
            if (health == PlayniteSetupConnectionKind.Incompatible)
                return Plan(PlayniteSetupAction.Manual, "The local service is incompatible", "The discovered service does not provide the expected WidgetRail API. Check the integration in Playnite before reconnecting.", version, digest, extension);
        }
        return installation.Running
            ? Plan(PlayniteSetupAction.Restart, "Restart to reconnect the integration", "Playnite is running but its integration is not ready. Restart this profile, check the connection, and preserve the installed integration files.", version, digest, extension)
            : Plan(PlayniteSetupAction.Start, "Start and connect Playnite", "Start the selected Playnite profile, check the integration, and save its connection securely.", version, digest, extension);

        PlayniteSetupPlan Plan(PlayniteSetupAction action, string title, string message, string? version, string? digest, string? path) =>
            new(installation, action, title, message, version, digest, path);
    }

    public ValueTask<PlayniteSetupResult> ApplyAsync(PlayniteSetupPlan plan, Action<string> progress, CancellationToken token) =>
        RunAsync(plan, false, progress, token);

    public ValueTask<PlayniteSetupResult> StartAsync(PlayniteSetupInstallation installation, Action<string> progress, CancellationToken token) =>
        RunAsync(new(installation, PlayniteSetupAction.Start, "Start Playnite", "", null, null, null), true, progress, token);

    private async ValueTask<PlayniteSetupResult> RunAsync(PlayniteSetupPlan requested, bool startOnly, Action<string> progress, CancellationToken token)
    {
        if (!await gate.WaitAsync(0, token).ConfigureAwait(false)) return new(false, "Setup is already running", "Wait for the current connection operation to finish.");
        PlayniteSetupDeployment? deployment = null;
        PlayniteSetupInstallation installation = requested.Installation;
        bool stopped = false;
        bool credentialSaved = false;
        string? oldCredential = null;
        IPlayniteCredentialStore appliedCredentials = credentials;
        PlayniteLocalBinding? previousBinding = null, proposedBinding = null;
        bool bindingSaved = false;
        FileStream? lease = null;
        FileStream? integrationLease = null;
        FileStream? connectionLease = null;
        try
        {
            connectionLease = connections.AcquireSetupLease();
            previousBinding = connections.Read();
            progress("Checking the selected Playnite installation…");
            var validated = PlayniteSetupDiscoveryService.Candidate(installation.ProgramDirectory, installation.DataDirectory, installation.Running, installation.Fullscreen);
            if (validated.Id != installation.Id) throw new PlayniteSetupException("The selected installation changed. Find Playnite again.");
            var plan = startOnly ? requested : await InspectAsync(installation, token).ConfigureAwait(false);
            if (!startOnly && plan.Action != PlayniteSetupAction.Connect &&
                (plan.Action != requested.Action || plan.ExtensionDirectory != requested.ExtensionDirectory || plan.InstalledDigest != requested.InstalledDigest))
                throw new PlayniteSetupException("The integration changed since it was checked. Find Playnite again before continuing.");
            if (!plan.CanApply) return new(false, plan.Title, plan.Message);
            installation = plan.Installation;
            var lockDirectory = Path.Combine(installation.DataDirectory, "WidgetRail.Setup");
            PlayniteSetupFiles.NoLinks(lockDirectory);
            Directory.CreateDirectory(lockDirectory);
            try { lease = new FileStream(Path.Combine(lockDirectory, "setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new PlayniteSetupException("Another setup operation is using this Playnite profile. Wait, then retry."); }
            if (plan.Action is PlayniteSetupAction.Install or PlayniteSetupAction.Repair or PlayniteSetupAction.Enable &&
                plan.ExtensionDirectory is { } extensionDirectory &&
                !PlayniteSetupFiles.Within(Path.Combine(installation.DataDirectory, "Extensions"), extensionDirectory))
            {
                var sharedLock = Path.Combine(installation.ProgramDirectory, "WidgetRail.Setup");
                PlayniteSetupFiles.NoLinks(sharedLock);
                Directory.CreateDirectory(sharedLock);
                try { integrationLease = new FileStream(Path.Combine(sharedLock, "setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { throw new PlayniteSetupException("Another profile is updating this shared Playnite integration. Wait, then retry."); }
            }
            if (plan.Action == PlayniteSetupAction.Restart)
            {
                progress("Restarting Playnite safely…");
                await platform.StopAsync(installation, token).ConfigureAwait(false);
                stopped = true;
            }
            if (plan.Action is PlayniteSetupAction.Install or PlayniteSetupAction.Repair or PlayniteSetupAction.Enable)
            {
                if (plan.Action != PlayniteSetupAction.Enable) package.Validate();
                deployment = new(installation, plan.ExtensionDirectory!, package);
                progress("Closing Playnite safely…");
                await platform.StopAsync(installation, token).ConfigureAwait(false);
                stopped = true;
                if (deployment.NeedsRecovery)
                {
                    deployment.Rollback();
                    plan = await InspectAsync(installation, token).ConfigureAwait(false);
                    if (!plan.CanApply) throw new PlayniteSetupException(plan.Message);
                    deployment = plan.Action is PlayniteSetupAction.Install or PlayniteSetupAction.Repair or PlayniteSetupAction.Enable
                        ? new(installation, plan.ExtensionDirectory!, package) : null;
                }
                else if (plan.InstalledDigest is { } expected &&
                         PlayniteSetupPackage.InstalledDigest(plan.ExtensionDirectory!) != expected)
                    throw new PlayniteSetupException("The installed integration changed while Playnite was closing. Retry setup.");
                progress("Preparing the integration and preserving settings…");
                deployment?.Prepare(plan.Action != PlayniteSetupAction.Enable);
                token.ThrowIfCancellationRequested();
                deployment?.Commit();
            }
            progress("Starting Playnite…");
            await platform.StartAsync(installation, token).ConfigureAwait(false);
            progress("Waiting for Playnite and checking the connection…");
            var credential = await WaitForConnectionAsync(installation, token).ConfigureAwait(false);
            var auth = PlayniteLocalConnectionStore.ReadCredentials(installation.DataDirectory)
                ?? throw new PlayniteSetupException("The integration did not publish its profile identity.");
            proposedBinding = new(1, Guid.NewGuid().ToString("D"), auth.ProfileId, installation.ProgramDirectory, installation.DataDirectory);
            appliedCredentials = credentials is PlayniteProfileCredentialStore scoped ? scoped.ForBinding(proposedBinding) : credentials;
            oldCredential = await appliedCredentials.ReadAsync(token).ConfigureAwait(false);
            await appliedCredentials.SaveAsync(credential, token).ConfigureAwait(false);
            credentialSaved = true;
            connections.Save(proposedBinding); bindingSaved = true;
            deployment?.Complete();
            try { discovery.Remember(installation); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* The verified connection remains usable. */ }
            return new(true, "Playnite connected", "Your library is ready. The connection is saved securely in Windows Credential Manager.");
        }
        catch (Exception error) when (error is PlayniteSetupException or IOException or UnauthorizedAccessException or JsonException or
            OperationCanceledException or PlayniteCredentialException or System.ComponentModel.Win32Exception or System.Management.ManagementException or InvalidOperationException)
        {
            var message = error is PlayniteSetupException ? error.Message : error is OperationCanceledException ? "Setup was interrupted. You can retry when ready." :
                error is PlayniteCredentialException ? "Windows Credential Manager could not save the connection. Retry when Windows can store credentials securely." :
                "Setup could not finish. Check that the selected folders are writable and Playnite can close normally, then retry.";
            try
            {
                if (deployment?.NeedsRecovery == true)
                {
                    progress("Restoring the previous integration…");
                    await platform.StopAsync(installation, CancellationToken.None).ConfigureAwait(false);
                    deployment.Rollback();
                    message += " The previous integration and settings were restored.";
                }
                if (credentialSaved)
                {
                    if (bindingSaved && proposedBinding is not null) connections.Restore(previousBinding, proposedBinding.BindingId);
                    if (oldCredential is null) await appliedCredentials.DeleteAsync(CancellationToken.None).ConfigureAwait(false);
                    else await appliedCredentials.SaveAsync(oldCredential, CancellationToken.None).ConfigureAwait(false);
                }
                if (stopped && installation.Running) await platform.StartAsync(installation, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException or PlayniteSetupException or PlayniteCredentialException or System.ComponentModel.Win32Exception or System.Management.ManagementException or JsonException or InvalidOperationException)
            { message += " Automatic recovery could not finish. The backup is retained; close Playnite and retry setup to recover it."; }
            return new(false, "Playnite setup needs attention", message);
        }
        finally { connectionLease?.Dispose(); integrationLease?.Dispose(); lease?.Dispose(); gate.Release(); }
    }

    private async Task<string> WaitForConnectionAsync(PlayniteSetupInstallation installation, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                string? credential = null;
                try { credential = ReadToken(installation.DataDirectory); }
                catch (Exception error) when (error is JsonException or IOException)
                { /* Bridge may still be creating its auth file. Retry within the startup deadline. */ }
                if (credential is not null)
                {
                    var status = await connection.CheckAsync(installation, credential, timeout.Token).ConfigureAwait(false);
                    if (status == PlayniteSetupConnectionKind.Ready) return credential;
                    if (status != PlayniteSetupConnectionKind.Offline)
                        throw new PlayniteSetupException(status switch
                        {
                            PlayniteSetupConnectionKind.WrongProfile => "The selected endpoint is serving a different Playnite profile. Reconnect the intended profile.",
                            PlayniteSetupConnectionKind.Authentication => "Playnite rejected this profile's saved token. Check that the correct Playnite instance is running.",
                            _ => "The local service does not provide the expected WidgetRail API. Check the integration version and selected profile.",
                        });
                }
                await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new PlayniteSetupException("Playnite did not become ready in time. Finish any startup dialog, then retry. No token copying is needed."); }
    }

    private bool NeedsPackageUpdate(string? version, string? digest, string extension) =>
        digest is not null && System.Version.TryParse(version, out var installed) &&
        installed <= System.Version.Parse(PlayniteSetupPackage.Version) && PlayniteSetupPackage.OwnedBuild(extension) &&
        !string.Equals(digest, package.PayloadDigest, StringComparison.OrdinalIgnoreCase);

    internal static string? ReadToken(string dataDirectory)
    {
        return PlayniteLocalConnectionStore.ReadCredentials(dataDirectory)?.Token;
    }
    private static bool Disabled(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "config.json");
        var plugin = Path.Combine(PlayniteSetupFiles.PluginData(dataDirectory), "config.json");
        return File.Exists(path) && PlayniteSetupFiles.ReadObject(path, 32 * 1024 * 1024)["DisabledPlugins"] is JsonArray disabled &&
            disabled.Any(value => value?.GetValue<string>() is PlayniteSetupFiles.ExtensionId or PlayniteSetupFiles.PluginId) ||
            File.Exists(plugin) && PlayniteSetupFiles.ReadObject(plugin)["Enabled"]?.GetValue<bool>() == false;
    }
    internal static string? FindExtension(PlayniteSetupInstallation installation)
    {
        // Match Playnite's user-before-program lookup, and refuse duplicates within
        // a root instead of guessing which version its loader would choose.
        foreach (var root in new[] { Path.Combine(installation.DataDirectory, "Extensions"), Path.Combine(installation.ProgramDirectory, "Extensions") }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            PlayniteSetupFiles.NoLinks(root);
            if (!Directory.Exists(root)) continue;
            var matches = new List<string>();
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
            int inspected = 0;
            foreach (var path in Directory.EnumerateFiles(root, "extension.yaml", options))
            {
                if (++inspected > 1024) throw new PlayniteSetupException("There are too many extension manifests to identify the integration reliably. Use manual setup.");
                if (PlayniteSetupFiles.ReadManifest(path).GetValueOrDefault("Id") == PlayniteSetupFiles.ExtensionId) matches.Add(Path.GetDirectoryName(path)!);
            }
            if (matches.Count > 1) throw new PlayniteSetupException("Multiple WidgetRail integration copies were found. Remove the duplicate in Playnite before using automatic setup.");
            if (matches.Count == 1) return matches[0];
        }
        return null;
    }
    private static string? RecoveryTarget(PlayniteSetupInstallation installation)
    {
        foreach (var directory in new[] { installation.DataDirectory, installation.ProgramDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(directory, "WidgetRail.Setup", PlayniteSetupFiles.RecoveryDirectory, "transaction.json");
            if (!File.Exists(path)) continue;
            var record = PlayniteSetupFiles.ReadObject(path, 16 * 1024);
            if (record["complete"]?.GetValue<bool>() == true) continue;
            var target = record["target"]?.GetValue<string>();
            if (record["pluginId"]?.GetValue<string>() != PlayniteSetupFiles.PluginId || target is null || !PlayniteSetupFiles.Within(Path.Combine(directory, "Extensions"), target))
                throw new PlayniteSetupException("The saved integration recovery record is invalid. Use manual setup.");
            return target;
        }
        return null;
    }
}
