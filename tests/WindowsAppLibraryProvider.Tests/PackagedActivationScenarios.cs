using System.Runtime.InteropServices;
using WidgetRail.WindowsAppLibraryProvider;

internal static class PackagedActivationScenarios
{
    internal static Task ForegroundTransferIsOptional()
    {
        foreach (var result in new[] { 0, unchecked((int)0x80004002), unchecked((int)0x80070005) })
        {
            var offers = 0; var activations = 0;
            WindowsPackagedAppLauncher.ActivateWithForegroundOffer(
                () => { ++offers; return result; },
                () => { ++activations; return 0; }, CancellationToken.None);
            if (offers != 1 || activations != 1) throw new Exception("Foreground offer prevented or replayed activation");
        }
        return Task.CompletedTask;
    }

    internal static Task ActivationFailureIsAuthoritative()
    {
        const int failed = unchecked((int)0x80270254);
        var activations = 0;
        try
        {
            WindowsPackagedAppLauncher.ActivateWithForegroundOffer(
                () => unchecked((int)0x80004002), () => { ++activations; return failed; }, CancellationToken.None);
            throw new Exception("Failed activation was reported as success");
        }
        catch (COMException error) when (error.HResult == failed)
        {
            if (activations != 1) throw new Exception("Failed activation was retried");
        }
        return Task.CompletedTask;
    }

    internal static Task CancellationPreventsActivation()
    {
        foreach (var cancelBeforeOffer in new[] { true, false })
        {
            using var cancellation = new CancellationTokenSource();
            var offers = 0; var activations = 0;
            if (cancelBeforeOffer) cancellation.Cancel();
            try
            {
                WindowsPackagedAppLauncher.ActivateWithForegroundOffer(
                    () => { ++offers; cancellation.Cancel(); return 0; },
                    () => { ++activations; return 0; }, cancellation.Token);
                throw new Exception("Canceled launch completed");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            if (activations != 0 || offers != (cancelBeforeOffer ? 0 : 1))
                throw new Exception("Canceled launch crossed the activation boundary");
        }
        return Task.CompletedTask;
    }
}
