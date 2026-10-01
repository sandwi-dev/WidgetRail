using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PackageIconValidationPage
{
    private async Task RunShutdownAsync()
    {
        var requestedStage = Environment.GetCommandLineArgs().FirstOrDefault(value => value.StartsWith("--icon-shutdown-stage=", StringComparison.Ordinal))?
            ["--icon-shutdown-stage=".Length..] ?? "render";
        var result = new JsonObject { ["pid"] = Environment.ProcessId, ["stage"] = requestedStage };
        var hideDuringShutdown = Environment.GetCommandLineArgs().Contains("--icon-shutdown-hidden");
        result["hidden"] = hideDuringShutdown;
        var cache = NativePackageIconTintCache.For(XamlRoot);
        using var callers = new CancellationTokenSource();
        var requests = new List<Task<NativePackageIconTintCache.Lease>>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? drain = null;
        var nativeWasPending = false;
        var preparationsAtStop = 0;
        var nativeAtStop = 0;
        var remainedPending = false;
        var dispatcherServiced = false;
        cache.NativeOperationStartedForTesting = (stage, pending) =>
        {
            if (stage != requestedStage || drain is not null) return;
            nativeWasPending = pending;
            preparationsAtStop = cache.PendingPreparations;
            nativeAtStop = cache.PendingNativeOperations;
            if (hideDuringShutdown) App.Window.AppWindow.Hide();
            callers.Cancel(); // Abandon consumer waits before draining the cache's work.
            drain = NativePackageIconTintCache.ShutdownAsync(DispatcherQueue);
            remainedPending = !drain.IsCompleted;
            DispatcherQueue.TryEnqueue(() => dispatcherServiced = true);
            entered.TrySetResult();
        };
        try
        {
            if (requestedStage is not ("render" or "pixels" or "surface")) throw new InvalidOperationException("Unknown icon shutdown stage.");
            for (var index = 0; index < 4; ++index)
            {
                var bytes = Encoding.UTF8.GetBytes($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 20\"><circle cx=\"10\" cy=\"10\" r=\"9\" fill=\"#{index + 1:X6}\" /></svg>");
                requests.Add(cache.AcquireAsync(new WidgetPresentationPackageIcon("shutdown", Convert.ToHexString(SHA256.HashData(bytes)), bytes), 512, callers.Token));
            }
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await drain!.WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var request in requests)
            {
                try { using var lease = await request; throw new InvalidOperationException("A retired request acquired an icon lease."); }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
            }
            Check(nativeWasPending && nativeAtStop > 0, "shutdown interrupts a genuinely pending native image operation");
            Check(preparationsAtStop > 1, "queued preparations remain tracked independently of cancelled consumers");
            Check(remainedPending && dispatcherServiced, "asynchronous drain keeps the XAML dispatcher available");
            Check(cache.PendingPreparations == 0 && cache.PendingNativeOperations == 0, "native preparation and image-operation counts reach zero before shutdown completes");
            Check(!cache.StagingAttached && cache.ReservedBytes == 0, "staging and cached surfaces retire only after the native drain");
            var rejected = false;
            try { _ = NativePackageIconTintCache.For(XamlRoot); }
            catch (ObjectDisposedException) { rejected = true; }
            Check(rejected, "late requests cannot create a new cache after dispatcher shutdown starts");
            Check(ReferenceEquals(drain, NativePackageIconTintCache.ShutdownAsync(DispatcherQueue)), "repeated shutdown shares the same completion");
            result["passed"] = true;
            status.Text = $"Passed {checks.Count} native icon shutdown checks ({requestedStage})";
        }
        catch (Exception error)
        {
            result["passed"] = false; result["error"] = error.ToString();
            status.Text = "Native icon shutdown failed: " + error.Message;
        }
        finally
        {
            cache.NativeOperationStartedForTesting = null;
            result["preparationsAtStop"] = preparationsAtStop; result["nativeAtStop"] = nativeAtStop;
            result["pendingPreparations"] = cache.PendingPreparations; result["pendingNativeOperations"] = cache.PendingNativeOperations;
            result["checks"] = new JsonArray(checks.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "package-icon-shutdown-result.json"), result.ToJsonString());
        }
    }
}
