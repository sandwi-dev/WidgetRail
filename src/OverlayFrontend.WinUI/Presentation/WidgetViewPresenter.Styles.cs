using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.PlatformSettings;
using Windows.UI;
using Windows.UI.Text;
using IconElement = Microsoft.UI.Xaml.Controls.IconElement;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private readonly Dictionary<Binding, NativeComputedStyleAdapter> nativeStyles = [];
    private NativeComputedStyleAdapter? controllerPressedStyle;
    private bool indexedRootStyleOnContainer;
    private string? fragmentRootId;
    private (bool Focused, bool Pressed)? indexedRootInteraction;

    internal static void SetHighContrastStyleOverride(bool? value) => NativeComputedStyleAdapter.SetHighContrastOverride(value);
    internal static void SetSystemHighContrast(bool value)
    {
        NativeComputedStyleAdapter.SetSystemHighContrast(value);
        WidgetPresentationSurface.SetSystemHighContrast(value);
    }
    internal void UseIndexedContainerStyles() => indexedRootStyleOnContainer = true;
    internal void SetIndexedRootInteraction(bool focused, bool pressed)
    {
        indexedRootInteraction = (focused, pressed);
        if (fragmentRootId is { } root && bindings.TryGetValue(root, out var binding) && declarations.TryGetValue(root, out var declaration))
            ApplyComputedStyles(binding, declaration.Node);
    }
    internal void ResetPressedStyles()
    {
        ResetSliderValues();
        SetSliderAdjustment(null);
        sliderBackPending = false;
        controllerPressedStyle = null;
        foreach (var adapter in nativeStyles.Values) adapter.SetControllerPressed(false);
        foreach (var binding in bindings.Values)
            if (binding.Element is WidgetIndexedCollectionView collection)
            { collection.CancelPendingActivation(); collection.SetControllerPressedStyle(false); }
    }

    private void ApplyComputedStyles(Binding binding, ViewNode node)
    {
        FrameworkElement target = binding.Element switch
        {
            WidgetRichTextView rich => rich.Document,
            WidgetPresentationSurface surface => surface.StylePanel,
            WidgetIndexedCollectionView collection => collection.NativeView,
            WidgetModalLayer layer => (FrameworkElement)layer.Chrome,
            _ => binding.Element,
        };
        if (nativeStyles.TryGetValue(binding, out var previous) && !ReferenceEquals(previous.Target, target))
        { previous.Dispose(); nativeStyles.Remove(binding); }
        if (!nativeStyles.TryGetValue(binding, out var adapter))
            nativeStyles.Add(binding, adapter = new(target, node.IsFocusable));
        adapter.ContextHintButton = WidgetContextIndicator.Resolve(node);
        adapter.ContextHintAdmitted = () => !disposed && presentationActive && presentationInputEnabled && ContextOwnerAvailable(binding);
        if (binding.Element is WidgetIndexedCollectionView indexed)
            indexed.ContextHintAdmitted = adapter.ContextHintAdmitted;
        adapter.SelectionSurface = binding.MotionHost?.SelectionSurface;
        adapter.RoundedContent = binding.Children as WidgetPosterPanel;
        adapter.DepthSlotsFactory = (binding.Element is Panel or Border or WidgetPresentationSurface || binding.MotionHost?.SelectionSurface is not null) && binding.MotionHost is { } depthHost
            ? depthHost.EnsureDepthSlots : null;
        adapter.TextContent = node.Kind is ViewNodeKind.Button or ViewNodeKind.Select or ViewNodeKind.TextEntry && binding.Element is ButtonBase button
            ? button.Content switch { TextBlock label => label, Grid panel => panel.Children.OfType<TextBlock>().SingleOrDefault(), _ => null } : null;
        adapter.Update(presentation?.RenderStyles.GetValueOrDefault(node.Id),
            typographyOnly: indexedRootStyleOnContainer && node.Id == fragmentRootId || binding.Element is HyperlinkButton,
            interaction: indexedRootStyleOnContainer && node.Id == fragmentRootId ? indexedRootInteraction : null);
        if (binding.Identity.Scope != activeScope) adapter.SetControllerPressed(false);
    }

    private void RetireComputedStyles(Binding binding)
    {
        if (!nativeStyles.Remove(binding, out var adapter)) return;
        if (ReferenceEquals(controllerPressedStyle, adapter)) controllerPressedStyle = null;
        adapter.Dispose();
    }

    private void RefreshContextIndicators()
    {
        foreach (var adapter in nativeStyles.Values) adapter.RefreshContextIndicator();
        foreach (var binding in bindings.Values)
            if (binding.Element is WidgetIndexedCollectionView collection) collection.RefreshContextIndicators();
    }

    private void UpdateControllerPressedStyle(ControllerEventPhase phase)
    {
        if (phase == ControllerEventPhase.Repeated) return;
        if (phase == ControllerEventPhase.Released)
        {
            controllerPressedStyle?.SetControllerPressed(false); controllerPressedStyle = null;
            FindIndexedCollection()?.SetControllerPressedStyle(false);
            return;
        }
        controllerPressedStyle?.SetControllerPressed(false); controllerPressedStyle = null;
        if (FocusedBinding() is not { } binding || !Eligible(binding)) return;
        if (binding.Element is WidgetIndexedCollectionView collection) collection.SetControllerPressedStyle(true);
        else if (nativeStyles.TryGetValue(binding, out var adapter))
        { controllerPressedStyle = adapter; adapter.SetControllerPressed(true); }
    }
}

