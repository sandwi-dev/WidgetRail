using Avalonia;
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
    public async Task Directional_focus_moves_while_slider_left_right_remains_adjustable()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var window = new Window { Width = 1000, Height = 640 };
            var page = new AudioMixerPage();
            window.Content = page;
            window.Show();
            var primaryAction = page.FindControl<Button>("PrimaryAction")!;
            var invoked = false;
            primaryAction.Click += (_, _) => invoked = true;
            primaryAction.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.IsTrue(invoked, "Enter must invoke the focused standard Avalonia Button.");

            var slider = page.FindControl<Slider>("MasterVolumeSlider")!;
            slider.Focus();

            Assert.IsFalse(FocusNavigator.Move(page, Key.Right), "Slider Left/Right must remain owned by the range control.");
            var originalValue = slider.Value;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.IsGreaterThan(originalValue, slider.Value, "Right must adjust the focused standard Avalonia Slider.");
            Assert.IsTrue(FocusNavigator.Move(page, Key.Down));
            Assert.AreNotSame(slider, window.FocusManager?.GetFocusedElement());
            window.Close();
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task B_key_returns_from_the_active_page_to_the_stationary_tray_surface()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            await window.NavigateAsync(PrototypeRoute.Settings);
            Assert.IsTrue(window.ShellView.ContentRegionControl.IsVisible);

            window.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.B, RawInputModifiers.None, PhysicalKey.None, null);
            await Task.Delay(TimeSpan.FromMilliseconds(180));

            Assert.IsFalse(window.ShellView.ContentRegionControl.IsVisible);
            Assert.IsTrue(window.ShellView.TrayRegionControl.IsVisible);
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
            var window = new Window { Width = 900, Height = 420 };
            var page = new GameLauncherPage();
            window.Content = page;
            window.Show();
            page.Measure(new Size(900, 420));
            page.Arrange(new Rect(0, 0, 900, 420));
            var scroll = page.ApplicationScrollControl;
            Assert.IsGreaterThan(0, scroll.Extent.Height - scroll.Viewport.Height, "Fixture must overflow its real ScrollViewer.");
            page.ApplicationButtons[0].Focus();
            for (var index = 1; index < page.ApplicationButtons.Count; index++)
            {
                Assert.IsTrue(FocusNavigator.Move(page, Key.Down));
            }

            Dispatcher.UIThread.RunJobs();
            Assert.AreSame(page.ApplicationButtons[^1], window.FocusManager?.GetFocusedElement());
            Assert.IsGreaterThan(0, scroll.Offset.Y);

            for (var index = page.ApplicationButtons.Count - 1; index > 0; index--)
            {
                Assert.IsTrue(FocusNavigator.Move(page, Key.Up));
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
