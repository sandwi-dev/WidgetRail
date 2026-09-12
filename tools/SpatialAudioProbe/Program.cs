using System.Text.Json;
using Windows.Media.Audio;
using Windows.Media.Devices;
using WidgetRail.WindowsAudioProvider;

// Developer-only feasibility probe. No arguments performs read-only inspection.
// A requested switch always attempts to restore the initial choice in finally.
if (args.Length > 1 || (args.Length == 1 && args[0] is not ("--test-sonic" or "--test-atmos")))
{
    Console.Error.WriteLine("Usage: SpatialAudioProbe [--test-sonic|--test-atmos]");
    return 2;
}
var endpoint = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
var configuration = SpatialAudioDeviceConfiguration.GetForDeviceId(endpoint);
var formats = new Dictionary<string, string>
{
    ["Windows Sonic"] = SpatialAudioFormatSubtype.WindowsSonic,
    ["Dolby Atmos for headphones"] = SpatialAudioFormatSubtype.DolbyAtmosForHeadphones,
    ["Dolby Atmos for home theater"] = SpatialAudioFormatSubtype.DolbyAtmosForHomeTheater,
};
// Endpoint identity is intentionally not printed.
Console.WriteLine(JsonSerializer.Serialize(new
{
    Supported = configuration.IsSpatialAudioSupported,
    Selected = configuration.DefaultSpatialAudioFormat,
    Active = configuration.ActiveSpatialAudioFormat,
    Formats = formats.Select(format => new { Name = format.Key, Format = format.Value,
        Supported = configuration.IsSpatialAudioFormatSupported(format.Value) }).ToArray(),
}));
if (args.Length == 0) return 0;
var target = args[0] == "--test-sonic"
    ? SpatialAudioFormatSubtype.WindowsSonic : SpatialAudioFormatSubtype.DolbyAtmosForHeadphones;
var original = configuration.DefaultSpatialAudioFormat;
var notifications = 0;
Windows.Foundation.TypedEventHandler<SpatialAudioDeviceConfiguration, object> handler =
    (_, _) => Interlocked.Increment(ref notifications);
configuration.ConfigurationChanged += handler;
var succeeded = false;
var restored = false;
await using var provider = new WindowsAudioPlatformBackend();
try
{
    var sessionsBefore = (await provider.GetAudioSessionsAsync(CancellationToken.None)).Count;
    var result = await configuration.SetDefaultSpatialAudioFormatAsync(target);
    await Task.Delay(500);
    succeeded = result.Status == SetDefaultSpatialAudioFormatStatus.Succeeded &&
        configuration.DefaultSpatialAudioFormat == target;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Operation = args[0], Status = result.Status.ToString(), Confirmed = succeeded,
        Notifications = Volatile.Read(ref notifications), SessionsBefore = sessionsBefore,
        SessionsAfter = (await provider.GetAudioSessionsAsync(CancellationToken.None)).Count,
    }));
}
catch (Exception error)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Operation = args[0], Error = error.GetType().Name, error.HResult }));
}
finally
{
    try
    {
        var result = await configuration.SetDefaultSpatialAudioFormatAsync(original);
        await Task.Delay(500);
        restored = configuration.DefaultSpatialAudioFormat == original;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Operation = "RestoreOriginal", Status = result.Status.ToString(), Confirmed = restored,
            Notifications = Volatile.Read(ref notifications),
            SessionsAfterRestore = (await provider.GetAudioSessionsAsync(CancellationToken.None)).Count,
        }));
    }
    finally { configuration.ConfigurationChanged -= handler; }
}
return !restored ? 3 : succeeded ? 0 : 1;
