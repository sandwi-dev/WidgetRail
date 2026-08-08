using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetStyling;
using CatalogService = GameBarAlternative.WidgetCatalog.WidgetCatalog;

namespace GameBarAlternative.GbarCli;

internal static class DevCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var parsed = new CommandArguments(
            args, "--host", "--configuration", "--build-timeout-seconds", "--debounce-ms");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: gbar dev <widget-directory|widget.csproj|file.gbarwidget> " +
                "[--host <OverlayHost.exe>] [--configuration <name>] " +
                "[--build-timeout-seconds <10-600>] [--debounce-ms <50-2000>]");

        var configuration = parsed.Option("--configuration") ?? "Debug";
        if (configuration.Length is < 1 or > 64 ||
            configuration.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
            throw new CliUsageException("--configuration must be a simple 1-64 character name.");
        var timeout = ParseBoundedInt(parsed.Option("--build-timeout-seconds"), 120, 10, 600,
            "--build-timeout-seconds");
        var debounce = ParseBoundedInt(parsed.Option("--debounce-ms"), 250, 50, 2_000,
            "--debounce-ms");
        var source = DevWidgetSource.Discover(parsed.Positionals[0]);
        var host = DevHostLocator.Resolve(parsed.Option("--host"), configuration);

        await output.WriteLineAsync($"Development widget: {source.DisplayPath}");
        await output.WriteLineAsync($"Overlay host: {host}");
        await output.WriteLineAsync("Unsigned development mode uses the normal community AppContainer worker boundary.");
        await output.WriteLineAsync("Press Ctrl+C to stop; the temporary catalog and worker processes will be removed.");

        await using var session = new DevSession(
            source, host, configuration, TimeSpan.FromSeconds(timeout),
            TimeSpan.FromMilliseconds(debounce), output, error);
        await session.RunAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static int ParseBoundedInt(string? value, int fallback, int minimum, int maximum, string name)
    {
        if (value is null) return fallback;
        if (!int.TryParse(value, out var parsed) || parsed < minimum || parsed > maximum)
            throw new CliUsageException($"{name} must be between {minimum} and {maximum}.");
        return parsed;
    }
}

internal enum DevWidgetSourceKind { Project, PackageDirectory, PackageArchive }

internal sealed record DevWidgetSource(
    DevWidgetSourceKind Kind,
    string DisplayPath,
    string Root,
    string? ProjectPath,
    string? ArchivePath)
{
    public static DevWidgetSource Discover(string requested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requested);
        var path = Path.GetFullPath(requested);
        if (File.Exists(path) &&
            Path.GetExtension(path).Equals(".gbarwidget", StringComparison.OrdinalIgnoreCase))
        {
            RejectReparse(path);
            return new(DevWidgetSourceKind.PackageArchive, path,
                Path.GetDirectoryName(path)!, null, path);
        }

        string root;
        string? selectedProject = null;
        if (File.Exists(path) && Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            RejectReparse(path);
            selectedProject = path;
            root = Path.GetDirectoryName(path)!;
        }
        else if (Directory.Exists(path))
        {
            RejectReparse(path);
            root = Path.TrimEndingDirectorySeparator(path);
        }
        else
        {
            throw new CliUsageException($"Development source does not exist: {path}");
        }

        var manifest = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifest))
            throw new CliUsageException($"Widget source is missing root-level manifest.json: {root}");
        RejectReparse(manifest);
        if (selectedProject is null)
        {
            var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            if (projects.Length > 1)
                throw new CliUsageException(
                    "Widget directory contains multiple projects; pass the intended .csproj explicitly.");
            selectedProject = projects.SingleOrDefault();
        }
        return selectedProject is null
            ? new(DevWidgetSourceKind.PackageDirectory, root, root, null, null)
            : new(DevWidgetSourceKind.Project, selectedProject, root, selectedProject, null);
    }

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new CliUsageException($"Development source cannot be a reparse point: {path}");
    }
}

