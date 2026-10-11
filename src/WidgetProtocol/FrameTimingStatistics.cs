namespace WidgetRail.WidgetProtocol;

/// <summary>Statistics over individual present intervals for one swap chain, up to sixty seconds.</summary>
public sealed record FrameTimingStatistics(double AverageFramesPerSecond, double? OnePercentLowFramesPerSecond,
    double AverageFrameMilliseconds, double RecentWorstFrameMilliseconds, int SampleCount, double WindowSeconds)
{
    public bool IsWellFormed() => double.IsFinite(AverageFramesPerSecond) && AverageFramesPerSecond is > 0 and <= 10_000 &&
        (OnePercentLowFramesPerSecond is null || double.IsFinite(OnePercentLowFramesPerSecond.Value) &&
            OnePercentLowFramesPerSecond > 0 && OnePercentLowFramesPerSecond <= AverageFramesPerSecond + .0001 && SampleCount >= 1000 && WindowSeconds >= 5) &&
        double.IsFinite(AverageFrameMilliseconds) && AverageFrameMilliseconds is >= .1 and <= 10_000 &&
        double.IsFinite(RecentWorstFrameMilliseconds) && RecentWorstFrameMilliseconds is >= 0 and <= 10_000 &&
        SampleCount is > 0 and <= 65536 && double.IsFinite(WindowSeconds) && WindowSeconds is >= 0 and <= 60;
}
