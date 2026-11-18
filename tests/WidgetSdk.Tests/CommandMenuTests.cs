using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class CommandMenuTests
{
    internal static Task Run()
    {
        var options = new[] { new SelectOption("refresh", "Refresh", "refresh", Glyph: WidgetGlyph.Refresh),
            new SelectOption("export", "Export", "export", IsDisabled: true), new SelectOption("remove", "Remove", "remove", IsBusy: true) };
        var element = UI.CommandMenu("More", options, "more", "More capture actions");
        options[0] = options[0] with { IsSelected = true };
        var menu = new WidgetView(element).CreateSnapshot("command-menu", 1);
        Check(menu.ProtocolVersion == 115 && menu.Root.Kind == ViewNodeKind.Select && menu.Root.IsCommandMenu == true, "Command menu must use explicit versioned Select semantics");
        Check(menu.Root.Text == "More" && menu.Root.AccessibilityLabel == "More capture actions" && menu.Root.AccessibilityValue is null,
            "Command opener must expose its label without a selected value");
        Check(menu.Root.SelectOptions.All(option => !option.IsSelected) && menu.Root.SelectOptions[1].IsDisabled && menu.Root.SelectOptions[2].IsBusy,
            "Factory must freeze input options and retain availability flags");
        var hiddenLabel = new WidgetView(element with { ShowLabel = false }).CreateSnapshot("command-menu", 1);
        Check(hiddenLabel.Root.Text == "More", "Settings-row label policy must not hide a command opener label");
        var payload = SnapshotJson.Serialize(menu);
        Check(SnapshotJson.Deserialize(payload).Root.IsCommandMenu == true && !Encoding.UTF8.GetString(payload).Contains("accessibilityValue"), "Command wire shape lost its mode or invented a value");
        Check(ViewSnapshotValidator.Validate(menu with { ProtocolVersion = 114 }).Any(error => error.Code == "feature_requires_version"), "Older hosts must reject command-menu semantics");
        var select = new WidgetView(UI.Select("More", [new SelectOption("refresh", "Refresh", "refresh", true)], "more")).CreateSnapshot("command-menu", 1);
        Check(select.Root.IsCommandMenu is null && select.Root.Text == "More: Refresh" && select.Root.AccessibilityValue == "Refresh" &&
            select.ProtocolVersion < 115 && !Encoding.UTF8.GetString(SnapshotJson.Serialize(select)).Contains("isCommandMenu"), "Ordinary Select behavior or wire compatibility changed");
        Reject(() => UI.Select("Value", [new SelectOption("one", "One", "one")], "value"));
        Reject(() => UI.CommandMenu("More", [new SelectOption("one", "One", "one", true)], "more"));
        Reject(() => UI.CommandMenu("More", [], "more"));
        Reject(() => UI.CommandMenu("More", Enumerable.Range(0, ProtocolConstants.MaximumSelectOptionCount + 1).Select(i => new SelectOption("item-" + i, "Item", "action-" + i)).ToArray(), "more"));
        Reject(() => UI.CommandMenu("More", [new SelectOption("one", "One", "same"), new SelectOption("two", "Two", "same")], "more"));
        Reject(() => UI.CommandMenu("More\n", [new SelectOption("one", "One", "one")], "more"));
        Reject(() => new WidgetView(element with { Options = [new SelectOption("one", "One", "one", true)] }).CreateSnapshot("invalid", 1));
        Invalid(menu with { Root = menu.Root with { SelectOptions = [menu.Root.SelectOptions[0] with { IsSelected = true }] } }, "command_menu_selected_option");
        Invalid(menu with { Root = menu.Root with { AccessibilityValue = "Refresh" } }, "command_menu_value_not_allowed");
        Invalid(menu with { Root = new ViewNode { Id = "text", Kind = ViewNodeKind.Text, Text = "Text", IsCommandMenu = true } }, "command_menu_not_allowed");
        Invalid(menu with { Root = menu.Root with { IsCommandMenu = null } }, "select_requires_one_selected_option");
        var explicitFalse = select with { ProtocolVersion = 114, Root = select.Root with { IsCommandMenu = false } };
        Invalid(explicitFalse, "feature_requires_version");

        const string generation = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        // Retain enough unchanged page content to exercise the incremental path;
        // tiny roots intentionally use the SDK's cheaper checkpoint fallback.
        var filler = UI.Text(new string('x', 2048), "unchanged-content");
        var before = new WidgetView(UI.Stack("root", UI.Select("More", [new SelectOption("refresh", "Refresh", "refresh", true)], "more"), filler))
            .CreateSnapshot("command-menu", 1) with { ProtocolVersion = 115 };
        var after = new WidgetView(UI.Stack("root", UI.CommandMenu("More", [new SelectOption("refresh", "Refresh", "refresh")], "more"), filler)).CreateSnapshot("command-menu", 2);
        var publication = WidgetPresentationDiff.Create(before, after, generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate);
        Check(publication.Update is not null && publication.Update.Operations.SelectMany(operation => operation.Properties ?? []).Any(change => change.Property == PresentationProperty.IsCommandMenu),
            "Mode change was omitted from an atomic publication");
        var applied = PresentationUpdateMaterializer.Apply(before, publication.Update!, generation);
        Check(applied.Root.Children[0].IsCommandMenu == true && applied.Root.Children[0].AccessibilityValue is null && applied.Root.Children[0].SelectOptions.All(option => !option.IsSelected), "Mode/options/value did not update atomically");
        var impact = PresentationPropertyMetadata.Impact(PresentationProperty.IsCommandMenu);
        Check(impact.HasFlag(PresentationPropertyImpact.Authority) && impact.HasFlag(PresentationPropertyImpact.Interaction) && impact.HasFlag(PresentationPropertyImpact.Accessibility), "Mode changes must retire stale interaction authority");
        var malformed = publication.Update! with { Operations = [new() { Kind = PresentationUpdateOperationKind.SetProperties, TargetId = "more", Properties = [new(PresentationProperty.IsCommandMenu, JsonSerializer.SerializeToElement("true"))] }] };
        Check(PresentationUpdateValidator.Validate(malformed).Count > 0, "Non-boolean command-menu update was accepted");
        var reversed = WidgetPresentationDiff.Create(after, before with { Sequence = 3 }, generation, 2, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update!;
        Check(PresentationUpdateMaterializer.Apply(after, reversed, generation).Root.Children[0].IsCommandMenu is null, "Ordinary Select mode could not be restored atomically");
        ViewSnapshot Preview(bool command)
        {
            SelectOption[] choices = [new("current", "Current", "current", IsSelected: !command), new("refresh", "Refresh", "refresh")];
            var control = command ? UI.CommandMenu("More", choices, "more") : UI.Select("More", choices, "more");
            return new WidgetView(UI.FocusPresentationSurface(UI.Button("Item", "open", "item").PresentOnFocus(UI.Stack("preview", control)),
                UI.Text("Empty", "empty"), "root") with { Preview = new() { EnterButton = ControllerButton.X } }).CreateSnapshot("pinned-menu", 1);
        }
        var pinnedInput = new PinnedActionInput(1, PinnedSurfaceContract.FullWidgetLayoutId, 1,
            new WidgetActionEvent("refresh", "more") { InputScopeId = "root", ControllerButton = ControllerButton.A, FocusedElementId = "more" });
        Check(PinnedActionContract.Resolve(Preview(false), pinnedInput) != PinnedActionContract.Resolve(Preview(true), pinnedInput),
            "Changing dropdown mode retained a captured pinned preview action with the same option");
        return Task.CompletedTask;
    }
    private static void Invalid(ViewSnapshot snapshot, string code) => Check(ViewSnapshotValidator.Validate(snapshot).Any(error => error.Code == code), "Missing validation: " + code);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid command/selection declaration was accepted"); }
}
