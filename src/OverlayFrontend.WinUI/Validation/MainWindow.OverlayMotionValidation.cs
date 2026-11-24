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
                    if (Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--validate-foreground-background") is { } expected)
                    {
                        Check(backgroundProcess is not null && ForegroundBackgroundAllowed && !BeforeFirstBackgroundFrame,
                            "The host captured an external window and rendered the opening frame with transparency allowed");
                        if (Environment.GetCommandLineArgs().Contains("--validate-background-reduced"))
                            page.ApplyOverlayMotionValidationAppearance(originalAppearance with { Motion = MotionPreference.Reduced });
                        await page.ValidateForegroundBackgroundAsync(expected == "match", Until, Check);
                        if (Environment.GetCommandLineArgs().Contains("--validate-discord-background-switch"))
                            await page.ValidateDiscordBackgroundSwitchAsync(expected == "match", Check);
                        if (Environment.GetCommandLineArgs().Contains("--validate-background-return"))
                            await page.ValidateBackgroundReturnAsync(expected == "match", HideOverlay, ShowOverlay, foregroundReturnTarget!,
                                () => AppWindow.IsVisible, path, Until, Check);
                        Write(true, null);
                        return;
                    }
                    if (Environment.GetCommandLineArgs().Contains("--validate-idle-worker-reopen"))
                    {
                        await page.ValidateIdleWorkerReopenAsync(HideOverlay, ShowOverlay, () => AppWindow.IsVisible, Until, Check);
                        Write(true, null);
                        return;
                    }
                    if (Environment.GetCommandLineArgs().Contains("--validate-guide-reopen"))
                    {
                        await page.ValidateGuideReopenAsync(HideOverlay, ShowOverlay, () => AppWindow.IsVisible,
                            () => !overlayOpenPending && overlayMotion?.Playback is { IsCompleted: true }, Until, Check);
                        Write(true, null);
                        return;
                    }
                    if (Environment.GetCommandLineArgs().Contains("--validate-radial-reopen"))
                    {
                        await page.ValidateRadialReopenAsync(HideOverlay, HideOverlayImmediately, ShowOverlay,
                            () => AppWindow.IsVisible, () => !overlayOpenPending && overlayMotion?.Playback is { IsCompleted: true }, Until, Check);
                        Write(true, null);
                        return;
                    }
                    Check(overlayMotionRecipe == WidgetMotionPolicy.OverlayVisibility(originalAppearance, page.SystemAnimationsEnabled, opening: true),
                        "cold entrance honors the saved visibility preference");
                    page.ApplyOverlayMotionValidationAppearance(originalAppearance with { Motion = MotionPreference.Full, AnimateOverlayVisibility = false });
                    HideOverlay();
                    Check(!AppWindow.IsVisible && overlayMotion?.Playback is { IsCompleted: true } && !page.OverlayMotionValidationRetaining,
                        "disabling overlay animation makes close immediate even with full motion");
                    ShowOverlay();
                    await Until(() => !overlayOpenPending && overlayMotion?.Playback is { IsCompleted: true });
                    page.ApplyOverlayMotionValidationAppearance(originalAppearance with
                    {
                        Motion = MotionPreference.Full, WidgetAnimationSpeed = .5,
                        AnimateOverlayVisibility = true, AnimateWidgetSwitching = false, SectionAnimation = WidgetSectionAnimation.None,
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
                    Check(shellVisual.Scale == System.Numerics.Vector3.One && backdropVisual.Scale == System.Numerics.Vector3.One,
                        "both layers retain their original size during the fade");
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
                    page.ApplyOverlayMotionValidationAppearance(page.Appearance with { AnimateOverlayVisibility = true, Motion = MotionPreference.Full, WidgetAnimationSpeed = .5 });
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
        internal async Task ValidateForegroundBackgroundAsync(bool expectMatch, Func<Func<bool>, Task> until, Action<bool, string> check)
        {
            const string backgroundId = "playnite-library.cinematic";
            await until(() => MainFocusEnabled && surface?.CurrentBinding?.View.Root is { } root &&
                Descendants(surface).OfType<Presentation.WidgetPresentationSurface>().Any() && HasProcessKey(root));
            var background = Descendants(surface!).OfType<Presentation.WidgetPresentationSurface>().First();
            if (expectMatch)
            {
                await until(() => background.IsForegroundRevealed);
                check(true, "The dimmed foreground game is revealed on the first cold opening");
                string? cardId = null;
                await until(() => Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is Microsoft.UI.Xaml.DependencyObject focused &&
                    (cardId = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(focused)).StartsWith("Widget.playnite.playing.", StringComparison.Ordinal));
                Presentation.WidgetIndexedCollectionView? posters = null;
                await until(() => (posters = Descendants(surface!).OfType<Presentation.WidgetIndexedCollectionView>()
                    .FirstOrDefault(list => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(list) == "Widget.playnite-library.library.grid")) is
                    { IsLoaded: true, NativeView.Items.Count: > 0 });
                check(posters!.Enter(allowFallback: true), "The poster rail accepts native focus");
                await until(() => !background.IsForegroundRevealed && background.ArtworkSource is not null);
                check(true, "Focusing the poster rail brings back decoded artwork");
                check(surface!.FocusHostTarget(cardId!["Widget.".Length..]), "The original game card accepts native focus");
                await until(() => background.IsForegroundRevealed);
                check(true, "Returning focus to Currently Playing reveals the game again");
            }
            else
            {
                await Task.Delay(400, lifetime.Token);
                check(!background.IsForegroundRevealed, "An unrelated foreground window keeps the widget background painted");
            }

            static bool HasProcessKey(WidgetRail.WidgetProtocol.ViewNode node) =>
                node.Id == backgroundId && node.ForegroundBackgroundProcessKey is not null || node.Children.Any(HasProcessKey);
        }
        internal async Task ValidateDiscordBackgroundSwitchAsync(bool match, Action<bool, string> check)
        {
            const string playnite = "widgetrail.samples.playnite-library";
            const string discord = "widgetrail.samples.discord-controls";
            foreach (bool enter in new[] { true, false, true })
            {
                await SelectAsync(discord, enter);
                check(activeWidget == discord && surface?.CurrentBinding is not null,
                    "Switched to the installed Discord widget");
                await Task.Delay(350, lifetime.Token);
                int frames = 0, wrongPaint = 0, fading = 0;
                void Observe(object? sender, object args)
                {
                    if (activeWidget != playnite || surface is null) return;
                    var paint = Descendants(surface).OfType<Presentation.WidgetPresentationSurface>()
                        .FirstOrDefault(s => s.HasForegroundPaint);
                    if (paint is null || paint.IsForegroundRevealed != match) wrongPaint++;
                    if (paint?.IsForegroundRevealed == true && paint.ForegroundTransitionDuration > TimeSpan.Zero) fading++;
                    frames++;
                }
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += Observe;
                try
                {
                    await SelectAsync(playnite, enter);
                    await Task.Delay(550, lifetime.Token);
                }
                finally { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= Observe; }
                check(frames > 0 && wrongPaint == 0 && fading == 0,
                    $"Discord to Playnite (enter={enter}): {frames} frames, {wrongPaint} wrong paint, {fading} animated reveals");
            }
        }

        internal async Task ValidateBackgroundReturnAsync(bool match, Action hide, Action show,
            WidgetRail.WindowsWindowActivation.TaskWindowTarget foregroundTarget,
            Func<bool> windowVisible, string resultPath, Func<Func<bool>, Task> until, Action<bool, string> check)
        {
            const string playnite = "widgetrail.samples.playnite-library";
            await until(() => surface?.CurrentBinding is not null && MainFocusEnabled);
            string FocusId() => Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is Microsoft.UI.Xaml.DependencyObject el
                ? Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(el) : "";
            var card = FocusId();
            var phase = "settled";
            var frames = new List<(string Phase, bool Revealed, bool Art, string Focus, long Ticks)>();
            void Sample(object? sender, object args)
            {
                if (!windowVisible() || activeWidget != playnite || surface is null) return;
                var bg = Descendants(surface).OfType<Presentation.WidgetPresentationSurface>().FirstOrDefault();
                frames.Add((phase, bg?.IsForegroundRevealed == true, bg?.ArtworkSource != null, FocusId(), Environment.TickCount64));
            }
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += Sample;
            try
            {
                await Task.Delay(200);
                await SelectAsync("settings", true);
                phase = "warm";
                await SelectAsync(playnite, true);
                await Task.Delay(850);
                var originalRun = surface!.CurrentBinding!.Frame.Authority.WorkerRun;
                phase = "closing";
                hide();
                await until(() => !windowVisible());
                await Task.Delay(8500);
                var windows = new WidgetRail.WindowsWindowActivation.WindowsTaskWindowActivation();
                if (windows.CaptureForegroundTarget() != foregroundTarget && windows.IsCurrent(foregroundTarget))
                    WinUIEx.HwndExtensions.SetForegroundWindow((nint)(nuint)foregroundTarget.Handle);
                await until(() => windows.CaptureForegroundTarget() == foregroundTarget);
                check(windows.CaptureForegroundTarget() == foregroundTarget,
                    "The requested external window is in front immediately before the cold reopen");
                phase = "cold";
                show();
                await until(() => visible && !switching && surface?.CurrentBinding?.Frame.Authority.WorkerRun is { } next && next != originalRun);
                await Task.Delay(900);
            }
            finally
            {
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= Sample;
                File.WriteAllLines(resultPath + ".frames", frames.Select(f => $"{f.Phase},{f.Revealed},{f.Art},{f.Focus},{f.Ticks}"));
            }
            foreach (var boundary in new[] { "warm", "cold" })
            {
                var samples = frames.Where(f => f.Phase == boundary).ToArray();
                check(samples.Length > 0 && samples.All(f => match ? f.Revealed : !f.Revealed && f.Art),
                    $"{boundary} retained the requested background throughout the visible handoff");
                check(samples[^1].Focus == card, $"{boundary} restored the settled shelf card");
            }
        }
        // Run with Playnite's production widget and a fixture-only five-second idle policy.
        internal async Task ValidateIdleWorkerReopenAsync(Action hide, Action show, Func<bool> nativeVisible,
            Func<Func<bool>, Task> until, Action<bool, string> check)
        {
            await until(() => MainFocusEnabled && !switching && surface?.CurrentBinding?.Frame.Authority.WorkerRun is not null);
            var run = surface!.CurrentBinding!.Frame.Authority.WorkerRun;
            FocusDirectionalTray();
            await until(() => !interactive && FocusedTrayWidget()?.Id == activeWidget);
            hide();
            await until(() => !nativeVisible());
            show();
            await until(() => visible && foreground && !switching && !interactive && FocusedTrayWidget()?.Id == activeWidget);
            check(surface.CurrentBinding?.Frame.Authority.WorkerRun == run, "Warm reopen preserves rail ownership on the same worker");
            hide();
            await until(() => !nativeVisible());
            await Task.Delay(TimeSpan.FromSeconds(8), lifetime.Token);
            show();
            await until(() => !switching && surface.CurrentBinding?.Frame.Authority.WorkerRun is { } next && next != run);
            check(MainFocusEnabled && DesiredLifecycle == WidgetRail.WidgetSdk.WidgetLifecycleState.Interactive,
                "Cold reopen enters the restarted worker without another input");
            var rail = "Widget.playnite-library.library.grid";
            bool ArtLoaded() => Descendants(surface).OfType<Presentation.WidgetIndexedCollectionView>()
                .Where(list => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(list) == rail)
                .Any(list => Descendants(list).OfType<Presentation.WidgetArtworkView>().Any(image => image.IsLoaded && image.ActualWidth > 0 &&
                    image.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapSource { PixelWidth: > 0 }));
            await until(() => Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is Microsoft.UI.Xaml.DependencyObject focused &&
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(focused).StartsWith("Widget.playnite", StringComparison.Ordinal) &&
                ArtLoaded() && Descendants(surface).OfType<Presentation.WidgetPresentationSurface>()
                .Any(item => item.ArtworkSource is not null));
            check(true, "Native widget focus, decoded poster art and cinematic artwork return after idle unload");
        }
        internal async Task ValidateGuideReopenAsync(Action hide, Action show, Func<bool> nativeVisible,
            Func<bool> motionSettled, Func<Func<bool>, Task> until, Action<bool, string> check)
        {
            await until(() => MainFocusEnabled && !switching && !trayGuide.HasPendingHints && trayGuide.DisplayedHints.Count > 2);
            Presentation.WidgetIndexedCollectionView? rows = null;
            await until(() => (rows = Descendants(surface!).OfType<Presentation.WidgetIndexedCollectionView>()
                .FirstOrDefault(list => list.IsLoaded && list.ActualHeight > 0)) is not null);
            check(rows!.Enter(allowFallback: true), "Enter a native music list row");
            await until(() => rows.CaptureContextRow() is not null && !trayGuide.HasPendingHints &&
                trayGuide.PublishedHintsForValidation.Any(hint => hint.Label == "Options"));
            var original = Appearance;
            foreach (var mode in new[] { "animated", "reversed", "disabled" })
            {
                ApplyOverlayMotionValidationAppearance(original with { Motion = MotionPreference.Full, AnimateOverlayVisibility = mode != "disabled" });
                await until(() => motionSettled() && !trayGuide.HasPendingHints &&
                    trayGuide.DisplayedHints.Any(hint => hint.Label == "Options") &&
                    trayGuide.DisplayedHints.Any(hint => hint.Label == "Close"));
                var expected = trayGuide.DisplayedHints.ToArray();
                int frames = 0;
                string? mismatch = null;
                void Observe(object? sender, object args)
                {
                    frames++;
                    if (trayGuide.DisplayedHints.SequenceEqual(expected) || !nativeVisible()) return;
                    mismatch ??= $"[{string.Join(", ", trayGuide.DisplayedHints.Select(h => h.Label))}] visible={visible} foreground={foreground} switching={switching} interactive={interactive} ready={trayGuide.ContextualReady}";
                }
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += Observe;
                try
                {
                    hide();
                    if (mode == "reversed") await Task.Delay(25);
                    else await until(() => !nativeVisible());
                    show();
                    try { await until(() => frames > 2 && MainFocusEnabled && !switching && motionSettled() && !trayGuide.HasPendingHints); }
                    catch (TimeoutException error)
                    {
                        throw new InvalidOperationException($"{mode}: {mismatch}; guide={trayGuide.HelpText}, ready={trayGuide.ContextualReady}, " +
                            $"main={MainFocusEnabled}, actualReady={surface?.IsGuidePresentationReady(true)}, row={rows.CaptureFocusedItem()}, " +
                            $"contextRow={rows.CaptureContextRow()?.Row.Item.Key}, rowState={rows.GuideRowStateForValidation()}",error);
                    }
                    Observe(null, EventArgs.Empty);
                    check(mismatch is null, $"{mode}: retained [{string.Join(", ", expected.Select(h => h.Label))}] across {frames} samples; first mismatch={mismatch ?? "none"}");
                }
                finally { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= Observe; }
                await RouteButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.Menu, WidgetRail.WidgetProtocol.ControllerEventPhase.Pressed);
                await RouteButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.Menu, WidgetRail.WidgetProtocol.ControllerEventPhase.Released);
                await until(() => surface!.HasTransientControl);
                await RouteButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.B, WidgetRail.WidgetProtocol.ControllerEventPhase.Pressed);
                await RouteButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.B, WidgetRail.WidgetProtocol.ControllerEventPhase.Released);
                await until(() => !surface!.HasTransientControl && rows.CaptureContextRow() is not null && !trayGuide.HasPendingHints);
                check(trayGuide.PublishedHintsForValidation.Any(hint => hint.Label == "Options"), mode + " reopened and dismissed the focused row's Options menu");
            }
        }
        internal async Task ValidateRadialReopenAsync(Action hide, Action immediateHide, Action show, Func<bool> nativeVisible,
            Func<bool> motionSettled, Func<Func<bool>, Task> until, Action<bool, string> check)
        {
            var original = Appearance;
            var widget = activeWidget!;
            await SelectAsync(widget, true);
            var retained = surface!;
            foreach (var mode in new[] { "animated", "reversed", "immediate", "disabled" })
            {
                ApplyOverlayMotionValidationAppearance(original with { AnimateOverlayVisibility = mode != "disabled", Motion = MotionPreference.Full });
                await until(() => MainFocusEnabled && !switching && motionSettled());
                await RouteButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.B, WidgetRail.WidgetProtocol.ControllerEventPhase.Pressed);
                await RouteButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.B, WidgetRail.WidgetProtocol.ControllerEventPhase.Released);
                await until(() => RadialOpen && radialView?.Visibility == Microsoft.UI.Xaml.Visibility.Visible);
                if (mode == "immediate") immediateHide(); else hide();
                if (mode == "reversed") check(retainingExitPresentation && nativeVisible(), "Reversed before the closing fade hid the HWND");
                else await until(() => !nativeVisible());

                int frames = 0;
                bool sawWheel = false;
                void SampleFrame(object? sender, object args)
                {
                    frames++;
                    sawWheel |= radialRequested || radialShowing || radialView?.Visibility == Microsoft.UI.Xaml.Visibility.Visible;
                }
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += SampleFrame;
                try
                {
                    show();
                    check(!radialRequested && !radialShowing && radialView?.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed,
                        mode + " clears radial chrome synchronously on entrance");
                    await until(() => frames > 2 && !switching && MainFocusEnabled && motionSettled());
                    check(!sawWheel && ReferenceEquals(retained, surface) && activeWidget == widget,
                        $"{mode} kept the same widget presenter with no radial in {frames} rendering callbacks");
                }
                finally { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= SampleFrame; }
            }
        }
        internal void ApplyOverlayMotionValidationAppearance(AppearanceSettings appearance)
        {
            // Fixture-only in-memory policy change: no provider action or settings write.
            savedAppearance = appearance;
            ApplyEffectiveAppearance();
            MotionPolicyChanged?.Invoke();
        }
    }
}
