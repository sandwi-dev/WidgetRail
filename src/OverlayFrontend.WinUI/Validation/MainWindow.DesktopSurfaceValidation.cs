using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private CancellationTokenSource? desktopSurfaceValidationLifetime;
    private NamedPipeClientStream? desktopSurfaceValidationPipe;

    private void ConfigureDesktopSurfaceValidation(string configurationPath)
    {
        var path = Path.GetFullPath(configurationPath);
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Oversized surface fixture configuration.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var configuration = document.RootElement;
        var pipeName = configuration.GetProperty("pipeName").GetString()!;
        var secret = configuration.GetProperty("token").GetString()!;
        var expectedProcess = configuration.GetProperty("helperProcessId").GetInt32();
        var expectedStart = configuration.GetProperty("helperStartTicks").GetInt64();
        var expectedExecutable = Path.GetFullPath(configuration.GetProperty("helperExecutable").GetString()!);
        if (!Regex.IsMatch(pipeName, "^wrail-desktop-surface-[a-f0-9]{32}$") ||
            !Regex.IsMatch(secret, "^[a-f0-9]{64}$") || expectedProcess <= 0)
            throw new InvalidDataException("Invalid surface fixture configuration.");

        var output = Path.Combine(Path.GetDirectoryName(path)!, "result.json");
        var page = new Validation.DesktopSurfaceValidationPage();
        RootFrame.Content = page;
        ShellTitle.Text = "Generated desktop surface — validation only";
        AppWindow.ResizeClient(new(1040, 760));
        var hostWindow = WinRT.Interop.WindowNative.GetWindowHandle(this);
        // The production input pump is exercised with a hardware-free backend.
        // It still observes actual Windows foreground. No controller is claimed.
        input = new(DispatcherQueue, hostWindow, new Validation.ReplayNativePlatform());
        var lifetime = desktopSurfaceValidationLifetime = new();
        var running = false;
        page.RunRequested += async () =>
        {
            if (running) return;
            running = true;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            var token = deadline.Token;
            var checks = new List<string>();
            var observations = new List<object>();
            var passed = false;
            string? failure = null;
            nint admittedWindow = 0;
            var dismissalCount = 0;
            var sequence = 0;
            StreamReader? reader = null;
            StreamWriter? writer = null;
            void Dismissal(nint _) => dismissalCount++;
            input.ExternalForegroundObserved += Dismissal;
            input.OwnsAuxiliaryControllerWindow = handle => handle != 0 && handle == admittedWindow && ExactGeneratedWindow();
            input.OwnsAuxiliaryProcess = pid => pid == (uint)expectedProcess && admittedWindow != 0 &&
                WindowsTaskWindowActivation.Observe(admittedWindow).Foreground == (long)admittedWindow && ExactGeneratedWindow();
            try
            {
                page.Report("Connecting to the isolated generated window…");
                Check(WindowsTaskWindowActivation.Observe(hostWindow).Foreground == (long)hostWindow,
                    "Run starts with real host foreground");
                using (var process = Process.GetProcessById(expectedProcess))
                    Check(process.StartTime.ToUniversalTime().Ticks == expectedStart &&
                        string.Equals(process.MainModule?.FileName, expectedExecutable, StringComparison.OrdinalIgnoreCase),
                        "Runner-owned helper executable and creation identity match");
                desktopSurfaceValidationPipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await desktopSurfaceValidationPipe.ConnectAsync(token);
                Check(DesktopSurfaceGetServerProcess(desktopSurfaceValidationPipe.SafePipeHandle, out var actualProcess) != 0 &&
                    actualProcess == (uint)expectedProcess, "OS pipe peer is the expected generated process");
                reader = new(desktopSurfaceValidationPipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                writer = new(desktopSurfaceValidationPipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                var initial = await Command("status");
                observations.Add(new { phase = "initial", state = initial });
                var window = (nint)long.Parse(initial.GetProperty("window").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var initialObservation = WindowsTaskWindowActivation.Observe(window);
                Check(initialObservation.TargetValid && initialObservation.TargetProcess == (uint)expectedProcess &&
                    !initial.GetProperty("visible").GetBoolean(), "Generated main window starts hidden with exact process identity");
                Check(DesktopSurfaceGetWindowRect(window, out var originalPhysical) != 0,
                    "Original native window rectangle is readable");
                observations.Add(new { phase = "original-native", bounds = Describe(originalPhysical) });
                admittedWindow = window;
                input.SetVisible(true);
                var point = page.SurfaceSlot.TransformToVisual(ShellRoot).TransformPoint(new(0, 0));
                var scale = GetDpiForWindow(hostWindow) / 96d;
                var origin = new DesktopSurfacePoint { X = (int)Math.Round(point.X * scale), Y = (int)Math.Round(point.Y * scale) };
                Check(DesktopSurfaceClientToScreen(hostWindow, ref origin) != 0, "Placement region maps to screen coordinates");
                var bounds = new { x = origin.X, y = origin.Y,
                    width = (int)Math.Round(page.SurfaceSlot.ActualWidth * scale),
                    height = (int)Math.Round(page.SurfaceSlot.ActualHeight * scale) };
                Check(bounds.width >= 80 && bounds.height >= 80, "Generated placement region is realized");
                Check(DesktopSurfaceAllowForeground((uint)expectedProcess) != 0, "Foreground owner grants the generated client eligibility");
                var borrowed = await Command("borrow", bounds);
                await Until(() => WindowsTaskWindowActivation.Observe(window).Foreground == (long)window);
                await Task.Delay(300, token); // Several real input-pump foreground observations.
                Check(AppWindow.IsVisible && dismissalCount == 0, "Exact borrowed window does not trigger external foreground dismissal");
                Check(ExactGeneratedWindow(), "Borrowed HWND still belongs to the same generated process");
                Check(DesktopSurfaceGetWindowRect(window, out var physical) != 0 &&
                    DesktopSurfaceGetClientRect(window, out _) != 0,
                    "Borrowed native window rectangles are readable");
                Check(DesktopSurfaceGetClientRect(window, out var client) != 0,
                    "Borrowed content rectangle is readable");
                var clientOrigin = new DesktopSurfacePoint();
                Check(DesktopSurfaceClientToScreen(window, ref clientOrigin) != 0,
                    "Borrowed content origin maps to physical screen coordinates");
                observations.Add(new { phase = "borrowed", requested = bounds, state = borrowed,
                    nativeBounds = Describe(physical), clientOrigin = new { x = clientOrigin.X, y = clientOrigin.Y },
                    nativeClient = Describe(client),
                    native = WindowsTaskWindowActivation.Observe(window) });
                // Electron's integer-DIP getBounds followed by dipToScreenRect
                // compounds rounding. Outer HWND bounds also include invisible
                // resize borders. Compare the actual client pixels to the slot.
                Check(Math.Abs(clientOrigin.X - bounds.x) <= 2 &&
                    Math.Abs(clientOrigin.Y - bounds.y) <= 2 &&
                    Math.Abs(client.Right - client.Left - bounds.width) <= 2 &&
                    Math.Abs(client.Bottom - client.Top - bounds.height) <= 2,
                    "Native client pixels match the content region within DPI rounding");
                var returnPrepared = await Command("prepareReturn");
                Check(returnPrepared.GetProperty("returnEligibilityGranted").GetBoolean(),
                    "Foreground-owning client grants the host return eligibility before hiding");
                // Keep the client visible/admitted until the host has taken focus.
                // Hiding first lets Windows choose another foreground owner and
                // can leave the host without permission to perform the return.
                var foreground = WindowsTaskWindowActivation.Observe(hostWindow);
                Check(foreground.Foreground == (long)window || foreground.Foreground == (long)hostWindow,
                    "Foreground remains within the handoff pair before return");
                // The hardware-free input backend intentionally does not acquire
                // native foreground. Use the same real Win32 wrapper as the other
                // foreground fixtures; Window.Activate alone is not confirmation.
                WinUIEx.HwndExtensions.SetForegroundWindow(hostWindow);
                observations.Add(new { phase = "return-requested", state = returnPrepared,
                    native = WindowsTaskWindowActivation.Observe(hostWindow) });
                await Until(() => WindowsTaskWindowActivation.Observe(hostWindow).Foreground == (long)hostWindow);
                Check(true, "Host regains foreground before the client hides");
                var restored = await Command("restore");
                Check(DesktopSurfaceGetWindowRect(window, out var restoredPhysical) != 0,
                    "Restored native window rectangle is readable");
                observations.Add(new { phase = "restored", state = restored, bounds = Describe(restoredPhysical),
                    native = WindowsTaskWindowActivation.Observe(window) });
                admittedWindow = 0;
                Check(WindowsTaskWindowActivation.Observe(hostWindow).Foreground == (long)hostWindow,
                    "Host remains foreground after client restoration");
                Check(!restored.GetProperty("visible").GetBoolean() && !restored.GetProperty("borrowed").GetBoolean() &&
                    restoredPhysical.Equals(originalPhysical),
                    "Original bounds and hidden state are restored without a second renderer");
                Check(AppWindow.IsVisible, "Host remains visible after returning from the generated client");
                passed = true;
                page.Report("Borrow/restore check passed. Visual, modal, controller and IME checks remain separate.");
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + ": " + error.Message;
                page.Report("Generated-window check failed. See result.json for the bounded failure.");
            }
            finally
            {
                admittedWindow = 0;
                input.SetVisible(false);
                input.ExternalForegroundObserved -= Dismissal;
                input.OwnsAuxiliaryProcess = null;
                input.OwnsAuxiliaryControllerWindow = null;
                // Disconnect makes the helper restore its own window and quit.
                try { writer?.Dispose(); reader?.Dispose(); }
                catch (IOException) { passed = false; failure ??= "Fixture pipe cleanup failed."; }
                desktopSurfaceValidationPipe?.Dispose(); desktopSurfaceValidationPipe = null;
                File.WriteAllText(output, JsonSerializer.Serialize(new
                {
                    passed, checks, observations, failure, dismissalCount,
                    visualReviewRequired = true, physicalControllerTested = false,
                    scope = "Generated WinUI/Electron borrow and restore only; hardware-free controller backend, real Windows foreground. No Discord, account, capture, media or production surface contract."
                }, new JsonSerializerOptions { WriteIndented = true }));
            }

            bool ExactGeneratedWindow()
            {
                if (admittedWindow == 0) return false;
                var observed = WindowsTaskWindowActivation.Observe(admittedWindow);
                return observed.TargetValid && observed.TargetProcess == (uint)expectedProcess;
            }
            void Check(bool condition, string message)
            { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
            async Task Until(Func<bool> condition)
            {
                while (!condition()) { token.ThrowIfCancellationRequested(); await Task.Delay(20, token); }
            }
            async Task<JsonElement> Command(string command, object? bounds = null)
            {
                token.ThrowIfCancellationRequested();
                var id = ++sequence;
                var request = command == "prepareReturn" ? JsonSerializer.Serialize(new { id, token = secret, command, processId = Environment.ProcessId }) :
                    bounds is null ? JsonSerializer.Serialize(new { id, token = secret, command }) :
                    JsonSerializer.Serialize(new { id, token = secret, command, bounds });
                await writer!.WriteLineAsync(request.AsMemory(), token);
                var line = new StringBuilder();
                var character = new char[1];
                while (true)
                {
                    if (await reader!.ReadAsync(character.AsMemory(), token) == 0) throw new IOException("Generated helper disconnected.");
                    if (character[0] == '\n') break;
                    if (line.Length >= 4096) throw new InvalidDataException("Oversized helper response.");
                    line.Append(character[0]);
                }
                using var response = JsonDocument.Parse(line.ToString());
                if (response.RootElement.GetProperty("id").GetInt32() != id) throw new InvalidDataException("Unexpected helper response identity.");
                var state = response.RootElement.GetProperty("state");
                if (state.GetProperty("processId").GetInt32() != expectedProcess) throw new InvalidDataException("Helper identity changed.");
                return state.Clone();
            }
            static object Describe(DesktopSurfaceRect rect) => new { x = rect.Left, y = rect.Top,
                width = rect.Right - rect.Left, height = rect.Bottom - rect.Top };
        };
    }

    private void RetireDesktopSurfaceValidation()
    {
        desktopSurfaceValidationLifetime?.Cancel();
        desktopSurfaceValidationPipe?.Dispose();
        desktopSurfaceValidationLifetime?.Dispose();
        desktopSurfaceValidationLifetime = null;
        desktopSurfaceValidationPipe = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DesktopSurfacePoint { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DesktopSurfaceRect { internal int Left, Top, Right, Bottom; }
    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DesktopSurfaceGetWindowRect(nint window, out DesktopSurfaceRect rect);
    [LibraryImport("user32.dll", EntryPoint = "GetClientRect", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DesktopSurfaceGetClientRect(nint window, out DesktopSurfaceRect rect);
    [LibraryImport("user32.dll", EntryPoint = "ClientToScreen", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DesktopSurfaceClientToScreen(nint window, ref DesktopSurfacePoint point);
    [LibraryImport("user32.dll", EntryPoint = "AllowSetForegroundWindow", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DesktopSurfaceAllowForeground(uint processId);
    [LibraryImport("kernel32.dll", EntryPoint = "GetNamedPipeServerProcessId", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DesktopSurfaceGetServerProcess(SafePipeHandle pipe, out uint processId);
}
