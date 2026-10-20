using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;

namespace WidgetRail.Tests.PlayniteLibraryCommunityApplication;

[TestClass]
public sealed class PlayniteSetupTests
{
    [TestMethod]
    [TestCategory("LivePlaynite")]
    public async Task LiveStoppedDiscoveryFindsRememberedInstallationWithoutStartingIt()
    {
        if (Environment.GetEnvironmentVariable("WRAIL_TEST_LIVE_PLAYNITE_STOPPED") != "1")
            Assert.Inconclusive("Explicit opt-in required for read-only stopped installation discovery.");
        var platform = new PlayniteSetupPlatform();
        Assert.IsFalse(platform.HasRunningProcess(), "Playnite must already be stopped.");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WidgetRail", "community-apps", "widgetrail.samples.playnite-library");
        var service = new PlayniteSetupService(root, Path.Combine(root, "unused"), platform);
        var found = await service.DiscoverAsync(null, null, default);
        Assert.AreEqual(1, found.Installations.Count, found.Message);
        Assert.IsNull(found.Message);
        Assert.IsNotNull(await service.StoppedInstallationAsync(default));
        Assert.IsFalse(platform.HasRunningProcess());
    }

    [TestMethod]
    [TestCategory("LivePlaynite")]
    public async Task LiveGuidedSetupReusesHealthyIntegrationWithoutRestart()
    {
        if (Environment.GetEnvironmentVariable("WRAIL_TEST_LIVE_PLAYNITE_SETUP") != "1")
            Assert.Inconclusive("Explicit opt-in required; validates the current local integration without changing it.");
        var bundle = Environment.GetEnvironmentVariable("WRAIL_TEST_PLAYNITE_SETUP_BUNDLE");
        Assert.IsFalse(string.IsNullOrEmpty(bundle));
        var temporary = Path.Combine(Path.GetTempPath(), "WidgetRail.Setup.Live", Guid.NewGuid().ToString("N"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var platform = new PlayniteSetupPlatform();
            var before = await platform.ProcessesAsync(timeout.Token);
            Assert.AreEqual(1, before.Count, "This check requires one running Playnite installation.");
            var credentials = new FakeCredentials();
            var service = new PlayniteSetupService(temporary, bundle!, platform, credentials: credentials);
            var found = await service.DiscoverAsync(null, null, timeout.Token);
            var current = found.Installations.Single(item => item.Running);
            var plan = await service.InspectAsync(current, timeout.Token);
            Assert.AreEqual(PlayniteSetupAction.Connect, plan.Action, plan.Message);
            var result = await service.ApplyAsync(plan, _ => { }, timeout.Token);
            Assert.IsTrue(result.Connected, result.Message);
            Assert.IsFalse(string.IsNullOrEmpty(credentials.Value));
            var after = await platform.ProcessesAsync(timeout.Token);
            Assert.AreEqual(before[0].Id, after.Single().Id, "Reusing a working integration must not restart Playnite.");
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
    [TestMethod]
    public async Task DiscoveryUsesQuotedRunningProfileInsteadOfGuessingPortableDefault()
    {
        using var fixture = new Fixture();
        fixture.Platform.Running = true;
        var found = await fixture.Service.DiscoverAsync(null, null, default);
        Assert.AreEqual(1, found.Installations.Count);
        Assert.AreEqual(fixture.Data, found.Installations[0].DataDirectory);
        Assert.IsTrue(found.Installations[0].Running);
        Assert.AreEqual(fixture.Program, PlayniteSetupPlatform.DataDirectory(fixture.Program, null));
        File.WriteAllText(Path.Combine(fixture.Program, "unins000.exe"), "fixture");
        Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playnite"),
            PlayniteSetupPlatform.DataDirectory(fixture.Program, null));
    }

    [TestMethod]
    public async Task HiddenStartPreservesCustomProfileAndNormalLibraryUpdates()
    {
        using var fixture = new Fixture();
        fixture.Install("1.5.3", Fixture.Original);
        var info = PlayniteSetupPlatform.LaunchInfo(fixture.Installation);
        CollectionAssert.AreEqual(new[] { "--startdesktop", "--startclosedtotray", "--hidesplashscreen", "--userdatadir", fixture.Data }, info.ArgumentList.ToArray());
        var fullscreen = PlayniteSetupPlatform.LaunchInfo(fixture.Installation with { Fullscreen = true });
        Assert.AreEqual(Path.Combine(fixture.Program, "Playnite.DesktopApp.exe"), fullscreen.FileName,
            "A background connection must not reopen fullscreen or apply its display mode.");
        CollectionAssert.AreEqual(info.ArgumentList.ToArray(), fullscreen.ArgumentList.ToArray());
        Assert.IsFalse(info.UseShellExecute);
        var result = await fixture.Service.StartAsync(fixture.Installation, _ => { }, default);
        Assert.IsTrue(result.Connected, result.Message);
        Assert.AreEqual(1, fixture.Platform.Starts);
        Assert.AreEqual(0, fixture.Platform.Stops);
        CollectionAssert.AreEqual(Fixture.Original, File.ReadAllBytes(fixture.Dll));
    }

    [TestMethod]
    public async Task MissingRememberedProfileDoesNotFallBackOrStartAFreshLibrary()
    {
        using var fixture = new Fixture();
        new PlayniteSetupDiscoveryService(fixture.Platform, fixture.State).Remember(fixture.Installation);
        var selected = fixture.Installation;
        var moved = Path.Combine(fixture.Root, "moved profile");
        Directory.Move(fixture.Data, moved);
        var found = await fixture.Service.DiscoverAsync(null, null, default);
        Assert.AreEqual(0, found.Installations.Count);
        StringAssert.Contains(found.Message!, "saved Playnite data folder");
        Assert.IsNull(await fixture.Service.StoppedInstallationAsync(default));
        var result = await fixture.Service.StartAsync(selected, _ => { }, default);
        Assert.IsFalse(result.Connected);
        Assert.AreEqual(0, fixture.Platform.Starts);
        Assert.IsFalse(Directory.Exists(fixture.Data));
    }

    [TestMethod]
    public async Task FreshSetupDeploysBundleAndConnectsWithoutCopyingToken()
    {
        using var fixture = new Fixture();
        Assert.IsNotNull(await fixture.Service.StoppedInstallationAsync(default),
            "A missing Bridge must still offer setup for the detected stopped installation.");
        var plan = await fixture.Service.InspectAsync(fixture.Installation, default);
        Assert.AreEqual(PlayniteSetupAction.Install, plan.Action);
        var result = await fixture.Service.ApplyAsync(plan, _ => { }, default);
        Assert.IsTrue(result.Connected, result.Message);
        Assert.AreEqual(Fixture.Token, fixture.Credentials.Value);
        Assert.AreEqual(PlayniteSetupPackage.Version, PlayniteSetupFiles.ReadManifest(Path.Combine(fixture.Extension, "extension.yaml"))["Version"]);
        Assert.IsTrue(PlayniteSetupFiles.ReadObject(fixture.PluginConfig)["NetworkPromptDismissed"]!.GetValue<bool>());
        Assert.AreEqual(1, fixture.Connection.Reads);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.State, "playnite-installation.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Extension, "previous")));
    }

    [TestMethod]
    public async Task HealthyUnknownBuildIsReusedWithoutRestartOrOverwrite()
    {
        using var fixture = new Fixture();
        fixture.Install("9.0.0", [9, 8, 7]);
        fixture.Platform.Running = true;
        var plan = await fixture.Service.InspectAsync(fixture.RunningInstallation, default);
        Assert.AreEqual(PlayniteSetupAction.Connect, plan.Action);
        var result = await fixture.Service.ApplyAsync(plan, _ => { }, default);
        Assert.IsTrue(result.Connected, result.Message);
        Assert.AreEqual(0, fixture.Platform.Stops);
        Assert.AreEqual(0, fixture.Platform.Starts);
        CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, File.ReadAllBytes(fixture.Dll));
    }

    [TestMethod]
    public async Task UnknownBrokenBuildIsNeverReplaced()
    {
        using var fixture = new Fixture();
        fixture.Install("9.0.0", [9, 8, 7]);
        fixture.Platform.Running = true;
        fixture.Connection.Status = PlayniteSetupConnectionKind.EvalUnavailable;
        var plan = await fixture.Service.InspectAsync(fixture.RunningInstallation, default);
        Assert.AreEqual(PlayniteSetupAction.Manual, plan.Action);
        Assert.IsFalse((await fixture.Service.ApplyAsync(plan, _ => { }, default)).Connected);
        Assert.AreEqual(0, fixture.Platform.Stops);
        CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, File.ReadAllBytes(fixture.Dll));
    }

