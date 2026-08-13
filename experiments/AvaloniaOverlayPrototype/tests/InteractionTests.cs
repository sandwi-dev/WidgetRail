using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using GameBarAlternative.AvaloniaPrototype.Lifecycle;
using GameBarAlternative.AvaloniaPrototype.Navigation;
using GameBarAlternative.AvaloniaPrototype.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameBarAlternative.AvaloniaPrototype.Tests;

[TestClass]
public sealed class InteractionTests
{
    [TestMethod]
    [Timeout(10_000)]
    public async Task Audio_slider_adjusts_horizontally_and_moves_spatially_in_the_aligned_column()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var window = new MainWindow { Width = 1000, Height = 640 };
            window.Show();
            await window.NavigateAsync(PrototypeRoute.AudioMixer);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            var page = (AudioMixerPage)window.ShellView.ActivePage!;
            var slider = page.FindControl<Slider>("MasterVolumeSlider")!;
            Assert.AreSame(slider, window.FocusManager?.GetFocusedElement(), "Down from tray must explicitly enter at the declared initial Slider.");
            var originalValue = slider.Value;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.IsGreaterThan(originalValue, slider.Value, "Right must adjust the focused Slider.");
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            var nextSlider = window.FocusManager?.GetFocusedElement() as Control;
            Assert.IsNotNull(nextSlider);
            Assert.AreEqual("audio.game.volume", AutomationProperties.GetAutomationId(nextSlider));
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.AreSame(slider, window.FocusManager?.GetFocusedElement());
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task B_key_from_content_restores_the_selected_stationary_tray_item()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            await window.NavigateAsync(PrototypeRoute.Settings);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.AreNotSame(window.ShellView.SelectedTrayButton, window.FocusManager?.GetFocusedElement());

            window.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.B, RawInputModifiers.None, PhysicalKey.None, null);

            Assert.IsTrue(window.ShellView.ContentRegionControl.IsVisible);
            Assert.AreSame(window.ShellView.SelectedTrayButton, window.FocusManager?.GetFocusedElement());
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Hiding_the_window_supersedes_destination_loading_and_retains_no_visible_work()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            var pending = window.NavigateAsync(PrototypeRoute.GameLauncher);
            window.Hide();

            var result = await pending;
            Assert.AreNotEqual(NavigationOutcome.Committed, result.Outcome);
            Assert.AreEqual(PrototypeVisibility.Hidden, window.Lifecycle.Visibility);
            Assert.IsTrue(window.Lifecycle.VisibleLifetime.IsCancellationRequested);
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Application_scroll_moves_to_the_end_and_back_to_the_first_item()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var window = new MainWindow { Width = 900, Height = 420 };
            window.Show();
            await window.NavigateAsync(PrototypeRoute.GameLauncher);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            var page = (GameLauncherPage)window.ShellView.ActivePage!;
            var scroll = page.ApplicationScrollControl;
            Assert.IsGreaterThan(0, scroll.Extent.Height - scroll.Viewport.Height, "Fixture must overflow its real ScrollViewer.");
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.AreSame(page.ApplicationButtons[0], window.FocusManager?.GetFocusedElement());
            for (var index = 1; index < page.ApplicationButtons.Count; index++)
            {
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            }

            Dispatcher.UIThread.RunJobs();
            Assert.AreSame(page.ApplicationButtons[^1], window.FocusManager?.GetFocusedElement());
            Assert.IsGreaterThan(0, scroll.Offset.Y);

            for (var index = page.ApplicationButtons.Count - 1; index > 0; index--)
            {
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.None, null);
            }

            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(0, scroll.Offset.Y, 0.01);
            Assert.AreSame(page.ApplicationButtons[0], window.FocusManager?.GetFocusedElement());
            window.Close();
            await Task.CompletedTask;
        });
    }

    private static async Task RunOnUiThreadAsync(Func<Task> action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }
}