/// <summary>One computed-state adapter; absent properties relinquish local ownership.</summary>
internal sealed class NativeComputedStyleAdapter : IDisposable
{
    private static WidgetMotionOptions motionOptions = WidgetMotionOptions.From(AppearanceSettings.Default, true);
    private static WidgetAccessibilityPolicy accessibility = WidgetAccessibilityPolicy.From(AppearanceSettings.Default);
    internal static void SetAccessibilityPolicy(AppearanceSettings settings)
    {
        var next = WidgetAccessibilityPolicy.From(settings);
        if (next == accessibility) return;
        accessibility = next; EnvironmentChanged?.Invoke();
    }
    private static double defaultTextScale = 1;
    private NativeTextScaleScope? textScaleScope;
    private double? textScaleOverride;
    private double textScale => textScaleOverride ?? textScaleScope?.Value ?? defaultTextScale;
    internal void SetOwnTextScale(double value)
    {
        value = NativeTextScaleScope.Normalize(value);
        if (textScaleOverride == value) return;
        textScaleOverride = value;
        Apply();
    }
    internal void RefreshTextScale() => Apply();
    private void BindTextScale(NativeTextScaleScope? next)
    {
        if (ReferenceEquals(textScaleScope, next)) return;
        if (textScaleScope is not null) textScaleScope.Changed -= Queue;
        textScaleScope = next;
        if (textScaleScope is not null) textScaleScope.Changed += Queue;
    }
    internal static void SetTextScale(double value)
    {
        value = double.IsFinite(value) ? Math.Clamp(value, AppearanceSettings.MinimumTextScale, AppearanceSettings.MaximumTextScale) : 1;
        if (value == defaultTextScale) return;
        defaultTextScale = value; EnvironmentChanged?.Invoke();
    }
    internal static void SetMotionPolicy(AppearanceSettings appearance, bool animationsEnabled)
    {
        var next = WidgetMotionOptions.From(appearance, animationsEnabled);
        if (next == motionOptions) return;
        motionOptions = next; EnvironmentChanged?.Invoke();
    }
    private static int systemHighContrast;
    private static int highContrastOverride = -1;
    private static readonly ConditionalWeakTable<FrameworkElement, NativeComputedStyleAdapter> owners = new();
    private static readonly ConditionalWeakTable<XamlRoot, RootFocusPresentation> focusRoots = new();
    private sealed class RootFocusPresentation
    {
        internal bool Enabled = true;
        internal event Action? Changed;
        internal void SetEnabled(bool enabled)
        {
            if (Enabled == enabled) return;
            Enabled = enabled;
            Changed?.Invoke();
        }
    }
    // Native focus remains remembered per XamlRoot while another HWND owns
    // interaction. Only that root's existing/new adapters change presentation;
    // this weak policy neither moves focus nor disables widget content.
    internal static void SetRootFocusPresentation(XamlRoot root, bool enabled) =>
        focusRoots.GetValue(root, static _ => new()).SetEnabled(enabled);
    private static event Action? EnvironmentChanged;
    internal static void SetSystemHighContrast(bool value)
    {
        Interlocked.Exchange(ref systemHighContrast, value ? 1 : 0);
        // ThemeSettings.Changed also reports a new contrast palette while the
        // boolean remains unchanged. Retained brushes must reread system colors.
        EnvironmentChanged?.Invoke();
    }
    internal static void SetHighContrastOverride(bool? value)
    { Volatile.Write(ref highContrastOverride, value is null ? -1 : value.Value ? 1 : 0); EnvironmentChanged?.Invoke(); }
    internal static NativeComputedStyleAdapter? For(FrameworkElement element) => owners.TryGetValue(element, out var adapter) ? adapter : null;

