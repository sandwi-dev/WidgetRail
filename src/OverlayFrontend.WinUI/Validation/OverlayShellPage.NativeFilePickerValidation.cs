using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableNativeFilePickerValidation(string output)
    {
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            var checks = new List<string>();
            var trace = new List<string>();
            var originalTrace = NativeFilePickerLifetime.TraceForValidation;
            string? error = null;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            NativeFilePickerLifetime.TraceForValidation = message => { trace.Add(message); originalTrace?.Invoke(message); };
            try
            {
                if (startup is not null) await startup;
                await Until(() => visible && foreground && interactive && !switching &&
                    FocusManager.GetFocusedElement(XamlRoot) is Control { IsLoaded: true, IsEnabled: true });
                var focus = FocusManager.GetFocusedElement(XamlRoot) as Control;
                var root = XamlRoot;
                var calls = 0;
                using (var alreadyCancelled = new CancellationTokenSource())
                {
                    alreadyCancelled.Cancel();
                    try
                    {
                        await WithNativeFilePickerAsync(root, null, _ => { calls++; return Task.FromResult(Array.Empty<string>()); }, alreadyCancelled.Token);
                        throw new InvalidOperationException("Initial cancellation unexpectedly opened a picker.");
                    }
                    catch (OperationCanceledException) when (alreadyCancelled.IsCancellationRequested) { }
                }
                Check(calls == 0 && !SystemFilePickerOpen, "An already cancelled request neither opens nor owns a picker");

                var choosing = WithNativeFilePickerAsync(root, null, token =>
                {
                    calls++;
                    return BrowserFilePicker.PickAsync(root.ContentIslandEnvironment.AppWindowId, new(false, false), token, "Fixture selection");
                }, cancellation.Token);
                await Until(() => SystemFilePickerOpen);
                Check(calls == 1, "Production browser/native-widget picker adapter invoked once");
                try
                {
                    await WithNativeFilePickerAsync(root, null, _ => { calls++; return Task.FromResult(Array.Empty<string>()); }, lifetime.Token);
                    throw new InvalidOperationException("Concurrent picker unexpectedly opened.");
                }
                catch (OperationCanceledException) { }
                Check(calls == 1 && SystemFilePickerOpen, "Concurrent request cannot steal or clear the first picker's input ownership");
                // Let Windows create its common dialog. The production lifetime
                // handles cancellation before creation too and joins its actual close.
                await Task.Delay(800, lifetime.Token);
                cancellation.Cancel();
                try
                {
                    await choosing.WaitAsync(TimeSpan.FromSeconds(15));
                    throw new InvalidOperationException("Cancelled native picker unexpectedly returned a selection.");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                Check(trace.Contains("cancellation requested"), "Cancellation reaches the production native dialog lifetime");
                Check(!SystemFilePickerOpen, "Actual native completion releases the shared shell picker owner");
                await Until(() => foreground && interactive && !switching);
                if (focus is { IsLoaded: true, IsEnabled: true })
                {
                    await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(root), focus));
                    Check(true, "Native cancellation restores the same invoking control");
                }
                else throw new InvalidOperationException("Invoking control retired during the focus restoration scenario.");
                Check(!HostChoiceActive && !LocalInstallActive, "Native picker cleanup leaves no replacement host dialog owner");
            }
            catch (Exception failure) { error = failure.ToString(); }
            finally
            {
                cancellation.Cancel();
                NativeFilePickerLifetime.TraceForValidation = originalTrace;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = error is null, checks, trace, error,
                    scope = "Real Windows file dialog cancellation/join and shared browser/widget shell input/focus lifecycle. No file is selected; broker request authority is covered separately by host-pipe tests." }));
            }
            void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); }
            static async Task Until(Func<bool> ready)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (!ready()) await Task.Delay(20, timeout.Token);
            }
        };
    }
}