internal static class DevHostLocator
{
    public static string Resolve(string? requested, string configuration)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return Validate(requested);
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                foreach (var candidateConfiguration in new[] { configuration, "Release", "Debug" }
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var candidate = Path.Combine(directory.FullName, "src", "OverlayHost", "out",
                        candidateConfiguration, "OverlayHost.exe");
                    if (File.Exists(candidate)) return Validate(candidate);
                }
            }
        }
        throw new CliUsageException(
            "Packaged OverlayHost.exe was not found. Build the overlay or pass --host <OverlayHost.exe>.");
    }

    private static string Validate(string requested)
    {
        var path = Path.GetFullPath(requested);
        if (!File.Exists(path) ||
            !Path.GetFileName(path).Equals("OverlayHost.exe", StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException($"Overlay host does not exist: {path}");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new CliUsageException("Overlay host cannot be a reparse point.");
        var root = Path.GetDirectoryName(path)!;
        foreach (var required in new[]
                 {
                     "widget-catalog.json",
                     Path.Combine("runtime", "Bridge", "WidgetBridge.exe"),
                     Path.Combine("runtime", "WidgetWorkerHost", "WidgetWorkerHost.exe"),
                 })
        {
            if (!File.Exists(Path.Combine(root, required)))
                throw new CliUsageException(
                    $"Overlay host is not a complete packaged build; missing {required}.");
        }
        return path;
    }
}

internal sealed class DevSession : IAsyncDisposable
{
    private readonly DevWidgetSource _source;
    private readonly string _host;
    private readonly string _configuration;
    private readonly TimeSpan _buildTimeout;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _readyTimeout;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly string _sessionRoot;
    private readonly Channel<byte> _changes = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private DevSourceWatcher? _watcher;
    private DevHostProcess? _hostProcess;
    private string? _activeGeneration;
    private DevWidgetIdentity? _activeIdentity;
    private int _generation;

    public DevSession(
        DevWidgetSource source,
        string host,
        string configuration,
        TimeSpan buildTimeout,
        TimeSpan debounce,
        TextWriter output,
        TextWriter error,
        TimeSpan? readyTimeout = null)
    {
        _source = source;
        _host = host;
        _configuration = configuration;
        _buildTimeout = buildTimeout;
        _debounce = debounce;
        _readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(10);
        if (_readyTimeout < TimeSpan.FromMilliseconds(100) || _readyTimeout > TimeSpan.FromSeconds(60))
            throw new ArgumentOutOfRangeException(nameof(readyTimeout));
        _output = output;
        _error = error;
        _sessionRoot = Path.Combine(Path.GetTempPath(), "GameBarAlternative", "gbar-dev",
            Guid.NewGuid().ToString("N"));
    }

    internal string SessionRoot => _sessionRoot;
    internal int? ActiveHostProcessId => _hostProcess is { HasExited: false } process ? process.Id : null;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_sessionRoot);
        _watcher = DevSourceWatcher.Create(_source, SignalChange);
        SignalChange();
        while (await _changes.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_changes.Reader.TryRead(out _)) { }
            await Task.Delay(_debounce, cancellationToken).ConfigureAwait(false);
            while (_changes.Reader.TryRead(out _)) { }
            try { _watcher?.Refresh(); }
            catch (Exception exception) when (exception is CliOperationException or IOException or UnauthorizedAccessException)
            {
                await _error.WriteLineAsync($"dev source change rejected: {SafeMessage(exception)}");
                continue;
            }
            await TryPublishAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _watcher?.Dispose();
        _changes.Writer.TryComplete();
        var stop = await StopHostAsync(_hostProcess).ConfigureAwait(false);
        _hostProcess?.Dispose();
        _hostProcess = null;
        var deleted = await DeleteTreeWithRetriesAsync(_sessionRoot).ConfigureAwait(false);
        if (!stop.Reclaimed || !deleted)
        {
            var details = string.Join("; ", new[]
            {
                stop.Reclaimed ? null : stop.Diagnostic,
                deleted ? null : $"temporary session directory remains at {_sessionRoot}",
            }.Where(item => item is not null));
            await _error.WriteLineAsync($"dev cleanup incomplete: {details}");
            throw new CliOperationException($"Development cleanup did not reclaim all resources: {details}");
        }
    }

    private void SignalChange() => _changes.Writer.TryWrite(0);

    private async Task TryPublishAsync(CancellationToken cancellationToken)
    {
        var generation = Path.Combine(_sessionRoot, $"generation-{++_generation:D6}");
        Directory.CreateDirectory(generation);
        try
        {
            var prepared = await DevGenerationBuilder.PrepareAsync(
                _source, generation, _configuration, _buildTimeout, _output, _error,
                cancellationToken).ConfigureAwait(false);
            var catalogRoot = Path.Combine(generation, "catalog");
            var catalog = new CatalogService(catalogRoot);
            var installed = await catalog.InstallAsync(prepared.PackagePath, cancellationToken)
                .ConfigureAwait(false);
            await catalog.SetEnabledAsync(installed.Id, true, cancellationToken).ConfigureAwait(false);

            var previousHost = _hostProcess;
            var previousGeneration = _activeGeneration;
            var previousIdentity = _activeIdentity;
            var nextIdentity = DevWidgetIdentity.FromManifest(prepared.Manifest);

            // A probe host has no hotkey/controller registrations. It must
            // authenticate exact bridge/catalog reconciliation before the
            // last-good interactive host is touched.
            var probe = await StartReadyHostAsync(
                catalogRoot, nextIdentity, generation, probeOnly: true, cancellationToken)
                .ConfigureAwait(false);
            var probeStop = await StopHostAsync(probe).ConfigureAwait(false);
            probe.Dispose();
            if (!probeStop.Reclaimed)
                throw new CliOperationException(
                    $"Development readiness probe could not be reclaimed: {probeStop.Diagnostic}");

            var previousStop = await StopHostAsync(previousHost).ConfigureAwait(false);
            if (!previousStop.Reclaimed)
                throw new CliOperationException(
                    $"Last-good overlay could not be stopped safely: {previousStop.Diagnostic}");
            previousHost?.Dispose();
            _hostProcess = null;
            DevHostProcess nextHost;
            try
            {
                nextHost = await StartReadyHostAsync(
                    catalogRoot, nextIdentity, generation, probeOnly: false, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                if (previousGeneration is not null && previousIdentity is not null)
                {
                    try
                    {
                        _hostProcess = await StartReadyHostAsync(
                            Path.Combine(previousGeneration, "catalog"), previousIdentity,
                            previousGeneration, probeOnly: false, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception restoreException) when (restoreException is CliOperationException or
                                                               System.ComponentModel.Win32Exception)
                    {
                        await _error.WriteLineAsync(
                            $"Last-good overlay restart also failed: {SafeMessage(restoreException)}");
                    }
                }
                throw;
            }
            _hostProcess = nextHost;
            _activeGeneration = generation;
            _activeIdentity = nextIdentity;
            await _output.WriteLineAsync(
                $"Ready: {prepared.Manifest.Id} {prepared.Manifest.Version} " +
                $"(generation {_generation}, PID {nextHost.Id}).");
            if (previousGeneration is not null)
            {
                if (!await DeleteTreeWithRetriesAsync(previousGeneration).ConfigureAwait(false))
                    await _error.WriteLineAsync(
                        $"dev cleanup warning: prior generation remains at {previousGeneration}");
            }
            _watcher?.Refresh();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is CliOperationException or WidgetPackageException or
                                           IOException or UnauthorizedAccessException or JsonException or
                                           System.ComponentModel.Win32Exception)
        {
            await _error.WriteLineAsync($"dev build rejected: {SafeMessage(exception)}");
            await _error.WriteLineAsync(_hostProcess is { HasExited: false }
                ? "Retained the last-good running widget. Waiting for another declared source change."
                : "No last-good widget is running. Waiting for another declared source change.");
            if (!string.Equals(generation, _activeGeneration, StringComparison.OrdinalIgnoreCase) &&
                !await DeleteTreeWithRetriesAsync(generation).ConfigureAwait(false))
                await _error.WriteLineAsync($"dev cleanup warning: rejected generation remains at {generation}");
        }
    }

    private DevHostProcess StartHost(string catalogRoot, DevReadyHandshake handshake, bool probeOnly)
    {
        var start = new ProcessStartInfo(_host)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(_host)!,
        };
        start.ArgumentList.Add(probeOnly ? "--hidden" : "--show");
        start.ArgumentList.Add("--development-catalog-root");
        start.ArgumentList.Add(catalogRoot);
        start.ArgumentList.Add("--development-ready-path");
        start.ArgumentList.Add(handshake.Path);
        start.ArgumentList.Add("--development-ready-nonce");
        start.ArgumentList.Add(handshake.Nonce);
        start.ArgumentList.Add("--development-widget-id");
        start.ArgumentList.Add(handshake.Identity.Id);
        start.ArgumentList.Add("--development-widget-instance");
        start.ArgumentList.Add(handshake.Identity.InstanceId);
        if (probeOnly) start.ArgumentList.Add("--development-probe-only");
        return DevHostProcess.Start(start);
    }

    private async Task<DevHostProcess> StartReadyHostAsync(
        string catalogRoot,
        DevWidgetIdentity identity,
        string handshakeRoot,
        bool probeOnly,
        CancellationToken cancellationToken)
    {
        var handshake = DevReadyHandshake.Create(handshakeRoot, catalogRoot, identity);
        var process = StartHost(catalogRoot, handshake, probeOnly);
        try
        {
            await handshake.WaitAsync(process.Process, _readyTimeout, cancellationToken)
                .ConfigureAwait(false);
            return process;
        }
        catch
        {
            var stop = await StopHostAsync(process).ConfigureAwait(false);
            process.Dispose();
            if (!stop.Reclaimed)
                await _error.WriteLineAsync(
                    $"dev cleanup warning: failed readiness host remains: {stop.Diagnostic}");
            throw;
        }
    }

    internal static Task<DevProcessStopResult> StopHostAsync(DevHostProcess? process)
    {
        return process is null
            ? Task.FromResult(new DevProcessStopResult(true, null))
            : process.StopAsync(TimeSpan.FromSeconds(5));
    }

    internal static async Task<bool> DeleteTreeWithRetriesAsync(string path)
    {
        // Windows can retain a process working-directory handle briefly after
        // Kill(entireProcessTree) and WaitForExitAsync have completed. Keep
        // cleanup bounded, but allow the handle-close notification enough time
        // to reach the filesystem before declaring a leaked dev directory.
        const int maximumAttempts = 10;
        for (var attempt = 0; attempt < maximumAttempts && Directory.Exists(path); attempt++)
        {
            try { Directory.Delete(path, recursive: true); }
            catch (IOException) when (attempt + 1 < maximumAttempts)
            {
                await Task.Delay(100 * (attempt + 1)).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt + 1 < maximumAttempts)
            {
                await Task.Delay(100 * (attempt + 1)).ConfigureAwait(false);
            }
        }
        return !Directory.Exists(path);
    }

    internal static string SafeMessage(Exception exception)
    {
        var message = exception is WidgetPackageException package
            ? $"{package.Code}: {package.Message}"
            : exception.Message;
        var clean = new string(message.Select(ch => ch is '\r' or '\n' or '\t' ? ' ' :
            char.IsControl(ch) ? '?' : ch).ToArray());
        return clean.Length <= 1_000 ? clean : clean[..1_000] + "…";
    }
}

