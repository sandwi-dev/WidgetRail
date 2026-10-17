using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PobCompanion;

internal sealed class PobPreferences(string directory)
{
    private string PathName=>Path.Combine(directory,"preferences.json");
    internal bool OverlayBrowser
    {
        get
        {
            try
            {
                var file=new FileInfo(PathName);
                if(!file.Exists||file.Length>4096)return false;
                using var json=JsonDocument.Parse(File.ReadAllText(PathName));
                return json.RootElement.TryGetProperty("websites",out var value)&&value.GetString()=="overlay";
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException){return false;}
        }
        set=>BuildDocument.SaveAtomic(PathName,JsonSerializer.Serialize(new{version=1,websites=value?"overlay":"windows"}));
    }

    internal static WidgetIntentRequest Web(string url,bool extended=false)=>WidgetIntentRequest.Create(extended?WidgetIntentContracts.WebV2:WidgetIntentContracts.ForWebUrl(url),
        JsonSerializer.SerializeToElement(new{url}),WidgetIntentRouting.Windows);
}
