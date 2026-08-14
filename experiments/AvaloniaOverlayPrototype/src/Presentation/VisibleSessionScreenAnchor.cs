using Avalonia;
using Avalonia.Platform;

namespace GameBarAlternative.AvaloniaPrototype.Presentation;

internal enum ScreenAnchorChangeReason
{
    InitialVisibleSession,
    DisplayTopologyChanged,
    DpiChanged,
    AnchoredScreenDisappeared,
}

internal readonly record struct ScreenAnchorDescriptor(
    string Identity,
    string? DisplayName,
    PixelRect Bounds,
    PixelRect WorkArea,
    double RenderScaling,
    bool IsPrimary)
{
    public static ScreenAnchorDescriptor FromScreen(Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var displayName = string.IsNullOrWhiteSpace(screen.DisplayName)
            ? null
            : screen.DisplayName.Trim();
        var bounds = screen.Bounds;
        return new ScreenAnchorDescriptor(
            $"{displayName ?? "unnamed"}|primary={screen.IsPrimary}|" +
            $"bounds={bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}",
            displayName,
            bounds,
            screen.WorkingArea,
            screen.Scaling > 0 ? screen.Scaling : 1,
            screen.IsPrimary);
    }
}

internal sealed record ScreenAnchorSnapshot(
    string Identity,
    string? DisplayName,
    PixelRect Bounds,
    PixelRect WorkArea,
    double RenderScaling,
    bool IsPrimary,
    long Revision,
    ScreenAnchorChangeReason ChangeReason)
{
    public WidgetEnvelopeConstraints ToEnvelopeConstraints() => new(
        WorkArea,
        RenderScaling,
        AccessibilityScale: 1,
        EnvelopeInsets.PlatformPlacement);
}

internal sealed class VisibleSessionScreenAnchor
{
    public ScreenAnchorSnapshot? Current { get; private set; }

    public ScreenAnchorSnapshot Initialize(ScreenAnchorDescriptor selected)
    {
        Current ??= CreateSnapshot(selected, 0, ScreenAnchorChangeReason.InitialVisibleSession);
        return Current;
    }

    public ScreenAnchorSnapshot RetainForPlacement(ScreenAnchorDescriptor? screenSelectedByWindowIntersection = null) =>
        Current ?? throw new InvalidOperationException("The visible-session screen anchor is not initialized.");

    public ScreenAnchorSnapshot ReconcileExplicitChange(
        IReadOnlyList<ScreenAnchorDescriptor> available,
        string? preferredIdentity,
        ScreenAnchorChangeReason observedReason)
    {
        if (Current is null) throw new InvalidOperationException("The visible-session screen anchor is not initialized.");
        if (observedReason is not ScreenAnchorChangeReason.DisplayTopologyChanged and
            not ScreenAnchorChangeReason.DpiChanged)
            throw new ArgumentOutOfRangeException(nameof(observedReason));

        var retained = FindRetainedScreen(available, Current);
        var changeReason = observedReason;
        if (retained is null)
        {
            retained = available.Where(screen =>
                    string.Equals(screen.Identity, preferredIdentity, StringComparison.Ordinal))
                .Select(screen => (ScreenAnchorDescriptor?)screen)
                .FirstOrDefault();
            retained ??= available.Where(screen => screen.IsPrimary)
                .Select(screen => (ScreenAnchorDescriptor?)screen)
                .FirstOrDefault();
            retained ??= available.Select(screen => (ScreenAnchorDescriptor?)screen).FirstOrDefault();
            if (retained is null)
                throw new InvalidOperationException("No screen remains available for the visible overlay session.");
            changeReason = ScreenAnchorChangeReason.AnchoredScreenDisappeared;
        }

        if (Matches(Current, retained.Value)) return Current;
        Current = CreateSnapshot(retained.Value, Current.Revision + 1, changeReason);
        return Current;
    }

    private static ScreenAnchorDescriptor? FindRetainedScreen(
        IReadOnlyList<ScreenAnchorDescriptor> available,
        ScreenAnchorSnapshot current)
    {
        foreach (var screen in available)
            if (string.Equals(screen.Identity, current.Identity, StringComparison.Ordinal)) return screen;

        if (!string.IsNullOrWhiteSpace(current.DisplayName))
        {
            var nameMatches = available.Where(screen =>
                    string.Equals(screen.DisplayName, current.DisplayName, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            if (nameMatches.Length == 1) return nameMatches[0];
        }

        var boundsMatches = available.Where(screen => screen.Bounds == current.Bounds).Take(2).ToArray();
        return boundsMatches.Length == 1 ? boundsMatches[0] : null;
    }

    private static bool Matches(ScreenAnchorSnapshot current, ScreenAnchorDescriptor candidate) =>
        string.Equals(current.Identity, candidate.Identity, StringComparison.Ordinal) &&
        string.Equals(current.DisplayName, candidate.DisplayName, StringComparison.Ordinal) &&
        current.Bounds == candidate.Bounds &&
        current.WorkArea == candidate.WorkArea &&
        Math.Abs(current.RenderScaling - candidate.RenderScaling) < 0.001 &&
        current.IsPrimary == candidate.IsPrimary;

    private static ScreenAnchorSnapshot CreateSnapshot(
        ScreenAnchorDescriptor descriptor,
        long revision,
        ScreenAnchorChangeReason reason) => new(
            descriptor.Identity,
            descriptor.DisplayName,
            descriptor.Bounds,
            descriptor.WorkArea,
            descriptor.RenderScaling,
            descriptor.IsPrimary,
            revision,
            reason);
}
