using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GameBarAlternative.AvaloniaPrototype.Views;

namespace GameBarAlternative.AvaloniaPrototype.Diagnostics;

public sealed record DiagnosticRect(double X, double Y, double Width, double Height)
{
    public bool IsContainedBy(DiagnosticRect outer, double tolerance = 0.5) =>
        X >= outer.X - tolerance &&
        Y >= outer.Y - tolerance &&
        X + Width <= outer.X + outer.Width + tolerance &&
        Y + Height <= outer.Y + outer.Height + tolerance;
}

public sealed record FrameSnapshot(
    PrototypeRoute Route,
    DateTimeOffset CompletedAtUtc,
    double TransitionMilliseconds,
    bool TransparentRoot,
    bool OpaqueBlackFallbackAbsent,
    DiagnosticRect Client,
    DiagnosticRect Content,
    DiagnosticRect Tray,
    IReadOnlyDictionary<string, DiagnosticRect> RequiredElements)
{
    public bool RequiredElementsContained => RequiredElements.Values.All(rect => rect.IsContainedBy(Client));
}

public static class FrameDiagnostics
{
    private static readonly object Gate = new();
    private static readonly List<FrameSnapshot> Frames = [];

    public static event EventHandler<FrameSnapshot>? FrameCompleted;

    public static IReadOnlyList<FrameSnapshot> RecordedFrames
    {
        get
        {
            lock (Gate)
            {
                return Frames.ToArray();
            }
        }
    }

    public static FrameSnapshot Record(
        PrototypeShellView shell,
        PrototypeRoute route,
        TimeSpan transitionDuration)
    {
        var snapshot = Capture(shell, route, transitionDuration);
        lock (Gate)
        {
            Frames.Add(snapshot);
        }

        FrameCompleted?.Invoke(null, snapshot);
        return snapshot;
    }

    public static FrameSnapshot Capture(
        PrototypeShellView shell,
        PrototypeRoute route,
        TimeSpan transitionDuration)
    {
        var required = new Dictionary<string, DiagnosticRect>(StringComparer.Ordinal)
        {
            ["content"] = RelativeBounds(shell.ContentRegionControl, shell),
            ["tray"] = RelativeBounds(shell.TrayRegionControl, shell),
        };

        if (shell.TransitionPresenterControl.AdmittedPage is { } page)
        {
            AddNamed(page, shell, required, "PageTitle", "title");
            AddNamed(page, shell, required, "PrimaryContent", "primaryContent");
            AddNamed(page, shell, required, "SettingsScroll", "primaryContent");
            AddNamed(page, shell, required, "ApplicationScroll", "primaryContent");
            AddNamed(page, shell, required, "PrimaryAction", "primaryAction");
            AddNamed(page, shell, required, "ControllerHelp", "controllerHelp");
        }

        return new FrameSnapshot(
            route,
            DateTimeOffset.UtcNow,
            transitionDuration.TotalMilliseconds,
            IsTransparent(shell.Background),
            !IsOpaqueBlack(shell.Background) &&
            !IsOpaqueBlack(shell.ContentRegionControl.Background) &&
            !IsOpaqueBlack(shell.TransitionPresenterControl.Background),
            new DiagnosticRect(0, 0, shell.Bounds.Width, shell.Bounds.Height),
            RelativeBounds(shell.ContentRegionControl, shell),
            RelativeBounds(shell.TrayRegionControl, shell),
            required);
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Frames.Clear();
        }
    }

    private static void AddNamed(
        Control page,
        Control root,
        IDictionary<string, DiagnosticRect> required,
        string name,
        string key)
    {
        var control = page.FindControl<Control>(name);
        if (control is not null)
        {
            required[key] = RelativeBounds(control, root);
        }
    }

    private static DiagnosticRect RelativeBounds(Control control, Visual root)
    {
        var origin = control.TranslatePoint(new Point(0, 0), root) ?? default;
        return new DiagnosticRect(origin.X, origin.Y, control.Bounds.Width, control.Bounds.Height);
    }

    private static bool IsTransparent(IBrush? brush) =>
        brush is null || brush is ISolidColorBrush solid && solid.Color.A == 0;

    private static bool IsOpaqueBlack(IBrush? brush) =>
        brush is ISolidColorBrush solid &&
        solid.Color is { A: byte.MaxValue, R: 0, G: 0, B: 0 };
}
