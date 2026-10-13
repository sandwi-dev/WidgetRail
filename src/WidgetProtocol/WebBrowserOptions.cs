using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<BrowserChrome>))]
public enum BrowserChrome { Toolbar = 0, OnDemand = 1 }

[JsonConverter(typeof(JsonStringEnumConverter<BrowserStorage>))]
public enum BrowserStorage { Private = 0, Persistent = 1 }

[JsonConverter(typeof(JsonStringEnumConverter<BrowserBackgroundMode>))]
public enum BrowserBackgroundMode { Automatic = 0, KeepAlive = 1 }

[JsonConverter(typeof(JsonStringEnumConverter<BrowserExternalLinkMode>))]
public enum BrowserExternalLinkMode { InWidget = 0, OtherOriginsInWindows = 1 }

/// <summary>
/// Host-owned browser policy. These options never grant website permissions or
/// expose browser data to the widget. ProfileKey is namespaced by widget identity.
/// </summary>
public sealed record WebBrowserOptions
{
    public BrowserChrome Chrome { get; init; }
    public BrowserStorage Storage { get; init; }
    public string ProfileKey { get; init; } = "default";
    /// <summary>Requests bounded background residency; the user can stop the session.</summary>
    public BrowserBackgroundMode Background { get; init; }
    /// <summary>OtherOriginsInWindows sends user-initiated offsite top-level links to Windows.</summary>
    public BrowserExternalLinkMode ExternalLinks { get; init; }

    public bool IsWellFormed() => Enum.IsDefined(Chrome) && Enum.IsDefined(Storage) &&
        Enum.IsDefined(Background) && Enum.IsDefined(ExternalLinks) &&
        ProfileKey is { Length: > 0 and <= 64 } &&
        ProfileKey.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_');

    /// <summary>Explicit starting configuration for a persistent full-page webapp.</summary>
    public static WebBrowserOptions WebApp => new()
    {
        Chrome = BrowserChrome.OnDemand,
        Storage = BrowserStorage.Persistent,
        ExternalLinks = BrowserExternalLinkMode.OtherOriginsInWindows,
    };
}
