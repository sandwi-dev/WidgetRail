using WidgetRail.PlatformBroker;
using WidgetRail.WindowsAppLibraryProvider;

internal static class PortableRegistrationScenarios
{
    internal static Task ExecutableAuthorityIsLocalAndLeased()
    {
        Assert.False(WindowsExecutableAuthorityReader.TryNormalizeLocalExecutablePath(
            @"\\server\share\Portable.exe", out _));
        Assert.False(WindowsExecutableAuthorityReader.TryNormalizeLocalExecutablePath(
            @"\\?\C:\Portable.exe", out _));
        Assert.False(WindowsExecutableAuthorityReader.TryNormalizeLocalExecutablePath(
            @"\\.\C:\Portable.exe", out _));
        Assert.False(WindowsExecutableAuthorityReader.TryNormalizeLocalExecutablePath(
            @"C:\Portable.com", out _));
        Assert.False(WindowsExecutableAuthorityReader.TryNormalizeLocalExecutablePath(
            "Portable.exe", out _));
        Assert.False(WindowsExecutableAuthorityReader.TryNormalizeLocalExecutablePath(
            @".\Portable.exe", out _));
        Assert.False(WindowsExecutableAuthorityReader.TryNormalizeLocalExecutablePath(
            @"C:Portable.exe", out _));
        var remaining =
            WindowsExecutableAuthorityReader.MaximumExecutablePathCharacters - 7;
        var components = new List<string>();
        while (remaining > 200)
        {
            components.Add(new string('a', 200));
            remaining -= 201;
        }
        components.Add(new string('a', remaining));
        var maximum = @"C:\" + string.Join('\\', components) + ".exe";
        Assert.True(WindowsExecutableAuthorityReader.TryNormalizeLocalExecutablePath(
            maximum, out _));
        Assert.False(WindowsExecutableAuthorityReader.TryNormalizeLocalExecutablePath(
            maximum[..^4] + "x.exe", out _));
        if (!OperatingSystem.IsWindows()) return Task.CompletedTask;

        using var temp = new PortableTemporaryDirectory();
        Assert.Equal(
            Path.Combine(Path.GetFullPath(temp.Path), "broker", "portable-apps"),
            WindowsPortableAppRegistrationPaths.ForCatalogRoot(temp.Path));
        var applicationDirectory = Path.Combine(temp.Path, "application");
        Directory.CreateDirectory(applicationDirectory);
        var path = Path.Combine(applicationDirectory, "Portable.exe");
        File.WriteAllBytes(path, [0x4d, 0x5a]);
        var reader = new WindowsExecutableAuthorityReader();
        using (var lease = reader.AcquireExact(path))
        {
            Assert.True(lease is not null);
            Assert.Equal(Path.GetFullPath(path), lease!.Authority.CanonicalPath);
            Assert.Throws<IOException>(() =>
                File.Open(path, FileMode.Open, FileAccess.Write, FileShare.Read).Dispose());
            Assert.Throws<IOException>(() => Directory.Move(
                applicationDirectory, applicationDirectory + ".moved"));
        }
        var moved = applicationDirectory + ".moved";
        Directory.Move(applicationDirectory, moved);
        Directory.Move(moved, applicationDirectory);

        var fileLink = Path.Combine(temp.Path, "PortableLink.exe");
        File.CreateSymbolicLink(fileLink, path);
        Assert.True(reader.AcquireExact(fileLink) is null);
        var directoryLink = Path.Combine(temp.Path, "application-link");
        Directory.CreateSymbolicLink(directoryLink, applicationDirectory);
        Assert.True(reader.AcquireExact(
            Path.Combine(directoryLink, "Portable.exe")) is null);

        var longDirectory = temp.Path;
        for (var index = 0; index < 3; ++index)
            longDirectory = Path.Combine(longDirectory, new string((char)('a' + index), 80));
        Directory.CreateDirectory(longDirectory);
        var longExecutable = Path.Combine(longDirectory, "LongPortable.exe");
        File.WriteAllBytes(longExecutable, [0x4d, 0x5a]);
        Assert.True(longExecutable.Length > 260);
        using var longLease = reader.AcquireExact(longExecutable);
        Assert.True(longLease is not null);
        return Task.CompletedTask;
    }

