using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<WidgetNotificationRetention>))]
public enum WidgetNotificationRetention { Pending, BannerOnly }
[JsonConverter(typeof(JsonStringEnumConverter<WidgetNotificationInteraction>))]
public enum WidgetNotificationInteraction { OpenWidget, Informational }
[JsonConverter(typeof(JsonStringEnumConverter<WidgetNotificationVisibility>))]
public enum WidgetNotificationVisibility { OverlayHidden, Always }

/// <summary>Host-rendered content. Key replaces this widget's previous notification; it is not a global ID.</summary>
public sealed record WidgetNotification(string Key, string Title)
{
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
public sealed record WidgetNotificationCommand(string Key, WidgetNotification? Notification = null, bool UpdateOnly = false);
public sealed record WidgetNotificationReceipt(bool Accepted);

public static class WidgetNotificationContract
{
    public static bool ValidKey(string? key) => key is { Length: > 0 and <= 128 } &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    public static bool IsValid(WidgetNotification? value) => value is not null && ValidKey(value.Key) &&
        value.Title is { Length: > 0 and <= 256 } && value.Body is { Length: <= 2048 } && value.Metadata is { Length: <= 256 } &&
        Enum.IsDefined(value.Retention) && Enum.IsDefined(value.Interaction) && Enum.IsDefined(value.Visibility) &&
        value.DurationMilliseconds is >= 2000 and <= 10000 && value.BackgroundOpacityPercent is >= 40 and <= 100 &&
        (value.OpenIntent is null || value.Retention == WidgetNotificationRetention.Pending && value.Interaction == WidgetNotificationInteraction.OpenWidget &&
            value.OpenIntent.IsWellFormed()) &&
        (value.ArtworkUrl is null || value.ArtworkUrl.Length <= 2048 && Uri.TryCreate(value.ArtworkUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Host.Length > 0);
    public static bool IsValid(WidgetNotificationCommand? value) => value is not null && ValidKey(value.Key) &&
        (value.Notification is null || IsValid(value.Notification) && value.Key == value.Notification.Key);
}