    [TestMethod]
    public async Task OfflineRunningIntegrationOffersRestartWithoutReplacingFiles()
    {
        using var fixture = new Fixture();
        fixture.Install("9.0.0", [9, 8, 7]);
        fixture.Platform.Running = true;
        fixture.Connection.Status = PlayniteSetupConnectionKind.Offline;
        var plan = await fixture.Service.InspectAsync(fixture.RunningInstallation, default);
        Assert.AreEqual(PlayniteSetupAction.Restart, plan.Action);
        fixture.Platform.OnStart = () => fixture.Connection.Status = PlayniteSetupConnectionKind.Ready;
        var result = await fixture.Service.ApplyAsync(plan, _ => { }, default);
        Assert.IsTrue(result.Connected, result.Message);
        Assert.AreEqual(1, fixture.Platform.Stops);
        Assert.AreEqual(1, fixture.Platform.Starts);
        CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, File.ReadAllBytes(fixture.Dll));
    }

    [TestMethod]
    public async Task EnablePreservesOtherDisabledPluginsAndBridgePreferences()
    {
        using var fixture = new Fixture();
        fixture.Install("9.0.0", [9, 8, 7]);
        File.WriteAllText(fixture.MainConfig, "{\"DisabledPlugins\":[\"other\",\"PlayniteBridge_f47ac10b\"],\"OtherSetting\":17}");
        File.WriteAllText(fixture.PluginConfig, "{\"SyncEnabled\":true,\"Custom\":\"keep\"}");
        var plan = await fixture.Service.InspectAsync(fixture.Installation, default);
        Assert.AreEqual(PlayniteSetupAction.Enable, plan.Action);
        var result = await fixture.Service.ApplyAsync(plan, _ => { }, default);
        Assert.IsTrue(result.Connected, result.Message);
        CollectionAssert.AreEqual(new[] { "other" }, PlayniteSetupFiles.ReadObject(fixture.MainConfig)["DisabledPlugins"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
        Assert.AreEqual(17, PlayniteSetupFiles.ReadObject(fixture.MainConfig)["OtherSetting"]!.GetValue<int>());
        var settings = PlayniteSetupFiles.ReadObject(fixture.PluginConfig);
        Assert.IsTrue(settings["SyncEnabled"]!.GetValue<bool>());
        Assert.AreEqual("keep", settings["Custom"]!.GetValue<string>());
        CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, File.ReadAllBytes(fixture.Dll));
    }

    [TestMethod]
    public async Task FailedVerificationRestoresFilesSettingsAndPreviousCredential()
    {
        using var fixture = new Fixture();
        fixture.Install("1.5.3", Fixture.Original);
        fixture.Platform.Running = true;
        fixture.Connection.Status = PlayniteSetupConnectionKind.EvalUnavailable;
        fixture.Credentials.Value = "pb_previous";
        File.WriteAllText(fixture.MainConfig, "{\"DisabledPlugins\":[\"other\"],\"OtherSetting\":17}");
        File.WriteAllText(fixture.PluginConfig, "{\"Custom\":\"unchanged\"}");
        var mainBefore = File.ReadAllBytes(fixture.MainConfig);
        var pluginBefore = File.ReadAllBytes(fixture.PluginConfig);
        var plan = await fixture.Service.InspectAsync(fixture.RunningInstallation, default);
        Assert.AreEqual(PlayniteSetupAction.Repair, plan.Action);
        var result = await fixture.Service.ApplyAsync(plan, _ => { }, default);
        Assert.IsFalse(result.Connected);
        StringAssert.Contains(result.Message, "restored");
        CollectionAssert.AreEqual(Fixture.Original, File.ReadAllBytes(fixture.Dll));
        CollectionAssert.AreEqual(mainBefore, File.ReadAllBytes(fixture.MainConfig));
        CollectionAssert.AreEqual(pluginBefore, File.ReadAllBytes(fixture.PluginConfig));
        Assert.AreEqual("pb_previous", fixture.Credentials.Value);
        Assert.IsTrue(fixture.Platform.Running);
    }

    [TestMethod]
    public async Task StalePlanAndShutdownFailureDoNotChangeInstalledFiles()
    {
        using var fixture = new Fixture();
        fixture.Install("1.5.3", Fixture.Original);
        var plan = await fixture.Service.InspectAsync(fixture.Installation, default);
        File.WriteAllBytes(fixture.Dll, [7, 7]);
        Assert.IsFalse((await fixture.Service.ApplyAsync(plan, _ => { }, default)).Connected);
        Assert.AreEqual(0, fixture.Platform.Stops);
        CollectionAssert.AreEqual(new byte[] { 7, 7 }, File.ReadAllBytes(fixture.Dll));
        File.WriteAllBytes(fixture.Dll, Fixture.Original);
        fixture.Platform.RefuseShutdown = true;
        Assert.IsFalse((await fixture.Service.ApplyAsync(plan, _ => { }, default)).Connected);
        CollectionAssert.AreEqual(Fixture.Original, File.ReadAllBytes(fixture.Dll));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InterruptedDeploymentCanRecoverBeforeOrAfterCommit(bool committed)
    {
        using var fixture = new Fixture();
        fixture.Install("1.5.3", Fixture.Original);
        var deployment = new PlayniteSetupDeployment(fixture.Installation, fixture.Extension, fixture.Package);
        deployment.Prepare(true);
        if (committed) deployment.Commit();
        var plan = await fixture.Service.InspectAsync(fixture.Installation, default);
        Assert.AreEqual("Finish interrupted setup", plan.Title);
        var result = await fixture.Service.ApplyAsync(plan, _ => { }, default);
        Assert.IsTrue(result.Connected, result.Message);
        Assert.IsFalse(deployment.NeedsRecovery);
        CollectionAssert.AreEqual(Fixture.Replacement, File.ReadAllBytes(fixture.Dll));
    }

    [TestMethod]
    public async Task MultipleInstallationsAndUnavailableArgumentsAreNotGuessed()
    {
        using var fixture = new Fixture();
        var second = fixture.CreateProgram("second");
        fixture.Platform.Folders = [fixture.Program, second];
        var discovery = await fixture.Service.DiscoverAsync(null, null, default);
        Assert.AreEqual(2, discovery.Installations.Count);
        Assert.IsNull(await fixture.Service.StoppedInstallationAsync(default));
        fixture.Platform.Folders = [fixture.Program];
        fixture.Platform.Running = true;
        fixture.Platform.HideArguments = true;
        discovery = await fixture.Service.DiscoverAsync(null, null, default);
        Assert.AreEqual(0, discovery.Installations.Count);
        StringAssert.Contains(discovery.Message!, "could not be determined");
    }

    [TestMethod]
    public async Task TamperedBundleAndDuplicateIntegrationsStopBeforeMutation()
    {
        using var fixture = new Fixture();
        File.AppendAllText(fixture.Package.PackagePath, "changed");
        await Assert.ThrowsAsync<PlayniteSetupException>(async () => await fixture.Service.InspectAsync(fixture.Installation, default));
        Assert.AreEqual(0, fixture.Platform.Stops);
        fixture.Install("9.0.0", [9]);
        var duplicate = Path.Combine(fixture.Data, "Extensions", "duplicate");
        Directory.CreateDirectory(duplicate);
        File.Copy(Path.Combine(fixture.Extension, "extension.yaml"), Path.Combine(duplicate, "extension.yaml"));
        await Assert.ThrowsAsync<PlayniteSetupException>(async () => await fixture.Service.InspectAsync(fixture.Installation, default));
        Assert.AreEqual(0, fixture.Platform.Stops);
    }

    private sealed class Fixture : IDisposable
    {
        internal static readonly byte[] Original = Encoding.UTF8.GetBytes("original bridge");
        internal static readonly byte[] Replacement = Encoding.UTF8.GetBytes("replacement bridge");
        internal const string Token = "pb_0123456789abcdef0123456789abcdef";
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "WidgetRail.Setup.Tests", Guid.NewGuid().ToString("N"));
        internal string Program { get; }
        internal string Data { get; }
        internal string State => Path.Combine(Root, "state");
        internal string Extension => Path.Combine(Data, "Extensions", PlayniteSetupFiles.ExtensionId);
        internal string Dll => Path.Combine(Extension, "PlayniteBridge.dll");
        internal string MainConfig => Path.Combine(Data, "config.json");
        internal string PluginConfig => Path.Combine(PlayniteSetupFiles.PluginData(Data), "config.json");
        internal FakePlatform Platform { get; }
        internal FakeConnection Connection { get; } = new();
        internal FakeCredentials Credentials { get; } = new();
        internal PlayniteSetupPackage Package { get; }
        internal PlayniteSetupService Service { get; }
        internal PlayniteSetupInstallation Installation => PlayniteSetupDiscoveryService.Candidate(Program, Data, false, false);
        internal PlayniteSetupInstallation RunningInstallation => Installation with { Running = true };
        internal Fixture()
        {
            Directory.CreateDirectory(Root);
            Program = CreateProgram("application");
            Data = Path.Combine(Root, "custom profile");
            Directory.CreateDirectory(Data);
            Platform = new(Program, Data);
            var bundle = Path.Combine(Root, "bundle");
            Directory.CreateDirectory(bundle);
            Package = new(bundle);
            using (var archive = ZipFile.Open(Package.PackagePath, ZipArchiveMode.Create))
            {
                Add("PlayniteBridge.dll", Replacement);
                Add("extension.yaml", Encoding.UTF8.GetBytes($"Id: {PlayniteSetupFiles.ExtensionId}\nVersion: {PlayniteSetupPackage.Version}\nModule: PlayniteBridge.dll\n"));
                Add("LICENSE", Encoding.UTF8.GetBytes("Fixture license"));
                Add("WidgetRail.Build.json", Encoding.UTF8.GetBytes("{}"));
                void Add(string name, byte[] content) { using var stream = archive.CreateEntry(name).Open(); stream.Write(content); }
            }
            PlayniteSetupFiles.WriteObject(Path.Combine(bundle, "bridge-package.json"), new()
            {
                ["version"] = PlayniteSetupPackage.Version, ["sha256"] = PlayniteSetupFiles.Digest(Package.PackagePath),
                ["dllSha256"] = Convert.ToHexString(SHA256.HashData(Replacement)),
                ["knownOriginalDllSha256"] = Convert.ToHexString(SHA256.HashData(Original)),
            });
            Service = new(State, bundle, Platform, Connection, Credentials);
        }
        internal string CreateProgram(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "Playnite.DesktopApp.exe"), "fixture");
            File.WriteAllText(Path.Combine(path, "Playnite.dll"), "fixture");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Playnite.SDK.dll"), Path.Combine(path, "Playnite.SDK.dll"));
            return path;
        }
        internal void Install(string version, byte[] dll)
        {
            Directory.CreateDirectory(Extension);
            File.WriteAllBytes(Dll, dll);
            File.WriteAllText(Path.Combine(Extension, "extension.yaml"), $"Id: {PlayniteSetupFiles.ExtensionId}\nVersion: {version}\nModule: PlayniteBridge.dll\n");
            Platform.CreateToken();
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class FakePlatform(string program, string data) : IPlayniteSetupPlatform
    {
        internal bool Running;
        internal bool HideArguments;
        internal bool RefuseShutdown;
        internal int Starts;
        internal int Stops;
        internal Action? OnStart;
        internal string[] Folders = [program];
        public bool HasRunningProcess() => Running;
        public ValueTask<IReadOnlyList<PlayniteSetupProcess>> ProcessesAsync(CancellationToken token) => ValueTask.FromResult<IReadOnlyList<PlayniteSetupProcess>>(
            Running ? [new(1, Path.Combine(program, "Playnite.DesktopApp.exe"), HideArguments ? null : $"\"{Path.Combine(program, "Playnite.DesktopApp.exe")}\" --userdatadir \"{data}\"", false)] : []);
        public IEnumerable<string> InstallationFolders() => Folders;
        public ValueTask StopAsync(PlayniteSetupInstallation installation, CancellationToken token)
        { Stops++; if (RefuseShutdown) throw new PlayniteSetupException("Fixture refuses shutdown."); Running = false; return ValueTask.CompletedTask; }
        public ValueTask StartAsync(PlayniteSetupInstallation installation, CancellationToken token)
        { if (!Running) Starts++; Running = true; CreateToken(); OnStart?.Invoke(); return ValueTask.CompletedTask; }
        internal void CreateToken()
        {
            var path = Path.Combine(PlayniteSetupFiles.PluginData(data), "auth.json");
            if (!File.Exists(path)) PlayniteSetupFiles.WriteObject(path, new() { ["token"] = Fixture.Token });
        }
    }
    private sealed class FakeConnection : IPlayniteSetupConnection
    {
        internal PlayniteSetupConnectionKind Status = PlayniteSetupConnectionKind.Ready;
        internal int Reads;
        public ValueTask<PlayniteSetupConnectionKind> CheckAsync(PlayniteSetupInstallation installation, string credential, CancellationToken token)
        { Reads++; return ValueTask.FromResult(Status); }
    }
    private sealed class FakeCredentials : IPlayniteCredentialStore
    {
        internal string? Value;
        public ValueTask<string?> ReadAsync(CancellationToken token) => ValueTask.FromResult(Value);
        public ValueTask SaveAsync(string value, CancellationToken token) { Value = value; return ValueTask.CompletedTask; }
        public ValueTask DeleteAsync(CancellationToken token) { Value = null; return ValueTask.CompletedTask; }
    }
}
