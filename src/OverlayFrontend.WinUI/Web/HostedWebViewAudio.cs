using Microsoft.Web.WebView2.Core;
namespace WidgetRail.OverlayFrontend.WinUI.Web;

/// <summary>Event-driven audio observation. Does not retain, activate or poll a document.</summary>
internal sealed class HostedWebViewAudio : IDisposable
{
    private CoreWebView2? core;
    private readonly Action changed;
    internal bool Playing { get; private set; }
    internal HostedWebViewAudio(CoreWebView2 core, Action changed)
    {
        this.core = core; this.changed = changed;
        core.IsDocumentPlayingAudioChanged += OnChanged;
        core.IsMutedChanged += OnChanged;
        Refresh();
    }
    private void OnChanged(object? sender, object args) => Refresh();
    private void Refresh()
    {
        bool next;
        try { next = core is { IsDocumentPlayingAudio: true, IsMuted: false }; }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { next = false; }
        if (next == Playing) return;
        Playing = next; changed();
    }
    public void Dispose()
    {
        var previous = core; core = null;
        if (previous is not null)
        {
            try { previous.IsDocumentPlayingAudioChanged -= OnChanged; previous.IsMutedChanged -= OnChanged; }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        }
        if (Playing) { Playing = false; changed(); }
    }
}
