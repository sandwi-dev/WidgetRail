using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using GameBarAlternative.AvaloniaPrototype.Navigation;
using GameBarAlternative.AvaloniaPrototype.Views;

namespace GameBarAlternative.AvaloniaPrototype.Diagnostics;

public sealed record DiagnosticRect(double X, double Y, double Width, double Height)
{
    public bool HasArea => Width > 0 && Height > 0;

    public bool IsContainedBy(DiagnosticRect outer, double tolerance = 0.5) =>
        HasArea &&
        X >= outer.X - tolerance &&
        Y >= outer.Y - tolerance &&
        X + Width <= outer.X + outer.Width + tolerance &&
        Y + Height <= outer.Y + outer.Height + tolerance;

    public DiagnosticRect? Intersect(DiagnosticRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(X + Width, other.X + other.Width);
        var bottom = Math.Min(Y + Height, other.Y + other.Height);
        return right > left && bottom > top ? new DiagnosticRect(left, top, right - left, bottom - top) : null;
    }
}

public sealed record ElementBoundsSnapshot(
    string AutomationId,
    string ControlType,
    DiagnosticRect LayoutBounds,
    DiagnosticRect? VisibleBounds,
    bool ClippedByScrollViewport)
{
    public bool IsValid(DiagnosticRect client) =>
        LayoutBounds.HasArea &&
        (ClippedByScrollViewport || VisibleBounds is { } visible && visible.IsContainedBy(client));
}

public sealed record TransitionDiagnosticSample(
    PrototypeRoute Route,
    TransitionPhase Phase,
    DateTimeOffset CapturedAtUtc,
    int VisualChildCount,
    int NonTransparentChildCount,
    bool TransparentRoot,
    bool OpaqueBlackBrushAbsent,
    bool AvaloniaSurfaceCoveragePresent);

public sealed record FrameSnapshot(
    PrototypeRoute Route,
    DateTimeOffset CompletedAtUtc,
    double TransitionMilliseconds,
    bool TransparentRoot,
    bool OpaqueBlackFallbackAbsent,
    DiagnosticRect Client,
    DiagnosticRect Content,
    DiagnosticRect Tray,
    IReadOnlyDictionary<string, DiagnosticRect> RequiredElements,
    IReadOnlyList<ElementBoundsSnapshot> VisibleRequiredElements)
{
    public bool RequiredElementsContained => RequiredElements.Values.All(rect => rect.IsContainedBy(Client));

    public bool AllVisibleRequiredElementsValid =>
        VisibleRequiredElements.Count > 0 && VisibleRequiredElements.All(element => element.IsValid(Client));
}

public static class FrameDiagnostics
{
    private static readonly object Gate = new();
    private static readonly List<FrameSnapshot> Frames = [];
    private static readonly List<TransitionDiagnosticSample> Transitions = [];

    public static event EventHandler<FrameSnapshot>? FrameCompleted;

    public static IReadOnlyList<FrameSnapshot> RecordedFrames
    {
        get { lock (Gate) return Frames.ToArray(); }
    }

    public static IReadOnlyList<TransitionDiagnosticSample> RecordedTransitions
    {
        get { lock (Gate) return Transitions.ToArray(); }
    }

    public static FrameSnapshot Record(PrototypeShellView shell, PrototypeRoute route, TimeSpan transitionDuration)
    {
        var snapshot = Capture(shell, route, transitionDuration);
        lock (Gate) Frames.Add(snapshot);
        FrameCompleted?.Invoke(null, snapshot);
        return snapshot;
    }

    public static void RecordTransition(PrototypeShellView shell, PrototypeRoute route, TransitionPhase phase)
    {
        var presenter = shell.TransitionPresenterControl;
        var children = presenter.Children.OfType<Control>().ToArray();
        var sample = new TransitionDiagnosticSample(
            route,
            phase,
            DateTimeOffset.UtcNow,
            children.Length,
            children.Count(child => child.IsVisible && child.Opacity > 0),
            IsTransparent(shell.Background),
            !EnumerateSurfaceBrushes(shell).Any(IsOpaqueBlack),
            !IsTransparent(shell.ContentRegionControl.Background) || children.Any(child => child.IsVisible && child.Opacity > 0));
        lock (Gate) Transitions.Add(sample);
    }

