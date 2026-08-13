using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using GameBarAlternative.AvaloniaPrototype.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameBarAlternative.AvaloniaPrototype.Tests;

[TestClass]
public sealed class SpatialFocusTests
{
    [TestMethod]
    [Timeout(15_000)]
    public async Task Tray_cycle_retains_selection_content_entry_is_explicit_back_restores_and_page_focus_is_remembered()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = new MainWindow { Width = 1280, Height = 720 };
            window.Show();
            await window.NavigateAsync(PrototypeRoute.AudioMixer);
            Assert.AreSame(window.ShellView.TrayButtons[1], window.FocusManager?.GetFocusedElement());

            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            await Task.Delay(280);
            Assert.AreEqual(PrototypeRoute.SpotifyPlayer, window.ShellView.SelectedRoute);
            Assert.AreSame(window.ShellView.TrayButtons[2], window.FocusManager?.GetFocusedElement(),
                "Page completion must not steal tray focus.");

            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
            await Task.Delay(280);
            Assert.AreSame(window.ShellView.TrayButtons[1], window.FocusManager?.GetFocusedElement());

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            var page = (AudioMixerPage)window.ShellView.ActivePage!;
            Assert.AreSame(page.FindControl<Slider>("MasterVolumeSlider"), window.FocusManager?.GetFocusedElement(),
                "Enter from tray must explicitly enter at the declared fallback.");

            var remembered = page.FindControl<Slider>("VoiceVolumeSlider")!;
            remembered.Focus(NavigationMethod.Directional);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.AreSame(window.ShellView.TrayButtons[1], window.FocusManager?.GetFocusedElement());

            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            await Task.Delay(280);
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
            await Task.Delay(280);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);

            var replacementPage = (AudioMixerPage)window.ShellView.ActivePage!;
            var restored = (Control)window.FocusManager!.GetFocusedElement()!;
            Assert.AreEqual("audio.voice.volume", AutomationProperties.GetAutomationId(restored));
            Assert.AreSame(replacementPage.FindControl<Slider>("VoiceVolumeSlider"), restored);
            window.Close();
        });
    }
}
