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
        ApplyFocusNeighbors(context);
        ownedRenders.Add(root, context);
        return root;
    }

    public int GetRealizedControlCount(Control semanticRoot) =>
        ownedRenders.TryGetValue(semanticRoot, out var context) ? context.RealizedControlCount : 0;

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
        if (!IsVisible(node, context)) return new Border { IsVisible = false };

        Control control = node.Kind switch
        {
            ViewNodeKind.Stack => RenderStack(node, Orientation.Vertical, context),
            ViewNodeKind.Row => RenderStack(node, Orientation.Horizontal, context),
            ViewNodeKind.Scroll => RenderScroll(node, context),
            ViewNodeKind.Text => new TextBlock
            {
                Text = node.Text ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
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

        context.RealizedControlCount++;
        ApplyIdentity(control, node, context);
        ApplyStyle(control, node, context);
        if (node.IsFocusable)
        {
            context.FocusableById[node.Id] = control;
            if (node.Focus is not null) context.PendingNeighbors.Add((control, node.Focus));
        }

        return control;
    }

    private Control RenderStack(ViewNode node, Orientation orientation, RenderContext context)
    {
        Panel panel = orientation == Orientation.Vertical
            ? new StackPanel { Orientation = orientation, Spacing = 8 }
            : new WrapPanel { Orientation = orientation };
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
                Content = RenderStack(node, Orientation.Horizontal, context),
            };
        }

        return CreateVirtualizedList(node, context);
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
        var columns = Math.Clamp((int)Math.Floor(760 / minimum), 1, node.GridMaximumColumns ?? 8);
        var grid = new Grid();
        for (var column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var index = 0; index < node.Children.Count; index++)
        {
            if (index % columns == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var child = RenderNode(node.Children[index], context);
            Grid.SetColumn(child, index % columns);
            Grid.SetRow(child, index / columns);
            child.Margin = new Thickness(4);
            grid.Children.Add(child);
        }
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
            var control = RenderNode(node, context);
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

    private Control RenderVirtualizedItem(ViewNode node, RenderContext context)
    {
        var control = RenderNode(node, context);
        control.Height = 88;
        control.VerticalAlignment = VerticalAlignment.Stretch;
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
            VerticalAlignment = VerticalAlignment.Stretch,
            // Avalonia 12.1.1 does not expose the newer BufferFactor property; its supported
            // VirtualizingStackPanel default is the desired zero additional viewport buffer.
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
        };
        list.ItemTemplate = new FuncDataTemplate<ViewNode>(
            (item, _) => item is null ? null : RenderVirtualizedItem(item, context), supportsRecycling: true);
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
        public bool Compact { get; } = compact;
        public RenderResources Resources { get; } = new();
        public Dictionary<string, Control> FocusableById { get; } = new(StringComparer.Ordinal);
        public List<(Control Control, FocusNeighbors Neighbors)> PendingNeighbors { get; } = [];
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
