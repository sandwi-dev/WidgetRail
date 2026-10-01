using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace WidgetRail.WidgetPresentationSession;

public sealed record BridgeProcessOptions(string InstallationRoot, string SettingsRoot, string InstalledCatalogRoot)
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Opt in only when the frontend owns a permission-checked capture renderer.</summary>
    public bool WindowPreviews { get; init; }
    /// <summary>Opt in only when the frontend exchanges and applies controller-control preferences.</summary>
    public bool ExclusiveControllerControl { get; init; }
    /// <summary>Optional host diagnostics for foreground delegation, without widget payloads.</summary>
    public Action<string>? ForegroundDelegationDiagnostic { get; init; }
    /// <summary>Optional external process-tree owner. Null preserves ordinary process startup.</summary>
    public string? ProcessOwnerJobName { get; init; }

    internal ProcessStartInfo CreateStartInfo(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        foreach (var path in new[] { InstallationRoot, SettingsRoot, InstalledCatalogRoot })
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException("Bridge paths must be explicit absolute paths.");
        if (ConnectTimeout < TimeSpan.FromMilliseconds(100) || ConnectTimeout > TimeSpan.FromSeconds(60))
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout));
        var root = Path.GetFullPath(InstallationRoot);
        var info = new ProcessStartInfo(Path.Combine(root, "runtime", "Bridge", "WidgetBridge.exe"))
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        var privateRuntime = Path.Combine(root, "dotnet");
        if (Directory.Exists(privateRuntime))
        {
            if (!File.Exists(Path.Combine(privateRuntime, "dotnet.exe")))
                throw new FileNotFoundException("The bundled .NET runtime is incomplete.", Path.Combine(privateRuntime, "dotnet.exe"));
            // The self-contained frontend and the framework-dependent services
            // have separate runtimes. Select only the child's owned runtime;
            // never change the frontend or the user's global environment.
            info.Environment["DOTNET_ROOT"] = privateRuntime;
            info.Environment["DOTNET_ROOT_X64"] = privateRuntime;
            info.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        }
        foreach (var value in new[]
        {
            "--host-pipe", pipeName, "--catalog", Path.Combine(root, "widget-catalog.json"),
            "--settings-root", Path.GetFullPath(SettingsRoot),
            "--installed-catalog-root", Path.GetFullPath(InstalledCatalogRoot),
            "--accept-timeout-ms", ((int)ConnectTimeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
        }) info.ArgumentList.Add(value);
        return info;
    }
}

