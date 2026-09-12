using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using Windows.Media.Audio;
using Windows.Foundation.Metadata;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsAudioProvider;

// Owned by the Core Audio MTA thread. WinRT callbacks only invalidate the owner.
// Every optional spatial failure is contained here, never promoted to an audio
// provider failure. Supported format does not imply an available license.
internal sealed class WindowsSpatialAudioAdapter(Action changed) : IDisposable
{
    private SpatialAudioDeviceConfiguration? _configuration;
    private string? _device;
    private readonly Dictionary<string, (string Name, string Subtype)> _formats = new(StringComparer.Ordinal);

    private void Bind(string? device)
    {
        if (device == _device && _configuration is not null) return;
        Dispose();
        if (string.IsNullOrWhiteSpace(device)) return;
        // GetForDeviceId requires a WinRT device-interface ID. Supplying the
        // MMDevice endpoint ID may silently report no spatial support.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var devices = DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector(),
                new[] { SpatialAudioDeviceIdentity.InstanceIdProperty })
            .AsTask(deadline.Token).WaitAsync(deadline.Token).GetAwaiter().GetResult();
        var interfaceId = SpatialAudioDeviceIdentity.Resolve(device, devices.Select(item =>
            new SpatialAudioDeviceInterface(item.Id,
                item.Properties.TryGetValue(SpatialAudioDeviceIdentity.InstanceIdProperty, out var instance)
                    ? instance as string : null, item.IsEnabled)));
        if (interfaceId is null) throw new InvalidOperationException("Spatial audio device interface is unavailable.");
        var configuration = SpatialAudioDeviceConfiguration.GetForDeviceId(interfaceId);
        _configuration = configuration;
        _device = device;
        configuration.ConfigurationChanged += OnChanged;
    }

    private void RefreshFormats()
    {
        var configuration = _configuration;
        if (configuration is null) return;
        // Device capabilities can settle after connection or change without
        // replacing the endpoint. Recompute from each authoritative refresh.
        _formats.Clear();
        _formats["off"] = ("Off", Guid.Empty.ToString("B"));
        foreach (var (id, name, property) in new[] {
            ("sonic", "Windows Sonic for headphones", "WindowsSonic"),
            ("atmos-headphones", "Dolby Atmos for headphones", "DolbyAtmosForHeadphones"),
            ("atmos-home", "Dolby Atmos for home theater", "DolbyAtmosForHomeTheater"),
            ("atmos-speakers", "Dolby Atmos for speakers", "DolbyAtmosForSpeakers"),
            ("dts-headphones", "DTS Headphone:X", "DTSHeadphoneX"),
            ("dts-home", "DTS:X for home theater", "DTSXForHomeTheater"),
            ("dts-ultra", "DTS:X Ultra", "DTSXUltra"),
        })
        {
            try
            {
                // Optional getters vary with the installed Windows SDK/OS.
                if (ResolveFormatSubtype(property) is string subtype &&
                    configuration.IsSpatialAudioFormatSupported(subtype))
                    _formats[id] = (name, subtype);
            }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }
    }

    // The 19041 managed projection predates the 20348 DTS:X getter. Format
    // subtypes are stable identifiers: use the value returned by the newer
    // documented getter only when Windows advertises that property at runtime.
    // Verified against the 26100 projection on the affected SAMSUNG endpoint.
    internal const string DtsXForHomeTheaterSubtype = "{10201B4A-3322-4967-BF40-2CAA9BAFCA44}";

    internal static string? ResolveFormatSubtype(string property)
    {
        if (typeof(SpatialAudioFormatSubtype).GetProperty(property)?.GetValue(null) is string subtype)
            return subtype;
        return property == "DTSXForHomeTheater" && ApiInformation.IsPropertyPresent(
            "Windows.Media.Audio.SpatialAudioFormatSubtype", property)
                ? DtsXForHomeTheaterSubtype : null;
    }

    private string Token(string subtype) => _formats.FirstOrDefault(pair =>
        string.Equals(pair.Value.Subtype, subtype, StringComparison.OrdinalIgnoreCase)).Key ?? "other";

    internal NativeSpatialAudioSnapshot? Read(string? device)
    {
        try
        {
            Bind(device);
            if (_configuration is null || _device is null) return null;
            RefreshFormats();
            var selected = Token(_configuration.DefaultSpatialAudioFormat);
            var formats = _formats.Select(pair => new AudioSpatialFormat(pair.Key, pair.Value.Name)).ToList();
            if (selected == "other") formats.Add(new("other", "Other spatial format"));
            return new(_device, _configuration.IsSpatialAudioSupported, selected,
                Token(_configuration.ActiveSpatialAudioFormat), formats);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Dispose(); return null; }
    }

    internal string Set(string device, string format, Func<string?> currentDevice)
    {
        try
        {
            if (currentDevice() != device) return "resource_not_found";
            Bind(device);
            RefreshFormats();
            if (_configuration is null || !_formats.TryGetValue(format, out var target))
                return "spatial_not_supported";
            // The owner is not the UI thread. Cancellation bounds the WinRT
            // request; no failed or timed-out request is retried.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var result = _configuration.SetDefaultSpatialAudioFormatAsync(target.Subtype)
                .AsTask(deadline.Token).WaitAsync(deadline.Token).GetAwaiter().GetResult();
            var code = result.Status switch
            {
                SetDefaultSpatialAudioFormatStatus.Succeeded => "ok",
                SetDefaultSpatialAudioFormatStatus.AccessDenied => "spatial_access_denied",
                SetDefaultSpatialAudioFormatStatus.LicenseExpired => "spatial_license_expired",
                SetDefaultSpatialAudioFormatStatus.LicenseNotValidForAudioEndpoint => "spatial_license_required",
                SetDefaultSpatialAudioFormatStatus.NotSupportedOnAudioEndpoint => "spatial_not_supported",
                _ => "spatial_unavailable",
            };
            if (code != "ok") return code;
            if (currentDevice() != device) return "resource_not_found";
            return Token(_configuration.DefaultSpatialAudioFormat) == format ? "ok" : "spatial_unconfirmed";
        }
        catch (OperationCanceledException) { return "spatial_timeout"; }
        catch (Exception error) when (error is not OutOfMemoryException) { return "spatial_unavailable"; }
    }

    private void OnChanged(SpatialAudioDeviceConfiguration sender, object args)
    {
        try { changed(); }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }

    public void Dispose()
    {
        var configuration = _configuration;
        _configuration = null;
        _device = null;
        _formats.Clear();
        if (configuration is null) return;
        try { configuration.ConfigurationChanged -= OnChanged; }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }
}
