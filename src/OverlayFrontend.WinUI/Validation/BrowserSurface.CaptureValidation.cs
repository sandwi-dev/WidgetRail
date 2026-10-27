namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    internal long FixtureCaptureDocumentEpoch=>pageEpoch;
    internal bool FixtureCaptureDocumentReady(string expectedUrl,long? afterEpoch=null)=>
        (afterEpoch is null||pageEpoch>afterEpoch.Value)&&CurrentBackendDocumentOrigin is not null&&
        string.Equals(core?.Source,expectedUrl,StringComparison.Ordinal);

    // Retain only this exact transport's counters after a fixture retires its surface.
    internal Func<object> CaptureStatisticsReaderForStallValidation()
    {
        var transport=hostedCapture?.Transport??throw new InvalidOperationException("No fixture capture transport.");
        return ()=>transport.Statistics;
    }

    // Generated pixels only, used to exercise the real shared-buffer browser
    // contract without a native worker, selected window or capture permission.
    internal void PostGeneratedCaptureFrameForValidation(int width,int height,long epoch,long sequence)
    {
        if(width is <2 or >640||height is <2 or >360||core is null)throw new ArgumentOutOfRangeException(nameof(width));
        var pixels=new byte[checked(width*height*4)];
        using var buffer=core.Environment.CreateSharedBuffer((ulong)pixels.Length);
        using(var memory=buffer.OpenStream().AsStreamForWrite())memory.Write(pixels,0,pixels.Length);
        core.PostSharedBufferToScript(buffer,Microsoft.Web.WebView2.Core.CoreWebView2SharedBufferAccess.ReadOnly,
            System.Text.Json.JsonSerializer.Serialize(new{kind=1,width,height,time=sequence*40000,bytes=pixels.Length,epoch,sequence}));
    }
}
