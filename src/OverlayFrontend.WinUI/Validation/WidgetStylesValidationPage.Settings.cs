using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task SettingsProductionLayoutAsync(string directory)
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
                presenter.Width = width; presenter.Height = 520;
                presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced, TextScale = largeText ? 1.5 : 1 }, false);
                presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                    descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                await Task.Delay(100);
                presenter.UpdateLayout();
                var label = Path.GetFileNameWithoutExtension(file) + " " + width + (largeText ? " large text" : "");
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
                if (snapshot.InitialFocusId is { } focusId && Find<Control>("Widget." + focusId) is { } focused)
                {
                    Check(focused.Focus(FocusState.Keyboard), label + ": initial control accepts focus");
                    presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                        descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles));
                    Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), focused), label + ": unchanged snapshot retains focus");
                }
            }
        }
        // Leave the final page at ordinary sizing for visual inspection.
        presenter.Width = 880;
        presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        presenter.UpdateLayout();
    }
}
