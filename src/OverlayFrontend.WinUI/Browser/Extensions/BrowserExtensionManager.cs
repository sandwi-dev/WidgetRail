using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser.Extensions;

/// <summary>One shared package; browser-owned data and activation remain profile-scoped.</summary>
internal sealed class BrowserExtensionManager(Web.HostedWebViewEnvironment runtime, string settingsRoot)
{
    internal ExtensionStore Store { get; } = new(settingsRoot);
    private readonly SemaphoreSlim operations = new(1, 1);
    private Task? initialization;
    private bool recoveryRequired;
    internal sealed class ProfileLease(WebView2 view) : IDisposable
    {
        internal CoreWebView2Profile Profile => view.CoreWebView2.Profile;
        public void Dispose() => view.Close();
    }
    internal async Task<ProfileLease> OpenAsync(ExtensionProfile profile, CancellationToken token)
    {
        if (profile.Private || !profile.Name.StartsWith("persistent-", StringComparison.Ordinal))
            throw new InvalidOperationException("Extensions require a persistent browser profile.");
        var environment = await runtime.GetAsync().WaitAsync(token);
        var options = environment.CreateCoreWebView2ControllerOptions(); options.ProfileName = profile.Name;
        var view = new WebView2();
        try { await view.EnsureCoreWebView2Async(environment, options); token.ThrowIfCancellationRequested(); return new(view); }
        catch { view.Close(); throw; }
    }
    private static ExtensionProfile ValidationProfile => new("persistent-extension-validation", "", "Package validation", false);
    internal sealed class Import(string folder, ExtensionManifest manifest) : IAsyncDisposable
    {
        internal string Folder => folder;
        internal ExtensionManifest Manifest => manifest;
        public async ValueTask DisposeAsync()
        {
            try { await Task.Run(() => { if (Directory.Exists(folder)) Directory.Delete(folder, true); }); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Diagnostics.FrontendFailureLog.Current.Write("extension-staging-cleanup", error); }
        }
    }
    internal async Task<Import> PrepareAsync(string source, CancellationToken token)
    {
        var staging = Path.Combine(Store.Root, "staging", Guid.NewGuid().ToString("N"));
        try { return new(staging, await Task.Run(() => ExtensionPackage.Copy(source, staging, token), token)); }
        catch { await Task.Run(() => { if (Directory.Exists(staging)) Directory.Delete(staging, true); }); throw; }
    }
    internal async Task InitializeAsync(CancellationToken token)
    {
        var attempt = initialization ??= RecoverAsync();
        try { await attempt.WaitAsync(token); }
        catch { if (attempt.IsFaulted && ReferenceEquals(initialization, attempt)) initialization = null; throw; }
    }
    internal async Task RegisterAsync(ExtensionProfile profile, CoreWebView2Profile native, CancellationToken token)
    {
        await InitializeAsync(token);
        await operations.WaitAsync(token);
        try
        {
            await RecoverPendingAsync();
            var ownership = await new WidgetRail.PlatformSettings.BrowserProfileLifecycleStore(settingsRoot).ReadAsync(profile.WidgetId, token);
            if (ownership.Removed || ownership.Pending.Any(p => p.Name == profile.Name) ||
                ownership.Generation.Length != 0 && !ownership.Profiles.Any(p => p.Name == profile.Name)) return;
            await Store.RegisterAsync(profile, token);
            if (profile.Private) return;
            foreach (var entry in (await Store.ReadAsync(token)).Library)
            {
                try { await ApplyAsync(entry, profile, native, token); }
                catch (Exception error) when (error is not OperationCanceledException)
                { Diagnostics.FrontendFailureLog.Current.Write("extension-profile-apply", error); }
            }
        }
        finally { operations.Release(); }
    }
    private async Task ApplyAsync(ExtensionEntry entry, ExtensionProfile profile, CoreWebView2Profile native, CancellationToken token)
    {
        var bindings = new Dictionary<string, ExtensionBinding>(entry.Bindings);
        try
        {
            var extension = (await native.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Id == entry.Id);
            var desired = entry.EnabledFor(profile.Name);
            if (extension is null && !desired && !bindings.ContainsKey(profile.Name)) return;
            if (extension is null || bindings.GetValueOrDefault(profile.Name)?.Revision != entry.Revision)
                extension = await native.AddBrowserExtensionAsync(Store.Files(entry));
            if (extension.Id != entry.Id) throw new InvalidOperationException("Extension identity changed unexpectedly.");
            if (extension.IsEnabled != desired) await extension.EnableAsync(desired);
            bindings[profile.Name] = new(entry.Revision);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            bindings[profile.Name] = new(bindings.GetValueOrDefault(profile.Name)?.Revision ?? "", "Could not apply. Retry this profile.");
            await Store.SaveAsync(entry with { Bindings = bindings }, CancellationToken.None);
            throw;
        }
        await Store.SaveAsync(entry with { Bindings = bindings }, token);
    }
    private async Task ApplyEverywhereAsync(string slot, CancellationToken token)
    {
        var catalog = await Store.ReadAsync(token);
        Exception? failure = null;
        foreach (var profile in catalog.Profiles.Where(p => !p.Private))
        {
            var entry = (await Store.ReadAsync(token)).Library.Single(e => e.Slot == slot);
            if (!entry.EnabledFor(profile.Name) && !entry.Bindings.ContainsKey(profile.Name)) continue;
            try { using var lease = await OpenAsync(profile, token); await ApplyAsync(entry, profile, lease.Profile, token); }
            catch (Exception error) when (error is not OperationCanceledException) { failure ??= error; }
        }
        if (failure is not null) throw new InvalidOperationException("Some profiles could not be updated. Their saved choices are retained; use Retry or reopen the widget.", failure);
    }
    internal async Task DeleteProfileAsync(string name, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        token = deadline.Token;
        await InitializeAsync(token);
        await operations.WaitAsync(token);
        try
        {
            await RecoverPendingAsync();
            if (!name.StartsWith("private-", StringComparison.Ordinal))
            {
                var environment = await runtime.GetAsync().WaitAsync(token);
                var options = environment.CreateCoreWebView2ControllerOptions(); options.ProfileName = name;
                var view = new WebView2();
                Exception? initializationError = null;
                view.CoreWebView2Initialized += (_, args) => initializationError = args.Exception;
                try
                {
                    await view.EnsureCoreWebView2Async(environment, options).AsTask().WaitAsync(token);
                    if (view.CoreWebView2 is { } core) core.Profile.Delete();
                    else if (initializationError?.HResult != unchecked((int)0x8007012F))
                        throw new InvalidOperationException("Browser profile deletion could not initialize: " + initializationError?.ToString(), initializationError);
                }
                catch (System.Runtime.InteropServices.COMException error) when (error.HResult == unchecked((int)0x8007012F))
                { /* WebView already owns this durable deletion; it retries locked files on browser startup. */ }
                finally { view.Close(); }
            }
            await Store.RemoveProfileAsync(name, CancellationToken.None);
        }
        finally { operations.Release(); }
    }