    internal static async Task RegistrationPersistsAndLaunchRevalidates()
    {
        using var temp = new PortableTemporaryDirectory();
        var path = Path.GetFullPath(Path.Combine(temp.Path, "Portable.exe"));
        var firstAuthority = Authority(path, 1);
        var observer = new MutableObserver();
        observer.Set(firstAuthority, "instance-one");
        var authority = new AuthorityReader(firstAuthority);
        var launcher = new Launcher();
        launcher.LeaseIsActive = () => authority.ActiveLeases == 1;
        var storeRoot = Path.Combine(temp.Path, "registrations");
        var identity = new BrokerWidgetIdentity(
            "dev.test.portable", "dev.test", "one");
        var otherIdentity = new BrokerWidgetIdentity(
            "dev.test.other", "dev.test", "one");

        await using var provider = Provider(
            observer, new WindowsPortableAppStore(storeRoot), authority, launcher);
        var observed = await provider.ObserveRunningAppsAsync(CancellationToken.None);
        Assert.Equal(1, observed.Items.Count);
        var candidate = observed.Items.Single();
        observer.SetDuplicate(firstAuthority, "instance-one");
        await ThrowsBroker(
            "stale_observation",
            () => provider.RegisterRunningAppAsync(
                identity,
                new RegisterRunningAppBackendRequest(
                    "saved-portable", candidate.StableProviderIdentity,
                    candidate.InstanceEvidence, "stale-revision"),
                CancellationToken.None));
        await ThrowsBroker(
            "stale_observation",
            () => provider.RegisterRunningAppAsync(
                identity,
                new RegisterRunningAppBackendRequest(
                    "saved-portable", candidate.StableProviderIdentity,
                    "stale-instance", observed.Revision),
                CancellationToken.None));
        var first = await provider.RegisterRunningAppAsync(
            identity,
            new RegisterRunningAppBackendRequest(
                "saved-portable", candidate.StableProviderIdentity,
                candidate.InstanceEvidence, observed.Revision),
            CancellationToken.None);
        Assert.False(first.AlreadyRegistered);
        Assert.Equal("Portable App", first.Item.DisplayName);
        Assert.Equal("source-portable", first.Item.SourceIdentity);
        Assert.True(first.Item.IsLaunchable);

        var repeated = await provider.RegisterRunningAppAsync(
            identity,
            new RegisterRunningAppBackendRequest(
                "saved-portable", candidate.StableProviderIdentity,
                candidate.InstanceEvidence, observed.Revision),
            CancellationToken.None);
        Assert.True(repeated.AlreadyRegistered);

        observer.SetConflicting(
            firstAuthority, firstAuthority with
            {
                FileIdentity = new WindowsExecutableFileIdentity(7, 99, 199),
            },
            "instance-one");
        await ThrowsBroker(
            "stale_observation",
            () => provider.RegisterRunningAppAsync(
                identity,
                new RegisterRunningAppBackendRequest(
                    "saved-portable", candidate.StableProviderIdentity,
                    candidate.InstanceEvidence, observed.Revision),
                CancellationToken.None));
        observer.SetDuplicate(firstAuthority, "instance-one");

        await using var restarted = Provider(
            observer, new WindowsPortableAppStore(storeRoot), authority, launcher);
        var restored = await restarted.ResolveRegisteredRunningAppsAsync(
            identity, ["saved-portable"], CancellationToken.None);
        Assert.Equal(1, restored.Count);
        Assert.Equal(first.Item.StableProviderIdentity, restored[0].StableProviderIdentity);
        Assert.Equal(0, (await restarted.ResolveRegisteredRunningAppsAsync(
            otherIdentity, ["saved-portable"], CancellationToken.None)).Count);

        await restarted.LaunchAppLibraryItemAsync(
            identity, restored[0].ProviderAppId, CancellationToken.None);
        Assert.Equal(1, launcher.Calls);
        Assert.Equal(0, authority.ActiveLeases);
        Assert.Equal(path, launcher.Last?.CanonicalPath);
        launcher.Failure = new System.ComponentModel.Win32Exception(
            1223, "private cancellation detail");
        var canceled = await ThrowsBroker(
            "elevation_cancelled",
            () => restarted.LaunchAppLibraryItemAsync(
                identity, restored[0].ProviderAppId, CancellationToken.None));
        Assert.Equal(
            "Administrator approval was canceled and the app was not opened.",
            canceled.Message);
        Assert.False(canceled.Message.Contains("private", StringComparison.Ordinal));
        launcher.Failure = new System.ComponentModel.Win32Exception(
            740, "private elevation detail");
        var required = await ThrowsBroker(
            "elevation_required",
            () => restarted.LaunchAppLibraryItemAsync(
                identity, restored[0].ProviderAppId, CancellationToken.None));
        Assert.Equal(
            "This app requires administrator approval and was not opened.",
            required.Message);
        Assert.False(required.Message.Contains("private", StringComparison.Ordinal));
        launcher.Failure = null;
        Assert.Equal(Path.GetDirectoryName(path), launcher.Last?.WorkingDirectory);
        await ThrowsBroker(
            "app_not_found",
            () => restarted.LaunchAppLibraryItemAsync(
                otherIdentity, restored[0].ProviderAppId, CancellationToken.None));

        var callsBeforeReplacement = launcher.Calls;
        var replacedAuthority = Authority(path, 2);
        authority.Current = replacedAuthority;
        observer.Set(replacedAuthority, "instance-two");
        Assert.Equal(0, (await restarted.ResolveRegisteredRunningAppsAsync(
            identity, ["saved-portable"], CancellationToken.None)).Count);
        await ThrowsBroker(
            "app_not_found",
            () => restarted.LaunchAppLibraryItemAsync(
                identity, restored[0].ProviderAppId, CancellationToken.None));
        Assert.Equal(callsBeforeReplacement, launcher.Calls);

        var replacedObservation = await restarted.ObserveRunningAppsAsync(
            CancellationToken.None);
        var replacedCandidate = replacedObservation.Items.Single();
        var replacement = await restarted.RegisterRunningAppAsync(
            identity,
            new RegisterRunningAppBackendRequest(
                "saved-portable", replacedCandidate.StableProviderIdentity,
                replacedCandidate.InstanceEvidence, replacedObservation.Revision),
            CancellationToken.None);
        Assert.False(replacement.AlreadyRegistered);
        var revalidated = await restarted.ResolveRegisteredRunningAppsAsync(
            identity, ["saved-portable"], CancellationToken.None);
        Assert.Equal(1, revalidated.Count);
        await restarted.LaunchAppLibraryItemAsync(
            identity, revalidated[0].ProviderAppId, CancellationToken.None);
        Assert.Equal(callsBeforeReplacement + 1, launcher.Calls);
        Assert.Equal(0, authority.ActiveLeases);
        Assert.Equal(replacedAuthority.FileIdentity, launcher.Last?.FileIdentity);

        authority.Current = null;
        Assert.Equal(0, (await restarted.ResolveRegisteredRunningAppsAsync(
            identity, ["saved-portable"], CancellationToken.None)).Count);
        await restarted.ForgetRunningAppAsync(
            identity, "saved-portable", CancellationToken.None);
        await restarted.ForgetRunningAppAsync(
            identity, "saved-portable", CancellationToken.None);
        Assert.False((await restarted.GetRunningAppRegistrationStateAsync(
            identity, CancellationToken.None)).Exists);
    }

