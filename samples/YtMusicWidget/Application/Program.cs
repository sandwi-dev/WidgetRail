using WidgetRail.Samples.YtMusicWidget.Standalone;
using WidgetRail.WidgetRuntime;

return await WidgetApplicationBootstrap.RunAsync(args,
    () => new StandaloneMusicWidget(new MusicService(AppContext.BaseDirectory,
        volumePreferencePath: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WidgetRail", "applications", "widgetrail.samples.ytmusic", "volume.txt"))));
