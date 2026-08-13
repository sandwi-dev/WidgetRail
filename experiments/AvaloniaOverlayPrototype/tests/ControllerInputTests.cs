using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using GameBarAlternative.AvaloniaPrototype.Input;
using GameBarAlternative.AvaloniaPrototype.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
    public async Task Controller_and_keyboard_share_tray_slider_activate_and_child_back_routing()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var adapter = new FakeControllerAdapter();
            var window = new MainWindow(adapter);
            window.Show();
            await window.NavigateAsync(PrototypeRoute.Settings);
            var page = (Control)window.ShellView.Navigation.CurrentPage!;
            var primary = page.FindControl<Button>("PrimaryAction")!;
            var invocationCount = 0;
            primary.Click += (_, _) => invocationCount++;
            primary.Focus();

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.AreEqual(1, invocationCount, "Tunnel-routed Enter must invoke exactly once.");
            await Task.Delay(90);
            adapter.Emit(SemanticInput.Activate);
            await PumpAsync();
            Assert.AreEqual(2, invocationCount, "Controller A must use the same one-shot activation path.");

            var slider = page.FindControl<Slider>("InterfaceScaleSlider")!;
            slider.Focus();
            var original = slider.Value;
            adapter.Emit(SemanticInput.Right);
            await PumpAsync();
            Assert.IsGreaterThan(original, slider.Value, "Controller Right must adjust a focused Slider.");
            adapter.Emit(SemanticInput.Down);
            await PumpAsync();
            Assert.AreNotSame(slider, window.FocusManager?.GetFocusedElement(), "Controller Down must leave a focused Slider.");

            slider.Focus();
            adapter.Emit(SemanticInput.Back);
            await Task.Delay(220);
            Assert.IsFalse(window.ShellView.ContentRegionControl.IsVisible, "Controller B must work from a focused child control.");

            window.ShellView.TrayButtons[0].Focus();
            adapter.Emit(SemanticInput.Left);
            await Task.Delay(260);
            Assert.AreEqual(PrototypeRoute.GameLauncher, window.ShellView.Navigation.CurrentRoute, "Tray Left must wrap and navigate without Enter.");
            Assert.AreSame(window.ShellView.TrayButtons[3], window.FocusManager?.GetFocusedElement());
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            await Task.Delay(260);
            Assert.AreEqual(PrototypeRoute.Settings, window.ShellView.Navigation.CurrentRoute, "Tray Right must wrap and share keyboard routing.");

            window.Hide();
            Assert.IsFalse(adapter.Active, "Hidden windows must stop controller polling dispatch.");
            window.Close();
            Assert.IsTrue(adapter.Disposed);
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Cross_source_activate_and_back_duplicates_are_suppressed()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var adapter = new FakeControllerAdapter();
            var window = new MainWindow(adapter);
            window.Show();
            await window.NavigateAsync(PrototypeRoute.Settings);
            var primary = ((Control)window.ShellView.Navigation.CurrentPage!).FindControl<Button>("PrimaryAction")!;
            var count = 0;
            primary.Click += (_, _) => count++;
            primary.Focus();

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            adapter.Emit(SemanticInput.Activate);
            await PumpAsync();
            Assert.AreEqual(1, count);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            adapter.Emit(SemanticInput.Back);
            await Task.Delay(220);
            Assert.IsFalse(window.ShellView.ContentRegionControl.IsVisible, "Keyboard/controller B co-report must not toggle the content twice.");
            window.Close();
        });
    }

    private static ControllerSnapshot Snapshot(bool connected, int slot = 0, float x = 0, float y = 0, bool a = false, bool b = false) =>
        new(connected, slot, x, y, false, false, false, false, a, b);

    private static async Task PumpAsync()
    {
        await Task.Delay(30);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class FakeControllerAdapter : IControllerInputAdapter
    {
        public event EventHandler<SemanticInputEventArgs>? InputReceived;

        public bool Active { get; private set; }

        public bool Disposed { get; private set; }

        public void Start() { }

        public void SetActive(bool active) => Active = active;

        public void ResetHeldState() { }

        public void Dispose() => Disposed = true;

        public void Emit(SemanticInput input) =>
            InputReceived?.Invoke(this, new SemanticInputEventArgs(input, SemanticInputSource.Controller));
    }
}
