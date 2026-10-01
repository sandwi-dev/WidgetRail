using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Development;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal Action<string>? NavigationObserved { get; set; }

    private string InspectionFocusId() => XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is DependencyObject element
        ? AutomationProperties.GetAutomationId(element) : string.Empty;

    /// <summary>Read-only developer capture. Never realizes rows or exposes text-entry values, action payloads or artwork URLs.</summary>
    internal DeveloperInspection CaptureDeveloperInspection()
    {
        const int limit = 1024;
        var nodes = new List<DeveloperInspectionNode>();
        var omitted = 0;
        var focus = InspectionFocusId();
        Capture(this, null, null);
        Visit(this);
        return new(frame?.Descriptor.Id ?? "", frame?.Authority.SnapshotSequence ?? 0, focus,
            ActualWidth, ActualHeight, nodes, omitted);

        void Visit(DependencyObject parent)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); ++index)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is WidgetViewPresenter nested && !ReferenceEquals(nested, this))
                {
                    SelectorItem? container = null;
                    for (var ancestor = VisualTreeHelper.GetParent(nested); ancestor is not null && !ReferenceEquals(ancestor, this);
                         ancestor = VisualTreeHelper.GetParent(ancestor))
                        if (ancestor is SelectorItem item) { container = item; break; }
                    Capture(nested, FindBinding(nested)?.Identity.Id, container);
                }
                Visit(child);
            }
        }
        void Capture(WidgetViewPresenter presenter, string? parentId, SelectorItem? container)
        {
            var prefix = ReferenceEquals(presenter, this) ? "" :
                (container is null ? parentId ?? "fragment" : AutomationProperties.GetAutomationId(container)) + "::";
            var document = presenter.CaptureLayoutDiagnostics(Math.Max(0, limit - nodes.Count));
            omitted += document["omittedNodes"]?.GetValue<int>() ?? 0;
            var origin = presenter.TransformToVisual(this).TransformPoint(new(0, 0));
            foreach (var node in document["nodes"]!.AsArray().OfType<JsonObject>())
            {
                var id = node["id"]!.GetValue<string>();
                var declaration = presenter.declarations[id];
                var binding = presenter.bindings[id];
                var nativeId = container is not null && id == presenter.fragmentRootId
                    ? AutomationProperties.GetAutomationId(container) : AutomationProperties.GetAutomationId(binding.Element);
                var focused = nativeId.Length > 0 && nativeId == focus;
                node["inputScope"] = declaration.Identity.Scope;
                node["classes"] = new JsonArray(declaration.Node.StyleClasses.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
                node["focusable"] = declaration.Node.IsFocusable;
                node["disabled"] = declaration.Node.IsDisabled;
                node["busy"] = declaration.Node.IsBusy;
                node["selected"] = declaration.Node.IsSelected;
                node["focused"] = focused;
                node["nativeFocusId"] = nativeId;
                node["focusNeighbors"] = new JsonObject { ["up"] = declaration.Node.Focus?.Up, ["down"] = declaration.Node.Focus?.Down,
                    ["left"] = declaration.Node.Focus?.Left, ["right"] = declaration.Node.Focus?.Right };
                var styles = presenter.presentation?.RenderStyles.GetValueOrDefault(id);
                var applied = presenter.nativeStyles.TryGetValue(binding, out var adapter) ? adapter.InspectionStyle :
                    focused ? styles?.Focused : styles?.Base;
                // Style values are declarations, not widget input/content. Keep
                // resource/source-bearing properties out of the developer panel.
                node["computedStyle"] = applied is null ? null : new JsonObject(applied
                    .Where(pair => !pair.Key.Contains("image", StringComparison.OrdinalIgnoreCase) && !pair.Key.Contains("source", StringComparison.OrdinalIgnoreCase))
                    .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value.Text))));
                nodes.Add(new(prefix + id, node["parent"]?.GetValue<string>() is { } parent ? prefix + parent : parentId, node["kind"]!.GetValue<string>(),
                    node["visible"]?.GetValue<string>() == "Visible", focused,
                    Number(node["x"]) + origin.X, Number(node["y"]) + origin.Y,
                    Number(node["ActualWidth"]), Number(node["ActualHeight"]),
                    node.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
            }
        }
        static double Number(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : 0;
    }
}
