using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<WidgetNotificationRetention>))]
public enum WidgetNotificationRetention { Pending, BannerOnly }
[JsonConverter(typeof(JsonStringEnumConverter<WidgetNotificationInteraction>))]
public enum WidgetNotificationInteraction { OpenWidget, Informational }
[JsonConverter(typeof(JsonStringEnumConverter<WidgetNotificationVisibility>))]
public enum WidgetNotificationVisibility { OverlayHidden, Always, SourceHidden }

/// <summary>Host-rendered content. Key replaces this widget's previous notification; it is not a global ID.</summary>
public sealed record WidgetNotification(string Key, string Title)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CategoryId { get; init; }
    public string Body { get; init; } = "";
    public string Metadata { get; init; } = "";
    public string? ArtworkUrl { get; init; }
    public bool ShowWidgetIcon { get; init; } = true;
    public WidgetNotificationRetention Retention { get; init; } = WidgetNotificationRetention.Pending;
    public WidgetNotificationInteraction Interaction { get; init; } = WidgetNotificationInteraction.OpenWidget;
    public WidgetNotificationVisibility Visibility { get; init; } = WidgetNotificationVisibility.OverlayHidden;
    public int DurationMilliseconds { get; init; } = 4000;
    public int BackgroundOpacityPercent { get; init; } = 100;
    /// <summary>Optional declared navigation, executed only when the user opens this pending notification.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WidgetIntentRequest? OpenIntent { get; init; }
}
public sealed record WidgetNotificationCommand(string Key, WidgetNotification? Notification = null, bool UpdateOnly = false)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfigureCategoryId { get; init; }
}
/// <summary>Bounded manifest metadata; defaults are adopted once by the host and never replace saved choices.</summary>
public sealed record WidgetNotificationCategory(string Id, string Name, string Description = "", bool EnabledByDefault = true,
    int DefaultOpacityPercent = 100, WidgetNotificationRetention? Retention = null);
public sealed record WidgetNotificationReceipt(bool Accepted);

public static class WidgetNotificationContract
{
    public const string DefaultCategoryId = "general";
    public const int MaximumCategories = 8;
    public static bool ValidCategoryId(string? id) => id is { Length: > 0 and <= 64 } && ValidKey(id);
    public static bool ValidCategories(IReadOnlyList<WidgetNotificationCategory>? categories) => categories is not null &&
        categories.Count <= MaximumCategories && categories.All(c => c is not null && ValidCategoryId(c.Id) && c.Id != DefaultCategoryId &&
            !string.IsNullOrWhiteSpace(c.Name) && c.Name.Length <= 64 && SafeText(c.Name) && c.Description is { Length: <= 256 } && SafeText(c.Description) && c.DefaultOpacityPercent is >= 40 and <= 100 && (c.Retention is null || Enum.IsDefined(c.Retention.Value))) &&
        categories.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == categories.Count;

    /// <summary>General accepts existing callers. A named category may restrict notices to one retention kind.</summary>
    public static bool AllowsCommand(IReadOnlyList<WidgetNotificationCategory> categories, WidgetNotificationCommand command)
    {
        var id = command.ConfigureCategoryId ?? command.Notification?.CategoryId ?? DefaultCategoryId;
        if (id == DefaultCategoryId) return true;
        var category = categories.FirstOrDefault(item => item.Id == id);
        return category is not null && (command.Notification is null || category.Retention is null || category.Retention == command.Notification.Retention);
    }

    private static bool SafeText(string value) => value.All(c => !char.IsControl(c) &&
        char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format);
    public static bool ValidKey(string? key) => key is { Length: > 0 and <= 128 } &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    public static bool IsValid(WidgetNotification? value) => value is not null && ValidKey(value.Key) && (value.CategoryId is null || ValidCategoryId(value.CategoryId)) &&
        value.Title is { Length: > 0 and <= 256 } && value.Body is { Length: <= 2048 } && value.Metadata is { Length: <= 256 } &&
        Enum.IsDefined(value.Retention) && Enum.IsDefined(value.Interaction) && Enum.IsDefined(value.Visibility) &&
        value.DurationMilliseconds is >= 2000 and <= 10000 && value.BackgroundOpacityPercent is >= 40 and <= 100 &&
        (value.OpenIntent is null || value.Retention == WidgetNotificationRetention.Pending && value.Interaction == WidgetNotificationInteraction.OpenWidget &&
            value.OpenIntent.IsWellFormed()) &&
        (value.ArtworkUrl is null || value.ArtworkUrl.Length <= 2048 && Uri.TryCreate(value.ArtworkUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Host.Length > 0);
    public static bool IsValid(WidgetNotificationCommand? value) => value is not null && ValidKey(value.Key) &&
        (value.ConfigureCategoryId is { } category
            ? ValidCategoryId(category) && value.Key == category && value.Notification is null && !value.UpdateOnly
            : value.Notification is null || IsValid(value.Notification) && value.Key == value.Notification.Key);
}
