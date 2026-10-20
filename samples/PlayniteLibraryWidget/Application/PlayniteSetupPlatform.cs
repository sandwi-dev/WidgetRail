using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteSetupProcess(int Id, string Executable, string? CommandLine, bool Fullscreen);
internal interface IPlayniteSetupPlatform
{
    bool HasRunningProcess();
    ValueTask<IReadOnlyList<PlayniteSetupProcess>> ProcessesAsync(CancellationToken token);
    IEnumerable<string> InstallationFolders();
    ValueTask StopAsync(PlayniteSetupInstallation installation, CancellationToken token);
    ValueTask StartAsync(PlayniteSetupInstallation installation, CancellationToken token);
}

internal sealed partial class PlayniteSetupPlatform : IPlayniteSetupPlatform
{
    public bool HasRunningProcess()
    {
        foreach (var name in new[] { "Playnite.DesktopApp", "Playnite.FullscreenApp" })
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length != 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }
    public async ValueTask<IReadOnlyList<PlayniteSetupProcess>> ProcessesAsync(CancellationToken token) =>
        await Task.Run(() => ReadProcesses(), token).WaitAsync(token).ConfigureAwait(false);

    private static IReadOnlyList<PlayniteSetupProcess> ReadProcesses()
    {
        using var current = Process.GetCurrentProcess();
        using var searcher = new ManagementObjectSearcher("root\\cimv2",
            "SELECT ProcessId, ExecutablePath, CommandLine, SessionId FROM Win32_Process WHERE Name='Playnite.DesktopApp.exe' OR Name='Playnite.FullscreenApp.exe'");
        searcher.Options.Timeout = TimeSpan.FromSeconds(5);
        using var rows = searcher.Get();
        var result = new List<PlayniteSetupProcess>();
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                if (Convert.ToInt32(row["SessionId"]) != current.SessionId || row["ExecutablePath"] is not string path) continue;
                result.Add(new(Convert.ToInt32(row["ProcessId"]), path, row["CommandLine"] as string,
                    string.Equals(Path.GetFileName(path), "Playnite.FullscreenApp.exe", StringComparison.OrdinalIgnoreCase)));
            }
        }
        return result;
    }

    public IEnumerable<string> InstallationFolders()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playnite"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Playnite"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Playnite"),
        };
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var protocol = root.OpenSubKey(@"Software\Classes\playnite\shell\open\command");
            if (protocol?.GetValue(null) is string command && SplitArguments(command).FirstOrDefault() is { } executable)
                if (Path.GetDirectoryName(executable) is { } directory) candidates.Add(directory);
            using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Playnite_is1");
            if (uninstall?.GetValue("InstallLocation") is string location) candidates.Add(location);
        }
        return candidates;
    }

    public async ValueTask StopAsync(PlayniteSetupInstallation installation, CancellationToken token)
    {
        var processes = await ProcessesAsync(token).ConfigureAwait(false);
        if (processes.Any(process => !Matches(process, installation)))
            throw new PlayniteSetupException("Another Playnite installation is running. Close it before continuing.");
        foreach (var entry in processes)
        {
            Process process;
            try { process = Process.GetProcessById(entry.Id); }
            catch (ArgumentException) { continue; } // Already exited since discovery.
            using var processLifetime = process;
            // Playnite's supported shutdown command saves settings and stops plugins;
            // never kill it or rely on CloseMainWindow, which may only minimize to tray.
            using var request = Process.Start(StartInfo(entry.Executable, installation.DataDirectory, "--shutdown"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new PlayniteSetupException("Playnite is still running. Finish any dialog or game operation, close Playnite, then retry."); }
        }
        if ((await ProcessesAsync(token).ConfigureAwait(false)).Count != 0)
            throw new PlayniteSetupException("Playnite restarted before setup could continue. Close Playnite and retry.");
    }

    public async ValueTask StartAsync(PlayniteSetupInstallation installation, CancellationToken token)
    {
        var running = await ProcessesAsync(token).ConfigureAwait(false);
        if (running.Any(process => !Matches(process, installation)))
            throw new PlayniteSetupException("Another Playnite profile is running. Close it before connecting this profile.");
        if (running.Count != 0) return;
        using var process = Process.Start(LaunchInfo(installation));
        if (process is null) throw new PlayniteSetupException("Playnite could not be started. Start it manually, then retry.");
    }

    internal static ProcessStartInfo LaunchInfo(PlayniteSetupInstallation installation) => StartInfo(
        Path.Combine(installation.ProgramDirectory, "Playnite.DesktopApp.exe"),
        installation.DataDirectory, "--startdesktop", "--startclosedtotray", "--hidesplashscreen");

    private static ProcessStartInfo StartInfo(string executable, string dataDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.ArgumentList.Add("--userdatadir");
        info.ArgumentList.Add(dataDirectory);
        return info;
    }

    internal static bool Matches(PlayniteSetupProcess process, PlayniteSetupInstallation installation) =>
        string.Equals(Path.GetDirectoryName(process.Executable), installation.ProgramDirectory, StringComparison.OrdinalIgnoreCase) &&
        process.CommandLine is not null && string.Equals(DataDirectory(installation.ProgramDirectory, process.CommandLine),
            installation.DataDirectory, StringComparison.OrdinalIgnoreCase);

    internal static string DataDirectory(string programDirectory, string? commandLine)
    {
        var arguments = commandLine is null ? [] : SplitArguments(commandLine);
        for (var index = 1; index < arguments.Length; index++)
        {
            if (arguments[index].Equals("--userdatadir", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= arguments.Length) throw new PlayniteSetupException("Playnite's custom data directory could not be read.");
                return PlayniteSetupFiles.LocalPath(arguments[index + 1]);
            }
            if (arguments[index].StartsWith("--userdatadir=", StringComparison.OrdinalIgnoreCase))
                return PlayniteSetupFiles.LocalPath(arguments[index][14..]);
        }
        return File.Exists(Path.Combine(programDirectory, "unins000.exe"))
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playnite")
            : programDirectory;
    }

    internal static string[] SplitArguments(string commandLine)
    {
        var pointer = CommandLineToArgvW(commandLine, out var count);
        if (pointer == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            var result = new string[count];
            for (var index = 0; index < count; index++) result[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size))!;
            return result;
        }
        finally { LocalFree(pointer); }
    }

    [LibraryImport("shell32.dll", EntryPoint = "CommandLineToArgvW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);
    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr LocalFree(IntPtr pointer);
}
