using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Actual Settings controls and broker; only an isolated WidgetRail profile is
    // writable. No power, network, driver, startup or Windows-display actions.
    internal void EnableAppearanceSettingsValidation(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var profile = Path.GetFullPath(options.SettingsRoot);
        if (!profile.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            profile.Equals(PlatformSettingsPaths.CreateDefault().RootDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Appearance validation requires an isolated SettingsRoot below its result directory.");
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return; started = true;
            var checks = new List<string>();
            var store = new PlatformSettingsStore(new(profile));
            PlatformSettingsDocument? original = null;
            string? failure = null;
            var phase = "startup";
            try
            {
                original = await store.LoadAsync();
                if (startup is not null) await startup;
                await SelectAsync("settings", true);
                await Invoke("category.overlay");
                await Wait(() => Ready("interface.stepper.increment") && activeDisplayId.Length > 0);
                var scale = Appearance.InterfaceScale;
                var textScale = Appearance.TextScale;
                var document = await store.LoadAsync();
                await Invoke("interface.stepper.increment");
                await Settled(value => DisplayScalePolicy.Resolve(value, activeDisplayId).InterfaceScale > scale,
                    () => Appearance.InterfaceScale > scale && SizingDiagnostics?["scale"]?.GetValue<double>() == Appearance.InterfaceScale);
                Check(Appearance.TextScale == textScale && (await store.LoadAsync()).Appearance.InterfaceScale == document.Appearance.InterfaceScale,
                    "native interface stepper changes this display's overlay scale while preserving text and global fallback");
                await Invoke("interface.stepper.decrement");
                await Settled(value => DisplayScalePolicy.Resolve(value, activeDisplayId).InterfaceScale == scale,
                    () => Appearance.InterfaceScale == scale);
                Check(true, "native interface decrement returns the live shell to its original scale");

                var darkness = Appearance.BackdropOpacity;
                await Change("opacity.stepper.decrement", value => value.BackdropOpacity < darkness, () => Appearance.BackdropOpacity < darkness,
                    "backdrop darkness reaches persisted preferences and live shell appearance");
                await Change("opacity.stepper.increment", value => value.BackdropOpacity == darkness, () => Appearance.BackdropOpacity == darkness,
                    "backdrop increment restores the previous value");
                var switcher = Appearance.WidgetSwitcher;
                await Change("overlay.widget-switcher", value => value.WidgetSwitcher != switcher, () => Appearance.WidgetSwitcher != switcher,
                    "switcher control changes the live rail/radial policy");
                await Change("overlay.widget-switcher", value => value.WidgetSwitcher == switcher, () => Appearance.WidgetSwitcher == switcher,
                    "switcher control round-trips through Settings");
                var position = Appearance.OverlayPosition;
                for (var step = 0; step < 3; ++step)
                {
                    var before = Appearance.OverlayPosition;
                    await Change("overlay.position", value => value.OverlayPosition != before, () => Appearance.OverlayPosition != before,
                        "position control advances the live overlay anchor");
                    await Wait(() => WidgetSurface.HorizontalAlignment == (Appearance.OverlayPosition switch
                    { OverlayPosition.BottomLeft => HorizontalAlignment.Left, OverlayPosition.BottomRight => HorizontalAlignment.Right, _ => HorizontalAlignment.Center }));
                }
                Check(Appearance.OverlayPosition == position, "position control cycles through all three anchors");

                foreach (var option in new[] { ("settle", WidgetFocusAnimation.Settle), ("none", WidgetFocusAnimation.None), ("fade", WidgetFocusAnimation.Fade) })
                    await Choose("overlay.focus-animation", option.Item1, value => value.FocusAnimation == option.Item2,
                        () => Appearance.FocusAnimation == option.Item2);
                foreach (var option in new[] { ("paging", WidgetSectionAnimation.Paging),
                    ("verticalslide", WidgetSectionAnimation.VerticalSlide), ("reveal", WidgetSectionAnimation.Reveal),
                    ("coverslide", WidgetSectionAnimation.CoverSlide), ("none", WidgetSectionAnimation.None), ("slide", WidgetSectionAnimation.Slide) })
                    await Choose("overlay.section-animation", option.Item1, value => value.SectionAnimation == option.Item2,
                        () => Appearance.SectionAnimation == option.Item2);
                foreach (var option in new[] { ("lift", WidgetModalAnimation.Lift), ("zoom", WidgetModalAnimation.Zoom) })
                    await Choose("overlay.modal-animation", option.Item1, value => value.ModalAnimation == option.Item2,
                        () => Appearance.ModalAnimation == option.Item2);
                var dialogs = Appearance.AnimateWidgetModals;
                await Change("overlay.animate-dialogs", value => value.AnimateWidgetModals != dialogs, () => Appearance.AnimateWidgetModals != dialogs,
                    "dialog animation toggle reaches the live motion policy");
                var speed = Appearance.WidgetAnimationSpeed;
                await Change("overlay.animation-speed.increment", value => value.WidgetAnimationSpeed > speed, () => Appearance.WidgetAnimationSpeed > speed,
                    "animation speed stepper reaches the live motion policy");
                Check(Find("overlay.startup") is null, "development Settings omit unavailable startup registration");

                await Back("category.accessibility");
                await Invoke("category.accessibility");
                await Wait(() => Ready("text.stepper.increment"));
                var text = Appearance.TextScale;
                var heading = Find("accessibility.heading") as TextBlock ?? throw new InvalidOperationException("Missing Settings heading");
                var fontSize = heading.FontSize;
                await Change("text.stepper.increment", value => DisplayScalePolicy.Resolve(value, activeDisplayId).TextScale > text,
                    () => Appearance.TextScale > text, "text size stepper updates this display's text scale");
                await Wait(() => (Find("accessibility.heading") as TextBlock)?.FontSize > fontSize);
                Check(true, "text-size update changes actual native heading typography");
                await Invoke("accessibility.visual");
                await Wait(() => Ready("bold-text.toggle"));
                var bold = Appearance.BoldText;
                await Change("bold-text.toggle", value => value.BoldText != bold, () => Appearance.BoldText != bold,
                    "bold-text control persists and updates the live accessibility policy");
                await Change("bold-text.toggle", value => value.BoldText == bold, () => Appearance.BoldText == bold,
                    "bold-text control restores its original value");
                var transparency = Appearance.Transparency;
                await Change("transparency.reduced", value => value.Transparency != transparency, () => Appearance.Transparency != transparency,
                    "reduced transparency reaches the live surface policy");
                await Change("contrast.high", value => value.Contrast == ContrastPreference.High, () => Appearance.Contrast == ContrastPreference.High,
                    "high contrast reaches the live shell and widget policy");
                await Back("motion.reduced");
                await Change("motion.reduced", value => value.Motion == MotionPreference.Reduced, () => Appearance.Motion == MotionPreference.Reduced,
                    "reduced motion reaches the shared motion policy");
                await Back("category.appearance");
                await Invoke("category.appearance");
                var switching = Appearance.AnimateWidgetSwitching;
                await Change("appearance.animate-widget-switching", value => value.AnimateWidgetSwitching != switching,
                    () => Appearance.AnimateWidgetSwitching != switching, "widget-switch animation toggle updates the live shell");
                var saved = (await store.LoadAsync()).Appearance;
                await SelectAsync("settings", false);
                SetVisible(false); await Task.Delay(100); SetVisible(true);
                await Wait(() => visible && !this.switching);
                Check(System.Text.Json.Nodes.JsonNode.DeepEquals(JsonSerializer.SerializeToNode((await new PlatformSettingsStore(new(profile)).LoadAsync()).Appearance),
                    JsonSerializer.SerializeToNode(saved)),
                    "fresh preference reader retains changes after shell hide/reopen");
                Check((await store.LoadAsync()).Controllers == original.Controllers,
                    "appearance actions leave controller and exclusive-control settings unchanged");
            }
            catch (Exception error) { failure = error.ToString(); }
            finally
            {
                if (original is not null)
                    try { await store.UpdateAsync(_ => original); }
                    catch (Exception error) { failure ??= "Isolated profile restoration failed: " + error; }
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure,
                    profile, physicalInput = false, systemSettingsChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
            }

            void Check(bool condition, string message)
            { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
            async Task Change(string id, Func<AppearanceSettings, bool> saved, Func<bool> applied, string message)
            { await Invoke(id); await Settled(saved, applied); Check(true, message); }
            async Task Choose(string id, string option, Func<AppearanceSettings, bool> saved, Func<bool> applied)
            {
                await Invoke(id);
                var optionId = "Widget." + id + ".Option." + option;
                phase = "open " + optionId;
                await Wait(() => Popup(optionId) is { IsLoaded: true, IsEnabled: true } &&
                    FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused &&
                    AutomationProperties.GetAutomationId(focused).StartsWith("Widget." + id + ".Option.", StringComparison.Ordinal));
                phase = "apply " + optionId;
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(Popup(optionId)!);
                ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
                await Settled(saved, applied);
                await Wait(() => Node(surface?.CurrentBinding?.View.Root, id) is { IsBusy: not true } node &&
                    node.SelectOptions.Any(value => value.Id == option && value.IsSelected));
                phase = "close " + optionId;
                await Wait(() => !surface!.HasTransientControl && Popup(optionId) is null);
                Check(true, id + " option " + option + " reaches storage and live policy");
            }
            async Task Back(string next)
            {
                phase = "back to " + next;
                if (surface is null || !await surface.HandleControllerButtonAsync(ControllerButton.B,
                    origin: WidgetRail.WidgetSdk.ControllerInputOrigin.AccessibilityAutomation))
                    throw new InvalidOperationException("Settings did not handle Back");
                await Wait(() => Ready(next));
            }
            async Task Invoke(string id)
            {
                phase = "invoke " + id;
                await Wait(() => Ready(id));
                var control = (Control)Find(id)!;
                if (!control.Focus(FocusState.Keyboard)) throw new InvalidOperationException("Cannot focus Settings control " + id);
                control.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                await Wait(() =>
                {
                    var bounds = control.TransformToVisual(surface).TransformBounds(new(0, 0, control.ActualWidth, control.ActualHeight));
                    return bounds.Top >= -1 && bounds.Bottom <= surface!.ActualHeight + 1;
                });
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(control);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            }
            async Task Settled(Func<AppearanceSettings, bool> saved, Func<bool> applied) =>
                await WaitAsync(async () => saved((await store.LoadAsync()).Appearance) && applied());
            bool Ready(string id) => MainFocusEnabled && Find(id) is Control { IsLoaded: true, IsEnabled: true, IsHitTestVisible: true } &&
                Node(surface?.CurrentBinding?.View.Root, id) is { IsBusy: not true, IsDisabled: not true };
            FrameworkElement? Find(string id) => surface is null ? null : Walk(surface).OfType<FrameworkElement>()
                .FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == "Widget." + id);
            Control? Popup(string id) => VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).SelectMany(popup => Walk(popup.Child))
                .OfType<Control>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
            Task Wait(Func<bool> predicate) => WaitAsync(() => Task.FromResult(predicate()));
            async Task WaitAsync(Func<Task<bool>> predicate)
            {
                var deadline = Environment.TickCount64 + 15000;
                while (!await predicate())
                {
                    if (RecoveryVisible || validationFailure is not null) throw new InvalidOperationException("Settings entered recovery", validationFailure);
                    if (Environment.TickCount64 > deadline) throw new TimeoutException("Appearance setting did not settle: " + phase + "; scope=" + surface?.CurrentBinding?.View.ActiveInputScopeId);
                    await Task.Delay(25);
                }
            }
        };
        static ViewNode? Node(ViewNode? node, string id) => node is null || node.Id == id ? node : node.Children.Select(child => Node(child, id)).FirstOrDefault(value => value is not null);
        static IEnumerable<DependencyObject> Walk(DependencyObject root)
        {
            yield return root;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
                foreach (var child in Walk(VisualTreeHelper.GetChild(root, index))) yield return child;
        }
    }
}