internal sealed record DevProcessStopResult(bool Reclaimed, string? Diagnostic);

internal sealed class DevHostProcess : IDisposable
{
    private const uint CreateSuspended = 0x00000004;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobInfoClassBasicAccounting = 1;
    private const int JobInfoClassExtendedLimits = 9;
    private readonly SafeFileHandle _job;
    private bool _disposed;

    private DevHostProcess(Process process, SafeFileHandle job)
    {
        Process = process;
        _job = job;
    }

    internal Process Process { get; }
    internal int Id => Process.Id;
    internal bool HasExited
    {
        get
        {
            try { return Process.HasExited; }
            catch (InvalidOperationException) { return true; }
        }
    }

    internal static DevHostProcess Start(ProcessStartInfo start)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (start.UseShellExecute)
            throw new CliOperationException("Development hosts require direct process creation.");
        var application = Path.GetFullPath(start.FileName);
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not create the development host Job Object.");

        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose,
            },
        };
        if (!SetInformationJobObject(
                job, JobInfoClassExtendedLimits, ref limits,
                (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error,
                "Could not configure development host process-tree reclamation.");
        }

        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
        var commandLine = new StringBuilder(BuildCommandLine(application, start.ArgumentList));
        ProcessInformation created = default;
        var assigned = false;
        try
        {
            if (!CreateProcessW(
                    application, commandLine, IntPtr.Zero, IntPtr.Zero,
                    inheritHandles: false, CreateSuspended, IntPtr.Zero,
                    string.IsNullOrWhiteSpace(start.WorkingDirectory)
                        ? Path.GetDirectoryName(application)
                        : Path.GetFullPath(start.WorkingDirectory),
                    ref startup, out created))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "OverlayHost could not be started.");
            if (!AssignProcessToJobObject(job, created.Process))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "OverlayHost could not be assigned to its process-tree Job Object.");
            assigned = true;
            if (ResumeThread(created.Thread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "OverlayHost could not be resumed after Job Object assignment.");
            var process = Process.GetProcessById(checked((int)created.ProcessId));
            return new DevHostProcess(process, job);
        }
        catch
        {
            if (assigned) _ = TerminateJobObject(job, 1);
            else if (created.Process != IntPtr.Zero) _ = TerminateProcess(created.Process, 1);
            job.Dispose();
            throw;
        }
        finally
        {
            if (created.Thread != IntPtr.Zero) _ = CloseHandle(created.Thread);
            if (created.Process != IntPtr.Zero) _ = CloseHandle(created.Process);
        }
    }

    internal async Task<DevProcessStopResult> StopAsync(TimeSpan timeout)
    {
        if (_disposed) return new(false, $"Job Object for PID {Id} was already disposed before verification");
        var terminateError = 0;
        if (!TerminateJobObject(_job, 1)) terminateError = Marshal.GetLastWin32Error();
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (!QueryInformationJobObject(
                    _job, JobInfoClassBasicAccounting, out JobObjectBasicAccountingInformation accounting,
                    (uint)Marshal.SizeOf<JobObjectBasicAccountingInformation>(), IntPtr.Zero))
                return new(false,
                    $"Job Object for PID {Id} could not report descendant reclamation " +
                    $"(Win32 error {Marshal.GetLastWin32Error()})");
            if (accounting.ActiveProcesses == 0) return new(true, null);
            if (DateTime.UtcNow >= deadline)
            {
                var termination = terminateError == 0 ? "termination was requested" :
                    $"termination failed with Win32 error {terminateError}";
                return new(false,
                    $"Job Object for PID {Id} retained {accounting.ActiveProcesses} active process(es); {termination}");
            }
            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _job.Dispose();
        Process.Dispose();
    }

    private static string BuildCommandLine(string application, IEnumerable<string> arguments) =>
        string.Join(' ', new[] { QuoteWindowsArgument(application) }.Concat(arguments.Select(QuoteWindowsArgument)));

    private static string QuoteWindowsArgument(string value)
    {
        if (value.Length != 0 && value.All(ch => ch is not ' ' and not '\t' and not '\n' and not '\v' and not '"'))
            return value;
        var builder = new StringBuilder(value.Length + 2).Append('"');
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }
            if (character == '"')
            {
                builder.Append('\\', slashes * 2 + 1).Append('"');
                slashes = 0;
                continue;
            }
            builder.Append('\\', slashes).Append(character);
            slashes = 0;
        }
        return builder.Append('\\', slashes * 2).Append('"').ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        internal uint Size;
        private IntPtr Reserved;
        private IntPtr Desktop;
        private IntPtr Title;
        private uint X;
        private uint Y;
        private uint XSize;
        private uint YSize;
        private uint XCountChars;
        private uint YCountChars;
        private uint FillAttribute;
        private uint Flags;
        private ushort ShowWindow;
        private ushort Reserved2Size;
        private IntPtr Reserved2;
        private IntPtr StandardInput;
        private IntPtr StandardOutput;
        private IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        internal IntPtr Process;
        internal IntPtr Thread;
        internal uint ProcessId;
        private uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        private long PerProcessUserTimeLimit;
        private long PerJobUserTimeLimit;
        internal uint LimitFlags;
        private UIntPtr MinimumWorkingSetSize;
        private UIntPtr MaximumWorkingSetSize;
        private uint ActiveProcessLimit;
        private UIntPtr Affinity;
        private uint PriorityClass;
        private uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        private ulong ReadOperationCount;
        private ulong WriteOperationCount;
        private ulong OtherOperationCount;
        private ulong ReadTransferCount;
        private ulong WriteTransferCount;
        private ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        internal JobObjectBasicLimitInformation BasicLimitInformation;
        private IoCounters IoInfo;
        private UIntPtr ProcessMemoryLimit;
        private UIntPtr JobMemoryLimit;
        private UIntPtr PeakProcessMemoryUsed;
        private UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicAccountingInformation
    {
        private long TotalUserTime;
        private long TotalKernelTime;
        private long ThisPeriodTotalUserTime;
        private long ThisPeriodTotalKernelTime;
        private uint TotalPageFaultCount;
        private uint TotalProcesses;
        internal uint ActiveProcesses;
        private uint TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job, int informationClass,
        ref JobObjectExtendedLimitInformation information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        SafeFileHandle job, int informationClass,
        out JobObjectBasicAccountingInformation information, uint informationLength,
        IntPtr returnLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags,
        IntPtr environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

internal sealed record DevWidgetIdentity(string Id, string InstanceId)
{
    public static DevWidgetIdentity FromManifest(WidgetManifest manifest)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{manifest.Id}@{manifest.Version}"));
        return new(manifest.Id,
            $"installed.{Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant()}");
    }
}