    internal static async Task InstalledPrecedenceAndCapacityAreExplicit()
    {
        using var temp = new PortableTemporaryDirectory();
        var path = Path.GetFullPath(Path.Combine(temp.Path, "Installed.exe"));
        var authority = Authority(path, 1);
        var observer = new MutableObserver();
        observer.Set(authority, "instance-installed");
        var source = new InstalledSource(observer.StableIdentity);
        var store = new WindowsPortableAppStore(Path.Combine(temp.Path, "registrations"));
        var identity = new BrokerWidgetIdentity(
            "dev.test.portable", "dev.test", "one");
        await using var provider = new WindowsAppLibraryProvider(
            [source], ImmediateSta.Instance, observer, store,
            new AuthorityReader(authority), new Launcher(), TimeSpan.FromSeconds(5));
        var observed = await provider.ObserveRunningAppsAsync(CancellationToken.None);
        var candidate = observed.Items.Single();
        var installed = await provider.RegisterRunningAppAsync(
            identity,
            new RegisterRunningAppBackendRequest(
                "saved-installed", candidate.StableProviderIdentity,
                candidate.InstanceEvidence, observed.Revision),
            CancellationToken.None);
        Assert.True(installed.AlreadyRegistered);
        Assert.True(installed.Item.ProviderAppId.StartsWith(
            "app-", StringComparison.Ordinal));
        Assert.Equal("installed-source", installed.Item.SourceIdentity);
        Assert.False((await provider.GetRunningAppRegistrationStateAsync(
            identity, CancellationToken.None)).Exists);

        var capacityRoot = Path.Combine(temp.Path, "capacity");
        var portableStore = new WindowsPortableAppStore(capacityRoot);
        for (var index = 0; index < WindowsPortableAppStore.MaximumRegistrations; index++)
        {
            _ = await portableStore.UpsertAsync(
                identity,
                new PortableAppRegistration(
                    $"saved-{index:D3}", $"stable-{index:D3}", $"App {index:D3}",
                    Path.GetFullPath(Path.Combine(temp.Path, $"App{index:D3}.exe")),
                    new WindowsExecutableFileIdentity(1, (ulong)index + 1, 0)),
                CancellationToken.None);
        }
        await ThrowsBroker(
            "registration_capacity",
            () => portableStore.UpsertAsync(
                identity,
                new PortableAppRegistration(
                    "saved-overflow", "stable-overflow", "Overflow",
                    Path.GetFullPath(Path.Combine(temp.Path, "Overflow.exe")),
                    new WindowsExecutableFileIdentity(1, 999, 0)),
                CancellationToken.None));
        _ = await portableStore.RemoveAsync(
            identity, "saved-000", CancellationToken.None);
        _ = await portableStore.UpsertAsync(
            identity,
            new PortableAppRegistration(
                "saved-reclaimed", "stable-reclaimed", "Reclaimed",
                Path.GetFullPath(Path.Combine(temp.Path, "Reclaimed.exe")),
                new WindowsExecutableFileIdentity(1, 1000, 0)),
            CancellationToken.None);
        var full = await portableStore.ReadAsync(identity, CancellationToken.None);
        Assert.Equal(WindowsPortableAppStore.MaximumRegistrations, full.Items.Count);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            portableStore.ReadAsync(identity, canceled.Token));

