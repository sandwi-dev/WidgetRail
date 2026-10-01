using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableSettingsRefreshFocusValidation(string path, Func<bool> ownsForeground)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            var samples = new List<object>();
            try
            {
                if (startup is not null) await startup;
                await SelectAsync("settings", true);
                await Until(() => MainFocusEnabled && ownsForeground() && Find("Widget.category.overlay") is { IsEnabled: true });
                await Task.Delay(1800);
                foreach (var targetId in new[] { "Widget.category.overlay", "Widget.category.controllers" })
                {
                    if (!Find(targetId)!.Focus(FocusState.Keyboard)) throw new InvalidOperationException("Cannot focus Settings category");
                    await SelectAsync("widgetrail.samples.sdk-gallery", true);
                    await SelectAsync("settings", true);
                    await CheckSettledReturn(targetId, "widget return");
                    SetVisible(false);
                    await Task.Delay(150);
                    SetVisible(true);
                    await CheckSettledReturn(targetId, "hide/reopen lifecycle");
                }
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }

            async Task CheckSettledReturn(string targetId, string mode)
            {
                await Until(() => MainFocusEnabled && FocusId() == targetId);
                var target = Find(targetId);
                var sequence = surface!.CurrentBinding!.Frame.Authority.SnapshotSequence;
                var lost = new List<string>();
                void Observe(object? sender, object args)
                {
                    if (lost.Count < 12 && (FocusId() != targetId || !ReferenceEquals(target, Find(targetId))))
                        lost.Add(FocusId() ?? "no native focus");
                }
                CompositionTarget.Rendering += Observe;
                try
                {
                    await Until(() => surface!.CurrentBinding!.Frame.Authority.SnapshotSequence > sequence &&
                        Find("Widget.settings.refresh") is { IsEnabled: true });
                    await Task.Delay(300);
                }
                finally { CompositionTarget.Rendering -= Observe; }
                samples.Add(new { targetId, mode, sequence, finalSequence = surface!.CurrentBinding!.Frame.Authority.SnapshotSequence, lost });
                if (lost.Count != 0 || FocusId() != targetId || !ReferenceEquals(target, Find(targetId)))
                    throw new InvalidOperationException("Settings refresh replaced or unfocused " + targetId + " after " + mode);
                checks.Add(targetId + " remains the same focused native control through " + mode + " and refresh completion");
            }
            string? FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : null;
            Control? Find(string id) => surface is null ? null : Descendants(surface).OfType<Control>().FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == id);
            async Task Until(Func<bool> condition)
            {
                var deadline = Environment.TickCount64 + 15000;
                while (!condition())
                {
                    if (RecoveryVisible || validationFailure is not null) throw new InvalidOperationException("Settings validation failed", validationFailure);
                    if (Environment.TickCount64 > deadline) throw new TimeoutException("Settings return did not settle");
                    await Task.Delay(20);
                }
            }
            void Write(bool passed, string? error)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { passed, checks, samples, error }));
            }
        };
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
                foreach (var node in Descendants(VisualTreeHelper.GetChild(root, i))) yield return node;
        }
    }
}
