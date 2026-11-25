using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI
{
    public sealed partial class MainWindow
    {
        private void EnableHiddenSlotValidation(Shell.OverlayShellPage page, string report)
        {
            var started = false;
            page.Loaded += async (_, _) =>
            {
                if (started) return;
                started = true;
                var checks = new List<string>();
                Exception? failure = null;
                try
                {
                    await page.WaitForNativeStartupAsync().WaitAsync(TimeSpan.FromSeconds(40));
                    await page.PrepareHiddenSlotFixtureAsync();
                    Check("Capture settings sections expand and collapse with controller focus, then Back returns to the gallery");
                    var root = page.OptionsForReplayValidation.SettingsRoot;
                    var directions = new[] { HostKeyboardShortcutKind.TrayUp, HostKeyboardShortcutKind.TrayLeft, HostKeyboardShortcutKind.TrayRight, HostKeyboardShortcutKind.TrayDown };
                    foreach (var kind in directions)
                    {
                        var before = (await new PlatformSettingsStore(new(root)).LoadAsync()).Controllers.GetShortcut(kind);
                        var issue = await SaveKeyboardShortcutAsync(root, before, before with { Enabled = true, Key = Key(kind),
                            Modifiers = OverlayShortcutModifiers.Control | OverlayShortcutModifiers.Alt }, default, kind);
                        if (issue is not null) throw new InvalidOperationException(issue);
                    }
                    await Wait(() => page.FocusFixtureWidget());
                    var beforeShortcut = FocusManager.GetFocusedElement(page.XamlRoot);
                    Fire(HostKeyboardShortcutKind.TrayUp);
                    await Wait(() => page.HiddenFixtureIdle && page.FixtureMarked(1));
                    if (!ReferenceEquals(beforeShortcut, FocusManager.GetFocusedElement(page.XamlRoot)))
                        throw new InvalidOperationException("Visible keyboard shortcut displaced widget focus.");
                    Check("Visible global shortcut ran while preserving focus inside the widget");
                    var fixturePid = Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--hidden-fixture-pid");
                    var delayedFixture = Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--hidden-delay-peer");
                    if (fixturePid is not null)
                        await ActivateFixture(fixturePid);
                    HideOverlayImmediately();
                    await Task.Delay(250);
                    var foreground = WindowsTaskWindowActivation.Observe(0).Foreground;
                    Fire(HostKeyboardShortcutKind.TrayUp);
                    await Wait(() => page.HiddenFixtureRunning);
                    Fire(HostKeyboardShortcutKind.TrayUp);
                    await Wait(() => !page.HiddenFixtureRunning && page.FixtureMarked(2));
                    Check("Two presses during one in-flight command reached the background worker once");
                    var approvedReplay = Environment.GetCommandLineArgs().Contains("--hidden-fixture-approved");
                    // Revoking capture permission also invalidates existing replay approvals.
                    if (!approvedReplay)
                    {
                        var consent = new ConsentStore(Path.Combine(root, "consent"));
                        var actor = new BrokerWidgetIdentity("example.capture-replay-fixture", "example.fixture", "capture-replay.instance");
                        await consent.SetDecisionAsync(actor, PlatformCapabilities.WindowCaptureV1, ConsentDecision.Deny);
                        Fire(HostKeyboardShortcutKind.TrayLeft);
                        await Wait(() => page.HiddenFixtureNotice("Open WidgetRail to review permission for this action."));
                        await Wait(() => page.HiddenFixtureIdle);
                        await consent.SetDecisionAsync(actor, PlatformCapabilities.WindowCaptureV1, ConsentDecision.Grant);
                        Check("Denied capture permission kept the overlay hidden and explained the required permission review");
                    }
                    Fire(HostKeyboardShortcutKind.TrayLeft);
                    await Wait(() => page.HiddenFixtureNotice("Open WidgetRail to confirm taking this screenshot."));
                    Check("Unremembered screenshot confirmation explains why the overlay is needed");
                    if (fixturePid is not null)
                    {
                        var store = new PlatformSettingsStore(new(root));
                        await store.UpdateAsync(s => s with { CaptureConfirmations = s.CaptureConfirmations.Remember(
                            "capture-replay-fixture", false, true, true, s.CaptureConfirmations.ResetVersion) }, default);
                        await Wait(() => page.HiddenFixtureIdle);
                        var started = Environment.TickCount64;
                        Fire(HostKeyboardShortcutKind.TrayLeft);
                        if (delayedFixture is not null)
                        {
                            await Task.Delay(1100);
                            if (Directory.Exists(Path.Combine(root,"captures")) && Directory.EnumerateFiles(Path.Combine(root,"captures"), "*.png", SearchOption.AllDirectories).Any())
                                throw new InvalidOperationException("Screenshot saved before the preparation time elapsed");
                            await ActivateFixture(delayedFixture);
                            foreground = WindowsTaskWindowActivation.Observe(0).Foreground;
                        }
                        await Wait(() => Directory.Exists(Path.Combine(root, "captures")) &&
                            Directory.EnumerateFiles(Path.Combine(root, "captures"), "*.png", SearchOption.AllDirectories).Any());
                        Check("Remembered screenshot confirmation saved a PNG while the fixture application retained foreground");
                        if (delayedFixture is not null)
                        {
                            var png = Directory.EnumerateFiles(Path.Combine(root,"captures"), "*.png", SearchOption.AllDirectories).Single();
                            var bytes = await File.ReadAllBytesAsync(png);
                            if (Environment.TickCount64 - started < 3000 || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)) != 800 ||
                                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4)) != 450)
                                throw new InvalidOperationException("The completed screenshot did not capture the second foreground window after the configured delay");
                            Check("Three-second preparation saved the 800 by 450 window selected during countdown; the overlay stayed hidden");
                        }
                    }
                    await Wait(() => page.HiddenFixtureIdle);
                    Fire(HostKeyboardShortcutKind.TrayRight);
                    await Wait(() => page.HiddenFixtureNotice("Start replay recording before saving a replay."));
                    Check("Save replay while off displayed the recorder-specific reason");
                    await Wait(() => page.HiddenFixtureIdle);
                    page.SetHiddenFixtureCaptureAction("start-replay");
                    Fire(HostKeyboardShortcutKind.TrayRight);
                    if (approvedReplay)
                    {
                        await Wait(() => page.HiddenFixtureReadyToSave, 45);
                        await Wait(() => page.HiddenFixtureIdle);
                        page.SetHiddenFixtureCaptureAction("save-replay");
                        Fire(HostKeyboardShortcutKind.TrayRight);
                        await Wait(() => SavedClips() > 0, 45);
                        await Wait(() => page.HiddenFixtureIdle);
                        page.SetHiddenFixtureCaptureAction("stop-replay");
                        Fire(HostKeyboardShortcutKind.TrayRight);
                        await Wait(() => page.HiddenFixtureStopped, 30);
                        Check("Approved foreground application started replay, saved the buffer and stopped with the overlay hidden");
                    }
                    else
                    {
                        await Wait(() => page.HiddenFixtureNotice("Open WidgetRail to choose the application to record."));
                        Check("Manual replay start explains that an application chooser needs the overlay");
                    }
                    await Wait(() => page.HiddenFixtureIdle);
                    page.SetHiddenFixtureCaptureAction("record-clip");
                    Fire(HostKeyboardShortcutKind.TrayRight);
                    await Wait(() => page.HiddenFixtureNotice("Open WidgetRail to confirm recording this clip."));
                    Check("One-off clips honor the independent recording confirmation");
                    if (fixturePid is not null)
                    {
                        var before = SavedClips();
                        var store = new PlatformSettingsStore(new(root));
                        await store.UpdateAsync(s => s with { CaptureConfirmations = s.CaptureConfirmations.Remember(
                            "capture-replay-fixture", true, true, true, s.CaptureConfirmations.ResetVersion) }, default);
                        await Wait(() => page.HiddenFixtureIdle);
                        Fire(HostKeyboardShortcutKind.TrayRight);
                        await Wait(() => SavedClips() > before, 90);
                        Check("Remembered recording confirmation saved a one-off MP4 without opening the overlay");
                    }
                    page.SetHiddenFixtureDownAssigned(true);
                    Fire(HostKeyboardShortcutKind.TrayDown);
                    await Wait(() => page.HiddenFixtureNotice("The fixture player disconnected."));
                    page.SetHiddenFixtureDownAssigned(false);
                    Fire(HostKeyboardShortcutKind.TrayDown);
                    await Wait(() => page.HiddenFixtureNotice("Assign an action to Down in tray quick actions."));
                    Check("The same Down shortcut followed its current assignment and reported a cleared slot");
                    if (AppWindow.IsVisible || WindowsTaskWindowActivation.Observe(0).Foreground != foreground)
                        throw new InvalidOperationException("The hidden test changed foreground ownership.");
                    Check("The overlay stayed hidden and the foreground window remained unchanged");
                    void Fire(HostKeyboardShortcutKind kind)
                    {
                        if (KeyboardRegistration(kind)?.IsRegistered != true) throw new InvalidOperationException("Shortcut unavailable.");
                        var key = Key(kind);
                        InjectKey(0x11,false); InjectKey(0x12,false); InjectKey(key,false); InjectKey(key,true); InjectKey(0x12,true); InjectKey(0x11,true);
                    }
                    static ushort Key(HostKeyboardShortcutKind kind) => kind switch {
                        HostKeyboardShortcutKind.TrayUp => 0x7C, HostKeyboardShortcutKind.TrayLeft => 0x7D,
                        HostKeyboardShortcutKind.TrayRight => 0x7E, _ => 0x7F };
                    int SavedClips() => Directory.EnumerateFiles(Path.Combine(root, "library"), "*.json").Count(path =>
                    {
                        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _)) return false;
                        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                        return document.RootElement.GetProperty("Item").GetProperty("Media").GetProperty("ContentType").GetString() == "video/mp4";
                    });
                    static async Task ActivateFixture(string pid)
                    {
                        using var peer = System.Diagnostics.Process.GetProcessById(int.Parse(pid));
                        var windows = new WindowsTaskWindowActivation();
                        var target = new TaskWindowTarget((ulong)peer.MainWindowHandle, (uint)peer.Id,
                            (ulong)peer.StartTime.ToUniversalTime().ToFileTimeUtc(), "WidgetRailCaptureFixture");
                        if (peer.ProcessName != "WindowCaptureFixture" || !windows.IsCurrent(target))
                            throw new InvalidOperationException($"Fixture identity changed: {target}");
                        await Wait(() => windows.IsTargetForeground(target) || windows.RequestActivation(target) && windows.IsTargetForeground(target));
                    }
                    static async Task Wait(Func<bool> ok, int seconds = 15) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds)); while (!ok()) await Task.Delay(25,timeout.Token); }
                    void Check(string value) => checks.Add(value);
                }
                catch (Exception error) { failure = new InvalidOperationException(page.HiddenFixtureState, error); }
                finally { await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { passed = failure is null, checks, failure = failure?.ToString() })); }
            };
        }
    }
}

