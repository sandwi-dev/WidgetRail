using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ControllerShortcutLabelTests
{
    public static Task Run()
    {
        var labeled = new WidgetView(
                UI.Stack("root", UI.Text("Ready", "status"))
                    .InputScope("root")
                    .Shortcut(ControllerButton.X, "toggle", label: "Play or pause"))
            .CreateSnapshot("shortcut.label", 1);
        Require(labeled.ProtocolVersion == ProtocolConstants.ControllerShortcutLabelVersion,
            "A labeled shortcut did not negotiate protocol v43.");
        Require(labeled.Root.Shortcuts.Single().Label == "Play or pause",
            "The SDK did not retain the exact shortcut label.");
        using (var document = JsonDocument.Parse(SnapshotJson.Serialize(labeled)))
        {
            var encoded = document.RootElement.GetProperty("root")
                .GetProperty("shortcuts")[0];
            Require(encoded.GetProperty("label").GetString() == "Play or pause",
                "The shortcut label did not serialize exactly.");
        }

        var unlabeled = new WidgetView(
                UI.Stack("root", UI.Text("Ready", "status"))
                    .InputScope("root")
                    .Shortcut(ControllerButton.X, "toggle"))
            .CreateSnapshot("shortcut.unlabeled", 1);
        Require(unlabeled.ProtocolVersion == ProtocolConstants.BaselineVersion,
            "An unlabeled shortcut unnecessarily raised the protocol version.");
        Require(unlabeled.Root.Shortcuts.Single().Label is null,
            "The legacy overload synthesized label metadata.");
        using (var document = JsonDocument.Parse(SnapshotJson.Serialize(unlabeled)))
        {
            var encoded = document.RootElement.GetProperty("root")
                .GetProperty("shortcuts")[0];
            Require(!encoded.TryGetProperty("label", out _),
                "An absent shortcut label was not omitted from JSON.");
        }

        foreach (var invalid in new[]
                 {
                     "",
                     "   ",
                     "Line\nfeed",
                     new string('x', ProtocolConstants.MaximumStringLength + 1),
                 })
        {
            var snapshot = RawSnapshot(new ControllerShortcut(
                ControllerButton.X, "toggle", Label: invalid));
            Require(ViewSnapshotValidator.Validate(snapshot).Any(error =>
                    error.Path == "$.root.shortcuts[0].label" &&
                    error.Code == "invalid_shortcut_label"),
                "An invalid shortcut label did not fail at its exact path.");
        }

        var labeledChildren = Enumerable.Range(0, 65)
            .Select(index => new ViewNode
            {
                Id = $"scope.{index}",
                Kind = ViewNodeKind.Stack,
                Shortcuts =
                [
                    new ControllerShortcut(
                        ControllerButton.X,
                        $"action.{index}",
                        Label: new string('x', ProtocolConstants.MaximumStringLength)),
                ],
            })
            .ToArray();
        var aggregate = new ViewSnapshot
        {
            ProtocolVersion = ProtocolConstants.ControllerShortcutLabelVersion,
            Sequence = 1,
            WidgetInstanceId = "shortcut.aggregate",
            ActiveInputScopeId = "root",
            Root = new ViewNode
            {
                Id = "root",
                Kind = ViewNodeKind.Stack,
                Children = labeledChildren,
            },
            PinnedLayouts =
            [
                new PinnedPresentationLayout
                {
                    Id = "pinned",
                    Name = "Pinned",
                    Surface = new WidgetSurfaceHints
                    {
                        PreferredWidth = 320,
                        PreferredHeight = 180,
                        MinimumWidth = 240,
                        MinimumHeight = 180,
                    },
                    Root = new ViewNode { Id = "pinned.root", Kind = ViewNodeKind.Stack },
                    ActiveInputScopeId = "pinned.root",
                },
            ],
        };
        Require(ViewSnapshotValidator.Validate(aggregate).Any(error =>
                error.Path == "$.pinnedLayouts" &&
                error.Code == "aggregate_strings_too_large"),
            "Shortcut labels did not participate in aggregate string accounting.");
        return Task.CompletedTask;
    }

    private static ViewSnapshot RawSnapshot(ControllerShortcut shortcut) => new()
    {
        ProtocolVersion = ProtocolConstants.ControllerShortcutLabelVersion,
        Sequence = 1,
        WidgetInstanceId = "shortcut.raw",
        ActiveInputScopeId = "root",
        Root = new ViewNode
        {
            Id = "root",
            Kind = ViewNodeKind.Stack,
            Shortcuts = [shortcut],
        },
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
