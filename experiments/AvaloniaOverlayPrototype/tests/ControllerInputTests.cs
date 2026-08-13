using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using GameBarAlternative.AvaloniaPrototype.Input;
using GameBarAlternative.AvaloniaPrototype.Navigation;
using GameBarAlternative.AvaloniaPrototype.Remote;
using GameBarAlternative.AvaloniaPrototype.Views;
using GameBarAlternative.AvaloniaPrototype.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace GameBarAlternative.AvaloniaPrototype.Tests;

[TestClass]
public sealed class ControllerInputTests
{
    [TestMethod]
    public void Processor_applies_dead_zone_repeat_edges_and_safe_device_replacement()
    {
        var processor = new ControllerStateProcessor();
        var neutral = Snapshot(connected: true);
        CollectionAssert.AreEqual(Array.Empty<SemanticInput>(), processor.Process(neutral, TimeSpan.Zero).ToArray());
        CollectionAssert.AreEqual(Array.Empty<SemanticInput>(), processor.Process(Snapshot(connected: true, x: 0.27f), TimeSpan.FromMilliseconds(1)).ToArray());
        CollectionAssert.AreEqual(new[] { SemanticInput.Right }, processor.Process(Snapshot(connected: true, x: 0.7f), TimeSpan.FromMilliseconds(2)).ToArray());
        CollectionAssert.AreEqual(Array.Empty<SemanticInput>(), processor.Process(Snapshot(connected: true, x: 0.7f), TimeSpan.FromMilliseconds(351)).ToArray());
        CollectionAssert.AreEqual(new[] { SemanticInput.Right }, processor.Process(Snapshot(connected: true, x: 0.7f), TimeSpan.FromMilliseconds(352)).ToArray());

        CollectionAssert.AreEqual(new[] { SemanticInput.Activate }, processor.Process(Snapshot(connected: true, a: true), TimeSpan.FromMilliseconds(500)).ToArray());
        CollectionAssert.AreEqual(Array.Empty<SemanticInput>(), processor.Process(Snapshot(connected: true, a: true), TimeSpan.FromMilliseconds(700)).ToArray(), "Held A must not repeat.");
        CollectionAssert.AreEqual(new[] { SemanticInput.Back }, processor.Process(Snapshot(connected: true, b: true), TimeSpan.FromMilliseconds(800)).ToArray());
        CollectionAssert.AreEqual(Array.Empty<SemanticInput>(), processor.Process(default, TimeSpan.FromMilliseconds(900)).ToArray());
        CollectionAssert.AreEqual(Array.Empty<SemanticInput>(), processor.Process(Snapshot(connected: true, slot: 1, a: true), TimeSpan.FromMilliseconds(1_000)).ToArray(), "Reconnect/device replacement must wait for neutral.");
        CollectionAssert.AreEqual(Array.Empty<SemanticInput>(), processor.Process(Snapshot(connected: true, slot: 1), TimeSpan.FromMilliseconds(1_010)).ToArray());
        CollectionAssert.AreEqual(new[] { SemanticInput.Activate }, processor.Process(Snapshot(connected: true, slot: 1, a: true), TimeSpan.FromMilliseconds(1_020)).ToArray());
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Deterministic_real_adapter_drives_router_and_resets_across_every_lifecycle_boundary()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var source = new DeterministicControllerStateSource();
            var adapter = new XInputControllerAdapter(source, enableBackgroundPolling: false);
            var emitted = new List<SemanticInput>();
            adapter.InputReceived += (_, args) => emitted.Add(args.Input);
            var window = new MainWindow(adapter) { Width = 1000, Height = 640 };
            window.Show();
            await window.NavigateAsync(PrototypeRoute.AudioMixer);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Poll(adapter, source, Snapshot(connected: true), 0);
            Poll(adapter, source, Snapshot(connected: true, down: true), 5);
            Poll(adapter, source, Snapshot(connected: true), 6);
            var page = (AudioMixerPage)window.ShellView.Navigation.CurrentPage!;
            var primary = page.FindControl<Button>("PrimaryAction")!;
            var invocationCount = 0;
            primary.Click += (_, _) => invocationCount++;

            var slider = page.FindControl<Slider>("MasterVolumeSlider")!;
            Assert.AreSame(slider, window.FocusManager?.GetFocusedElement());
            var initialSpatialTarget = FocusNavigator.FindTarget(
                window.FocusManager,
                slider,
                NavigationDirection.Down,
                window.ShellView.GetSpatialSearchRoots(slider));
            Assert.IsNotNull(initialSpatialTarget);
            var original = slider.Value;
            Poll(adapter, source, Snapshot(connected: true, x: 0.8f), 20);
            Assert.IsGreaterThan(original, slider.Value, "Controller Right must adjust a focused Slider.");
            Poll(adapter, source, Snapshot(connected: true), 30);
            var expectedSpatialTarget = FocusNavigator.FindTarget(
                window.FocusManager,
                slider,
                NavigationDirection.Down,
                window.ShellView.GetSpatialSearchRoots(slider));
            Assert.IsNotNull(expectedSpatialTarget);
            Assert.AreEqual("audio.game.volume", AutomationProperties.GetAutomationId(expectedSpatialTarget));
            Poll(adapter, source, Snapshot(connected: true, down: true), 40);
            Assert.AreEqual(SemanticInput.Down, emitted[^1], "Real processor must emit the directional edge into MainWindow.");
            Assert.AreEqual("audio.game.volume", AutomationProperties.GetAutomationId((Control)window.FocusManager!.GetFocusedElement()!),
                "Controller Down must stay in the aligned Slider column.");

            var firstFocus = window.FocusManager?.GetFocusedElement();
            Poll(adapter, source, Snapshot(connected: true, down: true), 380);
            Assert.AreSame(firstFocus, window.FocusManager?.GetFocusedElement(), "Held direction must wait for the repeat threshold.");
            Poll(adapter, source, Snapshot(connected: true, down: true), 390);
            Assert.AreNotSame(firstFocus, window.FocusManager?.GetFocusedElement(), "Held direction must repeat through actual focus after 350 ms.");

            Poll(adapter, source, Snapshot(connected: true), 1_000);
            slider.Focus();
            var beforeDeactivate = window.FocusManager?.GetFocusedElement();
            RaiseWindowLifecycle(window, "HandleDeactivated");
            Poll(adapter, source, Snapshot(connected: true, down: true), 1_500);
            Assert.AreSame(beforeDeactivate, window.FocusManager?.GetFocusedElement(), "Deactivated input must not dispatch.");
            RaiseWindowLifecycle(window, "HandleActivated");
            Poll(adapter, source, Snapshot(connected: true, down: true), 1_510);
            Assert.AreSame(beforeDeactivate, window.FocusManager?.GetFocusedElement(), "Activated must retain neutral gating after focus loss.");
            Poll(adapter, source, Snapshot(connected: true), 1_520);
            Poll(adapter, source, Snapshot(connected: true, down: true), 1_530);
            Assert.AreNotSame(beforeDeactivate, window.FocusManager?.GetFocusedElement());

            Poll(adapter, source, Snapshot(connected: true), 1_540);
            slider.Focus();
            window.Hide();
            Poll(adapter, source, Snapshot(connected: true, down: true), 2_000);
            Assert.IsNull(window.FocusManager?.GetFocusedElement(), "Hidden window must retain no focused input target.");
            window.Show();
            slider.Focus();
            var afterShowFocus = window.FocusManager?.GetFocusedElement();
            Poll(adapter, source, Snapshot(connected: true, down: true), 2_010);
            Assert.AreSame(afterShowFocus, window.FocusManager?.GetFocusedElement(), "Show must retain neutral gating after hidden cancellation.");
            Poll(adapter, source, Snapshot(connected: true), 2_020);

            await window.NavigateAsync(PrototypeRoute.AudioMixer);
            Dispatcher.UIThread.RunJobs();
            var afterRoute = window.FocusManager?.GetFocusedElement();
            Poll(adapter, source, Snapshot(connected: true, down: true), 2_100);
            Assert.AreSame(afterRoute, window.FocusManager?.GetFocusedElement(), "Route replacement must reset and neutral-gate held input.");
            Poll(adapter, source, Snapshot(connected: true), 2_110);
            Poll(adapter, source, Snapshot(connected: true, down: true), 2_120);
            Assert.AreSame(((AudioMixerPage)window.ShellView.ActivePage!).FindControl<Slider>("MasterVolumeSlider"), window.FocusManager?.GetFocusedElement(),
                "Neutral then Down must explicitly re-enter at the declared initial focus.");

            var currentMaster = (Slider)window.FocusManager!.GetFocusedElement()!;
            var beforeDisconnect = window.FocusManager?.GetFocusedElement();
            Poll(adapter, source, default, 2_200);
            Poll(adapter, source, Snapshot(connected: true, down: true), 2_210);
            Assert.AreSame(beforeDisconnect, window.FocusManager?.GetFocusedElement(), "Reconnect while held must wait for neutral.");
            Poll(adapter, source, Snapshot(connected: true), 2_220);
            Poll(adapter, source, Snapshot(connected: true, down: true), 2_230);
            Assert.AreNotSame(beforeDisconnect, window.FocusManager?.GetFocusedElement());

            Poll(adapter, source, Snapshot(connected: true), 2_240);
            currentMaster.Focus();
            var beforeReplacement = window.FocusManager?.GetFocusedElement();
            Poll(adapter, source, Snapshot(connected: true, slot: 1, down: true), 2_300);
            Assert.AreSame(beforeReplacement, window.FocusManager?.GetFocusedElement(), "Replacement device must wait for neutral.");
            Poll(adapter, source, Snapshot(connected: true, slot: 1), 2_310);
            Poll(adapter, source, Snapshot(connected: true, slot: 1, down: true), 2_320);
            Assert.AreNotSame(beforeReplacement, window.FocusManager?.GetFocusedElement());

            Poll(adapter, source, Snapshot(connected: true, slot: 1), 2_330);
            var activePrimary = ((AudioMixerPage)window.ShellView.ActivePage!).FindControl<Button>("PrimaryAction")!;
            activePrimary.Click += (_, _) => invocationCount++;
            activePrimary.Focus();
            Poll(adapter, source, Snapshot(connected: true, slot: 1, a: true), 2_340);
            Poll(adapter, source, Snapshot(connected: true, slot: 1, a: true), 2_700);
            Assert.AreEqual(1, invocationCount, "The real processor must keep controller A edge-triggered through the shared router.");
            Poll(adapter, source, Snapshot(connected: true, slot: 1), 2_710);
            Assert.IsNotNull(window.FocusManager?.GetFocusedElement() as Control, "Controller B fixture requires a focused child control.");
            Poll(adapter, source, Snapshot(connected: true, slot: 1, b: true), 2_720);
            Assert.AreEqual(PrototypeRoute.AudioMixer, window.ShellView.Navigation.CurrentRoute);
            Assert.AreSame(window.ShellView.SelectedTrayButton, window.FocusManager?.GetFocusedElement(),
                "Controller B must restore the selected tray item from a focused child.");

            await window.NavigateAsync(PrototypeRoute.GameLauncher);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Poll(adapter, source, Snapshot(connected: true, slot: 1), 3_000);
            Poll(adapter, source, Snapshot(connected: true, slot: 1, down: true), 3_010);
            Poll(adapter, source, Snapshot(connected: true, slot: 1), 3_020);
            Poll(adapter, source, Snapshot(connected: true, slot: 1, down: true), 3_030);
            var launcher = (GameLauncherPage)window.ShellView.ActivePage!;
            Assert.AreEqual("game-00001", launcher.FocusedSemanticId(window.FocusManager?.GetFocusedElement() as Control));
            Poll(adapter, source, Snapshot(connected: true, slot: 1), 3_040);
            Poll(adapter, source, Snapshot(connected: true, slot: 1, down: true), 3_050);
            Assert.AreEqual("game-00002", launcher.FocusedSemanticId(window.FocusManager?.GetFocusedElement() as Control),
                "Controller movement must use the shared semantic router over virtualized ListBox containers.");
            Poll(adapter, source, Snapshot(connected: true, slot: 1), 3_060);
            Poll(adapter, source, Snapshot(connected: true, slot: 1, a: true), 3_070);
            await Task.Delay(30);
            var remote = (FakeRemoteWidgetEndpoint)window.Composition.RemoteEndpoint;
            Assert.AreEqual(1, remote.Actions.Count);
            Assert.AreEqual(new RemoteWidgetAction(new RemoteWidgetItemId("game-00002"), GameLauncherViewModel.OpenActionId), remote.Actions[0],
                "Controller A must dispatch the exact focused semantic item through the shared router once.");
            Poll(adapter, source, Snapshot(connected: true, slot: 1, a: true), 3_500);
            await Task.Delay(20);
            Assert.AreEqual(1, remote.Actions.Count, "Held controller A must remain edge-triggered on a virtualized item.");
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Cross_source_activate_and_back_duplicates_are_suppressed()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var source = new DeterministicControllerStateSource();
            var adapter = new XInputControllerAdapter(source, enableBackgroundPolling: false);
            var window = new MainWindow(adapter);
            window.Show();
            await window.NavigateAsync(PrototypeRoute.Settings);
            Poll(adapter, source, Snapshot(connected: true), 0);
            var primary = ((Control)window.ShellView.Navigation.CurrentPage!).FindControl<Button>("PrimaryAction")!;
            var count = 0;
            primary.Click += (_, _) => count++;
            primary.Focus();

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Poll(adapter, source, Snapshot(connected: true, a: true), 10);
            Assert.AreEqual(1, count);

            Poll(adapter, source, Snapshot(connected: true), 20);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Poll(adapter, source, Snapshot(connected: true, b: true), 30);
            await Task.Delay(220);
            Assert.IsTrue(window.ShellView.ContentRegionControl.IsVisible);
            Assert.AreSame(window.ShellView.SelectedTrayButton, window.FocusManager?.GetFocusedElement(),
                "Keyboard/controller B co-report must restore tray focus exactly once.");
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Held_keyboard_enter_is_edge_triggered_and_rearms_on_key_up_or_focus_loss()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var source = new DeterministicControllerStateSource();
            var adapter = new XInputControllerAdapter(source, enableBackgroundPolling: false);
            var window = new MainWindow(adapter);
            window.Show();
            await window.NavigateAsync(PrototypeRoute.Settings);
            var primary = ((Control)window.ShellView.Navigation.CurrentPage!).FindControl<Button>("PrimaryAction")!;
            var count = 0;
            primary.Click += (_, _) => count++;
            primary.Focus();

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.AreEqual(1, count, "OS repeat KeyDown must not repeatedly activate the focused Button.");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.AreEqual(2, count, "KeyUp must rearm Enter activation.");

            RaiseWindowLifecycle(window, "HandleDeactivated");
            RaiseWindowLifecycle(window, "HandleActivated");
            primary.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.AreEqual(3, count, "Focus loss must clear a held keyboard edge whose KeyUp may be lost.");
            window.Close();
        });
    }

    private static ControllerSnapshot Snapshot(
        bool connected,
        int slot = 0,
        float x = 0,
        float y = 0,
        bool up = false,
        bool down = false,
        bool left = false,
        bool right = false,
        bool a = false,
        bool b = false) =>
        new(connected, slot, x, y, up, down, left, right, a, b);

    private static void Poll(
        XInputControllerAdapter adapter,
        DeterministicControllerStateSource source,
        ControllerSnapshot snapshot,
        double milliseconds)
    {
        source.Current = snapshot;
        adapter.PollOnce(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static void RaiseWindowLifecycle(MainWindow window, string methodName)
    {
        var method = typeof(WindowBase).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
            throw new InvalidOperationException($"Avalonia WindowBase.{methodName} was not found.");
        method.Invoke(window, null);
    }

    private sealed class DeterministicControllerStateSource : IControllerStateSource
    {
        public ControllerSnapshot Current { get; set; }

        public ControllerSnapshot Read() => Current;
    }
}
