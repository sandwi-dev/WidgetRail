namespace WidgetRail.PlatformSettings;

/// <summary>Host confirmation preferences, never capture permission grants.</summary>
public sealed record CaptureConfirmationChoice(bool SkipScreenshots = false, bool SkipRecordings = false);

public sealed record CaptureConfirmationSettings
{
    public const int MaximumWidgets = 256;
    public const long MaximumResetVersion = 9_007_199_254_740_990L;
    public long ResetVersion { get; init; }
    public IReadOnlyDictionary<string, CaptureConfirmationChoice> Widgets { get; init; } =
        new Dictionary<string, CaptureConfirmationChoice>(StringComparer.Ordinal);

    public bool ShouldConfirm(string widgetId, bool recording) =>
        !Widgets.TryGetValue(widgetId, out var choice) || !(recording ? choice.SkipRecordings : choice.SkipScreenshots);

    /// <summary>Only an accepted explicit choice can be remembered. A later reset wins over an older dialog.</summary>
    public CaptureConfirmationSettings Remember(string widgetId, bool recording, bool proceeded, bool remember, long expectedResetVersion)
    {
        if (!ThemeIdentity.IsValid(widgetId)) throw new ArgumentException("Invalid widget identity.", nameof(widgetId));
        if (!proceeded || !remember || expectedResetVersion != ResetVersion) return this;
        if (!Widgets.ContainsKey(widgetId) && Widgets.Count >= MaximumWidgets) return this;
        var copy = new Dictionary<string, CaptureConfirmationChoice>(Widgets, StringComparer.Ordinal);
        var previous = copy.GetValueOrDefault(widgetId) ?? new();
        copy[widgetId] = recording ? previous with { SkipRecordings = true } : previous with { SkipScreenshots = true };
        return this with { Widgets = copy };
    }

    public CaptureConfirmationSettings Reset() => ResetVersion < MaximumResetVersion
        ? new() { ResetVersion = ResetVersion + 1 }
        : throw new InvalidOperationException("Capture confirmation reset version exhausted.");
}
