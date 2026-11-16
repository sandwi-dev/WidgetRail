using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableCaptureCopyValidation(string output, bool source = false)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(output))!;
        if (!Path.GetFullPath(options.SettingsRoot).StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            options.SettingsRoot == PlatformSettingsPaths.CreateDefault().RootDirectory) throw new InvalidOperationException("Copy picker validation requires isolated settings.");
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return; started = true;
            var checks = new List<string>(); string? error = null;
            try
            {
                if (startup is not null) await startup;
                await SelectAsync("settings", true);
                await Wait(() => visible && foreground && interactive && !switching);
                var target = Path.Combine(directory, "destination"); Directory.CreateDirectory(target);
                await File.WriteAllTextAsync(Path.Combine(directory, "picker-ready.json"), JsonSerializer.Serialize(new { destination = target, pid = Environment.ProcessId }));
                var before = FocusManager.GetFocusedElement(XamlRoot);
                var selected = await PickCaptureCopyDestinationAsync(_ => Task.FromResult(true), lifetime.Token, source ? "Link this folder" : "Copy here");
                Check(string.Equals(selected, target, StringComparison.OrdinalIgnoreCase), "Windows folder picker returns the exact user-selected destination");
                Check(!SystemFilePickerOpen && interactive, "Completing the picker releases shell input ownership");
                await Wait(() => foreground);
                if (before is Control control && control.IsLoaded)
                    Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), before), "Picker completion restores the invoking control");
                await File.WriteAllTextAsync(Path.Combine(directory, "picker-cancel.json"), "ready");
                var alive = true;
                var cancelled = PickCaptureCopyDestinationAsync(_ => Task.FromResult(alive), lifetime.Token, source ? "Link this folder" : "Copy here");
                await Wait(() => SystemFilePickerOpen); await Task.Delay(400); alive = false;
                Check(await cancelled is null && !SystemFilePickerOpen, "Retiring request closes the owned native picker without a destination");
            }
            catch (Exception failure) { error = failure.ToString(); }
            finally
            {
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = error is null, checks, error }, new JsonSerializerOptions { WriteIndented = true }));
            }
            void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); }
            static async Task Wait(Func<bool> ready) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); while (!ready()) await Task.Delay(20, timeout.Token); }
        };
    }
}
