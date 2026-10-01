namespace WidgetRail.WidgetSdk;

public static partial class UI
{
    /// <summary>
    /// A label/description and control pair with stable child identities. The host
    /// stacks the pair at narrow widths without creating a second focus target.
    /// Give the control an accessible name matching the visible label.
    /// </summary>
    public static GridElement SettingsField(string id, string label, WidgetElement control,
        string? description = null, double minimumColumnWidth = 280)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(control);
        var copy = new List<WidgetElement>
        {
            Text(label, StableIdentifier.Child(id, "label")).Classes("wrail-setting-row__label"),
        };
        if (!string.IsNullOrWhiteSpace(description))
            copy.Add(Text(description, StableIdentifier.Child(id, "description")).Classes("wrail-setting-row__description"));
        return ResponsiveGrid(id, minimumColumnWidth, 2,
            Stack(StableIdentifier.Child(id, "copy"), copy.ToArray()).Classes("wrail-setting-row__copy"),
            Stack(StableIdentifier.Child(id, "control"), control).Classes("wrail-setting-row__control"))
            .Classes("wrail-setting-row");
    }
}
