using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GameBarAlternative.WindowsActivityProvider;

internal sealed class WindowsActivityNativeAdapter : IWindowsActivityNativeAdapter
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectDestroy = 0x8001;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const uint ObjidWindow = 0;
    private const uint GaRoot = 2;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const int SwRestore = 9;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;
    private readonly object _gate = new();
    private HookSubscription? _active;

    public IDisposable Start(Action<NativeActivityEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_active?.IsDisposed == true, this);
            if (_active is not null)
                throw new InvalidOperationException("The activity observer is already running.");
            _active = new HookSubscription(handler, () =>
            {
                lock (_gate) _active = null;
            });
            return _active;
        }
    }

    public nint GetCurrentForegroundWindow() => GetForegroundWindow();

    public NativeActivityCandidate? InspectWindow(nint window)
    {
        if (!IsEligibleWindow(window)) return null;
        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == Environment.ProcessId) return null;

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            var processName = Normalize(process.ProcessName, 96);
            if (processName is null) return null;
            var displayName = FriendlyName(process, processName);
            displayName = Normalize(displayName, 160);
            if (displayName is null) return null;
            return new NativeActivityCandidate(
                window, processId, processName.ToUpperInvariant(), displayName);
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    public bool IsWindowAvailable(nint window, uint expectedProcessId)
    {
        if (!IsEligibleWindow(window)) return false;
        _ = GetWindowThreadProcessId(window, out var processId);
        return processId == expectedProcessId;
    }

    public bool TryActivate(nint window, uint expectedProcessId)
    {
        if (!IsWindowAvailable(window, expectedProcessId)) return false;
        if (IsIconic(window)) ShowWindow(window, SwRestore);
        return SetForegroundWindow(window);
    }

    public void Dispose()
    {
        HookSubscription? active;
        lock (_gate)
        {
            active = _active;
            _active = null;
        }
        active?.Dispose();
    }

    private static bool IsEligibleWindow(nint window)
    {
        if (window == 0 || !IsWindow(window) || !IsWindowVisible(window) ||
            window == GetShellWindow() || GetAncestor(window, GaRoot) != window)
            return false;
        var style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        if ((style & (WsExToolWindow | WsExNoActivate)) != 0) return false;
        var length = GetWindowTextLengthW(window);
        if (length is <= 0 or > 4096) return false;
        var className = new StringBuilder(128);
        var classLength = GetClassNameW(window, className, className.Capacity);
        var windowClass = classLength > 0 ? className.ToString() : string.Empty;
        return windowClass is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or
            "Shell_SecondaryTrayWnd" or "XamlExplorerHostIslandWindow");
    }

    private static string FriendlyName(Process process, string processName)
    {
        try
        {
            var info = process.MainModule?.FileVersionInfo;
            return ResolveDisplayName(info?.FileDescription, info?.ProductName, processName);
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            Win32Exception or NotSupportedException)
        {
            return Humanize(processName);
        }
    }

    internal static string ResolveDisplayName(
        string? fileDescription, string? productName, string processName) =>
        SafeMetadataName(fileDescription) ?? SafeMetadataName(productName) ?? Humanize(processName);

    private static string? SafeMetadataName(string? value)
    {
        var normalized = Normalize(value, 160);
        if (normalized is null) return null;
        // Version resources are controlled by the executable publisher and
        // occasionally contain installation paths. Public activity summaries
        // must never forward a path, even when it is labelled as a description.
        if (normalized.Contains('\\') || normalized.Contains('/') ||
            (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
            return null;
        return normalized;
    }

    private static string Humanize(string processName)
    {
        var builder = new StringBuilder(processName.Length + 8);
        for (var index = 0; index < processName.Length; index++)
        {
            var current = processName[index];
            if (index > 0 && char.IsUpper(current) && char.IsLower(processName[index - 1]))
                builder.Append(' ');
            builder.Append(current is '_' or '-' ? ' ' : current);
        }
        return builder.ToString();
    }

    private static string? Normalize(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(' ', value.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > maximum) normalized = normalized[..maximum].TrimEnd();
        return normalized.Length != 0 && !normalized.Any(char.IsControl) ? normalized : null;
    }

    private sealed class HookSubscription : IDisposable
    {
        private readonly Action<NativeActivityEvent> _handler;
        private readonly Action _onDisposed;
        private readonly TaskCompletionSource _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread _thread;
        private uint _threadId;
        private nint _foregroundHook;
        private nint _destroyHook;
        private WinEventDelegate? _callback;
        private int _disposed;

        internal HookSubscription(Action<NativeActivityEvent> handler, Action onDisposed)
        {
            _handler = handler;
            _onDisposed = onDisposed;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "GameBarAlternative.ActivityWinEvent",
            };
            _thread.Start();
            _ready.Task.GetAwaiter().GetResult();
        }

        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        private void Run()
        {
            try
            {
                _threadId = GetCurrentThreadId();
                _callback = OnWinEvent;
                _foregroundHook = SetWinEventHook(
                    EventSystemForeground, EventSystemForeground, 0, _callback, 0, 0,
                    WineventOutOfContext | WineventSkipOwnProcess);
                _destroyHook = SetWinEventHook(
                    EventObjectDestroy, EventObjectDestroy, 0, _callback, 0, 0,
                    WineventOutOfContext | WineventSkipOwnProcess);
                if (_foregroundHook == 0 || _destroyHook == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                // Create this thread's message queue before publishing readiness;
                // otherwise an immediate Dispose could race PostThreadMessage.
                _ = PeekMessageW(out _, 0, 0, 0, PmNoRemove);
                _ready.TrySetResult();
                while (GetMessageW(out var message, 0, 0, 0) > 0)
                {
                    TranslateMessage(in message);
                    DispatchMessageW(in message);
                }
            }
            catch (Exception exception)
            {
                _ready.TrySetException(exception);
            }
            finally
            {
                if (_foregroundHook != 0) UnhookWinEvent(_foregroundHook);
                if (_destroyHook != 0) UnhookWinEvent(_destroyHook);
                _foregroundHook = 0;
                _destroyHook = 0;
            }
        }

        private void OnWinEvent(
            nint hook, uint eventType, nint window, int objectId, int childId,
            uint eventThread, uint eventTime)
        {
            if (window == 0 || objectId != unchecked((int)ObjidWindow) || childId != 0) return;
            _handler(new NativeActivityEvent(
                eventType == EventSystemForeground
                    ? NativeActivityEventKind.Foreground
                    : NativeActivityEventKind.Destroyed,
                window));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_threadId != 0) PostThreadMessageW(_threadId, WmQuit, 0, 0);
            if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(2000);
            _onDisposed();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        internal nint HWnd;
        internal uint Value;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X;
        internal int Y;
        internal uint Private;
    }

    private delegate void WinEventDelegate(
        nint hook, uint eventType, nint window, int objectId, int childId,
        uint eventThread, uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint module, WinEventDelegate callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Message message, nint window, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(
        out Message message, nint window, uint min, uint max, uint removeMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(in Message message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(in Message message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder className, int maximum);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
