using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Validation;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Explicit validation-only entry point. The driver must provision a disposable
    // catalog containing this fixture identity; never infer a user package to remove.
    internal void EnableSettingsUninstallValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            try
            {
                if (startup is not null) await startup;
                const string fixtureId = "widgetrail.tests.uninstall-native";
                var descriptor = catalogItems.Single(item => item.Id == fixtureId);
                Check(await InvokeIntentCoreAsync(prepareNotification: token =>
                    owner!.Session.PrepareWidgetSettingsIntentAsync(owner.Session.GetTarget(descriptor.Id), token)),
                    "Host Manage widget intent opens the disposable package in real Settings");
                await Until(() => activeWidget == "settings" && Find("Widget.installed.details.uninstall") is { IsEnabled: true });
                await Press("Widget.installed.details.uninstall");
                await Until(() => Find("Widget.installed.uninstall.action") is { IsEnabled: true });
                Check(Find("Widget.installed.uninstall.cancel") is not null, "Confirmation exposes Cancel before destructive action");
                await Press("Widget.installed.uninstall.cancel");
                await Until(() => Find("Widget.installed.details.uninstall") is { IsEnabled: true });
                Check(catalogItems.Any(item => item.Id == fixtureId), "Cancelling confirmation preserves the installed package");
                await Press("Widget.installed.details.uninstall");
                await Until(() => Find("Widget.installed.uninstall.action") is { IsEnabled: true });
                await Press("Widget.installed.uninstall.action");
                await Until(() => catalogItems.All(item => item.Id != fixtureId));
                await Until(() => MainFocusEnabled && !switching && surface?.CurrentBinding?.Frame.Snapshot.ActiveInputScopeId != "installed.uninstall.confirm");
                Check(!HostChoiceActive && !RecoveryVisible, "Confirmed uninstall refreshes the native catalog without a failure dialog");
                Check(activeWidget == "settings", "Settings remains usable after removing the selected package");
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }

            async Task Press(string id)
            {
                await Until(() => MainFocusEnabled && Find(id) is { IsEnabled: true, IsLoaded: true, ActualWidth: > 0 });
                // Inspection can publish another current details frame while
                // returning from confirmation. Reacquire its realized button.
                await Until(() =>
                {
                    if(Find(id) is not { IsEnabled: true, IsLoaded: true, ActualWidth: > 0 } control)return false;
                    control.StartBringIntoView();
                    return control.Focus(FocusState.Keyboard);
                });
                NativeContextProbeInput.PressKey(VirtualKey.Enter);
            }
            Control? Find(string id) => surface is null ? null : Descendants(surface).OfType<Control>()
                .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == id);
            async Task Until(Func<bool> condition)
            {
                var deadline = Environment.TickCount64 + 40000;
                while (!condition())
                {
                    if (RecoveryVisible || validationFailure is not null) throw new InvalidOperationException("Settings uninstall validation failed", validationFailure);
                    if (Environment.TickCount64 > deadline) throw new TimeoutException("Settings uninstall did not settle; scope=" + surface?.CurrentBinding?.Frame.Snapshot.ActiveInputScopeId);
                    await Task.Delay(25);
                }
            }
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
            void Write(bool passed, string? error) => File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { passed, checks, error }));
        };
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
                foreach (var node in Descendants(VisualTreeHelper.GetChild(root, i))) yield return node;
        }
    }
}
