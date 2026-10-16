using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetRuntime;
return await WidgetApplicationBootstrap.RunAsync(args, () => new PobWidget(1, AppContext.BaseDirectory,
    Environment.GetEnvironmentVariable("WIDGETRAIL_POB1_DATA") ?? Environment.GetEnvironmentVariable("WIDGETRAIL_POB_DATA")));
