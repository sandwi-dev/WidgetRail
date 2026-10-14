namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    internal static readonly TimeSpan HiddenMemoryDelay = Web.HostedWebViewMemoryPolicy.IdleDelay;
    private Web.HostedWebViewMemoryPolicy? memoryPolicy;
    private bool passivePinnedPresentation;
    private bool RequiresNormalMemory => visible && AttachedPopup is null && !passivePinnedPresentation;
#if ENABLE_WIDGET_VALIDATION
    internal bool ManualMemoryTargetForValidation { get; set; }
#endif

    private void UpdateMemoryPolicy()
    {
        if (retired || faulted || core is null) { memoryPolicy?.CancelPending(); return; }
#if ENABLE_WIDGET_VALIDATION
        if (ManualMemoryTargetForValidation) { memoryPolicy?.CancelPending(); return; }
#endif
        memoryPolicy ??= new(core, DispatcherQueue, error =>
            Diagnostics.FrontendFailureLog.Current.Write("browser-memory-target", null, $"Unavailable: {error.GetType().Name}"));
        memoryPolicy.Update(RequiresNormalMemory);
    }

    private void DisposeMemoryPolicy() => memoryPolicy?.Dispose();
}
