using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using WidgetRail.PackageDistribution;
using WidgetRail.PlatformDiagnostics;

namespace WidgetRail.WidgetBridge;

internal sealed record AppInstallation(string Version, string Edition, string Root, bool CanInstall);

internal sealed class BridgeAppUpdates : IAsyncDisposable
{
    private const string Repository = "sandwi-dev/WidgetRail";
    private readonly object _gate = new();
    private readonly AppInstallation _installation;
    private readonly string _cache;
    private readonly HttpMessageHandler? _http;
    private readonly Action<string> _launch;
    private readonly Action<string> _shortcut;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private Task? _job;
    private AppUpdateSnapshot _snapshot;
    private GitHubRelease? _release;
    private GitHubAsset? _asset;
    private string? _prepared;
    private bool _disposed;

    public BridgeAppUpdates(AppInstallation installation, string cache, HttpMessageHandler? http = null, Action<string>? launch = null, Action<string>? shortcut = null)
    {
        _installation = installation; _cache = Path.GetFullPath(cache); _http = http; _launch = launch ?? Launch; _shortcut = shortcut ?? WriteShortcut;
        _snapshot = new(0, "idle", installation.Version, CanInstall: installation.CanInstall,
            Message: installation.CanInstall ? "Check releases when you are ready. Updates are never installed automatically."
                : "Release checks are available. Install updates from the registered Inno installation, not a development candidate.");
    }

    internal static BridgeAppUpdates Create(string? profile)
    {
        var current = typeof(BridgeAppUpdates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
        var installation = new AppInstallation(current, "production", "", false);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\WidgetRail\Installation");
            if (key?.GetValue("ApplicationRoot") is string root && key.GetValue("Edition") is string edition && edition is "production" or "developer")
            {
                var info = Path.Combine(root, "release.json");
                if (File.Exists(info) && new FileInfo(info).Length <= 8 * 1024 * 1024)
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(info));
                    var version = json.RootElement.GetProperty("version").GetString()!;
                    var isCurrent = Path.GetFullPath(AppContext.BaseDirectory).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                    var normalProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail");
                    installation = new(isCurrent ? version : current, edition, root, isCurrent && (profile is null || Path.GetFullPath(profile).Equals(normalProfile, StringComparison.OrdinalIgnoreCase)));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) { }
        return new(installation, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail-Recovery"));
    }

    public ValueTask<AppUpdateSnapshot> HandleAsync(AppUpdateCommand command, CancellationToken token)
    {
        AppUpdateContract.ValidateCommand(command); token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (command.Action == "status") return ValueTask.FromResult(_snapshot);
            if (command.Action == "cancel")
            {
                _operation?.Cancel();
                if (_job?.IsCompleted != false) Set(_snapshot with { State = "cancelled", ReviewToken = null, Message = "Cancelled. Any prepared recovery files are retained locally." });
                return ValueTask.FromResult(_snapshot);
            }
            if (_job?.IsCompleted == false) return ValueTask.FromResult(_snapshot);
            if (command.Action == "install")
            {
                if (_snapshot.State != "ready" || command.ReviewToken != _snapshot.ReviewToken || !_installation.CanInstall || _prepared is null)
                    return ValueTask.FromResult(_snapshot);
                // This starts only the independent confirmation UI; it never closes
                // WidgetRail or launches setup while this app remains running.
                try { _launch(_prepared); Set(_snapshot with { State = "handoff", ReviewToken = null, Message = "Quit WidgetRail now; confirmation opens after it exits. If you finish later, use the desktop recovery shortcut. Setup remains interactive." }); }
                catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                { Set(_snapshot with { Message = "Could not open recovery. Use the WidgetRail Recovery shortcut on your desktop." }); }
                return ValueTask.FromResult(_snapshot);
            }
            if (command.Action == "download" && (_release is null || _asset is null || !_installation.CanInstall)) return ValueTask.FromResult(_snapshot);
            _operation?.Dispose();
            _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var cancellation = _operation.Token;
            Set(_snapshot with { State = command.Action == "check" ? "checking" : "downloading", ReviewToken = null,
                Message = command.Action == "check" ? "Checking releases…" : "Preparing verified update and recovery installers…", DownloadedBytes = 0, TotalBytes = null });
            _job = Task.Run(async () =>
            {
                try { if (command.Action == "check") await CheckAsync(cancellation); else await DownloadAsync(cancellation); }
                catch (OperationCanceledException) { lock (_gate) Set(_snapshot with { State = "cancelled", ReviewToken = null, Message = "Cancelled. No installation started." }); }
                catch (Exception error) when (error is DistributionOperationException or DistributionUsageException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception or NotSupportedException)
                { lock (_gate) Set(_snapshot with { State = "failed", ReviewToken = null, Message = SafeError(error) }); }
            });
            return ValueTask.FromResult(_snapshot);
        }
    }