/// <summary>
/// Owns one bridge child and its managed presentation connection. Does not attach
/// to another host or bypass catalog/worker admission. Call DisposeAsync on exit.
/// </summary>
public sealed class OwnedBridgeProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task stdout;
    private readonly Task stderr;
    private readonly StreamReader outputReader;
    private readonly StreamReader errorReader;
    private readonly ConcurrentQueue<string> diagnostics = new();
    private readonly object disposalGate = new();
    private readonly Action<string>? foregroundDiagnostic;
    private readonly CancellationTokenSource drainLifetime = new();
    private Task? disposal;
    private WidgetPresentationSession? session;
    public WidgetPresentationSession Session => session ?? throw new InvalidOperationException("Bridge connection is not established.");
    public int ProcessId => process.Id;
    public IReadOnlyList<string> Diagnostics => diagnostics.ToArray();

    private OwnedBridgeProcess(Process process, Action<string>? foregroundDiagnostic,
        StreamReader? standardOutput = null, StreamReader? standardError = null)
    {
        this.process = process;
        this.foregroundDiagnostic = foregroundDiagnostic;
        outputReader = standardOutput ?? process.StandardOutput;
        errorReader = standardError ?? process.StandardError;
        stdout = DrainAsync(outputReader);
        stderr = DrainAsync(errorReader);
    }

    public static async Task<OwnedBridgeProcess> StartAsync(BridgeProcessOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var pipe = "WidgetRail.WinUI." + Guid.NewGuid().ToString("N");
        var info = options.CreateStartInfo(pipe);
        if (!File.Exists(info.FileName) || !File.Exists(Path.Combine(info.WorkingDirectory, "widget-catalog.json")))
            throw new FileNotFoundException("The selected bridge installation is incomplete.");
        cancellationToken.ThrowIfCancellationRequested();
        var bound = options.ProcessOwnerJobName is not null
            ? JobBoundProcess.Start(info, options.ProcessOwnerJobName) : null;
        var child = bound?.Process ?? Process.Start(info) ?? throw new InvalidOperationException("Bridge process did not start.");
        var owner = new OwnedBridgeProcess(child, options.ForegroundDelegationDiagnostic, bound?.Output, bound?.Error);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.ConnectTimeout);
        var connection = WidgetPresentationSession.ConnectAsync(pipe,
            new() { ClientName = "WidgetRail.WinUI", ConnectTimeout = options.ConnectTimeout, WindowPreviews = options.WindowPreviews,
                ExclusiveControllerControl = options.ExclusiveControllerControl,
                BeforeInputWrite = owner.DelegateForegroundForInput }, deadline.Token);
        var exited = child.WaitForExitAsync(deadline.Token);
        try
        {
            if (await Task.WhenAny(connection, exited).ConfigureAwait(false) == exited)
            {
                deadline.Cancel();
                try { owner.session = await connection.ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException) { }
                cancellationToken.ThrowIfCancellationRequested();
                var reason = child.HasExited ? $"Bridge exited during connection (code {child.ExitCode})." : "Bridge connection timed out.";
                if (child.HasExited)
                {
                    // The runtime can fail before Program/Main and therefore before
                    // bridge logging exists. Preserve its bounded redirected output.
                    try { await Task.WhenAll(owner.stdout, owner.stderr).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
                    catch (TimeoutException) { }
                    var details = string.Join(Environment.NewLine, owner.Diagnostics);
                    if (details.Length > 4096) details = details[..4096];
                    if (details.Length != 0) reason += Environment.NewLine + details;
                }
                throw new IOException(reason);
            }
            owner.session = await connection.ConfigureAwait(false);
            return owner;
        }
        catch (Exception error)
        {
            await CleanupAfterFailedStartAsync(error, owner.DisposeAsync).ConfigureAwait(false);
            throw;
        }
        finally
        {
            deadline.Cancel();
            try { await exited.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                owner.Record($"Bridge exit observer failed: {error.GetType().Name}.");
            }
        }
    }

    internal static async Task CleanupAfterFailedStartAsync(Exception original, Func<ValueTask> cleanup)
    {
        try { await cleanup().ConfigureAwait(false); }
        catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException)
        {
            // Retain the connection/cancellation failure and its original stack.
            original.Data["BridgeCleanupFailure"] = cleanupError;
        }
    }

    private void DelegateForegroundForInput(string requestType)
    {
        string diagnostic;
        try
        {
            lock (disposalGate)
            {
                // The Process object owns this exact child; never discover a
                // broker by name or delegate to a widget-supplied process ID.
                if (disposal is not null || process.HasExited) return;
                var foreground = BridgeForegroundPermission.ForegroundProcess;
                if (foreground != (uint)Environment.ProcessId) return;
                var allowed = BridgeForegroundPermission.Allow((uint)process.Id, out var error);
                diagnostic = $"Foreground delegation request={requestType} brokerPid={process.Id} foregroundPid={foreground} allowed={allowed} error={error}";
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A retiring process or a denied Windows permission must not turn
            // an otherwise valid widget input into a transport/session failure.
            diagnostic = $"Foreground delegation request={requestType} unavailable={error.GetType().Name}";
        }
        Record(diagnostic);
        try { foregroundDiagnostic?.Invoke(diagnostic); }
        catch { /* Diagnostics do not control input dispatch. */ }
    }

    /// <summary>Refresh the owned broker's permission immediately before an admitted host handoff hides.</summary>
    public void RefreshForegroundPermissionForHandoff() => DelegateForegroundForInput("task-handoff");

    private async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        var line = new StringBuilder();
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), drainLifetime.Token).ConfigureAwait(false)) != 0)
                for (var i = 0; i < count; ++i)
                {
                    var value = buffer[i];
                    if (value == '\n') { Record(line.ToString()); line.Clear(); }
                    else if (value != '\r' && line.Length < 4096) line.Append(value);
                }
            if (line.Length != 0) Record(line.ToString());
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) when (drainLifetime.IsCancellationRequested) { }
    }

    private void Record(string message)
    {
        diagnostics.Enqueue(message);
        while (diagnostics.Count > 64) diagnostics.TryDequeue(out _);
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate) return new(disposal ??= StopAsync());
    }

    private async Task StopAsync()
    {
        try
        {
            if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    // Own only this child. Never kill a user's launched game by
                    // recursively terminating descendants of a full-trust widget.
                    if (!process.HasExited) process.Kill();
                    using var killedDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await process.WaitForExitAsync(killedDeadline.Token).ConfigureAwait(false);
                }
                drainLifetime.CancelAfter(TimeSpan.FromSeconds(3));
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            }
            finally
            {
                drainLifetime.Cancel();
                process.Dispose();
                outputReader.Dispose(); errorReader.Dispose();
                try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
                finally { drainLifetime.Dispose(); }
            }
        }
    }
}
