using Microsoft.UI.Xaml.Hosting;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI
{
    public sealed partial class MainWindow
    {
        private void EnableOverlayMotionValidation(Shell.OverlayShellPage page, string path)
        {
            var started = false;
            page.Loaded += async (_, _) =>
            {
                if (started) return;
                started = true;
                var checks = new List<string>();
                var originalAppearance = page.Appearance;
                try
                {
                    await page.WaitForNativeStartupAsync().WaitAsync(TimeSpan.FromSeconds(45));
                    await Until(() => page.NativeStartupHasContent && overlayMotion?.Playback is { IsCompleted: true });
                    originalAppearance = page.Appearance;
                    page.ApplyOverlayMotionValidationAppearance(originalAppearance with
                    {
                        Motion = MotionPreference.Full, WidgetAnimationSpeed = .5,
                        AnimateWidgetSwitching = false, SectionAnimation = WidgetSectionAnimation.None,
                    });
                    var shellVisual = ElementCompositionPreview.GetElementVisual(ShellRoot);
                    var backdropVisual = ElementCompositionPreview.GetElementVisual(desktopBackdrop!.MotionLayer);
                    Check(overlayRequestedVisible && AppWindow.IsVisible && page.OverlayMotionValidationVisible,
                        "startup opens the production shell through the visibility owner");
                    Check(shellVisual.Opacity == 1 && shellVisual.Scale == System.Numerics.Vector3.One,
                        "settled entrance restores full opacity and identity scale");
                    var settled = overlayMotion!.Playback;
                    ShowOverlay();
                    await Task.Delay(40);
                    Check(ReferenceEquals(settled, overlayMotion.Playback), "repeated visible Show does not replay entrance");

                    HideOverlay();
                    var closing = overlayMotion.Playback!;
                    Check(!overlayRequestedVisible && !page.OverlayMotionValidationVisible && !ShellRoot.IsHitTestVisible && input?.IsActive != true,
                        "exit immediately revokes logical visibility, pointer input and controller polling");
                    Check(AppWindow.IsVisible && page.OverlayMotionValidationRetaining && !closing.IsCompleted,
                        "exit retains native presentation until compositor completion");
                    await closing.WaitAsync(TimeSpan.FromSeconds(5));
                    await Until(() => !AppWindow.IsVisible);
                    Check(!page.OverlayMotionValidationRetaining && desktopBackdrop.IsVisible == false,
                        "exit completion hides shell and scrim and releases presentation retention");

                    ShowOverlay();
                    await Until(() => overlayMotion.Playback != closing && overlayMotion.Playback is { IsCompleted: true });
                    Check(AppWindow.IsVisible && page.OverlayMotionValidationVisible && ShellRoot.IsHitTestVisible,
                        "reopen restores native shell visibility and pointer admission");
                    HideOverlay();
                    var interrupted = overlayMotion.Playback!;
                    await Task.Delay(35);
                    ShowOverlay();
                    await Until(() => overlayMotion.Playback != interrupted);
                    Check(await interrupted == WidgetMotionOutcome.Superseded,
                        "rapid reopen supersedes the closing compositor batch");
                    Check(shellVisual.Opacity > 0, "reversal does not reset the retained visual to the hidden starting pose");
                    await overlayMotion.Playback!.WaitAsync(TimeSpan.FromSeconds(5));
                    await Task.Delay(240);
                    Check(AppWindow.IsVisible && overlayRequestedVisible && page.OverlayMotionValidationVisible,
                        "obsolete exit completion cannot hide a reopened shell");
                    Check(shellVisual.Opacity == 1 && shellVisual.Scale == System.Numerics.Vector3.One &&
                        backdropVisual.Opacity == 1 && backdropVisual.Scale == System.Numerics.Vector3.One,
                        "reversal settles shell and unscaled backdrop to their visible endpoints");

                    // Reopening re-establishes the production session, which can
                    // republish persisted appearance over this in-memory fixture
                    // override. Establish the intended policy for this next case.
                    page.ApplyOverlayMotionValidationAppearance(page.Appearance with { Motion = MotionPreference.Full, WidgetAnimationSpeed = .5 });
                    await Until(() => !overlayOpenPending && overlayMotion.Playback is { IsCompleted: true });
                    HideOverlay();
                    Check(AppWindow.IsVisible, "full-motion close is initially deferred");
                    page.ApplyOverlayMotionValidationAppearance(page.Appearance with { Motion = MotionPreference.Reduced });
                    Check(!AppWindow.IsVisible && !page.OverlayMotionValidationRetaining,
                        "enabling reduced motion during exit immediately settles and hides");
                    ShowOverlay();
                    await Until(() => overlayMotion.Playback is { IsCompleted: true } && shellVisual.Opacity == 1);
                    Check(AppWindow.IsVisible && shellVisual.Scale == System.Numerics.Vector3.One,
                        "reduced-motion reopening reaches the visible endpoint without zoom");
                    HideOverlay();
                    Check(!AppWindow.IsVisible && !overlayRequestedVisible,
                        "reduced-motion close does not delay physical hiding");

                    page.ApplyOverlayMotionValidationAppearance(originalAppearance with { Motion = MotionPreference.Full });
                    ShowOverlay();
                    await Until(() => overlayMotion.Playback is { IsCompleted: true } && shellVisual.Opacity == 1);
                    HideOverlayImmediately();
                    Check(!AppWindow.IsVisible && !page.OverlayMotionValidationVisible && !page.OverlayMotionValidationRetaining,
                        "external foreground dismissal stays immediate");
                    Write(true, null);
                }
                catch (Exception error) { Write(false, error.ToString()); }
                finally { page.ApplyOverlayMotionValidationAppearance(originalAppearance); }

                async Task Until(Func<bool> condition)
                {
                    var deadline = Environment.TickCount64 + 8000;
                    while (!condition())
                    {
                        if (Environment.TickCount64 >= deadline)
                            throw new TimeoutException($"Overlay motion did not settle: requested={overlayRequestedVisible}, native={AppWindow.IsVisible}, page={page.OverlayMotionValidationVisible}");
                        await Task.Delay(15);
                    }
                }
                void Check(bool condition, string message)
                {
                    if (!condition) throw new InvalidOperationException(message);
                    checks.Add(message);
                }
                void Write(bool passed, string? error)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { passed, checks, error }));
                }
            };
        }
    }
}

namespace WidgetRail.OverlayFrontend.WinUI.Shell
{
    internal sealed partial class OverlayShellPage
    {
        internal bool OverlayMotionValidationVisible => visible;
        internal bool OverlayMotionValidationRetaining => retainingExitPresentation;
        internal void ApplyOverlayMotionValidationAppearance(AppearanceSettings appearance)
        {
            // Fixture-only in-memory policy change: no provider action or settings write.
            savedAppearance = appearance;
            ApplyEffectiveAppearance();
            MotionPolicyChanged?.Invoke();
        }
    }
}
