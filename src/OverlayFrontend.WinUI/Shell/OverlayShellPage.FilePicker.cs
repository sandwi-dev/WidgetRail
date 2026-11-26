using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using System.Globalization;
using WidgetRail.Internal;
using WidgetRail.PlatformBroker;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private Task? filePickerPump, choosingWidgetFiles;

    private async Task PumpFilePickersAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(350));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                if (choosingWidgetFiles is { IsCompleted: false } || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen) continue;
                try
                {
                    var next = await owner!.Session.TakeFilePickerAsync(lifetime.Token);
                    if (next.Prompt is { } request) choosingWidgetFiles = ChooseWidgetFilesAsync(request);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception error) when (BridgeDisconnected && error is not OutOfMemoryException) { break; }
                catch (Exception error) when (error is not OutOfMemoryException)
                { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("file-picker-pump", error); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task ChooseWidgetFilesAsync(BridgeFilePickerPrompt request)
    {
        var paths = Array.Empty<string>();
        string? windowMatch = null;
        var session = owner!.Session;
        var selection = selectionVersion;
        var sourcePin = PinnedInputActive && pinned?.WidgetId == request.WidgetId ? pinned : null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(115));
        var token = cancellation.Token;
        Task? monitor = null;
        bool CurrentOwner() => !retired && visible && ReferenceEquals(owner?.Session, session) && (sourcePin is not null
            ? ReferenceEquals(pinned, sourcePin) && sourcePin.IsCurrent && PinnedInputActive
            : activeWidget == request.WidgetId && selectionVersion == selection);
        async Task<bool> Current() => CurrentOwner() && await session.IsFilePickerCurrentAsync(request.RequestId, token) && CurrentOwner();
        try
        {
            if (!CurrentOwner() || sourcePin is null && (!foreground || !interactive) || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen ||
                !await Current()) return;
            var root = sourcePin is null ? XamlRoot : sourcePin.Window.AutomationRoot.XamlRoot;
            if (root is null) return;
            monitor = MonitorAsync();
            if (request.IsWindowChoice)
            {
                var target = await ChooseApplicationWindowAsync(request.Title, sourcePin, token);
                if (target != null && await Current() && ulong.TryParse(target.ProcessCreated, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var created) && ulong.TryParse(target.Handle, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var hwnd) && created != 0 && hwnd != 0 && target.ProcessId != 0)
                    windowMatch = CaptureProcessKey.Window(target.ProcessId, created, hwnd);
                return;
            }
            var selected = await WithNativeFilePickerAsync(root, sourcePin, pickerToken =>
                ChooseBrowserPathsAsync(root, sourcePin, new(false, request.Multiple), request.Title,
                    request.Multiple ? WidgetFileContract.MaximumFiles : 1, pickerToken, request.CommitLabel), token);
            if (await Current())
            {
                if (selected.Length > WidgetFileContract.MaximumFiles)
                    await ShowHostChoiceAsync("Too many files", $"Choose up to {WidgetFileContract.MaximumFiles} files at a time.",
                        [], token, pinnedOwner: sourcePin);
                else paths = selected;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("file-picker", error); }
        finally
        {
            cancellation.Cancel();
            if (monitor is not null) await monitor;
            try
            {
                if (!retired && request.IsWindowChoice) await session.CompleteWindowPickerAsync(request.RequestId, windowMatch, lifetime.Token);
                else if (!retired) await session.CompleteFilePickerAsync(request.RequestId, paths, lifetime.Token);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("file-picker-completion", error); }
        }
        async Task MonitorAsync()
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
                while (await timer.WaitForNextTickAsync(token))
                    if (!await Current()) { cancellation.Cancel(); break; }
            }
            catch (Exception error) when (error is not OutOfMemoryException) { cancellation.Cancel(); }
        }
    }

    private async Task<NativeWindowPreviewTarget?> ChooseApplicationWindowAsync(string title, PinnedSurface? pin, CancellationToken token)
    {
        var windows = (await TaskWindows.GetWindowsAsync(token)).Where(w => w.PreviewTarget is { } target &&
            target.ProcessId != Environment.ProcessId).ToArray();
        var foregroundWindow = ForegroundBeforeOverlay?.Invoke();
        var current = windows.FirstOrDefault(w => w.PreviewTarget == foregroundWindow)?.WindowId;
        var choices = await Task.Run(() => windows.Select(window =>
        {
            var label = window.ApplicationName;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(checked((int)window.PreviewTarget!.ProcessId));
                label = process.ProcessName + ".exe";
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return new HostDialogChoice(window.WindowId, window.Title, Value: label);
        }).ToArray(), token);
        var selected = await ShowHostPreferenceAsync(title, choices.Length == 0 ? "No application windows are available." : "",
            choices, token, pin, current, "Cancel");
        if (selected == null) return null;
        var chosen = windows.FirstOrDefault(window => window.WindowId == selected)?.PreviewTarget;
        return chosen != null && (await TaskWindows.GetWindowsAsync(token)).Any(window => window.PreviewTarget == chosen) ? chosen : null;
    }
}
