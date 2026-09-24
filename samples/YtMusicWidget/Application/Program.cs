using WidgetRail.Samples.YtMusicWidget.Standalone;
using WidgetRail.WidgetRuntime;

return await WidgetApplicationBootstrap.RunAsync(args,
    () => new StandaloneMusicWidget(new MusicService(AppContext.BaseDirectory)));
