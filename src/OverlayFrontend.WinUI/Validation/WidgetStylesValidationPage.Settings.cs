using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task SettingsProductionLayoutAsync(string directory)
    {
        Exception? failure = null;
        try { await RunSettingsProductionLayoutAsync(directory); }
        catch (Exception error) { failure = error; }
        if (Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--settings-layout-result") is { } result)
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString(),
                scope = "Native renderer and action dispatch with fixture snapshots; no installer, registration, prerequisite or UAC operations." }));
        if (failure is not null) throw failure;
    }

    private async Task RunSettingsProductionLayoutAsync(string directory)
    {
        static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.Resize(new((int)(1060 * scale), (int)(940 * scale)));
        foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(path => Path.GetFileNameWithoutExtension(path) == "appearance" ? "zz" : path))
        {
            using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(file));
            var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(document.RootElement.GetProperty("snapshot").GetRawText()));
            snapshot = snapshot with { Sequence = ++sequence };
            var styles = document.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
            var descriptor = new BridgeWidgetDescriptor { Id = "settings.layout", Name = "Settings",
                InstanceId = snapshot.WidgetInstanceId, RuntimeGeneration = "settings-fixture", PresentationGeneration = "settings-fixture",
                Icon = WidgetGlyph.Settings, PackageContentDigest = string.Empty };
            foreach (var width in new[] { 880d, 520d })
            foreach (var largeText in new[] { false, true })
            {
                presenter.Width = width; presenter.Height = snapshot.Surface?.PreferredHeight ?? 520;
                presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced, TextScale = largeText ? 1.5 : 1 }, false);
                presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                    descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                await Task.Delay(100);
                presenter.UpdateLayout();
                var label = Path.GetFileNameWithoutExtension(file) + " " + width + (largeText ? " large text" : "");
                if (snapshot.ActiveInputScopeId == "settings-root" && Find<ScrollViewer>("Widget.settings.categories") is { } categories)
                {
                    if (width == snapshot.Surface?.PreferredWidth && !largeText)
                    {
                        Check(categories.ScrollableHeight <= 1,
                            label + ": home categories fit without scrolling at the requested default size (overflow " + categories.ScrollableHeight + ")");
                        var last = Find<Button>("Widget.category.diagnostics")!;
                        var utilities = Find<FrameworkElement>("Widget.settings.home.utilities")!;
                        var bottom = last.TransformToVisual(presenter).TransformPoint(new(0, last.ActualHeight)).Y;
                        var top = utilities.TransformToVisual(presenter).TransformPoint(new()).Y;
                        Check(top - bottom >= 8, label + ": last card has a clear gap above the utility row");
                    }
                    else
                    {
                        var last = Find<Button>("Widget.category.diagnostics")!;
                        Check(last.Focus(FocusState.Keyboard), label + ": constrained home retains access to its last category");
                        last.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                        await Wait(() => last.TransformToVisual(categories).TransformPoint(new(0, last.ActualHeight)).Y <= categories.ActualHeight + 1);
                    }
                }
                foreach (var node in Walk(snapshot.Root).Where(node => node.StyleClasses.Contains("widget-detail-action")))
                {
                    var button = Find<Button>("Widget." + node.Id)!;
                    Check(button.IsEnabled == (node.IsDisabled != true), label + ": detail availability matches " + node.Id);
                    if (node.IsDisabled == true)
                    {
                        var enabled = Walk(snapshot.Root).First(item => item.Id == "installed.details.update");
                        var peer = Find<Button>("Widget." + enabled.Id)!;
                        Check(ColorOf(button.Foreground) != ColorOf(peer.Foreground) && ColorOf(button.Background) != ColorOf(peer.Background),
                            label + ": disabled detail is visually distinct " + node.Id);
                    }
                }
                foreach (var node in Walk(snapshot.Root).Where(node => node.StyleClasses.Contains("wrail-setting-row")))
                {
                    var row = Find<FrameworkElement>("Widget." + node.Id)!;
                    var copy = Find<FrameworkElement>("Widget." + node.Id + ".copy")!;
                    var control = Find<FrameworkElement>("Widget." + node.Id + ".control")!;
                    var origin = control.TransformToVisual(row).TransformPoint(new(0, 0));
                    var copyOrigin = copy.TransformToVisual(row).TransformPoint(new(0, 0));
                    Check(origin.X >= 0 && origin.X + control.ActualWidth <= row.ActualWidth + 1,
                        label + ": control stays inside " + node.Id);
                    Check(width > 600 ? Math.Abs(origin.Y - copyOrigin.Y) < 1 : origin.Y >= copyOrigin.Y + copy.ActualHeight - 1,
                        label + ": setting pair uses the expected columns");
                }
                foreach (var node in Walk(snapshot.Root).Where(node => node.StyleClasses.Contains("installed-entry")))
                {
                    var row = Find<FrameworkElement>("Widget." + node.Id)!;
                    var copy = Find<FrameworkElement>("Widget." + node.Id + ".copy")!;
                    var version = Find<FrameworkElement>("Widget." + node.Id + ".version")!;
                    var chevron = Find<FrameworkElement>("Widget." + node.Id + ".chevron")!;
                    var versionOrigin = version.TransformToVisual(row).TransformPoint(new(0, 0));
                    var copyOrigin = copy.TransformToVisual(row).TransformPoint(new(0, 0));
                    var chevronOrigin = chevron.TransformToVisual(row).TransformPoint(new(0, 0));
                    Check(versionOrigin.X >= copyOrigin.X + copy.ActualWidth - 1 && chevronOrigin.X + chevron.ActualWidth <= row.ActualWidth + 1,
                        label + ": name/status and version do not overlap or overflow");
                    Check(Walk(node).Count(item => item.IsFocusable) == 1, label + ": installed entry has exactly one focus target");
                }
                if (snapshot.InitialFocusId is { } focusId && Find<Control>("Widget." + focusId) is { } focused)
                {
                    Check(focused.Focus(FocusState.Keyboard), label + ": initial control accepts focus");
                    presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                        descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                    Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), focused), label + ": unchanged snapshot retains focus");
                }
                var declarations = Walk(snapshot.Root).ToDictionary(node => node.Id);
                foreach (var node in declarations.Values.Where(node =>
                    node.StyleClasses.Contains("wrail-stepper__button") && node.IsDisabled != true))
                {
                    if (node.Focus?.Down is not { } nextId || !declarations.TryGetValue(nextId, out var next) || next.IsDisabled == true ||
                        Find<Control>("Widget." + node.Id) is not { } source || Find<Control>("Widget." + nextId) is not { } destination) continue;
                    Check(source.Focus(FocusState.Keyboard), label + ": stepper accepts focus " + node.Id);
                    presenter.MoveFocus(FocusNavigationDirection.Down);
                    await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), destination));
                    Check(true, label + ": vertical stepper navigation preserves its authored column " + node.Id);
                }
                if (snapshot.ActiveInputScopeId == "app-update.page")
                    await VerifyUpdateFixtureAsync(snapshot, Path.GetFileNameWithoutExtension(file), label);
            }
        }
        // Leave the final page at ordinary sizing for visual inspection.
        presenter.Width = 880;
        presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        presenter.UpdateLayout();
    }

    private async Task VerifyUpdateFixtureAsync(ViewSnapshot snapshot, string name, string label)
    {
        var dispatched = new List<string>();
        // Record the real presenter dispatch; never attach an installer service to native layout fixtures.
        var previousDispatch = presenter.DispatchActionAsync;
        presenter.DispatchActionAsync = action => { dispatched.Add(action.Action.ActionId); return Task.CompletedTask; };
        try
        {
            var interactive = name == "interactive-required";
            var recovery = name is "cancelled" or "failed" or "restart-required" or "startup-warning";
            var warning = Find<TextBlock>("Widget.app-update.input-warning");
            Check((warning is not null) == interactive, label + ": interactive guidance appears only for the fallback");
            if (interactive)
            {
                Check(warning!.Text.Contains("mouse and keyboard", StringComparison.OrdinalIgnoreCase),
                    label + ": fallback clearly names the input requirement");
                await Reveal(warning!);
                Check(!warning!.IsTextTrimmed, label + ": fallback warning is fully readable");
                var primary = Find<Button>("Widget.app-update.primary")!;
                Check(ContainsText(primary, "Open interactive setup"), label + ": fallback action has explicit setup wording");
                await Activate("app-update.primary", "app-update.install");
            }
            if (recovery)
            {
                Check(Find<Button>("Widget.app-update.primary") is null, label + ": recovery cannot invoke an unreviewed installer");
                var status = Find<TextBlock>("Widget.app-update.status")!;
                await Reveal(status);
                Check(status.Text.Length > 0 && !status.IsTextTrimmed, label + ": full recovery guidance is readable");
                Check(Nodes(snapshot.Root).Any(node => node.Id == "app-update.status" && node.StyleClasses.Contains("diagnostic-error")),
                    label + ": recovery uses the error presentation");
            }
            await Activate("app-update.check", "app-update.check");
            await Activate("app-update.back", "back");
            Check(dispatched.Count(action => action == "app-update.install") == (interactive ? 1 : 0),
                label + ": inspecting recovery and navigation never dispatches installation");
        }
        finally { presenter.DispatchActionAsync = previousDispatch; }

        async Task Activate(string id, string expected)
        {
            var button = Find<Button>("Widget." + id)!;
            Check(button is { IsEnabled: true, IsTabStop: true }, label + ": " + id + " remains available");
            Check(button.Focus(FocusState.Keyboard), label + ": " + id + " accepts controller-style focus");
            await Reveal(button);
            var before = dispatched.Count;
            presenter.ActivateFocused();
            await Wait(() => dispatched.Count == before + 1);
            Check(dispatched[^1] == expected && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), button),
                label + ": " + id + " dispatches exactly once without moving focus");
        }
        async Task Reveal(FrameworkElement element)
        {
            element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            await Wait(() => InsideViewport(element));
            Check(true, label + ": " + Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(element) + " is revealed without clipping");
        }
        bool InsideViewport(FrameworkElement element)
        {
            if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
            for (DependencyObject? parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not FrameworkElement ancestor || parent is not ScrollViewer && !ReferenceEquals(parent, presenter)) continue;
                var bounds = element.TransformToVisual(ancestor).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
                if (bounds.Left < -1 || bounds.Top < -1 || bounds.Right > ancestor.ActualWidth + 1 || bounds.Bottom > ancestor.ActualHeight + 1) return false;
                if (ReferenceEquals(parent, presenter)) return true;
            }
            return false;
        }
        static bool ContainsText(DependencyObject root, string text)
        {
            if (root is TextBlock block && block.Text == text) return true;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                if (ContainsText(VisualTreeHelper.GetChild(root, index), text)) return true;
            return false;
        }
        static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
    }
}
