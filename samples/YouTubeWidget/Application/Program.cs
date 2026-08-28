using WidgetRail.Samples.YouTubeWidget;
using WidgetRail.WidgetRuntime;

return await WidgetApplicationBootstrap.RunAsync(args, () =>
    new YouTubeVideoWidget(YouTubeApplicationService.CreateDefault()));
