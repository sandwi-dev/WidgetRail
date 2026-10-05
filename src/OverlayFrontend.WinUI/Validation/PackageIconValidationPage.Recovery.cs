using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PackageIconValidationPage
{
    private async Task RecoveryAsync()
    {
        var host = (StackPanel)Content;
        using var view = new WidgetPackageIconView();
        host.Children.Add(view);
        var calls = 0;
        ViewNode Node(string label = "Recovery") => new() { Id = "recovery", Kind = ViewNodeKind.Icon,
            Glyph = WidgetGlyph.Music, PackageIcon = new("red", WidgetPackageIconColorMode.OriginalColor), AccessibilityLabel = label };
        try
        {
            await Until(() => view.IsLoaded);
            view.Update(Node(), (_, _) => ++calls == 1
                ? Task.FromException<WidgetPresentationPackageIcon>(new WidgetPresentationSessionException("package_icon_saturated", "Fixture capacity"))
                : Task.FromResult(Bytes("red")), "recovery-1", null);
            await Until(() => view.Content is Image { Source: SvgImageSource });
            Check(calls == 2, "transient capacity failure recovers without replacing or refreshing the icon control");
            var good = view.Content;
            view.Update(Node("New label"), (_, _) => throw new InvalidOperationException("Compatible label must not reload"), "recovery-1", null);
            Check(ReferenceEquals(good, view.Content) && calls == 2, "compatible label update preserves ready SVG without fallback flash");

            calls = 0;
            var unavailable = true;
            var failed = false;
            view.Update(Node(), (_, _) =>
            {
                ++calls;
                return unavailable ? Task.FromException<WidgetPresentationPackageIcon>(new TimeoutException("Fixture timeout")) : Task.FromResult(Bytes("red"));
            }, "recovery-2", _ => failed = true);
            await Until(() => failed);
            Check(calls == 4 && view.Content is FontIcon, "transient attempts stop after a bounded batch with semantic fallback");
            unavailable = false;
            view.RetryTransientFailure();
            await Until(() => view.Content is Image);
            Check(calls == 5, "presentation reentry can rearm an exhausted transient failure for the same identity");

            calls = 0; failed = false;
            view.Update(Node(), (_, _) => { ++calls; throw new InvalidDataException("Fixture invalid bytes"); }, "recovery-3", _ => failed = true);
            await Until(() => failed);
            view.RetryTransientFailure();
            await Task.Delay(180);
            Check(calls == 1 && view.Content is FontIcon, "invalid asset data is terminal and never retried on presentation reentry");

            calls = 0;
            view.Update(Node(), (_, _) => { ++calls; throw new TimeoutException("Fixture cancelled retry"); }, "recovery-4", null);
            view.Update(Node(), (_, _) => Task.FromResult(Bytes("blue")), "recovery-5", null);
            await Until(() => view.Content is Image);
            var replacement = view.Content;
            await Task.Delay(250);
            Check(calls == 1 && ReferenceEquals(replacement, view.Content), "replacing authority cancels an old delayed retry and prevents late fallback publication");
        }
        finally { host.Children.Remove(view); }
    }
}
