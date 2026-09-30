using Microsoft.UI.Xaml.Automation;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PresentationSurfaceValidationPage
{
    private async Task BackgroundRetentionAsync()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolve = presenter.ResolveArtworkAsync!;
        presenter.ResolveArtworkAsync = async (handle, token) =>
        {
            if (handle == "replacement") { entered.TrySetResult(); await release.Task; }
            return await resolve(handle, token);
        };
        try
        {
            ApplyRetained(); await Focus("a");
            await Until(() => BackgroundSurface()?.ArtworkSource is not null);
            var surface = BackgroundSurface()!;
            var previous = surface.ArtworkSource;
            await Focus("outside");
            Check(ReferenceEquals(previous, surface.ArtworkSource), "leaving the background scope cleared pixels");
            ApplyRetained(omitSource: true); await Focus("outside");
            Check(ReferenceEquals(surface, BackgroundSurface()) && ReferenceEquals(previous, surface.ArtworkSource),
                "removing the selected source cleared its owning background");
            ApplyRetained(artwork: "missing"); await Focus("a"); await Task.Delay(100);
            Check(ReferenceEquals(previous, surface.ArtworkSource), "unavailable replacement cleared retained pixels");
            ApplyRetained(artwork: "replacement"); await Focus("a");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(ReferenceEquals(previous, surface.ArtworkSource), "pending replacement cleared retained pixels");
            release.TrySetResult();
            await Until(() => surface.ArtworkSource is not null && !ReferenceEquals(previous, surface.ArtworkSource));
            ++checks;

            ApplyRetained(omitSource: true, nested: true); await Focus("outside");
            previous = surface.ArtworkSource;
            await Focus("inner");
            await Until(() => Descendants(presenter).OfType<WidgetPresentationSurface>()
                .Any(s => AutomationProperties.GetAutomationId(s) == "Widget.nested" && s.ArtworkSource is not null));
            Check(ReferenceEquals(previous, surface.ArtworkSource), "nested background replaced outer pixels");
            ApplyRetained(omitSource: true);
            await Focus("outside");
            Check(ReferenceEquals(previous, surface.ArtworkSource), "removing nested surface cleared parent pixels");

            // Production sections replace their entire foreground tree while
            // the same declared background remains outside the motion group.
            presenter.ApplyAppearance(WidgetRail.PlatformSettings.AppearanceSettings.Default with
                { Motion = WidgetRail.PlatformSettings.MotionPreference.Full }, true);
            ApplyRetained(section: 0); await Focus("a");
            surface = BackgroundSurface()!; previous = surface.ArtworkSource;
            var unloaded = 0;
            surface.Unloaded += (_, _) => ++unloaded;
            for (var section = 1; section <= 3; ++section)
            {
                ApplyRetained(omitSource: true, section: section);
                await Focus("inside");
                Check(ReferenceEquals(surface, BackgroundSurface()) && ReferenceEquals(previous, surface.ArtworkSource) && unloaded == 0,
                    "section replacement unloaded or blanked its surviving background owner");
                if (presenter.TransitionPlayback is { } motion) await motion;
            }
            ApplyRetained(omitSource: true, retain: false); await Focus("outside");
            await Until(() => surface.ArtworkSource is null); ++checks;
            ApplyRetained(); await Focus("a"); await Until(() => surface.ArtworkSource is not null);
            ApplyRetained(omitSource: true, scope: "new-scope"); await Focus("outside");
            Check(!ReferenceEquals(surface, BackgroundSurface()) && BackgroundSurface()!.ArtworkSource is null,
                "new declared scope inherited old background pixels");

            ApplyRetained(); await Focus("a"); await Until(() => BackgroundSurface()!.ArtworkSource is not null);
            surface = BackgroundSurface()!;
            ApplyRetained(omitSource: true, enclosing: true); await Focus("outside");
            Check(!ReferenceEquals(surface, BackgroundSurface()) && BackgroundSurface()!.ArtworkSource is null,
                "reparenting a surface across a presentation boundary inherited old pixels");

            ApplyRetained(); await Focus("a"); await Until(() => BackgroundSurface()!.ArtworkSource is not null);
            ApplyRetained(omitSource: true, runtime: "new-runtime"); await Focus("outside");
            Check(BackgroundSurface()!.ArtworkSource is null, "new runtime inherited old background pixels");
            ApplyRetained(); await Focus("a"); await Until(() => BackgroundSurface()!.ArtworkSource is not null);
            ApplyRetained(omitSource: true, surfaceId: "another-background"); await Focus("outside");
            Check(Descendants(presenter).OfType<WidgetPresentationSurface>().All(s => s.ArtworkSource is null),
                "unrelated replacement surface inherited pixels by sibling position");
            ApplyRetained(omitSource: true); await Focus("outside");
            Check(BackgroundSurface()!.ArtworkSource is null, "removed surface resurrected old pixels");
        }
        finally { release.TrySetResult(); presenter.ResolveArtworkAsync = resolve; }
    }

    private void ApplyRetained(bool omitSource = false, string artwork = "a", bool retain = true,
        string scope = "root", string runtime = "runtime", string surfaceId = "background", bool nested = false, bool enclosing = false, int? section = null)
    {
        ViewNode Button(string id, string? handle = null) => new()
            { Id = id, Kind = ViewNodeKind.Button, Text = id, ActionId = id, FocusBackgroundArtworkHandle = handle };
        var children = new List<ViewNode>();
        if (!omitSource) children.Add(Button("a", artwork));
        if (nested) children.Add(new() { Id = "nested", Kind = ViewNodeKind.BackgroundSurface,
            UsesFocusedDescendantArtwork = true, Children = (ViewNode[])[Button("inner", "b")] });
        children.Add(Button("inside"));
        ViewNode background = new() { Id = surfaceId, Kind = ViewNodeKind.BackgroundSurface, UsesFocusedDescendantArtwork = true,
            RetainLastPresentation = retain, Children = (ViewNode[])[
                new() { Id = "foreground", Kind = ViewNodeKind.Row, Children = children,
                    Transition = section is { } order ? new("destinations", order.ToString(), order) : null }] };
        if (enclosing) background = new() { Id = "enclosing", Kind = ViewNodeKind.BackgroundSurface, Children = (ViewNode[])[background] };
        Present(new()
        {
            WidgetInstanceId = "surface.instance", Sequence = ++sequence, ActiveInputScopeId = scope, InitialFocusId = "outside",
            Root = new() { Id = "root", Kind = ViewNodeKind.Stack, InputScopeId = scope, Children = (ViewNode[])[background, Button("outside")] }
        }, runtime);
    }
}
