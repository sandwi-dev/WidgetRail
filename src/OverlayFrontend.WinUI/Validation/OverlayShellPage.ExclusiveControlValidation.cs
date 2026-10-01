using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.OverlayFrontend.WinUI.Validation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Real Settings worker, broker, preference storage and UI controls; only the
    // native controller boundary is fake. Never connects a physical input stream.
    internal void EnableExclusiveControlValidation(string path, PlatformInputPump mockPump, ReplayNativePlatform backend)
    {
        var resultDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var profile = Path.GetFullPath(options.SettingsRoot);
        if (!profile.StartsWith(resultDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile, PlatformSettingsPaths.CreateDefault().RootDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Exclusive-control validation requires an isolated SettingsRoot below its result directory.");
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            var checks = new List<string>();
            var store = new PlatformSettingsStore(new(profile));
            PlatformSettingsDocument? original = null;
            Exception? failure = null;
            Exception? pumpFailure = null;
            mockPump.Failed += error => pumpFailure = error;
            try
            {
                original = await store.LoadAsync();
                Check(!original.Controllers.ExclusiveControl, "isolated profile starts with exclusive control off");
                if (startup is not null) await startup;
                await SelectAsync("settings", true);
                await Until(() => Find("Widget.category.controllers") is Control { IsEnabled: true } && MainFocusEnabled);
                Invoke("Widget.category.controllers");
                await Until(() => Find("Widget.controllers.exclusive-control") is Control { IsEnabled: true } && StatusContains("Off"));
                Check(TextContains("Widget.controllers.hidhide.status", "Ready") && TextContains("Widget.controllers.vigem.status", "Ready"),
                    "actual Controllers page displays native prerequisite readiness and actionable exclusive control");
                Check(backend.ExclusiveControlCalls == 1 && backend.ExclusiveControlRequests[0] == 0,
                    "initial off receipt checks native recovery of policy left by a crashed owner");
                backend.ControllerPrerequisiteFlags = 0;
                await Until(() => Find("Widget.controllers.exclusive-control") is Control { IsEnabled: false } &&
                    TextContains("Widget.controllers.hidhide.status", "Unavailable") && TextContains("Widget.controllers.vigem.status", "Unavailable"));
                Check(backend.ExclusiveControlCalls == 1, "missing prerequisites disable enablement without additional policy changes");
                backend.ControllerPrerequisiteFlags = 7;
                await Until(() => Find("Widget.controllers.exclusive-control") is Control { IsEnabled: true });

                Invoke("Widget.controllers.exclusive-control");
                await Settled(true, PlatformControllerControlState.Active, 1);
                await Until(() => StatusContains("Active"));
                var enabled = (await store.LoadAsync()).Controllers;
                Check(enabled.Revision > original.Controllers.Revision, "Settings persists a new enable revision through production broker");
                mockPump.ApplyControllerControlPreference(enabled);
                await Task.Delay(1200);
                Check(backend.ExclusiveControlCalls == 2, "repeated status exchanges and duplicate revisions do not reapply enable");
                Invoke("Widget.controllers.open-shortcut");
                await UntilAsync(async () => (await store.LoadAsync()).Controllers.OpenShortcut != enabled.OpenShortcut);
                await Task.Delay(1200);
                Check(backend.ExclusiveControlCalls == 2, "changing the ordinary opening shortcut does not reapply isolation");

                await Until(() => Find("Widget.controllers.exclusive-control") is Control { IsEnabled: true });
                Invoke("Widget.controllers.exclusive-control");
                await Settled(false, PlatformControllerControlState.Off, 2);
                await Until(() => StatusContains("Off"));
                Check(backend.ExclusiveControlRequests.SequenceEqual(new uint[] { 0, 1, 0 }), "actual switch enables and disables the same native owner exactly once");

                backend.ExclusiveControlResult = PlatformStatus.ControllerIsolationUnavailable;
                backend.FailedControlState = PlatformControllerControlState.Failed;
                Invoke("Widget.controllers.exclusive-control");
                await Settled(true, PlatformControllerControlState.Failed, 3);
                await Until(() => StatusContains("could not start") && Find("Widget.controllers.exclusive-control") is Control { IsEnabled: true });
                var failedRevision = (await store.LoadAsync()).Controllers.Revision;
                Check(pumpFailure is null && backend.DestroyCalls == 0, "expected setup failure retains the controller owner and Settings retry control");
                backend.ExclusiveControlResult = PlatformStatus.Ok;
                Invoke("Widget.controllers.exclusive-control");
                await Settled(true, PlatformControllerControlState.Active, 4);
                Check((await store.LoadAsync()).Controllers.Revision > failedRevision, "explicit Settings retry creates a new revision even while intent remains enabled");
                await Until(() => StatusContains("Active"));
                Invoke("Widget.controllers.exclusive-control");
                await Settled(false, PlatformControllerControlState.Off, 5);
                await Until(() => StatusContains("Off"));

                backend.ExclusiveControlResult = PlatformStatus.ControllerIsolationUnavailable;
                backend.FailedControlState = PlatformControllerControlState.RecoveryRequired;
                Invoke("Widget.controllers.exclusive-control");
                await Settled(true, PlatformControllerControlState.RecoveryRequired, 6);
                await Until(() => StatusContains("needs to be restored") && Find("Widget.controllers.refresh") is Control { IsEnabled: true });
                mockPump.SetVisible(true);
                var blockedReads = backend.ReadControllerCalls;
                await Task.Delay(100);
                Check(mockPump.PrepareShow() && backend.ReadControllerCalls == blockedReads && backend.DestroyCalls == 0,
                    "recovery-required owner remains alive and keyboard-openable while polling is blocked");
                Check(mockPump.AcquireForeground(), "native foreground acquisition remains available during controller recovery");
                var recoveryRevision = (await store.LoadAsync()).Controllers.Revision;
                Invoke("Widget.controllers.refresh");
                await Settled(false, PlatformControllerControlState.Off, 7);
                await Until(() => backend.ReadControllerCalls > blockedReads && StatusContains("Off"));
                Check((await store.LoadAsync()).Controllers.Revision > recoveryRevision && pumpFailure is null,
                    "Restore controller access persists a new off revision and resumes ordinary polling");
                backend.PrepareVisibleResult = PlatformStatus.ControllerIsolationUnavailable;
                Check(!mockPump.PrepareShow() && backend.DestroyCalls == 0, "neutral-entry timeout cancels opening without disposing the owner");
                backend.PrepareVisibleResult = PlatformStatus.Ok;
                Check(mockPump.PrepareShow() && pumpFailure is null, "a later opening can succeed on the retained owner");
                var coldBackend = new ReplayNativePlatform { ControllerPrerequisiteFlags = 7, ExclusiveControlResult = PlatformStatus.Ok };
                using (var cold = new PlatformInputPump(DispatcherQueue, (nint)hostWindow, coldBackend))
                {
                    var preference = new TaskCompletionSource<ControllerSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
                    cold.InitializeControllerSettings(() => preference.Task);
                    var report = cold.ReadControllerControlAsync(lifetime.Token);
                    Check(!report.IsCompleted, "first broker status waits for the saved startup preference");
                    preference.SetResult(new() { ExclusiveControl = true, Revision = 4, OpenShortcut = ControllerOpenShortcut.Guide });
                    var status = await report;
                    Check(!cold.IsActive && status.State == PlatformDiagnostics.ControllerControlState.Active &&
                        coldBackend.ExclusiveControlRequests.SequenceEqual(new uint[] { 1 }),
                        "saved exclusive control initializes once while the overlay and widget service stay unopened");
                }
                Check(coldBackend.DestroyCalls == 1, "hidden-startup controller owner retires through normal cleanup");
            }
            catch (Exception error) { failure = error; }
            finally
            {
                mockPump.SetVisible(false);
                if (original is not null)
                {
                    try { await store.ReplaceAsync(original); }
                    catch (Exception error) { failure ??= error; }
                }
                backend.ExclusiveControlResult = PlatformStatus.Ok;
                backend.SetExclusiveControl(1, 0); // Explicitly fake boundary cleanup only.
                Directory.CreateDirectory(resultDirectory);
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    passed = failure is null, checks, error = failure?.ToString(),
                    requests = backend.ExclusiveControlRequests, nativeBackend = nameof(ReplayNativePlatform),
                    profile, physicalInput = false
                }));
            }

            void Check(bool condition, string description)
            {
                if (!condition) throw new InvalidOperationException(description);
                checks.Add(description);
            }
            async Task Settled(bool requested, PlatformControllerControlState state, int calls)
            {
                ++calls; // First Off receipt performs startup recovery before these user actions.
                await UntilAsync(async () => (await store.LoadAsync()).Controllers.ExclusiveControl == requested &&
                    backend.ControlState == state && backend.ExclusiveControlCalls >= calls);
                Check(backend.ExclusiveControlCalls == calls, $"request {calls} applied once and reported {state}");
            }
            FrameworkElement? Find(string id) => surface is null ? null : Descendants(surface).OfType<FrameworkElement>()
                .FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
            bool TextContains(string id, string text) => Find(id) is TextBlock block && block.Text.Contains(text, StringComparison.OrdinalIgnoreCase);
            bool StatusContains(string text) => TextContains("Widget.controllers.status", text);
            void Invoke(string id)
            {
                var element = Find(id) as Control ?? throw new InvalidOperationException("Missing control " + id);
                if (!element.IsEnabled) throw new InvalidOperationException("Disabled control " + id);
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(element);
                if (peer?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
                    throw new InvalidOperationException("Missing invoke pattern " + id);
                invoke.Invoke();
            }
            Task Until(Func<bool> condition) => UntilAsync(() => Task.FromResult(condition()));
            async Task UntilAsync(Func<Task<bool>> condition)
            {
                var deadline = Environment.TickCount64 + 20000;
                while (!await condition())
                {
                    if (RecoveryVisible || validationFailure is not null || pumpFailure is not null)
                        throw new InvalidOperationException("Exclusive-control validation failed", validationFailure ?? pumpFailure);
                    if (Environment.TickCount64 > deadline)
                        throw new TimeoutException($"Exclusive-control validation did not settle; state={backend.ControlState}, calls={backend.ExclusiveControlCalls}");
                    await Task.Delay(25);
                }
            }
        };
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var node in Descendants(VisualTreeHelper.GetChild(root, i))) yield return node;
        }
    }
}
