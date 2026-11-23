using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Motion;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private string? nativeTextSearchResult;
    private async Task TextProviderLifetimeAsync(string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var retained = new List<(ITextProvider Provider, WeakReference Owner)>();
        var ranges = new List<ITextRangeProvider>();
        Exception? failure = null;
        try
        {
            for (var index = 0; index < 24; index++)
            {
                retained.Add(await CaptureTextProviderAsync(!Environment.GetCommandLineArgs().Contains("--plain-text-provider-lifetime")));
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                await Task.Delay(30);
                File.WriteAllText(output + ".progress", $"query {index}; retired owners={retained.Count(item=>!item.Owner.IsAlive)}");
                foreach (var item in retained)
                {
                    // UIA clients may retain text patterns after navigation.
                    // A retired element may return ElementNotAvailable, but
                    // must never dereference a freed native text owner.
                    try { _ = item.Provider.GetSelection(); _ = item.Provider.DocumentRange?.GetText(128); }
                    catch (System.Runtime.InteropServices.COMException) { }
                }
                if(retained[^1].Provider.DocumentRange is {} range)ranges.Add(range.Clone());
            }
            Check(true, "Retained native text providers survive repeated owner departure and collection");
            retained.Clear();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            foreach(var range in ranges)
            {
                Check(range.GetText(128).Contains("Retiring"),"A cloned range retains its text after the original provider is released");
                Check(TextSearch(range)==nativeTextSearchResult,"Range search preserves the native result, including unsupported operations, after departure");
            }
        }
        catch (Exception error) { failure = error; }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<(ITextProvider, WeakReference)> CaptureTextProviderAsync(bool rich)
    {
        FrameworkElement text;
        if (rich)
        {
            var document = new WidgetRichTextView();
            document.UpdateSpans((FrameworkElement[])[new TextBlock { Text = "Retiring rich text" }]); text = document;
        }
        else text = new TextBlock { Text = "Retiring plain text", IsTextSelectionEnabled = false };
        FrameworkElement surface=text is TextBlock?new WidgetMotionHost(text,animated:false):text;
        host.Children.Add(surface); UpdateLayout(); await Task.Delay(20);
        var peer = text is TextBlock && !Environment.GetCommandLineArgs().Contains("--stock-text-provider-lifetime")
            ? FrameworkElementAutomationPeer.CreatePeerForElement(surface).GetChildren().Single()
            : FrameworkElementAutomationPeer.CreatePeerForElement(text);
        Check(peer is NativeTextAutomationPeer,"Production presentation exposes a lifetime-protected text peer");
        var provider = (ITextProvider)peer.GetPattern(PatternInterface.Text);
        var nativeDocument=text is WidgetRichTextView richText?richText.Document:text;
        var nativeProvider=(ITextProvider)FrameworkElementAutomationPeer.CreatePeerForElement(nativeDocument).GetPattern(PatternInterface.Text);
        nativeTextSearchResult=TextSearch(nativeProvider.DocumentRange);
        _ = provider.GetSelection();
        host.Children.Remove(surface);
        return (provider, new WeakReference(text));
    }
    private static string TextSearch(ITextRangeProvider range)
    {
        try { return "text:"+range.FindText("Retiring",false,false)?.GetText(8); }
        catch(System.Runtime.InteropServices.COMException error) { return "HRESULT:"+error.HResult.ToString("X8"); }
    }
}
