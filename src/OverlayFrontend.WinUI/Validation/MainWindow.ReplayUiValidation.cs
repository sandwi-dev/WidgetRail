using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private void EnableReplayUiValidation(Shell.OverlayShellPage page, string output, string targetFile)
    {
        var target = JsonSerializer.Deserialize<NativeWindowPreviewTarget>(File.ReadAllText(targetFile))
            ?? throw new InvalidDataException("An exact owned replay fixture target is required.");
        if (target.ClassName != "WidgetRailCaptureFixture") throw new InvalidDataException("Only the synthetic replay window is admitted.");
        var backend = new GuardedReplayUiBackend(target);
        page.ReplayBackendFactory = backend;
        page.EnableReplayUiValidation(output, backend, async verified =>
        {
            if (replaySaveCommand is null) throw new InvalidOperationException("Production replay shortcut handler is not attached.");
            var root = page.OptionsForReplayValidation.SettingsRoot;
            var current = (await new PlatformSettingsStore(new(root)).LoadAsync()).Controllers.EffectiveReplaySaveShortcut;
            var shortcut = current with { Enabled = true, Key = 0x84, Modifiers = OverlayShortcutModifiers.Control | OverlayShortcutModifiers.Alt };
            var problem = await SaveKeyboardShortcutAsync(root, current, shortcut, default, HostKeyboardShortcutKind.SaveReplay);
            if (problem is not null) throw new InvalidOperationException(problem);
            try
            {
                if (!ValidationOwnsForeground || replaySaveShortcut?.IsRegistered != true) throw new InvalidOperationException("Fixture does not own the native shortcut.");
                InjectKey(0x11, false); InjectKey(0x12, false); InjectKey(0x84, false);
                InjectKey(0x84, true); InjectKey(0x12, true); InjectKey(0x11, true);
                await verified();
            }
            finally
            {
                InjectKey(0x84, true); InjectKey(0x12, true); InjectKey(0x11, true);
                _ = await SaveKeyboardShortcutAsync(root, shortcut, current, default, HostKeyboardShortcutKind.SaveReplay);
            }
        });
    }
}

internal sealed class GuardedReplayUiBackend(NativeWindowPreviewTarget expected) : IReplayCaptureBackendFactory
{
    internal int Starts, Saves, CompletedSaves, Disposals;
    internal NativeWindowPreviewTarget? LastTarget;
    public async Task<IReplayCaptureBackend> StartAsync(NativeWindowPreviewTarget target, CaptureReplaySettings settings, CancellationToken token)
    {
        if (target != expected) throw new InvalidOperationException("Fixture refused to record a non-owned application.");
        LastTarget = target;
        var backend = await new NativeReplayCaptureBackendFactory(CaptureRecordingResolver.ResolveAsync).StartAsync(target, settings, token);
        Interlocked.Increment(ref Starts); return new Guard(this, backend);
    }
    private sealed class Guard(GuardedReplayUiBackend owner, IReplayCaptureBackend backend) : IReplayCaptureBackend
    {
        private int disposed;
        public CaptureRecordingConfiguration? ResolvedConfiguration=>backend.ResolvedConfiguration;
        public Task<ReplayBackendState> ReadStateAsync(CancellationToken token) => backend.ReadStateAsync(token);
        public void RenewAuthority() => backend.RenewAuthority();
        public Task<ReplayBackendSaveResult> SaveAsync(string path, int seconds, CancellationToken token)
        {
            Interlocked.Increment(ref owner.Saves);
            return Observe(backend.SaveAsync(path, seconds, token)); // Preserve synchronous native admission before Stop.
        }
        private async Task<ReplayBackendSaveResult> Observe(Task<ReplayBackendSaveResult> saving)
        { var result = await saving; Interlocked.Increment(ref owner.CompletedSaves); return result; }
        public Task StopAsync(CancellationToken token) => backend.StopAsync(token);
        public async ValueTask DisposeAsync()
        { if (Interlocked.Exchange(ref disposed, 1) == 0) { await backend.DisposeAsync(); Interlocked.Increment(ref owner.Disposals); } }
    }
}
