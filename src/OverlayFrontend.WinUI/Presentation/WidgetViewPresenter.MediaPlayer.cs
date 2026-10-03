using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetPresentationSession;
namespace WidgetRail.OverlayFrontend.WinUI.Presentation;
internal sealed partial class WidgetViewPresenter
{
    private MediaPlayerView? FocusedMediaPlayer => bindings.Values.Select(binding => binding.Element).OfType<MediaPlayerView>().FirstOrDefault(media => media.HasDialog) ??
        (FocusedBinding() is { Element: MediaPlayerView media } binding && Navigable(binding) ? media : null);
    private void UpdateMediaPlayer(MediaPlayerView media, ViewNode node, string scope)
    {
        media.InteractionChanged = NotifyControllerGuideChanged;
        media.SetActive(presentationActive && !disposed, presentationInputEnabled && scope == activeScope && node.IsDisabled != true && !HasTransientControl);
        if (presentationActive && !disposed && !presentationOnly && Session is { } session && presentation is { } binding && MediaPlayerDefinition.FromNode(node) is { } definition)
            media.Apply(token => session.ResolveMediaPlayerSourceAsync(binding.Frame, node.Id, binding.Selection, binding.Projection, token), definition,
                () =>
                {
                    if (disposed || presentation?.SameSurface(binding) != true || !declarations.TryGetValue(node.Id, out var current) || MediaPlayerDefinition.FromNode(current.Node) != definition) return false;
                    try { return session.ResolveMediaPlayer(binding.Frame, node.Id, binding.Selection, binding.Projection) == definition; }
                    catch (Exception error) when (error is WidgetPresentationSessionException or ObjectDisposedException) { return false; }
                }, (binding.Frame.Authority.WidgetId, binding.Frame.Authority.RuntimeGeneration,
                    binding.Frame.Authority.SessionGeneration, binding.Frame.Authority.WorkerRun, binding.Selection));

    }
    private void RefreshMediaPlayers()
    {
        foreach (var binding in bindings.Values)
            if (binding.Element is MediaPlayerView media && declarations.TryGetValue(binding.Identity.Id, out var declaration))
                UpdateMediaPlayer(media, declaration.Node, binding.Identity.Scope);
    }
}
