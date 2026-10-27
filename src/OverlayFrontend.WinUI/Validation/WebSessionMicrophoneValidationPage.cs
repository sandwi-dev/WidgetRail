using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WebSessionMicrophoneValidationPage:Page,IAsyncDisposable
{
    private Task? running;
    internal WebSessionMicrophoneValidationPage(string output)
    {
        Content=new TextBlock{Text="Testing source chooser and microphone consent with synthetic data…",Margin=new(24)};
        Loaded+=(_,_)=>running??=RunAsync(Path.GetFullPath(output));
    }
    private async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var checks=new List<string>();string? error=null;
        BrowserSurface.FixturePage=_=>"<!doctype html><title>Synthetic microphone fixture</title><body>Local synthetic microphone fixture. No account or physical device.</body>";
        try
        {
            await WebCapturePickerValidation.RunAsync(this,checks);
            await WebSessionMicrophoneValidation.RunAsync(output,checks);
            await WebSessionCaptureValidation.RunAsync(output,checks);
        }
        catch(Exception failure){error=failure.ToString();}
        finally{BrowserSurface.FixturePage=null;}
        var path=Path.Combine(output,"result.json");
        await File.WriteAllTextAsync(path+".tmp",JsonSerializer.Serialize(new{checks,error},new JsonSerializerOptions{WriteIndented=true}));
        File.Move(path+".tmp",path,true);
        ((TextBlock)Content).Text=error is null?$"Passed {checks.Count} synthetic consent and document lifetime checks.":error;
    }
    public async ValueTask DisposeAsync()
    { if(running is not null)await running; }
}
