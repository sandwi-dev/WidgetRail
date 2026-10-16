using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetRuntime;
return await WidgetApplicationBootstrap.RunAsync(args, () => new PobWidget(2, AppContext.BaseDirectory,
    Environment.GetEnvironmentVariable("WIDGETRAIL_POB2_DATA") ?? Environment.GetEnvironmentVariable("WIDGETRAIL_POB_DATA")));
