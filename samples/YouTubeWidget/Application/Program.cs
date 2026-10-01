using WidgetRail.Samples.YouTubeWidget;
using WidgetRail.WidgetRuntime;

return await WidgetApplicationBootstrap.RunAsync(args, () =>
    new YouTubeVideoWidget(YouTubeApplicationService.CreateDefault(),
        volumePreferencePath: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WidgetRail", "community-apps", "widgetrail.samples.youtube-video", "volume.txt")));
