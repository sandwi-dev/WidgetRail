using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace WidgetRail.WidgetPresentationSession;

public sealed record BridgeProcessOptions(string InstallationRoot, string SettingsRoot, string InstalledCatalogRoot)
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Opt in only when the frontend owns a permission-checked capture renderer.</summary>
    public bool WindowPreviews { get; init; }

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
    private readonly ConcurrentQueue<string> diagnostics = new();
    private readonly object disposalGate = new();
    private readonly CancellationTokenSource drainLifetime = new();
    private Task? disposal;
    private WidgetPresentationSession? session;
    public WidgetPresentationSession Session => session ?? throw new InvalidOperationException("Bridge connection is not established.");
    public int ProcessId => process.Id;
    public IReadOnlyList<string> Diagnostics => diagnostics.ToArray();

    private OwnedBridgeProcess(Process process)
    {
        this.process = process;
        stdout = DrainAsync(process.StandardOutput);
        stderr = DrainAsync(process.StandardError);
    }

    public static async Task<OwnedBridgeProcess> StartAsync(BridgeProcessOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var pipe = "WidgetRail.WinUI." + Guid.NewGuid().ToString("N");
        var info = options.CreateStartInfo(pipe);
        if (!File.Exists(info.FileName) || !File.Exists(Path.Combine(info.WorkingDirectory, "widget-catalog.json")))
            throw new FileNotFoundException("The selected bridge installation is incomplete.");
        cancellationToken.ThrowIfCancellationRequested();
        var child = Process.Start(info) ?? throw new InvalidOperationException("Bridge process did not start.");
        var owner = new OwnedBridgeProcess(child);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.ConnectTimeout);
        var connection = WidgetPresentationSession.ConnectAsync(pipe,
            new() { ClientName = "WidgetRail.WinUI", ConnectTimeout = options.ConnectTimeout, WindowPreviews = options.WindowPreviews }, deadline.Token);
        var exited = child.WaitForExitAsync(deadline.Token);
        try
        {
            if (await Task.WhenAny(connection, exited).ConfigureAwait(false) == exited)
            {
                deadline.Cancel();
                try { owner.session = await connection.ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException) { }
                cancellationToken.ThrowIfCancellationRequested();
                throw new IOException(child.HasExited ? $"Bridge exited during connection (code {child.ExitCode})." : "Bridge connection timed out.");
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
                try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
                finally { drainLifetime.Dispose(); }
            }
        }
    }
}