    public static FrameSnapshot Capture(PrototypeShellView shell, PrototypeRoute route, TimeSpan transitionDuration)
    {
        var required = new Dictionary<string, DiagnosticRect>(StringComparer.Ordinal)
        {
            ["content"] = RelativeBounds(shell.ContentRegionControl, shell),
            ["tray"] = RelativeBounds(shell.TrayRegionControl, shell),
        };
        var authored = new List<Control>();
        authored.AddRange(shell.TrayButtons);

        if (shell.TransitionPresenterControl.AdmittedPage is { } page)
        {
            AddNamed(page, shell, required, "PageTitle", "title");
            AddNamed(page, shell, required, "PrimaryContent", "primaryContent");
            AddNamed(page, shell, required, "SettingsScroll", "primaryContent");
            AddNamed(page, shell, required, "ApplicationScroll", "primaryContent");
            AddNamed(page, shell, required, "PrimaryAction", "primaryAction");
            AddNamed(page, shell, required, "ControllerHelp", "controllerHelp");
            authored.AddRange(page.GetLogicalDescendants().OfType<Control>()
                .Where(control => control is Button or TextBlock)
                .Where(control => !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(control))));
        }

        var client = new DiagnosticRect(0, 0, shell.Bounds.Width, shell.Bounds.Height);
        var elements = authored
            .Where(control => control.IsVisible)
            .Select(control => CaptureElement(control, shell, client))
            .ToArray();

        return new FrameSnapshot(
            route,
            DateTimeOffset.UtcNow,
            transitionDuration.TotalMilliseconds,
            IsTransparent(shell.Background),
            !EnumerateSurfaceBrushes(shell).Any(IsOpaqueBlack),
            client,
            RelativeBounds(shell.ContentRegionControl, shell),
            RelativeBounds(shell.TrayRegionControl, shell),
            required,
            elements);
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Frames.Clear();
            Transitions.Clear();
        }
    }

    private static ElementBoundsSnapshot CaptureElement(Control control, PrototypeShellView shell, DiagnosticRect client)
    {
        var id = AutomationProperties.GetAutomationId(control) ??
            throw new InvalidOperationException("Required authored control has no AutomationId.");

        var layout = RelativeBounds(control, shell);
        DiagnosticRect? visible = layout.Intersect(client);
        var clippedByScroll = false;
        foreach (var viewport in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var intersection = visible?.Intersect(RelativeBounds(viewport, shell));
            if (intersection is null) clippedByScroll = true;
            visible = intersection;
        }

        return new ElementBoundsSnapshot(id, control.GetType().Name, layout, visible, clippedByScroll);
    }

    private static IEnumerable<IBrush?> EnumerateSurfaceBrushes(PrototypeShellView shell)
    {
        yield return shell.Background;
        yield return shell.ContentRegionControl.Background;
        yield return shell.TransitionPresenterControl.Background;
        foreach (var border in shell.TransitionPresenterControl.GetVisualDescendants().OfType<Border>())
        {
            yield return border.Background;
        }
    }

    private static void AddNamed(Control page, Control root, IDictionary<string, DiagnosticRect> required, string name, string key)
    {
        var control = page.FindControl<Control>(name);
        if (control is not null) required[key] = RelativeBounds(control, root);
    }

    private static DiagnosticRect RelativeBounds(Control control, Visual root)
    {
        var origin = control.TranslatePoint(new Point(0, 0), root) ?? default;
        return new DiagnosticRect(origin.X, origin.Y, control.Bounds.Width, control.Bounds.Height);
    }

    private static bool IsTransparent(IBrush? brush) =>
        brush is null || brush is ISolidColorBrush solid && solid.Color.A == 0;

    private static bool IsOpaqueBlack(IBrush? brush) =>
        brush is ISolidColorBrush solid && solid.Color is { A: byte.MaxValue, R: 0, G: 0, B: 0 };
}
