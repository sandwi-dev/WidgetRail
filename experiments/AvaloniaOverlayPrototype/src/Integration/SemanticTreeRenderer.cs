using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
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

public sealed class SemanticTreeRenderer : IDisposable
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
    private readonly Dictionary<Control, RenderContext> ownedRenders = [];
    private RenderContext? latest;
    private bool disposed;

    public SemanticTreeRenderer(
        Func<SemanticActionRequest, Task> dispatch,
        Func<WidgetPresentationAuthority, string, CancellationToken, Task<ReadOnlyMemory<byte>>> artwork,
        Func<ViewNode, Task<string?>> requestText)
    {
        this.dispatch = dispatch;
        this.artwork = artwork;
        this.requestText = requestText;
    }

    public int RealizedControlCount => latest?.RealizedControlCount ?? 0;
    public int OwnedArtworkBitmapCount => ownedRenders.Values.Sum(context => context.Resources.OwnedBitmapCount);
    public long OwnedDecodedArtworkBytes => ownedRenders.Values.Sum(context => context.Resources.OwnedDecodedBitmapBytes);
    public int PendingArtworkRequestCount => ownedRenders.Values.Sum(context => context.Resources.PendingArtworkRequestCount);
    public int TrackedRenderCount => ownedRenders.Count;

    public Control Render(WidgetPresentationFrame currentFrame, bool isCompact)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var context = new RenderContext(currentFrame, isCompact);
        latest = context;
        var root = currentFrame.Snapshot.AdvancedPresentation is { } advanced
            ? RenderAdvanced(currentFrame.Snapshot.Root, advanced, context)
            : RenderNode(currentFrame.Snapshot.Root, context);
        PrepareSemanticRootForHost(root, currentFrame.Snapshot.AdvancedPresentation is not null);
        ApplyFocusNeighbors(context);
        ownedRenders.Add(root, context);
        return root;
    }

    public int GetRealizedControlCount(Control semanticRoot) =>
        ownedRenders.TryGetValue(semanticRoot, out var context) ? context.RealizedControlCount : 0;

    public bool SetCompact(Control semanticRoot, bool compact)
    {
        if (!ownedRenders.TryGetValue(semanticRoot, out var context) || context.Compact == compact)
            return false;
        context.Compact = compact;
        foreach (var (control, visibility) in context.ResponsiveControls)
            control.IsVisible = visibility switch
            {
                ResponsiveVisibility.CompactOnly => compact,
                ResponsiveVisibility.ExpandedOnly => !compact,
                _ => true,
            };
        foreach (var update in context.ResponsiveLayoutUpdates) update(compact);
        return true;
    }

    public void Release(Control semanticRoot)
    {
        if (!ownedRenders.Remove(semanticRoot, out var context)) return;
        context.Resources.Dispose();
        if (ReferenceEquals(latest, context)) latest = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var context in ownedRenders.Values.Distinct()) context.Resources.Dispose();
        ownedRenders.Clear();
        latest = null;
    }

    private Control RenderNode(ViewNode node, RenderContext context)
    {
        Control control = node.Kind switch
        {
            ViewNodeKind.Stack => RenderStack(node, Orientation.Vertical, context),
            ViewNodeKind.Row => RenderStack(node, Orientation.Horizontal, context),
            ViewNodeKind.Scroll => RenderScroll(node, context),
            ViewNodeKind.Text => new TextBlock
            {
                Text = node.Text ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
            },
            ViewNodeKind.Button => RenderButton(node),
            ViewNodeKind.Progress => new ProgressBar
            {
                Minimum = node.Minimum ?? 0,
                Maximum = node.Maximum ?? 100,
                Value = node.Value ?? 0,
                IsIndeterminate = node.Value is null,
                MinHeight = 8,
                IsHitTestVisible = false,
            },
            ViewNodeKind.Slider => RenderSlider(node),
            ViewNodeKind.Spacer => new Border { MinHeight = 8, MinWidth = 8, IsHitTestVisible = false },
            ViewNodeKind.Image => RenderImage(node, context),
            ViewNodeKind.Icon => new TextBlock
            {
                Text = Glyph(node.Glyph),
                FontSize = 22,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            },
            ViewNodeKind.LoadingIndicator => new ProgressBar
            {
                IsIndeterminate = true,
                Width = IndicatorSize(node.IndicatorSize),
                Height = IndicatorSize(node.IndicatorSize),
                IsHitTestVisible = false,
            },
            ViewNodeKind.ActionSurface => RenderActionSurface(node, context),
            ViewNodeKind.Grid => RenderGrid(node, context),
            ViewNodeKind.TextEntry => RenderTextEntry(node),
            _ => throw new ArgumentOutOfRangeException(nameof(node.Kind)),
        };

        if (node.VisibleWhen is { } visibility and not ResponsiveVisibility.Always)
        {
            control.IsVisible = IsVisible(node, context);
            context.ResponsiveControls.Add((control, visibility));
        }

        context.RealizedControlCount++;
        ApplyIdentity(control, node, context);
        ApplySemanticThemeClass(control, node.Kind);
        ApplyStyle(control, node, context);
        EnforceInteractiveMinimum(control, node.Kind);
        if (node.IsFocusable)
        {
            context.FocusableById[node.Id] = control;
            if (node.Focus is not null) context.PendingNeighbors.Add((control, node.Focus));
        }

        return control;
    }

    private static void EnforceInteractiveMinimum(Control control, ViewNodeKind kind)
    {
        if (kind is not ViewNodeKind.Button and not ViewNodeKind.Slider and
            not ViewNodeKind.ActionSurface and not ViewNodeKind.TextEntry)
            return;
        control.MinWidth = Math.Max(44, control.MinWidth);
        control.MinHeight = Math.Max(36, control.MinHeight);
    }

    private Control RenderStack(ViewNode node, Orientation orientation, RenderContext context)
    {
        Panel panel = orientation == Orientation.Vertical
            ? new StackPanel
            {
                Orientation = orientation,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            }
            : new WrapPanel
            {
                Orientation = orientation,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
        foreach (var child in node.Children)
        {
            var rendered = RenderNode(child, context);
            rendered.Margin = orientation == Orientation.Horizontal
                ? new Thickness(0, 0, 8, 8)
                : new Thickness(0, 0, 0, 8);
            panel.Children.Add(rendered);
        }

        return panel;
    }

    private Control RenderScroll(ViewNode node, RenderContext context)
    {
        if (node.ScrollAxis == ScrollAxis.Horizontal)
        {
            return new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = RenderStack(node, Orientation.Horizontal, context),
            };
        }

        return CreateVirtualizedList(node, context);
    }

    private Button RenderButton(ViewNode node)
    {
        var button = new Button
        {
            Content = node.Glyph is { } glyph
                ? new TextBlock
                {
                    Text = Glyph(glyph),
                    FontSize = 20,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                }
                : new TextBlock
                {
                    Text = node.Text ?? node.AccessibilityLabel ?? "Action",
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                },
            IsEnabled = node.IsDisabled is not true,
            MinWidth = 44,
            MinHeight = 44,
            HorizontalContentAlignment = node.Glyph is null
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
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
            HorizontalAlignment = HorizontalAlignment.Stretch,
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

    private Control RenderImage(ViewNode node, RenderContext context)
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
            IsHitTestVisible = false,
        };
        if (node.ImageSource is { Length: > 0 } source)
        {
            try
            {
                const string dataPrefix = "data:image/png;base64,";
                if (source.StartsWith(dataPrefix, StringComparison.Ordinal))
                {
                    using var stream = new MemoryStream(Convert.FromBase64String(source[dataPrefix.Length..]));
                    var bitmap = new Bitmap(stream);
                    if (context.Resources.TryOwn(bitmap)) image.Source = bitmap;
                    else bitmap.Dispose();
                }
                else if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
                {
                    var bitmap = new Bitmap(uri.ToString());
                    if (context.Resources.TryOwn(bitmap)) image.Source = bitmap;
                    else bitmap.Dispose();
                }
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or IOException)
            {
                AutomationProperties.SetHelpText(image, "Image unavailable");
            }
        }
        else if (node.ArtworkHandle is { Length: > 0 } handle)
        {
            _ = LoadArtworkAsync(image, context.Frame.Authority, handle, context.Resources);
        }
        return image;
    }

    private Button RenderActionSurface(ViewNode node, RenderContext context)
    {
        var content = RenderStack(node, node.ActionSurfaceOrientation == ActionSurfaceOrientation.Horizontal
            ? Orientation.Horizontal
            : Orientation.Vertical, context);
        var button = new Button
        {
            Content = content,
            IsEnabled = node.IsDisabled is not true,
            MinHeight = 44,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += async (_, _) =>
        {
            if (node.ActionId is { Length: > 0 } actionId)
                await dispatch(new SemanticActionRequest(node.Id, actionId));
        };
        return button;
    }

    private Control RenderGrid(ViewNode node, RenderContext context)
    {
        if (node.Children.Count > 64)
            return CreateVirtualizedList(node, context);

        var minimum = Math.Max(96, node.GridMinimumColumnWidth ?? 180);
        var maximumColumns = Math.Max(1, node.GridMaximumColumns ?? 8);
        var grid = new UniformGrid
        {
            Columns = 1,
            Rows = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
        };
        foreach (var childNode in node.Children)
        {
            var child = RenderNode(childNode, context);
            child.Margin = new Thickness(4);
            grid.Children.Add(child);
        }
        grid.SizeChanged += (_, args) =>
        {
            var availableWidth = Math.Max(0, args.NewSize.Width);
            var columns = Math.Clamp((int)Math.Floor((availableWidth + 8) / (minimum + 8)), 1, maximumColumns);
            if (grid.Columns != columns) grid.Columns = columns;
        };
        return grid;
    }

    private Control RenderAdvanced(ViewNode root, WidgetAdvancedPresentationView advanced, RenderContext context)
    {
        var slots = Flatten(root)
            .Where(node => node.AdvancedPresentationSlot is not null)
            .ToDictionary(node => node.AdvancedPresentationSlot!.Value);
        if (slots.Count == 0) return RenderNode(root, context);

        var layout = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var renderedSlots = new Dictionary<WidgetAdvancedPresentationSlot, Control>();
        foreach (var (slot, node) in slots)
        {
            var control = RenderNode(node, context);
            control.Margin = new Thickness(6);
            renderedSlots.Add(slot, control);
            layout.Children.Add(control);
        }
        ApplyLayout(context.Compact);
        context.ResponsiveLayoutUpdates.Add(ApplyLayout);
        layout.SizeChanged += (_, args) => ApplyLayout(context.Compact || args.NewSize.Width <= 760);
        return layout;

        void ApplyLayout(bool stacked)
        {
            if (stacked)
            {
                layout.ColumnDefinitions = new ColumnDefinitions("*");
                layout.RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto");
                Place(WidgetAdvancedPresentationSlot.DetailsPanel, 0, 0);
                Place(WidgetAdvancedPresentationSlot.CollectionNavigation, 0, 1);
                Place(WidgetAdvancedPresentationSlot.PrimaryCollection, 0, 2);
                Place(WidgetAdvancedPresentationSlot.SourceStatus, 0, 3);
                Place(WidgetAdvancedPresentationSlot.OperationStatus, 0, 4);
                Place(WidgetAdvancedPresentationSlot.ControllerHints, 0, 5);
                return;
            }

            layout.ColumnDefinitions = advanced.Preset switch
            {
                WidgetAdvancedPresentationPreset.Carousel => new ColumnDefinitions("3*,2*"),
                WidgetAdvancedPresentationPreset.CoverWall or WidgetAdvancedPresentationPreset.CompactGrid =>
                    new ColumnDefinitions("2*,4*"),
                _ => new ColumnDefinitions("2*,3*"),
            };
            layout.RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto");
            Place(WidgetAdvancedPresentationSlot.DetailsPanel, 0, 0);
            Place(WidgetAdvancedPresentationSlot.CollectionNavigation, 0, 1);
            Place(WidgetAdvancedPresentationSlot.PrimaryCollection, 1, 0, rowSpan: 2);
            Place(WidgetAdvancedPresentationSlot.SourceStatus, 0, 2);
            Place(WidgetAdvancedPresentationSlot.OperationStatus, 1, 2);
            Place(WidgetAdvancedPresentationSlot.ControllerHints, 0, 3, columnSpan: 2);
        }

        void Place(WidgetAdvancedPresentationSlot slot, int column, int row, int columnSpan = 1, int rowSpan = 1)
        {
            if (!renderedSlots.TryGetValue(slot, out var control)) return;
            Grid.SetColumn(control, column);
            Grid.SetRow(control, row);
            Grid.SetColumnSpan(control, columnSpan);
            Grid.SetRowSpan(control, rowSpan);
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
            MinWidth = 44,
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

    private void ApplyIdentity(Control control, ViewNode node, RenderContext context)
    {
        control.SetValue(SemanticNodeIdProperty, node.Id);
        control.SetValue(FocusPersistenceIdProperty, node.FocusPersistenceId ?? node.Id);
        control.SetValue(CollectionItemKeyProperty, node.CollectionItemKey);
        AutomationProperties.SetAutomationId(control,
            SemanticAutomationIdentity.ForWidgetNode(context.Frame.Authority.WidgetId, node));
        AutomationProperties.SetName(control,
            node.AccessibilityLabel ?? node.Text ?? node.Kind.ToString());
        if (!string.IsNullOrWhiteSpace(node.AccessibilityValue))
            AutomationProperties.SetHelpText(control, node.AccessibilityValue);
    }

    private void ApplyStyle(Control control, ViewNode node, RenderContext context)
    {
        foreach (var styleClass in node.StyleClasses.Where(IsSafeClass)) control.Classes.Add(styleClass);
        if (!context.Frame.RenderStyles.TryGetValue(node.Id, out var styles)) return;
        ApplyComputedStyle(control, styles.Base);
        control.GotFocus += (_, _) => ApplyComputedStyle(control, styles.Focused);
        control.LostFocus += (_, _) => ApplyComputedStyle(control, styles.Base);
        control.PointerPressed += (_, _) => ApplyComputedStyle(control, styles.Pressed);
        control.PointerReleased += (_, _) => ApplyComputedStyle(
            control, control.IsFocused ? styles.Focused : styles.Base);
    }

    private static void ApplySemanticThemeClass(Control control, ViewNodeKind kind)
    {
        control.Classes.Add("semantic");
        control.Classes.Add(kind switch
        {
            ViewNodeKind.Stack => "semantic-stack",
            ViewNodeKind.Row => "semantic-row",
            ViewNodeKind.Scroll => "semantic-scroll",
            ViewNodeKind.Text => "semantic-text",
            ViewNodeKind.Button => "semantic-button",
            ViewNodeKind.Progress => "semantic-progress",
            ViewNodeKind.Slider => "semantic-slider",
            ViewNodeKind.Spacer => "semantic-spacer",
            ViewNodeKind.Image => "semantic-image",
            ViewNodeKind.Icon => "semantic-icon",
            ViewNodeKind.LoadingIndicator => "semantic-loading",
            ViewNodeKind.ActionSurface => "semantic-action-surface",
            ViewNodeKind.Grid => "semantic-grid",
            ViewNodeKind.TextEntry => "semantic-text-entry",
            _ => "semantic-control",
        });

        if (kind is not ViewNodeKind.Icon and not ViewNodeKind.LoadingIndicator and not ViewNodeKind.Spacer)
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    private static void ApplyComputedStyle(
        Control control,
        IReadOnlyDictionary<string, BridgeComputedStyleValue> values)
    {
        ApplyLength(control, values, "min-width");
        ApplyLength(control, values, "min-height");
        ApplyLength(control, values, "max-width");
        ApplyLength(control, values, "max-height");
        ApplyLength(control, values, "width");
        ApplyLength(control, values, "height");
        if (TryNumber(values, "opacity") is { } opacity) control.Opacity = Math.Clamp(opacity, 0, 1);
        if (TryNumber(values, "gap") is { } spacing && control is StackPanel stack) stack.Spacing = spacing;
        if (values.TryGetValue("background", out var background) && TryBrush(background.Text) is { } backgroundBrush)
        {
            if (control is Border border) border.Background = backgroundBrush;
            else if (control is Panel panel) panel.Background = backgroundBrush;
            else if (control is Button && backgroundBrush is ISolidColorBrush { Color.A: 0 })
                control.ClearValue(TemplatedControl.BackgroundProperty);
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
            else if (control is Button && borderBrush is ISolidColorBrush { Color.A: 0 })
                control.ClearValue(TemplatedControl.BorderBrushProperty);
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
            if (TryNumber(values, "line-height") is { } lineHeight)
                text.LineHeight = lineHeight <= 4 ? text.FontSize * lineHeight : lineHeight;
            if (TryNumber(values, "max-lines") is { } maxLines)
            {
                text.MaxLines = Math.Max(1, (int)Math.Floor(maxLines));
                if (text.MaxLines == 1) text.TextWrapping = TextWrapping.NoWrap;
            }
            if (values.TryGetValue("text-overflow", out var overflow) &&
                string.Equals(overflow.Text, "ellipsis", StringComparison.OrdinalIgnoreCase))
                text.TextTrimming = TextTrimming.CharacterEllipsis;
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

    internal static void PrepareSemanticRootForHost(Control root, bool? fillViewport = null)
    {
        // Widget style still owns internal presentation, but the Avalonia page host owns the admitted
        // root's outer viewport. Carrying legacy renderer max-width or viewport-unit constraints into
        // this boundary recreates the narrow centered columns seen in the physical prototype.
        root.Width = double.NaN;
        root.MinWidth = 0;
        root.MaxWidth = double.PositiveInfinity;
        root.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (fillViewport is not null)
            root.VerticalAlignment = fillViewport.Value ? VerticalAlignment.Stretch : VerticalAlignment.Top;
    }

    private static void ApplyLength(
        Control control,
        IReadOnlyDictionary<string, BridgeComputedStyleValue> values,
        string property)
    {
        if (!values.TryGetValue(property, out var value) || value.Number is not { } number ||
            !double.IsFinite(number) || number < 0)
            return;

        var relative = value.Unit is "%" or "vw" or "vh";
        switch (property)
        {
            case "min-width": control.MinWidth = relative ? 0 : number; break;
            case "min-height": control.MinHeight = relative ? 0 : number; break;
            case "max-width": control.MaxWidth = relative ? double.PositiveInfinity : number; break;
            case "max-height": control.MaxHeight = relative ? double.PositiveInfinity : number; break;
            case "width":
                control.Width = relative ? double.NaN : number;
                if (relative) control.HorizontalAlignment = HorizontalAlignment.Stretch;
                break;
            case "height":
                control.Height = relative ? double.NaN : number;
                if (relative) control.VerticalAlignment = VerticalAlignment.Stretch;
                break;
        }
    }

    private static void ApplyFocusNeighbors(RenderContext context)
    {
        foreach (var (control, neighbors) in context.PendingNeighbors)
        {
            Set(XYFocus.UpProperty, neighbors.Up);
            Set(XYFocus.DownProperty, neighbors.Down);
            Set(XYFocus.LeftProperty, neighbors.Left);
            Set(XYFocus.RightProperty, neighbors.Right);

            void Set(AttachedProperty<InputElement> property, string? targetId)
            {
                if (targetId is not null && context.FocusableById.TryGetValue(targetId, out var target))
                    control.SetValue(property, target);
            }
        }
    }

    private Control RenderVirtualizedItem(ViewNode node, RenderContext context, bool uniformHeight)
    {
        var control = RenderNode(node, context);
        if (uniformHeight)
        {
            control.Height = 88;
            control.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            control.Height = double.NaN;
            control.MinHeight = Math.Max(44, control.MinHeight);
            control.VerticalAlignment = VerticalAlignment.Top;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => ApplyFocusNeighbors(context), Avalonia.Threading.DispatcherPriority.Loaded);
        return control;
    }

    private ListBox CreateVirtualizedList(ViewNode node, RenderContext context)
    {
        var list = new ListBox
        {
            ItemsSource = node.Children,
            SelectionMode = SelectionMode.Single,
            MinHeight = 80,
            MaxHeight = 540,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            // Avalonia 12.1.1 does not expose the newer BufferFactor property; its supported
            // VirtualizingStackPanel default is the desired zero additional viewport buffer.
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
        };
        var uniformHeight = node.Children.Count > 64;
        list.ItemTemplate = new FuncDataTemplate<ViewNode>(
            (item, _) => item is null ? null : RenderVirtualizedItem(item, context, uniformHeight), supportsRecycling: true);
        return list;
    }

    private async Task LoadArtworkAsync(
        Image image,
        WidgetPresentationAuthority authority,
        string handle,
        RenderResources resources)
    {
        resources.BeginArtworkRequest();
        try
        {
            var bytes = await artwork(authority, handle, resources.Token).ConfigureAwait(false);
            resources.Token.ThrowIfCancellationRequested();
            await using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            var bitmap = new Bitmap(stream);
            if (!resources.TryOwn(bitmap))
            {
                bitmap.Dispose();
                return;
            }
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    resources.Token.ThrowIfCancellationRequested();
                    image.Source = bitmap;
                },
                Avalonia.Threading.DispatcherPriority.Normal,
                resources.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            if (!resources.Token.IsCancellationRequested)
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                    () => AutomationProperties.SetHelpText(image, "Artwork unavailable"));
        }
        catch (OperationCanceledException) when (resources.Token.IsCancellationRequested) { }
        finally { resources.EndArtworkRequest(); }
    }

    private static bool IsVisible(ViewNode node, RenderContext context) => node.VisibleWhen switch
    {
        ResponsiveVisibility.CompactOnly => context.Compact,
        ResponsiveVisibility.ExpandedOnly => !context.Compact,
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

    private sealed class RenderContext(WidgetPresentationFrame frame, bool compact)
    {
        public WidgetPresentationFrame Frame { get; } = frame;
        public bool Compact { get; set; } = compact;
        public RenderResources Resources { get; } = new();
        public Dictionary<string, Control> FocusableById { get; } = new(StringComparer.Ordinal);
        public List<(Control Control, FocusNeighbors Neighbors)> PendingNeighbors { get; } = [];
        public List<(Control Control, ResponsiveVisibility Visibility)> ResponsiveControls { get; } = [];
        public List<Action<bool>> ResponsiveLayoutUpdates { get; } = [];
        public int RealizedControlCount { get; set; }
    }

    private sealed class RenderResources : IDisposable
    {
        private readonly object gate = new();
        private readonly CancellationTokenSource lifetime = new();
        private readonly List<Bitmap> bitmaps = [];
        private long decodedBitmapBytes;
        private int pendingArtworkRequests;
        private bool disposed;

        public CancellationToken Token => lifetime.Token;
        public int PendingArtworkRequestCount => Volatile.Read(ref pendingArtworkRequests);
        public int OwnedBitmapCount
        {
            get { lock (gate) return bitmaps.Count; }
        }
        public long OwnedDecodedBitmapBytes => Interlocked.Read(ref decodedBitmapBytes);

        public void BeginArtworkRequest() => Interlocked.Increment(ref pendingArtworkRequests);
        public void EndArtworkRequest() => Interlocked.Decrement(ref pendingArtworkRequests);

        public bool TryOwn(Bitmap bitmap)
        {
            lock (gate)
            {
                if (disposed || lifetime.IsCancellationRequested) return false;
                bitmaps.Add(bitmap);
                decodedBitmapBytes += checked((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4);
                return true;
            }
        }

        public void Dispose()
        {
            Bitmap[] owned;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                lifetime.Cancel();
                owned = bitmaps.ToArray();
                bitmaps.Clear();
                decodedBitmapBytes = 0;
            }
            foreach (var bitmap in owned) bitmap.Dispose();
            lifetime.Dispose();
        }
    }

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
