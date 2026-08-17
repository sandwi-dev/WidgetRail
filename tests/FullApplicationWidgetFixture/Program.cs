namespace WidgetRail.Tests.FullApplicationWidgetFixture;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (!args.Contains("--owned-helper", StringComparer.Ordinal))
            return;
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }
}