    private readonly FrameworkElement element;
    private readonly bool interactive;
    private readonly bool selectorInput;
    private RootFocusPresentation? focusRoot;
    private WidgetContextIndicator? contextIndicator;
    internal ControllerButton? ContextHintButton { get; set; }
    internal Func<bool>? ContextHintAdmitted { get; set; }
    internal Panel? ContextHintHost { get; set; }
    internal WidgetContextIndicator? ContextIndicator => contextIndicator;
    internal FrameworkElement Target => element;
    private readonly Dictionary<DependencyProperty, object> originals = [];
    private readonly Dictionary<string, (Color Color, SolidColorBrush Brush)> brushes = new(StringComparer.Ordinal);
    private (Color Color, double Amount, LinearGradientBrush Brush)? shadedBackground;
    private FontFamily? fontFamily;
    internal TextBlock? TextContent { get; set; }
    private TextBlock? styledText;
    private WidgetTextStyleAdapter? textStyle;
    private readonly List<(DependencyProperty Property, long Token)> registrations = [];
    private BridgeNodeRenderStyles? styles;
    private ResourceDictionary? priorResources;
    private ResourceDictionary? resources;
    private bool typographyOnly;
    private WidgetControlScaleMotion? scaleMotion;
    private WidgetFocusDecoration? focusDecoration;
    internal Border? SelectionSurface { get; set; }
    internal WidgetPosterPanel? RoundedContent { get; set; }
    internal Func<(Grid Shadow, Grid Edges)>? DepthSlotsFactory { get; set; }
    private WidgetNativeDepth? depth;
    private WidgetNativeButtonStates? buttonStates;
    private WidgetNativeRoundedBorder? roundedBorder;
    internal bool UsesVectorBorder => roundedBorder?.IsActive == true;
    private WidgetNativeSelectorPaint? selectorPaint;
    internal WidgetNativeDepth? Depth => depth;
    private static readonly SolidColorBrush transparentSurface = new(Microsoft.UI.Colors.Transparent);
    internal WidgetFocusDecoration? FocusDecoration => focusDecoration;
    internal WidgetControlScaleMotion? ScaleMotion => scaleMotion;
    internal double ArtworkScaleEnvelope => !interactive ? 1 : Math.Max(1,
        Math.Min(2, Math.Max(Number(styles?.Base, "scale") ?? 1,
            Math.Max(Number(styles?.Focused, "scale") ?? 1, Number(styles?.Pressed, "scale") ?? 1))));
    private (bool Focused, bool Pressed)? interactionOverride;
    private (bool Focused, bool Pressed)? lastInteraction;
    internal event Action<bool, bool>? InteractionChanged;
    internal (bool Focused, bool Pressed) Interaction => lastInteraction ?? (false, false);
    internal IReadOnlyDictionary<string, BridgeComputedStyleValue>? InspectionStyle =>
        Interaction.Pressed ? styles?.Pressed : Interaction.Focused ? styles?.Focused : styles?.Base;
    private bool pointerPressed;
    private bool keyboardPressed;
    private bool controllerPressed;
    private int queued;
    private volatile bool disposed;