    private async Task CheckAsync(CancellationToken token)
    {
        using var client = new GitHubReleaseClient(_http, includeInstallers: true);
        var releases = await client.ListAsync(Repository, 1, token);
        var preview = _installation.Version.Contains('-');
        var selected = releases.Where(r => preview || !r.Prerelease).Where(r => VersionCompare(r.Tag.TrimStart('v'), _installation.Version) > 0)
            .Select(r => (Release: r, Asset: r.Assets.SingleOrDefault(a => a.Name == InstallerName(r.Tag.TrimStart('v'), _installation.Edition))))
            .Where(pair => pair.Asset is not null).OrderByDescending(pair => pair.Release.Tag.TrimStart('v'), Comparer<string>.Create(VersionCompare)).FirstOrDefault();
        lock (_gate)
        {
            _release = selected.Release; _asset = selected.Asset;
            Set(_snapshot with { State = _release is null ? "current" : "available", AvailableVersion = _release?.Tag.TrimStart('v'), Notes = _release?.Notes ?? "",
                Message = _release is null ? "No newer matching release was found." : "Review the release notes. Downloading does not start installation." });
        }
    }

    private async Task DownloadAsync(CancellationToken token)
    {
        GitHubRelease release; GitHubAsset asset;
        lock (_gate) { release = _release!; asset = _asset!; }
        PlainPath(_cache); Directory.CreateDirectory(_cache);
        // Never prune somebody's recovery copy. A deliberate filesystem cleanup is
        // required after testing before unbounded recovery sets can accumulate.
        if (Directory.EnumerateDirectories(_cache).Where(p => !Path.GetFileName(p).StartsWith(".", StringComparison.Ordinal)).Take(3).Count() >= 3)
            throw new DistributionOperationException("Three recovery folders are retained. Remove an obsolete folder from WidgetRail-Recovery after confirming it is no longer needed, then retry.");
        using var client = new GitHubReleaseClient(_http, includeInstallers: true);
        var previous = await client.GetReleaseAsync(Repository, "v" + _installation.Version, token);
        var previousAsset = previous.Assets.SingleOrDefault(a => a.Name == InstallerName(_installation.Version, _installation.Edition))
            ?? throw new DistributionOperationException("The installed version's recovery installer is unavailable. No update will start.");
        var root = Path.Combine(_cache, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var newHash = await SaveInstallerAsync(client, release, asset, Path.Combine(root, "update.exe"), token);
        var oldHash = await SaveInstallerAsync(client, previous, previousAsset, Path.Combine(root, "previous.exe"), token);
        foreach (var name in new[] { "RecoveryEngine.ps1", "RecoveryProgress.ps1", "Recover.ps1" })
        {
            using var input = typeof(BridgeAppUpdates).Assembly.GetManifestResourceStream("WidgetRail.Recovery." + name)!;
            await using var output = new FileStream(Path.Combine(root, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, token);
        }
        await File.WriteAllTextAsync(Path.Combine(root, "plan.json"), JsonSerializer.Serialize(new
        { schema = 1, previousVersion = _installation.Version, updateVersion = release.Tag.TrimStart('v'), edition = _installation.Edition, previousRoot = _installation.Root,
            previousSha256 = oldHash, updateSha256 = newHash }), token);
        await File.WriteAllTextAsync(Path.Combine(root, "README.txt"),
            "WidgetRail update recovery\r\nUse the desktop WidgetRail Recovery shortcut after quitting WidgetRail.\r\n" +
            "This private folder contains installers and, after installation, a complete local profile backup. Do not share it.\r\n" +
            "Recovery restores the previous app and profile. The newer profile is kept as profile-after. Windows credentials and external widget data are not rolled back.\r\n", token);
        token.ThrowIfCancellationRequested();
        _shortcut(root);
        lock (_gate)
        {
            _prepared = root;
            Set(_snapshot with { State = "ready", ReviewToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                Message = "Both installers verified. Installation first backs up your profile. Recovery is available from the desktop even if WidgetRail cannot start." });
        }
    }

    private async Task<string> SaveInstallerAsync(GitHubReleaseClient client, GitHubRelease release, GitHubAsset asset, string target, CancellationToken token)
    {
        if (asset.Size <= 0 || asset.Size > 1024L * 1024 * 1024) throw new DistributionOperationException("Installer exceeds the 1 GiB download limit.");
        var source = GitHubPackageSource.Parse($"github:{Repository}@{release.Tag}/{asset.Name}", ".exe");
        var hash = await client.ResolveDigestAsync(source, release, token);
        using var downloader = new RemotePackageDownloader(_http, new() { MaximumBytes = 1024L * 1024 * 1024, OverallTimeout = TimeSpan.FromMinutes(15), TemporaryDirectoryRoot = Path.Combine(_cache, ".download") });
        await using var downloaded = await downloader.DownloadAsync(RemotePackageSource.Resolve(source.ToString(), ".exe")!, hash, token, ".exe", (bytes, total) =>
        { lock (_gate) Set(_snapshot with { DownloadedBytes = bytes, TotalBytes = total }); });
        downloaded.PackageStream.Position = 0;
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await downloaded.PackageStream.CopyToAsync(output, token);
        await output.FlushAsync(token);
        return Convert.ToHexString(hash);
    }

    internal static string InstallerName(string version, string edition) => $"WidgetRail{(edition == "developer" ? "-Developer" : "")}-{version}-win-x64-setup.exe";
    internal static int VersionCompare(string left, string right)
    {
        static (Version? Core, string[] Pre) Split(string value)
        { var parts = value.Split('+')[0].Split('-', 2); return (Version.TryParse(parts[0], out var core) ? core : null, parts.Length == 2 ? parts[1].Split('.') : []); }
        var a = Split(left); var b = Split(right);
        if (a.Core is null || b.Core is null) return -1;
        var difference = a.Core.CompareTo(b.Core); if (difference != 0) return difference;
        if (a.Pre.Length == 0 || b.Pre.Length == 0) return b.Pre.Length.CompareTo(a.Pre.Length);
        for (var i = 0; i < Math.Min(a.Pre.Length, b.Pre.Length); i++)
        {
            var an = long.TryParse(a.Pre[i], out var x); var bn = long.TryParse(b.Pre[i], out var y);
            difference = an && bn ? x.CompareTo(y) : an != bn ? an ? -1 : 1 : StringComparer.Ordinal.Compare(a.Pre[i], b.Pre[i]);
            if (difference != 0) return difference;
        }
        return a.Pre.Length.CompareTo(b.Pre.Length);
    }
    private void Set(AppUpdateSnapshot value) => _snapshot = value with { Revision = _snapshot.Revision + 1 };
    private static string SafeError(Exception error) => error is DistributionOperationException or DistributionUsageException
        ? error.Message[..Math.Min(500, error.Message.Length)] : "Could not prepare the update. Check free disk space and access to the recovery folder; no installation started.";
    private static void PlainPath(string path)
    { for (var item = new DirectoryInfo(path); item is not null; item = item.Parent) if (item.Exists && item.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Recovery path contains a link."); }
    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
    private static void Launch(string root)
    {
        var info = new ProcessStartInfo(PowerShell) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(root, "Recover.ps1"), "-Mode", "Install", "-WaitForAppExit" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Recovery could not start.");
    }
    private static void WriteShortcut(string root)
    {
        // Windows shell link, no shell command-string construction or cmd parsing.
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcut creation is unavailable.");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop)) throw new IOException("The desktop folder is unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic shortcut = shell.CreateShortcut(Path.Combine(desktop, "WidgetRail Recovery " + Path.GetFileName(root)[..8] + ".lnk"));
        try
        {
            shortcut.TargetPath = PowerShell;
            shortcut.Arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "Recover.ps1") + "\" -Mode Recover";
            shortcut.WorkingDirectory = root; shortcut.WindowStyle = 7; shortcut.Description = "Restore WidgetRail and its pre-update profile"; shortcut.Save();
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
    public async ValueTask DisposeAsync()
    {
        Task? job;
        lock (_gate) { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _operation?.Cancel(); job = _job; }
        if (job is not null) await job.ConfigureAwait(false);
        _operation?.Dispose(); _lifetime.Dispose();
    }
}
