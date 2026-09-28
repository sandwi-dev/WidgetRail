using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private Validation.ControllerReplayScenario? replay;
    private bool validationPlatformActivation;

    partial void ConfigureValidation(IReadOnlyList<string> arguments, ref bool handled)
    {
        var validateExternalSurface = arguments.Contains("--validate-external-surface");
        var validateController = arguments.Contains("--validate-controller");
        var replayController = arguments.Contains("--replay-controller");
        var validateCollection = arguments.Contains("--validate-collection");
        var validateGridView = arguments.Contains("--validate-gridview");
        var validateControls = arguments.Contains("--validate-controls");
        var validateIndexed = arguments.Contains("--validate-indexed");
        var validateFocusPolicy = arguments.Contains("--validate-focus-policy");
        var validateGrouped = arguments.Contains("--validate-grouped");
        var validateGroupedFlat = arguments.Contains("--validate-grouped-flat");
        var validateGroupedAdapted = arguments.Contains("--validate-grouped-adapted");
        var validateSurfaces = arguments.Contains("--validate-surfaces");
        var validateSelect = arguments.Contains("--validate-select");
        var validateMotion = arguments.Contains("--validate-motion");
        var validateModals = arguments.Contains("--validate-modals");
        var validateGlyphs = arguments.Contains("--validate-glyphs");
        var validateStyles = arguments.Contains("--validate-styles");
        var validateTextEntry = arguments.Contains("--validate-text-entry");
        var validateContextMenu = arguments.Contains("--validate-context-menu");
        var validateSlider = arguments.Contains("--validate-slider");
        var validatePackageIcons = arguments.Contains("--validate-package-icons");
        var validateShellSizing = arguments.Contains("--validate-shell-sizing");
        var validateEmbeddedMedia = arguments.Contains("--validate-embedded-media");
        var validateWindowPreview = arguments.Contains("--validate-window-preview");
        var shellConfiguration = Shell.FrontendArguments.Value(arguments, "--shell-config");
        var widgetConfiguration = Shell.FrontendArguments.Value(arguments, "--widget-config");
        var indexedValidationPipe = Shell.FrontendArguments.Value(arguments, "--indexed-validation-pipe");
        if (validateEmbeddedMedia)
        {
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
            AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(880 * scale), (int)Math.Ceiling(680 * scale)));
        }
        if (validateController || replayController)
        {
            var page = new Validation.ControllerValidationPage();
            RootFrame.Content = page;
            try
            {
                var backend = replayController ? new Validation.ReplayNativePlatform() : null;
                input = new(DispatcherQueue, WinRT.Interop.WindowNative.GetWindowHandle(this), backend);
                if (backend is not null) replay = new(this, page, backend);
                input.FrameReceived += page.Receive;
                input.Failed += page.ReportFailure;
                input.ToggleRequested += () =>
                {
                    if (AppWindow.IsVisible && input.IsForeground) { page.ResetInputPresentation(); input.SetVisible(false); AppWindow.Hide(); }
                    else
                    {
                        input.PrepareShow();
                        AppWindow.Show();
                        Activate();
                        input.AcquireForeground();
                        StartInput();
                    }
                };
                input.PrepareShow();
            }
            catch (Exception error) { input?.Dispose(); page.ReportFailure(error); }
        }
        else if (validateWindowPreview) RootFrame.Content = new Validation.WindowPreviewValidationPage();
        else if (arguments.Contains("--validate-shell-status")) RootFrame.Content = new Validation.ShellStatusValidationPage();
        else if (arguments.Contains("--validate-production-shell"))
        { AppWindow.Resize(new(1600, 1100)); RootFrame.Content = new Validation.ProductionShellValidationPage(); }
        else if (arguments.Contains("--validate-gamepad-boundary")) RootFrame.Content = new Validation.GamepadKeyBoundaryValidationPage();
        else if (validateEmbeddedMedia) RootFrame.Content = new Validation.EmbeddedMediaValidationPage();
        else if (validatePackageIcons) RootFrame.Content = new Validation.PackageIconValidationPage();
        else if (validateShellSizing) RootFrame.Content = new Validation.ShellSizingValidationPage();
        else if (arguments.Contains("--validate-shell-appearance")) RootFrame.Content = new Validation.ShellAppearanceValidationPage(this);
        else if (arguments.Contains("--validate-shell-chrome"))
        { AppWindow.Resize(new(1100, 1000)); RootFrame.Content = new Validation.ShellChromeValidationPage(); }
        else if (arguments.Contains("--validate-pinned-window")) RootFrame.Content = new Validation.PinnedWindowValidationPage(this);
        else if (validateSlider) RootFrame.Content = new Validation.SliderControlValidationPage();
        else if (validateContextMenu) RootFrame.Content = new Validation.ContextMenuControlValidationPage();
        else if (shellConfiguration is not null) return;
        else if (validateTextEntry) RootFrame.Content = new Validation.TextEntryValidationPage();
        else if (validateStyles) RootFrame.Content = new Validation.WidgetStylesValidationPage();
        else if (validateGlyphs) RootFrame.Content = new Validation.GlyphValidationPage();
        else if (validateModals) RootFrame.Content = new Validation.ModalValidationPage();
        else if (validateMotion) RootFrame.Content = new Validation.WidgetMotionValidationPage();
        else if (validateSelect) RootFrame.Content = new Validation.SelectControlValidationPage();
        else if (validateSurfaces) RootFrame.Content = new Validation.PresentationSurfaceValidationPage();
        else if (validateGrouped || validateGroupedFlat || validateGroupedAdapted)
            RootFrame.Content = new Validation.GroupedCollectionValidationPage(flatBaseline: validateGroupedFlat, useRangeAdapter: validateGroupedAdapted);
        else if (indexedValidationPipe is not null)
        {
            if (indexedValidationPipe.StartsWith("pinned-validation-", StringComparison.Ordinal))
                RootFrame.Content = new Validation.PinnedWidgetValidationPage(indexedValidationPipe);
            else if (indexedValidationPipe.StartsWith("discovered-validation-", StringComparison.Ordinal))
            {
                var discovery = new Validation.DiscoveredCollectionValidationPage(indexedValidationPipe);
                discovery.Initialize(); RootFrame.Content = discovery;
            }
            else RootFrame.Content = new Validation.IndexedWidgetValidationPage(indexedValidationPipe);
        }
        else if (widgetConfiguration is not null)
            RootFrame.Content = new Validation.BridgeWidgetValidationPage(widgetConfiguration);
        else if (validateControls)
            RootFrame.Content = new Validation.WidgetControlsValidationPage();
        else if (validateFocusPolicy)
            RootFrame.Content = new Validation.FocusPolicyValidationPage();
        else if (validateIndexed)
            RootFrame.Content = new Validation.IndexedCollectionValidationPage();
        else if (validateExternalSurface || validateCollection || validateGridView || arguments.Contains("--gallery"))
            RootFrame.Navigate(validateExternalSurface ? typeof(Validation.ExternalSurfacePage) :
            validateCollection ? typeof(Validation.CollectionValidationPage) :
            validateGridView ? typeof(Validation.GridViewValidationPage) : typeof(MainPage));
        else return; // Debug and Release use the same production launch by default.
        if (input is null && arguments.Contains("--validation-platform-activation"))
        {
            // Pixel checks need the same confirmed foreground path as production.
            // This opt-in reuses that adapter; it forwards no input to the fixture.
            input = new(DispatcherQueue, WinRT.Interop.WindowNative.GetWindowHandle(this));
            input.PrepareShow();
            validationPlatformActivation = true;
        }
        handled = true;
    }

    partial void ConfigureProductionValidation(Shell.OverlayShellPage page, IReadOnlyList<string> arguments)
    {
        validationPlatformActivation = arguments.Contains("--validation-platform-activation");
        if (arguments.Contains("--shell-no-controller") && Shell.FrontendArguments.Value(arguments, "--validate-widget-switches") is { } switchResult)
            page.EnableSwitchValidation(switchResult);
        if (arguments.Contains("--shell-no-controller") && arguments.Contains("--replay-shell-input"))
            page.EnableValidationInputReplay();
    }
    partial void QueueValidationEntryFocus() => (RootFrame.Content as Validation.ControllerValidationPage)?.QueueEntryFocus();
    partial void ResetValidationInput() => (RootFrame.Content as Validation.ControllerValidationPage)?.ResetInputPresentation();
    partial void StartValidationReplay()
    {
        if (validationPlatformActivation) input?.AcquireForeground();
        replay?.Start();
    }
    partial void RetireValidation()
    {
        replay?.Dispose();
        (RootFrame.Content as Validation.ExternalSurfacePage)?.Retire();
        (RootFrame.Content as Validation.GamepadKeyBoundaryValidationPage)?.Dispose();
    }
}
