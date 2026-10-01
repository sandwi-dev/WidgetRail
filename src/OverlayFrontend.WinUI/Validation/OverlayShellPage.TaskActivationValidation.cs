using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableTaskActivationValidation(string resultPath, string peerPath,
        Func<Task>? beforeInvoke = null, Func<List<string>, Task>? verifyHandoff = null, Action? cleanup = null, bool expectCancellation = false)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            WidgetPresentationHostEffect? issued = null;
            object? observed = null;
            string? error = null;
            var trace = new List<string>();
            var originalDiagnostic = WindowActivationDiagnostic;
            WindowActivationDiagnostic = message => { trace.Add(message); originalDiagnostic?.Invoke(message); };
            void OnEffect(WidgetPresentationHostEffect effect) => issued = effect;
            try
            {
                using var peer = JsonDocument.Parse(File.ReadAllText(peerPath));
                var target = peer.RootElement.GetProperty("target").Deserialize<TaskWindowTarget>()!;
                var native = new WindowsTaskWindowActivation();
                Check(native.IsCurrent(target) && WindowsTaskWindowActivation.Observe((nint)(nuint)target.Handle).TargetVisible,
                    "Disposable peer is live and visible with exact identity");
                if (startup is not null) await startup;
                await SelectAsync("task-switcher", true);
                WidgetPresentationFrame? frame = null;
                ViewNode? button = null;
                var deadline = Environment.TickCount64 + 15000;
                do
                {
                    frame = retainedSurfaces.GetValueOrDefault("task-switcher")?.Frame;
                    var windowId = frame?.WindowPreviews.FirstOrDefault(pair => pair.Value.Handle == target.Handle &&
                        pair.Value.ProcessId == target.ProcessId && pair.Value.ProcessCreated == target.ProcessCreated).Key;
                    button = frame is null || windowId is null ? null : Walk(frame.Snapshot.Root)
                        .FirstOrDefault(node => node.ActionId == "switch." + windowId && node.IsBusy != true);
                    if (button is not null && !switching) break;
                    await Task.Delay(50);
                } while (Environment.TickCount64 < deadline);
                Check(frame is not null && button is not null, "Real Task Switcher publishes exact disposable target");
                Check(native.IsOverlayForeground && visible, "Frontend owns foreground before permission handoff");
                TaskWindowActivationRequested += OnEffect;
                if (beforeInvoke is not null) await beforeInvoke();
                await InvokeAsync(new(frame!, new(button!.ActionId!, button.Id, InputScopeId: frame!.Authority.ActiveInputScopeId)));
                if (verifyHandoff is not null) await verifyHandoff(checks);
                if (expectCancellation)
                {
                    await Task.Delay(200);
                    Check(issued is not null && visible && native.IsOverlayForeground,
                        "Cancelled handoff preserves the current overlay and foreground");
                    Check(!trace.Any(line => line.Contains("phase=request-activation", StringComparison.Ordinal)),
                        "Cancelled handoff never submits activation to the broker");
                    return;
                }
                deadline = Environment.TickCount64 + 3000;
                while (Environment.TickCount64 < deadline && (visible || WindowsTaskWindowActivation.Observe(0).Foreground != (long)target.Handle))
                    await Task.Delay(20);
                await Task.Delay(200);
                var observation = WindowsTaskWindowActivation.Observe((nint)(nuint)target.Handle);
                observed = observation;
                Check(issued is not null && !visible, "Real worker effect hides the overlay before completion");
                Check(observation.Foreground == (long)target.Handle, "Exact target owns foreground after broker completion and settles for 200ms");
                var replay = await owner!.Session.CompleteTaskActivationAsync(issued!);
                Check(replay.Result == "Rejected" && replay.Code == "missing-expired-or-consumed-effect" && replay.ProcessId != Environment.ProcessId,
                    "Real broker consumes ticket once and rejects replay");
            }
            catch (Exception exception) { error = exception.ToString(); }
            finally
            {
                TaskWindowActivationRequested -= OnEffect;
                WindowActivationDiagnostic = originalDiagnostic;
                cleanup?.Invoke();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
                File.WriteAllText(resultPath, JsonSerializer.Serialize(new { checks, observed, trace, error, passed = error is null }));
            }
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        };
        static IEnumerable<ViewNode> Walk(ViewNode node)
        {
            yield return node;
            foreach (var child in node.Children ?? []) foreach (var nested in Walk(child)) yield return nested;
        }
    }
}
