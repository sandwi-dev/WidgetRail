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
            : RenderNode(currentFrame.Snapshot.Root, context, isRoot: true);
        PrepareSemanticRootForHost(root, currentFrame.Snapshot.AdvancedPresentation is not null);
        ApplyFocusNeighbors(context);
        ownedRenders.Add(root, context);
        return root;
    }

    public int GetRealizedControlCount(Control semanticRoot) =>
        ownedRenders.TryGetValue(semanticRoot, out var context) ? context.RealizedControlCount : 0;

    public bool OwnsVerticalScroll(Control semanticRoot) =>
        ownedRenders.TryGetValue(semanticRoot, out var context) && context.OwnsVerticalScroll;

    public bool OwnsHorizontalScroll(Control semanticRoot) =>
        ownedRenders.TryGetValue(semanticRoot, out var context) && context.OwnsHorizontalScroll;

    public bool SetCompact(Control semanticRoot, bool compact)
    {
        if (!ownedRenders.TryGetValue(semanticRoot, out var context) || context.Compact == compact)
            return false;
        context.Compact = compact;
        foreach (var (control, node) in context.ResponsiveControls)
            control.IsVisible = IsVisible(node, context);
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

    private Control RenderNode(ViewNode node, RenderContext context, bool isRoot = false)
    {
        Control control = node.Kind switch
        {
            ViewNodeKind.Stack => RenderStack(node, Orientation.Vertical, context, isRoot),
            ViewNodeKind.Row => RenderStack(node, Orientation.Horizontal, context, isRoot),
            ViewNodeKind.Scroll => RenderScroll(node, context),
            ViewNodeKind.Text => new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(node.Text)
                    ? node.AccessibilityLabel ?? string.Empty
                    : node.Text,
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
            ViewNodeKind.LoadingIndicator => RenderLoading(node),
            ViewNodeKind.ActionSurface => RenderActionSurface(node, context),
            ViewNodeKind.Grid => RenderGrid(node, context),
            ViewNodeKind.TextEntry => RenderTextEntry(node),
            _ => throw new ArgumentOutOfRangeException(nameof(node.Kind)),
        };

        if (node.VisibleWhen is not null || context.InheritedVisibilityById.ContainsKey(node.Id))
        {
            control.IsVisible = IsVisible(node, context);
            context.ResponsiveControls.Add((control, node));
        }

        context.RealizedControlCount++;
        ApplyIdentity(control, node, context);
        ApplySemanticThemeClass(control, node.Kind);
        ControllerComponentCompiler.Apply(control, ControllerComponentCompiler.Compile(node, isRoot));
        ApplySemanticState(control, node);
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

    private Control RenderStack(
        ViewNode node,
        Orientation orientation,
        RenderContext context,
        bool isRoot = false)
    {
        if (isRoot && orientation == Orientation.Vertical &&
            node.Children.Any(IsDirectVerticalScroll))
        {
            context.OwnsVerticalScroll = true;
            var flattened = new List<ViewNode>();
            foreach (var child in node.Children)
            {
                if (!IsDirectVerticalScroll(child))
                {
                    flattened.Add(child);
                    continue;
                }

                // The root ListBox is the sole vertical scrolling owner. Keep the typed Scroll
                // node as a bounded semantic/UIA section marker, then hoist its children into
                // that owner while inheriting the section's responsive visibility.
                context.HoistedVerticalScrollIds.Add(child.Id);
                flattened.Add(child);
                foreach (var item in child.Children)
                {
                    context.InheritedVisibilityById[item.Id] = child.VisibleWhen;
                    flattened.Add(item);
                }
            }
            return CreateVirtualizedList(node with { Children = flattened }, context);
        }

        var children = node.Children.Select(child => RenderNode(child, context)).ToArray();
        Panel panel;
        if (orientation == Orientation.Vertical && node.Children.Any(OwnsVerticalAxis))
        {
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            for (var index = 0; index < children.Length; index++)
            {
                var ownsAxis = OwnsVerticalAxis(node.Children[index]);
                grid.RowDefinitions.Add(new RowDefinition(ownsAxis ? GridLength.Star : GridLength.Auto));
                Grid.SetRow(children[index], index);
                children[index].Margin = index == children.Length - 1
                    ? default
                    : new Thickness(0, 0, 0, 12);
                grid.Children.Add(children[index]);
            }
            panel = grid;
        }
        else if (orientation == Orientation.Vertical)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Spacing = 12,
            };
            foreach (var child in children) stack.Children.Add(child);
            panel = stack;
        }
        else
        {
            var row = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            foreach (var child in children)
            {
                child.Margin = new Thickness(0, 0, 10, 10);
                row.Children.Add(child);
            }
            panel = row;
        }

        if (isRoot) return panel;
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Child = panel,
        };
    }

    private Control RenderScroll(ViewNode node, RenderContext context)
    {
        if (context.HoistedVerticalScrollIds.Contains(node.Id))
        {
            return new TextBlock
            {
                Text = string.Empty,
                MinHeight = 1,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
        }

        if (node.ScrollAxis == ScrollAxis.Horizontal)
        {
            context.OwnsHorizontalScroll = true;
            return new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = BuildChildrenPanel(node.Children, Orientation.Horizontal, context),
            };
        }

        context.OwnsVerticalScroll = true;
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

    private static Control RenderLoading(ViewNode node)
    {
        var indicator = new ProgressBar
        {
            IsIndeterminate = true,
            Width = IndicatorSize(node.IndicatorSize),
            Height = IndicatorSize(node.IndicatorSize),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Child = indicator,
        };
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
        var content = BuildChildrenPanel(node.Children,
            node.ActionSurfaceOrientation == ActionSurfaceOrientation.Horizontal
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
        {
            context.OwnsVerticalScroll = true;
            return CreateVirtualizedList(node, context);
        }

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
        ControllerComponentCompiler.Apply(layout,
            new ControllerComponent(ControllerComponentKind.AdvancedSlotSurface,
                ControllerNavigationZone.Page));
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

    private static void ApplySemanticState(Control control, ViewNode node)
    {
        control.Classes.Set("selected", node.IsSelected is true);
        control.Classes.Set("busy", node.IsBusy is true);
        control.Classes.Set("disabled", node.IsDisabled is true);
    }

    private Panel BuildChildrenPanel(
        IReadOnlyList<ViewNode> nodes,
        Orientation orientation,
        RenderContext context)
    {
        if (orientation == Orientation.Vertical)
        {
            if (nodes.Any(OwnsVerticalAxis))
            {
                var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
                for (var index = 0; index < nodes.Count; index++)
                {
                    var ownsAxis = OwnsVerticalAxis(nodes[index]);
                    grid.RowDefinitions.Add(new RowDefinition(ownsAxis ? GridLength.Star : GridLength.Auto));
                    var rendered = RenderNode(nodes[index], context);
                    Grid.SetRow(rendered, index);
                    rendered.Margin = index == nodes.Count - 1 ? default : new Thickness(0, 0, 0, 10);
                    grid.Children.Add(rendered);
                }
                return grid;
            }
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            foreach (var child in nodes) stack.Children.Add(RenderNode(child, context));
            return stack;
        }

        var row = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var child in nodes)
        {
            var rendered = RenderNode(child, context);
            rendered.Margin = new Thickness(0, 0, 10, 10);
            row.Children.Add(rendered);
        }
        return row;
    }

    private static bool OwnsVerticalAxis(ViewNode node) =>
        node.Kind == ViewNodeKind.Scroll && node.ScrollAxis is not ScrollAxis.Horizontal ||
        node.Kind == ViewNodeKind.Grid && node.Children.Count > 64 ||
        node.Children.Any(OwnsVerticalAxis);

    private static bool IsDirectVerticalScroll(ViewNode node) =>
        node.Kind == ViewNodeKind.Scroll && node.ScrollAxis is not ScrollAxis.Horizontal;

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

    internal static void PrepareSemanticRootForHost(Control root, bool? fillViewport = null)
    {
        // The typed component compiler owns presentation and the Avalonia page host owns the admitted
        // root's outer viewport. Carrying legacy max-width or viewport-unit constraints into this
        // boundary recreates the narrow centered columns seen in the physical prototype.
        root.Width = double.NaN;
        root.MinWidth = 0;
        root.MaxWidth = double.PositiveInfinity;
        root.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (fillViewport is not null)
            root.VerticalAlignment = fillViewport.Value ? VerticalAlignment.Stretch : VerticalAlignment.Top;
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
        if (context.HoistedVerticalScrollIds.Contains(node.Id))
        {
            control.Height = double.NaN;
            control.MinHeight = 1;
            control.VerticalAlignment = VerticalAlignment.Top;
        }
        else if (uniformHeight)
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

    private static bool IsVisible(ViewNode node, RenderContext context) =>
        MatchesVisibility(node.VisibleWhen, context.Compact) &&
        (!context.InheritedVisibilityById.TryGetValue(node.Id, out var inherited) ||
         MatchesVisibility(inherited, context.Compact));

    private static bool MatchesVisibility(ResponsiveVisibility? visibility, bool compact) => visibility switch
    {
        ResponsiveVisibility.CompactOnly => compact,
        ResponsiveVisibility.ExpandedOnly => !compact,
        _ => true,
    };

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
        public List<(Control Control, ViewNode Node)> ResponsiveControls { get; } = [];
        public List<Action<bool>> ResponsiveLayoutUpdates { get; } = [];
        public HashSet<string> HoistedVerticalScrollIds { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ResponsiveVisibility?> InheritedVisibilityById { get; } = new(StringComparer.Ordinal);
        public bool OwnsVerticalScroll { get; set; }
        public bool OwnsHorizontalScroll { get; set; }
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