namespace WidgetRail.OverlayFrontend.WinUI.Shell
{
    internal sealed partial class OverlayShellPage
    {
        internal async Task PrepareHiddenSlotFixtureAsync()
        {
            if (owner is null || !File.Exists(Path.Combine(options.InstallationRoot,"runtime","Bridge","replay-ui-fixture.marker")) ||
                Path.GetFullPath(options.SettingsRoot) == PlatformSettingsPaths.CreateDefault().RootDirectory)
                throw new InvalidOperationException("Expected an isolated replay fixture.");
            notificationCenter!.BannerRequested += (entry, _) => hiddenFixtureNotices.Add(entry.Body ?? "");
            App.Window!.Activate();
            await Wait(() => App.Window is MainWindow { ValidationOwnsForeground: true });
            await SelectAsync("capture-replay-fixture", true);
            await Wait(() => surface?.CurrentBinding is { } binding && Nodes(binding.View.Root).Any(node =>
                node.Id == "capture-library.more" && node.SelectOptions.Any(choice => choice.Id == "settings" && !choice.IsDisabled)));
            await Choose("capture-library.more", "settings");
            async Task Choose(string controlId, string optionId)
            {
                await FocusAndActivate(controlId);
                var option = "Widget." + controlId + ".Option." + optionId;
                Control? PopupChoice() => VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).SelectMany(popup => Descendants(popup.Child))
                    .OfType<Control>().FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == option);
                await Wait(() => PopupChoice() is { IsLoaded:true, IsEnabled:true });
                PopupChoice()!.Focus(FocusState.Keyboard);
                await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), PopupChoice()));
                await Press(ControllerButton.A);
            }
            const string replayHeader = "capture-library.section.Replay.header";
            await FocusAndActivate(replayHeader);
            await Wait(() => FindIntroductionTarget(surface!, "capture-library.approvals.open") is Button { IsLoaded:true });
            await FocusAndActivate(replayHeader);
            await Wait(() => FindIntroductionTarget(surface!, "capture-library.approvals.open") is null);
            if (FocusManager.GetFocusedElement(XamlRoot) is not DependencyObject focused || AutomationProperties.GetAutomationId(focused) != "Widget." + replayHeader)
                throw new InvalidOperationException("Collapsing replay settings lost its header focus.");
            if (FrontendArguments.Value(Environment.GetCommandLineArgs(), "--hidden-delay-peer") is not null)
            {
                await FocusAndActivate("capture-library.section.Preparation.header");
                await Choose("capture-library.setting.image.delay", "image.delay.3");
                await Wait(() => surface?.CurrentBinding is { } snapshot && Nodes(snapshot.View.Root).Any(node =>
                    node.Id == "capture-library.setting.image.delay" && node.IsBusy != true &&
                    node.SelectOptions.Any(choice => choice.Id == "image.delay.3" && choice.IsSelected)));
            }
            await Press(ControllerButton.B);
            await Wait(() => MainFocusEnabled && !switching && surface?.CurrentBinding is { } gallery &&
                Nodes(gallery.View.Root).Any(node => node.Shortcuts?.Any(shortcut => shortcut.Button == ControllerButton.LeftStick) == true) &&
                !surface.HasTransientControl && FindIntroductionTarget(surface, "capture-library.search") is { IsLoaded:true } &&
                FocusManager.GetFocusedElement(XamlRoot) is DependencyObject current &&
                AutomationProperties.GetAutomationId(current).StartsWith("Widget.capture-library.", StringComparison.Ordinal));
            await Press(ControllerButton.LeftStick);
            try
            {
                await Wait(() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject search &&
                    AutomationProperties.GetAutomationId(search) == "Widget.capture-library.search");
            }
            catch (OperationCanceledException)
            {
                var focusedId = FocusManager.GetFocusedElement(XamlRoot) is DependencyObject item ? AutomationProperties.GetAutomationId(item) : null;
                throw new InvalidOperationException($"L3 from gallery focused {focusedId}; scope={surface?.CurrentBinding?.View.ActiveInputScopeId}.");
            }
            async Task FocusAndActivate(string id)
            {
                await Wait(() => MainFocusEnabled && surface?.CurrentBinding is { } binding &&
                    Nodes(binding.View.Root).Any(node => node.Id == id && node.IsDisabled != true && node.IsBusy != true) &&
                    FindIntroductionTarget(surface, id) is Control { IsLoaded: true, IsEnabled: true, ActualWidth: > 0 } &&
                    surface.FocusHostTarget(id));
                await Wait(() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject current && AutomationProperties.GetAutomationId(current) == "Widget." + id);
                await Press(ControllerButton.A);
            }
            async Task Press(ControllerButton button)
            {
                Receive(new WidgetRail.OverlayPlatformClient.ControllerFrame { Connected = 1 });
                await RouteButtonAsync(button, ControllerEventPhase.Pressed);
                await RouteButtonAsync(button, ControllerEventPhase.Released);
            }
            static async Task Wait(Func<bool> ready)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (!ready()) await Task.Delay(30,timeout.Token);
            }
            await SelectAsync("replay-approval");
            var slots = preferences.TrayActions.Assign(TrayDirection.Up, new("replay-approval","mark","example.fixture",Package:"example.replay-approval"))
                .Assign(TrayDirection.Left,new("capture-replay-fixture","screenshot","example.fixture",Package:"example.capture-replay-fixture"))
                .Assign(TrayDirection.Right,new("capture-replay-fixture","save-replay","example.fixture",Package:"example.capture-replay-fixture"));
            preferences = preferences with { TrayActions = slots };
        }

        internal bool FocusFixtureWidget() => interactive && MainFocusEnabled && surface?.FocusHostTarget("approval.enable") == true;
        internal bool FixtureMarked(int count) => owner?.Session.GetState("replay-approval")?.LastGood?.Snapshot.Root.Children.Any(node => node.Text == "Marked " + count) == true;
        internal bool HiddenFixtureRunning => runningFavorites.ContainsKey(TrayDirection.Up);
        internal bool HiddenFixtureIdle => runningFavorites.Count == 0;
        internal bool HiddenFixtureReadyToSave => ReplayStatusForHost is { Phase: CaptureReplayPhase.Recording, BufferedDurationSeconds: >= 2 };
        internal bool HiddenFixtureStopped => ReplayStatusForHost is { Phase: CaptureReplayPhase.Off };
        private readonly List<string> hiddenFixtureNotices = [];
        internal string HiddenFixtureState => $"Replay={ReplayStatusForHost?.Phase}/{ReplayStatusForHost?.Issue}, busy={runningFavorites.Count}, " +
            "notifications=" + string.Join(" | ", hiddenFixtureNotices);
        internal void SetHiddenFixtureCaptureAction(string action) => preferences = preferences with
        {
            TrayActions = preferences.TrayActions.Assign(TrayDirection.Right,
                new("capture-replay-fixture", action, "example.fixture", Package:"example.capture-replay-fixture"))
        };
        internal void SetHiddenFixtureDownAssigned(bool assigned) => preferences = preferences with
        {
            TrayActions = preferences.TrayActions.Assign(TrayDirection.Down,
                assigned ? new("replay-approval", "reject", "example.fixture", Package: "example.replay-approval") : null)
        };
        internal bool HiddenFixtureNotice(string body) => notificationBannerState.Visible.Any(slot => slot.Entry.WasShown && slot.Entry.Body == body);
    }
}
