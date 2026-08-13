using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using GameBarAlternative.AvaloniaPrototype.Presentation;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetPresentationSession;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.AvaloniaPrototype.Integration;

public sealed record SemanticActionRequest(
    string SourceElementId,
    string ActionId,
    double? RequestedValue = null,
    string? CommittedText = null);

public sealed class SemanticTreeRenderer
{
    public static readonly AttachedProperty<string?> SemanticNodeIdProperty =
        AvaloniaProperty.RegisterAttached<SemanticTreeRenderer, Control, string?>("SemanticNodeId");
    public static readonly AttachedProperty<string?> FocusPersistenceIdProperty =
        AvaloniaProperty.RegisterAttached<SemanticTreeRenderer, Control, string?>("FocusPersistenceId");
    public static readonly AttachedProperty<string?> CollectionItemKeyProperty =
        AvaloniaProperty.RegisterAttached<SemanticTreeRenderer, Control, string?>("CollectionItemKey");

    private readonly Func<SemanticActionRequest, Task> dispatch;
    private readonly Func<WidgetPresentationAuthority, string, CancellationToken, Task<ReadOnlyMemory<byte>>> artwork;
    private readonly Func<ViewNode, Task<string?>> requestText;
    private readonly Dictionary<string, Control> focusableById = new(StringComparer.Ordinal);
    private readonly List<(Control Control, FocusNeighbors Neighbors)> pendingNeighbors = [];
    private WidgetPresentationFrame frame = null!;
    private bool compact;

    public SemanticTreeRenderer(
        Func<SemanticActionRequest, Task> dispatch,
        Func<WidgetPresentationAuthority, string, CancellationToken, Task<ReadOnlyMemory<byte>>> artwork,
        Func<ViewNode, Task<string?>> requestText)
    {
        this.dispatch = dispatch;
        this.artwork = artwork;
        this.requestText = requestText;
    }

    public int RealizedControlCount { get; private set; }

    public Control Render(WidgetPresentationFrame currentFrame, bool isCompact)
    {
        frame = currentFrame;
        compact = isCompact;
        focusableById.Clear();
        pendingNeighbors.Clear();
        RealizedControlCount = 0;
        var root = frame.Snapshot.AdvancedPresentation is { } advanced
            ? RenderAdvanced(frame.Snapshot.Root, advanced)
            : RenderNode(frame.Snapshot.Root);
        ApplyFocusNeighbors();
        return root;
    }

    private Control RenderNode(ViewNode node)
    {
        if (!IsVisible(node)) return new Border { IsVisible = false };

        Control control = node.Kind switch
        {
            ViewNodeKind.Stack => RenderStack(node, Orientation.Vertical),
            ViewNodeKind.Row => RenderStack(node, Orientation.Horizontal),
            ViewNodeKind.Scroll => RenderScroll(node),
            ViewNodeKind.Text => new TextBlock
            {
                Text = node.Text ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            },
            ViewNodeKind.Button => RenderButton(node),
            ViewNodeKind.Progress => new ProgressBar
            {
                Minimum = node.Minimum ?? 0,
                Maximum = node.Maximum ?? 100,
                Value = node.Value ?? 0,
                IsIndeterminate = node.Value is null,
                MinHeight = 8,
            },
            ViewNodeKind.Slider => RenderSlider(node),
            ViewNodeKind.Spacer => new Border { MinHeight = 8, MinWidth = 8 },
            ViewNodeKind.Image => RenderImage(node),
            ViewNodeKind.Icon => new TextBlock
            {
                Text = Glyph(node.Glyph),
                FontSize = 22,
                VerticalAlignment = VerticalAlignment.Center,
            },
            ViewNodeKind.LoadingIndicator => new ProgressBar
            {
                IsIndeterminate = true,
                Width = IndicatorSize(node.IndicatorSize),
                Height = IndicatorSize(node.IndicatorSize),
            },
            ViewNodeKind.ActionSurface => RenderActionSurface(node),
            ViewNodeKind.Grid => RenderGrid(node),
            ViewNodeKind.TextEntry => RenderTextEntry(node),
            _ => throw new ArgumentOutOfRangeException(nameof(node.Kind)),
        };

        RealizedControlCount++;
        ApplyIdentity(control, node);
        ApplyStyle(control, node);
        if (node.IsFocusable)
        {
            focusableById[node.Id] = control;
            if (node.Focus is not null) pendingNeighbors.Add((control, node.Focus));
        }

        return control;
    }