    internal async Task SetPolicyAsync(string slot, bool? defaultEnabled, string? profileName, ExtensionOverride choice, CancellationToken token)
    {
        await InitializeAsync(token); await operations.WaitAsync(token);
        try
        {
            await RecoverPendingAsync();
            var catalog = await Store.ReadAsync(token);
            var entry = catalog.Library.Single(e => e.Slot == slot);
            if (profileName is not null && !catalog.Profiles.Any(p => p.Name == profileName && !p.Private)) throw new InvalidOperationException("This profile is not available.");
            var overrides = new Dictionary<string, ExtensionOverride>(entry.Overrides);
            if (profileName is not null) { if (choice == ExtensionOverride.Default) overrides.Remove(profileName); else overrides[profileName] = choice; }
            var next = entry with { EnabledByDefault = defaultEnabled ?? entry.EnabledByDefault, Overrides = overrides };
            await Store.SaveAsync(next, token);
            if (defaultEnabled is not null) await ApplyEverywhereAsync(slot, token);
            else if (profileName is not null)
            {
                var profile = catalog.Profiles.Single(p => p.Name == profileName);
                using var lease = await OpenAsync(profile, token);
                await ApplyAsync(next, profile, lease.Profile, token);
            }
        }
        finally { operations.Release(); }
    }
    internal async Task<string> InstallAsync(Import import, string? replaceSlot, bool enabledByDefault, CancellationToken token)
    {
        await InitializeAsync(token); await operations.WaitAsync(token);
        try
        {
            await RecoverPendingAsync();
            var catalog = await Store.ReadAsync(token);
            var previous = replaceSlot is null ? null : catalog.Library.Single(e => e.Slot == replaceSlot);
            if (previous is not null && previous.Manifest.Key != import.Manifest.Key) throw new InvalidDataException("This package has a different extension identity. Install it separately.");
            if (previous is null && catalog.Library.Any(e => import.Manifest.Key is not null ? e.Manifest.Key == import.Manifest.Key : e.Manifest.Name == import.Manifest.Name))
                throw new InvalidOperationException("This extension is already in your library. Open it to update or enable it for another widget.");
            var slot = previous?.Slot ?? Guid.NewGuid().ToString("N");
            var overrides = previous?.Overrides ?? new Dictionary<string, ExtensionOverride>();
            var next = new ExtensionEntry(slot, previous?.Id ?? "", import.Manifest, Guid.NewGuid().ToString("N"),
                previous?.EnabledByDefault ?? enabledByDefault, overrides, previous?.Bindings ?? new());
            var root = Store.SlotRoot(slot); var files = Store.Files(next); var backup = Path.Combine(root, "previous");
            // A committed operation can leave only its backup if cleanup was interrupted.
            if (Directory.Exists(backup)) await Task.Run(() => Directory.Delete(backup, true));
            var transaction = new ExtensionTransaction(previous, next, catalog.Profiles.Where(p => !p.Private).Select(p => p.Name).ToArray());
            await WriteJournalAsync(slot, transaction);
            try
            {
                await Task.Run(() => { if (Directory.Exists(files)) Directory.Move(files, backup); Directory.Move(import.Folder, files); });
                using (var validation = await OpenAsync(ValidationProfile, CancellationToken.None))
                {
                    var extension = await validation.Profile.AddBrowserExtensionAsync(files);
                    var id = extension.Id; await extension.RemoveAsync();
                    if (previous is not null && id != previous.Id) throw new InvalidDataException("The update changed the extension identity.");
                    next = next with { Id = id };
                }
                transaction = transaction with { Next = next };
                await WriteJournalAsync(slot, transaction);
                await Store.SaveAsync(next, CancellationToken.None);
                await ApplyEverywhereAsync(slot, CancellationToken.None);
                File.Delete(Path.Combine(root, "pending.json"));
            }
            catch { await RestoreAsync(slot, transaction); throw; }
            try { if (Directory.Exists(backup)) await Task.Run(() => Directory.Delete(backup, true)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Diagnostics.FrontendFailureLog.Current.Write("extension-backup-cleanup", error); }
            return slot;
        }
        finally { operations.Release(); }
    }
    internal async Task RemoveAsync(string slot, CancellationToken token)
    {
        await InitializeAsync(token); await operations.WaitAsync(token);
        try
        {
            await RecoverPendingAsync();
            var catalog = await Store.ReadAsync(token); var entry = catalog.Library.Single(e => e.Slot == slot);
            var transaction = new ExtensionTransaction(entry, null, entry.Bindings.Keys.ToArray());
            await WriteJournalAsync(slot, transaction);
            await CompleteRemovalAsync(slot, transaction, catalog);
        }
        finally { operations.Release(); }
    }
    private async Task WriteJournalAsync(string slot, ExtensionTransaction transaction)
    {
        recoveryRequired = true;
        var root = Store.SlotRoot(slot); var journal = Path.Combine(root, "pending.json");
        await Task.Run(() => { Directory.CreateDirectory(root); File.WriteAllText(journal + ".tmp", JsonSerializer.Serialize(transaction, ExtensionJsonContext.Default.ExtensionTransaction)); File.Move(journal + ".tmp", journal, true); });
    }
    private async Task RecoverPendingAsync()
    {
        if (!recoveryRequired) return;
        await RecoverAsync();
        recoveryRequired = false;
    }
    private async Task RecoverAsync()
    {
        var packages = Path.Combine(Store.Root, "packages");
        var journals = await Task.Run(() => Directory.Exists(packages) ? Directory.EnumerateDirectories(packages).Select(d => Path.Combine(d, "pending.json")).Where(File.Exists).ToArray() : []);
        foreach (var journal in journals)
        {
            var slot = Path.GetFileName(Path.GetDirectoryName(journal))!; _ = Store.SlotRoot(slot);
            if (new FileInfo(journal).Length > 1024 * 1024) throw new InvalidDataException("Extension transaction is invalid.");
            var transaction = JsonSerializer.Deserialize(await File.ReadAllTextAsync(journal), ExtensionJsonContext.Default.ExtensionTransaction) ?? throw new InvalidDataException("Extension transaction is invalid.");
            if (transaction.Next is null) await CompleteRemovalAsync(slot, transaction, await Store.ReadAsync(default));
            else await RestoreAsync(slot, transaction);
        }
    }
    private async Task CompleteRemovalAsync(string slot, ExtensionTransaction transaction, ExtensionCatalog catalog)
    {
        foreach (var profile in catalog.Profiles.Where(p => !p.Private && transaction.Profiles.Contains(p.Name)))
        {
            using var lease = await OpenAsync(profile, default);
            var extension = (await lease.Profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Id == transaction.Previous?.Id);
            if (extension is not null) await extension.RemoveAsync();
        }
        await Store.ForgetAsync(slot, default);
        await Task.Run(() => { var root = Store.SlotRoot(slot); if (Directory.Exists(root)) Directory.Delete(root, true); });
    }
    private async Task RestoreAsync(string slot, ExtensionTransaction transaction)
    {
        var root = Store.SlotRoot(slot); var files = Path.Combine(root, "files"); var backup = Path.Combine(root, "previous");
        if (transaction.Previous is { } previous)
        {
            if (previous.Slot != slot) throw new InvalidDataException("Extension transaction identity is invalid.");
            if (Directory.Exists(backup)) await Task.Run(() => { if (Directory.Exists(files)) Directory.Delete(files, true); Directory.Move(backup, files); });
            // Force every attached native profile to re-register the restored files.
            var current = (await Store.ReadAsync(default)).Library.FirstOrDefault(e => e.Slot == slot);
            var attached = previous.Bindings.Keys.Concat(current?.Bindings.Keys ?? Enumerable.Empty<string>()).Distinct();
            await Store.SaveAsync(previous with { Bindings = attached.ToDictionary(p => p, _ => new ExtensionBinding("")) }, default);
            await ApplyEverywhereAsync(slot, default);
        }
        else
        {
            var catalog = await Store.ReadAsync(default);
            foreach (var profile in catalog.Profiles.Where(p => !p.Private && transaction.Profiles.Contains(p.Name)))
            {
                using var lease = await OpenAsync(profile, default);
                var native = (await lease.Profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Id == transaction.Next?.Id);
                if (native is not null) await native.RemoveAsync();
            }
            await Store.ForgetAsync(slot, default);
            if (Directory.Exists(files)) await Task.Run(() => Directory.Delete(files, true));
        }
        File.Delete(Path.Combine(root, "pending.json"));
    }
}
