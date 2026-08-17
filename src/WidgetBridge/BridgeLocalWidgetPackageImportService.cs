using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WidgetRail.WidgetCatalog;

namespace WidgetRail.WidgetBridge;

internal sealed class BridgeLocalWidgetPackageImportService : IAsyncDisposable
{
    private const int MaximumOperationIdLength = 64;
    private const int MaximumPackagePathLength = 32_767;
    private readonly object _gate = new();
    private readonly WidgetCatalog.WidgetCatalog _catalog;
    private readonly Func<BridgeLocalWidgetPackageOrigin, IDisposable> _admit;
    private readonly Func<CancellationToken, Task> _reload;
    private readonly Func<CancellationToken, Task> _beforePublish;
    private readonly Func<BridgeLocalWidgetPackageInstallCompleted, Task> _completed;
    private readonly Func<string, Stream> _openPackage;
    private ActiveInstall? _active;
    private bool _disposed;

    internal BridgeLocalWidgetPackageImportService(
        BridgeClientRegistry registry,
        WidgetCatalog.WidgetCatalog catalog,
        BridgeCatalogMonitor monitor,
        Func<BridgeLocalWidgetPackageInstallCompleted, Task> completed,
        Func<string, Stream>? openPackage = null)
        : this(
            catalog,
            origin => registry.AdmitLocalWidgetPackageImport(origin),
            cancellationToken => monitor.ReloadNowAsync(cancellationToken),
            completed,
            openPackage,
            beforePublish: null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(monitor);
    }

    internal BridgeLocalWidgetPackageImportService(
        WidgetCatalog.WidgetCatalog catalog,
        Func<BridgeLocalWidgetPackageOrigin, IDisposable> admit,
        Func<CancellationToken, Task> reload,
        Func<BridgeLocalWidgetPackageInstallCompleted, Task> completed,
        Func<string, Stream>? openPackage,
        Func<CancellationToken, Task>? beforePublish)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        _reload = reload ?? throw new ArgumentNullException(nameof(reload));
        _completed = completed ?? throw new ArgumentNullException(nameof(completed));
        _openPackage = openPackage ?? OpenPackageWithoutFollowingReparsePoints;
        _beforePublish = beforePublish ?? (_ => Task.CompletedTask);
    }

    internal bool Active { get { lock (_gate) return _active is not null; } }

    internal void Start(
        BridgeLocalWidgetPackageInstallRequest request,
        CancellationToken sessionCancellation)
    {
        ValidateRequest(request);
        using (_admit(request.Origin)) { }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active is not null)
                throw new BridgeProtocolException(
                    "A local widget package install is already active.");
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                sessionCancellation);
            var active = new ActiveInstall(request, cancellation);
            _active = active;
            active.Task = Task.Run(() => RunAsync(active));
        }
    }

    internal bool Cancel(string operationId)
    {
        if (!ValidOperationId(operationId)) return false;
        lock (_gate)
        {
            if (_active is null || !string.Equals(
                    _active.Request.OperationId, operationId, StringComparison.Ordinal))
                return false;
            _active.Cancellation.Cancel();
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? task;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _active?.Cancellation.Cancel();
            task = _active?.Task;
        }
        if (task is not null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task RunAsync(ActiveInstall active)
    {
        BridgeLocalWidgetPackageInstallCompleted result;
        try
        {
            await using var package = _openPackage(active.Request.PackagePath);
            var installed = await _catalog.InstallAsync(
                package,
                async (inspection, token) =>
                {
                    await _beforePublish(token).ConfigureAwait(false);
                    using var admission = _admit(active.Request.Origin);
                },
                active.Cancellation.Token).ConfigureAwait(false);
            // Publication is the operation's linearization point. Once the
            // catalog commit returns, a late overlay/session cancellation may
            // not relabel the durable install as cancelled.
            await _reload(CancellationToken.None).ConfigureAwait(false);
            result = new(
                active.Request.OperationId,
                "installed-disabled",
                installed.Id,
                installed.Version.ToString(),
                "Local widget package installed disabled. Review it before enabling.");
        }
        catch (OperationCanceledException) when (active.Cancellation.IsCancellationRequested)
        {
            result = new(
                active.Request.OperationId, "cancelled", "", "",
                "Local widget package install was cancelled.");
        }
        catch (WidgetPackageException exception)
        {
            result = Failure(active.Request.OperationId, exception.Code);
        }
        catch (BridgeProtocolException)
        {
            result = Failure(active.Request.OperationId, "stale_origin");
        }
        catch (Exception exception) when (exception is IOException or
                                                  UnauthorizedAccessException or
                                                  Win32Exception or
                                                  InvalidDataException)
        {
            result = Failure(active.Request.OperationId, "source_unavailable");
        }
        catch (Exception)
        {
            result = Failure(active.Request.OperationId, "install_failed");
        }

        try { await _completed(result).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or
                                                  OperationCanceledException or
                                                  ObjectDisposedException or
                                                  InvalidOperationException or
                                                  BridgeProtocolException)
        {
            // The native pipe is the only consumer. Session teardown can close
            // it while the background operation is publishing its terminal
            // result; losing that event must not fail bridge disposal.
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_active, active)) _active = null;
            }
            active.Cancellation.Dispose();
        }
    }

    private static BridgeLocalWidgetPackageInstallCompleted Failure(
        string operationId,
        string code)
    {
        var safeCode = code is { Length: > 0 and <= 64 } &&
            code.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
                ? code
                : "install_failed";
        return new(
            operationId, "failed", "", "",
            $"Local widget package install failed ({safeCode}).");
    }

    private static void ValidateRequest(BridgeLocalWidgetPackageInstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Origin);
        if (!ValidOperationId(request.OperationId))
            throw new BridgeProtocolException("Local widget package operation ID is invalid.");
        if (string.IsNullOrWhiteSpace(request.PackagePath) ||
            request.PackagePath.Length > MaximumPackagePathLength ||
            request.PackagePath.IndexOf('\0') >= 0 ||
            !Path.IsPathFullyQualified(request.PackagePath) ||
            !Path.GetExtension(request.PackagePath).Equals(
                ".wrwidget", StringComparison.OrdinalIgnoreCase))
            throw new BridgeProtocolException("Local widget package selection is invalid.");
    }

    private static bool ValidOperationId(string? value) =>
        value is { Length: > 0 and <= MaximumOperationIdLength } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static Stream OpenPackageWithoutFollowingReparsePoints(string path)
    {
        var handle = CreateFileW(
            path,
            0x80000000,
            0,
            IntPtr.Zero,
            3,
            0x00200000 | 0x08000000,
            IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!GetFileInformationByHandleEx(
                    handle, 9, out var info, (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            const uint directory = 0x10;
            const uint reparsePoint = 0x400;
            if ((info.FileAttributes & (directory | reparsePoint)) != 0)
                throw new IOException("The selected package is not a regular file.");
            return new FileStream(handle, FileAccess.Read, 64 * 1024, isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        internal uint FileAttributes;
        internal uint ReparseTag;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int informationClass,
        out FileAttributeTagInfo information,
        uint bufferSize);

    private sealed class ActiveInstall(
        BridgeLocalWidgetPackageInstallRequest request,
        CancellationTokenSource cancellation)
    {
        internal BridgeLocalWidgetPackageInstallRequest Request { get; } = request;
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal Task? Task { get; set; }
    }
}
