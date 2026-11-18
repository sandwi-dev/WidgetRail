using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

internal static class PreviewPartStyleScenarios
{
    internal static Task CascadeAndIsolation()
    {
        var platform = Parse("preview { padding: 12px; background: #111111; corner-radius: 10px; } preview-footer { padding: 8px 0px 0px; color: #888888; }", "platform");
        var user = Parse("preview { background: #222222; }", "user");
        var widget = Parse(".capture { padding: 30px; background: #ff0000; } preview.capture { padding: 0px; corner-radius: 6px; } preview-footer.capture { padding: 8px 12px 12px; }", "widget");
        var snapshot = Snapshot("capture");
        var theme = Compile(platform, user, widget);
        var styles = BridgeRenderStyleResolver.Resolve(snapshot, theme);
        var capture = styles["owner"];
        Check(capture.Base["background"].Text == "#ff0000", "Generic widget class did not style ordinary content.");
        Check(capture.PreviewFrame is { } frame && frame["background"].Text == "#222222" &&
            frame["padding"].Text == "0px" && frame["corner-radius"].Number == 6,
            "Scoped frame did not inherit the theme or generic owner rules leaked into the component part.");
        Check(capture.PreviewFooter?.ContainsKey("padding") == true && capture.PreviewFooter["color"].Text == "#888888",
            "Footer part lost independent padding or theme inheritance.");
        Check(styles["other"].PreviewFrame?["padding"].Text == "12px" &&
            styles["other"].PreviewFrame?["corner-radius"].Number == 10, "Scoped rule leaked into another preview.");
        Check(styles["content"].PreviewFrame is null && styles["content"].PreviewFooter is null,
            "Ordinary controls gained unused preview part styles.");
        var reset = BridgeRenderStyleResolver.Resolve(Snapshot("other-class"), theme)["owner"];
        Check(reset.PreviewFrame?["padding"].Text == "12px" && reset.PreviewFrame?["corner-radius"].Number == 10,
            "Removing the scoped class failed to restore defaults.");
        var anotherWidget = BridgeRenderStyleResolver.Resolve(snapshot, Compile(platform, user))["owner"];
        Check(anotherWidget.PreviewFrame?["padding"].Text == "12px" && anotherWidget.PreviewFrame?["background"].Text == "#222222",
            "Widget frame override leaked across widget style packages.");
        return Task.CompletedTask;
    }

    internal static Task TransportBounds()
    {
        var snapshot = Snapshot("capture");
        var theme = Compile(Parse("preview { padding: 12px; } preview.capture { padding: 0px; } preview-footer { padding: 8px 0px 0px; }", "fixture"));
        var styles = BridgeRenderStyleResolver.Resolve(snapshot, theme);
        var frozen = BridgeRenderStyleContract.ValidateAndFreeze(styles, styles.Keys.ToHashSet(), true);
        Check(frozen["owner"].PreviewFrame?["padding"].Text == "0px" && frozen["other"].PreviewFrame?["padding"].Text == "12px",
            "Part styles were lost by shared validation/freezing.");
        var roundTrip = BridgeJson.FromElement<Dictionary<string, BridgeNodeRenderStyles>>(BridgeJson.ToElement(frozen));
        Check(roundTrip["owner"].PreviewFrame?["padding"].Text == "0px" && roundTrip["owner"].PreviewFooter?.Count > 0,
            "Part styles did not survive bridge serialization.");
        var mutable = new Dictionary<string, BridgeComputedStyleValue>(styles["owner"].PreviewFrame!);
        var map = new Dictionary<string, BridgeNodeRenderStyles>(styles) { ["owner"] = styles["owner"] with { PreviewFrame = mutable } };
        var retained = BridgeRenderStyleContract.ValidateAndFreeze(map, map.Keys.ToHashSet(), true);
        mutable.Clear();
        Check(retained["owner"].PreviewFrame?.ContainsKey("padding") == true, "Mutable source could rewrite a retained frame style.");
        var value = styles["owner"].PreviewFrame!["padding"];
        var oversized = Enumerable.Range(0, BridgeRenderStyleLimits.MaximumPropertiesPerState + 1).ToDictionary(index => "property" + index, _ => value);
        map["owner"] = styles["owner"] with { PreviewFooter = oversized };
        try { BridgeRenderStyleContract.ValidateAndFreeze(map, map.Keys.ToHashSet(), true); throw new Exception("Oversized footer style was accepted."); }
        catch (BridgeProtocolException) { }
        return Task.CompletedTask;
    }

    private static ViewSnapshot Snapshot(string classes) => new()
    {
        WidgetInstanceId = "preview-style.fixture", Sequence = 1, ActiveInputScopeId = "root",
        Root = new() { Id = "root", Kind = ViewNodeKind.Stack, Children =
        [
            new() { Id = "owner", Kind = ViewNodeKind.FocusPresentationSurface, FocusPreview = new(), StyleClasses = [classes],
                Children = [new() { Id = "content", Kind = ViewNodeKind.Text, Text = "Content" }] },
            new() { Id = "other", Kind = ViewNodeKind.FocusPresentationSurface, FocusPreview = new() },
        ] },
    };
    private static WrssDocument Parse(string text, string name)
    {
        var parsed = WrssParser.Parse(text, name + ".wrss");
        Check(!parsed.Diagnostics.Any(value => value.Severity == WrssDiagnosticSeverity.Error), "Fixture stylesheet did not parse.");
        return parsed.Document;
    }
    private static WrssTheme Compile(params WrssDocument[] documents)
    {
        var compiled = WrssThemeCompiler.Compile(documents.Select((document, index) => new WrssThemeLayer(index * 100, [document])));
        Check(compiled.IsValid, "Fixture cascade did not compile.");
        return compiled.Theme!;
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