    private Control RenderStack(ViewNode node, Orientation orientation)
    {
        Panel panel = orientation == Orientation.Vertical
            ? new StackPanel { Orientation = orientation, Spacing = 8 }
            : new WrapPanel { Orientation = orientation };
        foreach (var child in node.Children)
        {
            var rendered = RenderNode(child);
            rendered.Margin = orientation == Orientation.Horizontal
                ? new Thickness(0, 0, 8, 8)
                : new Thickness(0, 0, 0, 8);
            panel.Children.Add(rendered);
        }

        return panel;
    }

    private Control RenderScroll(ViewNode node)
    {
        if (node.ScrollAxis == ScrollAxis.Horizontal)
        {
            return new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = RenderStack(node, Orientation.Horizontal),
            };
        }

        var list = new ListBox
        {
            ItemsSource = node.Children,
            SelectionMode = SelectionMode.Single,
            MinHeight = 80,
        };
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ViewNode>(
            (item, _) => item is null ? null : RenderVirtualizedItem(item), supportsRecycling: true);
        return list;
    }

    private Button RenderButton(ViewNode node)
    {
        var button = new Button
        {
            Content = node.Text ?? node.AccessibilityLabel ?? "Action",
            IsEnabled = node.IsDisabled is not true,
            MinHeight = 44,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Click += async (_, _) =>
        {
            if (node.ActionId is { Length: > 0 } actionId)
                await dispatch(new SemanticActionRequest(node.Id, actionId));
        };
        return button;
    }

    private Slider RenderSlider(ViewNode node)
    {
        var slider = new Slider
        {
            Minimum = node.Minimum ?? 0,
            Maximum = node.Maximum ?? 100,
            Value = node.Value ?? node.Minimum ?? 0,
            SmallChange = node.Step is > 0 ? node.Step.Value : 1,
            IsEnabled = node.IsDisabled is not true,
            MinHeight = 40,
        };
        var applying = false;
        slider.ValueChanged += async (_, _) =>
        {
            if (applying || node.ValueChangedActionId is not { Length: > 0 } actionId) return;
            var step = slider.SmallChange;
            var quantized = step > 0
                ? slider.Minimum + Math.Round((slider.Value - slider.Minimum) / step) * step
                : slider.Value;
            quantized = Math.Clamp(quantized, slider.Minimum, slider.Maximum);
            applying = true;
            slider.Value = quantized;
            applying = false;
            await dispatch(new SemanticActionRequest(node.Id, actionId, quantized));
        };
        return slider;
    }

    private Control RenderImage(ViewNode node)
    {
        var image = new Image
        {
            Stretch = node.ImageFit switch
            {
                ImageFit.Cover => Stretch.UniformToFill,
                ImageFit.Fill => Stretch.Fill,
                _ => Stretch.Uniform,
            },
            MinHeight = 72,
        };
        if (node.ImageSource is { Length: > 0 } source)
        {
            try
            {
                const string dataPrefix = "data:image/png;base64,";
                if (source.StartsWith(dataPrefix, StringComparison.Ordinal))
                {
                    using var stream = new MemoryStream(Convert.FromBase64String(source[dataPrefix.Length..]));
                    image.Source = new Bitmap(stream);
                }
                else if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
                {
                    image.Source = new Bitmap(uri.ToString());
                }
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or IOException)
            {
                AutomationProperties.SetHelpText(image, "Image unavailable");
            }
        }
        else if (node.ArtworkHandle is { Length: > 0 } handle)
        {
            _ = LoadArtworkAsync(image, frame.Authority, handle);
        }
        return image;
    }

    private Button RenderActionSurface(ViewNode node)
    {
        var content = RenderStack(node, node.ActionSurfaceOrientation == ActionSurfaceOrientation.Horizontal
            ? Orientation.Horizontal
            : Orientation.Vertical);
        var button = new Button
        {
            Content = content,
            IsEnabled = node.IsDisabled is not true,
            MinHeight = 44,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += async (_, _) =>
        {
            if (node.ActionId is { Length: > 0 } actionId)
                await dispatch(new SemanticActionRequest(node.Id, actionId));
        };
        return button;
    }

    private Control RenderGrid(ViewNode node)
    {
        if (node.Children.Count > 64)
        {
            var virtualized = new ListBox
            {
                ItemsSource = node.Children,
                SelectionMode = SelectionMode.Single,
                MinHeight = 80,
            };
            virtualized.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ViewNode>(
                (item, _) => item is null ? null : RenderVirtualizedItem(item), supportsRecycling: true);
            return virtualized;
        }

        var minimum = Math.Max(96, node.GridMinimumColumnWidth ?? 180);
        var columns = Math.Clamp((int)Math.Floor(760 / minimum), 1, node.GridMaximumColumns ?? 8);
        var grid = new Grid();
        for (var column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var index = 0; index < node.Children.Count; index++)
        {
            if (index % columns == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var child = RenderNode(node.Children[index]);
            Grid.SetColumn(child, index % columns);
            Grid.SetRow(child, index / columns);
            child.Margin = new Thickness(4);
            grid.Children.Add(child);
        }
        return grid;
    }

    private Control RenderAdvanced(ViewNode root, WidgetAdvancedPresentationView advanced)
    {
        var slots = Flatten(root)
            .Where(node => node.AdvancedPresentationSlot is not null)
            .ToDictionary(node => node.AdvancedPresentationSlot!.Value);
        if (slots.Count == 0) return RenderNode(root);

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,3*"),
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
        };
        Add(WidgetAdvancedPresentationSlot.DetailsPanel, 0, 0);
        Add(WidgetAdvancedPresentationSlot.PrimaryCollection, 1, 0, rowSpan: 2);
        Add(WidgetAdvancedPresentationSlot.CollectionNavigation, 0, 1);
        Add(WidgetAdvancedPresentationSlot.SourceStatus, 0, 2);
        Add(WidgetAdvancedPresentationSlot.OperationStatus, 1, 2);
        Add(WidgetAdvancedPresentationSlot.ControllerHints, 0, 3, columnSpan: 2);
        if (advanced.Preset is WidgetAdvancedPresentationPreset.CoverWall or
            WidgetAdvancedPresentationPreset.CompactGrid)
        {
            layout.ColumnDefinitions = new ColumnDefinitions("1*,4*");
        }
        else if (advanced.Preset == WidgetAdvancedPresentationPreset.Carousel)
        {
            layout.ColumnDefinitions = new ColumnDefinitions("3*,2*");
        }
        return layout;

        void Add(WidgetAdvancedPresentationSlot slot, int column, int row, int columnSpan = 1, int rowSpan = 1)
        {
            if (!slots.TryGetValue(slot, out var node)) return;
            var control = RenderNode(node);
            control.Margin = new Thickness(5);
            Grid.SetColumn(control, column);
            Grid.SetRow(control, row);
            Grid.SetColumnSpan(control, columnSpan);
            Grid.SetRowSpan(control, rowSpan);
            layout.Children.Add(control);
        }
    }

    private Button RenderTextEntry(ViewNode node)
    {
        var button = new Button
        {
            Content = string.IsNullOrEmpty(node.TextEntryValue)
                ? node.TextEntryPlaceholder ?? node.Text ?? "Enter text"
                : node.TextEntryValue,
            IsEnabled = node.IsDisabled is not true,
            MinHeight = 44,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Click += async (_, _) =>
        {
            if (node.ActionId is not { Length: > 0 } actionId) return;
            var text = await requestText(node);
            if (text is not null)
                await dispatch(new SemanticActionRequest(node.Id, actionId, CommittedText: text));
        };
        return button;
    }

    private void ApplyIdentity(Control control, ViewNode node)
    {
        control.SetValue(SemanticNodeIdProperty, node.Id);
        control.SetValue(FocusPersistenceIdProperty, node.FocusPersistenceId ?? node.Id);
        control.SetValue(CollectionItemKeyProperty, node.CollectionItemKey);
        AutomationProperties.SetAutomationId(control,
            SemanticAutomationIdentity.ForWidgetNode(frame.Authority.WidgetId, node));
        AutomationProperties.SetName(control,
            node.AccessibilityLabel ?? node.Text ?? node.Kind.ToString());
        if (!string.IsNullOrWhiteSpace(node.AccessibilityValue))
            AutomationProperties.SetHelpText(control, node.AccessibilityValue);
    }

    private void ApplyStyle(Control control, ViewNode node)
    {
        foreach (var styleClass in node.StyleClasses.Where(IsSafeClass)) control.Classes.Add(styleClass);
        if (!frame.RenderStyles.TryGetValue(node.Id, out var styles)) return;
        ApplyComputedStyle(control, styles.Base);
        control.GotFocus += (_, _) => ApplyComputedStyle(control, styles.Focused);
        control.LostFocus += (_, _) => ApplyComputedStyle(control, styles.Base);
        control.PointerPressed += (_, _) => ApplyComputedStyle(control, styles.Pressed);
        control.PointerReleased += (_, _) => ApplyComputedStyle(
            control, control.IsFocused ? styles.Focused : styles.Base);
    }

    private static void ApplyComputedStyle(
        Control control,
        IReadOnlyDictionary<string, BridgeComputedStyleValue> values)
    {
        if (TryNumber(values, "min-width") is { } minWidth) control.MinWidth = minWidth;
        if (TryNumber(values, "min-height") is { } minHeight) control.MinHeight = minHeight;
        if (TryNumber(values, "max-width") is { } maxWidth) control.MaxWidth = maxWidth;
        if (TryNumber(values, "max-height") is { } maxHeight) control.MaxHeight = maxHeight;
        if (TryNumber(values, "width") is { } width) control.Width = width;
        if (TryNumber(values, "height") is { } height) control.Height = height;
        if (TryNumber(values, "opacity") is { } opacity) control.Opacity = Math.Clamp(opacity, 0, 1);
        if (TryNumber(values, "gap") is { } spacing && control is StackPanel stack) stack.Spacing = spacing;
        if (values.TryGetValue("background", out var background) && TryBrush(background.Text) is { } backgroundBrush)
        {
            if (control is Border border) border.Background = backgroundBrush;
            else if (control is Panel panel) panel.Background = backgroundBrush;
            else if (control is TemplatedControl backgroundControl) backgroundControl.Background = backgroundBrush;
        }
        if (values.TryGetValue("color", out var foreground) && TryBrush(foreground.Text) is { } foregroundBrush)
        {
            if (control is TextBlock textBlock) textBlock.Foreground = foregroundBrush;
            else if (control is TemplatedControl foregroundControl) foregroundControl.Foreground = foregroundBrush;
        }
        if (values.TryGetValue("border-color", out var borderColor) && TryBrush(borderColor.Text) is { } borderBrush)
        {
            if (control is Border border) border.BorderBrush = borderBrush;
            else if (control is TemplatedControl borderControl) borderControl.BorderBrush = borderBrush;
        }
        if (TryNumber(values, "border-width") is { } borderWidth)
        {
            if (control is Border border) border.BorderThickness = new Thickness(borderWidth);
            else if (control is TemplatedControl thicknessControl) thicknessControl.BorderThickness = new Thickness(borderWidth);
        }
        if (TryNumber(values, "corner-radius") is { } radius)
        {
            if (control is Border border) border.CornerRadius = new CornerRadius(radius);
            else if (control is TemplatedControl radiusControl) radiusControl.CornerRadius = new CornerRadius(radius);
        }
        if (control is TextBlock text)
        {
            if (TryNumber(values, "font-size") is { } fontSize) text.FontSize = fontSize;
            if (values.TryGetValue("text-align", out var alignment))
                text.TextAlignment = alignment.Text switch
                {
                    "center" => TextAlignment.Center,
                    "right" => TextAlignment.Right,
                    "justify" => TextAlignment.Justify,
                    _ => TextAlignment.Left,
                };
        }
        if (TryNumber(values, "padding") is { } padding && control is TemplatedControl paddingControl)
            paddingControl.Padding = new Thickness(padding);
    }

    private void ApplyFocusNeighbors()
    {
        foreach (var (control, neighbors) in pendingNeighbors)
        {
            Set(XYFocus.UpProperty, neighbors.Up);
            Set(XYFocus.DownProperty, neighbors.Down);
            Set(XYFocus.LeftProperty, neighbors.Left);
            Set(XYFocus.RightProperty, neighbors.Right);

            void Set(AttachedProperty<InputElement> property, string? targetId)
            {
                if (targetId is not null && focusableById.TryGetValue(targetId, out var target))
                    control.SetValue(property, target);
            }
        }
    }

    private Control RenderVirtualizedItem(ViewNode node)
    {
        var control = RenderNode(node);
        Avalonia.Threading.Dispatcher.UIThread.Post(ApplyFocusNeighbors, Avalonia.Threading.DispatcherPriority.Loaded);
        return control;
    }

    private async Task LoadArtworkAsync(Image image, WidgetPresentationAuthority authority, string handle)
    {
        try
        {
            var bytes = await artwork(authority, handle, default);
            await using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            var bitmap = new Bitmap(stream);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => image.Source = bitmap);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            AutomationProperties.SetHelpText(image, "Artwork unavailable");
        }
    }

    private bool IsVisible(ViewNode node) => node.VisibleWhen switch
    {
        ResponsiveVisibility.CompactOnly => compact,
        ResponsiveVisibility.ExpandedOnly => !compact,
        _ => true,
    };

    private static bool IsSafeClass(string value) => value.Length is > 0 and <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static double? TryNumber(
        IReadOnlyDictionary<string, BridgeComputedStyleValue> values,
        string name) => values.TryGetValue(name, out var value) && value.Number is { } number &&
        double.IsFinite(number) && number >= 0 ? number : null;

    private static IBrush? TryBrush(string text)
    {
        try { return Brush.Parse(text); }
        catch (FormatException) { return null; }
    }

    private static double IndicatorSize(LoadingIndicatorSize? size) => size switch
    {
        LoadingIndicatorSize.Compact => 18,
        LoadingIndicatorSize.Large => 48,
        _ => 28,
    };

    private static string Glyph(WidgetGlyph? glyph) => glyph switch
    {
        WidgetGlyph.Music => "♪",
        WidgetGlyph.Play => "▶",
        WidgetGlyph.Pause => "Ⅱ",
        WidgetGlyph.Previous => "◀|",
        WidgetGlyph.Next => "|▶",
        WidgetGlyph.Refresh => "↻",
        WidgetGlyph.Shuffle => "⇄",
        WidgetGlyph.Like => "♥",
        WidgetGlyph.Dislike => "♡",
        WidgetGlyph.Repeat => "↺",
        WidgetGlyph.RepeatOne => "↺¹",
        WidgetGlyph.Settings => "⚙",
        WidgetGlyph.Warning => "⚠",
        WidgetGlyph.Check => "✓",
        WidgetGlyph.Connection => "●",
        WidgetGlyph.Volume => "◕",
        WidgetGlyph.Muted => "○",
        WidgetGlyph.Microphone => "●",
        WidgetGlyph.Wifi => "⌁",
        WidgetGlyph.Ethernet => "↔",
        _ => "•",
    };

    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child)) yield return descendant;
    }
}