internal sealed record DevReadyHandshake(
    string Path,
    string Nonce,
    string ExpectedPayload,
    DevWidgetIdentity Identity)
{
    public static DevReadyHandshake Create(
        string root,
        string catalogRoot,
        DevWidgetIdentity identity)
    {
        var directory = System.IO.Path.Combine(root, "ready");
        Directory.CreateDirectory(directory);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var path = System.IO.Path.Combine(directory, $"{nonce}.ready");
        var normalizedCatalog = System.IO.Path.GetFullPath(catalogRoot);
        var payload = $"gbar-dev-ready-v1\n{nonce}\n{normalizedCatalog}\n{identity.Id}\n{identity.InstanceId}\n";
        return new(path, nonce, payload, identity);
    }

    public async Task WaitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new CliOperationException(
                    $"OverlayHost exited before authenticated readiness with code {process.ExitCode}.");
            if (File.Exists(Path))
            {
                string payload;
                try
                {
                    var info = new FileInfo(Path);
                    if (info.Length > 4_096)
                        throw new CliOperationException("OverlayHost readiness payload exceeded its bound.");
                    payload = await File.ReadAllTextAsync(Path, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (!string.Equals(payload, ExpectedPayload, StringComparison.Ordinal))
                    throw new CliOperationException(
                        "OverlayHost readiness payload did not authenticate the exact development catalog generation.");
                File.Delete(Path);
                return;
            }
            if (DateTime.UtcNow >= deadline)
                throw new CliOperationException(
                    $"OverlayHost did not authenticate bridge/catalog readiness within {timeout.TotalSeconds:0} seconds.");
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed record PreparedDevGeneration(string PackagePath, WidgetManifest Manifest);

internal static class DevGenerationBuilder
{
    public static async Task<PreparedDevGeneration> PrepareAsync(
        DevWidgetSource source,
        string generationRoot,
        string configuration,
        TimeSpan buildTimeout,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (source.Kind == DevWidgetSourceKind.PackageArchive)
        {
            var inspection = await new CatalogService(Path.Combine(generationRoot, "validation"))
                .CreateInstaller().ValidateAsync(source.ArchivePath!, cancellationToken).ConfigureAwait(false);
            return new(source.ArchivePath!, inspection.Manifest);
        }

        var manifestPath = Path.Combine(source.Root, "manifest.json");
        var manifest = await ValidateSourceAsync(manifestPath, source.Root, error, cancellationToken)
            .ConfigureAwait(false);
        if (source.Kind == DevWidgetSourceKind.PackageDirectory)
        {
            var package = Path.Combine(generationRoot, "widget.gbarwidget");
            var packed = await WidgetPackagePacker.PackAsync(source.Root, package).ConfigureAwait(false);
            return new(packed.PackagePath, packed.Inspection.Manifest);
        }

        var packageRoot = Path.Combine(generationRoot, "package");
        var entrypoint = Path.GetFullPath(
            manifest.Entrypoint.Assembly.Replace('/', Path.DirectorySeparatorChar), packageRoot);
        if (!IsWithin(packageRoot, entrypoint))
            throw new CliOperationException("Manifest entrypoint escapes the package root.");
        var outputDirectory = Path.GetDirectoryName(entrypoint)!;
        Directory.CreateDirectory(outputDirectory);
        await RunBuildAsync(source.ProjectPath!, configuration, outputDirectory, buildTimeout,
            output, error, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(entrypoint))
            throw new CliOperationException(
                $"Build succeeded but did not produce the declared entrypoint '{manifest.Entrypoint.Assembly}'. " +
                "Set AssemblyName to match the manifest or correct entrypoint.assembly.");
        await File.WriteAllBytesAsync(Path.Combine(packageRoot, "manifest.json"),
            await File.ReadAllBytesAsync(manifestPath, cancellationToken), cancellationToken).ConfigureAwait(false);
        await CopyStylesAsync(source.Root, packageRoot, cancellationToken).ConfigureAwait(false);
        var packagePath = Path.Combine(generationRoot, "widget.gbarwidget");
        var packedProject = await WidgetPackagePacker.PackAsync(packageRoot, packagePath).ConfigureAwait(false);
        return new(packedProject.PackagePath, packedProject.Inspection.Manifest);
    }

    private static async Task<WidgetManifest> ValidateSourceAsync(
        string manifestPath,
        string root,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        WidgetManifest manifest;
        try { manifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(manifestPath, cancellationToken)); }
        catch (JsonException exception)
        {
            throw new CliOperationException(
                $"manifest.json({(exception.LineNumber ?? 0) + 1},{(exception.BytePositionInLine ?? 0) + 1}): " +
                $"invalid_json: {exception.Message}", exception);
        }
        var manifestIssues = WidgetManifestValidator.Validate(manifest);
        if (manifestIssues.Count != 0)
            throw new CliOperationException(
                $"manifest.json: {manifestIssues[0].Code}: {manifestIssues[0].Path}: {manifestIssues[0].Message}");
        var style = Path.Combine(root, "styles", "default.gbss");
        if (File.Exists(style))
        {
            var diagnostics = GbssValidator.Validate(style,
                await File.ReadAllTextAsync(style, cancellationToken).ConfigureAwait(false));
            foreach (var warning in diagnostics.Where(item => item.Severity == GbssDiagnosticSeverity.Warning))
                await error.WriteLineAsync(warning.ToString());
            var firstError = diagnostics.FirstOrDefault(item => item.Severity == GbssDiagnosticSeverity.Error);
            if (firstError is not null) throw new CliOperationException(firstError.ToString());
        }
        return manifest;
    }

    private static async Task RunBuildAsync(
        string project,
        string configuration,
        string outputDirectory,
        TimeSpan timeout,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var start = CreateBuildStartInfo(project, configuration, outputDirectory);
        using var process = Process.Start(start)
            ?? throw new CliOperationException("dotnet build could not be started.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var stdout = PumpBuildOutputAsync(process.StandardOutput, output, deadline.Token);
        var stderr = PumpBuildOutputAsync(process.StandardError, error, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await ObserveStoppedProcessAsync(process, stdout, stderr).ConfigureAwait(false);
            throw new CliOperationException($"dotnet build exceeded the {timeout.TotalSeconds:0}-second timeout.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await ObserveStoppedProcessAsync(process, stdout, stderr).ConfigureAwait(false);
            throw;
        }
        if (process.ExitCode != 0)
            throw new CliOperationException($"dotnet build failed with exit code {process.ExitCode}.");
    }

    internal static ProcessStartInfo CreateBuildStartInfo(
        string project,
        string configuration,
        string outputDirectory)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(project)!,
        };
        foreach (var argument in new[]
                 {
                     "build", project, "--configuration", configuration, "--nologo", "--output", outputDirectory,
                     // A dev generation is an isolated, bounded build. Persistent
                     // MSBuild/Roslyn servers can outlive it while retaining handles
                     // to the watched source tree, racing source cleanup and leaking
                     // dotnet/VBCSCompiler processes across rebuilds.
                     "--disable-build-servers",
                     "--property:UseSharedCompilation=false",
                     "--property:BuildInParallel=false",
                     "--property:MSBuildNodeReuse=false",
                 }) start.ArgumentList.Add(argument);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        return start;
    }

    private static async Task PumpBuildOutputAsync(
        StreamReader reader,
        TextWriter destination,
        CancellationToken cancellationToken)
    {
        var lines = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (lines++ >= 512) continue;
            var clean = DevSession.SafeMessage(new Exception(line));
            await destination.WriteLineAsync(clean).ConfigureAwait(false);
        }
        if (lines > 512)
            await destination.WriteLineAsync($"… {lines - 512} additional build lines suppressed.")
                .ConfigureAwait(false);
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static async Task ObserveStoppedProcessAsync(Process process, params Task[] pumps)
    {
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException) { }
        try { await Task.WhenAll(pumps).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private static async Task CopyStylesAsync(
        string sourceRoot,
        string packageRoot,
        CancellationToken cancellationToken)
    {
        var styles = Path.Combine(sourceRoot, "styles");
        if (!Directory.Exists(styles)) return;
        var files = DevSourceWatcher.EnumerateBounded(styles, "*.gbss", 256).ToArray();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(styles, file);
            var destination = Path.Combine(packageRoot, "styles", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !Path.IsPathRooted(relative) &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}

internal sealed class DevSourceWatcher : IDisposable
{
    private readonly DevWidgetSource _source;
    private readonly Action _changed;
    private readonly List<FileSystemWatcher> _watchers = [];
    private HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    private DevSourceWatcher(DevWidgetSource source, Action changed)
    {
        _source = source;
        _changed = changed;
        Refresh();
    }

    public IReadOnlyCollection<string> Files => _files;
    internal IReadOnlyCollection<string> WatchedDirectories =>
        _watchers.Select(watcher => watcher.Path).ToArray();

    public static DevSourceWatcher Create(DevWidgetSource source, Action changed) => new(source, changed);

    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var next = Capture(_source);
        foreach (var watcher in _watchers) StopWatcher(watcher);
        _watchers.Clear();
        _files = next;
        var directories = CaptureWatchDirectories(_source, _files);
        foreach (var directory in directories)
        {
            var names = _files.Where(file => string.Equals(
                    Path.GetDirectoryName(file), directory, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allPackageFiles = _source.Kind == DevWidgetSourceKind.PackageDirectory;
            var acceptsProjectInput = _source.Kind == DevWidgetSourceKind.Project &&
                IsWithinDirectory(_source.Root, directory) &&
                !IsExcludedProjectDirectory(_source.Root, directory);
            var acceptsNewStyle = IsWithinDirectory(Path.Combine(_source.Root, "styles"), directory);
            var childDirectories = Directory.EnumerateDirectories(directory)
                .Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var watcher = new FileSystemWatcher(directory, "*")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
                               NotifyFilters.Size | NotifyFilters.CreationTime,
                InternalBufferSize = 4 * 1024,
            };
            bool Accepts(string? name) => name is not null &&
                (allPackageFiles || names.Contains(name) || childDirectories.Contains(name) ||
                  acceptsProjectInput ||
                  (acceptsNewStyle && Path.GetExtension(name).Equals(".gbss", StringComparison.OrdinalIgnoreCase)));
            FileSystemEventHandler change = (_, args) =>
            {
                if (Accepts(args.Name) || (acceptsProjectInput && Directory.Exists(args.FullPath))) _changed();
            };
            RenamedEventHandler rename = (_, args) =>
            {
                if (Accepts(args.Name) || Accepts(args.OldName) ||
                    (acceptsProjectInput && Directory.Exists(args.FullPath))) _changed();
            };
            watcher.Changed += change;
            watcher.Created += change;
            watcher.Deleted += change;
            watcher.Renamed += rename;
            watcher.Error += (_, _) => _changed();
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var watcher in _watchers) StopWatcher(watcher);
        _watchers.Clear();
    }

    private static void StopWatcher(FileSystemWatcher watcher)
    {
        // Disable first so FileSystemWatcher's Windows directory handle and
        // outstanding ReadDirectoryChangesW request are closed synchronously.
        // Dispose alone can race a recursive source-tree cleanup while the
        // final native change notification is still being retired.
        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
    }

    internal static HashSet<string> Capture(DevWidgetSource source)
    {
        if (source.Kind == DevWidgetSourceKind.PackageArchive)
            return new HashSet<string>([source.ArchivePath!], StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(source.Root, "manifest.json"),
        };
        if (source.Kind == DevWidgetSourceKind.PackageDirectory)
        {
            foreach (var directory in EnumerateDirectoriesBounded(source.Root, 512))
            {
                foreach (var file in Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase))
                {
                    RejectReparse(file);
                    if (files.Count >= new WidgetCatalogOptions().MaximumArchiveEntries)
                        throw new CliOperationException(
                            "Package source exceeds the development package file-count limit.");
                    files.Add(Path.GetFullPath(file));
                }
            }
            return files;
        }
        if (source.ProjectPath is not null)
        {
            // MSBuild projects can consume arbitrary Content/None/Resource,
            // imported local props/targets, generated native inputs, and files
            // selected by custom targets. Conservatively watch the bounded
            // project tree rather than guessing extensions from the SDK.
            foreach (var file in EnumerateBounded(
                         source.Root, "*", 4_096, "bin", "obj", ".git", ".vs"))
                files.Add(file);
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets" })
            {
                for (var directory = new DirectoryInfo(source.Root); directory is not null; directory = directory.Parent)
                {
                    var candidate = Path.Combine(directory.FullName, name);
                    if (File.Exists(candidate)) { files.Add(candidate); break; }
                }
            }
        }
        foreach (var file in EnumerateBounded(Path.Combine(source.Root, "styles"), "*.gbss", 256))
            files.Add(file);
        return files;
    }

    private static IReadOnlyList<string> CaptureWatchDirectories(
        DevWidgetSource source,
        IReadOnlyCollection<string> files)
    {
        if (source.Kind == DevWidgetSourceKind.PackageArchive)
            return [Path.GetDirectoryName(source.ArchivePath!)!];
        if (source.Kind == DevWidgetSourceKind.PackageDirectory)
            return EnumerateDirectoriesBounded(source.Root, 512).ToArray();

        var directories = EnumerateDirectoriesBounded(
                source.Root, 512, "bin", "obj", ".git", ".vs")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in files.Select(Path.GetDirectoryName).Where(item => item is not null))
            directories.Add(directory!);
        return directories.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsWithinDirectory(string root, string candidate)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        return fullCandidate.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedProjectDirectory(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                             segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                             segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                             segment.Equals(".vs", StringComparison.OrdinalIgnoreCase));
    }

    internal static IEnumerable<string> EnumerateBounded(
        string root,
        string pattern,
        int maximum,
        params string[] excludedDirectories)
    {
        if (!Directory.Exists(root)) yield break;
        var excluded = excludedDirectories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        var count = 0;
        while (pending.Count != 0)
        {
            var directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new CliOperationException($"Source tree contains a reparse-point directory: {directory}");
            foreach (var child in Directory.EnumerateDirectories(directory).OrderDescending())
            {
                if (!excluded.Contains(Path.GetFileName(child))) pending.Push(child);
            }
            foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new CliOperationException($"Source tree contains a reparse-point file: {file}");
                if (++count > maximum)
                    throw new CliOperationException($"Declared source set exceeds the {maximum}-file development limit.");
                yield return Path.GetFullPath(file);
            }
        }
    }

    internal static IEnumerable<string> EnumerateDirectoriesBounded(
        string root,
        int maximum,
        params string[] excludedDirectories)
    {
        if (!Directory.Exists(root)) yield break;
        var excluded = excludedDirectories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        var count = 0;
        while (pending.Count != 0)
        {
            var directory = pending.Pop();
            RejectReparse(directory);
            if (++count > maximum)
                throw new CliOperationException(
                    $"Development source exceeds the {maximum}-directory watch limit.");
            yield return directory;
            foreach (var child in Directory.EnumerateDirectories(directory).OrderDescending())
            {
                if (!excluded.Contains(Path.GetFileName(child))) pending.Push(child);
            }
        }
    }

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new CliOperationException($"Source tree contains a reparse point: {path}");
    }
}
