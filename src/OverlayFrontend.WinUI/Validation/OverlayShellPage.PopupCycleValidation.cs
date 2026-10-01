using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnablePopupCycleValidation(string path)
    {
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return; started = true;
            var checks = new List<string>();
            var original = Appearance;
            Exception? failure = null;
            try
            {
                if (startup is not null) await startup;
                if (activeWidget is null) throw new InvalidOperationException("Missing initial widget");
                var selected = activeWidget;
                await SelectAsync(selected, enterWidget: false);
                foreach (var mode in new[] { WidgetSwitcherLayout.Rail, WidgetSwitcherLayout.Radial })
                {
                    Appearance = original with { WidgetSwitcher = mode };
                    FocusDirectionalTray();
                    if (mode == WidgetSwitcherLayout.Radial)
                    { PrepareRadialBackEntry(); SetInteractive(false); RefreshRadialChooser(); FocusTray(); }
                    await Until(() => RadialOpen == (mode == WidgetSwitcherLayout.Radial));
                    var descriptor = catalogItems.Single(item => item.Id == selected);
                    if (mode == WidgetSwitcherLayout.Radial)
                        await Until(() => radialView?.ContextAnchor(selected) is { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 });
                    await ShowTrayMenuAsync(descriptor);
                    await Until(() => trayMenu is { Items.Count: > 1 } menu && menu.Items.All(item => item.IsLoaded));
                    var menu = trayMenu!;
                    var enabled = menu.Items.Where(item => item.IsEnabled).ToArray();
                    if (enabled.Length < 2) throw new InvalidOperationException("Need two available popup commands");
                    var first = enabled[0]; var last = enabled[^1];
                    first.Focus(FocusState.Keyboard);
                    Move(NavigationDirection.Up);
                    await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), last));
                    checks.Add(mode + " command popup wraps Up from first to last through controller routing");
                    Move(NavigationDirection.Down);
                    await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), first));
                    checks.Add(mode + " command popup wraps Down from last to first through controller routing");
                    last.IsEnabled = false;
                    try
                    {
                        Move(NavigationDirection.Up);
                        await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), enabled[^2]));
                        checks.Add(mode + " wrapping skips unavailable commands");
                    }
                    finally { last.IsEnabled = true; }
                    CloseTrayMenu(true);
                    await Until(() => Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Count == 0);
                    if (activeWidget != selected) throw new InvalidOperationException("Navigation invoked a command");
                    checks.Add(mode + " traversal and dismissal preserve widget ownership without invoking commands");
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                CloseTrayMenu(false); Appearance = original;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
            }
            void Move(NavigationDirection direction)
            { var frame = Sample(); frame.DpadNavigation = new() { Direction = direction, Phase = NavigationPhase.Pressed }; Receive(frame); }
            async Task Until(Func<bool> condition, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? expression = null)
            { for (var i = 0; i < 200; ++i) { if (condition()) return; await Task.Delay(20); }
                throw new TimeoutException($"Popup focus did not settle: {expression}; mode={Appearance.WidgetSwitcher}, radial={RadialOpen}, interactive={interactive}, foreground={foreground}, menu={trayMenu?.Items.Count}, selection={selectionVersion}"); }
        };
    }
}
