using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetBridge;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed record HostedPagePalette(string Origin, string Scheme, bool HighContrast, Dictionary<string,string> Colors, double TextScale);

internal sealed partial class BrowserSurface
{
    private string? hostedThemeScript;
    private bool hostedThemeApplying;
    private long hostedThemeRevision;
    private long hostedThemeAppliedRevision;

    internal void ApplyHostedTheme(ShellChromePalette palette, IReadOnlyDictionary<string,BridgeNodeRenderStyles>? styles, double textScale)
    {
        if (!IsHostedPackage) return;
        static string Hex(Color color) => $"#{color.R:x2}{color.G:x2}{color.B:x2}";
        var colors = new Dictionary<string,string>
        {
            ["surface"]=Hex(palette.Surface), ["text"]=Hex(palette.Text),
            ["muted"]=Hex(palette.Muted), ["focus"]=Hex(palette.Focus),
        };
        foreach(var name in new[]{"red","blue","green","gold","purple","orange"})
        {
            var color=palette.Text;
            if(!palette.HighContrast && styles?.GetValueOrDefault("data-"+name)?.Base.GetValueOrDefault("color") is {} value &&
                NativeComputedStyleAdapter.TryColor(value.Text,out var resolved)) color=resolved;
            colors["data-"+name]=Hex(color);
        }
        var scheme=(.2126*palette.Surface.R+.7152*palette.Surface.G+.0722*palette.Surface.B)>140?"light":"dark";
        var data=JsonSerializer.Serialize(new HostedPagePalette(new Uri(document.Url).GetLeftPart(UriPartial.Authority),scheme,palette.HighContrast,colors,Math.Clamp(textScale,.85,1.5)),BrowserJsonContext.Default.HostedPagePalette);
        var script="(()=>{const p="+data+";if(location.origin!==p.Origin||!document.documentElement)return;const r=document.documentElement;"+
            "for(const [k,v] of Object.entries(p.Colors))r.style.setProperty('--widgetrail-'+k,v);r.style.setProperty('--widgetrail-text-scale',String(p.TextScale));r.style.colorScheme=p.Scheme;"+
            "r.dataset.widgetrailContrast=p.HighContrast?'high':'standard';window.dispatchEvent(new Event('widgetrailthemechange'));})()";
        if(hostedThemeScript==script)return;
        hostedThemeScript=script;hostedThemeRevision++;
        _=ApplyHostedPageThemeAsync();
    }

    private async Task ApplyHostedPageThemeAsync()
    {
        if(hostedThemeApplying || core is null || !IsHostedPackage || !IsHostedDocument(core.Source))return;
        hostedThemeApplying=true;
        try
        {
            while(core is {} current && IsHostedDocument(current.Source) && hostedThemeScript is {} script && hostedThemeAppliedRevision!=hostedThemeRevision && !lifetime.IsCancellationRequested)
            {
                var revision=hostedThemeRevision;
                await current.ExecuteScriptAsync(script);
                hostedThemeAppliedRevision=revision;
            }
        }
        catch(Exception error) when(error is InvalidOperationException or System.Runtime.InteropServices.COMException or OperationCanceledException)
        { /* Navigation/retirement can invalidate a page. The next load reapplies its theme. */ }
        finally{hostedThemeApplying=false;}
    }
}
