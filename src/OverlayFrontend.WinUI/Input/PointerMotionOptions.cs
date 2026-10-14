namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Presentation tuning in DIPs; never affects pointer input or hit testing.</summary>
internal static class PointerMotionOptions
{
    internal static readonly TimeSpan JumpDuration = TimeSpan.FromMilliseconds(110);
    internal static readonly TimeSpan TrailLifetime = TimeSpan.FromMilliseconds(100);
    internal const int TrailCapacity = 7;
    internal const float TrailDiameter = 8;
    internal const float TrailMaximumLength = 32;
    internal const float TrailMaximumOpacity = .2f;
    internal const double TrailSampleMilliseconds = 12;
    internal const float TrailMinimumStep = 1.5f;
    internal const float TrailFullStrengthSpeed = 500;
}