    internal NativeComputedStyleAdapter(FrameworkElement element, bool interactive = true)
    {
        this.element = element;
        this.interactive = interactive && element is Control;
        selectorInput = this.interactive && element is SelectorItem;
        if (owners.TryGetValue(element, out var previous)) previous.Dispose();
        owners.Add(element, this);
        if (this.interactive) { element.GotFocus += Changed; element.LostFocus += Changed; }
        element.ActualThemeChanged += ThemeChanged;
        element.SizeChanged += ShapeSizeChanged;
        element.Unloaded += Unloaded;
        element.Loaded += FocusRootLoaded;
        if (selectorInput)
        {
            element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PointerPressed), true);
            element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PointerReleased), true);
            element.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(PointerReleased), true);
            element.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(PointerReleased), true);
            element.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(KeyDown), true);
            element.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler(KeyUp), true);
        }
        if (this.interactive) Watch(Control.FocusStateProperty);
        if (element is Control) Watch(Control.IsEnabledProperty);
        if (element is Slider) Watch(Control.IsFocusEngagedProperty);
        if (this.interactive && element is ButtonBase) Watch(ButtonBase.IsPressedProperty);
        EnvironmentChanged += Queue;
    }
    private void Watch(DependencyProperty property) => registrations.Add((property,
        element.RegisterPropertyChangedCallback(property, (_, _) => Queue())));

    internal void Update(BridgeNodeRenderStyles? value, bool typographyOnly = false, (bool Focused, bool Pressed)? interaction = null)
    { styles = value; this.typographyOnly = typographyOnly; interactionOverride = interaction; Apply(); }
    internal void SetControllerPressed(bool value)
    { if (disposed) return; controllerPressed = value; Apply(); }

    private void Changed(object sender, RoutedEventArgs args) => Queue();
    private void ThemeChanged(FrameworkElement sender, object args) => Queue();
    private void ShapeSizeChanged(object sender, SizeChangedEventArgs args)
    { if (styles?.Base.GetValueOrDefault("shape")?.Text is "circle" or "pill" || styles?.Focused.GetValueOrDefault("shape")?.Text is "circle" or "pill") Queue(); }

    private double? ShapeRadius(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style)
    {
        var radius = Number(style, "corner-radius", true);
        return style?.GetValueOrDefault("shape")?.Text switch
        {
            "circle" or "pill" => Math.Min(element.ActualWidth, element.ActualHeight) / 2,
            "rounded" => Math.Max(8, radius ?? 0),
            _ => radius,
        };
    }
    private void Unloaded(object sender, RoutedEventArgs args)
    { pointerPressed = keyboardPressed = controllerPressed = false; Apply(); BindFocusRoot(null); BindTextScale(null); }
    private void FocusRootLoaded(object sender, RoutedEventArgs args) => Apply();
    private void BindFocusRoot(XamlRoot? root)
    {
        var next = root is null ? null : focusRoots.GetValue(root, static _ => new());
        if (ReferenceEquals(next, focusRoot)) return;
        if (focusRoot is not null) focusRoot.Changed -= Apply;
        focusRoot = next;
        if (focusRoot is not null) focusRoot.Changed += Apply;
    }
    private void PointerPressed(object sender, PointerRoutedEventArgs args) { pointerPressed = true; Queue(); }
    private void PointerReleased(object sender, PointerRoutedEventArgs args) { pointerPressed = false; Queue(); }
    private void KeyDown(object sender, KeyRoutedEventArgs args)
    { if (!Input.GamepadKeyBoundary.Owns(element, args) && args.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space) { keyboardPressed = true; Queue(); } }
    private void KeyUp(object sender, KeyRoutedEventArgs args)
    { if (!Input.GamepadKeyBoundary.Owns(element, args) && args.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space) { keyboardPressed = false; Queue(); } }
    private void Queue()
    {
        if (disposed || Interlocked.CompareExchange(ref queued, 1, 0) != 0) return;
        if (!element.DispatcherQueue.TryEnqueue(() => { Interlocked.Exchange(ref queued, 0); if (!disposed) Apply(); }))
            Interlocked.Exchange(ref queued, 0);
    }

    private void Apply()
    {
        if (disposed) return;
        if (!element.DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Apply computed styles on the element dispatcher.");
        BindTextScale(NativeTextScaleScope.Find(element));
        BindFocusRoot(element.XamlRoot);
        var presentFocus = focusRoot?.Enabled != false;
        if (!presentFocus) { focusDecoration?.Dispose(); focusDecoration = null; }
        // Restore the authored native-focus policy before creating a custom
        // decoration, whose owner independently suppresses the native outline.
        if (element is Control) Put(Control.UseSystemFocusVisualsProperty, presentFocus ? null : false);
        var focused = presentFocus && (interactionOverride?.Focused ?? (interactive && element is Control { FocusState: not FocusState.Unfocused }));
        var enabled = element is not Control { IsEnabled: false };
        if (!focused || !enabled) controllerPressed = keyboardPressed = false;
        if (!enabled || !presentFocus) pointerPressed = false;
        var pressed = presentFocus && enabled && (interactionOverride?.Pressed ?? (interactive && (controllerPressed || pointerPressed || keyboardPressed || element is ButtonBase { IsPressed: true })));
        if (lastInteraction != (focused, pressed))
        { lastInteraction = (focused, pressed); InteractionChanged?.Invoke(focused, pressed); }
        var style = pressed ? styles?.Pressed : focused ? styles?.Focused : styles?.Base;
        if (typographyOnly && RoundedContent is { } poster)
            poster.SetContentCornerRadius(ShapeRadius(style) is { } authoredRadius
                ? new CornerRadius(authoredRadius) : new());
        var systemContrast = Volatile.Read(ref highContrastOverride) is var contrastOverride && (contrastOverride < 0 ? Volatile.Read(ref systemHighContrast) != 0 : contrastOverride != 0);
        var contrast = accessibility.HighContrast(systemContrast);
        var fontSize = Number(style, "font-size", true) is { } font ? Math.Clamp(font, 1, 512) * textScale : (double?)null;
        if (element is TextBlock text)
        {
            Put(TextBlock.ForegroundProperty, Brush(style, "color", contrast, false));
            Put(TextBlock.FontSizeProperty, fontSize);
            Put(TextBlock.FontWeightProperty, Weight(style));
            Put(TextBlock.FontFamilyProperty, Family(style));
            Put(TextBlock.CharacterSpacingProperty, CharacterSpacing(style, fontSize ?? text.FontSize));
            if (!typographyOnly) Put(TextBlock.PaddingProperty, Spacing(style, "padding"));
            else Put(TextBlock.PaddingProperty, null);
        }
        else if (element is RichTextBlock rich)
        {
            Put(RichTextBlock.ForegroundProperty, Brush(style, "color", contrast, false));
            Put(RichTextBlock.FontSizeProperty, fontSize);
            Put(RichTextBlock.FontWeightProperty, Weight(style));
            Put(RichTextBlock.FontFamilyProperty, Family(style));
            Put(RichTextBlock.CharacterSpacingProperty, CharacterSpacing(style, fontSize ?? rich.FontSize));
            Put(RichTextBlock.PaddingProperty, Spacing(style, "padding"));
            Put(RichTextBlock.TextAlignmentProperty, Alignment(style));
            var lineHeight = Number(style, "line-height") is { } ratio ? Math.Clamp(ratio, .8, 3) * (fontSize ?? rich.FontSize) : (double?)null;
            Put(RichTextBlock.LineHeightProperty, lineHeight);
            Put(RichTextBlock.LineStackingStrategyProperty, lineHeight is null ? null : LineStackingStrategy.MaxHeight);
        }
        else if (element is FontIcon icon)
        {
            Put(IconElement.ForegroundProperty, Brush(style, "color", contrast, false));
            Put(FontIcon.FontSizeProperty, fontSize);
            // Glyphs own their specific font family; ordinary text themes cannot replace it.
        }
        else if (element is IconElement) Put(IconElement.ForegroundProperty, Brush(style, "color", contrast, false));
        else if (element is Control control)
        {
            Put(Control.ForegroundProperty, Brush(style, "color", contrast, false));
            Put(Control.FontSizeProperty, fontSize);
            Put(Control.FontWeightProperty, Weight(style));
            Put(Control.FontFamilyProperty, Family(style));
            Put(Control.CharacterSpacingProperty, CharacterSpacing(style, fontSize ?? control.FontSize));
            // A live preview is a graphics viewport, not a text presenter.
            // Text alignment on its authored placeholder must not center the
            // empty capture Grid at its zero intrinsic width.
            Put(Control.HorizontalContentAlignmentProperty, element is Previews.WidgetWindowPreview ? null : Alignment(style) switch
            {
                TextAlignment.Left => HorizontalAlignment.Left, TextAlignment.Right => HorizontalAlignment.Right,
                TextAlignment.Center => HorizontalAlignment.Center, _ => (HorizontalAlignment?)null,
            });
        }
        var textTarget = element as TextBlock ?? TextContent;
        if (!ReferenceEquals(styledText, textTarget))
        {
            textStyle?.Dispose();
            styledText = textTarget;
            textStyle = textTarget is null ? null : new(textTarget);
        }
        if (textTarget is not null)
            textStyle!.Apply(style, fontSize ?? (element is Control textOwner ? textOwner.FontSize : textTarget.FontSize), Alignment(style));
        if (typographyOnly)
        {
            depth?.Dispose(); depth = null;
            focusDecoration?.Dispose(); focusDecoration = null;
            scaleMotion?.Dispose(); scaleMotion = null;
            RestoreSurfaces();
            if (element is Control) Put(Control.UseSystemFocusVisualsProperty, presentFocus ? null : false);
            return;
        }
        // A collection item has one box owner. Its authored margin surrounds
        // the native SelectorItem, so scaling its paint/focus stays inside the
        // reserved gap instead of scaling an entire margin-inclusive cell.
        RefreshBoxLayout();
        if (interactive && (styles?.Base.ContainsKey("scale") == true || styles?.Focused.ContainsKey("scale") == true || styles?.Pressed.ContainsKey("scale") == true))
        {
            scaleMotion ??= new(element);
            scaleMotion.Apply((float)(Number(style, "scale") ?? 1), Number(style, "transition-duration") ?? 0,
                style?.GetValueOrDefault("transition-easing")?.Text, motionOptions);
        }
        else { scaleMotion?.Dispose(); scaleMotion = null; }
        var focusStyle = pressed ? styles?.Pressed : styles?.Focused;
        var engagedSlider = element is Slider { IsFocusEngaged: true };
        if (presentFocus && interactive && !contrast && !engagedSlider && element is Control focusControl &&
            Number(focusStyle, "outline-width", true) is > 0 and var outlineWidth &&
            focusStyle?.GetValueOrDefault("outline-color") is { } outlineColor && TryColor(outlineColor.Text, out var focusColor))
        {
            focusDecoration ??= WidgetFocusDecoration.Create(focusControl);
            focusDecoration?.Apply(focusColor, (float)outlineWidth, (float)(ShapeRadius(focusStyle) ?? 0),
                (float)(Number(focusStyle, "outline-offset") ?? 0), focused, motionOptions);
        }
        else { focusDecoration?.Dispose(); focusDecoration = null; }
        if (element is Slider)
        {
            // WinUI's engaged slider template moves its focus target onto the
            // native thumb. Keep that cue in the authored theme; system colors
            // remain authoritative in high contrast.
            Put(Control.FocusVisualPrimaryBrushProperty, engagedSlider && !contrast ? Brush(focusStyle, "outline-color", false, false) : null);
            Put(Control.FocusVisualSecondaryBrushProperty, engagedSlider && !contrast ? transparentSurface : null);
        }
        var opacity = Number(style, "opacity");
        Put(UIElement.OpacityProperty, accessibility.Opacity(opacity, contrast));
        var background = SurfaceBackground(Brush(style, "background", contrast, true), style, contrast);
        var border = Brush(style, "border-color", contrast, false);
        var padding = Spacing(style, "padding");
        var radius = ShapeRadius(style) is { } value ? new CornerRadius(value) : (CornerRadius?)null;
        var thickness = BorderWidth(style);
        var edgeColors = new[] { "border-top-color", "border-right-color", "border-bottom-color", "border-left-color" };
        var hasEdges = !contrast && edgeColors.Any(property => style?.ContainsKey(property) == true);
        var shadowColor = !contrast && style?.GetValueOrDefault("shadow-color") is { } shadowValue && TryColor(shadowValue.Text, out var shadowRgba)
            ? shadowRgba : Microsoft.UI.Colors.Transparent;
        var supportsDepth = DepthSlotsFactory is not null ||
            element is Button && WidgetNativeDepth.HasTemplateSlots(element) ||
            element is SelectorItem && WidgetVisualAdornment.CanAttach(element);
        if (supportsDepth && (hasEdges || shadowColor.A > 0))
        {
            Color Edge(string property) => style?.GetValueOrDefault(property) is { } authored && TryColor(authored.Text, out var color)
                ? color : border is SolidColorBrush fallback ? fallback.Color : Microsoft.UI.Colors.Transparent;
            depth ??= new(element, DepthSlotsFactory?.Invoke());
            depth.Apply(new(shadowColor, (float)Math.Clamp(Number(style, "shadow-blur", true) ?? 0, 0, 64),
                new((float)Math.Clamp(Number(style, "shadow-offset-x") ?? 0, -256, 256),
                    (float)Math.Clamp(Number(style, "shadow-offset-y") ?? 0, -256, 256)),
                (float)(radius?.TopLeft ?? 0), hasEdges ? thickness ?? new() : new(),
                Edge(edgeColors[0]), Edge(edgeColors[1]), Edge(edgeColors[2]), Edge(edgeColors[3]),
                DepthSlotsFactory is null ? 1 : (float)element.Opacity));
            if (hasEdges) border = transparentSurface;
        }
        else { depth?.Dispose(); depth = null; }
        if (SelectionSurface is { } selection)
        {
            selection.Background = background;
            selection.BorderBrush = border;
            selection.BorderThickness = thickness ?? new();
            selection.CornerRadius = radius ?? new();
            selection.Opacity = accessibility.Opacity(opacity, contrast) ?? 1;
            background = border = transparentSurface;
        }
        switch (element)
        {
            case Control:
                Put(Control.BackgroundProperty, background); Put(Control.BorderBrushProperty, border);
                Put(Control.BorderThicknessProperty, thickness); Put(Control.CornerRadiusProperty, radius); Put(Control.PaddingProperty, padding);
                break;
            case Grid:
                Put(Panel.BackgroundProperty, background); Put(Grid.BorderBrushProperty, border);
                Put(Grid.BorderThicknessProperty, thickness); Put(Grid.CornerRadiusProperty, radius); Put(Grid.PaddingProperty, padding);
                break;
            case StackPanel:
                Put(Panel.BackgroundProperty, background); Put(StackPanel.BorderBrushProperty, border);
                Put(StackPanel.BorderThicknessProperty, thickness); Put(StackPanel.CornerRadiusProperty, radius); Put(StackPanel.PaddingProperty, padding);
                break;
            case Border:
                Put(Border.BackgroundProperty, background); Put(Border.BorderBrushProperty, border);
                Put(Border.BorderThicknessProperty, thickness); Put(Border.CornerRadiusProperty, radius); Put(Border.PaddingProperty, padding);
                break;
            case Panel: Put(Panel.BackgroundProperty, background); break;
        }
        if (RoundedContent is { } rounded)
            rounded.SetContentCornerRadius(element is Control owner ? owner.CornerRadius : radius ?? new());
        if (element is SelectorItem selector)
        {
            selectorPaint ??= new(selector);
            selectorPaint.Update(background, Brush(style, "color", contrast, false), border, radius);
            RestoreResources();
        }
        else if (element is Button button)
        {
            UpdateButtonResources(background, Brush(style, "color", contrast, false), border);
            roundedBorder ??= new(button);
            roundedBorder.Update(border, thickness, radius);
        }
        else if (element is Slider)
        {
            var foreground = Brush(style, "color", contrast, false);
            if (foreground is null && background is null) RestoreResources();
            else
            {
                var values = EnsureResources();
                foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
                {
                    WidgetNativeResource.Set(values, "SliderTrackFill" + state, background);
                    WidgetNativeResource.Set(values, "SliderTrackValueFill" + state, foreground);
                    WidgetNativeResource.Set(values, "SliderThumbBackground" + state, foreground);
                    WidgetNativeResource.Set(values, "SliderThumbBorderBrush" + state, border);
                }
                WidgetNativeResource.Set(values, "SliderHeaderForeground", foreground);
                WidgetNativeResource.Set(values, "SliderTickBarFill", foreground);
            }
        }
        else if (element is ScrollViewer or ListViewBase)
        {
            var thumb = Brush(style, "scrollbar-thumb-color", contrast, false);
            var track = Brush(style, "scrollbar-track-color", contrast, true);
            var scrollbarWidth = Number(style, "scrollbar-width", true);
            if (thumb is null && track is null && scrollbarWidth is null) RestoreResources();
            else WidgetScrollBarResources.Apply(EnsureResources(), thumb, track, scrollbarWidth);
        }
        RefreshContextIndicator();
    }

    internal void RefreshContextIndicator()
    {
        if (disposed || element is not Control control) return;
        var prompt = lastInteraction?.Focused == true && control.IsEnabled && ContextHintAdmitted?.Invoke() == true ? ContextHintButton : null;
        if (prompt is not null) contextIndicator ??= new(control);
        contextIndicator?.Update(prompt, ContextHintHost);
    }

    private void Put(DependencyProperty property, object? value)
    {
        if (value is null)
        {
            if (!originals.Remove(property, out var original)) return;
            if (ReferenceEquals(original, DependencyProperty.UnsetValue)) element.ClearValue(property);
            else element.SetValue(property, original);
            return;
        }
        if (!originals.ContainsKey(property)) originals.Add(property, element.ReadLocalValue(property));
        var current = element.GetValue(property);
        if (current is FontFamily currentFamily && value is FontFamily family && currentFamily.Source == family.Source) return;
        if (!Equals(current, value)) element.SetValue(property, value);
    }
    internal void RefreshBoxLayout()
    {
        if (disposed || element is not SelectorItem) return;
        Put(FrameworkElement.MarginProperty, Spacing(styles?.Base, "margin"));
        Put(FrameworkElement.WidthProperty, BoxLength("width"));
        Put(FrameworkElement.HeightProperty, BoxLength("height"));
        Put(FrameworkElement.MinWidthProperty, BoxLength("min-width"));
        Put(FrameworkElement.MinHeightProperty, BoxLength("min-height"));
        Put(FrameworkElement.MaxWidthProperty, BoxLength("max-width"));
        Put(FrameworkElement.MaxHeightProperty, BoxLength("max-height"));
    }
    private double? BoxLength(string property)
    {
        if (styles?.Base.GetValueOrDefault(property) is not { Number: { } number } value || !double.IsFinite(number) || number < 0) return null;
        var viewport = WidgetViewPresenter.ViewportFor(element);
        return value.Unit switch { "px" or null => number, "vw" => viewport.Width * number / 100, "vh" => viewport.Height * number / 100, _ => null };
    }

    private void RestoreSurfaces()
    {
        roundedBorder?.Restore();
        // A realized row's native SelectorItem owns its box, padding and opacity.
        // The noninteractive fragment receives typography only on its root.
        foreach (var property in originals.Keys.ToArray())
            if (property != TextBlock.ForegroundProperty && property != TextBlock.FontSizeProperty && property != TextBlock.FontWeightProperty &&
                property != TextBlock.FontFamilyProperty && property != TextBlock.CharacterSpacingProperty &&
                property != Control.ForegroundProperty && property != Control.FontSizeProperty && property != Control.FontWeightProperty && property != Control.FontFamilyProperty &&
                property != Control.CharacterSpacingProperty) Put(property, null);
        RestoreResources();
    }

    private void UpdateButtonResources(Brush? background, Brush? foreground, Brush? border)
    {
        if (element is Button button)
        {
            buttonStates ??= WidgetNativeButtonStates.Create(button);
            if (buttonStates is not null)
            {
                buttonStates.Update(background is not null, foreground is not null, border is not null);
                RestoreResources();
                return;
            }
        }
        if (background is null && foreground is null && border is null) { RestoreResources(); return; }
        EnsureResources();
        var prefix = element is GridViewItem ? "GridViewItem" : element is ListViewItem ? "ListViewItem" : "Button";
        foreach (var (name, brush) in new[] { ("Background", background), ("Foreground", foreground), ("BorderBrush", border) })
            foreach (var suffix in new[] { "", "PointerOver", "Pressed", "Disabled", "Selected", "SelectedPointerOver", "SelectedPressed" })
            {
                var key = prefix + name + suffix;
                if (brush is null) resources!.Remove(key);
                else if (!resources!.TryGetValue(key, out var current) || !ReferenceEquals(current, brush)) resources[key] = brush;
            }
    }
    private ResourceDictionary EnsureResources()
    {
        if (resources is null)
        {
            priorResources = element.Resources;
            resources = new();
            // A native ResourceDictionary may have only one parent. Detach the
            // existing dictionary before nesting it, retaining its live resources.
            element.Resources = resources;
            resources.MergedDictionaries.Add(priorResources);
        }
        return resources;
    }
    private void RestoreResources()
    {
        if (resources is null) return;
        var ownsResources = ReferenceEquals(element.Resources, resources);
        resources.MergedDictionaries.Clear();
        if (ownsResources) element.Resources = priorResources!;
        resources = null; priorResources = null;
    }

    private SolidColorBrush? Brush(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style, string property, bool contrast, bool background)
    {
        if (style?.GetValueOrDefault(property) is not { } value || !TryColor(value.Text, out var color)) return null;
        if (background) color.A = accessibility.BackgroundAlpha(color.A);
        if (contrast && color.A != 0)
        {
            var resources = Application.Current.Resources;
            if (resources.TryGetValue("SystemColorWindowColor", out var window) && window is Color windowColor &&
                resources.TryGetValue("SystemColorWindowTextColor", out var text) && text is Color textColor)
            {
                color = background ? windowColor : textColor;
                if (!background && element is Control { IsEnabled: false } && resources.TryGetValue("SystemColorGrayTextColor", out var gray) && gray is Color grayColor)
                    color = grayColor;
            }
            else color = background ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255);
        }
        if (brushes.TryGetValue(property, out var previous) && previous.Color == color) return previous.Brush;
        var brush = new SolidColorBrush(color); brushes[property] = (color, brush); return brush;
    }
    private Brush? SurfaceBackground(SolidColorBrush? background, IReadOnlyDictionary<string, BridgeComputedStyleValue>? style, bool contrast)
    {
        var amount = contrast ? 0 : Math.Clamp(Number(style, "surface-shading") ?? 0, -.25, .25);
        if (background is null || amount == 0 || background.Color.A == 0) { shadedBackground = null; return background; }
        if (shadedBackground is { } cached && cached.Color == background.Color && cached.Amount == amount) return cached.Brush;
        var gradient = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
        gradient.GradientStops.Add(new() { Offset = 0, Color = Shade(background.Color, amount) });
        gradient.GradientStops.Add(new() { Offset = 1, Color = Shade(background.Color, -amount) });
        shadedBackground = (background.Color, amount, gradient);
        return gradient;
    }
    private static Color Shade(Color color, double amount)
    {
        byte Channel(byte value) => (byte)Math.Clamp(Math.Round(amount >= 0 ? value + (255 - value) * amount : value * (1 + amount)), 0, 255);
        return Color.FromArgb(color.A, Channel(color.R), Channel(color.G), Channel(color.B));
    }
    private static double? Number(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style, string property, bool length = false) =>
        style?.GetValueOrDefault(property) is { Number: { } value } candidate && double.IsFinite(value) &&
        (!length || candidate.Unit is null or "px") ? length ? Math.Max(0, value) : value : null;
    private static FontWeight? Weight(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style)
    {
        ushort? authored = style?.GetValueOrDefault("font-weight") is { } value
            ? (ushort)Math.Clamp(value.Number ?? (value.Text == "bold" ? 700 : 400), 100, 900) : null;
        return accessibility.FontWeight(authored) is { } weight ? new FontWeight { Weight = weight } : null;
    }
    private FontFamily? Family(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style)
    {
        if (style?.GetValueOrDefault("font-family") is not { Text.Length: > 0 } value) return null;
        var source = value.Text.Replace("\"", "").Replace("'", "");
        return fontFamily?.Source == source ? fontFamily : fontFamily = new(source);
    }
    private TextAlignment? Alignment(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style) =>
        style?.GetValueOrDefault("text-align")?.Text switch
        {
            "center" => TextAlignment.Center,
            "start" => element.FlowDirection == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left,
            "end" => element.FlowDirection == FlowDirection.RightToLeft ? TextAlignment.Left : TextAlignment.Right,
            _ => null,
        };
    private int? CharacterSpacing(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style, double fontSize) =>
        style?.GetValueOrDefault("letter-spacing") is { Number: { } number } value && double.IsFinite(number) && fontSize > 0
            ? value.Unit switch { "em" => (int)Math.Round(number * 1000), null or "px" => (int)Math.Round(number * textScale * 1000 / fontSize), _ => null } : null;
    private static Thickness? BorderWidth(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style)
    {
        var all = Number(style, "border-width", true);
        var left = Number(style, "border-left-width", true); var top = Number(style, "border-top-width", true);
        var right = Number(style, "border-right-width", true); var bottom = Number(style, "border-bottom-width", true);
        return all is null && left is null && top is null && right is null && bottom is null ? null :
            new(left ?? all ?? 0, top ?? all ?? 0, right ?? all ?? 0, bottom ?? all ?? 0);
    }
    internal static Thickness? Spacing(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style, string property)
    {
        if (style?.GetValueOrDefault(property) is not { } value) return null;
        var parts = value.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4) return null;
        var numbers = new double[parts.Length];
        for (var i = 0; i < parts.Length; ++i)
        {
            var text = parts[i].EndsWith("px", StringComparison.Ordinal) ? parts[i][..^2] : parts[i];
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) || !double.IsFinite(numbers[i]) || numbers[i] < 0) return null;
        }
        return parts.Length switch
        {
            1 => new(numbers[0]), 2 => new(numbers[1], numbers[0], numbers[1], numbers[0]),
            3 => new(numbers[1], numbers[0], numbers[1], numbers[2]),
            _ => new(numbers[3], numbers[0], numbers[1], numbers[2]),
        };
    }

    internal static bool TryColor(string text, out Color color)
    {
        color = default;
        if (text == "transparent") return true;
        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(character => new string(character, 2)));
            if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bits)) return false;
            color = hex.Length == 6 ? Color.FromArgb(255, (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits)
                : Color.FromArgb((byte)bits, (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8)); return true;
        }
        var alpha = text.StartsWith("rgba(", StringComparison.Ordinal);
        if ((!alpha && !text.StartsWith("rgb(", StringComparison.Ordinal)) || !text.EndsWith(')')) return false;
        var parts = text[(alpha ? 5 : 4)..^1].Split(',');
        if (parts.Length != (alpha ? 4 : 3)) return false;
        var bytes = new byte[] { 0, 0, 0, 255 };
        for (var i = 0; i < parts.Length; ++i)
        {
            var part = parts[i].Trim(); var percent = part.EndsWith('%');
            if (!double.TryParse(percent ? part[..^1] : part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) return false;
            var maximum = percent ? 100 : i == 3 ? 1 : 255;
            if (number < 0 || number > maximum) return false;
            bytes[i] = (byte)Math.Round(number * 255 / maximum, MidpointRounding.AwayFromZero);
        }
        color = Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2]); return true;
    }

    public void Dispose()
    {
        if (disposed) return;
        BindTextScale(null);
        BindFocusRoot(null);
        contextIndicator?.Dispose(); contextIndicator = null; ContextHintAdmitted = null; ContextHintHost = null;
        textStyle?.Dispose(); textStyle = null; styledText = null;
        scaleMotion?.Dispose(); scaleMotion = null;
        depth?.Dispose(); depth = null;
        buttonStates?.Restore(); buttonStates = null;
        roundedBorder?.Restore(); roundedBorder = null;
        selectorPaint?.Dispose(); selectorPaint = null;
        focusDecoration?.Dispose(); focusDecoration = null;
        foreach (var property in originals.Keys.ToArray()) Put(property, null);
        RestoreResources();
        if (interactive) { element.GotFocus -= Changed; element.LostFocus -= Changed; }
        element.ActualThemeChanged -= ThemeChanged; element.Unloaded -= Unloaded; element.Loaded -= FocusRootLoaded; element.SizeChanged -= ShapeSizeChanged;
        if (selectorInput)
        {
            element.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PointerPressed));
            element.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PointerReleased));
            element.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(PointerReleased));
            element.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(PointerReleased));
            element.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(KeyDown)); element.RemoveHandler(UIElement.KeyUpEvent, new KeyEventHandler(KeyUp));
        }
        foreach (var (property, token) in registrations) element.UnregisterPropertyChangedCallback(property, token);
        EnvironmentChanged -= Queue; owners.Remove(element); brushes.Clear(); disposed = true;
    }
}
