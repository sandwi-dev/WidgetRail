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
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.ViewManagement;
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
    internal void UseIndexedContainerStyles() => indexedRootStyleOnContainer = true;
    internal void SetIndexedRootInteraction(bool focused, bool pressed)
    {
        indexedRootInteraction = (focused, pressed);
        if (fragmentRootId is { } root && bindings.TryGetValue(root, out var binding) && declarations.TryGetValue(root, out var declaration))
            ApplyComputedStyles(binding, declaration.Node);
    }
    internal void ResetPressedStyles()
    {
        controllerPressedStyle = null;
        foreach (var adapter in nativeStyles.Values) adapter.SetControllerPressed(false);
        foreach (var binding in bindings.Values)
            if (binding.Element is WidgetIndexedCollectionView collection) collection.SetControllerPressedStyle(false);
    }

    private void ApplyComputedStyles(Binding binding, ViewNode node)
    {
        FrameworkElement target = binding.Element switch
        {
            WidgetPresentationSurface surface => surface.StylePanel,
            WidgetIndexedCollectionView collection => collection.NativeView,
            _ => binding.Element,
        };
        if (nativeStyles.TryGetValue(binding, out var previous) && !ReferenceEquals(previous.Target, target))
        { previous.Dispose(); nativeStyles.Remove(binding); }
        if (!nativeStyles.TryGetValue(binding, out var adapter))
            nativeStyles.Add(binding, adapter = new(target, node.IsFocusable));
        adapter.Update(frame?.RenderStyles.GetValueOrDefault(node.Id),
            typographyOnly: indexedRootStyleOnContainer && node.Id == fragmentRootId,
            interaction: indexedRootStyleOnContainer && node.Id == fragmentRootId ? indexedRootInteraction : null);
        if (binding.Identity.Scope != frame?.Authority.ActiveInputScopeId) adapter.SetControllerPressed(false);
    }

    private void RetireComputedStyles(Binding binding)
    {
        if (!nativeStyles.Remove(binding, out var adapter)) return;
        if (ReferenceEquals(controllerPressedStyle, adapter)) controllerPressedStyle = null;
        adapter.Dispose();
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
    private static readonly AccessibilitySettings accessibility = new();
    private static int highContrastOverride = -1;
    private static readonly ConditionalWeakTable<FrameworkElement, NativeComputedStyleAdapter> owners = new();
    private static event Action? EnvironmentChanged;
    static NativeComputedStyleAdapter() => accessibility.HighContrastChanged += (_, _) => EnvironmentChanged?.Invoke();
    internal static void SetHighContrastOverride(bool? value)
    { Volatile.Write(ref highContrastOverride, value is null ? -1 : value.Value ? 1 : 0); EnvironmentChanged?.Invoke(); }
    internal static NativeComputedStyleAdapter? For(FrameworkElement element) => owners.TryGetValue(element, out var adapter) ? adapter : null;

    private readonly FrameworkElement element;
    private readonly bool interactive;
    private readonly bool selectorInput;
    internal FrameworkElement Target => element;
    private readonly Dictionary<DependencyProperty, object> originals = [];
    private readonly Dictionary<string, (Color Color, SolidColorBrush Brush)> brushes = new(StringComparer.Ordinal);
    private FontFamily? fontFamily;
    private readonly List<(DependencyProperty Property, long Token)> registrations = [];
    private BridgeNodeRenderStyles? styles;
    private ResourceDictionary? priorResources;
    private ResourceDictionary? resources;
    private bool typographyOnly;
    private (bool Focused, bool Pressed)? interactionOverride;
    private (bool Focused, bool Pressed)? lastInteraction;
    internal event Action<bool, bool>? InteractionChanged;
    internal (bool Focused, bool Pressed) Interaction => lastInteraction ?? (false, false);
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
        element.Unloaded += Unloaded;
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
    private void Unloaded(object sender, RoutedEventArgs args)
    { pointerPressed = keyboardPressed = controllerPressed = false; Apply(); }
    private void PointerPressed(object sender, PointerRoutedEventArgs args) { pointerPressed = true; Queue(); }
    private void PointerReleased(object sender, PointerRoutedEventArgs args) { pointerPressed = false; Queue(); }
    private void KeyDown(object sender, KeyRoutedEventArgs args)
    { if (args.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space) { keyboardPressed = true; Queue(); } }
    private void KeyUp(object sender, KeyRoutedEventArgs args)
    { if (args.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space) { keyboardPressed = false; Queue(); } }
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
        var focused = interactionOverride?.Focused ?? (interactive && element is Control { FocusState: not FocusState.Unfocused });
        var enabled = element is not Control { IsEnabled: false };
        if (!focused || !enabled) controllerPressed = keyboardPressed = false;
        if (!enabled) pointerPressed = false;
        var pressed = enabled && (interactionOverride?.Pressed ?? (interactive && (controllerPressed || pointerPressed || keyboardPressed || element is ButtonBase { IsPressed: true })));
        if (lastInteraction != (focused, pressed))
        { lastInteraction = (focused, pressed); InteractionChanged?.Invoke(focused, pressed); }
        var style = pressed ? styles?.Pressed : focused ? styles?.Focused : styles?.Base;
        var contrast = Volatile.Read(ref highContrastOverride) is var contrastOverride && (contrastOverride < 0 ? accessibility.HighContrast : contrastOverride != 0);
        var fontSize = Number(style, "font-size", true) is { } font ? Math.Clamp(font, 1, 512) : (double?)null;
        if (element is TextBlock text)
        {
            Put(TextBlock.ForegroundProperty, Brush(style, "color", contrast, false));
            Put(TextBlock.FontSizeProperty, fontSize);
            Put(TextBlock.FontWeightProperty, Weight(style));
            Put(TextBlock.FontFamilyProperty, Family(style));
            Put(TextBlock.TextAlignmentProperty, Alignment(style));
            Put(TextBlock.CharacterSpacingProperty, CharacterSpacing(style, fontSize ?? text.FontSize));
            if (!typographyOnly) Put(TextBlock.PaddingProperty, Spacing(style, "padding"));
            else Put(TextBlock.PaddingProperty, null);
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
            Put(Control.HorizontalContentAlignmentProperty, Alignment(style) switch
            {
                TextAlignment.Left => HorizontalAlignment.Left, TextAlignment.Right => HorizontalAlignment.Right,
                TextAlignment.Center => HorizontalAlignment.Center, _ => (HorizontalAlignment?)null,
            });
        }
        if (typographyOnly)
        {
            RestoreSurfaces();
            return;
        }
        var opacity = Number(style, "opacity");
        Put(UIElement.OpacityProperty, opacity is null ? null : contrast ? 1d : Math.Clamp(opacity.Value, 0, 1));
        var background = Brush(style, "background", contrast, true);
        var border = Brush(style, "border-color", contrast, false);
        var padding = Spacing(style, "padding");
        var radius = Number(style, "corner-radius", true) is { } value ? new CornerRadius(value) : (CornerRadius?)null;
        var thickness = BorderWidth(style);
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
        if (element is Button or SelectorItem) UpdateButtonResources(background, Brush(style, "color", contrast, false), border);
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

    private void RestoreSurfaces()
    {
        // A realized row's native SelectorItem owns its box, padding and opacity.
        // The noninteractive fragment receives typography only on its root.
        foreach (var property in originals.Keys.ToArray())
            if (property != TextBlock.ForegroundProperty && property != TextBlock.FontSizeProperty && property != TextBlock.FontWeightProperty &&
                property != TextBlock.FontFamilyProperty && property != TextBlock.TextAlignmentProperty && property != TextBlock.CharacterSpacingProperty &&
                property != Control.ForegroundProperty && property != Control.FontSizeProperty && property != Control.FontWeightProperty && property != Control.FontFamilyProperty &&
                property != Control.CharacterSpacingProperty) Put(property, null);
        RestoreResources();
    }

    private void UpdateButtonResources(Brush? background, Brush? foreground, Brush? border)
    {
        if (background is null && foreground is null && border is null) { RestoreResources(); return; }
        if (resources is null)
        {
            priorResources = element.Resources; resources = new(); resources.MergedDictionaries.Add(priorResources); element.Resources = resources;
        }
        var prefix = element is GridViewItem ? "GridViewItem" : element is ListViewItem ? "ListViewItem" : "Button";
        foreach (var (name, brush) in new[] { ("Background", background), ("Foreground", foreground), ("BorderBrush", border) })
            foreach (var suffix in new[] { "", "PointerOver", "Pressed", "Disabled", "Selected", "SelectedPointerOver", "SelectedPressed" })
            {
                var key = prefix + name + suffix;
                if (brush is null) resources.Remove(key);
                else if (!resources.TryGetValue(key, out var current) || !ReferenceEquals(current, brush)) resources[key] = brush;
            }
    }
    private void RestoreResources()
    {
        if (resources is null) return;
        if (ReferenceEquals(element.Resources, resources)) element.Resources = priorResources!;
        resources.MergedDictionaries.Clear(); resources = null; priorResources = null;
    }

    private SolidColorBrush? Brush(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style, string property, bool contrast, bool background)
    {
        if (style?.GetValueOrDefault(property) is not { } value || !TryColor(value.Text, out var color)) return null;
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
    private static double? Number(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style, string property, bool length = false) =>
        style?.GetValueOrDefault(property) is { Number: { } value } candidate && double.IsFinite(value) &&
        (!length || candidate.Unit is null or "px") ? length ? Math.Max(0, value) : value : null;
    private static FontWeight? Weight(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style) =>
        style?.GetValueOrDefault("font-weight") is { } value ? new FontWeight
            { Weight = (ushort)Math.Clamp(value.Number ?? (value.Text == "bold" ? 700 : 400), 100, 900) } : null;
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
    private static int? CharacterSpacing(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style, double fontSize) =>
        style?.GetValueOrDefault("letter-spacing") is { Number: { } number } value && double.IsFinite(number) && fontSize > 0
            ? value.Unit switch { "em" => (int)Math.Round(number * 1000), null or "px" => (int)Math.Round(number * 1000 / fontSize), _ => null } : null;
    private static Thickness? BorderWidth(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style)
    {
        var all = Number(style, "border-width", true);
        var left = Number(style, "border-left-width", true); var top = Number(style, "border-top-width", true);
        var right = Number(style, "border-right-width", true); var bottom = Number(style, "border-bottom-width", true);
        return all is null && left is null && top is null && right is null && bottom is null ? null :
            new(left ?? all ?? 0, top ?? all ?? 0, right ?? all ?? 0, bottom ?? all ?? 0);
    }
    private static Thickness? Spacing(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style, string property)
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
        foreach (var property in originals.Keys.ToArray()) Put(property, null);
        RestoreResources();
        if (interactive) { element.GotFocus -= Changed; element.LostFocus -= Changed; }
        element.ActualThemeChanged -= ThemeChanged; element.Unloaded -= Unloaded;
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