        var document = Directory.GetFiles(
            capacityRoot, "*.json", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(
            document,
            "{\"schemaVersion\":1,\"schemaVersion\":1,\"revision\":1,\"items\":[]}");
        await ThrowsBroker(
            "registration_store_corrupt",
            () => portableStore.ReadAsync(identity, CancellationToken.None));
    }

    internal static async Task PackageRetirementIsCompleteAndWaitersStayRetired()
    {
        using var temp = new PortableTemporaryDirectory();
        var root = Path.Combine(temp.Path, "package-retirement");
        var first = new BrokerWidgetIdentity(
            "dev.test.shared", "unsigned." + new string('a', 64), "one");
        var second = new BrokerWidgetIdentity(
            "dev.test.shared", "unsigned." + new string('b', 64), "two");
        var neighbor = new BrokerWidgetIdentity(
            "dev.test.neighbor", "unsigned." + new string('c', 64), "one");
        var store = new WindowsPortableAppStore(root);
        await store.UpsertAsync(first, Registration("saved-first", "first", temp.Path, 1),
            CancellationToken.None);
        await store.UpsertAsync(second, Registration("saved-second", "second", temp.Path, 2),
            CancellationToken.None);
        await store.UpsertAsync(neighbor, Registration("saved-neighbor", "neighbor", temp.Path, 3),
            CancellationToken.None);

        var retired = await store.RetirePackageAsync(
            first.PackageId, CancellationToken.None);
        Assert.True(retired.Committed);
        Assert.Equal(0, (await store.ReadAsync(first, CancellationToken.None)).Items.Count);
        Assert.Equal(0, (await store.ReadAsync(second, CancellationToken.None)).Items.Count);
        Assert.Equal(1, (await store.ReadAsync(neighbor, CancellationToken.None)).Items.Count);
        var fresh = new BrokerWidgetIdentity(
            first.PackageId, "unsigned." + new string('d', 64), "fresh");
        await store.UpsertAsync(
            fresh, Registration("saved-fresh", "fresh", temp.Path, 4),
            CancellationToken.None);
        Assert.Equal(1, (await store.ReadAsync(fresh, CancellationToken.None)).Items.Count);

        var waitRoot = Path.Combine(temp.Path, "waiting-writer");
        Directory.CreateDirectory(waitRoot);
        var packageHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(first.PackageId))).ToLowerInvariant();
        var lockPath = Path.Combine(waitRoot, packageHash + ".lock");
        using var externalLock = new FileStream(
            lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var retirementWaiting = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var waitingStore = new WindowsPortableAppStore(
            waitRoot, () => retirementWaiting.TrySetResult());
        var retirement = waitingStore.RetirePackageAsync(
            first.PackageId, CancellationToken.None);
        await retirementWaiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var writerCancellation = new CancellationTokenSource();
        var oldWriter = waitingStore.UpsertAsync(
            first, Registration("saved-old", "old", temp.Path, 5),
            writerCancellation.Token);
        writerCancellation.Cancel();
        externalLock.Dispose();
        Assert.True((await retirement.WaitAsync(TimeSpan.FromSeconds(2))).Committed);
        await Assert.ThrowsAsync<OperationCanceledException>(() => oldWriter);
        Assert.Equal(0, (await waitingStore.ReadAsync(
            first, CancellationToken.None)).Items.Count);
    }

    internal static Task LaunchShapeIsExact()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Portable.exe"));
        var authority = Authority(path, 1);
        var start = WindowsPortableAppLauncher.CreateStartInfo(authority);
        Assert.Equal(path, start.FileName);
        Assert.Equal(Path.GetDirectoryName(path), start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
        Assert.False(start.ErrorDialog);
        Assert.Equal(string.Empty, start.Arguments);
        Assert.Equal(0, start.ArgumentList.Count);
        Assert.Equal(string.Empty, start.Verb);

        var consent = WindowsPortableAppLauncher.CreateShellConsentStartInfo(authority);
        Assert.Equal(path, consent.FileName);
        Assert.Equal(Path.GetDirectoryName(path), consent.WorkingDirectory);
        Assert.True(consent.UseShellExecute);
        Assert.False(consent.ErrorDialog);
        Assert.Equal(string.Empty, consent.Arguments);
        Assert.Equal(0, consent.ArgumentList.Count);
        Assert.Equal("open", consent.Verb);

        var normalAttempts = new List<System.Diagnostics.ProcessStartInfo>();
        new WindowsPortableAppLauncher(info =>
        {
            normalAttempts.Add(info);
            return true;
        }).Launch(authority, CancellationToken.None);
        Assert.Equal(1, normalAttempts.Count);
        Assert.False(normalAttempts[0].UseShellExecute);

        var consentAttempts = new List<System.Diagnostics.ProcessStartInfo>();
        new WindowsPortableAppLauncher(info =>
        {
            consentAttempts.Add(info);
            if (consentAttempts.Count == 1)
                throw new System.ComponentModel.Win32Exception(740);
            return true;
        }).Launch(authority, CancellationToken.None);
        Assert.Equal(2, consentAttempts.Count);
        Assert.False(consentAttempts[0].UseShellExecute);
        Assert.True(consentAttempts[1].UseShellExecute);
        Assert.Equal("open", consentAttempts[1].Verb);

        var canceledAttempts = 0;
        var canceled = Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            new WindowsPortableAppLauncher(_ =>
            {
                canceledAttempts++;
                throw new System.ComponentModel.Win32Exception(
                    canceledAttempts == 1 ? 740 : 1223);
            }).Launch(authority, CancellationToken.None));
        Assert.Equal(1223, canceled.NativeErrorCode);
        Assert.Equal(2, canceledAttempts);

        var deniedAttempts = 0;
        var denied = Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            new WindowsPortableAppLauncher(_ =>
            {
                deniedAttempts++;
                throw new System.ComponentModel.Win32Exception(5);
            }).Launch(authority, CancellationToken.None));
        Assert.Equal(5, denied.NativeErrorCode);
        Assert.Equal(1, deniedAttempts);
        return Task.CompletedTask;
    }

    private static WindowsAppLibraryProvider Provider(
        MutableObserver observer,
        IWindowsPortableAppStore store,
        IWindowsExecutableAuthorityReader authority,
        IWindowsPortableAppLauncher launcher) => new(
            [new InstalledSource(null)], ImmediateSta.Instance, observer,
            store, authority, launcher, TimeSpan.FromSeconds(5));

    private static WindowsExecutableAuthority Authority(string path, ulong id) =>
        new(path, new WindowsExecutableFileIdentity(7, id, id + 100));

    private static PortableAppRegistration Registration(
        string savedId,
        string identity,
        string root,
        ulong fileId) => new(
            savedId,
            "portable-" + identity,
            "Portable " + identity,
            Path.GetFullPath(Path.Combine(root, identity + ".exe")),
            new WindowsExecutableFileIdentity(9, fileId, fileId + 100));

    private static async Task<BrokerException> ThrowsBroker(string code, Func<Task> action)
    {
        try
        {
            await action();
            throw new InvalidOperationException($"Expected broker error {code}.");
        }
        catch (BrokerException exception)
        {
            Assert.Equal(code, exception.Code);
            return exception;
        }
    }

    private sealed class MutableObserver : IWindowsRunningAppObserver
    {
        private WindowsRunningAppObservation[] _values = [];
        internal string StableIdentity { get; private set; } = string.Empty;

        internal void Set(WindowsExecutableAuthority authority, string instance)
        {
            StableIdentity = WindowsStartMenuApplicationSource.IdentityForExecutable(
                authority.CanonicalPath);
            _values = [new(
                StableIdentity, instance, "Portable App", authority)];
        }

        internal void SetDuplicate(
            WindowsExecutableAuthority authority,
            string instance)
        {
            Set(authority, instance);
            _values = [_values[0], _values[0]];
        }

        internal void SetConflicting(
            WindowsExecutableAuthority first,
            WindowsExecutableAuthority second,
            string instance)
        {
            Set(first, instance);
            _values = [
                _values[0],
                new(StableIdentity, instance, "Portable App", second),
            ];
        }

        public IReadOnlyList<WindowsRunningAppObservation> Observe(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _values;
        }
    }

    private sealed class AuthorityReader(WindowsExecutableAuthority? current) :
        IWindowsExecutableAuthorityReader
    {
        internal WindowsExecutableAuthority? Current { get; set; } = current;
        internal int ActiveLeases { get; private set; }

        public IWindowsExecutableAuthorityLease? AcquireExact(string path)
        {
            if (Current is null || !string.Equals(
                    path, Current.CanonicalPath, StringComparison.OrdinalIgnoreCase))
                return null;
            ActiveLeases++;
            return new Lease(this, Current);
        }

        private sealed class Lease(
            AuthorityReader owner,
            WindowsExecutableAuthority authority) : IWindowsExecutableAuthorityLease
        {
            private AuthorityReader? _owner = owner;
            public WindowsExecutableAuthority Authority { get; } = authority;

            public void Dispose()
            {
                var current = Interlocked.Exchange(ref _owner, null);
                if (current is not null) current.ActiveLeases--;
            }
        }
    }

    private sealed class Launcher : IWindowsPortableAppLauncher
    {
        internal int Calls { get; private set; }
        internal WindowsExecutableAuthority? Last { get; private set; }
        internal Func<bool>? LeaseIsActive { get; set; }
        internal Exception? Failure { get; set; }

        public void Launch(
            WindowsExecutableAuthority authority,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(LeaseIsActive?.Invoke() ?? true);
            Calls++;
            Last = authority;
            if (Failure is not null) throw Failure;
        }
    }

    private sealed class InstalledSource(string? stableIdentity) : IGameLibrarySource
    {
        public string SourceIdentity => "installed-source";
        public string Attribution => "Installed";
        public GameLibrarySourceSnapshot Snapshot { get; private set; } =
            new("installed-source", "Installed", 0,
                GameLibrarySourceHealth.Unavailable, []);

        public GameLibrarySourceSnapshot Refresh(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = stableIdentity is null
                ? Array.Empty<GameLibrarySourceItem>()
                : [new GameLibrarySourceItem(
                    SourceIdentity, Attribution, stableIdentity, "Installed App",
                    WindowsAppLibraryKind.Application, true, true,
                    GameLibrarySourceActions.Launch, "none", "installed-item")];
            Snapshot = new(
                SourceIdentity, Attribution, Snapshot.SourceVersion + 1,
                GameLibrarySourceHealth.Healthy, items);
            return Snapshot;
        }

        public GameLibrarySourceItem? ResolveExact(
            GameLibrarySourceItem item,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return item;
        }

        public GameLibraryLaunchResult Launch(
            GameLibrarySourceItem exactItem,
            CancellationToken cancellationToken) =>
            new(AppLibraryLaunchObservationState.RequestAccepted,
                GameLibraryLaunchEvidence.None);

        public string? LoadArtwork(
            GameLibrarySourceItem exactItem,
            CancellationToken cancellationToken) => null;

        public void Dispose() { }
    }

    private sealed class ImmediateSta : IShellStaExecutor
    {
        internal static ImmediateSta Instance { get; } = new();

        public Task<T> RunAsync<T>(
            Func<CancellationToken, T> operation,
            CancellationToken cancellationToken) =>
            Task.FromResult(operation(cancellationToken));
    }

    private sealed class PortableTemporaryDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "WidgetRail-W193-" + Guid.NewGuid().ToString("N"));

        internal PortableTemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
